using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Fusion;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Runs inside the existing isolated preview-scene validator, never a saved world.</summary>
public static class ChallengeSubmissionValidation
{
    public static void Run(Transform fixture, StringBuilder report)
    {
        ValidatePolicies(report);
        ValidateGraphs(fixture, report);
        ValidateAuthoredMaterialCapture(fixture, report);
        ValidateAuthoredUI(report);
    }

    public static void ValidatePolicies(StringBuilder report)
    {
        PlayerRef host = PlayerRef.FromIndex(0), guest = PlayerRef.FromIndex(1), outsider = PlayerRef.FromIndex(2);
        int count = 0;
        foreach (MultiplayerChallengePhase phase in Enum.GetValues(typeof(MultiplayerChallengePhase)))
        foreach (PlayerRef player in new[] { host, guest, outsider })
        foreach (int revision in new[] { 6, 7, 8 })
        for (int arrived = 0; arrived < 4; arrived++)
        for (int ready = 0; ready < 4; ready++)
        for (int built = 0; built < 4; built++)
        for (int submitted = 0; submitted < 4; submitted++)
        {
            var state = new MultiplayerChallengeState
            {
                Phase = phase, Revision = 7, Guest = guest,
                HostArrived = (arrived & 1) != 0, GuestArrived = (arrived & 2) != 0,
                HostReady = (ready & 1) != 0, GuestReady = (ready & 2) != 0,
                HostBuilding = (built & 1) != 0, GuestBuilding = (built & 2) != 0,
                HostSubmitted = (submitted & 1) != 0, GuestSubmitted = (submitted & 2) != 0
            };
            bool allowed = phase == MultiplayerChallengePhase.Building && player != outsider && revision == 7 &&
                arrived == 3 && ready == 3 && built == 3 && (player == host ? (submitted & 1) == 0 : (submitted & 2) == 0);
            Check(MultiplayerChallengeRules.CanSubmit(state, player, host, revision) == allowed &&
                MultiplayerChallengeRules.TryAcceptSubmission(ref state, player, host, revision, 100f, 3) == allowed,
                "Submission gate accepted an invalid role, revision, phase, arrival or prepared workspace.");
            bool expectedHost = (submitted & 1) != 0 || allowed && player == host;
            bool expectedGuest = (submitted & 2) != 0 || allowed && player == guest;
            Check((bool)state.HostSubmitted == expectedHost && (bool)state.GuestSubmitted == expectedGuest &&
                state.Phase == (allowed && expectedHost && expectedGuest ? MultiplayerChallengePhase.SubmissionsReady : phase) &&
                state.HostSubmittedCost == (allowed && player == host ? 100f : 0f) &&
                state.GuestSubmittedCost == (allowed && player == guest ? 100f : 0f), "Submission changed another player's bridge or advanced early.");
            count += 2;
        }
        Pass(report, $"{count:N0} submission owner/phase/revision assertions pass; a player locks only their own bridge.");
        var cycle = new MultiplayerChallengeState
        {
            Phase = MultiplayerChallengePhase.Building, Revision = 9, Guest = guest,
            HostArrived = true, GuestArrived = true, HostReady = true, GuestReady = true, HostBuilding = true, GuestBuilding = true
        };
        Check(MultiplayerChallengeRules.TryAcceptSubmission(ref cycle, guest, host, 9, 80, 2) &&
            cycle.Phase == MultiplayerChallengePhase.Building && !MultiplayerChallengeRules.CanSubmit(cycle, guest, host, 9) &&
            MultiplayerChallengeRules.CanSubmit(cycle, host, host, 9), "First submission incorrectly locked the opponent.");
        Check(!MultiplayerChallengeRules.TryAcceptSubmission(ref cycle, guest, host, 9, 1, 1) && cycle.GuestSubmittedCost == 80 &&
            !MultiplayerChallengeRules.TryAcceptSubmission(ref cycle, host, host, 8, 1, 1), "Duplicate/stale submission replaced an accepted bridge.");
        foreach (float cost in new[] { -1f, float.NaN, float.PositiveInfinity })
            Check(!MultiplayerChallengeRules.TryAcceptSubmission(ref cycle, host, host, 9, cost, 1), "Invalid cost entered authoritative state.");
        Check(MultiplayerChallengeRules.TryAcceptSubmission(ref cycle, host, host, 9, 90, 3) &&
            cycle.Phase == MultiplayerChallengePhase.SubmissionsReady && cycle.HostSubmittedCost == 90 && cycle.GuestSubmittedCost == 80 &&
            !MultiplayerChallengeRules.CanSubmit(cycle, host, host, 9), "Both submissions failed to close the editing phase.");
        Pass(report, "Guest-first submission leaves the host editing; duplicates cannot replace accepted designs; both accepted advances once.");
        Check(ChallengeBridgeSubmissionRules.Message(ChallengeSubmissionError.EmptyBridge) == "Place bridge members before submitting.",
            "The empty-bridge placement prompt was removed.");
        foreach (ChallengeSubmissionError error in new[] { ChallengeSubmissionError.InvalidPacket, ChallengeSubmissionError.InvalidAnchors,
            ChallengeSubmissionError.InvalidNodes, ChallengeSubmissionError.InvalidMembers, ChallengeSubmissionError.InvalidMaterial,
            ChallengeSubmissionError.MaterialLimit, ChallengeSubmissionError.OverBudget })
            Check(string.IsNullOrEmpty(ChallengeBridgeSubmissionRules.Message(error)), "A non-empty design still shows an invalid-bridge warning.");
        foreach (ChallengeSubmissionError error in new[] { ChallengeSubmissionError.None, ChallengeSubmissionError.AlreadySubmitted,
            ChallengeSubmissionError.Busy, ChallengeSubmissionError.WrongChallenge })
            Check(!string.IsNullOrEmpty(ChallengeBridgeSubmissionRules.Message(error)), "A submission/session status was hidden with design warnings.");
        Pass(report, "Empty submissions retain the placement prompt; all other design-validation banners are hidden. Accepted, already-locked, busy and closed-session statuses remain visible; message visibility does not bypass host authority.");
    }

    private static void ValidateGraphs(Transform fixture, StringBuilder report)
    {
        var definition = ScriptableObject.CreateInstance<ContractSO>();
        var road = ScriptableObject.CreateInstance<BridgeMaterialSO>();
        var dual = ScriptableObject.CreateInstance<BridgeMaterialSO>();
        road.name = "Validation Challenge Road"; road.costPerMeter = 10f; road.maxLength = 6f; road.isRoad = true;
        dual.name = "Validation Challenge Dual"; dual.costPerMeter = 10f; dual.maxLength = 6f; dual.isDualBeam = true;
        definition.budget = 150f;
        definition.allowedMaterials.Add(new MaterialAllowance { material = road, maxPieces = 3 });
        definition.allowedMaterials.Add(new MaterialAllowance { material = dual, maxPieces = 3 });
        BuildLocation source = Child("Submission Fixture Site", fixture).AddComponent<BuildLocation>();
        Point start = Child("Submission Start", fixture).AddComponent<Point>();
        Point end = Child("Submission End", fixture).AddComponent<Point>();
        start.transform.position = new Vector3(20, 3, -8); end.transform.position = new Vector3(35, 3, -8);
        source.startingAnchors.Add(start); source.endingAnchors.Add(end); source.activeContract = definition;
        var graph = new ChallengeBridgeSubmission();
        graph.Nodes.Add(new ChallengeSubmittedNode { Position = start.transform.position, Anchor = 0 });
        graph.Nodes.Add(new ChallengeSubmittedNode { Position = end.transform.position, Anchor = 1 });
        graph.Nodes.Add(new ChallengeSubmittedNode { Position = new Vector3(25, 3, -8), Anchor = -1 });
        graph.Nodes.Add(new ChallengeSubmittedNode { Position = new Vector3(30, 3, -8), Anchor = -1 });
        graph.Bars.Add(new ChallengeSubmittedBar { Start = 0, End = 2, Material = road.Id });
        graph.Bars.Add(new ChallengeSubmittedBar { Start = 2, End = 3, Material = road.Id });
        graph.Bars.Add(new ChallengeSubmittedBar { Start = 3, End = 1, Material = road.Id });
        var catalog = new Dictionary<string, BridgeMaterialSO> { { road.Id, road }, { dual.Id, dual } };
        byte[] packet = ChallengeBridgeSubmissionCodec.Encode(graph);
        Func<ChallengeBridgeSubmission, ChallengeSubmissionError> validate = candidate =>
            ChallengeBridgeSubmissionRules.Validate(candidate, source, definition, catalog, -10, out _);
        try
        {
            Check(validate(graph) == ChallengeSubmissionError.None &&
                ChallengeBridgeSubmissionRules.Validate(graph, source, definition, catalog, -10, out float cost) == ChallengeSubmissionError.None &&
                Mathf.Abs(cost - 150f) < 0.001f, "Valid bridge or authoritative length-based cost failed.");
            ChallengeBridgeSubmission decoded = ChallengeBridgeSubmissionCodec.Decode(packet);
            Check(decoded.Nodes.Count == 4 && decoded.Bars.Count == 3 && decoded.Nodes[2].Position == graph.Nodes[2].Position &&
                decoded.Nodes[0].Anchor == 0 && decoded.Bars[2].End == 1 && decoded.Bars[0].Material == road.Id &&
                HostWorldBridgeSnapshotCodec.SameBytes(packet, ChallengeBridgeSubmissionCodec.Encode(decoded)), "Topology packet did not round-trip.");
            Pass(report, $"Topology round trip preserves anchors, endpoints and materials; a 3-member design transfers in {packet.Length} bytes without client costs.");
            TestMutation(packet, validate, g => g.Bars.Clear(), ChallengeSubmissionError.EmptyBridge);
            TestMutation(packet, validate, g => { var n = g.Nodes[0]; n.Position.x += 1; g.Nodes[0] = n; }, ChallengeSubmissionError.InvalidAnchors);
            TestMutation(packet, validate, g => { var n = g.Nodes[1]; n.Anchor = 0; g.Nodes[1] = n; }, ChallengeSubmissionError.InvalidAnchors);
            TestMutation(packet, validate, g => { var n = g.Nodes[2]; n.Position.y = float.NaN; g.Nodes[2] = n; }, ChallengeSubmissionError.InvalidNodes);
            TestMutation(packet, validate, g => { var n = g.Nodes[2]; n.Position.z += 2; g.Nodes[2] = n; }, ChallengeSubmissionError.InvalidNodes);
            TestMutation(packet, validate, g => { var n = g.Nodes[2]; n.Position.z += 0.9f; g.Nodes[2] = n; }, ChallengeSubmissionError.None);
            var depthGraph = ChallengeBridgeSubmissionCodec.Decode(packet);
            var depthNode = depthGraph.Nodes[2]; depthNode.Position.z += 0.4f; depthGraph.Nodes[2] = depthNode;
            Check(ChallengeBridgeSubmissionRules.Validate(depthGraph, source, definition, catalog, -10, out _, 0.1f) ==
                ChallengeSubmissionError.InvalidNodes, "Admission ignored the configured builder depth tolerance.");
            end.transform.position += Vector3.forward * 0.4f;
            var endNode = depthGraph.Nodes[1]; endNode.Position = end.transform.position; depthGraph.Nodes[1] = endNode;
            Check(validate(depthGraph) == ChallengeSubmissionError.None, "Slightly non-coplanar authored banks were rejected.");
            end.transform.position = new Vector3(35, 3, -8);
            Pass(report, "Builder-permitted snap depth and non-coplanar authored banks submit; configured tighter depth, altered anchors and genuinely off-plane nodes still fail safely.");
            TestMutation(packet, validate, g => { var b = g.Bars[0]; b.End = 999; g.Bars[0] = b; }, ChallengeSubmissionError.InvalidMembers);
            TestMutation(packet, validate, g => { var b = g.Bars[0]; b.End = b.Start; g.Bars[0] = b; }, ChallengeSubmissionError.InvalidMembers);
            TestMutation(packet, validate, g => { var b = g.Bars[0]; b.End = 1; g.Bars[0] = b; }, ChallengeSubmissionError.None);
            TestMutation(packet, validate, g => { var b = g.Bars[0]; b.Material = "UnapprovedPrefabPath"; g.Bars[0] = b; }, ChallengeSubmissionError.InvalidMaterial);
            definition.hiddenMaterials.Add(road); Check(validate(graph) == ChallengeSubmissionError.InvalidMaterial, "Hidden material was accepted."); definition.hiddenMaterials.Clear();
            definition.allowedMaterials[0].maxPieces = 2; Check(validate(graph) == ChallengeSubmissionError.None, "Piece count blocked simulation."); definition.allowedMaterials[0].maxPieces = 3;
            TestMutation(packet, validate, g => g.Bars.Add(g.Bars[0]), ChallengeSubmissionError.None);
            definition.allowedMaterials[0].maxPieces = 0;
            TestMutation(packet, validate, g => g.Bars.Add(g.Bars[0]), ChallengeSubmissionError.None);
            definition.allowedMaterials[0].maxPieces = 3;
            definition.budget = 149; Check(validate(graph) == ChallengeSubmissionError.None, "Over-budget design was rejected before simulation."); definition.budget = 150;
            TestMutation(packet, validate, g => { var b = g.Bars[0]; b.Material = dual.Id; g.Bars[0] = b; }, ChallengeSubmissionError.None);
            definition.budget = 200;
            var dualGraph = ChallengeBridgeSubmissionCodec.Decode(packet); var dualBar = dualGraph.Bars[0]; dualBar.Material = dual.Id; dualGraph.Bars[0] = dualBar;
            Check(ChallengeBridgeSubmissionRules.Validate(dualGraph, source, definition, catalog, -10, out cost) == ChallengeSubmissionError.None &&
                Mathf.Abs(cost - 200) < 0.001f, "Dual-member cost was not doubled."); definition.budget = 150;
            road.maxLength = 4.8f; Check(validate(graph) == ChallengeSubmissionError.None, "Existing +0.2m snap tolerance was changed."); road.maxLength = 6;
            road.maxLength = float.PositiveInfinity; Check(validate(graph) == ChallengeSubmissionError.None, "Authored unlimited member length was rejected.");
            road.maxLength = 1f; Check(validate(graph) == ChallengeSubmissionError.None, "Span limit blocked a safe finite member from simulation.");
            road.maxLength = 6;
            road.isPier = true; Check(validate(graph) == ChallengeSubmissionError.None, "Unusual pier placement was rejected before simulation."); road.isPier = false;
            var pierGraph = new ChallengeBridgeSubmission();
            pierGraph.Nodes.AddRange(graph.Nodes.GetRange(0, 2));
            pierGraph.Nodes.Add(new ChallengeSubmittedNode { Position = new Vector3(25, -10, -8), Anchor = -1 });
            pierGraph.Nodes.Add(new ChallengeSubmittedNode { Position = new Vector3(25, -5, -8), Anchor = -1 });
            pierGraph.Bars.Add(new ChallengeSubmittedBar { Start = 2, End = 3, Material = road.Id }); road.isPier = true;
            Check(validate(pierGraph) == ChallengeSubmissionError.None, "An existing legal pier foundation was rejected."); road.isPier = false;
            TestMutation(packet, validate, g => g.Nodes.Add(new ChallengeSubmittedNode { Position = new Vector3(22, 9, -8), Anchor = -1 }), ChallengeSubmissionError.None);
            TestMutation(packet, validate, g => g.Bars.RemoveRange(1, 2), ChallengeSubmissionError.None);
            var disconnected = ChallengeBridgeSubmissionCodec.Decode(packet);
            disconnected.Bars.RemoveAt(2); disconnected.Bars.RemoveAt(0);
            Check(validate(disconnected) == ChallengeSubmissionError.None, "A floating/disconnected member was rejected before simulation.");
            TestMutation(packet, validate, g => { var n = g.Nodes[2]; n.Position = g.Nodes[3].Position; g.Nodes[2] = n; }, ChallengeSubmissionError.InvalidMembers);
            foreach (float badPrice in new[] { -1f, float.NaN, float.PositiveInfinity })
            { road.costPerMeter = badPrice; Check(validate(graph) == ChallengeSubmissionError.InvalidMembers, "Unsafe material pricing reached simulation."); }
            road.costPerMeter = 10f;
            Pass(report, "Non-empty incomplete/disconnected bridges, loose nodes, overlapping/overlong members, unusual piers, quantity excess and over-budget drafts are accepted for simulation. Empty graphs, altered anchors, nonfinite/off-plane data, foreign/degenerate endpoints and forbidden materials are still rejected; host pricing is unchanged.");
            var truncated = new byte[packet.Length - 1]; Array.Copy(packet, truncated, truncated.Length); RejectPacket(truncated);
            var trailing = new byte[packet.Length + 1]; Array.Copy(packet, trailing, packet.Length); RejectPacket(trailing);
            byte[] badVersion = (byte[])packet.Clone(); badVersion[0] = 99; RejectPacket(badVersion);
            byte[] badCount = (byte[])packet.Clone(); Buffer.BlockCopy(BitConverter.GetBytes(int.MaxValue), 0, badCount, 4, 4); RejectPacket(badCount);
            byte[] badId = (byte[])packet.Clone(); badId[12 + graph.Nodes.Count * 16 + 8] = 129; RejectPacket(badId);
            byte[] badUtf8 = (byte[])packet.Clone(); badUtf8[12 + graph.Nodes.Count * 16 + 10] = 255; RejectPacket(badUtf8);
            RejectPacket(new byte[ChallengeBridgeSubmissionCodec.MaxPacketBytes + 1]);
            var excessive = ChallengeBridgeSubmissionCodec.Decode(packet);
            while (excessive.Nodes.Count <= ChallengeBridgeSubmissionCodec.MaxNodes) excessive.Nodes.Add(graph.Nodes[2]);
            Check(validate(excessive) == ChallengeSubmissionError.InvalidPacket, "Oversize graph reached validation.");
            Pass(report, "Truncated, trailing, unsupported, excessive-count, oversized-ID, invalid-UTF8 and oversized packets are rejected before accepting geometry.");
            ChallengeBridgeSubmission copy = ChallengeBridgeSubmissionCodec.Decode(packet);
            copy.Bars.Clear(); Check(ChallengeBridgeSubmissionCodec.Decode(packet).Bars.Count == 3, "Retained packet was mutable through a decoded graph.");
            Check(source.bakedBars.Count == 0 && start.transform.position == new Vector3(20, 3, -8) &&
                end.transform.position == new Vector3(35, 3, -8) && definition.ContractID != ChallengeBuildWorkspace.ContractPrefix,
                "Validation altered saved-world objects.");
            Pass(report, "Accepted packet storage is detached from later local edits and fresh decoding; validation never bakes or modifies world geometry.");
            ValidateWorkspaceCapture(source, definition, road, fixture, report);
        }
        finally { Object.DestroyImmediate(definition); Object.DestroyImmediate(road); Object.DestroyImmediate(dual); }
    }

    private static void ValidateAuthoredUI(StringBuilder report)
    {
        string scene = File.ReadAllText("Assets/Scenes/CanyonCrossing.unity");
        Func<string, string> block = id => Regex.Match(scene, @"(?ms)^--- !u!\d+ &" + id + @"\r?\n.*?(?=^--- !u!|\z)").Value;
        Check(block("900000000000011001").Contains("m_IsActive: 0") && block("900000000000011010").Contains("m_IsActive: 0") &&
            block("900000000000011005").Contains("m_Interactable: 0") && block("900000000000011005").Contains("m_MethodName: SubmitBridge") &&
            block("900000000000011005").Contains("m_Target: {fileID: 900000000000000105}") &&
            block("900000000000011002").Contains("m_Father: {fileID: 6234276506990522558}") &&
            block("900000000000011011").Contains("m_Father: {fileID: 6234276506990522558}") &&
            block("900000000000000105").Contains("submissionStatusText: {fileID: 900000000000011017}"), "Submission controls are not safely scene-authored.");
        Pass(report, "Submit Bridge and the status card are authored under the existing Build Canvas, persistently wired, and hidden/disabled in single-player.");
    }

    private static void ValidateWorkspaceCapture(BuildLocation source, ContractSO definition, BridgeMaterialSO material,
        Transform fixture, StringBuilder report)
    {
        BarCreator creator = Child("Submission Capture Creator", fixture).AddComponent<BarCreator>();
        creator.pointToInstantiate = Child("Submission Capture Node Prefab", fixture);
        creator.pointToInstantiate.AddComponent<Point>();
        creator.barToInstantiate = Child("Submission Capture Member Prefab", fixture);
        creator.barToInstantiate.AddComponent<Bar>();
        ChallengeBuildWorkspace workspace = null;
        try
        {
            workspace = ChallengeBuildWorkspace.Create(source, definition, creator, 17, 2);
            Point start = workspace.Location.startingAnchors[0], end = workspace.Location.endingAnchors[0];
            Point middleA = Child("Submission Own Node A", creator.pointParent).AddComponent<Point>();
            Point middleB = Child("Submission Own Node B", creator.pointParent).AddComponent<Point>();
            middleA.AssignOwner(workspace.Location, true); middleB.AssignOwner(workspace.Location, true);
            middleA.transform.position = new Vector3(25, 3, -8); middleB.transform.position = new Vector3(30, 3, -8);
            Point[] points = { start, middleA, middleB, end };
            for (int i = 0; i < 3; i++)
            {
                Bar bar = Child("Submission Own Bar", creator.barParent).AddComponent<Bar>();
                bar.AssignOwner(workspace.Location, true); bar.materialData = material;
                bar.startPoint = points[i]; bar.endPoint = points[i + 1];
            }
            Child("Submission Undone Node", creator.pointParent).AddComponent<Point>().gameObject.SetActive(false);
            Child("Submission Undone Bar", creator.barParent).AddComponent<Bar>().gameObject.SetActive(false);
            workspace.SetEditingEnabled(false);
            ChallengeBridgeSubmission capture = ChallengeBridgeSubmissionRules.Capture(workspace);
            Check(!creator.enabled && capture.Nodes.Count == 4 && capture.Bars.Count == 3 &&
                ChallengeBridgeSubmissionRules.Validate(capture, source, definition,
                    new Dictionary<string, BridgeMaterialSO> { { material.Id, material } }, -10, out float cost) == ChallengeSubmissionError.None &&
                Mathf.Abs(cost - 150) < 0.001f, "Private workspace did not capture a valid frozen logical graph.");
            Point foreign = Child("Submission Foreign Owned Node", creator.pointParent).AddComponent<Point>();
            foreign.AssignOwner(source, true);
            bool rejected = false;
            try { ChallengeBridgeSubmissionRules.Capture(workspace); }
            catch (InvalidDataException) { rejected = true; }
            Object.DestroyImmediate(foreign.gameObject);
            Check(rejected, "Capture accepted another location's node.");
            Pass(report, "Native private-workspace capture includes only its own finished members, excludes Undo/deleted pieces, rejects foreign ownership and remains readable with editing frozen.");
        }
        finally { workspace?.Dispose(); }
    }

    private static void ValidateAuthoredMaterialCapture(Transform fixture, StringBuilder report)
    {
        var definition = ScriptableObject.CreateInstance<ContractSO>();
        definition.budget = 1000000f;
        BuildLocation site = Child("Authored Submission Site", fixture).AddComponent<BuildLocation>();
        Point start = Child("Authored Submission Start", fixture).AddComponent<Point>();
        Point end = Child("Authored Submission End", fixture).AddComponent<Point>();
        start.transform.position = new Vector3(40, 3, -8);
        end.transform.position = new Vector3(43, 3, -8);
        site.startingAnchors.Add(start); site.endingAnchors.Add(end); site.activeContract = definition;
        BarCreator creator = Child("Authored Submission Creator", fixture).AddComponent<BarCreator>();
        creator.pointToInstantiate = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/BridgeBuilder/Prefabs/Point 1.prefab");
        creator.barToInstantiate = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/BridgeBuilder/Prefabs/materials/map1/WoodRoad.prefab");
        Check(creator.pointToInstantiate != null && creator.barToInstantiate != null, "Authored construction prefabs are missing.");
        ChallengeBuildWorkspace workspace = null;
        try
        {
            workspace = ChallengeBuildWorkspace.Create(site, definition, creator, 19, 2);
            int tested = 0, visualBars = 0;
            foreach (string path in new[] { "Wood Road", "Beam", "Concrete Road", "Pier", "RopeMaterial" })
            {
                BridgeMaterialSO material = UnityEditor.AssetDatabase.LoadAssetAtPath<BridgeMaterialSO>(
                    "Assets/BridgeBuilder/Data/Resources/" + path + ".asset");
                Check(material != null && material.segmentPrefab != null, "Authored material is missing: " + path);
                end.transform.position = material.isPier ? new Vector3(40, 6, -8) : new Vector3(43, 3, -8);
                workspace.Location.endingAnchors[0].transform.position = end.transform.position;
                GameObject obj = Object.Instantiate(creator.barToInstantiate, workspace.BarRoot);
                Bar bar = obj.GetComponent<Bar>();
                // Reproduce Initialize's real authored visual hierarchy without
                // invoking runtime deferred Destroy from an Edit Mode test.
                for (int i = obj.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(obj.transform.GetChild(i).gameObject);
                bar.AssignOwner(workspace.Location, true); bar.materialData = material;
                bar.startPoint = workspace.Location.startingAnchors[0]; bar.endPoint = workspace.Location.endingAnchors[0];
                for (int i = 0; i < (material.isDualBeam ? 2 : 1); i++)
                {
                    GameObject segment = Object.Instantiate(material.segmentPrefab, bar.transform);
                    segment.name = "VisualSegment_" + i;
                    segment.SetActive(true);
                }
                int rawCount = workspace.Root.GetComponentsInChildren<Bar>(true).Length;
                if (path == "Wood Road")
                    Check(rawCount > 1, "Regression fixture did not reproduce nested visual Bar components: " + path);
                visualBars += rawCount - 1;
                workspace.SetEditingEnabled(false);
                var graph = ChallengeBridgeSubmissionRules.Capture(workspace);
                var decoded = ChallengeBridgeSubmissionCodec.Decode(ChallengeBridgeSubmissionCodec.Encode(graph));
                Check(graph.Nodes.Count == 2 && graph.Bars.Count == 1 && decoded.Bars.Count == 1 &&
                    decoded.Bars[0].Material == material.Id && decoded.Bars[0].Start != decoded.Bars[0].End,
                    "Authored visuals were serialized as structural members: " + path);
                Check(ChallengeBridgeSubmissionRules.Validate(decoded, site, definition,
                    new Dictionary<string, BridgeMaterialSO> { { material.Id, material } }, 3f, out float cost) == ChallengeSubmissionError.None &&
                    Mathf.Abs(cost - 3f * material.costPerMeter * (material.isDualBeam ? 2 : 1)) < 0.01f,
                    "Captured authored member failed host rules or authoritative pricing: " + path);
                var receipt = BridgeConstructionReceipt.Capture(workspace.Location,
                    workspace.Root.GetComponentsInChildren<Bar>(true), includeDisabled: true);
                Check(Mathf.Abs(receipt.TotalCost - cost) < 0.01f,
                    "Completion receipt differs from authored construction pricing: " + path);
                // Missing endpoints on the actual logical object must still fail.
                bar.endPoint = null;
                bool rejected = false;
                try { ChallengeBridgeSubmissionRules.Capture(workspace); } catch (InvalidDataException) { rejected = true; }
                Check(rejected, "A malformed logical member was silently discarded: " + path);
                Object.DestroyImmediate(obj);
                tested++;
            }
            Pass(report, $"Actual authored prefabs: {tested} materials captured/encoded/host-validated and receipt-priced as one logical member each with correct cost, excluding {visualBars} nested visual Bars; malformed logical endpoints still rejected with editing frozen.");
        }
        finally { workspace?.Dispose(); Object.DestroyImmediate(definition); }
    }

    private static void TestMutation(byte[] packet, Func<ChallengeBridgeSubmission, ChallengeSubmissionError> validate,
        Action<ChallengeBridgeSubmission> mutate, ChallengeSubmissionError expected)
    {
        var graph = ChallengeBridgeSubmissionCodec.Decode(packet); mutate(graph);
        Check(validate(graph) == expected, "Submission mutation was not rejected as " + expected + ".");
    }
    private static void RejectPacket(byte[] packet)
    {
        try { ChallengeBridgeSubmissionCodec.Decode(packet); }
        catch (Exception error) when (error is InvalidDataException || error is EndOfStreamException || error is ArgumentException) { return; }
        throw new InvalidOperationException("Malformed submission packet was accepted.");
    }
    private static GameObject Child(string name, Transform parent)
    { var obj = new GameObject(name); obj.transform.SetParent(parent, false); return obj; }
    private static void Check(bool value, string failure) { if (!value) throw new InvalidOperationException(failure); }
    private static void Pass(StringBuilder report, string message) { report.AppendLine("PASS: " + message); }
}
