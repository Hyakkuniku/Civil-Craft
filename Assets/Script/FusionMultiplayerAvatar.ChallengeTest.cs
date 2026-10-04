using Fusion;
using UnityEngine;

public sealed partial class FusionMultiplayerAvatar
{
    private bool CanRunChallengeTests => Object != null && Object.IsValid && IsSessionHost && HasStateAuthority &&
        Runner != null && Runner.IsServer && AreChallengeParticipantsAtSite();

    internal bool PrepareChallengeTest(int revision, int index, float timeout)
    {
        if (!CanRunChallengeTests) return false;
        var state = ChallengeState;
        if (!MultiplayerChallengeRules.TryPrepareTest(ref state, revision, index)) return false;
        state.Deadline = TickTimer.CreateFromSeconds(Runner, timeout); ChallengeState = state; return true;
    }
    internal bool StartChallengeTest(int revision, int index)
    {
        if (!CanRunChallengeTests) return false;
        var state = ChallengeState;
        if (!MultiplayerChallengeRules.TryStartTest(ref state, revision, index)) return false;
        ChallengeState = state; return true;
    }
    internal bool RecordChallengeTest(int revision, int index, ChallengeTestOutcome outcome, float seconds, float peakStress)
    {
        if (!CanRunChallengeTests) return false;
        var state = ChallengeState;
        if (!MultiplayerChallengeRules.TryRecordTest(ref state, revision, index, outcome, seconds, peakStress)) return false;
        ChallengeState = state; return true;
    }
    internal void FinishChallengeTests(int revision)
    {
        if (!CanRunChallengeTests) return;
        var state = ChallengeState;
        if (MultiplayerChallengeRules.TryFinishTests(ref state, revision))
        {
            ChallengeState = state;
            CaptureCompletedMultiplayerResult(state, MultiplayerChallengeResult.None);
        }
        else if (state.Revision == revision && state.Phase == MultiplayerChallengePhase.Testing && state.TestIndex == 2)
            FailChallengeTest("The host could not verify the final budget and stress measurements.");
    }
    internal void PublishChallengeTestProgress(float elapsed, float stress)
    {
        if (!CanRunChallengeTests || ChallengeState.Phase != MultiplayerChallengePhase.Testing) return;
        var state = ChallengeState; state.TestElapsed = elapsed; state.TestStress = stress; ChallengeState = state;
    }
    internal void FailChallengeTest(string message)
    {
        if (!CanRunChallengeTests) return;
        var state = ChallengeState; state.TestSetupMessage = message.Length <= 256 ? message : message.Substring(0, 256);
        ChallengeState = state; EndChallenge(MultiplayerChallengeResult.TestSetupFailed);
    }

    internal void ReportChallengeTestView(int revision, int index, bool success, bool finalFrame)
    {
        if (Object != null && Object.IsValid && HasInputAuthority) RPC_ChallengeTestViewReady(revision, index, success, finalFrame);
    }
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority, Channel = RpcChannel.Reliable)]
    private void RPC_ChallengeTestViewReady(int revision, int index, bool success, bool finalFrame)
    {
        var host = FindHost(Runner);
        if (host == null || !host.CanRunChallengeTests || host.ChallengeState.Revision != revision ||
            host.ChallengeState.TestIndex != index || Object.InputAuthority != host.ChallengeState.Guest) return;
        if (finalFrame)
        { Runner.GetComponent<FusionChallengeTestSync>()?.ConfirmFinalView(revision, index, success); return; }
        if (host.ChallengeState.Phase != MultiplayerChallengePhase.PreparingTest) return;
        if (!success) { host.FailChallengeTest("The guest could not prepare the matching bridge/vehicle test view."); return; }
        var state = host.ChallengeState; state.GuestTestViewReady = true; host.ChallengeState = state;
    }
    internal void PublishChallengeTestMotion(int revision, int index, byte[] chunk)
    {
        if (CanRunChallengeTests) RPC_ChallengeTestMotion(revision, index, chunk);
    }
    // Unlike large-data callbacks, these messages are attached to the already
    // replicated host avatar. Each payload stays below Fusion's 512-byte limit.
    internal void PublishChallengeTestPacket(PlayerRef guest, int revision, int index, int kind, int total, int offset, byte[] chunk)
    {
        if (!CanRunChallengeTests || guest != ChallengeState.Guest)
            throw new System.InvalidOperationException("The host no longer has authority to send this test view.");
        var result = RPC_ChallengeTestPacket(guest, revision, index, kind, total, offset, chunk);
        if (!ChallengeTestRpcWasSent(result.SendMessageResult))
            throw new System.InvalidOperationException("Test view RPC was not sent: " + result.SendMessageResult);
    }
    // Installed Fusion returns Sent=0 for a successful RPC. MaskSent only
    // covers the legacy routed results; zero is NOT a failed send.
    internal static bool ChallengeTestRpcWasSent(RpcSendMessageResult result) =>
        result == RpcSendMessageResult.Sent ||
        ((result & RpcSendMessageResult.MaskSent) != 0 && (result & RpcSendMessageResult.MaskNotSent) == 0);
    [Rpc(RpcSources.StateAuthority, RpcTargets.Proxies, Channel = RpcChannel.Reliable, TickAligned = false, InvokeLocal = false)]
    private RpcInvokeInfo RPC_ChallengeTestPacket([RpcTarget] PlayerRef guest, int revision, int index, int kind, int total, int offset, byte[] chunk)
    {
        if (IsSessionHost && Runner != null && !Runner.IsServer && Runner.LocalPlayer == guest)
            Runner.GetComponent<FusionChallengeTestSync>()?.ReceiveTestPacketChunk(revision, index, kind, total, offset, chunk);
        return default;
    }
    internal void RequestChallengeTestPresentation(int revision, int index)
    {
        if (Object != null && Object.IsValid && HasInputAuthority)
            RPC_RequestChallengeTestPresentation(revision, index);
    }
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority, Channel = RpcChannel.Reliable, TickAligned = false)]
    private void RPC_RequestChallengeTestPresentation(int revision, int index)
    {
        Runner.GetComponent<FusionChallengeTestSync>()?.ReceivePresentationRequest(Object.InputAuthority, revision, index);
    }
    [Rpc(RpcSources.StateAuthority, RpcTargets.Proxies, Channel = RpcChannel.Unreliable, TickAligned = false, InvokeLocal = false)]
    private void RPC_ChallengeTestMotion(int revision, int index, byte[] chunk)
    {
        if (IsSessionHost && Runner != null && !Runner.IsServer)
            Runner.GetComponent<FusionChallengeTestSync>()?.ReceiveMotion(revision, index, chunk);
    }
}
