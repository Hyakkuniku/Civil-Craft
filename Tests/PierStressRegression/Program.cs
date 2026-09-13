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
        Console.WriteLine("PASS: pier foundation carries center load with bank anchors present.");

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

        pierMaterial.maxCompression = 5000;
        var overload = DeterministicBridgeStressSolver.Analyze(points,bars,1000,true,11);
        overload.TryGetStress(pier,5,out _,out ratio,out tension);
        Check(!tension && ratio>1, "Weak pier must still overload in compression.");
        Console.WriteLine("PASS: weak pier overload remains detectable.");
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
