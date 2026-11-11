using System;
using System.Collections.Generic;
using UnityEngine;

static class Program
{
    static Point Node(float x, float y, bool bank = false) => new Point {
        transform = new Transform { position = new Vector3(x,y,0) },
        originalIsAnchor = bank, isAnchor = bank
    };
    static Bar Member(Point a, Point b, BridgeMaterialSO material) => new Bar {
        startPoint = a, endPoint = b, materialData = material
    };
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Main()
    {
        var roadMaterial = new BridgeMaterialSO { isRoad=true, massPerMeter=0, maxTension=100000, maxCompression=100000 };
        var pierMaterial = new BridgeMaterialSO { isPier=true, massPerMeter=0, maxTension=100000, maxCompression=100000 };
        var left = Node(-5,0,true);
        var top = Node(0,0);
        var right = Node(5,0,true);
        var bottom = Node(0,-5);
        top.isAnchor = bottom.isAnchor = true; // Runtime pier endpoint flags.
        var pier = Member(bottom,top,pierMaterial);
        var points = new List<Point> { left,top,right,bottom };
        var bars = new List<Bar> { Member(left,top,roadMaterial),Member(top,right,roadMaterial),pier };
        var result = DeterministicBridgeStressSolver.Analyze(points,bars,1000,true,11);
        Check(result != null, "Bank + pier bridge must solve.");
        result.TryGetStress(pier,5,out _,out float ratio,out bool tension);
        Check(!tension && Math.Abs(ratio - 0.098f) < 0.0001f,
            $"Grounded pier must carry 9810 N in compression (9.8%), got {ratio*100}% tension={tension}.");
        Check(result.PeakStructuralStress < 1, "Adequately supported bridge must survive.");
        Check(result.DeadLoadSample.UnsupportedRoad == null && result.Samples[5].UnsupportedRoad == null,
            "A supported pier span must not be selected for collapse.");
        Console.WriteLine("PASS: pier foundation carries center load with bank anchors present.");

        // Report the unsupported road separately from the real material stress.
        var mechanismLeft = Node(-5, 0, true);
        var mechanismCenter = Node(0, 0);
        var mechanismRight = Node(5, 0, true);
        var mechanismBars = new List<Bar> {
            Member(mechanismLeft, mechanismCenter, roadMaterial),
            Member(mechanismCenter, mechanismRight, roadMaterial)
        };
        var mechanism = DeterministicBridgeStressSolver.Analyze(
            new List<Point> { mechanismLeft, mechanismCenter, mechanismRight },
            mechanismBars,
            1000,
            true,
            11);
        Check(mechanism != null && mechanism.IsValid, "Incomplete truss must still produce stress samples.");
        Check(!mechanism.IsStructurallyStable, "Untriangulated deck must remain identifiable as a mechanism.");
        Check(mechanism.DeadLoadSample.UnsupportedRoad == null && mechanism.Samples[0].UnsupportedRoad == null &&
            mechanism.Samples[5].UnsupportedRoad != null,
            "A weightless road should only have an unsupported load once the vehicle reaches a free joint.");
        for (int i = 0; i < mechanism.Bars.Length; i++)
            Check(mechanism.Samples[5].StructuralRatios[i] < 1f,
                "Mechanism diagnostics must not fabricate a 100% member overload.");
        Console.WriteLine("PASS: incomplete truss keeps real per-member stress instead of synthetic failure.");

        CheckFlatDeckSupport();
        CheckRoadBendingOnPiers();
        CheckRoadStressInteraction();

        pier.startPoint = top; pier.endPoint = bottom;
        var reversed = DeterministicBridgeStressSolver.Analyze(points,bars,1000,true,11);
        Check(Same(result,reversed), "Endpoint reversal changed stresses.");
        Console.WriteLine("PASS: reversed pier endpoints preserve support and stress.");

        // A tiny deck slope previously magnified the load from an ungrounded
        // heavy pier into a large axial force in the nearly horizontal roads.
        top.transform.position = new Vector3(0,-0.001f,0);
        pierMaterial.massPerMeter = 182.5f;
        roadMaterial.massPerMeter = 9f;
        var heavyPier = DeterministicBridgeStressSolver.Analyze(points,bars,1000,true,11);
        Check(heavyPier != null && heavyPier.PeakStructuralStress < 1,
            "A grounded heavy pier must not collapse a slightly sloped deck on startup.");
        Console.WriteLine("PASS: heavy pier and slightly sloped deck survive dead and live load.");
        top.transform.position = new Vector3(0,0,0);
        pierMaterial.massPerMeter = roadMaterial.massPerMeter = 0;

        pier.gameObject.activeInHierarchy = false;
        var removed = DeterministicBridgeStressSolver.Analyze(points,bars,1000,true,11);
        Check(!Array.Exists(removed.Bars, b => b == pier), "Inactive pier must not supply a foundation or load.");
        pier.gameObject.activeInHierarchy = true;
        Console.WriteLine("PASS: inactive pier is excluded.");

        left.originalIsAnchor = right.originalIsAnchor = false;
        left.isAnchor = right.isAnchor = true;
        var leftFoot = Node(-5,-5); var rightFoot = Node(5,-5);
        leftFoot.isAnchor = rightFoot.isAnchor = true;
        points.Add(leftFoot); points.Add(rightFoot);
        bars.Add(Member(leftFoot,left,pierMaterial)); bars.Add(Member(rightFoot,right,pierMaterial));
        var pierOnly = DeterministicBridgeStressSolver.Analyze(points,bars,1000,true,11);
        Check(pierOnly != null, "Pier-only supports must solve without PhysX fallback.");
        pierOnly.TryGetStress(pier,5,out _,out ratio,out tension);
        Check(!tension && Math.Abs(ratio-0.098f)<0.0001f, "Pier-only supports must retain loaded columns.");
        Console.WriteLine("PASS: pier-only bridge keeps columns load-bearing.");

        for (int i=0;i<100;i++)
            Check(Same(pierOnly,DeterministicBridgeStressSolver.Analyze(points,bars,1000,true,11)), "Repeat differs.");
        Console.WriteLine("PASS: 100 repeated analyses match every stress and direction sample.");

        // Bridge Stats supplies HashSet snapshots. Reordering those inputs must
        // not change matrix assembly, road-joint load selection, or result order.
        var shuffledPoints = new List<Point>(points);
        shuffledPoints.Reverse();
        var shuffledBars = new List<Bar>(bars);
        shuffledBars.Reverse();
        var shuffled = DeterministicBridgeStressSolver.Analyze(
            shuffledPoints, shuffledBars, 1000, true, 11);
        Check(Same(pierOnly, shuffled),
            "Equivalent bridge input order changed deterministic stress results.");
        Console.WriteLine("PASS: shuffled Bridge Stats inputs produce identical deterministic results.");

        pierMaterial.maxCompression = 5000;
        var overload = DeterministicBridgeStressSolver.Analyze(points,bars,1000,true,11);
        overload.TryGetStress(pier,5,out _,out ratio,out tension);
        Check(!tension && ratio>1, "Weak pier must still overload in compression.");
        Console.WriteLine("PASS: weak pier overload remains detectable.");

        ConcretePierStrengthChecks.Run();
    }

    static void CheckFlatDeckSupport()
    {
        var road = new BridgeMaterialSO { isRoad = true, massPerMeter = 180, maxTension = 320000, maxCompression = 2000000 };
        var beam = new BridgeMaterialSO { massPerMeter = 60, maxTension = 1200000, maxCompression = 2600000 };
        var rope = new BridgeMaterialSO { isRope = true, massPerMeter = 5, maxTension = 450000, maxCompression = 0 };
        var pier = new BridgeMaterialSO { isPier = true, massPerMeter = 240, maxTension = 400000, maxCompression = 2100000 };
        var points = new List<Point>();
        var deck = new List<Bar>();
        for (int i = 0; i <= 6; i++)
        {
            points.Add(Node(i * 10, 0, i == 0 || i == 6));
            if (i > 0) deck.Add(Member(points[i - 1], points[i], road));
        }
        foreach (var reinforcement in new[] { beam, rope })
        {
            var bars = new List<Bar>(deck);
            for (int i = 1; i < points.Count; i++)
                bars.Add(Member(points[i - 1], points[i], reinforcement));

            var single = DeterministicBridgeStressSolver.Analyze(points, bars, 20, false, 11);
            var prepared = DeterministicBridgeStressSolver.Prepare(points, bars, false);
            var convoy = prepared.Evaluate(new[] { new DeterministicBridgeStressSolver.PointLoad(20, 20),
                new DeterministicBridgeStressSolver.PointLoad(40, 10) });
            Check(deck.Contains(single.DeadLoadSample.UnsupportedRoad) && deck.Contains(convoy.DeadLoadSample.UnsupportedRoad),
                "Horizontal beams and ropes cannot carry an unsupported road's weight.");
            Check(single.PeakStructuralStress < 1f && convoy.PeakStructuralStress < 1f,
                "An unsupported deck can have low actual member stress.");
        }

        var supportedBars = new List<Bar>(deck);
        for (int i = 1; i < 6; i++)
        {
            Point foot = Node(i * 10, -15);
            points.Add(foot);
            supportedBars.Add(Member(foot, points[i], pier));
        }
        Point hangingPoint = Node(30, -2);
        points.Add(hangingPoint);
        supportedBars.Add(Member(points[3], hangingPoint, beam));
        var supported = DeterministicBridgeStressSolver.Analyze(points, supportedBars, 5000, false, 11);
        Check(!supported.IsStructurallyStable && supported.DeadLoadSample.UnsupportedRoad == null &&
            supported.Samples[5].UnsupportedRoad == null,
            "A dangling member can sway without invalidating the supported road.");

        Point hangerAnchor = Node(10, 6, true);
        Point cableSpanEnd = Node(20, 0, true);
        var cableSupported = DeterministicBridgeStressSolver.Analyze(
            new List<Point> { points[0], points[1], cableSpanEnd, hangerAnchor },
            new List<Bar> { deck[0], Member(points[1], cableSpanEnd, road), Member(hangerAnchor, points[1], rope) },
            5000, false, 11);
        Check(cableSupported != null && cableSupported.IsStructurallyStable &&
            cableSupported.DeadLoadSample.UnsupportedRoad == null && cableSupported.Samples[5].UnsupportedRoad == null,
            "A tensioned hanger must be allowed to carry vertical load from the road.");
        Console.WriteLine("PASS: flat beams and ropes lose support, while piers and tensioned hangers retain it.");
    }

    static void CheckRoadBendingOnPiers()
    {
        Point left = Node(0, 0, true), firstTop = Node(10, 0), secondTop = Node(20, 0), right = Node(30, 0, true);
        Point firstFoot = Node(10, -29.7942f), secondFoot = Node(20, -29.7942f);
        var points = new List<Point> { left, firstTop, secondTop, right, firstFoot, secondFoot };
        var wood = new BridgeMaterialSO { isRoad = true, massPerMeter = 9, maxTension = 124500,
            maxCompression = 124000, roadBendingMomentLimit = 180000 };
        var concrete = new BridgeMaterialSO { isRoad = true, massPerMeter = 180, maxTension = 320000,
            maxCompression = 2000000, roadBendingMomentLimit = 800000 };
        var pierMaterial = new BridgeMaterialSO { isPier = true, massPerMeter = 240, maxTension = 400000,
            maxCompression = 2100000 };
        Bar firstPier = Member(firstFoot, firstTop, pierMaterial);
        Bar middle = Member(firstTop, secondTop, wood);
        var bars = new List<Bar> { Member(left, firstTop, wood), middle, Member(secondTop, right, wood),
            firstPier, Member(secondFoot, secondTop, pierMaterial) };

        var woodResult = DeterministicBridgeStressSolver.Analyze(points, bars, 20000, false, 31);
        Check(woodResult.IsStructurallyStable && woodResult.DeadLoadSample.UnsupportedRoad == null,
            "The road must have pier support before measuring its bending limit.");
        woodResult.TryGetSimulationStress(middle, 15, 1f, out _, out float stress, out _, out _, out float bending);
        Check(bending >= 1f && Math.Abs(stress - 2.731f) < .0001f, "The supported wood panel must overload in bending.");
        woodResult.TryGetSimulationStress(middle, 15, .25f, out _, out stress, out _, out _, out bending);
        Check(bending > 0f && stress < 1f, "A vehicle with only partial road contact must not break this wood panel.");
        woodResult.TryGetStress(middle, 10, out _, out stress, out _);
        Check(stress == .006f, "Weight directly at a joint must go to the supports with no extra panel moment.");
        woodResult.TryGetStress(firstPier, 15, out _, out float woodPierStress, out bool pierTension);
        Check(!pierTension && woodPierStress < 1f, "Piers should remain intact when the wood panel overloads.");

        for (int i = 0; i < 3; i++) bars[i].materialData = concrete;
        var concreteResult = DeterministicBridgeStressSolver.Analyze(points, bars, 20000, false, 31);
        concreteResult.TryGetSimulationStress(middle, 15, 1f, out _, out stress, out _, out _, out bending);
        Check(bending > 0f && Math.Abs(stress - .641f) < .0001f, "Concrete must carry the same mid-panel vehicle.");
        concreteResult.TryGetStress(firstPier, 15, out _, out float concretePierStress, out _);
        Check(concretePierStress - woodPierStress > .05f,
            "The heavier deck should increase compression on the same pier without weakening the deck.");
        woodResult.TryGetStress(firstPier, 10, out _, out float woodAtJoint, out _);
        concreteResult.TryGetStress(firstPier, 10, out _, out float concreteAtJoint, out _);
        Check(Math.Abs(woodAtJoint - .737f) < .0001f && Math.Abs(concreteAtJoint - .790f) < .0001f,
            "Brooks-sized pier should reproduce the stress difference when the same load is directly over it.");

        var prepared = DeterministicBridgeStressSolver.Prepare(points, bars, true);
        var heavy = prepared.Evaluate(new[] { new DeterministicBridgeStressSolver.PointLoad(15, 40000) });
        heavy.TryGetSimulationStress(middle, 0, 1f, out float displayed, out stress, out _, out _, out bending);
        Check(bending >= 1f && stress > 1f && displayed < stress, "Overloaded concrete must break, counting dead load only in total stress.");
        var two = prepared.Evaluate(new[] { new DeterministicBridgeStressSolver.PointLoad(17, 15000),
            new DeterministicBridgeStressSolver.PointLoad(13, 15000) });
        two.TryGetSimulationStress(middle, 0, 1f, out displayed, out stress, out _, out _, out bending);
        Check(bending > 0f && Math.Abs(stress - .579f) < .0001f && Math.Abs(displayed - .552f) < .0001f,
            "Two loads must share one moment diagram and count the deck weight once.");

        // A 6–8–10 panel has a 10 m longitudinal length and 8 m horizontal lever arm.
        var bankSpan = Member(Node(8, 6, true), left, wood);
        var fixedSpan = DeterministicBridgeStressSolver.Analyze(
            new[] { bankSpan.startPoint, left }, new[] { bankSpan }, 5000, false, 3);
        Check(fixedSpan != null && fixedSpan.IsStructurallyStable && Math.Abs(fixedSpan.PeakStructuralStress - .55f) < .0001f,
            "A reversed sloped road between fixed banks must include its horizontal bending lever arm.");
        Console.WriteLine("PASS: supported wood and concrete panel bending, pier compression, partial load, convoy, and sloped fixed span.");
    }

    static void CheckRoadStressInteraction()
    {
        var deck = new BridgeMaterialSO { isRoad = true, massPerMeter = 180, maxTension = 320000,
            maxCompression = 2000000, roadBendingMomentLimit = 800000 };
        var steel = new BridgeMaterialSO { isDualBeam = true, massPerMeter = 60,
            maxTension = 1200000, maxCompression = 2600000 };
        var pier = new BridgeMaterialSO { isPier = true, massPerMeter = 170,
            maxTension = 750000, maxCompression = 3100000 };
        float EvaluateTruss(float height, bool addPiers = false)
        {
            var lower = new[] { Node(0, 0, true), Node(10, 0), Node(20, 0), Node(30, 0, true) };
            var upper = new[] { Node(5, height), Node(15, height), Node(25, height) };
            var points = new List<Point>(lower);
            points.AddRange(upper);
            var bars = new List<Bar>();
            Bar middle = Member(lower[1], lower[2], deck);
            for (int panel = 0; panel < 3; panel++)
            {
                bars.Add(panel == 1 ? middle : Member(lower[panel], lower[panel + 1], deck));
                bars.Add(Member(lower[panel], upper[panel], steel));
                bars.Add(Member(upper[panel], lower[panel + 1], steel));
                if (panel > 0) bars.Add(Member(upper[panel - 1], upper[panel], steel));
            }
            if (addPiers)
                for (int joint = 1; joint < 3; joint++)
                {
                    Point foundation = Node(joint * 10, -30);
                    points.Add(foundation);
                    bars.Add(Member(foundation, lower[joint], pier));
                }

            var prepared = DeterministicBridgeStressSolver.Prepare(points, bars, false);
            var result = prepared.Evaluate(new[] { new DeterministicBridgeStressSolver.PointLoad(15, 20000) });
            Check(result.IsStructurallyStable, "The truss comparison requires a stable design.");
            Check(result.TryGetSimulationStress(middle, 0, 1f, out float displayed, out float combined,
                out bool tension, out float axial, out float bending) && tension,
                "The middle road panel also acts as the truss's tension chord.");
            Check(Math.Abs(bending - .6407f) < .0001f && axial > 0f &&
                Math.Abs(combined - (axial + bending)) < .00051f && combined == displayed,
                "The panel must combine local bending and truss tension from the same load case.");
            return combined;
        }

        float shallow = EvaluateTruss(3f);
        float deep = EvaluateTruss(15f);
        float onPiers = EvaluateTruss(3f, addPiers: true);
        Check(shallow > 1f && deep < 1f && onPiers < shallow - .05f,
            $"Changing only the truss layout must change the peak on the same concrete panel. Shallow={shallow:P1}, deep={deep:P1}.");
        Console.WriteLine($"PASS: equal 10 m panels under 20,000 kg score {shallow:P1} in a shallow truss, {deep:P1} in a deeper truss, and {onPiers:P1} in the shallow truss with piers.");

        Bar probe = Member(Node(0, 0, true), Node(10, 0, true), deck);
        var unloaded = new DeterministicBridgeStressSolver.Sample(1);
        var loaded = new DeterministicBridgeStressSolver.Sample(1);
        // Equal and opposite 160 kN axial forces, normalized by each capacity.
        unloaded.SetStress(0, .08m, .1m, .08m, .1m, false);
        loaded.SetStress(0, .5m, .5m, .5m, .5m, true);
        var interpolation = new DeterministicBridgeStressSolver.Result(
            new[] { probe }, new[] { loaded }, unloaded, 1f, 1f, true, false);
        interpolation.TryGetSimulationStress(probe, 0, .5f, out _, out float partial,
            out _, out float partialAxial, out float partialBending);
        Check(partialAxial < .0001f && Math.Abs(partial - .3f) < .0001f && partial == partialBending,
            "Opposite axial forces must cancel at partial road contact while bending remains.");
    }

    static bool Same(DeterministicBridgeStressSolver.Result a, DeterministicBridgeStressSolver.Result b)
    {
        if (a==null || b==null || a.Samples.Length!=b.Samples.Length) return false;
        for (int i=0;i<a.Samples.Length;i++)
            for (int j=0;j<a.Bars.Length;j++)
                if (a.Samples[i].DisplayedRatios[j]!=b.Samples[i].DisplayedRatios[j] ||
                    a.Samples[i].StructuralRatios[j]!=b.Samples[i].StructuralRatios[j] ||
                    a.Samples[i].IsTension[j]!=b.Samples[i].IsTension[j]) return false;
        return true;
    }
}
