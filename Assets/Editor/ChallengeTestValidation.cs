using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Fusion;
using UnityEngine;
using Object = UnityEngine.Object;

public static class ChallengeTestValidation
{
    public static void Run(Transform fixture, StringBuilder report)
    { ValidatePolicies(report); ValidateIsolatedRunAndView(fixture, report); }

    public static void ValidatePolicies(StringBuilder report)
    {
        PlayerRef host = PlayerRef.FromIndex(0), guest = PlayerRef.FromIndex(1);
        foreach (bool guestFirst in new[] { false, true })
        {
            var state = new MultiplayerChallengeState { Revision = 12, Phase = MultiplayerChallengePhase.Building, Guest = guest,
                HostArrived = true, GuestArrived = true, HostReady = true, GuestReady = true, HostBuilding = true, GuestBuilding = true };
            var first = guestFirst ? guest : host; var second = guestFirst ? host : guest;
            Check(MultiplayerChallengeRules.TryAcceptSubmission(ref state, first, host, 12, 100, 1), "First submission rejected.");
            var copy = state;
            Check(!MultiplayerChallengeRules.TryPrepareTest(ref copy, 12, 1), "Testing began before both submitted.");
            Check(MultiplayerChallengeRules.TryAcceptSubmission(ref state, second, host, 12, 120, 2) &&
                state.HostSubmissionOrder == (guestFirst ? 2 : 1) && state.GuestSubmissionOrder == (guestFirst ? 1 : 2), "Submission ordering was not authoritative.");
            copy = state; Check(!MultiplayerChallengeRules.TryPrepareTest(ref copy, 11, 1) &&
                !MultiplayerChallengeRules.TryPrepareTest(ref copy, 12, 2), "Stale or out-of-order test began.");
            Check(MultiplayerChallengeRules.TryPrepareTest(ref state, 12, 1) &&
                MultiplayerChallengeRules.TestsHostBridge(state) == !guestFirst, "First accepted player was not tested first.");
            copy = state; Check(!MultiplayerChallengeRules.TryStartTest(ref copy, 12, 1), "Host started before the guest view was ready.");
            state.GuestTestViewReady = true;
            Check(MultiplayerChallengeRules.TryStartTest(ref state, 12, 1), "Prepared test failed to start.");
            copy = state; Check(!MultiplayerChallengeRules.TryPrepareTest(ref copy, 12, 2), "Second bridge tested before first result.");
            foreach (float bad in new[] { -1f, float.NaN, float.PositiveInfinity })
            {
                copy = state; Check(!MultiplayerChallengeRules.TryRecordTest(ref copy, 12, 1, ChallengeTestOutcome.Crossed, bad, 0.5f), "Invalid duration accepted.");
                copy = state; Check(!MultiplayerChallengeRules.TryRecordTest(ref copy, 12, 1, ChallengeTestOutcome.Crossed, 2f, bad), "Invalid peak accepted.");
            }
            Check(MultiplayerChallengeRules.TryRecordTest(ref state, 12, 1, ChallengeTestOutcome.Crossed, 3f, 0.4f) &&
                (guestFirst ? state.GuestTestOutcome : state.HostTestOutcome) == ChallengeTestOutcome.Crossed &&
                (guestFirst ? state.HostTestOutcome : state.GuestTestOutcome) == ChallengeTestOutcome.None, "First test overwrote the opponent result.");
            copy = state; Check(!MultiplayerChallengeRules.TryRecordTest(ref copy, 12, 1, ChallengeTestOutcome.Fell, 1, 1) &&
                !MultiplayerChallengeRules.TryFinishTests(ref copy, 12), "Duplicate result replaced the first, or results appeared early.");
            Check(MultiplayerChallengeRules.TryPrepareTest(ref state, 12, 2) &&
                !state.GuestTestViewReady && MultiplayerChallengeRules.TestsHostBridge(state) == guestFirst,
                "Second test did not reset readiness or use the remaining bridge.");
            copy = state; Check(!MultiplayerChallengeRules.TryStartTest(ref copy, 12, 2), "Second test skipped spectator readiness.");
            state.GuestTestViewReady = true;
            Check(MultiplayerChallengeRules.TryStartTest(ref state, 12, 2) &&
                MultiplayerChallengeRules.TryRecordTest(ref state, 12, 2, ChallengeTestOutcome.Collapsed, 2f, 1f) &&
                state.Phase == MultiplayerChallengePhase.Testing && MultiplayerChallengeRules.TryFinishTests(ref state, 12) &&
                state.Phase == MultiplayerChallengePhase.TestResults, "Both outcomes failed to finish after the final-view gate.");
            Check(state.HostTestOutcome == (guestFirst ? ChallengeTestOutcome.Collapsed : ChallengeTestOutcome.Crossed) &&
                state.GuestTestOutcome == (guestFirst ? ChallengeTestOutcome.Crossed : ChallengeTestOutcome.Collapsed), "Result ownership swapped.");
        }
        report.AppendLine("PASS: Host-first AND guest-first submissions test in authoritative order only after both submit; each test waits for guest readiness; stale/duplicate results cannot overwrite either design; results follow both tests.");
    }

    private static void ValidateIsolatedRunAndView(Transform fixture, StringBuilder report)
    {
        var definition = ScriptableObject.CreateInstance<ContractSO>();
        definition.contractID = "VALIDATION_TEST_ONLY"; definition.budget = 1000; definition.liveLoadWeight = 50;
        var material = ScriptableObject.CreateInstance<BridgeMaterialSO>();
        material.name = "Isolated Test Road"; material.costPerMeter = 10; material.maxLength = 6; material.isRoad = true;
        GameObject segment = GameObject.CreatePrimitive(PrimitiveType.Cube); segment.transform.SetParent(fixture, false); material.segmentPrefab = segment;
        BuildLocation site = Child("Sequential Test Fixture Site", fixture).AddComponent<BuildLocation>();
        site.activeContract = definition;
        Point start = Child("Sequential Test Start", fixture).AddComponent<Point>();
        Point end = Child("Sequential Test End", fixture).AddComponent<Point>();
        start.transform.position = new Vector3(40, 3, -8); end.transform.position = new Vector3(43, 3, -8);
        site.startingAnchors.Add(start); site.endingAnchors.Add(end);
        BarCreator creator = Child("Sequential Test Creator", fixture).AddComponent<BarCreator>();
        creator.pointToInstantiate = Child("Sequential Test Point Prefab", fixture); creator.pointToInstantiate.AddComponent<Point>();
        creator.barToInstantiate = Child("Sequential Test Bar Prefab", fixture); creator.barToInstantiate.AddComponent<Bar>();
        BridgePhysicsManager settings = Child("Sequential Test Original Physics", fixture).AddComponent<BridgePhysicsManager>();
        settings.physicsSolverIterations = 52; settings.physicsSolverVelocityIterations = 24; settings.settleFramesAmount = 71;
        LiveLoadVehicle vehicle = Child("Sequential Test Original Vehicle", fixture).AddComponent<LiveLoadVehicle>();
        vehicle.assignedContract = definition; vehicle.startPoint = start.transform; vehicle.endPoint = end.transform;
        vehicle.transform.position = start.transform.position;
        vehicle.physicsManager = settings; vehicle.maxSpeed = 3.5f; vehicle.engineTorque = 987;
        GameObject wheel = GameObject.CreatePrimitive(PrimitiveType.Cube); wheel.transform.SetParent(vehicle.transform, false);
        vehicle.wheelObjects = new[] { wheel };
        var graph = new ChallengeBridgeSubmission();
        graph.Nodes.Add(new ChallengeSubmittedNode { Position = start.transform.position, Anchor = 0 });
        graph.Nodes.Add(new ChallengeSubmittedNode { Position = end.transform.position, Anchor = 1 });
        graph.Bars.Add(new ChallengeSubmittedBar { Start = 0, End = 1, Material = material.Id });
        var catalog = new Dictionary<string, BridgeMaterialSO> { { material.Id, material } };
        ChallengeBridgeTestRun run = null; ChallengeBridgeTestView view = null;
        bool autoSync = UnityEngine.Physics.autoSyncTransforms; int iterations = UnityEngine.Physics.defaultSolverIterations;
        float maximumDelta = Time.maximumDeltaTime;
        try
        {
            run = ChallengeBridgeTestRun.Create(graph, site, definition, creator, settings, vehicle, 77, 12, 1);
            Check(run.Physics != settings && run.Vehicle != vehicle && run.Location != site && run.Physics.IsSessionChallengeTest &&
                run.Physics.SessionTestVehicle == run.Vehicle && run.Vehicle.physicsManager == run.Physics &&
                run.Physics.physicsSolverIterations == 52 && run.Physics.physicsSolverVelocityIterations == 24 && run.Physics.settleFramesAmount == 71 &&
                run.Vehicle.maxSpeed == vehicle.maxSpeed && run.Vehicle.engineTorque == vehicle.engineTorque && run.Vehicle.TotalTestWeight == 77 &&
                !run.Physics.BakeBridge(definition), "Test reused world objects, lost settings/load or allowed baking.");
            var descriptor = new ChallengeTestDescriptor { Bridge = run.CapturePresentation("Validation/Bridge"),
                VehicleKey = ChallengeTestDescriptor.VehiclePath(vehicle), Weight = 77,
                VehiclePoses = new[] { HostWorldBridgePose.World(run.Vehicle.transform), HostWorldBridgePose.World(run.Vehicle.wheelObjects[0].transform) } };
            var decoded = ChallengeTestDescriptor.Decode(descriptor.Encode());
            Check(decoded.Weight == 77 && decoded.Bridge.Bars.Count == 1 && decoded.VehicleKey == descriptor.VehicleKey,
                "Test baseline did not round trip.");
            view = new ChallengeBridgeTestView(decoded, site, creator, catalog, settings);
            Check(view.Root.GetComponentsInChildren<MonoBehaviour>(true).Length == 0 &&
                view.Root.GetComponentsInChildren<Rigidbody>(true).Length == 0 && view.Root.GetComponentsInChildren<Collider>(true).Length == 0,
                "Guest simulation view contains gameplay scripts, physics or collision.");
            byte[] motion = ChallengeTestMotionCodec.Encode(1, 0, 1f, run.MotionTargets, run.Bars);
            Check(motion.Length <= RpcAttribute.MaxPayloadSize - 64 && motion.Length == 16 + run.MotionTargets.Count * 30,
                "Motion exceeds Fusion RPC payload or unexpected bandwidth.");
            var chunk = ChallengeTestMotionCodec.Decode(motion); view.Receive(chunk); view.Render(0f);
            material.isRope = true;
            var ropeChunk = ChallengeTestMotionCodec.Decode(ChallengeTestMotionCodec.Encode(2, 0, 2, run.MotionTargets, run.Bars));
            Check((ropeChunk.Stress[0] & 0x4000) != 0 && ropeChunk.Positions[0] == run.Bars[0].StartPosition &&
                ropeChunk.RopeEnds[0] == run.Bars[0].EndPosition && ropeChunk.RopeLengths[0] == run.Bars[0].currentLength,
                "Deforming rope endpoints/rest length were lost or required extra bandwidth.");
            using (var ropeView = new ChallengeBridgeTestView(decoded, site, creator, catalog, settings))
            {
                ropeView.Receive(ropeChunk); ropeView.Render(0f);
                var line = ropeView.Root.GetComponentInChildren<LineRenderer>();
                Check(line != null && line.enabled && line.positionCount == 17 &&
                    line.GetPosition(0) == ropeChunk.Positions[0] && line.GetPosition(16) == ropeChunk.RopeEnds[0],
                    "Guest rope did not render the authoritative deforming endpoints.");
            }
            material.isRope = false;
            var newer = ChallengeTestMotionCodec.Decode(motion); newer.Sequence = 2; newer.Time = 2; newer.Positions[0] += Vector3.right;
            view.Receive(newer); view.Render(0f);
            Vector3 newestPose = view.Root.transform.GetChild(0).position;
            var third = ChallengeTestMotionCodec.Decode(ChallengeTestMotionCodec.Encode(3, 0, 2.05f, run.MotionTargets, run.Bars));
            third.Positions[0] += Vector3.right * 1.05f;
            view.Receive(third); view.Render(0.1f);
            float interpolatedX = view.Root.transform.GetChild(0).position.x;
            Check(interpolatedX < newestPose.x && interpolatedX > newestPose.x - 0.15f,
                "A multi-snapshot interpolation delay clamped to the previous pose instead of using history.");
            view.Render(0f); newestPose = view.Root.transform.GetChild(0).position;
            view.Receive(chunk); view.Render(0f);
            Check(view.Root.transform.GetChild(0).position == newestPose, "Late motion rewound the guest's bridge.");
            view.CompleteFinalFrame(); view.Render(0.25f);
            Check(view.Root.transform.GetChild(0).position == newestPose, "Interpolation rewound an acknowledged final frame.");
            Array.Resize(ref motion, motion.Length - 1); RejectMotion(motion);
            byte[] invalid = ChallengeTestMotionCodec.Encode(3, 0, 3, run.MotionTargets, run.Bars); invalid[7] = 9; RejectMotion(invalid);
            Check(!settings.IsSimulationActive && !vehicle.IsDriving && site.bakedBars.Count == 0 &&
                start.transform.position == new Vector3(40,3,-8) && end.transform.position == new Vector3(43,3,-8), "A test mutated the original world.");
            run.Physics.ActivatePhysics();
            Check(run.Physics.IsSimulationActive && !settings.IsSimulationActive && !vehicle.IsDriving,
                "Physics startup reached the story manager or world vehicle.");
            report.AppendLine("PASS: Disposable host graph copies original solver/settling/vehicle settings and frozen load; original physics, vehicle, anchors and baked lists remain unchanged; bake is blocked.");
            report.AppendLine("PASS: Guest baseline is visual only, with no gameplay scripts, Rigidbody or collider; motion and deforming-rope endpoints round-trip within Fusion's RPC limit, reject malformed chunks and ignore late snapshots.");
        }
        finally
        {
            view?.Dispose(); run?.Dispose(); Object.DestroyImmediate(definition); Object.DestroyImmediate(material);
        }
        Check(UnityEngine.Physics.autoSyncTransforms == autoSync && UnityEngine.Physics.defaultSolverIterations == iterations && Time.maximumDeltaTime == maximumDelta,
            "Disposing a test failed to restore global physics settings.");
        report.AppendLine("PASS: Cancelling/disposing an isolated test restores global physics settings and destroys transient geometry; no PlayerDataManager save/reward route is used.");
    }
    private static void RejectMotion(byte[] packet)
    {
        try { ChallengeTestMotionCodec.Decode(packet); }
        catch (Exception error) when (error is InvalidDataException || error is EndOfStreamException) { return; }
        throw new InvalidOperationException("Invalid motion was accepted.");
    }
    private static GameObject Child(string name, Transform parent) { var obj = new GameObject(name); obj.transform.SetParent(parent, false); return obj; }
    private static void Check(bool pass, string message) { if (!pass) throw new InvalidOperationException(message); }
}
