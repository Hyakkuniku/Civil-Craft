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
    private const int PresentationMessage = 1, FinalFrameMessage = 2, RequestPresentationMessage = 3;
    private const float PresentationRequestInterval = 2f;
    private const int MaximumPresentationRequests = 3;
    [SerializeField, Range(5f, 30f)] private float viewPreparationSeconds = 20f;
    [Tooltip("Minimum on-screen introduction before each test, on BOTH peers. Physics remains paused until the guest acknowledges it.")]
    [SerializeField, Range(1f, 8f)] private float testIntroductionSeconds = 3f;
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
    private MultiplayerChallengeLobbyUI presentationUI;
    private bool guestIntroductionPending;
    private byte[] preparedPresentation;
    private float nextPresentationRequest, nextPresentationResend;
    private int presentationRequestIndex, presentationRequests;
    private readonly ChallengeTestPacketAssembly presentationAssembly = new ChallengeTestPacketAssembly();
    private readonly ChallengeTestPacketAssembly finalAssembly = new ChallengeTestPacketAssembly();
    private Coroutine outgoingPacket;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private int diagnosticIndex;
    private float diagnosticStartedAt;
    private bool diagnosticReported;
    private string presentationReceipt = "none";
    private int presentationSends, receivedPresentationRequests;
#endif

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
        if (bound && revision != state.Revision)
        {
            Clear();
            if (!ResolveChallenge()) return;
        }
        if (state.Phase != MultiplayerChallengePhase.SubmissionsReady && !MultiplayerChallengeRules.IsTestPhase(state.Phase)) return;
        if (!bound)
        {
            var ui = FindObjectOfType<MultiplayerChallengeLobbyUI>(true);
            presentationUI = ui;
            workspace = ui != null ? ui.LocalBuildWorkspace : null;
            if (workspace == null || workspace.Root == null || workspace.Location.SessionChallengeRevision != state.Revision) return;
            bound = true; revision = state.Revision;
            workspaceWasActive = workspace.Root.activeSelf;
            workspace.SetEditingEnabled(false); workspace.Root.SetActive(false);
        }
        TracePreparationWait(state);
        if (runner.IsServer)
        {
            if (state.Phase == MultiplayerChallengePhase.SubmissionsReady && !preparing)
            { BeginPreparation(1); return; }
            if (state.Phase == MultiplayerChallengePhase.PreparingTest)
            {
                if (state.Deadline.ExpiredOrNotRunning(runner)) { host.FailChallengeTest("Both test views could not be prepared in time."); return; }
                bool introductionComplete = !preparing && run != null && ShowIntroduction(state);
                if (introductionComplete && state.GuestTestViewReady && !started && host.StartChallengeTest(revision, index))
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
            // Name presentation follows replicated preparation, not descriptor
            // arrival. Readiness still requires BOTH a prepared view and the intro.
            bool introductionComplete = state.Phase == MultiplayerChallengePhase.PreparingTest && ShowIntroduction(state);
            if (pendingPresentation != null && state.Phase == MultiplayerChallengePhase.PreparingTest && pendingPresentationIndex == state.TestIndex)
                PrepareGuestView(state);
            if (state.Phase == MultiplayerChallengePhase.PreparingTest && (view == null || index != state.TestIndex))
                RequestMissingPresentation(state);
            // Readiness includes an actually visible introduction. Late descriptor
            // delivery never shortens the guest's time to read whose bridge is next.
            if (guestIntroductionPending && state.Phase == MultiplayerChallengePhase.PreparingTest && state.TestIndex == index &&
                view != null && introductionComplete && LocalAvatar() != null)
            {
                guestIntroductionPending = false;
                LocalAvatar().ReportChallengeTestView(revision, index, true, false);
            }
            if (pendingFinal != null && pendingFinalIndex == index && view != null && state.TestIndex == index)
                ApplyFinalView();
        }
    }

    private void RequestMissingPresentation(MultiplayerChallengeState state)
    {
        if (presentationRequestIndex != state.TestIndex)
        {
            presentationRequestIndex = state.TestIndex;
            presentationRequests = 0;
            nextPresentationRequest = Time.unscaledTime + PresentationRequestInterval;
        }
        if (presentationRequests >= MaximumPresentationRequests || Time.unscaledTime < nextPresentationRequest) return;
        presentationRequests++;
        nextPresentationRequest = Time.unscaledTime + PresentationRequestInterval;
        LocalAvatar()?.RequestChallengeTestPresentation(revision, state.TestIndex);
    }

    private void SendPreparedPresentation()
    {
        QueueTestPacket(PresentationMessage, preparedPresentation);
        nextPresentationResend = Time.unscaledTime + PresentationRequestInterval;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        presentationSends++;
#endif
    }

    private void QueueTestPacket(int kind, byte[] packet)
    {
        if (outgoingPacket != null) return; // Do not restart a large transfer on a retry.
        if (packet == null || packet.Length == 0 || packet.Length > ChallengeTestPacketAssembly.MaximumBytes)
            throw new InvalidDataException("Invalid test transfer size.");
        outgoingPacket = StartCoroutine(SendTestPacket(kind, packet, revision, index));
    }
    private IEnumerator SendTestPacket(int kind, byte[] packet, int packetRevision, int packetIndex)
    {
        // Pace only large designs: up to 1280 payload bytes each 20ms. Movement
        // retains its existing unreliable channel and bandwidth/tick settings.
        for (int offset = 0, batch = 0; offset < packet.Length; offset += ChallengeTestPacketAssembly.ChunkBytes)
        {
            if (!ResolveChallenge() || revision != packetRevision || index != packetIndex) break;
            int length = Math.Min(ChallengeTestPacketAssembly.ChunkBytes, packet.Length - offset);
            var chunk = new byte[length]; Buffer.BlockCopy(packet, offset, chunk, 0, length);
            try { host.PublishChallengeTestPacket(host.ChallengeState.Guest, packetRevision, packetIndex, kind, packet.Length, offset, chunk); }
            catch (Exception error) { FailSetup(error); break; }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (offset == 0)
                Debug.Log($"[Challenge test transfer] sent kind={kind} revision={packetRevision} index={packetIndex} bytes={packet.Length} via reliable RPC", this);
#endif
            if (++batch == 4 && offset + length < packet.Length)
            { batch = 0; yield return new WaitForSecondsRealtime(0.02f); }
        }
        // Always yield once: StartCoroutine must assign its handle before cleanup.
        yield return null;
        outgoingPacket = null;
    }

    internal void ReceivePresentationRequest(PlayerRef sender, int receivedRevision, int receivedIndex)
    {
        if (!ResolveChallenge() || !runner.IsServer) return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        receivedPresentationRequests++;
#endif
        if (preparedPresentation != null &&
            CanServePresentation(host.ChallengeState, sender, receivedRevision, receivedIndex, revision, index) &&
            Time.unscaledTime >= nextPresentationResend)
            SendPreparedPresentation();
    }

    internal void ReceiveTestPacketChunk(int receivedRevision, int receivedIndex, int kind, int total, int offset, byte[] chunk)
    {
        if (runner == null || runner.IsServer || !ResolveChallenge() ||
            (kind != PresentationMessage && kind != FinalFrameMessage) ||
            !CanBufferTestPacket(host.ChallengeState, receivedRevision, receivedIndex, kind == FinalFrameMessage)) return;
        // A new revision can arrive before Update retires the previous workspace.
        if (bound && revision != receivedRevision)
        { Clear(); if (!ResolveChallenge()) return; }
        var assembly = kind == PresentationMessage ? presentationAssembly : finalAssembly;
        if (!assembly.Add(receivedRevision, receivedIndex, total, offset, chunk, out byte[] packet)) return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (kind == PresentationMessage)
            presentationReceipt = $"rpc:{receivedRevision}/{receivedIndex}/{assembly.ReceivedBytes}/{total}B";
#endif
        if (packet == null) return;
        OnReliableData(runner.LocalPlayer, ReliableKey.FromInts(MessageTag, kind, receivedRevision, receivedIndex), packet);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log($"[Challenge test transfer] received kind={kind} revision={receivedRevision} index={receivedIndex} bytes={packet.Length} via reliable RPC", this);
#endif
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    private void TracePreparationWait(MultiplayerChallengeState state)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (state.Phase != MultiplayerChallengePhase.PreparingTest) return;
        if (diagnosticIndex != state.TestIndex)
        { diagnosticIndex = state.TestIndex; diagnosticStartedAt = Time.unscaledTime; diagnosticReported = false; }
        if (diagnosticReported || Time.unscaledTime - diagnosticStartedAt < 6f) return;
        diagnosticReported = true;
        var local = LocalAvatar();
        Debug.LogWarning($"[Challenge preparation wait] host={runner.IsServer} revision={revision} index={index} stateIndex={state.TestIndex} " +
            $"hostRun={run != null} preparing={preparing} spectator={view != null} bufferedBytes={pendingPresentation?.Length ?? 0} " +
            $"bufferedIndex={pendingPresentationIndex} guestIntroPending={guestIntroductionPending} guestReady={state.GuestTestViewReady} " +
            $"descriptorBytes={preparedPresentation?.Length ?? 0} requests={presentationRequests} receipt={presentationReceipt} " +
            $"descriptorSends={presentationSends} receivedRequests={receivedPresentationRequests} " +
            $"localAuthority={local != null && local.HasInputAuthority} physicsActive={run != null && run.Physics.IsSimulationActive} " +
            (presentationUI != null ? presentationUI.TestIntroductionDiagnostic : "introUI=missing"), this);
#endif
    }

    private void BeginPreparation(int nextIndex)
    {
        if (!host.PrepareChallengeTest(revision, nextIndex, viewPreparationSeconds + IntroductionDuration)) return;
        index = nextIndex; preparing = true; started = resultRecorded = frozen = finalConfirmed = false;
        preparedPresentation = null; nextPresentationResend = 0f;
        sequence = cursor = 0; nextMotionTime = nextProgressTime = 0f;
        run?.Dispose(); run = null;
        StartCoroutine(PrepareHostRun());
    }
    private float IntroductionDuration => ChallengeTestMotionCodec.Finite(testIntroductionSeconds)
        ? Mathf.Clamp(testIntroductionSeconds, 1f, 8f) : 3f;
    private bool ShowIntroduction(MultiplayerChallengeState state)
    {
        if (presentationUI == null) return false;
        string playerName = host.MapPlayerName;
        if (!MultiplayerChallengeRules.TestsHostBridge(state))
            playerName = runner.TryGetPlayerObject(state.Guest, out NetworkObject obj) && obj != null
                ? obj.GetComponent<FusionMultiplayerAvatar>()?.MapPlayerName ?? "Guest" : "Guest";
        return presentationUI.PresentTestIntroduction(state, playerName, IntroductionDuration);
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
                vehicleSource = ChallengeTestDescriptor.ResolveVehicle(state.ChallengeVehicleKey.ToString(), site.gameObject.scene);
                if (vehicleSource == null) throw new InvalidOperationException("No authored vehicle matches this challenge contract.");
                testWeight = state.ChallengeVehicleWeight; // Same host-frozen loaded mass shown during building, for BOTH designs.
                if (!ChallengeTestMotionCodec.Finite(testWeight) || testWeight <= 0f)
                    throw new InvalidOperationException("The challenge live load weight is invalid.");
                foreach (var candidate in FindObjectsOfType<BridgePhysicsManager>(true))
                    if (!candidate.IsSessionChallengeTest && candidate.gameObject.scene == site.gameObject.scene) { settings = candidate; break; }
            }
            HideWorldVehicles(site, definition);
            var submissions = GetComponent<FusionChallengeSubmissionSync>();
            PlayerRef owner = MultiplayerChallengeRules.TestsHostBridge(state) ? host.Object.InputAuthority : state.Guest;
            if (submissions == null || !submissions.TryGetAcceptedBridge(owner, revision, out var graph, out _))
                throw new InvalidOperationException("The accepted bridge packet is unavailable.");
            if (ChallengeBridgeSubmissionRules.Validate(graph, site, definition, ChallengeBridgeSubmissionRules.CreateMaterialCatalog(definition),
                workspace.Creator.pierBaseY, out _, workspace.Creator.nodeSnapDepthTolerance) != ChallengeSubmissionError.None)
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
                VehiclePoses = new HostWorldBridgePose[run.Vehicle.wheelObjects.Length + 1],
                BoatKeys = run.BoatKeys.ToArray(), BoatPoses = new HostWorldBridgePose[run.Boats.Count] };
            descriptor.VehiclePoses[0] = HostWorldBridgePose.World(run.Vehicle.transform);
            for (int i = 0; i < run.Vehicle.wheelObjects.Length; i++) descriptor.VehiclePoses[i + 1] = HostWorldBridgePose.World(run.Vehicle.wheelObjects[i].transform);
            for (int i = 0; i < run.Boats.Count; i++) descriptor.BoatPoses[i] = HostWorldBridgePose.World(run.Boats[i].transform);
            // Keep the immutable view until this test ends. A transport callback
            // may precede the guest's matching snapshot; it can explicitly pull
            // the same view after reaching PreparingTest instead of timing out.
            preparedPresentation = descriptor.Encode();
            SendPreparedPresentation();
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
        foreach (var boat in ChallengeTestDescriptor.SelectBoats(site, definition))
        {
            if (vehicleVisibility.ContainsKey(boat.gameObject)) continue;
            vehicleVisibility.Add(boat.gameObject, boat.gameObject.activeSelf); boat.gameObject.SetActive(false);
        }
    }
    private void PrepareGuestView(MultiplayerChallengeState state)
    {
        byte[] packet = pendingPresentation; pendingPresentation = null;
        guestIntroductionPending = false;
        view?.Dispose(); view = null; index = state.TestIndex;
        try
        {
            var descriptor = ChallengeTestDescriptor.Decode(packet);
            BuildLocation site = MultiplayerChallengeRules.ResolveSite(state.SiteKey.ToString());
            ContractSO definition = ChallengeBuildWorkspace.ResolveContract(site, state.ContractKey.ToString());
            if (site == null || definition == null || descriptor.Bridge.Name != state.SiteKey.ToString() || descriptor.Bridge.Nodes.Count != 0 ||
                descriptor.VehicleKey != state.ChallengeVehicleKey.ToString() || descriptor.Weight != state.ChallengeVehicleWeight ||
                descriptor.Bridge.Bars.Count != (MultiplayerChallengeRules.TestsHostBridge(state) ? state.HostSubmittedBars : state.GuestSubmittedBars))
                throw new InvalidDataException("The test view does not match the accepted bridge.");
            HideWorldVehicles(site, definition);
            BridgePhysicsManager guestSettings = FindObjectOfType<BridgePhysicsManager>(true);
            view = new ChallengeBridgeTestView(descriptor, site, workspace.Creator, ChallengeBridgeSubmissionRules.CreateMaterialCatalog(definition), guestSettings);
            guestIntroductionPending = true;
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
                QueueTestPacket(FinalFrameMessage, chunks);
            }
            catch (Exception error) { FailSetup(error); }
        }
    }
    private void LateUpdate()
    {
        if (view != null) view.Render(spectatorInterpolationSeconds);
        if (!bound || !started || run == null || preparing || frozen || runner == null || !runner.IsServer || host == null || !host.IsChallengeBusy) return;
        motionCredit = Mathf.Min(maximumMotionBytesPerSecond * 0.15f, motionCredit + Time.unscaledDeltaTime * maximumMotionBytesPerSecond);
        if (Time.unscaledTime < nextMotionTime || run.MotionTargets.Count == 0) return;
        if (cursor == 0) sequence = unchecked(sequence + 1);
        int messages = 0;
        while (cursor < run.MotionTargets.Count && messages++ < 16)
        {
            byte[] chunk = ChallengeTestMotionCodec.Encode(sequence, cursor, Time.unscaledTime - motionEpoch,
                run.MotionTargets, run.Bars, run.Vehicle.wheelObjects.Length, run.Boats);
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
        if (tag != MessageTag || runner == null) return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!runner.IsServer && kind == PresentationMessage)
            presentationReceipt = $"callback:{receivedRevision}/{receivedIndex}/{data.Length}B";
#endif
        if (!ResolveChallenge())
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!runner.IsServer && kind == PresentationMessage) presentationReceipt += ":inactive-challenge";
#endif
            return;
        }
        if (runner.IsServer)
        {
            if (kind == RequestPresentationMessage && data.Length == 1 && data[0] == 1)
                ReceivePresentationRequest(sender, receivedRevision, receivedIndex);
            return;
        }
        if (
            (kind != PresentationMessage && kind != FinalFrameMessage) || data.Length == 0 ||
            data.Length > HostWorldBridgeSnapshotCodec.MaxPacketBytes + 4096 ||
            !CanBufferTestPacket(host.ChallengeState, receivedRevision, receivedIndex, kind == FinalFrameMessage))
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (kind == PresentationMessage) presentationReceipt += $":rejected-state={host.ChallengeState.Phase}/{host.ChallengeState.Revision}/{host.ChallengeState.TestIndex}";
#endif
            return;
        }
        // Reliable transfers and replicated snapshots have independent timing.
        // The first descriptor can arrive while the guest still sees Building.
        // Keep it until PreparingTest; never construct a view or release physics
        // based on an early packet alone. Fusion Host/Client callbacks identify
        // the server connection with the client's PlayerRef, not the host's ID.
        if (kind == PresentationMessage && !(view != null && index == receivedIndex))
        { pendingPresentation = data.ToArray(); pendingPresentationIndex = receivedIndex; }
        else if (kind == FinalFrameMessage)
        { pendingFinal = data.ToArray(); pendingFinalIndex = receivedIndex; }
    }

    internal static bool CanServePresentation(MultiplayerChallengeState state, PlayerRef sender, int receivedRevision,
        int receivedIndex, int preparedRevision, int preparedIndex) =>
        sender == state.Guest && sender != PlayerRef.None && state.Phase == MultiplayerChallengePhase.PreparingTest &&
        !state.GuestTestViewReady && receivedRevision == state.Revision && receivedRevision == preparedRevision &&
        (receivedIndex == 1 || receivedIndex == 2) && receivedIndex == state.TestIndex && receivedIndex == preparedIndex;

    internal static bool CanBufferTestPacket(MultiplayerChallengeState state, int receivedRevision, int receivedIndex, bool finalFrame)
    {
        if (state.Revision != receivedRevision || (receivedIndex != 1 && receivedIndex != 2)) return false;
        if (state.Phase == MultiplayerChallengePhase.Building || state.Phase == MultiplayerChallengePhase.SubmissionsReady)
            return !finalFrame && state.TestIndex == 0 && receivedIndex == 1;
        if (state.Phase != MultiplayerChallengePhase.PreparingTest && state.Phase != MultiplayerChallengePhase.Testing) return false;
        return receivedIndex == state.TestIndex ||
            (!finalFrame && state.TestIndex == 1 && receivedIndex == 2);
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
                byte[] chunk = ChallengeTestMotionCodec.Encode(sequence, offset, Time.unscaledTime - motionEpoch,
                    run.MotionTargets, run.Bars, run.Vehicle.wheelObjects.Length, run.Boats);
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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        diagnosticIndex = 0; diagnosticReported = false; presentationReceipt = "none";
        presentationSends = receivedPresentationRequests = 0;
#endif
        StopAllCoroutines();
        outgoingPacket = null; presentationAssembly.Clear(); finalAssembly.Clear();
        run?.Dispose(); run = null; view?.Dispose(); view = null;
        if (workspace != null && workspace.Root != null) workspace.Root.SetActive(workspaceWasActive);
        foreach (var entry in vehicleVisibility) if (entry.Key != null) entry.Key.SetActive(entry.Value);
        vehicleVisibility.Clear(); workspace = null; vehicleSource = null; settings = null; host = null;
        presentationUI = null; guestIntroductionPending = false;
        pendingPresentation = pendingFinal = null;
        preparedPresentation = null; presentationRequests = presentationRequestIndex = 0;
        nextPresentationRequest = nextPresentationResend = 0f;
        bound = preparing = started = resultRecorded = frozen = finalConfirmed = false;
        revision = index = sequence = cursor = 0;
    }
    private void OnDisable() { Clear(); }
    private void OnDestroy() { Clear(); }
}

/// <summary>One bounded immutable transfer per test/kind; duplicate chunks are idempotent.</summary>
internal sealed class ChallengeTestPacketAssembly
{
    internal const int ChunkBytes = 320;
    internal const int MaximumBytes = ChallengeTestDescriptor.MaxPacketBytes;
    private byte[] bytes;
    private bool[] received;
    private int revision, index, count;
    internal int ReceivedBytes { get; private set; }
    internal bool Add(int rev, int test, int total, int offset, byte[] chunk, out byte[] packet)
    {
        packet = null;
        if (rev <= 0 || (test != 1 && test != 2) || total <= 0 || total > MaximumBytes ||
            offset < 0 || offset >= total || offset % ChunkBytes != 0 || chunk == null ||
            chunk.Length != Math.Min(ChunkBytes, total - offset)) return false;
        if (bytes == null || revision != rev || index != test)
        {
            Clear(); revision = rev; index = test;
            bytes = new byte[total]; received = new bool[(total + ChunkBytes - 1) / ChunkBytes];
        }
        if (bytes.Length != total) return false;
        int slot = offset / ChunkBytes;
        if (received[slot])
        {
            for (int i = 0; i < chunk.Length; i++) if (bytes[offset + i] != chunk[i]) return false;
            return true;
        }
        Buffer.BlockCopy(chunk, 0, bytes, offset, chunk.Length);
        received[slot] = true; count++; ReceivedBytes += chunk.Length;
        if (count == received.Length) packet = bytes;
        return true;
    }
    internal void Clear()
    { bytes = null; received = null; revision = index = count = ReceivedBytes = 0; }
}
