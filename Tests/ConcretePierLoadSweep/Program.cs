using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnityEngine;

// Isolated gameplay capacity sweep. Uses the production material/solver sources
// with engine boundary doubles, and never mutates a live asset or player save.
static class Program
{
    private const string AssetPath = "Assets/BridgeBuilder/Data/Resources/Concrete Pier.asset";
    private static readonly float[] Heights = { 10f, 20f, 25f, 26f, 27f, 28f, 29f, 30f };
    private static readonly float[] Loads = { 3000f, 5000f, 10000f, 15000f, 20000f, 30000f, 40000f };
    private const int Samples = 11;
    private sealed class Fixture
    {
        public List<Point> Points;
        public List<Bar> Bars;
        public Bar Pier;
    }
    private sealed class Reading
    {
        public float PierPeak, DeadOnly, Overall;
        public bool Stable;
        public DeterministicBridgeStressSolver.Result Result;
    }

    static int Main()
    {
        try { Run(); return 0; }
        catch (Exception exception) { Console.Error.WriteLine("SWEEP FAILED: " + exception.Message); return 1; }
    }

    static void Run()
    {
        string root = FindRoot();
        string path = Path.Combine(root, AssetPath);
        string originalHash = Hash(path);
        try
        {
            BridgeMaterialSO material = ReadMaterial(path);
            Require(material.isPier && !material.isDualBeam && material.maxTension == 400000f &&
                material.maxCompression == 2100000f && material.massPerMeter == 240f &&
                material.costPerMeter == 900f && material.maxLength == 30f &&
                material.pierBucklingReferenceLength == 10f && material.pierBucklingExponent == 2f &&
                material.pierMinimumCompressionMultiplier == .15f,
                "The actual asset must retain the approved 400k/2.1M strength, 240 kg/m, 900 cost/m, 30m and 10/2/.15 buckling preset.");
            Require(material.GetPlacedMassPerMeter() == 240f, "Single-pier placed mass must not double.");
            Console.WriteLine("Actual Concrete Pier preset unchanged: T400000N / C2100000N; single240kg/m; cost900/m; max30m; buckling10m/exponent2/floor0.15.");
            Console.WriteLine("ISOLATED CENTER-PIER FIXTURE, NOT A COMPLETE LEVEL/BRIDGE: two 5m massless non-limiting deck bars, fixed banks(-5,0)/(5,0), free shared joint(0,0), pier base(0,-height); actual production solver's inferred lower foundation. 11 route samples, full nominal moving point load, total dead+live utilization.");
            Console.WriteLine("height_m,vehicle_kg,pier_compression_limit_N,pier_peak_pct,dead_only_pct,overall_peak_pct,stiffness_stable,at_or_above_60pct,at_or_above_100pct");
            var byLoad = Loads.ToDictionary(load => load, _ => -1f);
            int cases = 0;
            foreach (float height in Heights)
            {
                Fixture fixture = BuildFixture(material, height);
                foreach (float load in Loads)
                {
                    Reading reading = Analyze(fixture, load);
                    Require(Same(reading.Result, Analyze(fixture, load).Result),
                        $"Repeat changed samples at height={height}, load={load}.");
                    Require(reading.Stable && reading.PierPeak > 0f && reading.Overall >= reading.PierPeak &&
                        reading.PierPeak >= byLoad[load], "The fixture must remain stable with finite, nondecreasing tall-pier utilization.");
                    byLoad[load] = reading.PierPeak;
                    Console.WriteLine(FormattableString.Invariant($"{height:F0},{load:F0},{material.GetCompressionLimit(height):F1},{reading.PierPeak*100f:F1},{reading.DeadOnly*100f:F1},{reading.Overall*100f:F1},{reading.Stable},{reading.PierPeak>=.6f},{reading.PierPeak>=1f}"));
                    cases++;
                }
            }
            Console.WriteLine("ROUNDED-SOLVER THRESHOLD BRACKETS (vehicle kg, not total material+vehicle mass; <=1kg bracket):");
            Console.WriteLine("height_m,first_60pct_vehicle_kg_bracket,first_100pct_vehicle_kg_bracket");
            foreach (float height in Heights)
            {
                Fixture fixture = BuildFixture(material, height);
                var target = FindThreshold(fixture, .6f);
                var failure = FindThreshold(fixture, 1f);
                Console.WriteLine(FormattableString.Invariant($"{height:F0},[{target.low:F2};{target.high:F2}],[{failure.low:F2};{failure.high:F2}]"));
            }
            Console.WriteLine($"PASS: {cases} matrix cases repeat sample-for-sample; actual single-pier mass, capacity preset and height weakening retained; 60%/100% thresholds bracketed through real solver calls.");
            Console.WriteLine("LIMIT: these percentages are rounded game-model axial utilization, not real-world load ratings. Stable means matrix rank, not vehicle-crossing success. The massless deck excludes real deck/truss self-weight, sharing and bending/PhysX behavior; level balance requires the real saved-bridge/playtest comparison. No strength or other balance values were changed.");
        }
        finally
        {
            Require(Hash(path) == originalHash, "Concrete Pier asset changed during the read-only sweep.");
            Console.WriteLine("PASS: full Concrete Pier asset SHA-256 unchanged; no live scene, asset, player-data or network writes.");
        }
    }

    static Fixture BuildFixture(BridgeMaterialSO material, float height)
    {
        Point Node(float x, float y, bool bank = false) => new() {
            transform = new Transform { position = new Vector3(x,y,0f) },
            originalIsAnchor = bank, isAnchor = bank
        };
        Point left = Node(-5f,0f,true), top = Node(0f,0f), right = Node(5f,0f,true), foot = Node(0f,-height);
        top.isAnchor = foot.isAnchor = true; // Same runtime flags as the placement path; not authored anchors.
        var pier = new Bar { startPoint = foot, endPoint = top, materialData = material };
        var deck = new BridgeMaterialSO { name = "Isolated massless non-limiting fixture deck", isRoad = true,
            massPerMeter = 0f, maxTension = 1000000000f, maxCompression = 1000000000f };
        return new Fixture {
            Points = new List<Point> { left,top,right,foot }, Pier = pier,
            Bars = new List<Bar> {
                new Bar { startPoint = left, endPoint = top, materialData = deck },
                new Bar { startPoint = top, endPoint = right, materialData = deck }, pier
            }
        };
    }

    static Reading Analyze(Fixture fixture, float load)
    {
        var result = DeterministicBridgeStressSolver.Analyze(fixture.Points,fixture.Bars,load,false,Samples);
        Require(result != null && result.IsValid, "The production solver did not produce a valid fixture result.");
        float peak = 0f;
        for (int sample = 0; sample < result.Samples.Length; sample++)
        {
            Require(result.TryGetStress(fixture.Pier,sample,out float displayed,out float structural,out bool tension), "Pier was excluded from actual solver output.");
            Require(!tension && float.IsFinite(displayed) && displayed == structural, "Total-load fixture must report finite compression consistently.");
            peak = Math.Max(peak,displayed);
        }
        Require(result.TryGetDeadLoadStress(fixture.Pier,out float dead,out _,out bool deadTension) && !deadTension,
            "The actual 240 kg/m pier must retain its self-weight compression sample.");
        return new Reading { PierPeak=peak,DeadOnly=dead,Overall=result.PeakDisplayedStress,
            Stable=result.IsStructurallyStable,Result=result };
    }

    static (float low,float high) FindThreshold(Fixture fixture, float target)
    {
        float low = 0f, high = 1000f;
        Require(Analyze(fixture,low).PierPeak < target, "Zero-load self-weight already reaches requested threshold.");
        while (Analyze(fixture,high).PierPeak < target)
        {
            low = high; high *= 2f;
            Require(high <= 1000000f, "Threshold exceeds diagnostic search cap.");
        }
        while (high-low > 1f)
        {
            float middle = (low+high)/2f;
            if (Analyze(fixture,middle).PierPeak >= target) high = middle; else low = middle;
        }
        Require(Analyze(fixture,low).PierPeak < target && Analyze(fixture,high).PierPeak >= target,
            "The reported threshold bracket does not straddle the actual rounded result.");
        return (low,high);
    }

    static bool Same(DeterministicBridgeStressSolver.Result a, DeterministicBridgeStressSolver.Result b)
    {
        if (a.Bars.Length != b.Bars.Length || a.Samples.Length != b.Samples.Length ||
            a.IsStructurallyStable != b.IsStructurallyStable) return false;
        for (int sample=0;sample<a.Samples.Length;sample++)
            for (int member=0;member<a.Bars.Length;member++)
                if (a.Samples[sample].DisplayedRatios[member] != b.Samples[sample].DisplayedRatios[member] ||
                    a.Samples[sample].StructuralRatios[member] != b.Samples[sample].StructuralRatios[member] ||
                    a.Samples[sample].IsTension[member] != b.Samples[sample].IsTension[member]) return false;
        return true;
    }

    static BridgeMaterialSO ReadMaterial(string path)
    {
        string source = File.ReadAllText(path);
        float Number(string field)
        {
            Match match = Regex.Match(source,"(?m)^  "+Regex.Escape(field)+@":\s*([^\r\n]+)");
            Require(match.Success,"Missing current material field: "+field);
            return float.Parse(match.Groups[1].Value,CultureInfo.InvariantCulture);
        }
        return new BridgeMaterialSO { name="Concrete Pier",isPier=Number("isPier")!=0,isDualBeam=Number("isDualBeam")!=0,
            maxTension=Number("maxTension"),maxCompression=Number("maxCompression"),massPerMeter=Number("massPerMeter"),
            costPerMeter=Number("costPerMeter"),maxLength=Number("maxLength"),
            pierBucklingReferenceLength=Number("pierBucklingReferenceLength"),pierBucklingExponent=Number("pierBucklingExponent"),
            pierMinimumCompressionMultiplier=Number("pierMinimumCompressionMultiplier") };
    }
    static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    static string FindRoot()
    {
        for (var directory=new DirectoryInfo(Directory.GetCurrentDirectory());directory!=null;directory=directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName,AssetPath))) return directory.FullName;
        throw new InvalidOperationException("Run from the Civil-Craft Unity project root.");
    }
    static void Require(bool condition,string message) { if(!condition) throw new InvalidOperationException(message); }
}
