using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Fusion;
using Fusion.Sockets;
using UnityEngine;

/// <summary>Immutable, bounded, idempotent submission transfers, separate from saved-world synchronization.</summary>
[DisallowMultipleComponent]
public sealed class FusionChallengeSubmissionSync : MonoBehaviour
{
    public const int MessageTag = 0x43435342;
    private const int SubmitMessage = 1, ReplyMessage = 2;
    [SerializeField, Min(5f)] private float slowTransferNoticeSeconds = 15f;
    [SerializeField, Range(2f, 10f)] private float submissionRetrySeconds = 3f;
    private const int MaximumSubmissionSends = 3;
    private NetworkRunner runner;
    private int boundRevision, attempt, pendingAttempt;
    private bool bound, pending, locallyAccepted;
    private float pendingSince;
    private string localMessage;
    private byte[] pendingPacket;
    private int submissionSends;
    private float nextSubmissionSend;
    private readonly Dictionary<PlayerRef, AcceptedBridge> accepted = new Dictionary<PlayerRef, AcceptedBridge>();
    private readonly Dictionary<PlayerRef, float> lastAttemptTimes = new Dictionary<PlayerRef, float>();
    private sealed class AcceptedBridge { public byte[] Packet; public int Attempt, Bars; public float Cost; }

    public bool LocalEditingLocked => pending || locallyAccepted;
    public bool IsSending => pending;
    public bool IsLocallyAccepted => locallyAccepted;
    public string LocalMessage => pending && Time.unscaledTime - pendingSince >= slowTransferNoticeSeconds
        ? "Still waiting for host confirmation. Your bridge stays locked; you can cancel the challenge."
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
            pendingPacket = null;
            localMessage = ChallengeBridgeSubmissionRules.Message(ChallengeSubmissionError.None);
        }
        else if (!runner.IsServer && CanRetrySubmission(host.ChallengeState, runner.LocalPlayer, host.Object.InputAuthority,
            boundRevision, pending, pendingPacket != null, submissionSends, Time.unscaledTime, nextSubmissionSend))
            SendPendingSubmission();
    }

    internal static bool CanRetrySubmission(MultiplayerChallengeState state, PlayerRef player, PlayerRef host, int revision,
        bool awaitingReply, bool hasPacket, int sends, float now, float nextSend) =>
        awaitingReply && hasPacket && sends > 0 && sends < MaximumSubmissionSends && now >= nextSend &&
        MultiplayerChallengeRules.CanSubmit(state, player, host, revision);

    private void SendPendingSubmission()
    {
        submissionSends++;
        nextSubmissionSend = Time.unscaledTime + Mathf.Clamp(submissionRetrySeconds, 2f, 10f);
        try
        {
            runner.SendReliableDataToServer(ReliableKey.FromInts(MessageTag, SubmitMessage, boundRevision, pendingAttempt), pendingPacket);
            LogTransfer("sent " + submissionSends + "/" + MaximumSubmissionSends, runner.LocalPlayer, boundRevision, pendingAttempt, pendingPacket.Length);
        }
        catch (Exception error)
        {
            // Ambiguous transport failure: retry the SAME graph/attempt. Never
            // unlock or recapture a design that the host may already have accepted.
            localMessage = "Submission transport interrupted. Waiting for host confirmation; cancel to return safely.";
            LogTransfer("send failed: " + error.Message, runner.LocalPlayer, boundRevision, pendingAttempt, pendingPacket.Length);
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
        pendingPacket = null; submissionSends = 0;
        localMessage = "Submitting bridge...";
        workspace.Creator.CancelAllModes();
        workspace.Creator.SetActiveMaterial(null); // Hide a pier preview as well as unfinished drawing/paste ghosts.
        workspace.SetEditingEnabled(false);
        StartCoroutine(CaptureAndSend(workspace, boundRevision, pendingAttempt));
    }

    private IEnumerator CaptureAndSend(ChallengeBuildWorkspace workspace, int revision, int request)
    {
        // CancelCreation uses deferred Destroy. Never serialize those discarded ghosts this frame.
        yield return null;
        if (!pending || pendingAttempt != request) yield break;
        if (!TryGetChallenge(out var host) || host.ChallengeState.Revision != revision || workspace?.Location == null ||
            !MultiplayerChallengeRules.CanSubmit(host.ChallengeState, runner.LocalPlayer, host.Object.InputAuthority, revision))
        {
            // Nothing has been sent yet, so this deferred capture can safely fail
            // instead of leaving the button stuck in Submitting forever.
            LogTransfer("capture cancelled before send", runner.LocalPlayer, revision, request, 0);
            ApplyReply(revision, request, ChallengeSubmissionError.WrongChallenge);
            yield break;
        }
        byte[] packet;
        try
        {
            ChallengeBridgeSubmission graph = ChallengeBridgeSubmissionRules.Capture(workspace);
            ContractSO contract = workspace.Location.activeContract;
            ChallengeSubmissionError error = ChallengeBridgeSubmissionRules.Validate(graph, workspace.Location, contract,
                ChallengeBridgeSubmissionRules.CreateMaterialCatalog(contract), workspace.Creator.pierBaseY, out _, workspace.Creator.nodeSnapDepthTolerance);
            if (error != ChallengeSubmissionError.None)
            { LogRejectedSubmission("local admission", error, graph, workspace.Location); ApplyReply(revision, request, error); yield break; }
            packet = ChallengeBridgeSubmissionCodec.Encode(graph);
        }
        catch (Exception error)
        {
            LogRejectedGraph("local capture", error);
            ApplyReply(revision, request, ChallengeSubmissionError.InvalidPacket); yield break;
        }
        if (runner.IsServer) ReceiveSubmission(runner.LocalPlayer, revision, request, packet);
        else
        {
            pendingPacket = packet; // Frozen once; every retry has identical bytes and attempt ID.
            SendPendingSubmission();
        }
    }

    public void OnReliableData(PlayerRef sender, ReliableKey key, ReadOnlySpan<byte> data)
    {
        key.GetInts(out int tag, out int kind, out int revision, out int request);
        if (tag != MessageTag || request <= 0) return;
        LogTransfer("received kind=" + kind, sender, revision, request, data.Length);
        if (!TryGetChallenge(out var host) || host.ChallengeState.Revision != revision)
        { LogTransfer("ignored inactive/stale challenge", sender, revision, request, data.Length); return; }
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
            !runner.TryGetPlayerObject(sender, out NetworkObject obj) || obj == null || !obj.IsValid || obj.InputAuthority != sender)
        { LogTransfer("ignored authority/ownership", sender, revision, request, data.Length); return; }
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
                ChallengeBridgeSubmissionRules.CreateMaterialCatalog(contract), creator != null ? creator.pierBaseY : -10f, out float cost,
                creator != null ? creator.nodeSnapDepthTolerance : 1f);
            if (error != ChallengeSubmissionError.None)
            { LogRejectedSubmission("host admission", error, graph, site); Reply(sender, revision, request, error); return; }
            byte[] immutablePacket = ChallengeBridgeSubmissionCodec.Encode(graph);
            if (!host.TryAcceptValidatedChallengeBridge(sender, revision, cost, graph.Bars.Count))
            { Reply(sender, revision, request, ChallengeSubmissionError.WrongChallenge); return; }
            accepted.Add(sender, new AcceptedBridge { Packet = immutablePacket, Attempt = request, Cost = cost, Bars = graph.Bars.Count });
            LogTransfer("accepted", sender, revision, request, immutablePacket.Length);
            Reply(sender, revision, request, ChallengeSubmissionError.None);
        }
        catch (Exception error) when (error is InvalidDataException || error is EndOfStreamException || error is ArgumentException)
        { LogRejectedGraph("host decode", error); Reply(sender, revision, request, ChallengeSubmissionError.InvalidPacket); }
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    private void LogTransfer(string stage, PlayerRef peer, int revision, int request, int bytes)
    {
        Debug.Log($"[Challenge submission transfer] host={runner != null && runner.IsServer} peer={peer} revision={revision} attempt={request} bytes={bytes} stage={stage}", this);
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    private void LogRejectedSubmission(string stage, ChallengeSubmissionError error, ChallengeBridgeSubmission graph, BuildLocation site)
    {
        if (error == ChallengeSubmissionError.EmptyBridge) return;
        string anchors = string.Empty;
        var authored = ChallengeBridgeSubmissionRules.GetAnchors(site);
        for (int i = 0; i < graph.Nodes.Count && anchors.Length < 900; i++)
        {
            var node = graph.Nodes[i];
            if (node.Anchor >= 0 && node.Anchor < authored.Count)
                anchors += $" anchor[{node.Anchor}] submitted={node.Position.ToString("F3")} host={ChallengeBridgeSubmissionRules.ChallengeAnchorPosition(authored[node.Anchor]).ToString("F3")};";
        }
        float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;
        foreach (var node in graph.Nodes) { minZ = Mathf.Min(minZ, node.Position.z); maxZ = Mathf.Max(maxZ, node.Position.z); }
        Debug.LogWarning($"[Challenge submission] {stage}: {error}; site={MultiplayerChallengeRules.SiteKey(site)} " +
            $"nodes={graph.Nodes.Count} bars={graph.Bars.Count} depth={minZ:F3}..{maxZ:F3}.{anchors}", this);
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    private void LogRejectedGraph(string stage, Exception error)
    {
        Debug.LogWarning($"[Challenge submission] {stage} failed: {error.Message}", this);
    }

    private void Reply(PlayerRef player, int revision, int request, ChallengeSubmissionError error)
    {
        LogTransfer("reply " + error, player, revision, request, 1);
        if (player == runner.LocalPlayer) ApplyReply(revision, request, error);
        else
        {
            try { runner.SendReliableDataToPlayer(player, ReliableKey.FromInts(MessageTag, ReplyMessage, revision, request), new[] { (byte)error }); }
            catch (Exception exception) { LogTransfer("reply failed: " + exception.Message, player, revision, request, 1); }
        }
    }

    private void ApplyReply(int revision, int request, ChallengeSubmissionError error)
    {
        if (!bound || revision != boundRevision || request != pendingAttempt || !pending) return;
        pending = false; locallyAccepted = error == ChallengeSubmissionError.None || error == ChallengeSubmissionError.AlreadySubmitted;
        pendingPacket = null;
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
        pendingPacket = null; submissionSends = 0; nextSubmissionSend = 0f;
        localMessage = null; accepted.Clear(); lastAttemptTimes.Clear();
    }
    private void OnDisable() { Clear(); }
    private void OnDestroy() { Clear(); }
}
