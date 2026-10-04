using System;
using System.Collections.Generic;
using Fusion;

/// <summary>Small completed-result latch, retained when the live challenge is cleared.</summary>
public struct MultiplayerResultSnapshot : INetworkStruct
{
    public NetworkString<_32> MatchId;
    public NetworkString<_64> HostAccountId;
    public NetworkString<_64> GuestAccountId;
    public PlayerRef Guest;
    public ChallengeWinner Winner;
    public ChallengeTestOutcome HostTestOutcome;
    public ChallengeTestOutcome GuestTestOutcome;
}

/// <summary>Account-bound queue data. Authentication credentials are never stored here.</summary>
[Serializable]
public sealed class MultiplayerResultReport
{
    public string accountId;
    public string titleId;
    public string matchId;
    public string opponentId;
    public string outcome;
    public int attempts;
    public long nextAttemptUtcTicks;
    public bool delivered;
    public bool rejected;
}

/// <summary>A separate local receipt ledger; it never touches PlayerSaveData.</summary>
[Serializable]
public sealed class MultiplayerResultLedger
{
    public const int MaximumDeliveredReports = 256;
    public int schemaVersion = 1;
    public List<MultiplayerResultReport> reports = new List<MultiplayerResultReport>();

    public bool TryEnqueue(MultiplayerResultReport report, string titleId)
    {
        if (!MultiplayerResultPolicy.IsValidReport(report) || string.IsNullOrEmpty(titleId)) return false;
        if (reports == null) reports = new List<MultiplayerResultReport>();
        foreach (MultiplayerResultReport existing in reports)
            if (existing != null && string.Equals(existing.titleId, titleId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(existing.accountId, report.accountId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(existing.matchId, report.matchId, StringComparison.OrdinalIgnoreCase)) return false;
        report.titleId = titleId;
        report.matchId = report.matchId.ToLowerInvariant();
        report.attempts = 0;
        report.nextAttemptUtcTicks = 0;
        report.delivered = report.rejected = false;
        reports.Add(report);
        return true;
    }

    public MultiplayerResultReport FindNextEligible(string titleId, string accountId, long utcTicks)
    {
        if (reports == null || !MultiplayerResultPolicy.IsAccountId(accountId)) return null;
        foreach (MultiplayerResultReport report in reports)
            if (MultiplayerResultPolicy.IsValidReport(report) && !report.delivered && !report.rejected &&
                string.Equals(report.titleId, titleId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(report.accountId, accountId, StringComparison.OrdinalIgnoreCase) &&
                report.nextAttemptUtcTicks <= utcTicks) return report;
        return null;
    }

    public void MarkDelivered(MultiplayerResultReport report)
    {
        if (report == null || reports == null || !reports.Contains(report)) return;
        report.delivered = true;
        report.rejected = false;
        report.nextAttemptUtcTicks = 0;
        int deliveredCount = 0;
        foreach (MultiplayerResultReport candidate in reports)
            if (candidate != null && candidate.delivered) deliveredCount++;
        // The server keeps durable idempotency receipts; locally retain recent
        // successful IDs without allowing this device's ledger to grow forever.
        for (int index = 0; deliveredCount > MaximumDeliveredReports && index < reports.Count;)
        {
            if (reports[index] != null && reports[index].delivered)
            { reports.RemoveAt(index); deliveredCount--; }
            else index++;
        }
    }
}

/// <summary>Pure eligibility and outcome rules; no API, scene, or saved-story changes.</summary>
public static class MultiplayerResultPolicy
{
    public static bool IsMatchId(string value) => IsHex(value, 32, 32);

    public static bool IsAccountId(string value) => IsHex(value, 1, 64);

    public static bool TryAuthenticatedAccountId(string sdkAccountId, bool isLoggedIn, bool isGuestSelected,
        out string accountId)
    {
        accountId = string.Empty;
        if (!isLoggedIn || isGuestSelected || !IsAccountId(sdkAccountId)) return false;
        accountId = sdkAccountId;
        return true;
    }

    public static int RetryDelaySeconds(int attempts) =>
        Math.Min(300, 5 * (1 << Math.Min(6, Math.Max(1, attempts) - 1)));

    private static bool IsHex(string value, int minimum, int maximum)
    {
        if (string.IsNullOrEmpty(value) || value.Length < minimum || value.Length > maximum) return false;
        foreach (char character in value)
            if (!(character >= '0' && character <= '9' || character >= 'a' && character <= 'f' ||
                  character >= 'A' && character <= 'F')) return false;
        return true;
    }

    private static bool IsRecorded(ChallengeTestOutcome outcome) =>
        outcome != ChallengeTestOutcome.None && Enum.IsDefined(typeof(ChallengeTestOutcome), outcome);

    public static bool TryCapture(MultiplayerChallengeState state, MultiplayerChallengeResult completion,
        out MultiplayerResultSnapshot snapshot)
    {
        snapshot = default;
        if (state.Phase != MultiplayerChallengePhase.TestResults || state.Result != MultiplayerChallengeResult.None ||
            completion != MultiplayerChallengeResult.None || state.TestIndex != 2 ||
            !state.HostSubmitted || !state.GuestSubmitted || state.Guest == PlayerRef.None ||
            !IsRecorded(state.HostTestOutcome) || !IsRecorded(state.GuestTestOutcome) ||
            !IsMatchId(state.MatchId.ToString()) || !IsAccountId(state.HostAccountId.ToString()) ||
            !IsAccountId(state.GuestAccountId.ToString()) ||
            string.Equals(state.HostAccountId.ToString(), state.GuestAccountId.ToString(), StringComparison.OrdinalIgnoreCase) ||
            !TryGetOutcome(state.Winner, true, out _)) return false;
        snapshot = new MultiplayerResultSnapshot
        {
            MatchId = state.MatchId, HostAccountId = state.HostAccountId, GuestAccountId = state.GuestAccountId,
            Guest = state.Guest, Winner = state.Winner,
            HostTestOutcome = state.HostTestOutcome, GuestTestOutcome = state.GuestTestOutcome
        };
        return true;
    }

    public static bool TryGetOutcome(ChallengeWinner winner, bool isHost, out string outcome)
    {
        outcome = null;
        switch (winner)
        {
            case ChallengeWinner.Host: outcome = isHost ? "win" : "loss"; return true;
            case ChallengeWinner.Guest: outcome = isHost ? "loss" : "win"; return true;
            case ChallengeWinner.Draw: outcome = "draw"; return true;
            case ChallengeWinner.NoWinner: outcome = "loss"; return true;
            default: return false;
        }
    }

    public static bool TryCreateReport(MultiplayerChallengeState state, MultiplayerChallengeResult completion,
        PlayerRef participant, PlayerRef host, string authenticatedAccountId, out MultiplayerResultReport report)
    {
        report = null;
        return TryCapture(state, completion, out MultiplayerResultSnapshot snapshot) &&
            TryCreateReport(snapshot, participant, host, authenticatedAccountId, out report);
    }

    public static bool TryCreateReport(MultiplayerResultSnapshot snapshot, PlayerRef participant, PlayerRef host,
        string authenticatedAccountId, out MultiplayerResultReport report)
    {
        report = null;
        if (host == PlayerRef.None || snapshot.Guest == PlayerRef.None || host == snapshot.Guest ||
            participant != host && participant != snapshot.Guest || !IsAccountId(authenticatedAccountId) ||
            !IsMatchId(snapshot.MatchId.ToString()) || !IsRecorded(snapshot.HostTestOutcome) ||
            !IsRecorded(snapshot.GuestTestOutcome)) return false;
        string own = participant == host ? snapshot.HostAccountId.ToString() : snapshot.GuestAccountId.ToString();
        string opponent = participant == host ? snapshot.GuestAccountId.ToString() : snapshot.HostAccountId.ToString();
        if (!IsAccountId(own) || !IsAccountId(opponent) ||
            !string.Equals(own, authenticatedAccountId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(own, opponent, StringComparison.OrdinalIgnoreCase) ||
            !TryGetOutcome(snapshot.Winner, participant == host, out string outcome)) return false;
        report = new MultiplayerResultReport
        {
            accountId = authenticatedAccountId, matchId = snapshot.MatchId.ToString(),
            opponentId = opponent, outcome = outcome
        };
        return true;
    }

    public static bool IsValidReport(MultiplayerResultReport report) => report != null &&
        IsAccountId(report.accountId) && IsAccountId(report.opponentId) && IsMatchId(report.matchId) &&
        !string.Equals(report.accountId, report.opponentId, StringComparison.OrdinalIgnoreCase) &&
        (report.outcome == "win" || report.outcome == "loss" || report.outcome == "draw");
}
