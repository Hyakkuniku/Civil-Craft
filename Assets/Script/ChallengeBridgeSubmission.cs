using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

// Logical, immutable-on-acceptance graphs only: no colliders, rewards, save IDs,
// client-provided costs, prefab paths or ownership claims travel over the wire.
public sealed class ChallengeBridgeSubmission
{
    public readonly List<ChallengeSubmittedNode> Nodes = new List<ChallengeSubmittedNode>();
    public readonly List<ChallengeSubmittedBar> Bars = new List<ChallengeSubmittedBar>();
}

public struct ChallengeSubmittedNode { public Vector3 Position; public int Anchor; }
public struct ChallengeSubmittedBar { public int Start, End; public string Material; }
public enum ChallengeSubmissionError
{
    None, InvalidPacket, WrongChallenge, AlreadySubmitted, EmptyBridge, InvalidAnchors,
    InvalidNodes, InvalidMembers, InvalidMaterial, MaterialLimit, OverBudget, Busy
}

public static class ChallengeBridgeSubmissionCodec
{
    public const int Version = 1;
    public const int MaxNodes = 4096, MaxBars = 8192, MaxPacketBytes = 1024 * 1024;
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    public static byte[] Encode(ChallengeBridgeSubmission graph)
    {
        if (graph == null || graph.Nodes.Count > MaxNodes || graph.Bars.Count > MaxBars)
            throw new InvalidDataException("Bridge exceeds the submission size limit.");
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream, StrictUtf8, true))
        {
            writer.Write(Version); writer.Write(graph.Nodes.Count); writer.Write(graph.Bars.Count);
            foreach (var node in graph.Nodes)
            {
                writer.Write(node.Position.x); writer.Write(node.Position.y); writer.Write(node.Position.z); writer.Write(node.Anchor);
            }
            foreach (var bar in graph.Bars)
            {
                byte[] id = StrictUtf8.GetBytes(bar.Material ?? string.Empty);
                if (id.Length == 0 || id.Length > 128) throw new InvalidDataException("Invalid material identifier.");
                writer.Write(bar.Start); writer.Write(bar.End); writer.Write((ushort)id.Length); writer.Write(id);
            }
            if (stream.Length > MaxPacketBytes) throw new InvalidDataException("Bridge exceeds the submission size limit.");
            return stream.ToArray();
        }
    }

    public static ChallengeBridgeSubmission Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12 || data.Length > MaxPacketBytes) throw new InvalidDataException("Invalid submission length.");
        using (var stream = new MemoryStream(data.ToArray(), false))
        using (var reader = new BinaryReader(stream, StrictUtf8))
        {
            if (reader.ReadInt32() != Version) throw new InvalidDataException("Unsupported submission version.");
            int nodes = reader.ReadInt32(), bars = reader.ReadInt32();
            if (nodes < 0 || nodes > MaxNodes || bars < 0 || bars > MaxBars ||
                (long)nodes * 16 + (long)bars * 11 > stream.Length - stream.Position)
                throw new InvalidDataException("Invalid submission counts.");
            var graph = new ChallengeBridgeSubmission();
            for (int i = 0; i < nodes; i++) graph.Nodes.Add(new ChallengeSubmittedNode
            { Position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()), Anchor = reader.ReadInt32() });
            for (int i = 0; i < bars; i++)
            {
                int start = reader.ReadInt32(), end = reader.ReadInt32();
                int length = reader.ReadUInt16();
                if (length == 0 || length > 128 || length > stream.Length - stream.Position)
                    throw new InvalidDataException("Invalid material identifier length.");
                graph.Bars.Add(new ChallengeSubmittedBar
                { Start = start, End = end, Material = StrictUtf8.GetString(reader.ReadBytes(length)) });
            }
            if (stream.Position != stream.Length) throw new InvalidDataException("Unexpected submission trailing data.");
            return graph;
        }
    }
}

public static class ChallengeBridgeSubmissionRules
{
    // Point.Update snaps non-runtime authored points to the integer grid in
    // Play Mode. Hidden world anchors may never get that Update on a guest.
    // Competition copies and host admission must use the same placement without
    // mutating either saved world or relaxing anchor identity/position checks.
    public static Vector3 ChallengeAnchorPosition(Point anchor) => anchor.Runtime
        ? anchor.transform.position : (Vector3)Vector3Int.RoundToInt(anchor.transform.position);

    public static List<Point> GetAnchors(BuildLocation site)
    {
        var anchors = new List<Point>();
        if (site == null) return anchors;
        foreach (Point point in site.startingAnchors) if (point != null && !anchors.Contains(point)) anchors.Add(point);
        foreach (Point point in site.endingAnchors) if (point != null && !anchors.Contains(point)) anchors.Add(point);
        return anchors;
    }

    public static ChallengeBridgeSubmission Capture(ChallengeBuildWorkspace workspace)
    {
        if (workspace == null || workspace.Root == null || workspace.Location == null || workspace.Creator == null ||
            workspace.PointRoot == null || workspace.BarRoot == null ||
            workspace.PointRoot.parent != workspace.Root.transform || workspace.BarRoot.parent != workspace.Root.transform)
            throw new InvalidDataException("The private challenge workspace is missing.");
        var graph = new ChallengeBridgeSubmission();
        var indices = new Dictionary<Point, int>();
        List<Point> anchors = GetAnchors(workspace.Location);
        // Creator, auto-draw and paste put logical objects directly under these
        // containers. Material segment prefabs also contain Bar components, but
        // those nested visual meshes are NOT members and have no endpoints.
        foreach (Transform child in workspace.PointRoot)
        {
            Point point = child.GetComponent<Point>();
            if (point == null) continue;
            if (!point.gameObject.activeInHierarchy || !point.enabled) continue;
            if (point.OwnerLocation != workspace.Location) throw new InvalidDataException($"Node '{point.name}' belongs to another bridge.");
            indices.Add(point, graph.Nodes.Count);
            graph.Nodes.Add(new ChallengeSubmittedNode { Position = point.transform.position, Anchor = anchors.IndexOf(point) });
        }
        foreach (Transform child in workspace.BarRoot)
        {
            Bar bar = child.GetComponent<Bar>();
            if (bar == null) continue;
            if (!bar.gameObject.activeInHierarchy || !bar.enabled) continue;
            if (bar.OwnerLocation != workspace.Location || bar.startPoint == null || bar.endPoint == null ||
                bar.materialData == null || !indices.TryGetValue(bar.startPoint, out int start) || !indices.TryGetValue(bar.endPoint, out int end))
                throw new InvalidDataException($"Member '{bar.name}' has missing or foreign endpoints, ownership or material.");
            graph.Bars.Add(new ChallengeSubmittedBar { Start = start, End = end, Material = bar.materialData.Id });
        }
        return graph;
    }

    public static Dictionary<string, BridgeMaterialSO> CreateMaterialCatalog(ContractSO definition)
    {
        var catalog = new Dictionary<string, BridgeMaterialSO>(StringComparer.Ordinal);
        var ambiguous = new HashSet<string>();
        var candidates = new HashSet<BridgeMaterialSO>(Resources.FindObjectsOfTypeAll<BridgeMaterialSO>());
        if (definition != null && definition.allowedMaterials != null)
            foreach (var allowance in definition.allowedMaterials) if (allowance?.material != null) candidates.Add(allowance.material);
        foreach (var material in candidates)
        {
            if (material == null || string.IsNullOrWhiteSpace(material.Id)) continue;
            if (catalog.TryGetValue(material.Id, out var existing) && existing != material) ambiguous.Add(material.Id);
            else catalog[material.Id] = material;
        }
        foreach (string id in ambiguous) catalog.Remove(id);
        return catalog;
    }

    // Admission checks only data safety, authored anchors and legal materials.
    // Incomplete/weak/disconnected/over-budget construction is judged by the
    // simulation and final scoring, not rejected before either player can test it.
    // The host always prices its own material assets, never a client-provided cost.
    public static ChallengeSubmissionError Validate(ChallengeBridgeSubmission graph, BuildLocation site, ContractSO definition,
        IReadOnlyDictionary<string, BridgeMaterialSO> catalog, float pierBaseY, out float cost, float nodeDepthTolerance = 1f)
    {
        cost = 0f;
        if (graph == null || site == null || definition == null || catalog == null) return ChallengeSubmissionError.WrongChallenge;
        if (graph.Nodes.Count > ChallengeBridgeSubmissionCodec.MaxNodes || graph.Bars.Count > ChallengeBridgeSubmissionCodec.MaxBars)
            return ChallengeSubmissionError.InvalidPacket;
        if (graph.Bars.Count == 0) return ChallengeSubmissionError.EmptyBridge;
        // Match the actual builder's snap-depth tolerance instead of demanding
        // perfectly coplanar authored banks, foundations and snapped endpoints.
        float depthTolerance = Finite(nodeDepthTolerance) ? Mathf.Max(nodeDepthTolerance, 0.01f) : 1f;
        List<Point> anchors = GetAnchors(site);
        if (anchors.Count == 0 || site.startingAnchors.Count == 0 || site.endingAnchors.Count == 0)
            return ChallengeSubmissionError.InvalidAnchors;
        var presentAnchors = new HashSet<int>();
        foreach (var node in graph.Nodes)
        {
            if (!Finite(node.Position) || node.Anchor < -1 || node.Anchor >= anchors.Count) return ChallengeSubmissionError.InvalidNodes;
            if (node.Anchor >= 0)
            {
                if (!presentAnchors.Add(node.Anchor) || Vector3.Distance(node.Position, ChallengeAnchorPosition(anchors[node.Anchor])) > 0.01f)
                    return ChallengeSubmissionError.InvalidAnchors;
            }
            else
            {
                bool samePlane = false;
                foreach (Point anchor in anchors) if (Mathf.Abs(node.Position.z - ChallengeAnchorPosition(anchor).z) <= depthTolerance + 0.001f) { samePlane = true; break; }
                if (!samePlane) return ChallengeSubmissionError.InvalidNodes;
            }
        }
        if (presentAnchors.Count != anchors.Count) return ChallengeSubmissionError.InvalidAnchors;
        double totalCost = 0;
        foreach (var bar in graph.Bars)
        {
            if (bar.Start < 0 || bar.End < 0 || bar.Start >= graph.Nodes.Count || bar.End >= graph.Nodes.Count || bar.Start == bar.End)
                return ChallengeSubmissionError.InvalidMembers;
            if (bar.Material == null || !catalog.TryGetValue(bar.Material, out BridgeMaterialSO material) || material == null ||
                definition.IsMaterialHidden(material)) return ChallengeSubmissionError.InvalidMaterial;
            MaterialAllowance allowance = null;
            if (definition.allowedMaterials != null && definition.allowedMaterials.Count > 0)
            {
                foreach (var allowed in definition.allowedMaterials)
                    if (allowed != null && allowed.material == material) { allowance = allowed; break; }
                if (allowance == null) return ChallengeSubmissionError.InvalidMaterial;
            }
            Vector3 start = graph.Nodes[bar.Start].Position, end = graph.Nodes[bar.End].Position;
            float length = Vector3.Distance(start, end);
            // Degenerate/non-planar members are unsafe to reconstruct. Material
            // span limits and pier placement are builder rules, not admission gates.
            if (!Finite(material.costPerMeter) || material.costPerMeter < 0f ||
                length < 0.0001f || Mathf.Abs(start.z - end.z) > depthTolerance + 0.001f)
                return ChallengeSubmissionError.InvalidMembers;
            totalCost += (double)length * material.costPerMeter * (material.isDualBeam ? 2 : 1);
        }
        if (totalCost > float.MaxValue) return ChallengeSubmissionError.InvalidMembers;
        cost = (float)totalCost;
        return ChallengeSubmissionError.None;
    }

    private static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z) &&
        Mathf.Abs(v.x) < 1000000f && Mathf.Abs(v.y) < 1000000f && Mathf.Abs(v.z) < 1000000f;
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    public static string Message(ChallengeSubmissionError error)
    {
        switch (error)
        {
            case ChallengeSubmissionError.None: return "Bridge submitted. Waiting for your opponent.";
            case ChallengeSubmissionError.EmptyBridge: return "Place bridge members before submitting.";
            case ChallengeSubmissionError.AlreadySubmitted: return "Your accepted bridge is already locked.";
            case ChallengeSubmissionError.Busy: return "Please wait briefly before submitting again.";
            case ChallengeSubmissionError.WrongChallenge: return "This challenge is no longer accepting your bridge.";
            // Only an empty design gets a design-warning banner. Validation still
            // rejects malformed/unauthorized graphs and unlocks rejected drafts;
            // the authored status card returns to its normal building instructions.
            default: return string.Empty;
        }
    }
}
