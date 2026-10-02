using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

public enum MultiplayerChallengePhase { None, Invited, Teleporting, Lobby, Countdown, ReadyToBuild, Building, SubmissionsReady, PreparingTest, Testing, TestResults }
public enum MultiplayerChallengeResult { None, Declined, Cancelled, TimedOut, TravelFailed, PlayerLeft, BuildSetupFailed, TestSetupFailed }
public enum ChallengeTestOutcome { None, Crossed, Collapsed, Fell, Stalled, TimeLimit }

/// <summary>Session-only challenge state. Never added to either player's saved data.</summary>
public struct MultiplayerChallengeState : INetworkStruct
{
    public int Revision;
    public MultiplayerChallengePhase Phase;
    public MultiplayerChallengeResult Result;
    public PlayerRef Guest;
    public NetworkString<_256> SiteKey;
    public NetworkString<_128> ContractKey;
    public NetworkBool HostArrived;
    public NetworkBool GuestArrived;
    public NetworkBool HostReady;
    public NetworkBool GuestReady;
    public NetworkBool HostBuilding;
    public NetworkBool GuestBuilding;
    public NetworkBool HostSubmitted;
    public NetworkBool GuestSubmitted;
    public float HostSubmittedCost;
    public float GuestSubmittedCost;
    public int HostSubmittedBars;
    public int GuestSubmittedBars;
    public int HostSubmissionOrder;
    public int GuestSubmissionOrder;
    public int TestIndex;
    public NetworkBool GuestTestViewReady;
    public float TestElapsed;
    public float TestStress;
    public ChallengeTestOutcome HostTestOutcome;
    public ChallengeTestOutcome GuestTestOutcome;
    public float HostCrossingSeconds;
    public float GuestCrossingSeconds;
    public float HostPeakStress;
    public float GuestPeakStress;
    public NetworkString<_256> TestSetupMessage;
    public TickTimer Deadline;
}

public static class MultiplayerChallengeRules
{
    public static bool CanRespond(MultiplayerChallengeState state, PlayerRef player, int revision) =>
        state.Phase == MultiplayerChallengePhase.Invited && state.Revision == revision && state.Guest == player;

    public static bool CanArrive(MultiplayerChallengeState state, PlayerRef player, PlayerRef host, int revision) =>
        state.Phase == MultiplayerChallengePhase.Teleporting && state.Revision == revision &&
        (player == host || player == state.Guest);

    public static bool CanSetReady(MultiplayerChallengeState state, PlayerRef player, PlayerRef host,
        int revision, bool ready) => state.Revision == revision && state.HostArrived && state.GuestArrived &&
        (player == host || player == state.Guest) &&
        (state.Phase == MultiplayerChallengePhase.Lobby || (state.Phase == MultiplayerChallengePhase.Countdown && !ready));

    // Pure state transitions are shared by the host and isolated policy tests.
    // Only the host RPC writes their result to network state and supplies the timer.
    public static bool TrySetReady(ref MultiplayerChallengeState state, PlayerRef player, PlayerRef host,
        int revision, bool ready)
    {
        if (!CanSetReady(state, player, host, revision, ready)) return false;
        bool current = player == host ? (bool)state.HostReady : (bool)state.GuestReady;
        if (current == ready) return false; // Duplicate reliable requests cannot restart a countdown.
        if (player == host) state.HostReady = ready;
        else state.GuestReady = ready;
        state.Phase = state.HostReady && state.GuestReady
            ? MultiplayerChallengePhase.Countdown : MultiplayerChallengePhase.Lobby;
        state.Deadline = default;
        return true;
    }

    public static bool TryFinishCountdown(ref MultiplayerChallengeState state, bool expired)
    {
        if (state.Phase != MultiplayerChallengePhase.Countdown || !expired ||
            !state.HostArrived || !state.GuestArrived || !state.HostReady || !state.GuestReady) return false;
        state.Phase = MultiplayerChallengePhase.ReadyToBuild;
        state.Deadline = default;
        return true;
    }

    public static bool CanPrepareBuild(MultiplayerChallengeState state, PlayerRef player, PlayerRef host, int revision) =>
        state.Revision == revision && state.Phase == MultiplayerChallengePhase.ReadyToBuild &&
        state.HostArrived && state.GuestArrived && state.HostReady && state.GuestReady &&
        (player == host || player == state.Guest);

    public static bool TryPrepareBuild(ref MultiplayerChallengeState state, PlayerRef player, PlayerRef host, int revision)
    {
        if (!CanPrepareBuild(state, player, host, revision)) return false;
        if (player == host) { if (state.HostBuilding) return false; state.HostBuilding = true; }
        else { if (state.GuestBuilding) return false; state.GuestBuilding = true; }
        if (state.HostBuilding && state.GuestBuilding)
        { state.Phase = MultiplayerChallengePhase.Building; state.Deadline = default; }
        return true;
    }

    public static bool HasSubmitted(MultiplayerChallengeState state, PlayerRef player, PlayerRef host) =>
        player == host ? (bool)state.HostSubmitted : player == state.Guest && (bool)state.GuestSubmitted;

    public static bool CanSubmit(MultiplayerChallengeState state, PlayerRef player, PlayerRef host, int revision) =>
        state.Revision == revision && state.Phase == MultiplayerChallengePhase.Building &&
        state.HostArrived && state.GuestArrived && state.HostReady && state.GuestReady && state.HostBuilding && state.GuestBuilding &&
        (player == host || player == state.Guest) && !HasSubmitted(state, player, host);

    public static bool TryAcceptSubmission(ref MultiplayerChallengeState state, PlayerRef player, PlayerRef host,
        int revision, float cost, int bars)
    {
        if (!CanSubmit(state, player, host, revision) || float.IsNaN(cost) || float.IsInfinity(cost) || cost < 0f || bars <= 0)
            return false;
        if (player == host)
        { state.HostSubmissionOrder = state.GuestSubmitted ? 2 : 1; state.HostSubmitted = true; state.HostSubmittedCost = cost; state.HostSubmittedBars = bars; }
        else
        { state.GuestSubmissionOrder = state.HostSubmitted ? 2 : 1; state.GuestSubmitted = true; state.GuestSubmittedCost = cost; state.GuestSubmittedBars = bars; }
        if (state.HostSubmitted && state.GuestSubmitted) state.Phase = MultiplayerChallengePhase.SubmissionsReady;
        return true;
    }

    public static bool IsTestPhase(MultiplayerChallengePhase phase) => phase == MultiplayerChallengePhase.PreparingTest ||
        phase == MultiplayerChallengePhase.Testing || phase == MultiplayerChallengePhase.TestResults;

    public static bool TestsHostBridge(MultiplayerChallengeState state) => state.TestIndex == state.HostSubmissionOrder;

    public static bool TryPrepareTest(ref MultiplayerChallengeState state, int revision, int index)
    {
        if (state.Revision != revision || !state.HostSubmitted || !state.GuestSubmitted ||
            (state.HostSubmissionOrder != 1 && state.HostSubmissionOrder != 2) ||
            state.GuestSubmissionOrder != 3 - state.HostSubmissionOrder ||
            !((index == 1 && state.Phase == MultiplayerChallengePhase.SubmissionsReady && state.TestIndex == 0) ||
              (index == 2 && state.Phase == MultiplayerChallengePhase.Testing && state.TestIndex == 1 &&
               (TestsHostBridge(state) ? state.HostTestOutcome : state.GuestTestOutcome) != ChallengeTestOutcome.None))) return false;
        state.TestIndex = index; state.Phase = MultiplayerChallengePhase.PreparingTest;
        state.GuestTestViewReady = false; state.TestElapsed = state.TestStress = 0f; state.Deadline = default;
        return true;
    }

    public static bool TryStartTest(ref MultiplayerChallengeState state, int revision, int index)
    {
        if (state.Revision != revision || state.TestIndex != index || (index != 1 && index != 2) ||
            state.Phase != MultiplayerChallengePhase.PreparingTest || !state.GuestTestViewReady ||
            !state.HostSubmitted || !state.GuestSubmitted) return false;
        state.Phase = MultiplayerChallengePhase.Testing; state.Deadline = default; return true;
    }

    public static bool TryRecordTest(ref MultiplayerChallengeState state, int revision, int index,
        ChallengeTestOutcome outcome, float seconds, float peakStress)
    {
        if (state.Revision != revision || state.TestIndex != index || state.Phase != MultiplayerChallengePhase.Testing ||
            (index != 1 && index != 2) || !Enum.IsDefined(typeof(ChallengeTestOutcome), outcome) || outcome == ChallengeTestOutcome.None ||
            float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f ||
            float.IsNaN(peakStress) || float.IsInfinity(peakStress) || peakStress < 0f ||
            (TestsHostBridge(state) ? state.HostTestOutcome : state.GuestTestOutcome) != ChallengeTestOutcome.None) return false;
        if (TestsHostBridge(state))
        { state.HostTestOutcome = outcome; state.HostCrossingSeconds = seconds; state.HostPeakStress = peakStress; }
        else
        { state.GuestTestOutcome = outcome; state.GuestCrossingSeconds = seconds; state.GuestPeakStress = peakStress; }
        return true;
    }

    public static bool TryFinishTests(ref MultiplayerChallengeState state, int revision)
    {
        if (state.Revision != revision || state.Phase != MultiplayerChallengePhase.Testing || state.TestIndex != 2 ||
            state.HostTestOutcome == ChallengeTestOutcome.None || state.GuestTestOutcome == ChallengeTestOutcome.None) return false;
        state.Phase = MultiplayerChallengePhase.TestResults; return true;
    }

    // Names, rather than runtime instance IDs or root sibling indices, survive
    // loading the same authored scene on separate machines. Reject ambiguous paths.
    public static string SiteKey(BuildLocation location)
    {
        if (location == null) return string.Empty;
        var names = new List<string>();
        for (Transform node = location.transform; node != null; node = node.parent)
            names.Add(Uri.EscapeDataString(node.name));
        names.Reverse();
        return string.Join("/", names);
    }

    public static BuildLocation ResolveSite(string key)
    {
        BuildLocation match = null;
        foreach (BuildLocation site in UnityEngine.Object.FindObjectsOfType<BuildLocation>(true))
        {
            if (site.gameObject.scene.name != FusionConnectionManager.HostWorldSceneName || SiteKey(site) != key) continue;
            if (match != null) return null;
            match = site;
        }
        return match;
    }

    public static bool IsCompletedHostSite(BuildLocation site)
    {
        PlayerDataManager data = PlayerDataManager.Instance;
        return site != null && site.gameObject.scene.name == FusionConnectionManager.HostWorldSceneName &&
            site.activeContract != null && site.bakedBars.Exists(bar => bar != null) && !site.IsRedesigningBridge &&
            !site.IsRedesignBlockedByNPCTravel && data != null && data.IsContractCompleted(site.activeContract.ContractID);
    }
}
