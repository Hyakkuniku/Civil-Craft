using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

public sealed partial class FusionMultiplayerAvatar
{
    [Networked] private bool ChallengeCompletionsReady { get; set; }
    [Networked] private NetworkString<_64> ChallengeCompletionManifest { get; set; }
    [Networked] private ulong ChallengeCompletedLow { get; set; }
    [Networked] private ulong ChallengeCompletedHigh { get; set; }

    private ChallengeCompletionCatalog completionCatalog;
    private PlayerDataManager completionData;
    private PlayerData completionProfile;
    private string completionScope;
    private int completionCatalogSize = -1;
    private ulong completionCatalogSourceHash;
    private bool completionRepublish;
    private bool completionDirty = true;
    private bool localCompletionsReady;
    private ulong localCompletedLow, localCompletedHigh;
    private float nextCompletionCheck;

    private void ResetChallengeCompletionPublication()
    {
        DisposeChallengeCompletionPublication();
        nextCompletionCheck = 0f;
        if (HasStateAuthority) InvalidateChallengeCompletionSnapshot();
    }

    private void DisposeChallengeCompletionPublication()
    {
        if (completionData != null) completionData.OnSaveCommitted -= MarkChallengeCompletionsDirty;
        completionData = null;
        completionProfile = null;
        completionCatalog = null;
        completionCatalogSize = -1;
        completionCatalogSourceHash = 0;
        completionScope = null;
        completionDirty = true;
        localCompletionsReady = false;
        completionRepublish = true;
    }

    private void MarkChallengeCompletionsDirty() => completionDirty = true;

    private ChallengeCompletionCatalog EnsureChallengeCompletionCatalog()
    {
        PlayerDataManager data = PlayerDataManager.Instance;
        if (data == null || data.allGameContracts == null) return null;
        if (completionData != data)
        {
            DisposeChallengeCompletionPublication();
            completionData = data;
            if (HasInputAuthority) completionData.OnSaveCommitted += MarkChallengeCompletionsDirty;
        }
        ulong sourceHash = 14695981039346656037UL;
        unchecked
        {
            foreach (ContractSO contract in data.allGameContracts)
            {
                string id = contract != null ? contract.ContractID : string.Empty;
                foreach (char value in id) { sourceHash ^= value; sourceHash *= 1099511628211UL; }
                sourceHash ^= 255; sourceHash *= 1099511628211UL;
            }
        }
        if (completionCatalog != null && completionCatalogSize == data.allGameContracts.Count &&
            completionCatalogSourceHash == sourceHash) return completionCatalog.Count > 0 ? completionCatalog : null;
        var ids = new List<string>();
        foreach (ContractSO contract in data.allGameContracts)
            if (contract != null) ids.Add(contract.ContractID);
        try
        {
            completionCatalog = ChallengeCompletionCatalog.Create(ids);
            completionCatalogSize = data.allGameContracts.Count;
            completionCatalogSourceHash = sourceHash;
            completionDirty = true;
            completionRepublish = true;
            return completionCatalog.Count > 0 ? completionCatalog : null;
        }
        catch (Exception error)
        {
            completionCatalog = null;
            Debug.LogWarning("[Challenge completion] Invalid contract catalog: " + error.Message, this);
            return null;
        }
    }

    private static bool IsPersonalChallengeSaveReady(PlayerDataManager data)
    {
        if (data == null || data.CurrentData == null) return false;
        string account = MultiplayerResultReportingService.AuthenticatedAccountId ?? string.Empty;
        if (!string.Equals(data.ActiveAccountId ?? string.Empty, account, StringComparison.OrdinalIgnoreCase)) return false;
        CloudSaveManager cloud = data.GetComponent<CloudSaveManager>();
        return cloud == null || !cloud.HasConflict;
    }

    private void UpdateChallengeCompletionPublication()
    {
        if (Object == null || !Object.IsValid || !HasInputAuthority || Time.unscaledTime < nextCompletionCheck) return;
        nextCompletionCheck = Time.unscaledTime + .5f;
        ChallengeCompletionCatalog catalog = EnsureChallengeCompletionCatalog();
        PlayerDataManager data = PlayerDataManager.Instance;
        string scope = data != null ? data.ActiveAccountId ?? string.Empty : string.Empty;
        if (!ReferenceEquals(completionProfile, data != null ? data.CurrentData : null) || completionScope != scope)
        {
            completionProfile = data != null ? data.CurrentData : null;
            completionScope = scope;
            completionDirty = true;
            completionRepublish = true;
        }
        bool ready = catalog != null && IsPersonalChallengeSaveReady(data);
        if (!ready)
        {
            completionDirty = true;
            localCompletionsReady = false;
            localCompletedLow = localCompletedHigh = 0;
        }
        else if (completionDirty || !localCompletionsReady)
        {
            // CurrentData remains this player's own save when visiting a host.
            // A received host bridge is only a visual and never supplies these bits.
            catalog.Capture(data.IsContractCompleted, out localCompletedLow, out localCompletedHigh);
            localCompletionsReady = true;
            completionDirty = false;
        }
        string manifest = ready ? catalog.Fingerprint : string.Empty;
        if (!completionRepublish && ChallengeCompletionsReady == ready && ChallengeCompletionManifest.ToString() == manifest &&
            string.Equals(AuthenticatedPlayFabId.ToString(), MultiplayerResultReportingService.AuthenticatedAccountId ?? string.Empty,
                StringComparison.OrdinalIgnoreCase) &&
            ChallengeCompletedLow == localCompletedLow && ChallengeCompletedHigh == localCompletedHigh) return;
        PublishAuthenticatedAccountIdentity();
        if (HasStateAuthority)
            ApplyChallengeCompletions(manifest, localCompletedLow, localCompletedHigh,
                MultiplayerResultReportingService.AuthenticatedAccountId ?? string.Empty, ready);
        else RPC_PublishChallengeCompletions(manifest, localCompletedLow, localCompletedHigh,
            MultiplayerResultReportingService.AuthenticatedAccountId ?? string.Empty, ready);
        completionRepublish = false;
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority, Channel = RpcChannel.Reliable)]
    private void RPC_PublishChallengeCompletions(string manifest, ulong low, ulong high, string account, bool ready)
    {
        if (!HasStateAuthority) return;
        ApplyChallengeCompletions(manifest, low, high, account, ready);
    }

    private void ApplyChallengeCompletions(string manifest, ulong low, ulong high, string account, bool ready)
    {
        InvalidateChallengeCompletionSnapshot();
        if (!ready || !string.Equals(account, AuthenticatedPlayFabId.ToString(), StringComparison.OrdinalIgnoreCase)) return;
        ChallengeCompletionCatalog catalog = EnsureChallengeCompletionCatalog();
        if (catalog == null || !string.Equals(manifest, catalog.Fingerprint, StringComparison.Ordinal) || !catalog.ValidBits(low, high)) return;
        ChallengeCompletionManifest = manifest;
        ChallengeCompletedLow = low;
        ChallengeCompletedHigh = high;
        ChallengeCompletionsReady = true;
    }

    private void InvalidateChallengeCompletionSnapshot()
    {
        ChallengeCompletionsReady = false;
        ChallengeCompletionManifest = string.Empty;
        ChallengeCompletedLow = ChallengeCompletedHigh = 0;
    }

    private bool TryGetChallengeGuest(out FusionMultiplayerAvatar guest)
    {
        guest = null;
        if (Runner == null || !Runner.IsRunning || !IsSessionHost) return false;
        foreach (PlayerRef player in Runner.ActivePlayers)
            if (player != Object.InputAuthority && Runner.TryGetPlayerObject(player, out NetworkObject obj) &&
                obj != null && obj.IsValid && obj.GetComponent<FusionMultiplayerAvatar>() is FusionMultiplayerAvatar avatar)
            {
                if (guest != null) { guest = null; return false; }
                guest = avatar;
            }
        return guest != null;
    }

    public bool AreChallengeCompletionsReady
    {
        get
        {
            ChallengeCompletionCatalog catalog = EnsureChallengeCompletionCatalog();
            return catalog != null && ChallengeCompletionsReady &&
                ChallengeCompletionManifest.ToString() == catalog.Fingerprint &&
                TryGetChallengeGuest(out FusionMultiplayerAvatar guest) && guest.ChallengeCompletionsReady &&
                guest.ChallengeCompletionManifest.ToString() == catalog.Fingerprint;
        }
    }

    private bool BothPlayersCompletedChallengeContract(string contractId)
    {
        ChallengeCompletionCatalog catalog = EnsureChallengeCompletionCatalog();
        if (catalog == null || !AreChallengeCompletionsReady || !TryGetChallengeGuest(out FusionMultiplayerAvatar guest)) return false;
        // Re-read the host's personal record at admission rather than trusting a stale local bit.
        PlayerDataManager data = PlayerDataManager.Instance;
        return IsPersonalChallengeSaveReady(data) && data.IsContractCompleted(contractId) &&
            catalog.HasCompletion(contractId, ChallengeCompletionManifest.ToString(), ChallengeCompletionsReady,
                ChallengeCompletedLow, ChallengeCompletedHigh) &&
            catalog.HasCompletion(contractId, guest.ChallengeCompletionManifest.ToString(), guest.ChallengeCompletionsReady,
                guest.ChallengeCompletedLow, guest.ChallengeCompletedHigh);
    }
}
