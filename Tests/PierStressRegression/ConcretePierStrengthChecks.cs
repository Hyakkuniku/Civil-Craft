using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Task-specific gameplay preset checks. The material and deterministic solver
/// are the actual production sources compiled by this project; only Unity's
/// object/attribute boundary is doubled. No live scene or player data is used.
/// </summary>
static class ConcretePierStrengthChecks
{
    private const string MaterialPath = "Assets/BridgeBuilder/Data/Resources/Concrete Pier.asset";

    public static void Run()
    {
        string root = FindRoot();
        BridgeMaterialSO material = ReadMaterial(Path.Combine(root, MaterialPath));
        BridgeMaterialSO beam = ReadMaterial(Path.Combine(root, "Assets/BridgeBuilder/Data/Resources/ConcreteBeam.asset"));
        BridgeMaterialSO road = ReadMaterial(Path.Combine(root, "Assets/BridgeBuilder/Data/Resources/Concrete Road.asset"));
        BridgeMaterialSO woodPier = ReadMaterial(Path.Combine(root, "Assets/BridgeBuilder/Data/Resources/Pier.asset"));
        Check(material.isPier && !material.isDualBeam, "Concrete Pier must retain the single-pier solver path.");
        Check(material.maxTension == 400000f && material.maxCompression == 2100000f,
            "Concrete Pier must use the approved 400,000 N tension / 2,100,000 N compression gameplay preset.");
        Check(material.maxTension == beam.maxTension && material.maxCompression == beam.maxCompression,
            "The approved preset must match the existing Concrete Beam's per-member limits, not invent a certified concrete rating.");
        Check(material.maxLength == 30f, "The user's existing 30 m placement limit must be retained.");
        Check(material.pierBucklingReferenceLength == 10f && material.pierBucklingExponent == 2f &&
            material.pierMinimumCompressionMultiplier == .15f,
            "The strength edit must retain the existing height-dependent buckling settings.");
        Check(Near(material.GetCompressionLimit(10f), 2100000f) &&
            Near(material.GetCompressionLimit(20f), 525000f) &&
            Near(material.GetCompressionLimit(30f), 315000f),
            "The real material method must retain 2,100,000 / 525,000 / 315,000 N compression limits at 10 / 20 / 30 m.");
        Console.WriteLine("PASS: actual Concrete Pier asset uses the approved strength preset and retained 10/20/30 m buckling limits.");

        Check(material.costPerMeter == 900f && material.massPerMeter == 240f &&
            Near(material.GetPlacedMassPerMeter(), 240f),
            "Concrete Pier must use the approved 900 cost / 240 kg per metre preset without dual-beam mass doubling.");
        Check(beam.isDualBeam && beam.costPerMeter == 600f && Near(beam.GetPlacedMassPerMeter(), 180f) &&
            road.costPerMeter == 650f && Near(road.GetPlacedMassPerMeter(), 180f) &&
            woodPier.costPerMeter == 500f && Near(woodPier.GetPlacedMassPerMeter(), 182.5f),
            "Existing Concrete Beam, Concrete Road and Wood Pier balance must remain unchanged.");
        // Bar.GetCost applies this same explicit per-member placement multiplier.
        // This assertion checks the data balance, not a mock purchase/billing path.
        float beamPlacedCostPerMeter = beam.costPerMeter * (beam.isDualBeam ? 2f : 1f);
        Check(beamPlacedCostPerMeter == 1200f && material.costPerMeter < beamPlacedCostPerMeter &&
            material.costPerMeter > road.costPerMeter && material.costPerMeter > woodPier.costPerMeter &&
            material.GetPlacedMassPerMeter() > beam.GetPlacedMassPerMeter() &&
            material.GetPlacedMassPerMeter() > road.GetPlacedMassPerMeter() &&
            material.GetPlacedMassPerMeter() > woodPier.GetPlacedMassPerMeter(),
            "Concrete Pier must be heavier than existing materials, cost more than road/wood pier, and cost less than a paired Concrete Beam.");
        Console.WriteLine("PASS: actual assets retain the approved 900/240 single-pier balance and unchanged comparison material values.");

        float ratio10 = CenterCompressionRatio(material, 10f, 3000f);
        float ratio20 = CenterCompressionRatio(material, 20f, 3000f);
        float ratio30 = CenterCompressionRatio(material, 30f, 3000f);
        Check(ratio10 > 0f && ratio10 < ratio20 && ratio20 < ratio30 && ratio30 < 1f,
            "A 3,000 kg gameplay test load must be carried by the actual pier, with greater utilization as it gets taller.");
        Console.WriteLine("PASS: actual-asset piers carry the 3,000 kg fixture load while taller columns retain higher stress.");

        BridgeMaterialSO massless = ReadMaterial(Path.Combine(root, MaterialPath));
        massless.massPerMeter = 0f; // In-memory counterfactual only; all strength/stiffness parameters are unchanged.
        float masslessRatio = CenterCompressionRatio(massless, 30f, 3000f);
        Check(massless.maxTension == material.maxTension && massless.maxCompression == material.maxCompression &&
            Near(massless.GetCompressionLimit(30f), material.GetCompressionLimit(30f)) &&
            masslessRatio > 0f && masslessRatio < ratio30 && ratio30 < 1f,
            "The real solver must include the heavier pier's self-weight while retaining the supported 3,000 kg fixture's safety margin.");
        Console.WriteLine($"PASS: actual 240 kg/m pier self-weight increases 30 m compression utilization ({masslessRatio:F4} -> {ratio30:F4}) without failing the supported fixture.");

        float shortHeavyRatio = CenterCompressionRatio(material, 10f, 40000f);
        float tallHeavyRatio = CenterCompressionRatio(material, 30f, 40000f);
        Check(shortHeavyRatio < 1f && tallHeavyRatio > 1f,
            "A deliberately excessive 40,000 kg fixture must still overload the tall pier; increased strength must not disable buckling/failure.");
        Console.WriteLine("PASS: the real solver still detects deliberate tall-pier compression overload with the stronger material.");

        BridgeMaterialSO placeholder = ReadMaterial(Path.Combine(root, MaterialPath));
        placeholder.maxTension = placeholder.maxCompression = 3000f;
        Check(CenterCompressionRatio(placeholder, 30f, 3000f) > 1f,
            "The old placeholder limits must remain distinguishable from the approved gameplay preset.");
        Console.WriteLine("PASS: the old 3,000 N placeholder fails the same fixture, confirming the strength change affects the real solver.");
    }

    private static float CenterCompressionRatio(BridgeMaterialSO material, float height, float loadKg)
    {
        Point Node(float x, float y, bool bank = false) => new Point {
            transform = new Transform { position = new Vector3(x, y, 0f) },
            originalIsAnchor = bank, isAnchor = bank
        };
        var left = Node(-5f, 0f, true);
        var top = Node(0f, 0f);
        var right = Node(5f, 0f, true);
        var foot = Node(0f, -height);
        top.isAnchor = foot.isAnchor = true; // Runtime pier flags, not scene-fixed supports.
        var pier = new Bar { startPoint = foot, endPoint = top, materialData = material };
        // A deliberately non-limiting, massless deck isolates the asset's actual
        // pier capacity/self-weight and avoids silently changing real road SOs.
        var deck = new BridgeMaterialSO { isRoad = true, massPerMeter = 0f,
            maxTension = 1000000000f, maxCompression = 1000000000f };
        var bars = new List<Bar> {
            new Bar { startPoint = left, endPoint = top, materialData = deck },
            new Bar { startPoint = top, endPoint = right, materialData = deck }, pier
        };
        var result = DeterministicBridgeStressSolver.Analyze(
            new List<Point> { left, top, right, foot }, bars, loadKg, true, 11);
        Check(result != null && result.IsValid && result.TryGetStress(pier, 5, out _, out _, out _),
            "The actual-asset fixture must produce a center-load pier sample.");
        result.TryGetStress(pier, 5, out _, out float ratio, out bool tension);
        Check(!tension && !float.IsNaN(ratio) && !float.IsInfinity(ratio),
            "The vertical pier fixture must report finite compression, not skip its support member.");
        return ratio;
    }

    private static BridgeMaterialSO ReadMaterial(string path)
    {
        string source = File.ReadAllText(path);
        float Number(string field)
        {
            Match match = Regex.Match(source, "(?m)^  " + Regex.Escape(field) + @":\s*([^\r\n]+)");
            Check(match.Success, "Missing material field: " + field);
            return float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        }
        return new BridgeMaterialSO {
            isPier = Number("isPier") != 0f, isDualBeam = Number("isDualBeam") != 0f,
            maxTension = Number("maxTension"), maxCompression = Number("maxCompression"),
            maxLength = Number("maxLength"), massPerMeter = Number("massPerMeter"), costPerMeter = Number("costPerMeter"),
            pierBucklingReferenceLength = Number("pierBucklingReferenceLength"),
            pierBucklingExponent = Number("pierBucklingExponent"),
            pierMinimumCompressionMultiplier = Number("pierMinimumCompressionMultiplier")
        };
    }

    private static string FindRoot()
    {
        for (DirectoryInfo directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, MaterialPath))) return directory.FullName;
        throw new InvalidOperationException("Run from the Civil-Craft project root.");
    }
    private static bool Near(float actual, float expected) => Math.Abs(actual - expected) <= .1f;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
