using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Deterministic, quasi-static 2D truss analysis used for gameplay stress.
/// Geometry and material inputs are quantized before decimal arithmetic, so the
/// same bridge and contract produce the same samples independently of PhysX.
/// </summary>
public static class DeterministicBridgeStressSolver
{
    public sealed class Result
    {
        private readonly Dictionary<Bar, int> barIndices;

        internal Result(Bar[] bars, Sample[] samples, float peakDisplayed, float peakStructural)
        {
            Bars = bars;
            Samples = samples;
            PeakDisplayedStress = peakDisplayed;
            PeakStructuralStress = peakStructural;
            barIndices = new Dictionary<Bar, int>(bars.Length);
            for (int i = 0; i < bars.Length; i++)
                if (bars[i] != null) barIndices[bars[i]] = i;
        }

        public Bar[] Bars { get; }
        public Sample[] Samples { get; }
        public float PeakDisplayedStress { get; }
        public float PeakStructuralStress { get; }
        public bool IsValid => Bars.Length > 0 && Samples.Length > 0;

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
    }

    public sealed class Sample
    {
        internal Sample(int barCount)
        {
            DisplayedRatios = new float[barCount];
            StructuralRatios = new float[barCount];
            IsTension = new bool[barCount];
        }

        public float[] DisplayedRatios { get; }
        public float[] StructuralRatios { get; }
        public bool[] IsTension { get; }
    }

    private sealed class NodeData
    {
        public Point Point;
        public decimal X;
        public decimal Y;
        public bool IsFixed;
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
        public decimal MassKg;
        public bool IsRope;
        public bool IsRoad;
    }

    private const decimal Gravity = 9.81m;
    private const decimal MillimetresPerMetre = 1000m;
    private const decimal MinimumPivot = 0.000000000001m;

    public static Result Analyze(
        IList<Point> sourcePoints,
        IList<Bar> sourceBars,
        float liveLoadKg,
        bool displayLiveLoadOnly,
        int requestedSampleCount)
    {
        if (sourcePoints == null || sourceBars == null) return null;

        List<NodeData> nodes = new List<NodeData>();
        Dictionary<Point, int> nodeIndices = new Dictionary<Point, int>();
        for (int i = 0; i < sourcePoints.Count; i++)
        {
            Point point = sourcePoints[i];
            if (point == null || nodeIndices.ContainsKey(point)) continue;

            Vector3 position = point.transform.position;
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
        for (int i = 0; i < sourceBars.Count; i++)
        {
            Bar bar = sourceBars[i];
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
            // BarCreator places player pier bases at pierBaseY and deliberately
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
                MassKg = QuantizeMass((float)length * material.GetPlacedMassPerMeter()),
                IsRope = material.isRope,
                IsRoad = material.isRoad
            });
        }

        if (nodes.Count < 2 || members.Count == 0) return null;

        int degreeCount = 0;
        foreach (NodeData node in nodes)
        {
            if (node.IsFixed) continue;
            node.XDegree = degreeCount++;
            node.YDegree = degreeCount++;
        }
        if (degreeCount == 0) return null;

        decimal[,] stiffness = new decimal[degreeCount, degreeCount];
        foreach (MemberData member in members)
            AddMemberStiffness(stiffness, nodes, member);

        // A tiny deterministic diagonal keeps mechanisms solvable and produces a
        // very high utilization instead of a platform-dependent singular failure.
        decimal largestDiagonal = 0m;
        for (int i = 0; i < degreeCount; i++)
            largestDiagonal = DecimalMax(largestDiagonal, DecimalAbs(stiffness[i, i]));
        decimal regularization = largestDiagonal > 0m ? largestDiagonal / 1000000000m : 0.000001m;
        for (int i = 0; i < degreeCount; i++) stiffness[i, i] += regularization;

        if (!TryFactor(stiffness, out decimal[,] factor, out int[] pivots)) return null;

        decimal[] deadLoads = new decimal[degreeCount];
        foreach (MemberData member in members)
        {
            decimal endpointWeight = member.MassKg * Gravity / 2m;
            AddVerticalLoad(deadLoads, nodes[member.NodeA], -endpointWeight);
            AddVerticalLoad(deadLoads, nodes[member.NodeB], -endpointWeight);
        }

        decimal[] deadDisplacements = Solve(factor, pivots, deadLoads);
        decimal[] deadForces = CalculateMemberForces(nodes, members, deadDisplacements);

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

        int sampleCount = Mathf.Clamp(requestedSampleCount, 3, 201);
        Sample[] samples = new Sample[sampleCount];
        float peakDisplayed = 0f;
        float peakStructural = 0f;
        decimal vehicleWeight = QuantizeMass(liveLoadKg) * Gravity;

        for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            decimal progress = (decimal)sampleIndex / (sampleCount - 1);
            decimal loadX = roadMinX + (roadMaxX - roadMinX) * progress;
            decimal[] totalLoads = (decimal[])deadLoads.Clone();
            ApplyRoadPointLoad(totalLoads, nodes, members, roadMemberIndices, loadX, vehicleWeight);

            decimal[] displacements = Solve(factor, pivots, totalLoads);
            decimal[] totalForces = CalculateMemberForces(nodes, members, displacements);
            Sample sample = new Sample(members.Count);

            for (int memberIndex = 0; memberIndex < members.Count; memberIndex++)
            {
                MemberData member = members[memberIndex];
                decimal totalForce = totalForces[memberIndex];
                bool tension = totalForce >= 0m;
                decimal limit = tension ? member.TensionLimit : member.CompressionLimit;
                decimal structuralRatio = limit > 0m ? DecimalAbs(totalForce) / limit : 0m;

                decimal displayedForce = displayLiveLoadOnly
                    ? DecimalAbs(totalForce - deadForces[memberIndex])
                    : DecimalAbs(totalForce);
                decimal displayedRatio = limit > 0m ? displayedForce / limit : 0m;

                // Ropes are tension-only members. Compression is neither displayed
                // nor allowed to trigger deterministic structural failure.
                if (member.IsRope && !tension)
                {
                    displayedRatio = 0m;
                    structuralRatio = 0m;
                }

                sample.DisplayedRatios[memberIndex] = RatioToFloat(displayedRatio);
                sample.StructuralRatios[memberIndex] = RatioToFloat(structuralRatio);
                sample.IsTension[memberIndex] = tension;
                peakDisplayed = Mathf.Max(peakDisplayed, sample.DisplayedRatios[memberIndex]);
                peakStructural = Mathf.Max(peakStructural, sample.StructuralRatios[memberIndex]);
            }

            samples[sampleIndex] = sample;
        }

        Bar[] resultBars = new Bar[members.Count];
        for (int i = 0; i < members.Count; i++) resultBars[i] = members[i].Bar;
        return new Result(resultBars, samples, peakDisplayed, peakStructural);
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
        decimal loadX,
        decimal weight)
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
        AddVerticalLoad(loads, nodeA, -weight * (1m - fraction));
        AddVerticalLoad(loads, nodeB, -weight * fraction);
    }

    private static void AddVerticalLoad(decimal[] loads, NodeData node, decimal force)
    {
        if (node.YDegree >= 0) loads[node.YDegree] += force;
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
        if (rounded >= 1000000m) return 1000000f;
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
