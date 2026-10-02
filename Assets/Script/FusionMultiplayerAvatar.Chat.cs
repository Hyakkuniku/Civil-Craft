using System;
using Fusion;
using UnityEngine;

public sealed partial class FusionMultiplayerAvatar
{
    public static event Action<NetworkRunner, int, string, string> ChatMessageReceived;
    private float nextLocalChatTime;
    private float nextServerChatTime;
    private int nextChatSequence;
    private int lastDeliveredChatSequence;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetChatListeners() { ChatMessageReceived = null; }

    public bool CanSendChat => Object != null && Object.IsValid && HasInputAuthority &&
        Runner != null && Runner.IsRunning && Time.unscaledTime >= nextLocalChatTime &&
        FusionConnectionManager.Instance != null && FusionConnectionManager.Instance.Runner == Runner &&
        FusionConnectionManager.Instance.IsHostWorldSession && !FusionConnectionManager.Instance.IsNetworkSceneLoading &&
        FusionConnectionManager.Instance.ConnectedPlayerCount > 1 && FindHost(Runner) != null;

    public bool TrySendChat(string message)
    {
        string body = SessionChatHistory.Normalize(message);
        if (!CanSendChat || body.Length == 0) return false;
        nextLocalChatTime = Time.unscaledTime + SessionChatHistory.SendInterval;
        if (HasStateAuthority) AcceptChat(body);
        else RPC_SubmitChat(body);
        return true;
    }

    // Fusion authenticates the caller against this avatar's InputAuthority. The
    // sender cannot supply an IGN or impersonate the other player in the payload.
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority, Channel = RpcChannel.Reliable)]
    private void RPC_SubmitChat(string message) { AcceptChat(message); }

    private void AcceptChat(string message)
    {
        FusionConnectionManager connection = FusionConnectionManager.Instance;
        if (!HasStateAuthority || Runner == null || !Runner.IsServer || connection == null ||
            connection.Runner != Runner || !connection.IsHostWorldSession ||
            connection.IsNetworkSceneLoading || Object == null || !Object.IsValid ||
            !Runner.TryGetPlayerObject(Object.InputAuthority, out NetworkObject player) || player != Object ||
            message == null || message.Length > SessionChatHistory.MessageLimit ||
            Time.unscaledTime < nextServerChatTime) return;
        string body = SessionChatHistory.Normalize(message);
        FusionMultiplayerAvatar host = FindHost(Runner);
        if (body.Length == 0 || host == null || !host.HasStateAuthority) return;
        // A small server-side guard tolerates normal arrival jitter around the
        // one-second local cooldown while rejecting a modified client's flood.
        nextServerChatTime = Time.unscaledTime + 0.75f;
        host.RPC_DeliverChat(++host.nextChatSequence, NormalizeMapPlayerName(MapPlayerName), body);
    }

    // One host-owned object sequences and broadcasts both players' messages.
    // Reliable RPCs are sent only on Send, never per frame or network tick.
    [Rpc(RpcSources.StateAuthority, RpcTargets.All, Channel = RpcChannel.Reliable)]
    private void RPC_DeliverChat(int sequence, string name, string message)
    {
        if (!IsSessionHost || sequence <= lastDeliveredChatSequence) return;
        lastDeliveredChatSequence = sequence;
        ChatMessageReceived?.Invoke(Runner, sequence, name, message);
    }
}
