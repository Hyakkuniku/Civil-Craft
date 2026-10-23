using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

static partial class Program
{
    static void AddExplicitTestPiers(List<PointRecord> points, List<BarRecord> bars,
        string topXs, string baseYText)
    {
        float baseY = float.Parse(baseYText, CultureInfo.InvariantCulture);
        float[] xs = topXs.Split(',').Select(value => float.Parse(value, CultureInfo.InvariantCulture)).ToArray();
        Require(float.IsFinite(baseY) && xs.Length > 0 && xs.All(float.IsFinite) && xs.Distinct().Count() == xs.Length,
            "Explicit test pier locations must be finite and distinct.");
        Console.WriteLine("SOURCE SAVE HAS NO PIERS: adding explicitly requested hypothetical test piers IN MEMORY ONLY. Source save is not restored or rewritten.");
        foreach (float x in xs)
        {
            PointRecord[] matches = points.Where(point => Math.Abs(point.X - x) < .001f && point.Y > baseY &&
                !point.OriginalIsAnchor && bars.Any(bar => bar.A == point.Id || bar.B == point.Id)).ToArray();
            Require(matches.Length == 1, "Each explicit X must identify exactly one non-authored shared bridge joint.");
            PointRecord top = matches[0];
            Require(bars.Any(bar => bar.Material == "Concrete Road" && (bar.A == top.Id || bar.B == top.Id)),
                "Explicit test pier top must be connected to the actual concrete roadway.");
            int footId = points.Max(point => point.Id) + 1;
            points.Add(new PointRecord { Id = footId, X = top.X, Y = baseY, Z = top.Z,
                IsAnchor = true, OriginalIsAnchor = false });
            bars.Add(new BarRecord { Index = bars.Count, A = footId, B = top.Id, Material = "Concrete Pier" });
            Console.WriteLine($"  TEST ONLY: Concrete Pier at shared node#{top.Id} X={top.X:F4}, topY={top.Y:F4}, baseY={baseY:F4}.");
        }
    }

    // Counterfactuals exist only in managed memory. Neither source coordinates
    // nor the current site's configured foundation height are written anywhere.
    static void RunTallSweep(List<PointRecord> sourcePoints, List<BarRecord> sourceBars,
        Dictionary<string, BridgeMaterialSO> materials, int samples, string contractId)
    {
        var piers = sourceBars.Where(bar => materials[bar.Material].isPier).ToList();
        Require(piers.Count > 0, "The selected bridge has no pier members.");
        var originalNodes = sourcePoints.ToDictionary(point => point.Id);
        var bases = new HashSet<int>();
        var originalLengths = new List<float>();
        foreach (BarRecord pier in piers)
        {
            PointRecord a = originalNodes[pier.A], b = originalNodes[pier.B];
            Require(Math.Abs(a.X - b.X) < .001f && Math.Abs(a.Z - b.Z) < .001f,
                "Tall-pier sweep requires vertical saved piers.");
            int lower = a.Y < b.Y ? a.Id : b.Id;
            int upper = a.Y < b.Y ? b.Id : a.Id;
            Require(a.Y != b.Y && !originalNodes[upper].OriginalIsAnchor,
                "A pier must have distinct heights and a top not independently fixed.");
            Require(!sourceBars.Any(bar => !materials[bar.Material].isPier && (bar.A == lower || bar.B == lower)),
                "Moving a foundation must not move a non-pier member.");
            Require(sourceBars.Any(bar => !materials[bar.Material].isPier && (bar.A == upper || bar.B == upper)),
                "A pier top must share a real node with the original bridge.");
            bases.Add(lower);
            originalLengths.Add(Length(a, b));
        }
        float tallestOriginal = originalLengths.Max();
        Graph original = BuildGraph(sourcePoints, sourceBars, materials, false, false);
        var originalResult = Analyze(original, 3000f, samples, false);
        Require(Same(originalResult, Analyze(original, 3000f, samples, false)),
            "The original-height reference bridge did not repeat exactly.");
        Console.WriteLine($"ORIGINAL-HEIGHT REFERENCE at 3000kg: tallest={tallestOriginal:F4}m; overall={originalResult.PeakStructuralStress * 100:F1}%; road/beam={ExistingPeak(originalResult) * 100:F1}%.");
        float[] heights = { 25f, 26f, 27f, 28f, 29f, 30f };
        float[] loads = { 3000f, 5000f, 10000f, 15000f, 20000f, 30000f, 40000f };
        Console.WriteLine($"TALL CURVED BRIDGE SWEEP: contract={contractId}; {heights.Length * loads.Length} height/load pairs; samples={samples}; TOTAL dead+live stress.");
        Console.WriteLine("Same saved road, beams, curvature, anchors, materials and connectivity in all comparisons. Only exclusive pier bases are lowered equally in memory; their height difference is retained. This is a hypothetical deeper foundation, NOT a changed/placeable current scene.");
        Console.WriteLine("A=current pier mass; B=omit only pier bars; C=same pier geometry/strength/stiffness with zero in-memory pier mass. Ratios round to 0.1 percentage point. Thresholds: 60% reference strength-star target, 100% material capacity.");
        Console.WriteLine("tallest_m,shortest_m,vehicle_kg,all_A_pct,pier_A_pct,road_beam_A_pct,no_piers_B_pct,massless_C_pct,stable_A,governing_A,governing_mode,governing_sample");
        int rows = 0;
        foreach (float height in heights)
        {
            float delta = height - tallestOriginal;
            var moved = sourcePoints.Select(point => new PointRecord { Id = point.Id,
                X = point.X, Y = point.Y - (bases.Contains(point.Id) ? delta : 0f), Z = point.Z,
                IsAnchor = point.IsAnchor, OriginalIsAnchor = point.OriginalIsAnchor }).ToList();
            var movedById = moved.ToDictionary(point => point.Id);
            foreach (PointRecord point in sourcePoints.Where(point => !bases.Contains(point.Id)))
                Require(point.X == movedById[point.Id].X && point.Y == movedById[point.Id].Y && point.Z == movedById[point.Id].Z,
                    "The sweep changed original non-foundation geometry.");
            foreach (BarRecord pier in piers)
                Require(Length(movedById[pier.A], movedById[pier.B]) <= materials[pier.Material].maxLength + .001f,
                    "Counterfactual pier exceeds its actual placement limit.");
            Graph actual = BuildGraph(moved, sourceBars, materials, false, false);
            Graph without = BuildGraph(moved, sourceBars, materials, true, false);
            Graph massless = BuildGraph(moved, sourceBars, materials, false, true);
            float shortest = piers.Min(pier => Length(movedById[pier.A], movedById[pier.B]));
            var heightRows = new List<(float Load, float Overall, float Pier)>();
            var dead = Analyze(actual, 0f, samples, false);
            Console.WriteLine($"HEIGHT {height:F1}m: shortest={shortest:F4}m; pier mass={actual.Bars.Where(bar => bar.materialData.isPier).Sum(bar => Length(bar.startPoint.transform.position, bar.endPoint.transform.position) * bar.materialData.GetPlacedMassPerMeter()):F1}kg; dead-only peak={dead.PeakStructuralStress * 100:F1}%; pier compression limits={string.Join("/", actual.Bars.Where(bar => bar.materialData.isPier).Select(bar => Limit(bar, false).ToString("F0", CultureInfo.InvariantCulture)))}N.");
            foreach (float load in loads)
            {
                var a = Analyze(actual, load, samples, false);
                var b = Analyze(without, load, samples, false);
                var c = Analyze(massless, load, samples, false);
                Require(a.IsStructurallyStable && b.IsStructurallyStable && c.IsStructurallyStable,
                    "A tested configuration contains a free structural mechanism; do not treat its load percentages as a valid balance comparison.");
                Require(Same(a, Analyze(actual, load, samples, false)) &&
                    Same(b, Analyze(without, load, samples, false)) &&
                    Same(c, Analyze(massless, load, samples, false)),
                    "A curved-bridge height/load counterfactual did not repeat exactly.");
                Require(a.Bars.Length == sourceBars.Count && b.Bars.Length == sourceBars.Count - piers.Count,
                    "A pier or road/beam disconnected during the sweep.");
                MemberPeak governing = Peaks(actual, a).First();
                float pierPeak = Peaks(actual, a).Where(peak => peak.Bar.materialData.isPier).Max(peak => peak.Ratio);
                heightRows.Add((load, a.PeakStructuralStress, pierPeak));
                Console.WriteLine(FormattableString.Invariant($"{height:F1},{shortest:F4},{load:F0},{a.PeakStructuralStress * 100:F1},{pierPeak * 100:F1},{ExistingPeak(a) * 100:F1},{b.PeakStructuralStress * 100:F1},{c.PeakStructuralStress * 100:F1},{a.IsStructurallyStable},{governing.Record.Material}#{governing.Record.Index},{Direction(governing.Tension)},{governing.Sample}/{samples-1}"));
                rows++;
            }
            string FirstOver(Func<(float Load, float Overall, float Pier), bool> predicate)
            {
                var match = heightRows.Where(predicate).ToArray();
                return match.Length == 0 ? "not reached through 40000kg" : match[0].Load.ToString("F0", CultureInfo.InvariantCulture) + "kg";
            }
            Console.WriteLine($"SAMPLED LIMITS {height:F1}m: first overall >60%={FirstOver(row => row.Overall > .6f)}; first overall >=100%={FirstOver(row => row.Overall >= 1f)}; first pier >=100%={FirstOver(row => row.Pier >= 1f)}. These are sampled loads, not interpolated safe limits.");
        }
        Require(rows == 42, "The requested six-height/seven-load matrix is incomplete.");
        Console.WriteLine("PASS: all 42 curved-bridge height/load pairs and all 126 A/B/C comparisons repeat sample-for-sample; existing road/beam geometry and source material values retained.");
        Console.WriteLine("LIMIT: an isolated saved-graph solver test, not native PhysX/device, terrain collision, cost/star affordability, or a real-world engineering validation. Above-capacity rows identify the calculated limit before runtime breakage/redistribution. No strength value was tuned to make a fixture pass.");
    }
}
