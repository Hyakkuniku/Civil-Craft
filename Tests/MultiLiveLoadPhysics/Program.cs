using System;
using System.Collections.Generic;
using UnityEngine;
using Solver = DeterministicBridgeStressSolver;

static class Program
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static Point Node(float x, float y, bool fixedNode = false) => new Point {
        transform = new Transform { position = new Vector3(x, y, 0) }, originalIsAnchor = fixedNode, isAnchor = fixedNode };
    static Bar Member(Point a, Point b, BridgeMaterialSO material) => new Bar { startPoint = a, endPoint = b, materialData = material };
    static Solver.PointLoad Load(float x, float kg) => new Solver.PointLoad(x, kg);
    static Solver.Sample Copy(Solver.Result result)
    {
        Check(result != null && result.IsValid, "A valid current load result is required.");
        var sample = new Solver.Sample(result.Bars.Length);
        Array.Copy(result.Samples[0].DisplayedRatios, sample.DisplayedRatios, result.Bars.Length);
        Array.Copy(result.Samples[0].StructuralRatios, sample.StructuralRatios, result.Bars.Length);
        Array.Copy(result.Samples[0].IsTension, sample.IsTension, result.Bars.Length); return sample;
    }
    static float Signed(Solver.Sample sample, int i) => sample.StructuralRatios[i] * (sample.IsTension[i] ? 1f : -1f);
    static bool Same(Solver.Sample a, Solver.Sample b, float tolerance = .000002f)
    {
        if (a.StructuralRatios.Length != b.StructuralRatios.Length) return false;
        for (int i = 0; i < a.StructuralRatios.Length; i++)
            if (Math.Abs(Signed(a, i) - Signed(b, i)) > tolerance || Math.Abs(a.DisplayedRatios[i] - b.DisplayedRatios[i]) > tolerance) return false;
        return true;
    }
    static void Main()
    {
        var road = new BridgeMaterialSO { isRoad = true, massPerMeter = 120, maxTension = 1000000, maxCompression = 1000000 };
        var beam = new BridgeMaterialSO { massPerMeter = 90, maxTension = 1000000, maxCompression = 1000000 };
        var pier = new BridgeMaterialSO { isPier = true, massPerMeter = 240, maxTension = 1000000, maxCompression = 1000000 };
        Point a = Node(-10, 0, true), b = Node(-5, 0), c = Node(0, 0), d = Node(5, 0), e = Node(10, 0, true);
        Point u = Node(-5, 4), v = Node(0, 4), w = Node(5, 4), p = Node(-5, -6), q = Node(5, -6);
        var points = new List<Point> { a, b, c, d, e, u, v, w, p, q };
        var bars = new List<Bar> { Member(a,b,road), Member(b,c,road), Member(c,d,road), Member(d,e,road),
            Member(a,u,beam), Member(u,b,beam), Member(u,v,beam), Member(u,c,beam), Member(v,c,beam), Member(v,w,beam),
            Member(c,w,beam), Member(w,d,beam), Member(w,e,beam), Member(p,b,pier), Member(q,d,pier) };
        var prepared = Solver.Prepare(points, bars, false);
        Check(prepared != null && prepared.RoadMinX == -10 && prepared.RoadMaxX == 10, "Prepared road bounds must reflect the actual deck.");
        var dead = Copy(prepared.Evaluate(Array.Empty<Solver.PointLoad>()));
        var first = Copy(prepared.Evaluate(new[] { Load(-7,1200) }));
        var second = Copy(prepared.Evaluate(new[] { Load(3,2300) }));
        var bothResult = prepared.Evaluate(new[] { Load(-7,1200), Load(3,2300) }); var both = Copy(bothResult);
        // Signed force superposition for this equal-capacity, non-rope truss:
        // F(dead + A + B) = F(dead+A) + F(dead+B) - F(dead). Dead load occurs once.
        // Production ratios round to .001; four independently rounded terms
        // can differ by at most .002 even when their signed forces agree.
        for (int i = 0; i < both.StructuralRatios.Length; i++)
            Check(Math.Abs(Signed(both,i) - Signed(first,i) - Signed(second,i) + Signed(dead,i)) < .0021f,
                "Combined loading must count dead load once and solve one physical force vector.");
        var lumped = Copy(prepared.Evaluate(new[] { Load(0,3500) }));
        Check(!Same(both,lumped,.0001f), "Different vehicle positions must not be replaced by one summed load at the center.");
        var coincident = Copy(prepared.Evaluate(new[] { Load(0,1200), Load(0,2300) }));
        Check(Same(coincident,lumped), "Coincident physical loads must equal one load of the combined mass.");
        var legacy = Solver.Analyze(points,bars,3500,false,21);
        Check(Same(legacy.Samples[10],coincident), "Prepared combined evaluation must match legacy single-load sampling at the same position.");
        var partial = Copy(prepared.Evaluate(new[] { Load(0,1200*.25f), Load(0,2300*.5f), Load(7,0) }));
        var effective = Copy(prepared.Evaluate(new[] { Load(0,1450) }));
        Check(Same(partial,effective), "Partial wheel contact must contribute only effective supported vehicle mass; off-bridge load contributes zero.");
        var four = Copy(prepared.Evaluate(new[] { Load(-8,500), Load(-2,800), Load(2,1300), Load(8,2000) }));
        Check(!Same(four,dead), "Four live loads must affect the bridge.");
        var three = Copy(prepared.Evaluate(new[] { Load(-8,500), Load(-2,800), Load(2,1300) }));
        Check(!Same(three,four), "The fourth vehicle must independently contribute, and three-vehicle configurations must solve.");
        Check(Same(dead,Copy(prepared.Evaluate(Array.Empty<Solver.PointLoad>()))), "Departed vehicles must restore exactly the same dead-load sample.");
        Check(ReferenceEquals(bothResult,prepared.Evaluate(new[] { Load(0,3500) })), "Runtime current results must be reused rather than allocating arrays for every tick.");
        bool pierLoaded = false;
        for (int i = 0; i < bothResult.Bars.Length; i++)
            if (bothResult.Bars[i].materialData.isPier && both.StructuralRatios[i] > dead.StructuralRatios[i]) pierLoaded = true;
        Check(pierLoaded, "The actual pier must carry part of the combined loading.");
        for (int i = 0; i < 100; i++) Check(Same(both,Copy(prepared.Evaluate(new[] { Load(-7,1200), Load(3,2300) }))), "Repeated current load solves must remain deterministic.");
        var rope = new BridgeMaterialSO { isRope = true, massPerMeter = 10, maxTension = 200000, maxCompression = 200000 };
        bars.Add(Member(a,v,rope)); bars.Add(Member(v,e,rope)); bars.Add(Member(u,d,rope)); bars.Add(Member(p,b,rope));
        var cablePrepared = Solver.Prepare(points,bars,false);
        var cableResult = cablePrepared.Evaluate(new[] { Load(-7,1200), Load(3,2300) }); var cable = Copy(cableResult);
        int slackCables = 0;
        for (int i = 0; i < cableResult.Bars.Length; i++)
            if (cableResult.Bars[i].materialData.isRope && !cable.IsTension[i])
            { slackCables++; Check(cable.StructuralRatios[i] == 0 && cable.DisplayedRatios[i] == 0, "Slack cables must not carry compression in the combined solve."); }
        Check(slackCables > 0, "The cable fixture must actually exercise removal of a compressed/slack rope.");
        Check(Same(cable,Copy(cablePrepared.Evaluate(new[] { Load(3,2300), Load(-7,1200) }))), "Combined load ordering must not change cable active-set forces.");
        bars[5].gameObject.activeInHierarchy = false;
        var damaged = Solver.Prepare(points,bars,false);
        Check(damaged.Evaluate(new[] { Load(-7,1200), Load(3,2300) }).Bars.Length == cableResult.Bars.Length - 1, "Reprepared topology must exclude a detached member while retaining all vehicle loads.");
        bool rejected = false; try { prepared.Evaluate(new[] { Load(0,float.NaN) }); } catch (ArgumentException) { rejected=true; }
        Check(rejected, "Nonfinite loaded mass must be rejected before decimal arithmetic.");
        rejected=false; try { prepared.Evaluate(new[] { Load(0,float.MaxValue) }); } catch (ArgumentException) { rejected=true; }
        Check(rejected, "Huge finite weights must fail validation rather than overflow decimal arithmetic.");
        var far = Copy(prepared.Evaluate(new[] { Load(float.MaxValue,1000) }));
        Check(Same(far,Copy(prepared.Evaluate(new[] { Load(10,1000) }))), "Finite out-of-span positions must clamp safely before decimal conversion.");
        Check(!Same(four,Copy(prepared.Evaluate(new[] { Load(-8,500), Load(-2,800), Load(2,1300), Load(8,2000), Load(0,1000) }))), "The fifth vehicle must independently contribute.");
        rejected=false; try { prepared.Evaluate(new[] { Load(0,1),Load(0,1),Load(0,1),Load(0,1),Load(0,1),Load(0,1) }); } catch (ArgumentException) { rejected=true; }
        Check(rejected, "The approved five-vehicle bound must be enforced.");
        Console.WriteLine("PASS: " + checks + " combined-load/partial-contact/pier/cable/determinism/topology assertions against the production solver.");
    }
}
