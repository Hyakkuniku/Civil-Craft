using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Fusion;
using Fusion.Sockets;
using UnityEngine;

/// <summary>One bounded reliable graph transfer per submission, separate from saved-world synchronization.</summary>
[DisallowMultipleComponent]
public sealed class FusionChallengeSubmissionSync : MonoBehaviour
{
    public const int MessageTag = 0x43435342;
    private const int SubmitMessage = 1, ReplyMessage = 2;
    [SerializeField, Min(5f)] private float slowTransferNoticeSeconds = 15f;
    private NetworkRunner runner;
    private int boundRevision, attempt, pendingAttempt;
    private bool bound, pending, locallyAccepted;
    private float pendingSince;
    private string localMessage;
    private readonly Dictionary<PlayerRef, AcceptedBridge> accepted = new Dictionary<PlayerRef, AcceptedBridge>();
    private readonly Dictionary<PlayerRef, float> lastAttemptTimes = new Dictionary<PlayerRef, float>();
    private sealed class AcceptedBridge { public byte[] Packet; public int Attempt, Bars; public float Cost; }

    public bool LocalEditingLocked => pending || locallyAccepted;
    public bool IsSending => pending;
    public bool IsLocallyAccepted => locallyAccepted;
    public string LocalMessage => pending && Time.unscaledTime - pendingSince >= slowTransferNoticeSeconds
        ? "Still waiting for host validation. Your bridge stays locked; you can cancel the challenge."
        : localMessage;

    private void Awake() { runner = GetComponent<NetworkRunner>(); }

    private bool TryGetChallenge(out FusionMultiplayerAvatar host)
    {
        host = null;
        FusionConnectionManager connection = FusionConnectionManager.Instance;
        if (runner == null || !runner.IsRunning || connection == null || connection.Runner != runner ||
            !connection.IsHostWorldSession || connection.IsNetworkSceneLoading) return false;
        host = FusionMultiplayerAvatar.FindHost(runner);
        return host != null && host.IsChallengeBusy;
    }

    private void BindRevision(int revision)
    {
        if (bound && boundRevision == revision) return;
        Clear(); bound = true; boundRevision = revision;
    }

    private void Update()
    {
        if (!TryGetChallenge(out var host)) { Clear(); return; }
        BindRevision(host.ChallengeState.Revision);
        if (MultiplayerChallengeRules.HasSubmitted(host.ChallengeState, runner.LocalPlayer, host.Object.InputAuthority))
        {
            pending = false; locallyAccepted = true;
            localMessage = ChallengeBridgeSubmissionRules.Message(ChallengeSubmissionError.None);
        }
    }

    public void SubmitLocalBridge(ChallengeBuildWorkspace workspace)
    {
        if (!TryGetChallenge(out var host) || workspace?.Location == null) return;
        BindRevision(host.ChallengeState.Revision);
        if (LocalEditingLocked || !MultiplayerChallengeRules.CanSubmit(host.ChallengeState, runner.LocalPlayer,
            host.Object.InputAuthority, workspace.Location.SessionChallengeRevision)) return;
        pending = true; pendingSince = Time.unscaledTime;
        pendingAttempt = attempt = attempt == int.MaxValue ? 1 : attempt + 1;
        localMessage = "Submitting bridge for host validation...";
        workspace.Creator.CancelAllModes();
        workspace.Creator.SetActiveMaterial(null); // Hide a pier preview as well as unfinished drawing/paste ghosts.
        workspace.SetEditingEnabled(false);
        StartCoroutine(CaptureAndSend(workspace, boundRevision, pendingAttempt));
    }

    private IEnumerator CaptureAndSend(ChallengeBuildWorkspace workspace, int revision, int request)
    {
        // CancelCreation uses deferred Destroy. Never serialize those discarded ghosts this frame.
        yield return null;
        if (!pending || pendingAttempt != request || !TryGetChallenge(out var host) || host.ChallengeState.Revision != revision ||
            workspace?.Location == null || !MultiplayerChallengeRules.CanSubmit(host.ChallengeState, runner.LocalPlayer,
                host.Object.InputAuthority, revision)) yield break;
        byte[] packet;
        try
        {
            ChallengeBridgeSubmission graph = ChallengeBridgeSubmissionRules.Capture(workspace);
            ContractSO contract = workspace.Location.activeContract;
            ChallengeSubmissionError error = ChallengeBridgeSubmissionRules.Validate(graph, workspace.Location, contract,
                ChallengeBridgeSubmissionRules.CreateMaterialCatalog(contract), workspace.Creator.pierBaseY, out _);
            if (error != ChallengeSubmissionError.None) { ApplyReply(revision, request, error); yield break; }
            packet = ChallengeBridgeSubmissionCodec.Encode(graph);
        }
        catch (Exception error) when (error is InvalidDataException || error is ArgumentException)
        {
            LogRejectedGraph("local capture", error);
            ApplyReply(revision, request, ChallengeSubmissionError.InvalidPacket); yield break;
        }
        if (runner.IsServer) ReceiveSubmission(runner.LocalPlayer, revision, request, packet);
        else
        {
            try { runner.SendReliableDataToServer(ReliableKey.FromInts(MessageTag, SubmitMessage, revision, request), packet); }
            catch (Exception)
            {
                // A transport error is ambiguous: the host may have received it. Do not unlock a potentially accepted graph.
                localMessage = "Submission transport interrupted. Waiting for host confirmation; cancel to return safely.";
            }
        }
    }

    public void OnReliableData(PlayerRef sender, ReliableKey key, ReadOnlySpan<byte> data)
    {
        key.GetInts(out int tag, out int kind, out int revision, out int request);
        if (tag != MessageTag || request <= 0 || !TryGetChallenge(out var host) || host.ChallengeState.Revision != revision) return;
        BindRevision(revision);
        if (runner.IsServer && kind == SubmitMessage) ReceiveSubmission(sender, revision, request, data);
        // Fusion's client-side callback identifies the server connection with the client's PlayerRef.
        // This branch only accepts the server connection in Host/Client mode, never a caller-supplied owner field.
        else if (!runner.IsServer && kind == ReplyMessage && data.Length == 1 &&
            Enum.IsDefined(typeof(ChallengeSubmissionError), (int)data[0]))
            ApplyReply(revision, request, (ChallengeSubmissionError)data[0]);
    }

    private void ReceiveSubmission(PlayerRef sender, int revision, int request, ReadOnlySpan<byte> data)
    {
        if (!TryGetChallenge(out var host) || !runner.IsServer || !host.HasStateAuthority || host.ChallengeState.Revision != revision ||
            (sender != host.Object.InputAuthority && sender != host.ChallengeState.Guest) ||
            !runner.TryGetPlayerObject(sender, out NetworkObject obj) || obj == null || !obj.IsValid || obj.InputAuthority != sender) return;
        BindRevision(revision);
        if (accepted.TryGetValue(sender, out AcceptedBridge previous))
        {
            Reply(sender, revision, request, previous.Attempt == request ? ChallengeSubmissionError.None : ChallengeSubmissionError.AlreadySubmitted);
            return;
        }
        if (!MultiplayerChallengeRules.CanSubmit(host.ChallengeState, sender, host.Object.InputAuthority, revision))
        { Reply(sender, revision, request, ChallengeSubmissionError.WrongChallenge); return; }
        if (lastAttemptTimes.TryGetValue(sender, out float last) && Time.unscaledTime - last < 0.5f)
        { Reply(sender, revision, request, ChallengeSubmissionError.Busy); return; }
        lastAttemptTimes[sender] = Time.unscaledTime;
        try
        {
            ChallengeBridgeSubmission graph = ChallengeBridgeSubmissionCodec.Decode(data);
            BuildLocation site = MultiplayerChallengeRules.ResolveSite(host.ChallengeState.SiteKey.ToString());
            ContractSO contract = ChallengeBuildWorkspace.ResolveContract(site, host.ChallengeState.ContractKey.ToString());
            BarCreator creator = FindObjectOfType<BarCreator>(true);
            ChallengeSubmissionError error = ChallengeBridgeSubmissionRules.Validate(graph, site, contract,
                ChallengeBridgeSubmissionRules.CreateMaterialCatalog(contract), creator != null ? creator.pierBaseY : -10f, out float cost);
            if (error != ChallengeSubmissionError.None) { Reply(sender, revision, request, error); return; }
            byte[] immutablePacket = ChallengeBridgeSubmissionCodec.Encode(graph);
            if (!host.TryAcceptValidatedChallengeBridge(sender, revision, cost, graph.Bars.Count))
            { Reply(sender, revision, request, ChallengeSubmissionError.WrongChallenge); return; }
            accepted.Add(sender, new AcceptedBridge { Packet = immutablePacket, Attempt = request, Cost = cost, Bars = graph.Bars.Count });
            Reply(sender, revision, request, ChallengeSubmissionError.None);
        }
        catch (Exception error) when (error is InvalidDataException || error is EndOfStreamException || error is ArgumentException)
        { LogRejectedGraph("host decode", error); Reply(sender, revision, request, ChallengeSubmissionError.InvalidPacket); }
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    private void LogRejectedGraph(string stage, Exception error)
    {
        Debug.LogWarning($"[Challenge submission] {stage} failed: {error.Message}", this);
    }

    private void Reply(PlayerRef player, int revision, int request, ChallengeSubmissionError error)
    {
        if (player == runner.LocalPlayer) ApplyReply(revision, request, error);
        else
        {
            try { runner.SendReliableDataToPlayer(player, ReliableKey.FromInts(MessageTag, ReplyMessage, revision, request), new[] { (byte)error }); }
            catch (Exception) { /* A disconnect/scene change ends the challenge; acceptance remains in authoritative state. */ }
        }
    }

    private void ApplyReply(int revision, int request, ChallengeSubmissionError error)
    {
        if (!bound || revision != boundRevision || request != pendingAttempt || !pending) return;
        pending = false; locallyAccepted = error == ChallengeSubmissionError.None || error == ChallengeSubmissionError.AlreadySubmitted;
        localMessage = ChallengeBridgeSubmissionRules.Message(error);
        // A rejection never overrides a newer authoritative acceptance snapshot.
        if (TryGetChallenge(out var host) && MultiplayerChallengeRules.HasSubmitted(host.ChallengeState, runner.LocalPlayer, host.Object.InputAuthority))
            locallyAccepted = true;
    }

    // A fresh decode prevents future testing code from mutating the retained accepted graph.
    public bool TryGetAcceptedBridge(PlayerRef player, int revision, out ChallengeBridgeSubmission graph, out float cost)
    {
        graph = null; cost = 0f;
        if (runner == null || !runner.IsServer || !TryGetChallenge(out var host) || host.ChallengeState.Revision != revision ||
            !bound || boundRevision != revision || !accepted.TryGetValue(player, out AcceptedBridge bridge)) return false;
        graph = ChallengeBridgeSubmissionCodec.Decode(bridge.Packet); cost = bridge.Cost;
        return true;
    }

    public void Clear()
    {
        if (bound || pending) StopAllCoroutines();
        bound = pending = locallyAccepted = false;
        boundRevision = attempt = pendingAttempt = 0;
        localMessage = null; accepted.Clear(); lastAttemptTimes.Clear();
    }
    private void OnDisable() { Clear(); }
    private void OnDestroy() { Clear(); }
}
