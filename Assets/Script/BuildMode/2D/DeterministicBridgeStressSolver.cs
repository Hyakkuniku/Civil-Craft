using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Deterministic 2D truss and local road-panel bending analysis for gameplay stress.
/// Geometry and material inputs are quantized before decimal arithmetic, so the
/// same bridge and contract produce the same samples independently of PhysX.
/// </summary>
public static class DeterministicBridgeStressSolver
{
    public sealed class Result
    {
        private readonly Dictionary<Bar, int> barIndices;
        private readonly bool displayLiveLoadOnly;

        internal Result(
            Bar[] bars,
            Sample[] samples,
            Sample deadLoadSample,
            float peakDisplayed,
            float peakStructural,
            bool isStructurallyStable,
            bool displayLiveLoadOnly)
        {
            Bars = bars;
            Samples = samples;
            DeadLoadSample = deadLoadSample;
            PeakDisplayedStress = peakDisplayed;
            PeakStructuralStress = peakStructural;
            IsStructurallyStable = isStructurallyStable;
            this.displayLiveLoadOnly = displayLiveLoadOnly;
            barIndices = new Dictionary<Bar, int>(bars.Length);
            for (int i = 0; i < bars.Length; i++)
                if (bars[i] != null) barIndices[bars[i]] = i;
        }

        public Bar[] Bars { get; }
        public Sample[] Samples { get; }
        public Sample DeadLoadSample { get; }
        public float PeakDisplayedStress { get; private set; }
        public float PeakStructuralStress { get; private set; }
        /// <summary>
        /// False when the authored bars contain a free mechanism. This is exposed
        /// for diagnostics; gameplay still uses the calculated member stresses and
        /// lets the physics simulation demonstrate the mechanism's deformation.
        /// </summary>
        public bool IsStructurallyStable { get; private set; }
        public bool IsValid => Bars.Length > 0 && Samples.Length > 0;

        internal void SetCurrentState(float displayed, float structural, bool stable)
        { PeakDisplayedStress = displayed; PeakStructuralStress = structural; IsStructurallyStable = stable; }

        public bool TryGetStress(Bar bar, int sampleIndex, out float displayed, out float structural, out bool tension)
        {
            displayed = 0f;
            structural = 0f;
            tension = false;
            if (bar == null || Samples.Length == 0 || !barIndices.TryGetValue(bar, out int barIndex))
                return false;

            Sample sample = Samples[Mathf.Clamp(sampleIndex, 0, Samples.Length - 1)];
            displayed = sample.DisplayedRatios[barIndex];
            structural = sample.StructuralRatios[barIndex];
            tension = sample.IsTension[barIndex];
            return true;
        }

        public bool TryGetDeadLoadStress(Bar bar, out float displayed, out float structural, out bool tension)
        {
            displayed = 0f;
            structural = 0f;
            tension = false;
            if (bar == null || DeadLoadSample == null || !barIndices.TryGetValue(bar, out int barIndex))
                return false;

            displayed = DeadLoadSample.DisplayedRatios[barIndex];
            structural = DeadLoadSample.StructuralRatios[barIndex];
            tension = DeadLoadSample.IsTension[barIndex];
            return true;
        }

        public bool TryGetSimulationStress(Bar bar, int sampleIndex, float loadFactor,
            out float displayed, out float structural, out bool tension, out float axialRatio, out float bendingRatio)
        {
            displayed = structural = axialRatio = bendingRatio = 0f;
            tension = false;
            if (bar == null || Samples.Length == 0 || !barIndices.TryGetValue(bar, out int barIndex))
                return false;

            Sample sample = Samples[Mathf.Clamp(sampleIndex, 0, Samples.Length - 1)];
            if (loadFactor <= 0f || loadFactor >= 1f)
            {
                Sample applied = loadFactor <= 0f ? DeadLoadSample : sample;
                displayed = applied.DisplayedRatios[barIndex];
                structural = applied.StructuralRatios[barIndex];
                tension = applied.IsTension[barIndex];
                axialRatio = applied.MemberStresses[barIndex].Axial;
                bendingRatio = applied.MemberStresses[barIndex].Bending;
                return true;
            }

            MemberStress loaded = sample.MemberStresses[barIndex];
            MemberStress dead = DeadLoadSample.MemberStresses[barIndex];
            axialRatio = Mathf.Lerp(dead.Axial, loaded.Axial, loadFactor);
            float displayedAxial = Mathf.Lerp(dead.DisplayedAxial, loaded.DisplayedAxial, loadFactor);
            bendingRatio = Mathf.Lerp(dead.Bending, loaded.Bending, loadFactor);
            tension = sample.IsTension[barIndex];
            BridgeMaterialSO material = bar.materialData;
            if (material != null && material.isRoad && material.roadBendingMomentLimit > 0f &&
                tension != DeadLoadSample.IsTension[barIndex])
            {
                // Opposite axial loads can cancel during entry or exit. Blend
                // force before applying the tension or compression capacity.
                float tensionLimit = material.maxTension;
                float compressionLimit = material.maxCompression;
                float deadForce = tension ? -dead.Axial * compressionLimit : dead.Axial * tensionLimit;
                float loadedForce = tension ? loaded.Axial * tensionLimit : -loaded.Axial * compressionLimit;
                float force = Mathf.Lerp(deadForce, loadedForce, loadFactor);
                tension = force >= 0f;
                float capacity = tension ? tensionLimit : compressionLimit;
                axialRatio = capacity > 0f ? Math.Abs(force) / capacity : 0f;
                displayedAxial = displayLiveLoadOnly && capacity > 0f ? Math.Abs(force - deadForce) / capacity : axialRatio;
            }
            displayed = Math.Min(MaxStressRatio,
                displayedAxial + Mathf.Lerp(dead.DisplayedBending, loaded.DisplayedBending, loadFactor));
            structural = Math.Min(MaxStressRatio, axialRatio + bendingRatio);
            return true;
        }
    }

    internal readonly struct MemberStress
    {
        public readonly float Axial;
        public readonly float Bending;
        public readonly float DisplayedAxial;
        public readonly float DisplayedBending;

        public MemberStress(decimal axial, decimal bending, decimal displayedAxial, decimal displayedBending)
        {
            Axial = (float)DecimalMin(MaxStressRatio, axial);
            Bending = (float)DecimalMin(MaxStressRatio, bending);
            DisplayedAxial = (float)DecimalMin(MaxStressRatio, displayedAxial);
            DisplayedBending = (float)DecimalMin(MaxStressRatio, displayedBending);
        }
    }

    public sealed class Sample
    {
        internal Sample(int barCount)
        {
            DisplayedRatios = new float[barCount];
            StructuralRatios = new float[barCount];
            IsTension = new bool[barCount];
            MemberStresses = new MemberStress[barCount];
        }

        public float[] DisplayedRatios { get; }
        public float[] StructuralRatios { get; }
        public bool[] IsTension { get; }
        public Bar UnsupportedRoad { get; internal set; }
        internal MemberStress[] MemberStresses { get; }

        internal void SetStress(int index, decimal axial, decimal bending, decimal displayedAxial,
            decimal displayedBending, bool tension)
        {
            MemberStresses[index] = new MemberStress(axial, bending, displayedAxial, displayedBending);
            // Axial and bending stress reinforce each other on one face of the
            // panel. Use a linear capacity interaction for the same load case.
            // For members without a bending limit, the bending term is zero.
            StructuralRatios[index] = RatioToFloat(axial + bending);
            DisplayedRatios[index] = RatioToFloat(displayedAxial + displayedBending);
            IsTension[index] = tension;
        }
    }

    private sealed class NodeData
    {
        public Point Point;
        public decimal X;
        public decimal Y;
        public bool IsFixed;
        public bool HasMember;
        public int XDegree = -1;
        public int YDegree = -1;
    }

    private sealed class MemberData
    {
        public Bar Bar;
        public int NodeA;
        public int NodeB;
        public decimal Length;
        public decimal Cos;
        public decimal Sin;
        public decimal AxialRigidity;
        public decimal TensionLimit;
        public decimal CompressionLimit;
        public decimal BendingMomentLimit;
        public decimal MassKg;
        public decimal WindProfileArea;
        public decimal WindDeckArea;
        public bool IsRope;
        public bool IsRoad;
    }

    private sealed class ActiveSystem
    {
        public decimal[,] Factor;
        public int[] Pivots;
        public bool IsStructurallyStable;
        public decimal Regularization;
    }

    private const decimal Gravity = 9.81m;
    private const decimal MillimetresPerMetre = 1000m;
    private const decimal MinimumPivot = 0.000000000001m;
    private const int MaxStressRatio = 1000000;

    private readonly struct RoadPointLoad
    {
        public readonly int MemberIndex;
        public readonly decimal Fraction;
        public readonly decimal Weight;

        public RoadPointLoad(int memberIndex, decimal fraction, decimal weight)
        {
            MemberIndex = memberIndex;
            Fraction = fraction;
            Weight = weight;
        }
    }

    /// <summary>A live-load contribution already scaled by actual wheel contact with the bridge.</summary>
    public readonly struct PointLoad
    {
        public readonly float WorldX;
        public readonly float WeightKg;
        public PointLoad(float worldX, float weightKg) { WorldX = worldX; WeightKg = weightKg; }
    }

    public readonly struct EnvironmentalLoad
    {
        public readonly decimal WindPressure;
        public readonly decimal AccelerationX;
        public readonly decimal AccelerationY;
        public bool IsActive => WindPressure != 0m || AccelerationX != 0m || AccelerationY != 0m;

        public EnvironmentalLoad(float windPressure, float accelerationX, float accelerationY)
        {
            WindPressure = (decimal)windPressure;
            AccelerationX = (decimal)accelerationX;
            AccelerationY = (decimal)accelerationY;
        }

        public void MemberForce(decimal mass, decimal profileArea, decimal deckArea, out decimal x, out decimal y)
        {
            x = mass * AccelerationX + profileArea * WindPressure;
            y = mass * AccelerationY - deckArea * DecimalAbs(WindPressure) * .15m;
        }

        public static float ProfileArea(float length, bool road, bool rope, bool paired) =>
            length * (road ? .25f : (rope ? .06f : .3f) * (paired ? 2f : 1f));

        public static decimal DeckArea(float length, float width) =>
            float.IsFinite(width) ? (decimal)length * (decimal)Mathf.Clamp(width, 0f, 100f) : 0m;
    }

    /// <summary>Prepared geometry and cached active-set factors. Evaluate returns one reused current result.</summary>
    public sealed class PreparedAnalysis
    {
        private readonly Func<IReadOnlyList<PointLoad>, EnvironmentalLoad, Result> evaluate;
        private readonly Func<int, int, float, Result> evaluateSample;
        public float RoadMinX { get; }
        public float RoadMaxX { get; }
        internal PreparedAnalysis(Func<IReadOnlyList<PointLoad>, EnvironmentalLoad, Result> evaluator,
            Func<int, int, float, Result> sampler, float minX, float maxX)
        { evaluate = evaluator; evaluateSample = sampler; RoadMinX = minX; RoadMaxX = maxX; }
        public Result Evaluate(IReadOnlyList<PointLoad> loads, EnvironmentalLoad environment = default) =>
            evaluate(loads ?? Array.Empty<PointLoad>(), environment);
        internal Result EvaluateSample(int index, int count, float kg) => evaluateSample(index, count, kg);
    }

    // Preserve the single-load sampling API used by Bridge Stats and legacy contracts.
    public static Result Analyze(IList<Point> sourcePoints, IList<Bar> sourceBars, float liveLoadKg,
        bool displayLiveLoadOnly, int requestedSampleCount, bool usePreSimulationPositions = false)
    {
        PreparedAnalysis prepared = Prepare(sourcePoints, sourceBars, displayLiveLoadOnly, usePreSimulationPositions);
        if (prepared == null) return null;
        int count = Mathf.Clamp(requestedSampleCount, 3, 201);
        var samples = new Sample[count]; Result current = null;
        float peakDisplayed = 0f, peakStructural = 0f; bool stable = true;
        for (int index = 0; index < count; index++)
        {
            if (liveLoadKg == 0f && index > 0) { samples[index] = samples[0]; continue; }
            current = prepared.EvaluateSample(index, count, liveLoadKg);
            if (current == null) return null;
            Sample source = current.Samples[0], sample = new Sample(current.Bars.Length);
            Array.Copy(source.DisplayedRatios, sample.DisplayedRatios, current.Bars.Length);
            Array.Copy(source.StructuralRatios, sample.StructuralRatios, current.Bars.Length);
            Array.Copy(source.IsTension, sample.IsTension, current.Bars.Length);
            Array.Copy(source.MemberStresses, sample.MemberStresses, current.Bars.Length);
            sample.UnsupportedRoad = source.UnsupportedRoad;
            samples[index] = sample;
            peakDisplayed = Mathf.Max(peakDisplayed, current.PeakDisplayedStress);
            peakStructural = Mathf.Max(peakStructural, current.PeakStructuralStress);
            stable &= current.IsStructurallyStable;
        }
        return new Result(current.Bars, samples, current.DeadLoadSample, peakDisplayed, peakStructural, stable,
            displayLiveLoadOnly);
    }

    public static PreparedAnalysis Prepare(
        IList<Point> sourcePoints,
        IList<Bar> sourceBars,
        bool displayLiveLoadOnly,
        bool usePreSimulationPositions = false)
    {
        if (sourcePoints == null || sourceBars == null) return null;

        // Callers commonly own their bridge topology in HashSets. HashSet
        // enumeration order is not a structural property and can change after
        // editing, reloading, or building for another platform. Canonicalize the
        // inputs here so every consumer (simulation and Bridge Stats) assembles
        // the same matrix and resolves road-joint load ties the same way.
        List<Point> orderedPoints = GetCanonicalPoints(sourcePoints, usePreSimulationPositions);
        List<Bar> orderedBars = GetCanonicalBars(sourceBars, usePreSimulationPositions);

        List<NodeData> nodes = new List<NodeData>();
        Dictionary<Point, int> nodeIndices = new Dictionary<Point, int>();
        for (int i = 0; i < orderedPoints.Count; i++)
        {
            Point point = orderedPoints[i];

            Vector3 position = usePreSimulationPositions ? point.preSimPos : point.transform.position;
            NodeData node = new NodeData
            {
                Point = point,
                X = QuantizeMetres(position.x),
                Y = QuantizeMetres(position.y),
                IsFixed = point.IsScenePlacedAnchor
            };
            nodeIndices.Add(point, nodes.Count);
            nodes.Add(node);
        }

        List<MemberData> members = new List<MemberData>();
        for (int i = 0; i < orderedBars.Count; i++)
        {
            Bar bar = orderedBars[i];
            if (bar == null || !bar.enabled || !bar.gameObject.activeInHierarchy ||
                bar.materialData == null || bar.startPoint == null || bar.endPoint == null ||
                !nodeIndices.TryGetValue(bar.startPoint, out int nodeA) ||
                !nodeIndices.TryGetValue(bar.endPoint, out int nodeB) || nodeA == nodeB)
                continue;

            decimal dx = nodes[nodeB].X - nodes[nodeA].X;
            decimal dy = nodes[nodeB].Y - nodes[nodeA].Y;
            decimal length = DecimalSqrt(dx * dx + dy * dy);
            if (length <= 0m) continue;

            BridgeMaterialSO material = bar.materialData;
            // BarCreator places player pier bases at the site's foundation height and deliberately
            // leaves originalIsAnchor false. They are foundations even when bank
            // anchors exist. Only constrain the lower end: fixing both runtime
            // isAnchor endpoints would bypass the column and its buckling limit.
            // Infer the base from quantized height so reversed/pasted bars agree.
            if (material.isPier && nodes[nodeA].Y != nodes[nodeB].Y)
            {
                int baseNode = nodes[nodeA].Y < nodes[nodeB].Y ? nodeA : nodeB;
                nodes[baseNode].IsFixed = true;
            }

            decimal parallelMembers = material.isDualBeam ? 2m : 1m;
            decimal tensionLimit = QuantizeNewtons(material.maxTension) * parallelMembers;
            decimal compressionLimit = QuantizeNewtons(material.GetCompressionLimit((float)length)) * parallelMembers;
            decimal referenceCapacity = DecimalMax(tensionLimit, compressionLimit);
            if (referenceCapacity <= 0m) referenceCapacity = 1m;

            members.Add(new MemberData
            {
                Bar = bar,
                NodeA = nodeA,
                NodeB = nodeB,
                Length = length,
                Cos = dx / length,
                Sin = dy / length,
                // Capacity-proportional stiffness gives stronger materials a
                // deterministic share of load in statically indeterminate bridges.
                AxialRigidity = referenceCapacity * 1000m,
                TensionLimit = tensionLimit,
                CompressionLimit = compressionLimit,
                BendingMomentLimit = material.isRoad && material.roadBendingMomentLimit > 0f &&
                    !float.IsInfinity(material.roadBendingMomentLimit)
                    ? QuantizeNewtons(material.roadBendingMomentLimit) * parallelMembers : 0m,
                MassKg = QuantizeMass((float)length * bar.PlacedMassPerMeter),
                WindProfileArea = (decimal)EnvironmentalLoad.ProfileArea((float)length, material.isRoad, material.isRope, material.isDualBeam),
                WindDeckArea = material.isRoad ? EnvironmentalLoad.DeckArea((float)length, bar.RoadColliderWidth) : 0m,
                IsRope = material.isRope,
                IsRoad = material.isRoad
            });
            nodes[nodeA].HasMember = true;
            nodes[nodeB].HasMember = true;
        }

        if (nodes.Count < 2 || members.Count == 0) return null;

        // A separate pier or construction offcut has no load path to the road.
        // Its free degrees of freedom must not make the driven bridge appear
        // unstable. Keep every component containing a road member, plus all
        // members genuinely connected to those components through shared nodes.
        // Disconnected road sections remain in the analysis.
        List<int>[] connectedMembersByNode = new List<int>[nodes.Count];
        for (int i = 0; i < members.Count; i++)
        {
            MemberData member = members[i];
            if (connectedMembersByNode[member.NodeA] == null)
                connectedMembersByNode[member.NodeA] = new List<int>();
            if (connectedMembersByNode[member.NodeB] == null)
                connectedMembersByNode[member.NodeB] = new List<int>();
            connectedMembersByNode[member.NodeA].Add(i);
            connectedMembersByNode[member.NodeB].Add(i);
        }

        bool[] includedNodes = new bool[nodes.Count];
        bool[] includedMembers = new bool[members.Count];
        Queue<int> nodesToVisit = new Queue<int>();
        for (int i = 0; i < members.Count; i++)
        {
            if (!members[i].IsRoad) continue;
            EnqueueNode(members[i].NodeA, includedNodes, nodesToVisit);
            EnqueueNode(members[i].NodeB, includedNodes, nodesToVisit);
        }
        if (nodesToVisit.Count == 0) return null;

        while (nodesToVisit.Count > 0)
        {
            int nodeIndex = nodesToVisit.Dequeue();
            foreach (int memberIndex in connectedMembersByNode[nodeIndex])
            {
                if (includedMembers[memberIndex]) continue;
                includedMembers[memberIndex] = true;
                MemberData member = members[memberIndex];
                EnqueueNode(member.NodeA, includedNodes, nodesToVisit);
                EnqueueNode(member.NodeB, includedNodes, nodesToVisit);
            }
        }

        List<MemberData> roadConnectedMembers = new List<MemberData>(members.Count);
        for (int i = 0; i < members.Count; i++)
            if (includedMembers[i]) roadConnectedMembers.Add(members[i]);
        members = roadConnectedMembers;
        foreach (NodeData node in nodes) node.HasMember = false;
        foreach (MemberData member in members)
        {
            nodes[member.NodeA].HasMember = true;
            nodes[member.NodeB].HasMember = true;
        }

        int degreeCount = 0;
        foreach (NodeData node in nodes)
        {
            // Build locations can retain unused construction points. They are not
            // part of the bridge and must not create empty stiffness rows.
            if (!node.HasMember || node.IsFixed) continue;
            node.XDegree = degreeCount++;
            node.YDegree = degreeCount++;
        }
        // A panel between two fixed joints has no axial displacement to solve,
        // but still bends under its own weight and vehicles.
        if (degreeCount == 0 && !members.Exists(member => member.BendingMomentLimit > 0m))
            return null;

        // Cache factorizations for rope active sets. A slack rope must be removed
        // from stiffness, not merely hidden from the reported stress: otherwise
        // it can secretly prop up a bridge while carrying compression.
        Dictionary<string, ActiveSystem> activeSystems = new Dictionary<string, ActiveSystem>();
        bool isStructurallyStable = true;

        decimal[] deadLoads = new decimal[degreeCount];
        foreach (MemberData member in members)
        {
            decimal endpointWeight = member.MassKg * Gravity / 2m;
            AddVerticalLoad(deadLoads, nodes[member.NodeA], -endpointWeight);
            AddVerticalLoad(deadLoads, nodes[member.NodeB], -endpointWeight);
        }

        decimal[] deadForces = SolveTensionOnly(
            nodes, members, degreeCount, deadLoads, activeSystems, out bool deadLoadStable, out Bar unsupportedDeadLoadRoad);
        if (deadForces == null) return null;
        isStructurallyStable &= deadLoadStable;

        // The unloaded stress is identical at every route sample. Keep it with
        // the loaded result instead of solving the whole bridge a second time.
        Sample deadLoadSample = new Sample(members.Count) { UnsupportedRoad = unsupportedDeadLoadRoad };
        for (int memberIndex = 0; memberIndex < members.Count; memberIndex++)
        {
            MemberData member = members[memberIndex];
            decimal deadForce = deadForces[memberIndex];
            bool tension = member.IsRope ? deadForce > 0m : deadForce >= 0m;
            decimal limit = tension ? member.TensionLimit : member.CompressionLimit;
            decimal structuralRatio = limit > 0m ? DecimalAbs(deadForce) / limit : 0m;
            if (member.IsRope && !tension) structuralRatio = 0m;
            decimal bendingRatio = member.BendingMomentLimit > 0m
                ? member.MassKg * Gravity * DecimalAbs(member.Length * member.Cos) / (8m * member.BendingMomentLimit)
                : 0m;
            deadLoadSample.SetStress(memberIndex, structuralRatio, bendingRatio,
                displayLiveLoadOnly ? 0m : structuralRatio, displayLiveLoadOnly ? 0m : bendingRatio, tension);
        }

        List<int> roadMemberIndices = new List<int>();
        decimal roadMinX = decimal.MaxValue;
        decimal roadMaxX = decimal.MinValue;
        for (int i = 0; i < members.Count; i++)
        {
            if (!members[i].IsRoad) continue;
            roadMemberIndices.Add(i);
            roadMinX = DecimalMin(roadMinX, DecimalMin(nodes[members[i].NodeA].X, nodes[members[i].NodeB].X));
            roadMaxX = DecimalMax(roadMaxX, DecimalMax(nodes[members[i].NodeA].X, nodes[members[i].NodeB].X));
        }
        if (roadMemberIndices.Count == 0 || roadMaxX <= roadMinX) return null;

        Bar[] resultBars = new Bar[members.Count];
        for (int i = 0; i < members.Count; i++) resultBars[i] = members[i].Bar;
        var sample = new Sample(members.Count);
        var current = new Result(resultBars, new[] { sample }, deadLoadSample, 0f, 0f, isStructurallyStable,
            displayLiveLoadOnly);
        var totalLoads = new decimal[degreeCount];
        var bendingLoads = new List<RoadPointLoad>(4);
        var environmentalDeckWeights = new decimal[members.Count];
        Result EvaluateCurrent(IReadOnlyList<PointLoad> loads, int sampleIndex, int sampleCount, float singleKg,
            EnvironmentalLoad environment)
        {
            Array.Copy(deadLoads, totalLoads, degreeCount);
            bendingLoads.Clear();
            if (environment.IsActive)
            {
                for (int index = 0; index < members.Count; index++)
                {
                    MemberData member = members[index];
                    environment.MemberForce(member.MassKg, member.WindProfileArea, member.WindDeckArea,
                        out decimal x, out decimal y);
                    AddHorizontalLoad(totalLoads, nodes[member.NodeA], x / 2m);
                    AddHorizontalLoad(totalLoads, nodes[member.NodeB], x / 2m);
                    AddVerticalLoad(totalLoads, nodes[member.NodeA], y / 2m);
                    AddVerticalLoad(totalLoads, nodes[member.NodeB], y / 2m);
                    environmentalDeckWeights[index] = -y;
                }
            }
            if (loads == null && singleKg > 0f)
            {
                // Legacy samples retain their original decimal positions exactly.
                decimal progress = (decimal)sampleIndex / (sampleCount - 1);
                ApplyRoadPointLoad(totalLoads, nodes, members, roadMemberIndices, bendingLoads,
                    roadMinX + (roadMaxX - roadMinX) * progress, QuantizeMass(singleKg) * Gravity);
            }
            else if (loads != null)
            {
                if (loads.Count > 4) throw new ArgumentException("A live-load test supports at most four vehicles.", nameof(loads));
                for (int index = 0; index < loads.Count; index++)
                {
                    PointLoad load = loads[index];
                    if (float.IsNaN(load.WorldX) || float.IsInfinity(load.WorldX) ||
                        float.IsNaN(load.WeightKg) || float.IsInfinity(load.WeightKg) || load.WeightKg < 0f || load.WeightKg > 1000000000f)
                        throw new ArgumentException("Live-load positions must be finite and weights must be between zero and 1,000,000,000 kg.", nameof(loads));
                    if (load.WeightKg <= 0f) continue;
                    decimal x = QuantizeMetres(Mathf.Clamp(load.WorldX, (float)roadMinX, (float)roadMaxX));
                    x = DecimalMax(roadMinX, DecimalMin(roadMaxX, x));
                    decimal mass = QuantizeMass(load.WeightKg);
                    ApplyRoadPointLoad(totalLoads, nodes, members, roadMemberIndices, bendingLoads,
                        x, mass * (Gravity - environment.AccelerationY), mass * environment.AccelerationX);
                }
            }
            decimal[] totalForces = SolveTensionOnly(nodes, members, degreeCount, totalLoads, activeSystems,
                out bool sampleStable, out Bar unsupportedRoad);
            if (totalForces == null) return null;
            if (bendingLoads.Count > 1)
                bendingLoads.Sort((left, right) => left.Fraction.CompareTo(right.Fraction));
            sample.UnsupportedRoad = unsupportedRoad;
            float peakDisplayed = 0f, peakStructural = 0f;
            for (int memberIndex = 0; memberIndex < members.Count; memberIndex++)
            {
                MemberData member = members[memberIndex];
                decimal totalForce = totalForces[memberIndex];
                bool tension = member.IsRope ? totalForce > 0m : totalForce >= 0m;
                decimal limit = tension ? member.TensionLimit : member.CompressionLimit;
                decimal structuralRatio = limit > 0m ? DecimalAbs(totalForce) / limit : 0m;
                decimal displayedForce = displayLiveLoadOnly
                    ? DecimalAbs(totalForce - deadForces[memberIndex]) : DecimalAbs(totalForce);
                decimal displayedRatio = limit > 0m ? displayedForce / limit : 0m;
                if (member.IsRope && !tension) { displayedRatio = 0m; structuralRatio = 0m; }
                decimal bendingRatio = 0m;
                decimal displayedBending = 0m;
                if (member.BendingMomentLimit > 0m)
                {
                    decimal horizontalLength = DecimalAbs(member.Length * member.Cos);
                    decimal environmentalWeight = environment.IsActive ? environmentalDeckWeights[memberIndex] : 0m;
                    decimal totalDistributedWeight = member.MassKg * Gravity + environmentalWeight;
                    bendingRatio = CalculatePeakBendingMoment(memberIndex, bendingLoads, totalDistributedWeight, horizontalLength)
                        / member.BendingMomentLimit;
                    displayedBending = displayLiveLoadOnly
                        ? CalculatePeakBendingMoment(memberIndex, bendingLoads, environmentalWeight, horizontalLength)
                            / member.BendingMomentLimit
                        : bendingRatio;
                }
                sample.SetStress(memberIndex, structuralRatio, bendingRatio, displayedRatio, displayedBending, tension);
                peakDisplayed = Mathf.Max(peakDisplayed, sample.DisplayedRatios[memberIndex]);
                peakStructural = Mathf.Max(peakStructural, sample.StructuralRatios[memberIndex]);
            }
            current.SetCurrentState(peakDisplayed, peakStructural, deadLoadStable && sampleStable);
            return current;
        }
        return new PreparedAnalysis((loads, environment) => EvaluateCurrent(loads, 0, 0, 0f, environment),
            (index, count, kg) => EvaluateCurrent(null, index, count, kg, default), (float)roadMinX, (float)roadMaxX);
    }

    private static List<Point> GetCanonicalPoints(
        IList<Point> sourcePoints,
        bool usePreSimulationPositions)
    {
        List<Point> points = new List<Point>(sourcePoints.Count);
        HashSet<Point> seen = new HashSet<Point>();
        for (int i = 0; i < sourcePoints.Count; i++)
        {
            Point point = sourcePoints[i];
            if (point != null && seen.Add(point)) points.Add(point);
        }

        points.Sort((a, b) => ComparePoints(a, b, usePreSimulationPositions));
        return points;
    }

    private static List<Bar> GetCanonicalBars(
        IList<Bar> sourceBars,
        bool usePreSimulationPositions)
    {
        List<Bar> bars = new List<Bar>(sourceBars.Count);
        HashSet<Bar> seen = new HashSet<Bar>();
        for (int i = 0; i < sourceBars.Count; i++)
        {
            Bar bar = sourceBars[i];
            if (bar != null && seen.Add(bar)) bars.Add(bar);
        }

        bars.Sort((a, b) => CompareBars(a, b, usePreSimulationPositions));
        return bars;
    }

    private static int CompareBars(Bar a, Bar b, bool usePreSimulationPositions)
    {
        if (ReferenceEquals(a, b)) return 0;

        Point aFirst = a.startPoint;
        Point aSecond = a.endPoint;
        if (ComparePoints(aFirst, aSecond, usePreSimulationPositions) > 0)
        {
            aFirst = a.endPoint;
            aSecond = a.startPoint;
        }

        Point bFirst = b.startPoint;
        Point bSecond = b.endPoint;
        if (ComparePoints(bFirst, bSecond, usePreSimulationPositions) > 0)
        {
            bFirst = b.endPoint;
            bSecond = b.startPoint;
        }

        int comparison = ComparePoints(aFirst, bFirst, usePreSimulationPositions);
        if (comparison != 0) return comparison;
        comparison = ComparePoints(aSecond, bSecond, usePreSimulationPositions);
        if (comparison != 0) return comparison;

        string aMaterialId = a.materialData != null ? a.materialData.Id : string.Empty;
        string bMaterialId = b.materialData != null ? b.materialData.Id : string.Empty;
        return string.Compare(aMaterialId, bMaterialId, StringComparison.Ordinal);
    }

    private static int ComparePoints(Point a, Point b, bool usePreSimulationPositions)
    {
        if (ReferenceEquals(a, b)) return 0;
        if (a == null) return -1;
        if (b == null) return 1;

        Vector3 aPosition = usePreSimulationPositions ? a.preSimPos : a.transform.position;
        Vector3 bPosition = usePreSimulationPositions ? b.preSimPos : b.transform.position;
        int comparison = QuantizeMetres(aPosition.x).CompareTo(QuantizeMetres(bPosition.x));
        if (comparison != 0) return comparison;
        comparison = QuantizeMetres(aPosition.y).CompareTo(QuantizeMetres(bPosition.y));
        if (comparison != 0) return comparison;
        comparison = QuantizeMetres(aPosition.z).CompareTo(QuantizeMetres(bPosition.z));
        if (comparison != 0) return comparison;

        // At equal coordinates, authored supports sort before runtime joints.
        // Valid bridges merge coincident runtime joints, so no transient Unity
        // instance ID is used as a cross-run tie breaker.
        return b.IsScenePlacedAnchor.CompareTo(a.IsScenePlacedAnchor);
    }

    private static void EnqueueNode(int index, bool[] included, Queue<int> queue)
    {
        if (included[index]) return;
        included[index] = true;
        queue.Enqueue(index);
    }

    private static decimal[] SolveTensionOnly(
        List<NodeData> nodes,
        List<MemberData> members,
        int degreeCount,
        decimal[] loads,
        Dictionary<string, ActiveSystem> activeSystems,
        out bool isStable,
        out Bar unsupportedRoad)
    {
        unsupportedRoad = null;
        if (degreeCount == 0)
        {
            isStable = true;
            return new decimal[members.Count];
        }
        bool[] slack = new bool[members.Count];
        int ropeCount = 0;
        for (int i = 0; i < members.Count; i++)
            if (members[i].IsRope) ropeCount++;

        // Removing a compressed rope can make another rope slack. Each pass
        // removes at least one, so this terminates after at most ropeCount + 1
        // solves. This intentionally errs on the safe side for ambiguous ropes.
        for (int pass = 0; pass <= ropeCount; pass++)
        {
            char[] keyChars = new char[ropeCount];
            int ropeIndex = 0;
            for (int i = 0; i < members.Count; i++)
                if (members[i].IsRope) keyChars[ropeIndex++] = slack[i] ? '0' : '1';
            string key = new string(keyChars);

            if (!activeSystems.TryGetValue(key, out ActiveSystem system))
            {
                decimal[,] stiffness = new decimal[degreeCount, degreeCount];
                for (int i = 0; i < members.Count; i++)
                    if (!slack[i]) AddMemberStiffness(stiffness, nodes, members[i]);

                // Test the actual structure before the small numerical support
                // added below. A bridge relying on a slack cable is a mechanism.
                bool stable = HasFullRank(stiffness);
                decimal largestDiagonal = 0m;
                for (int i = 0; i < degreeCount; i++)
                    largestDiagonal = DecimalMax(largestDiagonal, DecimalAbs(stiffness[i, i]));
                decimal regularization = largestDiagonal > 0m
                    ? largestDiagonal / 1000000000m : 0.000001m;
                for (int i = 0; i < degreeCount; i++) stiffness[i, i] += regularization;
                if (!TryFactor(stiffness, out decimal[,] factor, out int[] pivots))
                {
                    isStable = false;
                    return null;
                }

                system = new ActiveSystem
                {
                    Factor = factor,
                    Pivots = pivots,
                    IsStructurallyStable = stable,
                    Regularization = regularization
                };
                // A convoy can visit more active sets than a sampled single load.
                // Keep cached factors bounded even within one long slack-rope solve.
                if (activeSystems.Count >= 64) activeSystems.Clear();
                activeSystems.Add(key, system);
            }

            decimal[] displacements = Solve(system.Factor, system.Pivots, loads);
            decimal[] forces = CalculateMemberForces(nodes, members, displacements);
            bool removedRope = false;
            for (int i = 0; i < members.Count; i++)
            {
                if (!members[i].IsRope) continue;
                if (slack[i])
                    forces[i] = 0m;
                else if (forces[i] < 0m)
                {
                    slack[i] = true;
                    forces[i] = 0m;
                    removedRope = true;
                }
            }

            if (removedRope) continue;
            isStable = system.IsStructurallyStable;
            if (!isStable)
                unsupportedRoad = FindUnsupportedRoad(nodes, members, loads, displacements, system.Regularization);
            return forces;
        }

        isStable = false;
        return null;
    }

    private static Bar FindUnsupportedRoad(List<NodeData> nodes, List<MemberData> members,
        decimal[] loads, decimal[] displacements, decimal regularization)
    {
        decimal largestVerticalLoad = 0m;
        foreach (NodeData node in nodes)
            if (node.YDegree >= 0) largestVerticalLoad = DecimalMax(largestVerticalLoad, -loads[node.YDegree]);

        // The regularization acts as a weak spring at every free degree of freedom.
        // Its upward reaction identifies downward load that the actual members cannot carry.
        // Ignore small numerical reactions and mechanisms away from the roadway.
        decimal largestUnsupportedLoad = DecimalMax(1m, largestVerticalLoad / 100m);
        Bar unsupportedRoad = null;
        foreach (MemberData member in members)
        {
            if (!member.IsRoad) continue;
            int startY = nodes[member.NodeA].YDegree;
            int endY = nodes[member.NodeB].YDegree;
            decimal startReaction = startY >= 0 ? DecimalMax(0m, -displacements[startY] * regularization) : 0m;
            decimal endReaction = endY >= 0 ? DecimalMax(0m, -displacements[endY] * regularization) : 0m;
            decimal unsupportedLoad = startReaction + endReaction;
            if (unsupportedLoad <= largestUnsupportedLoad) continue;
            largestUnsupportedLoad = unsupportedLoad;
            unsupportedRoad = member.Bar;
        }
        return unsupportedRoad;
    }

    private static bool HasFullRank(decimal[,] source)
    {
        int size = source.GetLength(0);
        if (size == 0) return false;

        decimal[,] matrix = (decimal[,])source.Clone();
        decimal largestEntry = 0m;
        for (int row = 0; row < size; row++)
        {
            for (int column = 0; column < size; column++)
                largestEntry = DecimalMax(largestEntry, DecimalAbs(matrix[row, column]));
        }

        if (largestEntry <= MinimumPivot) return false;
        decimal rankTolerance = DecimalMax(MinimumPivot, largestEntry / 10000000000m);
        int pivotRow = 0;

        for (int column = 0; column < size && pivotRow < size; column++)
        {
            int bestRow = pivotRow;
            decimal bestMagnitude = DecimalAbs(matrix[bestRow, column]);
            for (int row = pivotRow + 1; row < size; row++)
            {
                decimal magnitude = DecimalAbs(matrix[row, column]);
                if (magnitude <= bestMagnitude) continue;
                bestMagnitude = magnitude;
                bestRow = row;
            }

            if (bestMagnitude <= rankTolerance) continue;

            if (bestRow != pivotRow)
            {
                for (int swapColumn = column; swapColumn < size; swapColumn++)
                {
                    decimal temporary = matrix[pivotRow, swapColumn];
                    matrix[pivotRow, swapColumn] = matrix[bestRow, swapColumn];
                    matrix[bestRow, swapColumn] = temporary;
                }
            }

            for (int row = pivotRow + 1; row < size; row++)
            {
                if (DecimalAbs(matrix[row, column]) <= rankTolerance) continue;
                decimal factor = matrix[row, column] / matrix[pivotRow, column];
                for (int eliminationColumn = column; eliminationColumn < size; eliminationColumn++)
                    matrix[row, eliminationColumn] -= factor * matrix[pivotRow, eliminationColumn];
            }

            pivotRow++;
        }

        return pivotRow == size;
    }

    private static void AddMemberStiffness(decimal[,] matrix, List<NodeData> nodes, MemberData member)
    {
        decimal coefficient = member.AxialRigidity / member.Length;
        decimal c = member.Cos;
        decimal s = member.Sin;
        decimal cc = coefficient * c * c;
        decimal cs = coefficient * c * s;
        decimal ss = coefficient * s * s;
        decimal[,] local =
        {
            { cc, cs, -cc, -cs },
            { cs, ss, -cs, -ss },
            { -cc, -cs, cc, cs },
            { -cs, -ss, cs, ss }
        };
        int[] degrees =
        {
            nodes[member.NodeA].XDegree, nodes[member.NodeA].YDegree,
            nodes[member.NodeB].XDegree, nodes[member.NodeB].YDegree
        };

        for (int row = 0; row < 4; row++)
        {
            if (degrees[row] < 0) continue;
            for (int column = 0; column < 4; column++)
            {
                if (degrees[column] < 0) continue;
                matrix[degrees[row], degrees[column]] += local[row, column];
            }
        }
    }

    private static void ApplyRoadPointLoad(
        decimal[] loads,
        List<NodeData> nodes,
        List<MemberData> members,
        List<int> roadIndices,
        List<RoadPointLoad> bendingLoads,
        decimal loadX,
        decimal weight, decimal horizontalForce = 0m)
    {
        int selectedIndex = roadIndices[0];
        decimal selectedDistance = decimal.MaxValue;
        foreach (int index in roadIndices)
        {
            MemberData member = members[index];
            decimal xA = nodes[member.NodeA].X;
            decimal xB = nodes[member.NodeB].X;
            decimal minX = DecimalMin(xA, xB);
            decimal maxX = DecimalMax(xA, xB);
            decimal distance = loadX < minX ? minX - loadX : loadX > maxX ? loadX - maxX : 0m;
            if (distance < selectedDistance)
            {
                selectedDistance = distance;
                selectedIndex = index;
            }
        }

        MemberData selected = members[selectedIndex];
        NodeData nodeA = nodes[selected.NodeA];
        NodeData nodeB = nodes[selected.NodeB];
        decimal dx = nodeB.X - nodeA.X;
        decimal fraction = dx == 0m ? 0.5m : DecimalClamp((loadX - nodeA.X) / dx, 0m, 1m);
        if (horizontalForce != 0m)
        {
            AddHorizontalLoad(loads, nodeA, horizontalForce * (1m - fraction));
            AddHorizontalLoad(loads, nodeB, horizontalForce * fraction);
        }
        AddVerticalLoad(loads, nodeA, -weight * (1m - fraction));
        AddVerticalLoad(loads, nodeB, -weight * fraction);
        if (selected.BendingMomentLimit > 0m && dx != 0m && fraction > 0m && fraction < 1m && weight > 0m)
            bendingLoads.Add(new RoadPointLoad(selectedIndex, fraction, weight));
    }

    // The truss receives the same support reactions as before. Between the
    // joints, treat each deck panel as a simply supported beam. Using the
    // horizontal lever arm accounts for vertical loads on a sloped panel.
    private static decimal CalculatePeakBendingMoment(int memberIndex, List<RoadPointLoad> pointLoads,
        decimal distributedWeight, decimal horizontalLength)
    {
        decimal shear = distributedWeight / 2m;
        foreach (RoadPointLoad load in pointLoads)
            if (load.MemberIndex == memberIndex) shear += load.Weight * (1m - load.Fraction);

        decimal moment = 0m;
        decimal peak = 0m;
        decimal previousFraction = 0m;
        foreach (RoadPointLoad load in pointLoads)
        {
            if (load.MemberIndex != memberIndex) continue;
            AdvanceTo(load.Fraction);
            shear -= load.Weight;
        }
        AdvanceTo(1m);
        return peak * horizontalLength;

        void AdvanceTo(decimal fraction)
        {
            decimal interval = fraction - previousFraction;
            // Moment peaks at zero shear or at an interval boundary.
            decimal peakOffset = distributedWeight != 0m
                ? DecimalClamp(shear / distributedWeight, 0m, interval)
                : interval;
            peak = DecimalMax(peak, DecimalAbs(moment + (shear - distributedWeight * peakOffset / 2m) * peakOffset));
            moment += (shear - distributedWeight * interval / 2m) * interval;
            peak = DecimalMax(peak, DecimalAbs(moment));
            shear -= distributedWeight * interval;
            previousFraction = fraction;
        }
    }

    private static void AddVerticalLoad(decimal[] loads, NodeData node, decimal force)
    {
        if (node.YDegree >= 0) loads[node.YDegree] += force;
    }

    private static void AddHorizontalLoad(decimal[] loads, NodeData node, decimal force)
    {
        if (node.XDegree >= 0) loads[node.XDegree] += force;
    }

    private static decimal[] CalculateMemberForces(
        List<NodeData> nodes,
        List<MemberData> members,
        decimal[] displacements)
    {
        decimal[] forces = new decimal[members.Count];
        for (int i = 0; i < members.Count; i++)
        {
            MemberData member = members[i];
            NodeData a = nodes[member.NodeA];
            NodeData b = nodes[member.NodeB];
            decimal uxA = a.XDegree >= 0 ? displacements[a.XDegree] : 0m;
            decimal uyA = a.YDegree >= 0 ? displacements[a.YDegree] : 0m;
            decimal uxB = b.XDegree >= 0 ? displacements[b.XDegree] : 0m;
            decimal uyB = b.YDegree >= 0 ? displacements[b.YDegree] : 0m;
            decimal extension = member.Cos * (uxB - uxA) + member.Sin * (uyB - uyA);
            forces[i] = member.AxialRigidity / member.Length * extension;
        }
        return forces;
    }

    private static bool TryFactor(decimal[,] source, out decimal[,] factor, out int[] pivots)
    {
        int size = source.GetLength(0);
        factor = (decimal[,])source.Clone();
        pivots = new int[size];
        for (int i = 0; i < size; i++) pivots[i] = i;

        for (int column = 0; column < size; column++)
        {
            int pivotRow = column;
            decimal pivotMagnitude = DecimalAbs(factor[column, column]);
            for (int row = column + 1; row < size; row++)
            {
                decimal magnitude = DecimalAbs(factor[row, column]);
                if (magnitude > pivotMagnitude)
                {
                    pivotMagnitude = magnitude;
                    pivotRow = row;
                }
            }
            if (pivotMagnitude <= MinimumPivot) return false;

            if (pivotRow != column)
            {
                for (int j = 0; j < size; j++)
                {
                    decimal temporary = factor[column, j];
                    factor[column, j] = factor[pivotRow, j];
                    factor[pivotRow, j] = temporary;
                }
                int pivot = pivots[column];
                pivots[column] = pivots[pivotRow];
                pivots[pivotRow] = pivot;
            }

            for (int row = column + 1; row < size; row++)
            {
                factor[row, column] /= factor[column, column];
                for (int j = column + 1; j < size; j++)
                    factor[row, j] -= factor[row, column] * factor[column, j];
            }
        }
        return true;
    }

    private static decimal[] Solve(decimal[,] factor, int[] pivots, decimal[] rightHandSide)
    {
        int size = rightHandSide.Length;
        decimal[] solution = new decimal[size];
        for (int i = 0; i < size; i++) solution[i] = rightHandSide[pivots[i]];

        for (int row = 0; row < size; row++)
            for (int column = 0; column < row; column++)
                solution[row] -= factor[row, column] * solution[column];

        for (int row = size - 1; row >= 0; row--)
        {
            for (int column = row + 1; column < size; column++)
                solution[row] -= factor[row, column] * solution[column];
            solution[row] /= factor[row, row];
        }
        return solution;
    }

    private static decimal QuantizeMetres(float metres)
    {
        return decimal.Round((decimal)metres * MillimetresPerMetre, 0, MidpointRounding.AwayFromZero) /
               MillimetresPerMetre;
    }

    private static decimal QuantizeNewtons(float newtons)
    {
        return decimal.Round((decimal)Mathf.Max(0f, newtons), 0, MidpointRounding.AwayFromZero);
    }

    private static decimal QuantizeMass(float kilograms)
    {
        return decimal.Round((decimal)Mathf.Max(0f, kilograms), 3, MidpointRounding.AwayFromZero);
    }

    private static float RatioToFloat(decimal ratio)
    {
        decimal rounded = decimal.Round(DecimalMax(0m, ratio), 3, MidpointRounding.AwayFromZero);
        if (rounded >= MaxStressRatio) return MaxStressRatio;
        return (float)rounded;
    }

    private static decimal DecimalSqrt(decimal value)
    {
        if (value <= 0m) return 0m;
        decimal estimate = value > 1m ? value : 1m;
        for (int i = 0; i < 24; i++) estimate = (estimate + value / estimate) / 2m;
        return estimate;
    }

    private static decimal DecimalAbs(decimal value) => value < 0m ? -value : value;
    private static decimal DecimalMin(decimal a, decimal b) => a < b ? a : b;
    private static decimal DecimalMax(decimal a, decimal b) => a > b ? a : b;
    private static decimal DecimalClamp(decimal value, decimal minimum, decimal maximum) =>
        value < minimum ? minimum : value > maximum ? maximum : value;
}
