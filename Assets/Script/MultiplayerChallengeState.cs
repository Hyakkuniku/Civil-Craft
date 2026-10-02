using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

public enum MultiplayerChallengePhase { None, Invited, Teleporting, Lobby, Countdown, ReadyToBuild, Building }
public enum MultiplayerChallengeResult { None, Declined, Cancelled, TimedOut, TravelFailed, PlayerLeft, BuildSetupFailed }

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
