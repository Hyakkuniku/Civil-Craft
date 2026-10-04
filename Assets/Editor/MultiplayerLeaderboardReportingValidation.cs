using System;
using System.Text;
using Fusion;
using UnityEngine;

/// <summary>Isolated production-policy checks. Does not start a room, sign in, submit an API request, or write player saves.</summary>
public static class MultiplayerLeaderboardReportingValidation
{
    private const string MatchId = "0123456789abcdef0123456789abcdef";
    private const string HostAccount = "AA11";
    private const string GuestAccount = "BB22";
    private static readonly PlayerRef Host = PlayerRef.FromIndex(0);
    private static readonly PlayerRef Guest = PlayerRef.FromIndex(1);

    public static void Run(StringBuilder report)
    {
        if (report == null) throw new ArgumentNullException(nameof(report));
        ValidateOutcomes(report);
        ValidateCaptureEligibility(report);
        ValidateParticipantIdentity(report);
        ValidateAuthenticationAndRetry(report);
        ValidateQueueRecovery(report);
        ValidateWinRate(report);
    }

    private static MultiplayerChallengeState Completed(ChallengeWinner winner = ChallengeWinner.Host) =>
        new MultiplayerChallengeState
        {
            Phase = MultiplayerChallengePhase.TestResults,
            Result = MultiplayerChallengeResult.None,
            MatchId = MatchId,
            HostAccountId = HostAccount,
            GuestAccountId = GuestAccount,
            Guest = Guest,
            TestIndex = 2,
            HostSubmitted = true,
            GuestSubmitted = true,
            HostTestOutcome = ChallengeTestOutcome.Crossed,
            GuestTestOutcome = ChallengeTestOutcome.Collapsed,
            Winner = winner
        };

    private static void ValidateOutcomes(StringBuilder report)
    {
        var winners = new[] { ChallengeWinner.Host, ChallengeWinner.Guest, ChallengeWinner.Draw, ChallengeWinner.NoWinner };
        var hostOutcomes = new[] { "win", "loss", "draw", "loss" };
        var guestOutcomes = new[] { "loss", "win", "draw", "loss" };
        for (int index = 0; index < winners.Length; index++)
        {
            Check(MultiplayerResultPolicy.TryGetOutcome(winners[index], true, out string hostOutcome) &&
                  hostOutcome == hostOutcomes[index], "Host outcome is incorrect for " + winners[index]);
            Check(MultiplayerResultPolicy.TryGetOutcome(winners[index], false, out string guestOutcome) &&
                  guestOutcome == guestOutcomes[index], "Guest outcome is incorrect for " + winners[index]);
            MultiplayerChallengeState state = Completed(winners[index]);
            Check(MultiplayerResultPolicy.TryCreateReport(state, MultiplayerChallengeResult.None, Host, Host,
                    HostAccount, out MultiplayerResultReport host) && host.outcome == hostOutcomes[index],
                "Completed host report outcome is incorrect for " + winners[index]);
            Check(MultiplayerResultPolicy.TryCreateReport(state, MultiplayerChallengeResult.None, Guest, Host,
                    GuestAccount, out MultiplayerResultReport guest) && guest.outcome == guestOutcomes[index],
                "Completed guest report outcome is incorrect for " + winners[index]);
        }
        foreach (bool isHost in new[] { false, true })
        {
            Check(!MultiplayerResultPolicy.TryGetOutcome(ChallengeWinner.Pending, isHost, out _),
                "A pending winner produced a match result.");
            Check(!MultiplayerResultPolicy.TryGetOutcome((ChallengeWinner)999, isHost, out _),
                "An unknown winner produced a match result.");
        }
        report.AppendLine("PASS: Host/guest win and loss, draw, and NoWinner outcomes map correctly. NoWinner gives each participant one loss; pending/unknown winners are excluded.");
    }

    private static void ValidateCaptureEligibility(StringBuilder report)
    {
        MultiplayerChallengeState state = Completed();
        Check(MultiplayerResultPolicy.TryCapture(state, MultiplayerChallengeResult.None,
                out MultiplayerResultSnapshot retained) && retained.MatchId.ToString() == MatchId &&
              retained.HostAccountId.ToString() == HostAccount && retained.GuestAccountId.ToString() == GuestAccount &&
              retained.Winner == state.Winner, "The completed-result latch lost match/participant identity.");
        foreach (MultiplayerChallengePhase phase in Enum.GetValues(typeof(MultiplayerChallengePhase)))
        {
            state = Completed(); state.Phase = phase;
            Check(MultiplayerResultPolicy.TryCapture(state, MultiplayerChallengeResult.None, out _) ==
                  (phase == MultiplayerChallengePhase.TestResults), "An incomplete phase can record a match: " + phase);
        }
        int completionChecks = 0;
        foreach (MultiplayerChallengeResult stored in Enum.GetValues(typeof(MultiplayerChallengeResult)))
            foreach (MultiplayerChallengeResult completion in Enum.GetValues(typeof(MultiplayerChallengeResult)))
            {
                state = Completed(); state.Result = stored;
                Check(MultiplayerResultPolicy.TryCapture(state, completion, out _) ==
                      (stored == MultiplayerChallengeResult.None && completion == MultiplayerChallengeResult.None),
                    "A declined/cancelled/failed/left-player challenge can record a match.");
                completionChecks++;
            }
        foreach (int testIndex in new[] { -1, 0, 1, 3 })
        {
            state = Completed(); state.TestIndex = testIndex;
            Check(!MultiplayerResultPolicy.TryCapture(state, MultiplayerChallengeResult.None, out _),
                "A challenge without both completed tests was captured.");
        }
        state = Completed(); state.HostSubmitted = false; RejectCapture(state, "missing host submission");
        state = Completed(); state.GuestSubmitted = false; RejectCapture(state, "missing guest submission");
        state = Completed(); state.Guest = PlayerRef.None; RejectCapture(state, "missing guest participant");
        state = Completed(); state.HostTestOutcome = ChallengeTestOutcome.None; RejectCapture(state, "missing host outcome");
        state = Completed(); state.GuestTestOutcome = ChallengeTestOutcome.None; RejectCapture(state, "missing guest outcome");
        state = Completed(); state.HostTestOutcome = (ChallengeTestOutcome)999; RejectCapture(state, "unknown host outcome");
        state = Completed(); state.GuestTestOutcome = (ChallengeTestOutcome)999; RejectCapture(state, "unknown guest outcome");
        state = Completed(); state.Winner = ChallengeWinner.Pending; RejectCapture(state, "pending winner");
        state = Completed(); state.MatchId = ""; RejectCapture(state, "missing match ID");
        state = Completed(); state.MatchId = "not-a-guid"; RejectCapture(state, "invalid match ID");
        state = Completed(); state.HostAccountId = ""; RejectCapture(state, "missing host account");
        state = Completed(); state.GuestAccountId = ""; RejectCapture(state, "missing guest account");
        state = Completed(); state.GuestAccountId = "aa11"; RejectCapture(state, "same account on both participants");
        Check(MultiplayerResultPolicy.TryCreateReport(retained, Guest, Host, GuestAccount, out MultiplayerResultReport reportCopy) &&
              reportCopy.matchId == MatchId && reportCopy.opponentId == HostAccount && reportCopy.outcome == "loss",
            "Returning to the world cannot recover a valid result from the retained snapshot.");
        report.AppendLine($"PASS: All challenge phases and {completionChecks} stored/completion result combinations exclude incomplete, cancelled, declined, failed and player-left matches. Both recorded outcomes, submissions, GUID and distinct accounts are required; retained snapshots remain reportable after phase clearing.");
    }

    private static void ValidateParticipantIdentity(StringBuilder report)
    {
        MultiplayerChallengeState state = Completed();
        Check(MultiplayerResultPolicy.TryCreateReport(state, MultiplayerChallengeResult.None, Host, Host,
                "aa11", out MultiplayerResultReport own) && own.accountId == "aa11" &&
              own.opponentId == GuestAccount && own.matchId == MatchId,
            "Case-insensitive authenticated account matching failed.");
        Check(!MultiplayerResultPolicy.TryCreateReport(state, MultiplayerChallengeResult.None, Host, Host, GuestAccount, out _),
            "Host can report a result as the guest's account.");
        Check(!MultiplayerResultPolicy.TryCreateReport(state, MultiplayerChallengeResult.None, Guest, Host, HostAccount, out _),
            "Guest can report a result as the host's account.");
        Check(!MultiplayerResultPolicy.TryCreateReport(state, MultiplayerChallengeResult.None, PlayerRef.FromIndex(2), Host, HostAccount, out _),
            "A spectator can report a participant's result.");
        Check(!MultiplayerResultPolicy.TryCreateReport(state, MultiplayerChallengeResult.None, PlayerRef.None, Host, HostAccount, out _),
            "A missing participant can report a match.");
        Check(!MultiplayerResultPolicy.TryCreateReport(state, MultiplayerChallengeResult.None, Host, PlayerRef.None, HostAccount, out _),
            "A missing host can report a match.");
        Check(!MultiplayerResultPolicy.TryCreateReport(state, MultiplayerChallengeResult.None, Guest, Guest, GuestAccount, out _),
            "The same PlayerRef is accepted as both host and guest.");
        foreach (string invalid in new[] { null, "", " ", "Engineer", "AA11 ", "<b>AA11</b>", new string('a', 65) })
        {
            Check(!MultiplayerResultPolicy.IsAccountId(invalid), "Invalid account ID accepted: " + invalid);
            Check(!MultiplayerResultPolicy.TryCreateReport(state, MultiplayerChallengeResult.None, Host, Host, invalid, out _),
                "A report can use an invalid authenticated account.");
        }
        foreach (string invalid in new[] { null, "", " ", new string('a', 31), new string('a', 33), new string('g', 32), "01234567-89ab-cdef-0123-456789abcdef" })
            Check(!MultiplayerResultPolicy.IsMatchId(invalid), "Invalid match GUID accepted: " + invalid);
        Check(MultiplayerResultPolicy.IsMatchId(MatchId.ToUpperInvariant()), "A valid uppercase GUID was rejected.");
        Check(MultiplayerResultPolicy.IsAccountId(new string('f', 64)), "A valid maximum-length account ID was rejected.");
        Check(MultiplayerResultPolicy.IsValidReport(own), "A valid account-bound report was rejected.");
        Check(!MultiplayerResultPolicy.IsValidReport(null), "A missing report was accepted.");
        own.opponentId = own.accountId.ToUpperInvariant();
        Check(!MultiplayerResultPolicy.IsValidReport(own), "A self-match report was accepted.");
        own.opponentId = GuestAccount; own.outcome = "cancelled";
        Check(!MultiplayerResultPolicy.IsValidReport(own), "Cancellation is accepted as a leaderboard outcome.");
        report.AppendLine("PASS: Report identity is bound to the authenticated host/guest account; swapped accounts, spectators, missing participants, self-matches and invalid account/match IDs are rejected.");
    }

    private static void ValidateWinRate(StringBuilder report)
    {
        Check(MultiplayerLeaderboardRowUI.FormatWinRate(0, 0) == "—", "No decisive games should display an em dash.");
        Check(MultiplayerLeaderboardRowUI.FormatWinRate(3, 1) == Rate(75d), "Three wins and one loss should display a 75% win rate.");
        // Draws are represented separately by the reporting policy and never enter these two decisive counters.
        Check(MultiplayerResultPolicy.TryGetOutcome(ChallengeWinner.Draw, true, out string draw) && draw == "draw" &&
              MultiplayerLeaderboardRowUI.FormatWinRate(0, 0) == "—", "Draw-only history produces a decisive win rate.");
        Check(MultiplayerLeaderboardRowUI.FormatWinRate(int.MaxValue, int.MaxValue) == Rate(50d),
            "Large win/loss totals overflow their decisive game denominator.");
        Check(MultiplayerLeaderboardRowUI.FormatWinRate(int.MaxValue, 0) == Rate(100d),
            "Maximum supported wins produce an invalid percentage.");
        Check(MultiplayerLeaderboardRowUI.FormatWinRate(0, int.MaxValue) == Rate(0d),
            "Maximum supported losses produce an invalid percentage.");
        Check(MultiplayerLeaderboardRowUI.FormatWinRate(-1, -1) == "—" &&
              MultiplayerLeaderboardRowUI.FormatWinRate(3, -1) == Rate(100d),
            "Negative counters should be clamped before computing win rate.");
        report.AppendLine("PASS: Scene row win rate is wins/(wins+losses), with draws excluded: no decisive games show —, 3/1 shows 75%, and maximum integer counters do not overflow.");
    }

    private static void ValidateAuthenticationAndRetry(StringBuilder report)
    {
        foreach (bool loggedIn in new[] { false, true })
            foreach (bool selectedGuest in new[] { false, true })
            {
                bool accepted = MultiplayerResultPolicy.TryAuthenticatedAccountId(HostAccount, loggedIn,
                    selectedGuest, out string accountId);
                Check(accepted == (loggedIn && !selectedGuest) &&
                      accountId == (accepted ? HostAccount : string.Empty),
                    "SDK account identity is exposed while offline or while guest play is selected.");
            }
        Check(!MultiplayerResultPolicy.TryAuthenticatedAccountId("Engineer", true, false, out _) &&
              !MultiplayerResultPolicy.TryAuthenticatedAccountId(null, true, false, out _),
            "A display name or missing SDK ID is accepted as an authenticated account.");
        int[] delays = { 5, 10, 20, 40, 80, 160, 300, 300 };
        for (int index = 0; index < delays.Length; index++)
            Check(MultiplayerResultPolicy.RetryDelaySeconds(index + 1) == delays[index],
                "Result retry backoff is incorrect for attempt " + (index + 1));
        Check(MultiplayerResultPolicy.RetryDelaySeconds(0) == 5 &&
              MultiplayerResultPolicy.RetryDelaySeconds(-1) == 5 &&
              MultiplayerResultPolicy.RetryDelaySeconds(int.MaxValue) == 300,
            "Result retry delay is outside its 5..300 second limits.");
        report.AppendLine("PASS: Guest/offline/invalid SDK identities cannot submit results. Retry backoff increases from 5 seconds to a capped 300 seconds.");
    }

    private static void ValidateQueueRecovery(StringBuilder report)
    {
        const string title = "ABCD1";
        const long retryTime = 10000;
        var ledger = new MultiplayerResultLedger();
        MultiplayerResultReport original = NewReport();
        Check(ledger.TryEnqueue(original, title) && ledger.reports.Count == 1,
            "A valid completed result was not queued.");
        for (int observation = 0; observation < 12; observation++)
            Check(!ledger.TryEnqueue(NewReport(), title.ToLowerInvariant()),
                "Repeated network snapshots queued the same match again.");
        MultiplayerResultReport changedOutcome = NewReport(); changedOutcome.outcome = "loss";
        Check(!ledger.TryEnqueue(changedOutcome, title), "An already queued match ID accepted a conflicting outcome.");
        MultiplayerResultReport uppercase = NewReport();
        uppercase.accountId = HostAccount.ToLowerInvariant(); uppercase.matchId = MatchId.ToUpperInvariant();
        Check(!ledger.TryEnqueue(uppercase, title), "Changing match/account case bypassed result suppression.");
        Check(ledger.reports.Count == 1 && original.matchId == MatchId,
            "Snapshot suppression changed the pending report.");
        Check(ledger.FindNextEligible(title, HostAccount, 0) == original,
            "A fresh pending report is not eligible for its account.");
        Check(ledger.FindNextEligible(title, GuestAccount, long.MaxValue) == null &&
              ledger.FindNextEligible("OTHER", HostAccount, long.MaxValue) == null &&
              ledger.FindNextEligible(title, "", long.MaxValue) == null,
            "The queue exposed another account/title's pending report.");
        original.attempts = 3; original.nextAttemptUtcTicks = retryTime;
        string serialized = JsonUtility.ToJson(ledger);
        MultiplayerResultLedger recovered = JsonUtility.FromJson<MultiplayerResultLedger>(serialized);
        Check(recovered != null && recovered.schemaVersion == 1 && recovered.reports.Count == 1 &&
              recovered.reports[0].attempts == 3 && recovered.reports[0].nextAttemptUtcTicks == retryTime,
            "Serializing/reloading the pure queue lost its retry state.");
        Check(recovered.FindNextEligible(title, HostAccount, retryTime - 1) == null &&
              recovered.FindNextEligible(title.ToLowerInvariant(), HostAccount.ToLowerInvariant(), retryTime) == recovered.reports[0],
            "A recovered result ignores retry time or case-insensitive account/title identity.");
        Check(!recovered.TryEnqueue(NewReport(), title), "A reloaded pending result was enqueued again.");
        MultiplayerResultReport guest = NewReport(); guest.accountId = GuestAccount; guest.opponentId = HostAccount; guest.outcome = "loss";
        Check(recovered.TryEnqueue(guest, title) && recovered.FindNextEligible(title, GuestAccount, 0) == guest,
            "The other participant's account-bound receipt was suppressed or misrouted.");
        MultiplayerResultReport otherTitle = NewReport();
        Check(recovered.TryEnqueue(otherTitle, "OTHER") &&
              recovered.FindNextEligible("OTHER", HostAccount, 0) == otherTitle,
            "A result queued for a different title was suppressed or misrouted.");
        recovered.MarkDelivered(recovered.reports[0]);
        Check(recovered.FindNextEligible(title, HostAccount, long.MaxValue) == null &&
              !recovered.TryEnqueue(NewReport(), title),
            "A delivered result became eligible or was enqueued again.");
        guest.rejected = true;
        Check(recovered.FindNextEligible(title, GuestAccount, long.MaxValue) == null,
            "A terminal rejected result remains eligible for submission.");
        recovered = JsonUtility.FromJson<MultiplayerResultLedger>(JsonUtility.ToJson(recovered));
        Check(!recovered.TryEnqueue(NewReport(), title) &&
              recovered.FindNextEligible(title, HostAccount, long.MaxValue) == null &&
              recovered.FindNextEligible(title, GuestAccount, long.MaxValue) == null,
            "Reloading lost completed/rejected match suppression.");
        var invalid = NewReport(); invalid.matchId = "bad";
        Check(!recovered.TryEnqueue(invalid, title) && !recovered.TryEnqueue(NewReport(), string.Empty),
            "Invalid results or missing titles are accepted by the queue.");

        // Successful receipt pruning must never remove pending outcomes that still need delivery.
        var bounded = new MultiplayerResultLedger();
        MultiplayerResultReport pending = NewReport(); pending.matchId = new string('f', 32);
        Check(bounded.TryEnqueue(pending, title), "Pending receipt fixture was not queued.");
        for (int index = 1; index <= MultiplayerResultLedger.MaximumDeliveredReports + 2; index++)
        {
            var delivered = NewReport(); delivered.matchId = index.ToString("x32");
            Check(bounded.TryEnqueue(delivered, title), "Distinct completed receipt was unexpectedly suppressed.");
            bounded.MarkDelivered(delivered);
        }
        Check(bounded.reports.Count == MultiplayerResultLedger.MaximumDeliveredReports + 1 &&
              bounded.reports.Contains(pending) && bounded.FindNextEligible(title, HostAccount, 0) == pending,
            "Successful receipt pruning removed an undelivered result or exceeded its retention cap.");
        report.AppendLine("PASS: Repeated/case-varied snapshots and conflicting outcomes suppress duplicate match IDs. JSON round trips preserve pending retries and completed/rejected suppression; reports stay isolated by account/title, and receipt pruning retains every pending result. No API requests or player save writes were made.");
    }

    private static MultiplayerResultReport NewReport() => new MultiplayerResultReport
    {
        accountId = HostAccount, opponentId = GuestAccount, matchId = MatchId, outcome = "win"
    };

    private static string Rate(double percentage) => percentage.ToString("0.0") + "%";
    private static void RejectCapture(MultiplayerChallengeState state, string reason) =>
        Check(!MultiplayerResultPolicy.TryCapture(state, MultiplayerChallengeResult.None, out _),
            "Completed-result capture accepted " + reason + ".");
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[Multiplayer leaderboard validation] " + message);
    }
}
