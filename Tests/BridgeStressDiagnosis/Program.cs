using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using UnityEngine;

// Read-only diagnostic replay of ONE explicitly selected saved bridge. The full
// player save is decrypted only in memory and is never printed or rewritten.
static partial class Program
{
    sealed class PointRecord
    {
        public int Id;
        public float X, Y, Z;
        public bool IsAnchor, OriginalIsAnchor;
    }
    sealed class BarRecord
    {
        public int Index, A, B;
        public string Material;
    }
    sealed class Graph
    {
        public readonly Dictionary<int, Point> PointsById = new();
        public readonly Dictionary<Bar, BarRecord> Records = new();
        public readonly List<Point> Points = new();
        public readonly List<Bar> Bars = new();
    }
    sealed class MemberPeak
    {
        public Bar Bar;
        public BarRecord Record;
        public int Sample;
        public float Ratio;
        public bool Tension;
    }

    static int Main(string[] args)
    {
        try { Run(args); return 0; }
        catch (Exception exception) { Console.Error.WriteLine("DIAGNOSIS STOPPED: " + exception.Message); return 1; }
    }

    static void Run(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i += 2)
        {
            Require(i + 1 < args.Length && args[i].StartsWith("--", StringComparison.Ordinal),
                "Use --save <absolute path> --contract <exact ID> --load <kg> --samples <count> [--sweep tall-heavy].");
            options.Add(args[i], args[i + 1]);
        }
        string Required(string name) => options.TryGetValue(name, out string value) && !string.IsNullOrWhiteSpace(value)
            ? value : throw new InvalidOperationException("Missing required option: " + name);
        string savePath = Required("--save");
        Require(Path.IsPathFullyQualified(savePath) && File.Exists(savePath), "The explicit save path must be an existing absolute file.");
        string contractId = Required("--contract");
        float loadKg = float.Parse(Required("--load"), CultureInfo.InvariantCulture);
        int samples = int.Parse(Required("--samples"), CultureInfo.InvariantCulture);
        Require(float.IsFinite(loadKg) && loadKg >= 0 && samples >= 3 && samples <= 201,
            "Load must be finite/nonnegative and sample count must be 3..201.");
        string root = FindRoot();
        var protectedHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [savePath] = Hash(savePath) };
        try
        {
            string purpose = GetPurpose(savePath);
            Require(SaveEncryption.TryDecryptOrReadLegacy(File.ReadAllText(savePath), purpose,
                out string json, out _, out string error), "Save decryption failed: " + error);
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement[] matches = document.RootElement.GetProperty("savedBridges").EnumerateArray()
                .Where(item => item.GetProperty("contractId").GetString() == contractId).ToArray();
            Require(matches.Length == 1, "The exact contract ID must identify one saved bridge, not " + matches.Length + ".");
            JsonElement bridge = matches[0];
            var pointRecords = bridge.GetProperty("points").EnumerateArray().Select(item => new PointRecord {
                Id = item.GetProperty("index").GetInt32(),
                X = item.GetProperty("position").GetProperty("x").GetSingle(),
                Y = item.GetProperty("position").GetProperty("y").GetSingle(),
                Z = item.GetProperty("position").GetProperty("z").GetSingle(),
                IsAnchor = item.GetProperty("isAnchor").GetBoolean(),
                OriginalIsAnchor = item.GetProperty("originalIsAnchor").GetBoolean()
            }).ToList();
            var barRecords = bridge.GetProperty("bars").EnumerateArray().Select((item, index) => new BarRecord {
                Index = index, A = item.GetProperty("startPointIndex").GetInt32(),
                B = item.GetProperty("endPointIndex").GetInt32(), Material = item.GetProperty("materialName").GetString()
            }).ToList();
            Require(pointRecords.Select(p => p.Id).Distinct().Count() == pointRecords.Count, "Duplicate saved node IDs.");
            var materials = new Dictionary<string, BridgeMaterialSO>(StringComparer.Ordinal);
            foreach (string materialName in barRecords.Select(b => b.Material).Distinct())
            {
                Require(!string.IsNullOrWhiteSpace(materialName) && Path.GetFileName(materialName) == materialName,
                    "Invalid material name in selected bridge.");
                string path = Path.Combine(root, "Assets/BridgeBuilder/Data/Resources", materialName + ".asset");
                Require(File.Exists(path), "No exact Resources asset for selected material: " + materialName);
                protectedHashes.Add(path, Hash(path));
                materials.Add(materialName, ReadMaterial(path, materialName));
            }

            if (options.TryGetValue("--sweep", out string sweep))
            {
                Require(sweep == "tall-heavy", "Unknown sweep; use tall-heavy.");
                if (options.TryGetValue("--test-pier-top-x", out string topPositions))
                {
                    Require(!barRecords.Any(bar => materials[bar.Material].isPier),
                        "Explicit test-pier addition is only for a saved bridge without any piers.");
                    string pierPath = Path.Combine(root, "Assets/BridgeBuilder/Data/Resources/Concrete Pier.asset");
                    Require(File.Exists(pierPath), "The current Concrete Pier material is missing.");
                    protectedHashes.Add(pierPath, Hash(pierPath));
                    var pierMaterial = ReadMaterial(pierPath, "Concrete Pier");
                    Require(pierMaterial.isPier && !pierMaterial.isRoad && !pierMaterial.isDualBeam,
                        "The explicit test material must retain the current single-pier behavior.");
                    materials.Add("Concrete Pier", pierMaterial);
                    AddExplicitTestPiers(pointRecords, barRecords, topPositions, Required("--test-pier-base-y"));
                }
                RunTallSweep(pointRecords, barRecords, materials, samples, contractId);
                return;
            }

            Console.WriteLine($"Selected contract={contractId}; saved points={pointRecords.Count}; bars={barRecords.Count}; saved cost={Number(bridge, "totalSpent"):F4}; saved peak={Number(bridge, "maxStress"):F4}%.");
            Console.WriteLine($"Replay uses CURRENT asset properties, saved world coordinates, load={loadKg:F3} kg, samples={samples}, TOTAL dead+live display. Source bar indices below are zero-based save-list indices.");
            foreach (var group in barRecords.GroupBy(b => b.Material))
            {
                var material = materials[group.Key];
                float totalLength = group.Sum(b => Length(pointRecords.Single(p => p.Id == b.A), pointRecords.Single(p => p.Id == b.B)));
                Console.WriteLine($"Material {group.Key}: {group.Count()} bars; length={totalLength:F4} m; placed mass/m={material.GetPlacedMassPerMeter():F3}; total mass={totalLength * material.GetPlacedMassPerMeter():F3} kg; T={material.maxTension:F1}; C={material.maxCompression:F1}; dual={material.isDualBeam}; pier={material.isPier}.");
            }
            Graph actual = BuildGraph(pointRecords, barRecords, materials, false, false);
            Graph without = BuildGraph(pointRecords, barRecords, materials, true, false);
            Graph masslessPiers = BuildGraph(pointRecords, barRecords, materials, false, true);
            var a = Analyze(actual, loadKg, samples, false);
            var b = Analyze(without, loadKg, samples, false);
            var c = Analyze(masslessPiers, loadKg, samples, false);
            var liveOnly = Analyze(actual, loadKg, samples, true);
            Require(Same(a, Analyze(actual, loadKg, samples, false)), "Repeated exact-bridge analysis changed any sample.");
            Require(Same(b, Analyze(without, loadKg, samples, false)), "Repeated no-pier analysis changed any sample.");
            Require(Same(c, Analyze(masslessPiers, loadKg, samples, false)), "Repeated massless-pier analysis changed any sample.");
            PrintScenario("A ACTUAL SAVED BRIDGE", actual, a, samples);
            PrintScenario("B OMIT ONLY PIER BARS", without, b, samples);
            PrintScenario("C SAME SUPPORTS/STIFFNESS, PIER MASS=0 IN MEMORY", masslessPiers, c, samples);
            Console.WriteLine("DECOMPOSITION (maxima can move between different members/samples; these are not sums of forces):");
            Console.WriteLine($"All-member peak: A={a.PeakDisplayedStress:P3}; B={b.PeakDisplayedStress:P3}; C={c.PeakDisplayedStress:P3}.");
            Console.WriteLine($"Existing road+beam peak: A={ExistingPeak(a):P3}; B={ExistingPeak(b):P3}; C={ExistingPeak(c):P3}.");
            Console.WriteLine("MATCHED GOVERNING ORIGINAL MEMBERS (each scenario's own peak sample):");
            var actualPeaks = Peaks(actual, a);
            var withoutPeaks = Peaks(without, b);
            foreach (int index in actualPeaks.Take(3).Concat(withoutPeaks.Take(3)).Select(p => p.Record.Index).Distinct())
            {
                MemberPeak ap = actualPeaks.Single(p => p.Record.Index == index);
                MemberPeak bp = withoutPeaks.SingleOrDefault(p => p.Record.Index == index);
                Console.WriteLine($"  bar#{index} {ap.Record.Material}: A {PeakLabel(ap)}; B {(bp == null ? "removed pier" : PeakLabel(bp))}.");
            }
            Console.WriteLine("PIER JOINTS / LOAD TRANSFER:");
            foreach (Bar pier in actual.Bars.Where(bar => bar.materialData.isPier))
            {
                BarRecord record = actual.Records[pier];
                bool aLower = pier.startPoint.transform.position.y < pier.endPoint.transform.position.y;
                Point lower = aLower ? pier.startPoint : pier.endPoint;
                Point upper = aLower ? pier.endPoint : pier.startPoint;
                int lowerId = aLower ? record.A : record.B, upperId = aLower ? record.B : record.A;
                Bar[] topConnections = actual.Bars.Where(bar => bar != pier && (bar.startPoint == upper || bar.endPoint == upper)).ToArray();
                MemberPeak totalPeak = Peaks(actual, a).Single(p => p.Bar == pier);
                MemberPeak livePeak = Peaks(actual, liveOnly).Single(p => p.Bar == pier);
                a.TryGetDeadLoadStress(pier, out float dead, out _, out bool deadTension);
                Console.WriteLine($"bar#{record.Index} lower node#{lowerId} y={lower.transform.position.y:F4} -> upper node#{upperId} y={upper.transform.position.y:F4}; lower inferred foundation={lower.transform.position.y != upper.transform.position.y}; upper original anchor={upper.originalIsAnchor}; solver road-component included={Array.IndexOf(a.Bars, pier) >= 0}; shared-top connections={string.Join(", ", topConnections.Select(bar => "#" + actual.Records[bar].Index + " " + actual.Records[bar].Material))}.");
                Console.WriteLine($"  total peak={totalPeak.Ratio:P3} ({Direction(totalPeak.Tension)}); dead-only={dead:P3} ({Direction(deadTension)}); pure-live peak={livePeak.Ratio:P3} ({Direction(livePeak.Tension)}), approximate peak axial live change={livePeak.Ratio * Limit(pier, livePeak.Tension):F1} N at sample {livePeak.Sample}/{samples-1}.");
            }
            Console.WriteLine("PASS: all three scenarios are sample-for-sample deterministic on repeat.");
            Console.WriteLine("LIMIT: saved successful design is replayed, not a native/runtime capture. Route maxima assume complete crossing at full nominal live load; runtime displayed peaks only include visited samples, partial vehicle entry, and any deformation/break state. No terrain collider queries are performed by this solver replay.");
        }
        finally
        {
            foreach (var entry in protectedHashes)
                Require(Hash(entry.Key) == entry.Value, "Protected source file changed during diagnosis: " + entry.Key);
            Console.WriteLine($"PASS: selected save and {protectedHashes.Count - 1} material asset SHA-256 hashes unchanged; no player data or assets written.");
        }
    }

    static Graph BuildGraph(List<PointRecord> points, List<BarRecord> bars,
        Dictionary<string, BridgeMaterialSO> materials, bool omitPiers, bool masslessPiers)
    {
        Graph graph = new();
        foreach (PointRecord record in points)
        {
            Point point = new() { transform = new Transform { position = new Vector3(record.X, record.Y, record.Z) },
                preSimPos = new Vector3(record.X, record.Y, record.Z), isAnchor = record.IsAnchor,
                originalIsAnchor = record.OriginalIsAnchor };
            graph.PointsById.Add(record.Id, point);
            graph.Points.Add(point);
        }
        foreach (BarRecord record in bars)
        {
            BridgeMaterialSO material = materials[record.Material];
            if (omitPiers && material.isPier) continue;
            if (masslessPiers && material.isPier) material = CloneMassless(material);
            Require(graph.PointsById.ContainsKey(record.A) && graph.PointsById.ContainsKey(record.B), "Saved bar has a missing endpoint.");
            Bar bar = new() { startPoint = graph.PointsById[record.A], endPoint = graph.PointsById[record.B], materialData = material };
            graph.Bars.Add(bar);
            graph.Records.Add(bar, record);
        }
        return graph;
    }

    static BridgeMaterialSO CloneMassless(BridgeMaterialSO source) => new() {
        name = source.name, isPier = source.isPier, isRoad = source.isRoad, isRope = source.isRope,
        isDualBeam = source.isDualBeam, maxTension = source.maxTension, maxCompression = source.maxCompression,
        massPerMeter = 0f, maxLength = source.maxLength, costPerMeter = source.costPerMeter,
        pierBucklingReferenceLength = source.pierBucklingReferenceLength,
        pierBucklingExponent = source.pierBucklingExponent,
        pierMinimumCompressionMultiplier = source.pierMinimumCompressionMultiplier
    };

    static DeterministicBridgeStressSolver.Result Analyze(Graph graph, float load, int samples, bool liveOnly)
    {
        var result = DeterministicBridgeStressSolver.Analyze(graph.Points, graph.Bars, load, liveOnly, samples);
        Require(result != null && result.IsValid, "Selected bridge did not produce a valid deterministic result.");
        return result;
    }

    static List<MemberPeak> Peaks(Graph graph, DeterministicBridgeStressSolver.Result result)
    {
        var peaks = new List<MemberPeak>();
        for (int barIndex = 0; barIndex < result.Bars.Length; barIndex++)
        {
            Bar bar = result.Bars[barIndex];
            MemberPeak peak = new() { Bar = bar, Record = graph.Records[bar], Ratio = -1 };
            for (int sample = 0; sample < result.Samples.Length; sample++)
                if (result.Samples[sample].DisplayedRatios[barIndex] > peak.Ratio)
                {
                    peak.Ratio = result.Samples[sample].DisplayedRatios[barIndex];
                    peak.Tension = result.Samples[sample].IsTension[barIndex];
                    peak.Sample = sample;
                }
            peaks.Add(peak);
        }
        return peaks.OrderByDescending(peak => peak.Ratio).ThenBy(peak => peak.Record.Index).ToList();
    }

    static void PrintScenario(string title, Graph graph, DeterministicBridgeStressSolver.Result result, int samples)
    {
        var peaks = Peaks(graph, result);
        float deadPeak = result.DeadLoadSample.DisplayedRatios.Max();
        Console.WriteLine(title);
        Console.WriteLine($"  included members={result.Bars.Length}; structurally stable={result.IsStructurallyStable}; displayed peak={result.PeakDisplayedStress:P3}; structural peak={result.PeakStructuralStress:P3}; dead-only peak={deadPeak:P3}.");
        foreach (MemberPeak peak in peaks.Take(10))
        {
            Vector3 a = peak.Bar.startPoint.transform.position, b = peak.Bar.endPoint.transform.position;
            result.TryGetDeadLoadStress(peak.Bar, out float dead, out _, out bool deadTension);
            result.TryGetStress(peak.Bar, peak.Sample, out _, out float structural, out _);
            Console.WriteLine($"  bar#{peak.Record.Index} {peak.Record.Material} node#{peak.Record.A}({a.x:F4},{a.y:F4})->node#{peak.Record.B}({b.x:F4},{b.y:F4}); length={Length(a,b):F4} m; peak={peak.Ratio:P3}; structural={structural:P3}; {Direction(peak.Tension)} limit={Limit(peak.Bar,peak.Tension):F1} N; approx axial={peak.Ratio * Limit(peak.Bar,peak.Tension):F1} N; sample={peak.Sample}/{samples-1} progress={(float)peak.Sample/(samples-1):P2}; dead={dead:P3} {Direction(deadTension)}.");
        }
    }

    static float ExistingPeak(DeterministicBridgeStressSolver.Result result)
    {
        float peak = 0f;
        for (int member = 0; member < result.Bars.Length; member++)
            if (!result.Bars[member].materialData.isPier)
                foreach (var sample in result.Samples) peak = Math.Max(peak, sample.DisplayedRatios[member]);
        return peak;
    }
    static bool Same(DeterministicBridgeStressSolver.Result a, DeterministicBridgeStressSolver.Result b)
    {
        if (a.Bars.Length != b.Bars.Length || a.Samples.Length != b.Samples.Length) return false;
        for (int sample = 0; sample < a.Samples.Length; sample++)
            for (int member = 0; member < a.Bars.Length; member++)
                if (a.Samples[sample].DisplayedRatios[member] != b.Samples[sample].DisplayedRatios[member] ||
                    a.Samples[sample].StructuralRatios[member] != b.Samples[sample].StructuralRatios[member] ||
                    a.Samples[sample].IsTension[member] != b.Samples[sample].IsTension[member]) return false;
        return true;
    }
    static BridgeMaterialSO ReadMaterial(string path, string name)
    {
        string source = File.ReadAllText(path);
        float Number(string field)
        {
            Match match = Regex.Match(source, "(?m)^  " + Regex.Escape(field) + @":\s*([^\r\n]+)");
            Require(match.Success, "Missing material field: " + field + " in " + name);
            return float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        }
        return new BridgeMaterialSO { name = name, isPier = Number("isPier") != 0, isRoad = Number("isRoad") != 0,
            isRope = Number("isRope") != 0, isDualBeam = Number("isDualBeam") != 0,
            maxTension = Number("maxTension"), maxCompression = Number("maxCompression"),
            massPerMeter = Number("massPerMeter"), costPerMeter = Number("costPerMeter"), maxLength = Number("maxLength"),
            pierBucklingReferenceLength = Number("pierBucklingReferenceLength"), pierBucklingExponent = Number("pierBucklingExponent"),
            pierMinimumCompressionMultiplier = Number("pierMinimumCompressionMultiplier") };
    }
    static string PeakLabel(MemberPeak peak) => $"{peak.Ratio:P3} {Direction(peak.Tension)}, limit={Limit(peak.Bar,peak.Tension):F1}N, approx force={peak.Ratio * Limit(peak.Bar,peak.Tension):F1}N, sample={peak.Sample}";
    static float Limit(Bar bar, bool tension)
    {
        // Use the production solver's own quantization/square-root helpers so
        // capacity diagnostics do not silently use unquantized saved lengths.
        Type solver = typeof(DeterministicBridgeStressSolver);
        object Call(string method, params object[] values) => solver.GetMethod(method,
            BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, values);
        Vector3 a = bar.startPoint.transform.position, b = bar.endPoint.transform.position;
        decimal dx = (decimal)Call("QuantizeMetres", b.x) - (decimal)Call("QuantizeMetres", a.x);
        decimal dy = (decimal)Call("QuantizeMetres", b.y) - (decimal)Call("QuantizeMetres", a.y);
        float length = (float)(decimal)Call("DecimalSqrt", dx*dx+dy*dy);
        float capacity = tension ? bar.materialData.maxTension : bar.materialData.GetCompressionLimit(length);
        return (float)(decimal)Call("QuantizeNewtons", capacity) * (bar.materialData.isDualBeam ? 2f : 1f);
    }
    static float Length(PointRecord a, PointRecord b) => Length(new Vector3(a.X,a.Y,a.Z),new Vector3(b.X,b.Y,b.Z));
    // The production solver is explicitly 2D; saved z must not inflate its member length.
    static float Length(Vector3 a, Vector3 b) => MathF.Sqrt((b.x-a.x)*(b.x-a.x)+(b.y-a.y)*(b.y-a.y));
    static float Number(JsonElement item, string name) => item.GetProperty(name).GetSingle();
    static string Direction(bool tension) => tension ? "TENSION" : "COMPRESSION";
    static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    static string GetPurpose(string path)
    {
        string directory = Path.GetDirectoryName(path), parent = Path.GetDirectoryName(directory);
        return string.Equals(Path.GetFileName(parent), "Accounts", StringComparison.OrdinalIgnoreCase)
            ? "local-account:" + Path.GetFileName(directory) : "local-guest";
    }
    static string FindRoot()
    {
        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Assets/Script/BuildMode/2D/DeterministicBridgeStressSolver.cs"))) return directory.FullName;
        throw new InvalidOperationException("Run from the Civil-Craft project root.");
    }
    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
