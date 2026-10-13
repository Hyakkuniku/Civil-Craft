#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using PlayFab;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Manually captures allowlisted LIVE state. No auto capture, network requests,
/// file access, player/journal writes, session changes or wallet operations.
/// </summary>
public sealed class WalletReadOnlyCheck : EditorWindow
{
    private string report;
    private bool passed;
    private Vector2 scroll;

    [MenuItem("Civil Craft/Wallet/Read-Only Check")]
    public static void Open()
    {
        var window = GetWindow<WalletReadOnlyCheck>("Wallet Read-Only Check");
        window.minSize = new Vector2(580, 480);
        window.Show();
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox("Diagnostic only: does not activate, import, credit, refresh or save anything. " +
            "Load testBetaOmaygosh's existing save in Play Mode and let sync finish before capturing.", MessageType.Info);
        using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying || EditorApplication.isCompiling))
            if (GUILayout.Button("Capture live read-only check")) Capture();
        if (string.IsNullOrEmpty(report)) return;
        EditorGUILayout.HelpBox(passed ? "PASS: captured live state matches the reviewed fresh zero opening. " +
            "Coins are NOT activated by this check." : "NOT VERIFIED: see the report. No state was changed.",
            passed ? MessageType.Info : MessageType.Warning);
        if (GUILayout.Button("Copy safe report")) EditorGUIUtility.systemCopyBuffer = report;
        scroll = EditorGUILayout.BeginScrollView(scroll);
        var style = new GUIStyle(EditorStyles.textArea) { wordWrap = true };
        float width = Math.Max(420f, position.width - 40f);
        float height = style.CalcHeight(new GUIContent(report), width);
        EditorGUILayout.SelectableLabel(report, style, GUILayout.Height(Math.Max(250f, height)));
        EditorGUILayout.EndScrollView();
    }

    private void Capture()
    {
        try
        {
            var first = ReadLive(out string firstJson, out object[] firstRefs);
            // No await/coroutine/yield or game method dispatch: both samples are
            // taken synchronously on the Editor main thread. Fail on any drift.
            var second = ReadLive(out string secondJson, out object[] secondRefs);
            bool sameRefs = firstRefs.Length == secondRefs.Length;
            for (int i = 0; sameRefs && i < firstRefs.Length; i++)
                sameRefs = ReferenceEquals(firstRefs[i], secondRefs[i]);
            // Before adding timestamps/coherence, this serialized DTO contains
            // only fixed allowlisted readings. No SDK/save/journal objects.
            first.coherentCapture = sameRefs && !string.IsNullOrEmpty(firstJson) &&
                string.Equals(firstJson, secondJson, StringComparison.Ordinal) &&
                string.Equals(JsonUtility.ToJson(first), JsonUtility.ToJson(second), StringComparison.Ordinal);
            first.capturedAtUtc = DateTime.UtcNow.ToString("O");
            List<string> errors = WalletReadOnlyCheckPolicy.Evaluate(first);
            passed = errors.Count == 0;
            report = "Wallet Read-Only Check v1\n" + (passed ? "RESULT: PASS" : "RESULT: NOT VERIFIED") +
                "\nThis is diagnostic evidence at one instant, not import/activation permission.\n" +
                (errors.Count == 0 ? "" : "Checks:\n- " + string.Join("\n- ", errors) + "\n") +
                JsonUtility.ToJson(first, true);
        }
        catch (Exception)
        {
            // Never echo arbitrary exception text, paths, state or auth contexts.
            passed = false;
            report = "Wallet Read-Only Check v1\nRESULT: NOT VERIFIED\n" +
                "The live components or reviewed field layout could not be read. No state-changing game/wallet method was called.";
        }
        Repaint();
    }

    private static WalletReadOnlyCheckPolicy.Evidence ReadLive(out string json, out object[] references)
    {
        var e = new WalletReadOnlyCheckPolicy.Evidence { editorPlaying = EditorApplication.isPlaying };
        json = null;
        references = Array.Empty<object>();
        if (!e.editorPlaying || EditorApplication.isCompiling) return e;
        PlayerDataManager manager = PlayerDataManager.Instance;
        if (manager == null || manager.CurrentData == null) return e;
        CloudSaveManager cloud = manager.GetComponent<CloudSaveManager>();
        GameWalletService wallet = manager.GetComponent<GameWalletService>();
        if (cloud == null || wallet == null) return e;
        PlayerData data = manager.CurrentData;

        bool fields = true;
        var entity = ReadField<PlayFab.DataModels.EntityKey>(cloud, "entity", ref fields);
        var journal = ReadField<GameWalletJournal>(wallet, "journal", ref fields);
        PlayerDataManager cloudManager = ReadField<PlayerDataManager>(cloud, "saveManager", ref fields);
        PlayerDataManager walletManager = ReadField<PlayerDataManager>(wallet, "playerData", ref fields);
        CloudSaveManager walletCloud = ReadField<CloudSaveManager>(wallet, "cloud", ref fields);
        e.walletAccount = ReadField<string>(wallet, "accountId", ref fields);
        e.walletTitle = ReadField<string>(wallet, "titleId", ref fields);
        e.walletGeneration = ReadField<int>(wallet, "generation", ref fields);
        e.journalAvailable = ReadField<bool>(wallet, "journalAvailable", ref fields);
        e.journalDirty = ReadField<bool>(wallet, "journalDirty", ref fields);
        e.walletInFlight = ReadField<bool>(wallet, "inFlight", ref fields);
        e.suspendedForConflict = ReadField<bool>(cloud, "suspendSyncForConflict", ref fields);
        e.startupSelectionPending = ReadField<bool>(cloud, "startupSelectionPending", ref fields);
        e.auxiliaryFileOperation = ReadField<bool>(cloud, "auxiliaryFileOperationInProgress", ref fields);
        e.legacyImportInProgress = ReadField<bool>(cloud, "legacyWalletImportInProgress", ref fields);
        e.cloudRetryRequired = ReadField<bool>(cloud, "retryRequired", ref fields);
        string lastSyncedHash = ReadField<string>(cloud, "lastSyncedHash", ref fields);
        e.reflectionFieldsAvailable = fields;
        e.componentsBound = ReferenceEquals(manager, cloudManager) && ReferenceEquals(manager, walletManager) &&
            ReferenceEquals(cloud, walletCloud);

        // Only public title/classic identity and the logged-in BOOLEAN. Never
        // inspect, serialize or display an authentication context or ticket/token.
        e.sdkTitle = PlayFabSettings.staticSettings.TitleId;
        e.sdkAccount = PlayFabSettings.staticPlayer.PlayFabId;
        e.loggedIn = PlayFabClientAPI.IsClientLoggedIn();
        e.loginChoice = PlayerPrefs.GetInt("LoginChoice", 0); // 1=guest; 2=signed-in.
        e.protectedMultiplayerGuest = FusionConnectionManager.Instance != null &&
            FusionConnectionManager.Instance.IsGuestSaveProtected;
        e.dataAccount = manager.ActiveAccountId;
        e.cloudAccount = cloud.ActiveAccountId;
        e.cloudGeneration = cloud.SessionGeneration;
        e.entityId = entity != null ? entity.Id : null;
        e.entityType = entity != null ? entity.Type : null;
        e.sessionReady = cloud.IsSessionReady;
        e.legacySourceReady = cloud.IsLegacyWalletImportSourceReady;
        e.canImportLegacy = cloud.CanImportLegacyWallet;
        e.canPublishDashboard = cloud.CanPublishDashboard;
        e.hasConflict = cloud.HasConflict;
        e.cloudInFlight = cloud.IsSyncing;
        e.hasSignedInAccount = wallet.HasSignedInAccount;
        e.requiresSignIn = wallet.RequiresSignIn;

        e.gold = data.gold; // Raw values; never clamp a negative into approval.
        e.lifetimeGoldEarned = data.lifetimeGoldEarned;
        e.lifetimeGoldSpent = data.lifetimeGoldSpent;
        e.walletAuthorityVersion = data.walletAuthorityVersion;
        e.walletCoinsVersion = data.walletCoinsVersion;
        e.completedContracts = Count(data.completedContracts);
        e.unlockedAchievements = Count(data.unlockedAchievements);
        e.purchasedShopItems = Count(data.purchasedShopItemIds);
        e.unlockedCosmetics = Count(data.unlockedCosmeticIDs);
        e.unlockedContractMaterials = Count(data.unlockedContractMaterials);
        e.rewardOutbox = Count(data.walletRewardOutbox);
        e.contractEvidence = Count(data.walletContractEvidence);
        if (journal != null)
        {
            e.journalSchemaVersion = journal.schemaVersion;
            e.journalTitle = journal.titleId;
            e.journalAccount = journal.accountId;
            e.journalRewards = Count(journal.rewards);
            e.journalPurchases = Count(journal.purchases);
            e.journalDeliveredRewards = Count(journal.deliveredRewardIds);
        }
        json = manager.GetCurrentDataJson(); // Same exact compact JSON used by game import.
        e.saveHash = Hash(json);
        e.matchesLastSyncedHash = !string.IsNullOrEmpty(lastSyncedHash) && e.saveHash == lastSyncedHash;
        references = new object[] { manager, cloud, wallet, data, journal, entity };
        return e;
    }

    private static T ReadField<T>(object owner, string name, ref bool fieldsAvailable)
    {
        FieldInfo field = owner.GetType().GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        if (field == null || field.FieldType != typeof(T)) { fieldsAvailable = false; return default(T); }
        return (T)field.GetValue(owner); // Fixed named fields only; never invoke methods/setters.
    }

    private static int Count<T>(List<T> values) => values != null ? values.Count : -1;

    private static string Hash(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        using (SHA256 sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(json))).Replace("-", "").ToLowerInvariant();
    }
}
#endif
