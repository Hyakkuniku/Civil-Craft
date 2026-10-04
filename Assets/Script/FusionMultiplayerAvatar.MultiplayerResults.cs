using Fusion;
using UnityEngine;

public sealed partial class FusionMultiplayerAvatar
{
    [Networked] public NetworkString<_64> AuthenticatedPlayFabId { get; private set; }
    [Networked] public MultiplayerResultSnapshot CompletedMultiplayerResult { get; private set; }
    private string lastPublishedAccountId;
    private float nextAccountIdentityCheck;

    private void PublishAuthenticatedAccountIdentity()
    {
        if (Object == null || !Object.IsValid || !HasInputAuthority) return;
        string account = MultiplayerResultReportingService.AuthenticatedAccountId;
        if (lastPublishedAccountId == account) return;
        lastPublishedAccountId = account;
        if (HasStateAuthority) AuthenticatedPlayFabId = account;
        else RPC_PublishAuthenticatedAccountIdentity(account);
    }

    // The input owner publishes its SDK-authenticated ID, never a ticket/token.
    // CloudScript still uses the authenticated API caller as the reporting player;
    // an opponent's replicated identifier is not authority to write its statistics.
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority, Channel = RpcChannel.Reliable)]
    private void RPC_PublishAuthenticatedAccountIdentity(string accountId)
    {
        if (!HasStateAuthority || accountId == null ||
            accountId.Length != 0 && !MultiplayerResultPolicy.IsAccountId(accountId)) return;
        AuthenticatedPlayFabId = accountId;
    }

    private void ObserveCompletedMultiplayerResults()
    {
        if (Time.unscaledTime >= nextAccountIdentityCheck)
        {
            nextAccountIdentityCheck = Time.unscaledTime + 0.5f;
            PublishAuthenticatedAccountIdentity();
        }
        FusionMultiplayerAvatar host = FindHost(Runner);
        if (host == null || host.Object == null || !host.Object.IsValid) return;
        string caller = MultiplayerResultReportingService.AuthenticatedAccountId;
        if (MultiplayerResultPolicy.TryCreateReport(host.ChallengeState, MultiplayerChallengeResult.None,
                Object.InputAuthority, host.Object.InputAuthority, caller, out MultiplayerResultReport liveReport))
            MultiplayerResultReportingService.Enqueue(liveReport);
        // A return-to-world can clear the phase before this client's Update sees it.
        // Keep the last completed match replicated until the next completion.
        if (MultiplayerResultPolicy.TryCreateReport(host.CompletedMultiplayerResult,
                Object.InputAuthority, host.Object.InputAuthority, caller, out MultiplayerResultReport retainedReport))
            MultiplayerResultReportingService.Enqueue(retainedReport);
    }

    private void CaptureCompletedMultiplayerResult(MultiplayerChallengeState completed,
        MultiplayerChallengeResult completion)
    {
        if (!MultiplayerResultPolicy.TryCapture(completed, completion, out MultiplayerResultSnapshot snapshot)) return;
        if (HasStateAuthority) CompletedMultiplayerResult = snapshot;
        if (HasInputAuthority && MultiplayerResultPolicy.TryCreateReport(snapshot, Object.InputAuthority,
                Object.InputAuthority, MultiplayerResultReportingService.AuthenticatedAccountId,
                out MultiplayerResultReport report))
            MultiplayerResultReportingService.Enqueue(report);
    }
}
