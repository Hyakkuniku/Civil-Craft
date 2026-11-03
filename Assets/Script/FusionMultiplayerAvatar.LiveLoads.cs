using System.Collections.Generic;
using System.Text;
using Fusion;
using UnityEngine;

public sealed partial class FusionMultiplayerAvatar
{
    public struct MultiLiveLoadPose : INetworkStruct
    {
        public NetworkString<_256> VehicleKey;
        public NetworkString<_64> DisplayName;
        public Vector3 Position;
        public Quaternion Rotation;
        public NetworkBool Simulating;
        public int WheelCount;
        public float WeightKg;
        public LiveLoadTestParticipantState State;
        public NetworkBool TestFailed;
    }
    public struct MultiLiveLoadWheelPose : INetworkStruct
    {
        public Vector3 Position;
        public Quaternion Rotation;
    }
    [Networked] private int HostMultiLiveLoadCount { get; set; }
    [Networked, Capacity(4)] private NetworkArray<MultiLiveLoadPose> HostMultiLiveLoads => default;
    [Networked, Capacity(128)] private NetworkArray<MultiLiveLoadWheelPose> HostMultiLiveLoadWheels => default;
    private readonly Dictionary<int, RemoteMultiLiveLoad> remoteMultipleLoads = new Dictionary<int, RemoteMultiLiveLoad>();
    private readonly Dictionary<string, LiveLoadVehicle> remoteMultipleCandidates = new Dictionary<string, LiveLoadVehicle>();
    private int remoteMultipleSceneHandle;
    private float nextMultipleCandidateLookup;
    private BuildLocation publishedMultiLoadLocation;
    private LiveLoadTestRoster publishedMultiLoadRoster;
    private readonly MultiLiveLoadPose[] preparedMultipleStates = new MultiLiveLoadPose[4];
    private readonly MultiLiveLoadWheelPose[] preparedMultipleWheels = new MultiLiveLoadWheelPose[128];
    private readonly string[] receivedMultipleKeys = new string[4];
    private readonly LiveLoadVehicle[] receivedMultipleVehicles = new LiveLoadVehicle[4];
    private readonly List<string> remoteMultipleKeys = new List<string>(4);
    private readonly HashSet<string> multiplePublishedKeys = new HashSet<string>(System.StringComparer.Ordinal);

    private bool PublishHostMultipleLiveLoads()
    {
        GameManager game = GameManager.Instance;
        BuildLocation location = game != null ? game.ActiveBuildLocation : null;
        ContractSO contract = game != null ? game.CurrentContract : null;
        if (location == null || contract == null || !contract.HasMultipleLiveLoads || IsChallengeBusy)
        {
            HostMultiLiveLoadCount = 0; publishedMultiLoadLocation = null; publishedMultiLoadRoster = null;
            return false;
        }
        // Prefer the run's immutable participants; resolve scene bindings only on
        // entry to the site, never through a per-frame global vehicle search.
        ContractLiveLoadBinding firstBinding = location.liveLoadVehicles != null && location.liveLoadVehicles.Count > 0 ? location.liveLoadVehicles[0] : null;
        BridgePhysicsManager physics = firstBinding != null && firstBinding.vehicle != null ? firstBinding.vehicle.physicsManager : null;
        LiveLoadTestRoster roster = physics != null && physics.MultiLoadSession != null &&
            physics.MultiLoadSession.Roster.Location == location && physics.MultiLoadSession.Roster.Contract == contract ? physics.MultiLoadSession.Roster : null;
        if (roster == null)
        {
            if (publishedMultiLoadLocation != location || publishedMultiLoadRoster == null)
            {
                publishedMultiLoadLocation = location;
                LiveLoadTestRoster.TryCreate(location, contract, out publishedMultiLoadRoster, out _, false);
            }
            roster = publishedMultiLoadRoster;
        }
        if (roster == null || physics == null || physics.gameObject.scene != location.gameObject.scene || roster.Participants.Count < 2 || roster.Participants.Count > 4)
        { HostMultiLiveLoadCount = 0; return true; }
        multiplePublishedKeys.Clear();
        for (int index = 0; index < roster.Participants.Count; index++)
        {
            LiveLoadVehicle vehicle = roster.Participants[index].Vehicle;
            string key = vehicle != null ? ChallengeTestDescriptor.VehiclePath(vehicle) : string.Empty;
            if (vehicle == null || vehicle.physicsManager != physics || string.IsNullOrWhiteSpace(key) || key.Length > 256 || !multiplePublishedKeys.Add(key) || !ValidMultipleWheels(vehicle))
            { HostMultiLiveLoadCount = 0; return true; }
            var state = new MultiLiveLoadPose { VehicleKey = key, DisplayName = BoundedMultipleLabel(roster.Participants[index].DisplayName, index),
                Position = vehicle.transform.position, Rotation = vehicle.transform.rotation,
                WheelCount = vehicle.wheelObjects.Length, Simulating = physics != null && physics.IsSimulationActive,
                WeightKg = roster.Participants[index].WeightKg,
                State = physics != null && physics.MultiLoadSession != null ? physics.MultiLoadSession.Participants[index].State : LiveLoadTestParticipantState.Waiting,
                TestFailed = physics != null && physics.MultiLoadSession != null && physics.MultiLoadSession.Failed };
            if (!ValidMultipleState(state)) { HostMultiLiveLoadCount = 0; return true; }
            preparedMultipleStates[index] = state;
            for (int wheel = 0; wheel < vehicle.wheelObjects.Length; wheel++)
            {
                Transform child = vehicle.wheelObjects[wheel].transform;
                var pose = new MultiLiveLoadWheelPose {
                    Position = vehicle.transform.InverseTransformPoint(child.position),
                    Rotation = Quaternion.Inverse(vehicle.transform.rotation) * child.rotation };
                if (!ValidMultiplePose(pose.Position, pose.Rotation)) { HostMultiLiveLoadCount = 0; return true; }
                preparedMultipleWheels[index * 32 + wheel] = pose;
            }
        }
        // Publish only a complete validated frame. A malformed later vehicle
        // must never leave a partly replaced roster visible on the guest.
        for (int index = 0; index < roster.Participants.Count; index++)
        {
            HostMultiLiveLoads.Set(index, preparedMultipleStates[index]);
            for (int wheel = 0; wheel < preparedMultipleStates[index].WheelCount; wheel++)
                HostMultiLiveLoadWheels.Set(index * 32 + wheel, preparedMultipleWheels[index * 32 + wheel]);
        }
        HostMultiLiveLoadCount = roster.Participants.Count;
        return true;
    }

    private void UpdateRemoteMultipleLiveLoads()
    {
        if (HostMultiLiveLoadCount < 1 || HostMultiLiveLoadCount > 4) { RestoreRemoteMultipleLiveLoads(); return; }
        bool unresolved = false;
        for (int index = 0; index < HostMultiLiveLoadCount; index++)
        {
            MultiLiveLoadPose state = HostMultiLiveLoads[index]; string key = state.VehicleKey.ToString();
            if (!ValidMultipleState(state)) { RestoreRemoteMultipleLiveLoads(); return; }
            for (int previous = 0; previous < index; previous++)
                if (receivedMultipleKeys[previous] == key) { RestoreRemoteMultipleLiveLoads(); return; }
            receivedMultipleKeys[index] = key;
            for (int wheel = 0; wheel < state.WheelCount; wheel++)
            {
                var pose = HostMultiLiveLoadWheels[index * 32 + wheel];
                if (!ValidMultiplePose(pose.Position, pose.Rotation)) { RestoreRemoteMultipleLiveLoads(); return; }
            }
            if (!remoteMultipleCandidates.TryGetValue(key, out var candidate) || candidate == null) unresolved = true;
        }
        if (remoteMultipleSceneHandle != gameObject.scene.handle || (unresolved && Time.unscaledTime >= nextMultipleCandidateLookup))
        {
            RestoreRemoteMultipleLiveLoads(); remoteMultipleCandidates.Clear(); remoteMultipleSceneHandle = gameObject.scene.handle;
            nextMultipleCandidateLookup = Time.unscaledTime + .5f;
            foreach (var vehicle in Resources.FindObjectsOfTypeAll<LiveLoadVehicle>())
            {
                if (vehicle == null || vehicle.IsSessionChallengeTestVehicle || vehicle.gameObject.scene != gameObject.scene) continue;
                string key = ChallengeTestDescriptor.VehiclePath(vehicle);
                // An ambiguous hierarchy path is never resolved arbitrarily.
                if (remoteMultipleCandidates.ContainsKey(key)) remoteMultipleCandidates[key] = null;
                else remoteMultipleCandidates.Add(key, vehicle);
            }
        }
        bool replaceRoster = MultipleRosterIdentityChanged(remoteMultipleKeys, receivedMultipleKeys, HostMultiLiveLoadCount);
        for (int index = 0; index < HostMultiLiveLoadCount; index++)
        {
            if (!remoteMultipleCandidates.TryGetValue(receivedMultipleKeys[index], out var source) || source == null || !ValidMultipleWheels(source) ||
                HostMultiLiveLoads[index].WheelCount != source.wheelObjects.Length)
            { RestoreRemoteMultipleLiveLoads(); return; }
            receivedMultipleVehicles[index] = source;
            if (!remoteMultipleLoads.TryGetValue(index, out var current) || current.Vehicle != source) replaceRoster = true;
        }
        if (replaceRoster)
        {
            // Restore every old owner before capturing any replacement. In an
            // A/B -> B/A reorder, incremental replacement would snapshot B's
            // remote pose and then clear its new owner while restoring old B.
            RestoreRemoteMultipleLiveLoads();
            for (int index = 0; index < HostMultiLiveLoadCount; index++)
            {
                remoteMultipleLoads.Add(index, new RemoteMultiLiveLoad(receivedMultipleVehicles[index], receivedMultipleKeys[index]));
                remoteMultipleKeys.Add(receivedMultipleKeys[index]);
            }
        }
        for (int index = 0; index < HostMultiLiveLoadCount; index++)
        {
            MultiLiveLoadPose state = HostMultiLiveLoads[index]; RemoteMultiLiveLoad remote = remoteMultipleLoads[index];
            remote.SetInspection(!state.Simulating);
            remote.Vehicle.SetRemoteMultiplayerWeight(state.WeightKg);
            Transform body = remote.Vehicle.transform;
            float blend = (body.position - state.Position).sqrMagnitude > 9f ? 1f : 1f - Mathf.Exp(-18f * Time.deltaTime);
            body.SetPositionAndRotation(Vector3.Lerp(body.position, state.Position, blend), Quaternion.Slerp(body.rotation, state.Rotation, blend));
            for (int wheel = 0; wheel < state.WheelCount; wheel++)
            {
                var pose = HostMultiLiveLoadWheels[index * 32 + wheel];
                Transform child = remote.Vehicle.wheelObjects[wheel].transform;
                child.SetPositionAndRotation(body.TransformPoint(pose.Position), body.rotation * pose.Rotation);
            }
        }
        if (TryGetRemoteMultiLoadStatus(out string text)) MultiLoadTestHUD.PresentRemote(this, text);
        else MultiLoadTestHUD.HideRemote(this);
    }

    private void RestoreRemoteMultipleLiveLoads()
    {
        MultiLoadTestHUD.HideRemote(this);
        foreach (var remote in remoteMultipleLoads.Values) remote.Restore(); remoteMultipleLoads.Clear(); remoteMultipleKeys.Clear();
    }

    internal static bool MultipleRosterIdentityChanged(IReadOnlyList<string> current, IReadOnlyList<string> next, int count)
    {
        if (current == null || next == null || count < 1 || count > 4 || next.Count < count || current.Count != count) return true;
        for (int index = 0; index < count; index++) if (!string.Equals(current[index], next[index], System.StringComparison.Ordinal)) return true;
        return false;
    }
    private static bool ValidMultipleWheels(LiveLoadVehicle vehicle)
    {
        if (vehicle == null || vehicle.wheelObjects == null || vehicle.wheelObjects.Length > 32) return false;
        foreach (GameObject wheel in vehicle.wheelObjects) if (wheel == null || !wheel.transform.IsChildOf(vehicle.transform)) return false;
        return true;
    }
    private static bool ValidMultipleState(MultiLiveLoadPose state)
    {
        string key = state.VehicleKey.ToString();
        return !string.IsNullOrWhiteSpace(key) && key.Length <= 256 && state.WheelCount >= 0 && state.WheelCount <= 32 &&
            (int)state.State >= (int)LiveLoadTestParticipantState.Waiting && (int)state.State <= (int)LiveLoadTestParticipantState.Failed &&
            ChallengeTestMotionCodec.Finite(state.WeightKg) && state.WeightKg > 0f && state.WeightKg <= LiveLoadTestRoster.MaxWeightKg &&
            ValidMultiplePose(state.Position, state.Rotation);
    }
    private static bool ValidMultiplePose(Vector3 position, Quaternion rotation)
    {
        float rotationLength = Quaternion.Dot(rotation, rotation);
        return ChallengeTestMotionCodec.Finite(position.x) && ChallengeTestMotionCodec.Finite(position.y) &&
            ChallengeTestMotionCodec.Finite(position.z) && position.sqrMagnitude <= 1e12f &&
            ChallengeTestMotionCodec.Finite(rotationLength) && rotationLength >= .5f && rotationLength <= 1.5f;
    }

    private static string BoundedMultipleLabel(string text, int index)
    {
        if (string.IsNullOrWhiteSpace(text)) return "Vehicle " + (index + 1);
        text = text.Trim();
        if (text.Length <= 64) return text;
        int length = char.IsHighSurrogate(text[63]) ? 63 : 64;
        return text.Substring(0, length);
    }
    private static string EscapeMultipleLabel(string text) => (text ?? string.Empty).Replace('<', '‹').Replace('>', '›');

    private bool HasRemoteMultiplePresentation => Object != null && Object.IsValid && !HasStateAuthority && IsSessionHost &&
        IsBridgeBuilder && !IsChallengeBusy && HostMultiLiveLoadCount > 0 && HostMultiLiveLoadCount <= 4;

    /// <summary>Guest status uses the host's roster; guests never create a local physics session.</summary>
    public bool TryGetRemoteMultiLoadStatus(out string text)
    {
        text = string.Empty;
        if (!HasRemoteMultiplePresentation) return false;
        int passed = 0, focus = -1; bool failed = false;
        bool simulationActive = false;
        for (int index = 0; index < HostMultiLiveLoadCount; index++)
        {
            var entry = HostMultiLiveLoads[index];
            if (!ValidMultipleState(entry)) return false;
            if (entry.State == LiveLoadTestParticipantState.Passed) passed++;
            if (focus < 0 && entry.State == LiveLoadTestParticipantState.Driving) focus = index;
            failed |= entry.TestFailed || entry.State == LiveLoadTestParticipantState.Failed;
            simulationActive |= entry.Simulating;
        }
        if (!simulationActive && !failed && passed != HostMultiLiveLoadCount) return false;
        if (failed) text = "LIVE LOAD TEST FAILED\n" + passed + "/" + HostMultiLiveLoadCount + " passed";
        else if (passed == HostMultiLiveLoadCount) text = "ALL LIVE LOADS PASSED · " + passed + "/" + HostMultiLiveLoadCount;
        else
        {
            if (focus < 0) for (int index = 0; index < HostMultiLiveLoadCount; index++)
                if (HostMultiLiveLoads[index].State == LiveLoadTestParticipantState.Waiting) { focus = index; break; }
            text = focus >= 0 ? "Vehicle " + (focus + 1) + "/" + HostMultiLiveLoadCount + " — " +
                EscapeMultipleLabel(HostMultiLiveLoads[focus].DisplayName.ToString()) + "\n" + passed + "/" + HostMultiLiveLoadCount + " passed" :
                "LIVE LOADS · " + passed + "/" + HostMultiLiveLoadCount + " passed";
        }
        return true;
    }

    /// <summary>Escaped result text for the guest's existing scrollable receipt.</summary>
    public string GetRemoteMultiLoadResults()
    {
        if (!HasRemoteMultiplePresentation) return string.Empty;
        var text = new StringBuilder("<b>LIVE LOAD RESULTS</b>");
        for (int index = 0; index < HostMultiLiveLoadCount; index++)
        {
            var entry = HostMultiLiveLoads[index];
            if (!ValidMultipleState(entry)) return string.Empty;
            string state = entry.State == LiveLoadTestParticipantState.Passed ? "PASSED" :
                entry.State == LiveLoadTestParticipantState.Failed ? "FAILED" : "NOT COMPLETED";
            text.Append('\n').Append(EscapeMultipleLabel(entry.DisplayName.ToString())).Append(" · ")
                .Append(entry.WeightKg.ToString("N0")).Append(" kg · ").Append(state);
        }
        return text.ToString();
    }

    private void OnDisable()
    {
        RestoreRemoteMultipleLiveLoads();
        RestoreRemoteLiveLoad();
    }

    private sealed class RemoteMultiLiveLoad
    {
        public readonly LiveLoadVehicle Vehicle;
        public readonly string Key;
        private readonly bool active, enabled;
        private readonly Rigidbody[] bodies;
        private readonly bool[] kinematic;
        private readonly float[] masses;
        private readonly Collider[] colliders;
        private readonly bool[] collision;
        private readonly UnityEngine.AI.NavMeshObstacle[] obstacles;
        private readonly bool[] obstacleEnabled;
        private readonly Transform[] transforms;
        private readonly Vector3[] positions, scales;
        private readonly Quaternion[] rotations;
        public RemoteMultiLiveLoad(LiveLoadVehicle vehicle, string key)
        {
            Vehicle = vehicle; Key = key; active = vehicle.gameObject.activeSelf; enabled = vehicle.enabled;
            transforms = vehicle.GetComponentsInChildren<Transform>(true); positions = new Vector3[transforms.Length]; rotations = new Quaternion[transforms.Length]; scales = new Vector3[transforms.Length];
            for (int i = 0; i < transforms.Length; i++) { positions[i] = transforms[i].localPosition; rotations[i] = transforms[i].localRotation; scales[i] = transforms[i].localScale; }
            if (!active) vehicle.gameObject.SetActive(true);
            vehicle.SetRemoteMultiplayerRepresentation(true); vehicle.enabled = false;
            bodies = vehicle.GetComponentsInChildren<Rigidbody>(true); kinematic = new bool[bodies.Length]; masses = new float[bodies.Length];
            for (int i = 0; i < bodies.Length; i++) { kinematic[i] = bodies[i].isKinematic; masses[i] = bodies[i].mass; bodies[i].isKinematic = true; }
            colliders = vehicle.GetComponentsInChildren<Collider>(true); collision = new bool[colliders.Length];
            for (int i = 0; i < colliders.Length; i++) { collision[i] = colliders[i].enabled; colliders[i].enabled = false; }
            obstacles = vehicle.GetComponentsInChildren<UnityEngine.AI.NavMeshObstacle>(true); obstacleEnabled = new bool[obstacles.Length];
            for (int i = 0; i < obstacles.Length; i++) { obstacleEnabled[i] = obstacles[i].enabled; obstacles[i].enabled = false; }
        }
        public void SetInspection(bool available)
        {
            if (Vehicle == null) return;
            Vehicle.enabled = available && enabled;
            for (int i = 0; i < colliders.Length; i++) if (colliders[i] != null) colliders[i].enabled = available && collision[i];
        }
        public void Restore()
        {
            if (Vehicle == null) return;
            for (int i = 0; i < transforms.Length; i++) if (transforms[i] != null) { transforms[i].localPosition = positions[i]; transforms[i].localRotation = rotations[i]; transforms[i].localScale = scales[i]; }
            for (int i = 0; i < bodies.Length; i++) if (bodies[i] != null) bodies[i].isKinematic = kinematic[i];
            for (int i = 0; i < colliders.Length; i++) if (colliders[i] != null) colliders[i].enabled = collision[i];
            for (int i = 0; i < obstacles.Length; i++) if (obstacles[i] != null) obstacles[i].enabled = obstacleEnabled[i];
            Vehicle.SetRemoteMultiplayerRepresentation(false); Vehicle.enabled = enabled;
            Vehicle.SetRemoteMultiplayerWeight(0f);
            for (int i = 0; i < bodies.Length; i++) if (bodies[i] != null) bodies[i].mass = masses[i];
            if (!active) Vehicle.gameObject.SetActive(false);
        }
    }
}
