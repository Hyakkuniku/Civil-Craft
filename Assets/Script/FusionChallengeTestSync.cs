using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Fusion;
using Fusion.Sockets;
using UnityEngine;

/// <summary>Host-only sequential simulation; guest mesh spectators; bounded motion and authoritative results.</summary>
[DisallowMultipleComponent]
public sealed class FusionChallengeTestSync : MonoBehaviour
{
    public const int MessageTag = 0x43435453;
    private const int PresentationMessage = 1, FinalFrameMessage = 2;
    [SerializeField, Range(5f, 30f)] private float viewPreparationSeconds = 20f;
    [SerializeField, Range(5f, 30f)] private float finalAcknowledgementSeconds = 15f;
    [SerializeField, Range(30f, 600f)] private float maximumTestSeconds = 120f;
    [SerializeField, Range(0.5f, 5f)] private float resultObservationSeconds = 2f;
    [SerializeField, Range(5f, 30f)] private float motionSnapshotsPerSecond = 15f;
    [SerializeField, Range(8192f, 262144f)] private float maximumMotionBytesPerSecond = 49152f;
    [SerializeField, Range(0.04f, 0.25f)] private float spectatorInterpolationSeconds = 0.1f;
    private NetworkRunner runner;
    private FusionMultiplayerAvatar host;
    private ChallengeBuildWorkspace workspace;
    private ChallengeBridgeTestRun run;
    private ChallengeBridgeTestView view;
    private int revision, index, sequence, cursor;
    private bool bound, preparing, started, resultRecorded, frozen, finalConfirmed;
    private float resultTime, frozenTime, motionEpoch, nextMotionTime, motionCredit, nextProgressTime;
    private LiveLoadVehicle vehicleSource;
    private BridgePhysicsManager settings;
    private float testWeight;
    private readonly Dictionary<GameObject, bool> vehicleVisibility = new Dictionary<GameObject, bool>();
    private byte[] pendingPresentation, pendingFinal;
    private int pendingPresentationIndex, pendingFinalIndex;
    private bool workspaceWasActive;

    private void Awake() { runner = GetComponent<NetworkRunner>(); }
    private bool ResolveChallenge()
    {
        var connection = FusionConnectionManager.Instance;
        if (runner == null || !runner.IsRunning || connection == null || connection.Runner != runner ||
            !connection.IsHostWorldSession || connection.IsNetworkSceneLoading) return false;
        host = FusionMultiplayerAvatar.FindHost(runner);
        return host != null && host.IsChallengeBusy;
    }
    private void Update()
    {
        if (!ResolveChallenge()) { Clear(); return; }
        var state = host.ChallengeState;
        if (bound && revision != state.Revision) Clear();
        if (state.Phase != MultiplayerChallengePhase.SubmissionsReady && !MultiplayerChallengeRules.IsTestPhase(state.Phase)) return;
        if (!bound)
        {
            var ui = FindObjectOfType<MultiplayerChallengeLobbyUI>(true);
            workspace = ui != null ? ui.LocalBuildWorkspace : null;
            if (workspace == null || workspace.Root == null || workspace.Location.SessionChallengeRevision != state.Revision) return;
            bound = true; revision = state.Revision;
            workspaceWasActive = workspace.Root.activeSelf;
            workspace.SetEditingEnabled(false); workspace.Root.SetActive(false);
        }
        if (runner.IsServer)
        {
            if (state.Phase == MultiplayerChallengePhase.SubmissionsReady && !preparing)
            { BeginPreparation(1); return; }
            if (state.Phase == MultiplayerChallengePhase.PreparingTest)
            {
                if (state.Deadline.ExpiredOrNotRunning(runner)) { host.FailChallengeTest("Both test views could not be prepared in time."); return; }
                if (!preparing && run != null && state.GuestTestViewReady && !started && host.StartChallengeTest(revision, index))
                {
                    try { started = true; run.Physics.ActivatePhysics(); }
                    catch (Exception error) { FailSetup(error); }
                }
            }
            if (state.Phase == MultiplayerChallengePhase.Testing && frozen && !finalConfirmed && Time.unscaledTime - frozenTime > finalAcknowledgementSeconds)
                host.FailChallengeTest("The guest did not confirm the completed test view. No saved bridge was changed.");
            if (state.Phase == MultiplayerChallengePhase.Testing && frozen && finalConfirmed)
            {
                finalConfirmed = false;
                if (index == 1) BeginPreparation(2);
                else { host.FinishChallengeTests(revision); started = false; }
            }
        }
        else
        {
            if (pendingPresentation != null && state.Phase == MultiplayerChallengePhase.PreparingTest && pendingPresentationIndex == state.TestIndex)
                PrepareGuestView(state);
            if (pendingFinal != null && pendingFinalIndex == index && view != null && state.TestIndex == index)
                ApplyFinalView();
        }
    }

    private void BeginPreparation(int nextIndex)
    {
        if (!host.PrepareChallengeTest(revision, nextIndex, viewPreparationSeconds)) return;
        index = nextIndex; preparing = true; started = resultRecorded = frozen = finalConfirmed = false;
        sequence = cursor = 0; nextMotionTime = nextProgressTime = 0f;
        run?.Dispose(); run = null;
        StartCoroutine(PrepareHostRun());
    }
    private IEnumerator PrepareHostRun()
    {
        try
        {
            var state = host.ChallengeState;
            BuildLocation site = MultiplayerChallengeRules.ResolveSite(state.SiteKey.ToString());
            ContractSO definition = ChallengeBuildWorkspace.ResolveContract(site, state.ContractKey.ToString());
            if (site == null || definition == null || definition.liveLoadMode != ContractSO.LiveLoadMode.Vehicle)
                throw new InvalidOperationException("This milestone supports vehicle crossings; this location has no vehicle test.");
            if (vehicleSource == null)
            {
                foreach (var candidate in FindObjectsOfType<LiveLoadVehicle>(true))
                {
                    if (candidate.IsSessionChallengeTestVehicle || candidate.gameObject.scene != site.gameObject.scene || candidate.assignedContract == null ||
                        candidate.assignedContract.ContractID != definition.ContractID) continue;
                    if (vehicleSource == null || candidate.gameObject.activeInHierarchy && !vehicleSource.gameObject.activeInHierarchy ||
                        candidate.gameObject.activeInHierarchy == vehicleSource.gameObject.activeInHierarchy &&
                        string.CompareOrdinal(ChallengeTestDescriptor.VehiclePath(candidate), ChallengeTestDescriptor.VehiclePath(vehicleSource)) < 0)
                        vehicleSource = candidate;
                }
                if (vehicleSource == null) throw new InvalidOperationException("No authored vehicle matches this challenge contract.");
                testWeight = vehicleSource.GetSessionTestWeight(); // Freeze identical loaded weight for BOTH designs, before hiding the world vehicle.
                foreach (var candidate in FindObjectsOfType<BridgePhysicsManager>(true))
                    if (!candidate.IsSessionChallengeTest && candidate.gameObject.scene == site.gameObject.scene) { settings = candidate; break; }
            }
            HideWorldVehicles(site, definition);
            var submissions = GetComponent<FusionChallengeSubmissionSync>();
            PlayerRef owner = MultiplayerChallengeRules.TestsHostBridge(state) ? host.Object.InputAuthority : state.Guest;
            if (submissions == null || !submissions.TryGetAcceptedBridge(owner, revision, out var graph, out _))
                throw new InvalidOperationException("The accepted bridge packet is unavailable.");
            if (ChallengeBridgeSubmissionRules.Validate(graph, site, definition, ChallengeBridgeSubmissionRules.CreateMaterialCatalog(definition),
                workspace.Creator.pierBaseY, out _) != ChallengeSubmissionError.None)
                throw new InvalidOperationException("The accepted bridge no longer matches the challenge definition.");
            run = ChallengeBridgeTestRun.Create(graph, site, definition, workspace.Creator, settings, vehicleSource, testWeight, revision, index);
        }
        catch (Exception error) { FailSetup(error); yield break; }
        // Start must subscribe the isolated vehicle before the first settling event,
        // and Initialize's retired visual children must finish deferred destruction.
        yield return null;
        if (!ResolveChallenge() || !bound || host.ChallengeState.Revision != revision || host.ChallengeState.TestIndex != index || run == null) yield break;
        try
        {
            var descriptor = new ChallengeTestDescriptor { Bridge = run.CapturePresentation(host.ChallengeState.SiteKey.ToString()),
                VehicleKey = ChallengeTestDescriptor.VehiclePath(vehicleSource), Weight = testWeight,
                VehiclePoses = new HostWorldBridgePose[run.Vehicle.wheelObjects.Length + 1] };
            descriptor.VehiclePoses[0] = HostWorldBridgePose.World(run.Vehicle.transform);
            for (int i = 0; i < run.Vehicle.wheelObjects.Length; i++) descriptor.VehiclePoses[i + 1] = HostWorldBridgePose.World(run.Vehicle.wheelObjects[i].transform);
            runner.SendReliableDataToPlayer(host.ChallengeState.Guest, ReliableKey.FromInts(MessageTag, PresentationMessage, revision, index), descriptor.Encode());
            preparing = false; motionEpoch = Time.unscaledTime; motionCredit = 0f;
        }
        catch (Exception error) { FailSetup(error); }
    }
    private void HideWorldVehicles(BuildLocation site, ContractSO definition)
    {
        foreach (var vehicle in FindObjectsOfType<LiveLoadVehicle>(true))
        {
            if (vehicle.IsSessionChallengeTestVehicle || vehicle.gameObject.scene != site.gameObject.scene || vehicle.assignedContract == null ||
                vehicle.assignedContract.ContractID != definition.ContractID || vehicleVisibility.ContainsKey(vehicle.gameObject)) continue;
            vehicleVisibility.Add(vehicle.gameObject, vehicle.gameObject.activeSelf); vehicle.gameObject.SetActive(false);
        }
    }
    private void PrepareGuestView(MultiplayerChallengeState state)
    {
        byte[] packet = pendingPresentation; pendingPresentation = null;
        view?.Dispose(); view = null; index = state.TestIndex;
        try
        {
            var descriptor = ChallengeTestDescriptor.Decode(packet);
            BuildLocation site = MultiplayerChallengeRules.ResolveSite(state.SiteKey.ToString());
            ContractSO definition = ChallengeBuildWorkspace.ResolveContract(site, state.ContractKey.ToString());
            if (site == null || definition == null || descriptor.Bridge.Name != state.SiteKey.ToString() || descriptor.Bridge.Nodes.Count != 0 ||
                descriptor.Bridge.Bars.Count != (MultiplayerChallengeRules.TestsHostBridge(state) ? state.HostSubmittedBars : state.GuestSubmittedBars))
                throw new InvalidDataException("The test view does not match the accepted bridge.");
            HideWorldVehicles(site, definition);
            BridgePhysicsManager guestSettings = FindObjectOfType<BridgePhysicsManager>(true);
            view = new ChallengeBridgeTestView(descriptor, site, workspace.Creator, ChallengeBridgeSubmissionRules.CreateMaterialCatalog(definition), guestSettings);
            LocalAvatar()?.ReportChallengeTestView(revision, index, true, false);
        }
        catch (Exception error)
        {
            Debug.LogWarning("[Challenge test view] " + error.Message, this);
            LocalAvatar()?.ReportChallengeTestView(revision, index, false, false);
        }
    }
    private FusionMultiplayerAvatar LocalAvatar() => runner != null && runner.TryGetPlayerObject(runner.LocalPlayer, out NetworkObject obj) && obj != null
        ? obj.GetComponent<FusionMultiplayerAvatar>() : null;

    private void FixedUpdate()
    {
        if (!bound || !started || run == null || runner == null || !runner.IsServer || !ResolveChallenge() ||
            host.ChallengeState.Revision != revision || host.ChallengeState.Phase != MultiplayerChallengePhase.Testing || host.ChallengeState.TestIndex != index) return;
        if (!resultRecorded)
        {
            var outcome = run.Step(Time.fixedDeltaTime, LevelFailedManager.Instance, maximumTestSeconds);
            if (outcome != ChallengeTestOutcome.None && host.RecordChallengeTest(revision, index, outcome, run.Elapsed, run.PeakStress))
            { resultRecorded = true; resultTime = Time.unscaledTime; }
        }
        if (Time.unscaledTime >= nextProgressTime)
        { nextProgressTime = Time.unscaledTime + 0.1f; host.PublishChallengeTestProgress(run.Elapsed, run.Physics.GetMaxBridgeStress()); }
        if (resultRecorded && !frozen && Time.unscaledTime - resultTime >= resultObservationSeconds)
        {
            run.Physics.FreezeSessionChallengeTest();
            frozen = true; frozenTime = Time.unscaledTime;
            try
            {
                var chunks = CaptureFinalFrame();
                runner.SendReliableDataToPlayer(host.ChallengeState.Guest, ReliableKey.FromInts(MessageTag, FinalFrameMessage, revision, index), chunks);
            }
            catch (Exception error) { FailSetup(error); }
        }
    }
    private void LateUpdate()
    {
        if (view != null) view.Render(spectatorInterpolationSeconds);
        if (!bound || run == null || preparing || frozen || runner == null || !runner.IsServer || host == null || !host.IsChallengeBusy) return;
        motionCredit = Mathf.Min(maximumMotionBytesPerSecond * 0.15f, motionCredit + Time.unscaledDeltaTime * maximumMotionBytesPerSecond);
        if (Time.unscaledTime < nextMotionTime || run.MotionTargets.Count == 0) return;
        if (cursor == 0) sequence = unchecked(sequence + 1);
        int messages = 0;
        while (cursor < run.MotionTargets.Count && messages++ < 16)
        {
            byte[] chunk = ChallengeTestMotionCodec.Encode(sequence, cursor, Time.unscaledTime - motionEpoch, run.MotionTargets, run.Bars);
            float chargedBytes = chunk.Length + 64; // Include an allowance for Fusion/transport headers.
            if (motionCredit < chargedBytes) return;
            motionCredit -= chargedBytes;
            host.PublishChallengeTestMotion(revision, index, chunk);
            cursor += Mathf.Min(ChallengeTestMotionCodec.TargetsPerChunk, run.MotionTargets.Count - cursor);
        }
        if (cursor >= run.MotionTargets.Count)
        { cursor = 0; nextMotionTime = Time.unscaledTime + 1f / motionSnapshotsPerSecond; }
    }

    public void ReceiveMotion(int receivedRevision, int receivedIndex, byte[] packet)
    {
        if (runner == null || runner.IsServer || !bound || view == null || receivedRevision != revision || receivedIndex != index) return;
        try { view.Receive(ChallengeTestMotionCodec.Decode(packet)); }
        catch (Exception error) when (error is InvalidDataException || error is EndOfStreamException) { /* Drop invalid motion; no gameplay state comes from it. */ }
    }
    public void OnReliableData(PlayerRef sender, ReliableKey key, ReadOnlySpan<byte> data)
    {
        key.GetInts(out int tag, out int kind, out int receivedRevision, out int receivedIndex);
        if (tag != MessageTag || runner == null || runner.IsServer || !ResolveChallenge() || host.ChallengeState.Revision != receivedRevision ||
            (receivedIndex != 1 && receivedIndex != 2) || data.Length > HostWorldBridgeSnapshotCodec.MaxPacketBytes + 4096 ||
            (host.ChallengeState.Phase != MultiplayerChallengePhase.SubmissionsReady && !MultiplayerChallengeRules.IsTestPhase(host.ChallengeState.Phase))) return;
        // A reliable packet may arrive before the corresponding network-state tick.
        // Retain at most the next descriptor/final frame, scoped by revision/index.
        if (kind == PresentationMessage && receivedIndex >= host.ChallengeState.TestIndex)
        { pendingPresentation = data.ToArray(); pendingPresentationIndex = receivedIndex; }
        else if (kind == FinalFrameMessage && receivedIndex >= host.ChallengeState.TestIndex)
        { pendingFinal = data.ToArray(); pendingFinalIndex = receivedIndex; }
    }
    private byte[] CaptureFinalFrame()
    {
        sequence = unchecked(sequence + 1);
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            int count = (run.MotionTargets.Count + ChallengeTestMotionCodec.TargetsPerChunk - 1) / ChallengeTestMotionCodec.TargetsPerChunk;
            writer.Write(count);
            for (int offset = 0; offset < run.MotionTargets.Count; offset += ChallengeTestMotionCodec.TargetsPerChunk)
            {
                byte[] chunk = ChallengeTestMotionCodec.Encode(sequence, offset, Time.unscaledTime - motionEpoch, run.MotionTargets, run.Bars);
                writer.Write(chunk.Length); writer.Write(chunk);
            }
            return stream.ToArray();
        }
    }
    private void ApplyFinalView()
    {
        byte[] packet = pendingFinal; pendingFinal = null;
        try
        {
            using (var stream = new MemoryStream(packet, false))
            using (var reader = new BinaryReader(stream))
            {
                int count = reader.ReadInt32();
                if (count != (view.TargetCount + ChallengeTestMotionCodec.TargetsPerChunk - 1) / ChallengeTestMotionCodec.TargetsPerChunk)
                    throw new InvalidDataException("Invalid final frame count.");
                int nextOffset = 0;
                for (int i = 0; i < count; i++)
                {
                    int length = reader.ReadInt32();
                    if (length < 16 || length > 256 || length > stream.Length - stream.Position) throw new InvalidDataException("Invalid final frame chunk.");
                    var chunk = ChallengeTestMotionCodec.Decode(reader.ReadBytes(length));
                    if (chunk.Offset != nextOffset || chunk.TotalTargets != view.TargetCount) throw new InvalidDataException("Missing final frame targets.");
                    nextOffset += chunk.Positions.Length; view.Receive(chunk);
                }
                if (stream.Position != stream.Length || nextOffset != view.TargetCount) throw new InvalidDataException("Unexpected final frame data.");
            }
            view.CompleteFinalFrame(); LocalAvatar()?.ReportChallengeTestView(revision, index, true, true);
        }
        catch (Exception error)
        { Debug.LogWarning("[Challenge test] " + error.Message, this); LocalAvatar()?.ReportChallengeTestView(revision, index, false, true); }
    }
    internal void ConfirmFinalView(int receivedRevision, int receivedIndex, bool success)
    {
        if (!bound || !frozen || revision != receivedRevision || index != receivedIndex || runner == null || !runner.IsServer) return;
        if (success) finalConfirmed = true; else host.FailChallengeTest("The guest could not display the completed test frame.");
    }
    private void FailSetup(Exception error)
    {
        Debug.LogWarning("[Challenge test] " + error.Message + " Saved bridges were not changed.", this);
        if (host != null && runner != null && runner.IsServer) host.FailChallengeTest(error.Message);
    }
    public void Clear()
    {
        StopAllCoroutines();
        run?.Dispose(); run = null; view?.Dispose(); view = null;
        if (workspace != null && workspace.Root != null) workspace.Root.SetActive(workspaceWasActive);
        foreach (var entry in vehicleVisibility) if (entry.Key != null) entry.Key.SetActive(entry.Value);
        vehicleVisibility.Clear(); workspace = null; vehicleSource = null; settings = null; host = null;
        pendingPresentation = pendingFinal = null;
        bound = preparing = started = resultRecorded = frozen = finalConfirmed = false;
        revision = index = sequence = cursor = 0;
    }
    private void OnDisable() { Clear(); }
    private void OnDestroy() { Clear(); }
}
