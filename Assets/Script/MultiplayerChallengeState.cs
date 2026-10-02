using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

public enum MultiplayerChallengePhase { None, Invited, Teleporting, Lobby }
public enum MultiplayerChallengeResult { None, Declined, Cancelled, TimedOut, TravelFailed, PlayerLeft }

/// <summary>Session-only challenge state. Never added to either player's saved data.</summary>
public struct MultiplayerChallengeState : INetworkStruct
{
    public int Revision;
    public MultiplayerChallengePhase Phase;
    public MultiplayerChallengeResult Result;
    public PlayerRef Guest;
    public NetworkString<_256> SiteKey;
    public NetworkBool HostArrived;
    public NetworkBool GuestArrived;
    public TickTimer Deadline;
}

public static class MultiplayerChallengeRules
{
    public static bool CanRespond(MultiplayerChallengeState state, PlayerRef player, int revision) =>
        state.Phase == MultiplayerChallengePhase.Invited && state.Revision == revision && state.Guest == player;

    public static bool CanArrive(MultiplayerChallengeState state, PlayerRef player, PlayerRef host, int revision) =>
        state.Phase == MultiplayerChallengePhase.Teleporting && state.Revision == revision &&
        (player == host || player == state.Guest);

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
