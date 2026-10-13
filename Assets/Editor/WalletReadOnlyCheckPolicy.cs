using System;
using System.Collections.Generic;

/// <summary>
/// Pure review policy for the one approved fresh-account zero-opening test.
/// A passing diagnostic is not an import, activation or monetary authorization.
/// This file lives under Editor and is excluded from player builds.
/// </summary>
public static class WalletReadOnlyCheckPolicy
{
    public const string ExpectedTitle = "17FA03";
    public const string ExpectedClassicPlayer = "5D2A34877435C96";
    public const string ExpectedEntity = "B6C2522543CEB1DA";
    public const string ExpectedEntityType = "title_player_account";

    [Serializable]
    public sealed class Evidence
    {
        public int diagnosticSchemaVersion = 1;
        public string capturedAtUtc;
        public bool editorPlaying;
        public bool reflectionFieldsAvailable;
        public bool coherentCapture;
        public bool componentsBound;
        public bool loggedIn;
        public int loginChoice;
        public bool protectedMultiplayerGuest;
        public string sdkTitle;
        public string sdkAccount;
        public string dataAccount;
        public string cloudAccount;
        public string walletAccount;
        public string walletTitle;
        public string entityId;
        public string entityType;
        public int cloudGeneration = -1;
        public int walletGeneration = -1;
        public bool sessionReady;
        public bool legacySourceReady;
        public bool canImportLegacy;
        public bool canPublishDashboard;
        public bool hasConflict;
        public bool cloudInFlight;
        public bool suspendedForConflict;
        public bool startupSelectionPending;
        public bool auxiliaryFileOperation;
        public bool legacyImportInProgress;
        public bool cloudRetryRequired;
        public bool hasSignedInAccount;
        public bool requiresSignIn;
        public bool walletInFlight;
        public bool journalAvailable;
        public bool journalDirty;
        public int journalSchemaVersion = -1;
        public string journalTitle;
        public string journalAccount;
        public int journalRewards = -1;
        public int journalPurchases = -1;
        public int journalDeliveredRewards = -1;
        public int gold;
        public int lifetimeGoldEarned;
        public int lifetimeGoldSpent;
        public int walletAuthorityVersion;
        public long walletCoinsVersion;
        public int completedContracts = -1;
        public int unlockedAchievements = -1;
        public int purchasedShopItems = -1;
        public int unlockedCosmetics = -1;
        public int unlockedContractMaterials = -1;
        public int rewardOutbox = -1;
        public int contractEvidence = -1;
        public string saveHash;
        // Informational: position/scene-only changes can differ from the last upload.
        public bool matchesLastSyncedHash;
    }

    public static List<string> Evaluate(Evidence e)
    {
        var errors = new List<string>();
        if (e == null) { errors.Add("No live evidence was captured."); return errors; }
        if (e.diagnosticSchemaVersion != 1) errors.Add("Unsupported diagnostic schema.");
        if (!e.editorPlaying) errors.Add("Load the account in Play Mode first.");
        if (!e.reflectionFieldsAvailable) errors.Add("A reviewed runtime field is missing or has changed type.");
        if (!e.coherentCapture) errors.Add("The live account, save or readiness changed during capture; retake.");
        if (!e.componentsBound) errors.Add("The save, cloud and wallet components are not bound to the same player manager.");
        if (!e.loggedIn || e.loginChoice != 2 || !e.hasSignedInAccount || e.requiresSignIn)
            errors.Add("The game is not using the expected signed-in player session.");
        if (e.protectedMultiplayerGuest) errors.Add("A protected multiplayer guest visit cannot be used for migration review.");
        if (!Same(e.sdkTitle, ExpectedTitle) || !Same(e.walletTitle, ExpectedTitle))
            errors.Add("The active title does not match the approved title.");
        if (!Same(e.sdkAccount, ExpectedClassicPlayer) || !Same(e.dataAccount, ExpectedClassicPlayer) ||
            !Same(e.cloudAccount, ExpectedClassicPlayer) || !Same(e.walletAccount, ExpectedClassicPlayer) ||
            !Same(e.journalAccount, ExpectedClassicPlayer))
            errors.Add("The SDK, selected save, cloud, wallet or journal account does not match testBetaOmaygosh.");
        if (!Same(e.entityId, ExpectedEntity) || e.entityType != ExpectedEntityType)
            errors.Add("The selected cloud entity does not match the approved player entity.");
        if (e.cloudGeneration < 0 || e.cloudGeneration != e.walletGeneration)
            errors.Add("The cloud and wallet session generations do not match.");
        if (!e.sessionReady || !e.legacySourceReady || !e.canImportLegacy || !e.canPublishDashboard ||
            e.hasConflict || e.cloudInFlight || e.suspendedForConflict || e.startupSelectionPending ||
            e.auxiliaryFileOperation || e.legacyImportInProgress || e.cloudRetryRequired)
            errors.Add("Online save selection is not settled; finish sync/conflict resolution and retake.");
        if (e.walletInFlight || !e.journalAvailable || e.journalDirty || e.journalSchemaVersion != 1 ||
            !Same(e.journalTitle, ExpectedTitle))
            errors.Add("The matching account wallet journal is unavailable, dirty or busy.");
        if (e.gold != 0 || e.lifetimeGoldEarned != 0 || e.lifetimeGoldSpent != 0 ||
            e.walletAuthorityVersion != 0 || e.walletCoinsVersion != 0)
            errors.Add("This is not the reviewed zero-balance, unmigrated legacy opening; review separately.");
        if (e.completedContracts != 0 || e.unlockedAchievements != 0 || e.purchasedShopItems != 0 ||
            e.unlockedCosmetics != 0 || e.unlockedContractMaterials != 0 || e.rewardOutbox != 0 ||
            e.contractEvidence != 0 || e.journalRewards != 0 || e.journalPurchases != 0 ||
            e.journalDeliveredRewards != 0)
            errors.Add("Import-related progression, ownership or operation lists are nonempty or unavailable; review separately.");
        if (!IsSha256(e.saveHash)) errors.Add("The exact compact live-save SHA-256 is missing or invalid.");
        return errors;
    }

    private static bool Same(string actual, string expected) =>
        !string.IsNullOrEmpty(actual) && string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

    private static bool IsSha256(string value)
    {
        if (value == null || value.Length != 64) return false;
        foreach (char c in value) if (!(c >= '0' && c <= '9' || c >= 'a' && c <= 'f')) return false;
        return true;
    }
}
