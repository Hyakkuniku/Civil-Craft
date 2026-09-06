using UnityEngine;
using System.IO;
using System;
using System.Collections.Generic;

[DisallowMultipleComponent]
public class PlayerDataManager : MonoBehaviour
{
    private const string AchievementsFeatureId = "achievements";

    public static PlayerDataManager Instance { get; private set; }
    public PlayerData CurrentData { get; private set; }
    
    [Header("Achievements Database")]
    [Tooltip("Drag ALL your AchievementSO files here so the game can automatically check them!")]
    public List<AchievementSO> allGameAchievements = new List<AchievementSO>();

    [Header("Contracts Database")]
    [Tooltip("All ContractSO assets used by map-completion achievements. This list is populated automatically in the Unity Editor.")]
    public List<ContractSO> allGameContracts = new List<ContractSO>();

    public Action OnAlmanacUnlocked;
    public Action OnAlmanacAlertsChanged;
    public Action OnObjectiveAlertsChanged;
    public Action OnMinimapUnlockChanged;
    /// <summary>Raised whenever the persistent feature-unlock collection changes.</summary>
    public Action OnFeatureUnlocksChanged;
    public Action<AchievementSO> OnAchievementUnlocked; 
    /// <summary>Raised once when a contract is newly added to persistent completion data.</summary>
    public Action<string> OnContractCompleted;
    /// <summary>Raised once when a LessonData ID is newly added to the archive.</summary>
    public Action<string> OnLessonUnlocked;
    /// <summary>Raised after a material is newly acknowledged and saved to the Almanac.</summary>
    public Action<string> OnMaterialDiscovered;
    
    // Optional: Useful if you have a top-right Gold UI that needs to refresh immediately!
    public Action OnCurrencyChanged; 
    /// <summary>Raised after a shop purchase has been saved successfully.</summary>
    public Action<string> OnShopItemPurchased;
    
    private string saveFilePath;
    private bool isCheckingAchievements = false; // Prevents infinite loops!
    private bool hasMigratedContractIdentifiers;
    private bool suppressAutomaticPositionSave;
    private readonly HashSet<string> completionRecordsMissingBridge = new HashSet<string>();
    

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        Instance = null;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        // DontDestroyOnLoad rejects child objects. Keep the code defensive even
        // when a scene or prefab accidentally nests this service under Managers.
        if (transform.parent != null)
            transform.SetParent(null, true);

        DontDestroyOnLoad(gameObject);

        saveFilePath = Application.persistentDataPath + "/playerSaveData.json";
        LoadGame();
        RegisterContracts(Resources.FindObjectsOfTypeAll<ContractSO>());
        MigrateCompletedContractFeatureUnlocks();
        MigrateEarnedAchievementFeatureUnlock();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) return;

        string[] achievementGuids = UnityEditor.AssetDatabase.FindAssets("t:AchievementSO");
        List<AchievementSO> discoveredAchievements = new List<AchievementSO>();
        foreach (string guid in achievementGuids)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            AchievementSO achievement = UnityEditor.AssetDatabase.LoadAssetAtPath<AchievementSO>(path);
            if (achievement != null) discoveredAchievements.Add(achievement);
        }
        discoveredAchievements.Sort((left, right) =>
            string.Compare(left.achievementID, right.achievementID, StringComparison.OrdinalIgnoreCase));
        if (!ListsMatch(allGameAchievements, discoveredAchievements))
        {
            allGameAchievements = discoveredAchievements;
            UnityEditor.EditorUtility.SetDirty(this);
        }

        string[] contractGuids = UnityEditor.AssetDatabase.FindAssets("t:ContractSO");
        List<ContractSO> discoveredContracts = new List<ContractSO>();
        foreach (string guid in contractGuids)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            ContractSO contract = UnityEditor.AssetDatabase.LoadAssetAtPath<ContractSO>(path);
            if (contract != null) discoveredContracts.Add(contract);
        }

        discoveredContracts.Sort((left, right) =>
            string.Compare(left.ContractID, right.ContractID, StringComparison.OrdinalIgnoreCase));

        bool changed = allGameContracts == null || allGameContracts.Count != discoveredContracts.Count;
        if (!changed)
        {
            for (int i = 0; i < discoveredContracts.Count; i++)
            {
                if (allGameContracts[i] == discoveredContracts[i]) continue;
                changed = true;
                break;
            }
        }

        if (!changed) return;
        allGameContracts = discoveredContracts;
        UnityEditor.EditorUtility.SetDirty(this);
    }

    private static bool ListsMatch<T>(List<T> current, List<T> discovered) where T : UnityEngine.Object
    {
        if (current == null || current.Count != discovered.Count) return false;
        for (int i = 0; i < discovered.Count; i++)
        {
            if (current[i] != discovered[i]) return false;
        }
        return true;
    }
#endif

    public void SaveGame()
    {
        TrySaveGame();
    }

    /// <summary>
    /// Permanently unlocks the overworld minimap. This is safe to call from a
    /// contract reward, tutorial UnityEvent, pickup, or debug control.
    /// </summary>
    public void UnlockMinimap()
    {
        if (CurrentData == null || CurrentData.hasUnlockedMinimap) return;

        CurrentData.hasUnlockedMinimap = true;
        SaveGame();
        OnMinimapUnlockChanged?.Invoke();
    }

    /// <summary>
    /// Permanently unlocks a feature by stable ID. This can also be called directly
    /// from a UnityEvent for non-contract rewards.
    /// </summary>
    public bool UnlockFeature(string featureId)
    {
        string normalizedId = NormalizeFeatureId(featureId);
        if (CurrentData == null || string.IsNullOrEmpty(normalizedId)) return false;

        EnsureFeatureUnlockList();
        if (IsFeatureUnlocked(normalizedId)) return false;

        CurrentData.unlockedFeatureIds.Add(normalizedId);
        if (!TrySaveGame())
        {
            CurrentData.unlockedFeatureIds.Remove(normalizedId);
            return false;
        }

        OnFeatureUnlocksChanged?.Invoke();
        AchievementPopupNotification.NotifyFeatureUnlock(GetFeatureDisplayName(normalizedId));
        return true;
    }

    public bool IsFeatureUnlocked(string featureId)
    {
        string normalizedId = NormalizeFeatureId(featureId);
        return CurrentData != null && !string.IsNullOrEmpty(normalizedId) &&
               CurrentData.unlockedFeatureIds != null &&
               CurrentData.unlockedFeatureIds.Exists(savedId =>
                   string.Equals(NormalizeFeatureId(savedId), normalizedId,
                       StringComparison.Ordinal));
    }

    public bool TrySaveGame()
    {
        if (CurrentData == null || string.IsNullOrWhiteSpace(saveFilePath)) return false;

        string temporaryPath = saveFilePath + ".tmp";
        string backupPath = saveFilePath + ".bak";

        try
        {
            string json = JsonUtility.ToJson(CurrentData, true);
            File.WriteAllText(temporaryPath, json);

            if (File.Exists(saveFilePath))
            {
                try
                {
                    File.Replace(temporaryPath, saveFilePath, backupPath);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Copy(saveFilePath, backupPath, true);
                    File.Copy(temporaryPath, saveFilePath, true);
                    File.Delete(temporaryPath);
                }
                catch (IOException)
                {
                    File.Copy(saveFilePath, backupPath, true);
                    File.Copy(temporaryPath, saveFilePath, true);
                    File.Delete(temporaryPath);
                }
            }
            else
            {
                File.Move(temporaryPath, saveFilePath);
            }

            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError($"[PlayerDataManager] Failed to save player data: {exception.Message}", this);
            try
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            catch (Exception) { }
            return false;
        }
    }

    public void LoadGame()
    {
        CurrentData = TryReadSaveFile(saveFilePath);
        if (CurrentData == null)
        {
            CurrentData = TryReadSaveFile(saveFilePath + ".bak");
            if (CurrentData != null)
                Debug.LogWarning("[PlayerDataManager] Recovered player data from the backup save.", this);
        }

        if (CurrentData == null)
            CurrentData = new PlayerData
            {
                playerName = PlayerPrefs.GetString("SavedPlayerName", "Guest")
            };

        NormalizeLoadedData();
        SaveGame();
    }

    /// <summary>
    /// Replaces the live player data with a debugger snapshot and commits it
    /// through the same atomic JSON save path as normal gameplay. Scene-bound
    /// systems should reload the active scene after this returns successfully.
    /// </summary>
    public bool TryRestoreDebugState(PlayerData snapshotData, out string error)
    {
        error = string.Empty;
        if (snapshotData == null)
        {
            error = "The debug state contains no player data.";
            return false;
        }

        PlayerData previousData = CurrentData;

        try
        {
            // Clone the deserialized object so the snapshot container cannot
            // retain a mutable reference to the live save after restoration.
            string snapshotJson = JsonUtility.ToJson(snapshotData);
            PlayerData restoredData = JsonUtility.FromJson<PlayerData>(snapshotJson);
            if (restoredData == null)
            {
                error = "The debug player data could not be deserialized.";
                return false;
            }

            CurrentData = restoredData;
            NormalizeLoadedData();

            if (!TrySaveGame())
            {
                CurrentData = previousData;
                error = "The restored state could not be written to the normal save file.";
                return false;
            }

            return true;
        }
        catch (Exception exception)
        {
            CurrentData = previousData;
            error = exception.Message;
            Debug.LogError($"[PlayerDataManager] Failed to restore debug state: {exception.Message}", this);
            return false;
        }
    }

    /// <summary>
    /// Prevents the outgoing scene's PlayerSpawnManager.OnDestroy from replacing
    /// the position that was just restored from a debugger snapshot.
    /// </summary>
    public void SuppressAutomaticPositionSaveForSceneReload()
    {
        suppressAutomaticPositionSave = true;
    }

    public void ResumeAutomaticPositionSaving()
    {
        suppressAutomaticPositionSave = false;
    }

    private PlayerData TryReadSaveFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

        try
        {
            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) return null;
            return JsonUtility.FromJson<PlayerData>(json);
        }
        catch (Exception exception)
        {
            Debug.LogError($"[PlayerDataManager] Could not read '{path}': {exception.Message}", this);
            return null;
        }
    }

    // ────────────────────────────────────────────────
    // LOCATION SAVING
    // ────────────────────────────────────────────────
    
    public void SavePlayerPosition(string sceneName, Vector3 position)
    {
        if (suppressAutomaticPositionSave) return;

        if (CurrentData != null)
        {
            CurrentData.lastSavedScene = sceneName;
            CurrentData.lastSavedPosition = new SerializableVector3(position);
            SaveGame();
        }
    }

    [ContextMenu("Delete Save Data")] 
    public void DeleteSaveData()
    {
        if (File.Exists(saveFilePath)) File.Delete(saveFilePath);
        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();
        CurrentData = new PlayerData();
        CurrentData.playerName = "Guest";
        completionRecordsMissingBridge.Clear();
        SaveGame();
        OnCurrencyChanged?.Invoke();
        OnFeatureUnlocksChanged?.Invoke();
    }

    // ────────────────────────────────────────────────
    // STAT TRACKING & AUTO-CHECKING
    // ────────────────────────────────────────────────

    public void AddGold(int amount) 
    { 
        CurrentData.gold += amount; 
        CurrentData.lifetimeGoldEarned += amount; 
        SaveGame(); 
        OnCurrencyChanged?.Invoke();
        CheckAllAchievements(); 
    }
    
    public bool SpendGold(int amount) 
    { 
        if (CurrentData.gold >= amount) 
        { 
            CurrentData.gold -= amount; 
            CurrentData.lifetimeGoldSpent += amount; 
            SaveGame(); 
            OnCurrencyChanged?.Invoke();
            CheckAllAchievements(); 
            return true; 
        }
        return false;
    }
    
    public void AddExp(int amount) 
    { 
        CurrentData.exp += amount; 
        CurrentData.lifetimeExpEarned += Mathf.Max(0, amount);
        SaveGame(); 
        OnCurrencyChanged?.Invoke();
        CheckAllAchievements(); 
    }
    
    public void AddBridgeBuilt() 
    { 
        CurrentData.lifetimeBridgesBuilt++; 
        SaveGame(); 
        CheckAllAchievements(); 
    }

    /// <summary>
    /// Completes and optionally rewards a contract in one save operation.
    /// Bridge-build progress is deliberately recorded by LevelCompleteManager,
    /// because completing/turning in a contract is a different achievement type.
    /// Completion is rejected unless validated bridge geometry already exists.
    /// Returns true only when a new completion was persisted successfully.
    /// </summary>
    public bool CompleteContract(
        string contractName,
        int goldReward = 0,
        int expReward = 0)
    {
        if (CurrentData == null || string.IsNullOrWhiteSpace(contractName)) return false;
        string contractId = NormalizeContractIdentifier(contractName);

        // Older save files may not contain this list yet.
        if (CurrentData.completedContracts == null)
            CurrentData.completedContracts = new List<string>();

        if (!HasValidSavedBridge(contractId))
        {
            Debug.LogError(
                $"[PlayerDataManager] Refusing to complete '{contractId}' because no valid saved bridge geometry exists.",
                this);
            return false;
        }

        if (CurrentData.completedContracts.Exists(savedId =>
                ContractIdentifiersMatch(savedId, contractId))) return false;

        int previousGold = CurrentData.gold;
        int previousExp = CurrentData.exp;
        int previousLifetimeGold = CurrentData.lifetimeGoldEarned;
        int previousLifetimeExp = CurrentData.lifetimeExpEarned;
        int previousContractCount = CurrentData.lifetimeContractsCompleted;
        bool previousContractsTab = CurrentData.hasUnlockedContractsTab;
        bool previousContractsAlert = CurrentData.hasUnreadContractsAlert;
        bool previousObjectiveAlert = CurrentData.hasUnreadObjectiveAlert;
        List<string> newlyUnlockedFeatureIds = AddContractFeatureUnlocks(contractId);

        CurrentData.completedContracts.Add(contractId);
        CurrentData.lifetimeContractsCompleted++;
        CurrentData.gold += Mathf.Max(0, goldReward);
        CurrentData.exp += Mathf.Max(0, expReward);
        CurrentData.lifetimeGoldEarned += Mathf.Max(0, goldReward);
        CurrentData.lifetimeExpEarned += Mathf.Max(0, expReward);
        CurrentData.hasUnlockedContractsTab = true;
        CurrentData.hasUnreadContractsAlert = true;
        CurrentData.hasUnreadObjectiveAlert = true;

        if (!TrySaveGame())
        {
            CurrentData.completedContracts.Remove(contractId);
            CurrentData.gold = previousGold;
            CurrentData.exp = previousExp;
            CurrentData.lifetimeGoldEarned = previousLifetimeGold;
            CurrentData.lifetimeExpEarned = previousLifetimeExp;
            CurrentData.lifetimeContractsCompleted = previousContractCount;
            CurrentData.hasUnlockedContractsTab = previousContractsTab;
            CurrentData.hasUnreadContractsAlert = previousContractsAlert;
            CurrentData.hasUnreadObjectiveAlert = previousObjectiveAlert;
            foreach (string featureId in newlyUnlockedFeatureIds)
                CurrentData.unlockedFeatureIds.Remove(featureId);
            return false;
        }

        if (goldReward > 0 || expReward > 0) OnCurrencyChanged?.Invoke();
        OnAlmanacAlertsChanged?.Invoke();
        OnObjectiveAlertsChanged?.Invoke();
        if (newlyUnlockedFeatureIds.Count > 0)
        {
            OnFeatureUnlocksChanged?.Invoke();
            NotifyContractFeatureUnlocks(contractId, newlyUnlockedFeatureIds);
        }
        OnContractCompleted?.Invoke(contractId);
        ContractSO completedContract = FindRegisteredContract(contractId);
        AchievementPopupNotification.NotifyAlmanacEntry(
            completedContract != null ? completedContract.name : contractId,
            "Contract");
        CheckAllAchievements();
        return true;
    }

    public bool IsContractCompleted(string contractName)
    {
        if (CurrentData == null || CurrentData.completedContracts == null ||
            string.IsNullOrWhiteSpace(contractName)) return false;

        string contractId = NormalizeContractIdentifier(contractName);
        return CurrentData.completedContracts.Exists(savedId =>
                   ContractIdentifiersMatch(savedId, contractId)) &&
               HasValidSavedBridge(contractId);
    }

    /// <summary>
    /// Returns whether this player has ever successfully completed the contract.
    /// Unlike IsContractCompleted, this historical record remains independent
    /// from bridge replacement. Progression gates such as tutorial retirement
    /// must use it even while a redesign session is in progress.
    /// </summary>
    public bool HasContractCompletionRecord(string contractName)
    {
        if (CurrentData == null || CurrentData.completedContracts == null ||
            string.IsNullOrWhiteSpace(contractName)) return false;

        string contractId = NormalizeContractIdentifier(contractName);
        return CurrentData.completedContracts.Exists(savedId =>
            ContractIdentifiersMatch(savedId, contractId));
    }

    public bool HasAnyCompletedContract()
    {
        return CurrentData != null && CurrentData.completedContracts != null &&
               CurrentData.completedContracts.Exists(IsContractCompleted);
    }

    public void MarkObjectiveAlertUnread()
    {
        if (CurrentData == null) return;
        CurrentData.hasUnreadObjectiveAlert = true;
        SaveGame();
        OnObjectiveAlertsChanged?.Invoke();
    }

    public void ClearObjectiveAlert()
    {
        if (CurrentData == null || !CurrentData.hasUnreadObjectiveAlert) return;
        CurrentData.hasUnreadObjectiveAlert = false;
        SaveGame();
        OnObjectiveAlertsChanged?.Invoke();
    }

    // ────────────────────────────────────────────────
    // ACHIEVEMENT LOGIC
    // ────────────────────────────────────────────────

    /// <summary>
    /// Adds the achievement assets used by the active UI/scene to the database
    /// that evaluates progress. This prevents the display list and unlock list
    /// from silently drifting apart when achievements are replaced or renamed.
    /// </summary>
    public void RegisterAchievements(IEnumerable<AchievementSO> achievements)
    {
        if (achievements == null) return;
        if (allGameAchievements == null)
            allGameAchievements = new List<AchievementSO>();

        allGameAchievements.RemoveAll(item => item == null);

        foreach (AchievementSO achievement in achievements)
        {
            if (achievement == null || string.IsNullOrWhiteSpace(achievement.achievementID))
                continue;

            int existingIndex = allGameAchievements.FindIndex(item =>
                item != null && item.achievementID == achievement.achievementID);

            if (existingIndex >= 0)
                allGameAchievements[existingIndex] = achievement;
            else
                allGameAchievements.Add(achievement);
        }
    }

    /// <summary>
    /// Supplements the serialized contract database from scene systems such as
    /// the Almanac. Stable contract IDs are used for new saves; asset names remain
    /// accepted so existing saves can be migrated safely.
    /// </summary>
    public void RegisterContracts(IEnumerable<ContractSO> contracts)
    {
        if (contracts == null) return;
        if (allGameContracts == null)
            allGameContracts = new List<ContractSO>();

        allGameContracts.RemoveAll(item => item == null);
        foreach (ContractSO contract in contracts)
        {
            if (contract == null) continue;

            int existingIndex = allGameContracts.FindIndex(item =>
                item != null && string.Equals(
                    item.ContractID, contract.ContractID, StringComparison.Ordinal));

            if (existingIndex >= 0)
                allGameContracts[existingIndex] = contract;
            else
                allGameContracts.Add(contract);
        }

        if (CurrentData != null)
            MigrateLegacyContractIdentifiers();
    }

    /// <summary>Returns the contract asset represented by a stable or legacy saved identifier.</summary>
    public ContractSO GetRegisteredContract(string contractIdentifier)
    {
        return FindRegisteredContract(contractIdentifier);
    }

    private List<string> AddContractFeatureUnlocks(string contractName)
    {
        List<string> addedIds = new List<string>();
        if (CurrentData == null || string.IsNullOrWhiteSpace(contractName)) return addedIds;

        EnsureFeatureUnlockList();
        ContractSO contract = FindRegisteredContract(contractName);
        if (contract == null || contract.featureUnlockRewards == null) return addedIds;

        foreach (FeatureUnlockReward reward in contract.featureUnlockRewards)
        {
            string featureId = reward != null ? NormalizeFeatureId(reward.featureId) : string.Empty;
            if (string.IsNullOrEmpty(featureId) || IsFeatureUnlocked(featureId)) continue;

            CurrentData.unlockedFeatureIds.Add(featureId);
            addedIds.Add(featureId);
        }

        return addedIds;
    }

    private ContractSO FindRegisteredContract(string contractName)
    {
        if (allGameContracts == null || string.IsNullOrWhiteSpace(contractName)) return null;
        string normalizedName = contractName.Trim();
        return allGameContracts.Find(contract =>
            contract != null && contract.MatchesIdentifier(normalizedName));
    }

    private string NormalizeContractIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier)) return string.Empty;
        ContractSO contract = FindRegisteredContract(identifier);
        return contract != null ? contract.ContractID : identifier.Trim();
    }

    private bool ContractIdentifiersMatch(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        return string.Equals(
            NormalizeContractIdentifier(left),
            NormalizeContractIdentifier(right),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Converts save records and PlayerPrefs keys that used ContractSO asset names
    /// into stable contract IDs. This is intentionally idempotent and keeps all
    /// existing progress, bridge geometry, objectives, and material unlocks.
    /// </summary>
    private void MigrateLegacyContractIdentifiers()
    {
        if (CurrentData == null || allGameContracts == null) return;

        Dictionary<string, ContractSO> contractsById =
            new Dictionary<string, ContractSO>(StringComparer.Ordinal);
        foreach (ContractSO contract in allGameContracts)
        {
            if (contract == null || string.IsNullOrWhiteSpace(contract.contractID)) continue;
            if (contractsById.TryGetValue(contract.ContractID, out ContractSO duplicate))
            {
                if (duplicate == contract) continue;
                Debug.LogError(
                    $"[PlayerDataManager] Contract ID '{contract.ContractID}' is shared by " +
                    $"'{duplicate.name}' and '{contract.name}'. Legacy save migration was cancelled.",
                    this);
                return;
            }

            contractsById.Add(contract.ContractID, contract);
        }

        bool saveChanged = false;
        bool playerPrefsChanged = false;

        if (CurrentData.completedContracts != null)
        {
            HashSet<string> seenIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < CurrentData.completedContracts.Count; i++)
            {
                string previous = CurrentData.completedContracts[i];
                string migrated = NormalizeContractIdentifier(previous);
                if (!string.Equals(previous, migrated, StringComparison.Ordinal))
                {
                    CurrentData.completedContracts[i] = migrated;
                    saveChanged = true;
                }

                if (seenIds.Add(migrated)) continue;
                CurrentData.completedContracts.RemoveAt(i--);
                saveChanged = true;
            }
        }

        if (CurrentData.activeQuests != null)
        {
            foreach (TrackedTask task in CurrentData.activeQuests)
            {
                if (task == null || string.IsNullOrWhiteSpace(task.contractName)) continue;
                string migrated = NormalizeContractIdentifier(task.contractName);
                if (string.Equals(task.contractName, migrated, StringComparison.Ordinal)) continue;
                task.contractName = migrated;
                saveChanged = true;
            }
        }

        if (CurrentData.savedBridges != null)
        {
            foreach (SavedBridgeData bridge in CurrentData.savedBridges)
            {
                if (bridge == null || string.IsNullOrWhiteSpace(bridge.contractId)) continue;
                string migrated = NormalizeContractIdentifier(bridge.contractId);
                if (string.Equals(bridge.contractId, migrated, StringComparison.Ordinal)) continue;
                bridge.contractId = migrated;
                saveChanged = true;
            }
        }

        if (CurrentData.unlockedContractMaterials != null)
        {
            for (int i = 0; i < CurrentData.unlockedContractMaterials.Count; i++)
            {
                string key = CurrentData.unlockedContractMaterials[i];
                if (string.IsNullOrEmpty(key)) continue;

                foreach (ContractSO contract in contractsById.Values)
                {
                    string legacyPrefix = contract.name + "_";
                    if (!key.StartsWith(legacyPrefix, StringComparison.Ordinal)) continue;

                    CurrentData.unlockedContractMaterials[i] =
                        contract.ContractID + key.Substring(contract.name.Length);
                    saveChanged = true;
                    break;
                }
            }
        }

        foreach (ContractSO contract in contractsById.Values)
        {
            if (string.Equals(contract.name, contract.ContractID, StringComparison.Ordinal)) continue;

            string legacyKey = "LockedContract_" + contract.name;
            if (!PlayerPrefs.HasKey(legacyKey)) continue;

            string stableKey = "LockedContract_" + contract.ContractID;
            if (!PlayerPrefs.HasKey(stableKey))
                PlayerPrefs.SetInt(stableKey, PlayerPrefs.GetInt(legacyKey));
            PlayerPrefs.DeleteKey(legacyKey);
            playerPrefsChanged = true;
        }

        if (playerPrefsChanged) PlayerPrefs.Save();
        RebuildMissingBridgeCompletionRecords(!hasMigratedContractIdentifiers);
        hasMigratedContractIdentifiers = true;
        if (saveChanged) SaveGame();
    }

    private void NotifyContractFeatureUnlocks(string contractName, List<string> featureIds)
    {
        if (featureIds == null || featureIds.Count == 0) return;

        ContractSO contract = FindRegisteredContract(contractName);
        foreach (string featureId in featureIds)
        {
            FeatureUnlockReward matchingReward = contract != null && contract.featureUnlockRewards != null
                ? contract.featureUnlockRewards.Find(reward =>
                    reward != null && string.Equals(
                        NormalizeFeatureId(reward.featureId),
                        NormalizeFeatureId(featureId),
                        StringComparison.Ordinal))
                : null;

            string displayName = matchingReward != null &&
                                 !string.IsNullOrWhiteSpace(matchingReward.displayName)
                ? matchingReward.displayName.Trim()
                : GetFeatureDisplayName(featureId);

            AchievementPopupNotification.NotifyFeatureUnlock(
                displayName,
                matchingReward != null ? matchingReward.icon : null);
        }
    }

    private void MigrateCompletedContractFeatureUnlocks()
    {
        if (CurrentData == null || CurrentData.completedContracts == null) return;

        EnsureFeatureUnlockList();
        bool changed = false;
        foreach (string completedContract in CurrentData.completedContracts)
        {
            if (!IsContractCompleted(completedContract)) continue;
            changed |= AddContractFeatureUnlocks(completedContract).Count > 0;
        }

        if (changed) SaveGame();
    }

    private void MigrateEarnedAchievementFeatureUnlock()
    {
        if (CurrentData == null || CurrentData.unlockedAchievements == null ||
            CurrentData.unlockedAchievements.Count == 0 ||
            IsFeatureUnlocked(AchievementsFeatureId))
        {
            return;
        }

        // Preserve access for older saves without showing an out-of-context
        // feature notification as soon as the save loads.
        EnsureFeatureUnlockList();
        CurrentData.unlockedFeatureIds.Add(AchievementsFeatureId);
        SaveGame();
    }

    private void EnsureFeatureUnlockList()
    {
        if (CurrentData != null && CurrentData.unlockedFeatureIds == null)
            CurrentData.unlockedFeatureIds = new List<string>();
    }

    private static string NormalizeFeatureId(string featureId)
    {
        return string.IsNullOrWhiteSpace(featureId)
            ? string.Empty
            : featureId.Trim().ToLowerInvariant();
    }

    private static string GetFeatureDisplayName(string featureId)
    {
        string normalized = NormalizeFeatureId(featureId);
        if (string.IsNullOrEmpty(normalized)) return "New Feature";

        string displayName = normalized.Replace('_', ' ').Replace('-', ' ');
        return char.ToUpperInvariant(displayName[0]) + displayName.Substring(1);
    }

    public int GetCompletedContractCountForMap(
        ContractSO.ContractMap map,
        bool excludeTutorialContracts = false)
    {
        HashSet<string> completedContracts = new HashSet<string>(StringComparer.Ordinal);
        if (CurrentData == null || CurrentData.completedContracts == null)
            return 0;

        foreach (string contractName in CurrentData.completedContracts)
        {
            if (!string.IsNullOrWhiteSpace(contractName))
                completedContracts.Add(NormalizeContractIdentifier(contractName));
        }

        int completed = 0;
        HashSet<string> countedContracts = new HashSet<string>(StringComparer.Ordinal);
        if (allGameContracts == null) return 0;

        foreach (ContractSO contract in allGameContracts)
        {
            if (contract == null || !contract.countsTowardMapAchievements ||
                (excludeTutorialContracts && contract.isTutorialContract) ||
                contract.contractMap != map || !countedContracts.Add(contract.ContractID))
                continue;

            if (completedContracts.Contains(contract.ContractID)) completed++;
        }

        return completed;
    }

    public int GetTotalContractCountForMap(
        ContractSO.ContractMap map,
        bool excludeTutorialContracts = false)
    {
        if (allGameContracts == null) return 0;

        HashSet<string> contracts = new HashSet<string>(StringComparer.Ordinal);
        foreach (ContractSO contract in allGameContracts)
        {
            if (contract != null && contract.countsTowardMapAchievements &&
                (!excludeTutorialContracts || !contract.isTutorialContract) &&
                contract.contractMap == map)
                contracts.Add(contract.ContractID);
        }

        return contracts.Count;
    }

    public int GetCompletedMapCount(bool excludeTutorialContracts = false)
    {
        int completedMaps = 0;
        foreach (ContractSO.ContractMap map in Enum.GetValues(typeof(ContractSO.ContractMap)))
        {
            int total = GetTotalContractCountForMap(map, excludeTutorialContracts);
            if (total > 0 &&
                GetCompletedContractCountForMap(map, excludeTutorialContracts) >= total)
                completedMaps++;
        }
        return completedMaps;
    }

    public int GetCompletedStoryModeContractCount(bool excludeTutorialContracts = true)
    {
        if (CurrentData == null || CurrentData.completedContracts == null ||
            allGameContracts == null) return 0;

        HashSet<string> completedContracts = new HashSet<string>(StringComparer.Ordinal);
        foreach (string contractId in CurrentData.completedContracts)
            completedContracts.Add(NormalizeContractIdentifier(contractId));
        HashSet<string> countedContracts = new HashSet<string>(StringComparer.Ordinal);
        int completed = 0;

        foreach (ContractSO contract in allGameContracts)
        {
            if (contract == null || !contract.isStoryModeContract ||
                (excludeTutorialContracts && contract.isTutorialContract) ||
                !countedContracts.Add(contract.ContractID))
                continue;

            if (completedContracts.Contains(contract.ContractID)) completed++;
        }

        return completed;
    }

    public int GetCompletedRegisteredContractCount(bool excludeTutorialContracts)
    {
        if (!excludeTutorialContracts)
            return CurrentData != null ? CurrentData.lifetimeContractsCompleted : 0;
        if (CurrentData == null || CurrentData.completedContracts == null ||
            allGameContracts == null) return 0;

        HashSet<string> completedContracts = new HashSet<string>(StringComparer.Ordinal);
        foreach (string contractId in CurrentData.completedContracts)
            completedContracts.Add(NormalizeContractIdentifier(contractId));
        HashSet<string> countedContracts = new HashSet<string>(StringComparer.Ordinal);
        int completed = 0;

        foreach (ContractSO contract in allGameContracts)
        {
            if (contract == null || contract.isTutorialContract ||
                !countedContracts.Add(contract.ContractID))
                continue;

            if (completedContracts.Contains(contract.ContractID)) completed++;
        }

        return completed;
    }

    public int GetAchievementProgress(AchievementSO achievement)
    {
        if (achievement == null || CurrentData == null) return 0;

        switch (achievement.goalType)
        {
            case AchievementSO.GoalType.TotalBridgesBuilt:
                return CurrentData.lifetimeBridgesBuilt;
            case AchievementSO.GoalType.TotalGoldEarned:
                return CurrentData.lifetimeGoldEarned;
            case AchievementSO.GoalType.TotalGoldSpent:
                return CurrentData.lifetimeGoldSpent;
            case AchievementSO.GoalType.TotalExpEarned:
                return CurrentData.lifetimeExpEarned;
            case AchievementSO.GoalType.ContractsCompleted:
                return GetCompletedRegisteredContractCount(achievement.excludeTutorialContracts);
            case AchievementSO.GoalType.AllContractsInMapCompleted:
                return GetCompletedContractCountForMap(
                    achievement.targetContractMap,
                    achievement.excludeTutorialContracts);
            case AchievementSO.GoalType.AllContractsAcrossAllMapsCompleted:
                return GetCompletedMapCount(achievement.excludeTutorialContracts);
            case AchievementSO.GoalType.StoryModeContractsCompleted:
                return GetCompletedStoryModeContractCount(achievement.excludeTutorialContracts);
            case AchievementSO.GoalType.BuildLocationCompleted:
                return CurrentData.unlockedAchievements.Contains(achievement.achievementID) ? 1 : 0;
            default:
                return 0;
        }
    }

    public int GetAchievementTarget(AchievementSO achievement)
    {
        if (achievement == null) return 0;

        switch (achievement.goalType)
        {
            case AchievementSO.GoalType.AllContractsInMapCompleted:
                return GetTotalContractCountForMap(
                    achievement.targetContractMap,
                    achievement.excludeTutorialContracts);
            case AchievementSO.GoalType.AllContractsAcrossAllMapsCompleted:
                return Enum.GetValues(typeof(ContractSO.ContractMap)).Length;
            case AchievementSO.GoalType.BuildLocationCompleted:
                return 1;
            default:
                return Mathf.Max(0, achievement.targetAmount);
        }
    }

    public void CheckAllAchievements()
    {
        // 1. Prevent checking if the list is empty
        if (allGameAchievements == null || allGameAchievements.Count == 0) return;
        
        // 2. Prevent an infinite loop if unlocking an achievement triggers another AddGold!
        if (isCheckingAchievements) return; 
        
        isCheckingAchievements = true;

        foreach (AchievementSO ach in allGameAchievements)
        {
            CheckAchievement(ach);
        }

        isCheckingAchievements = false;
    }

    private void CheckAchievement(AchievementSO achievement)
    {
        if (achievement == null) return;
        
        // Don't unlock it twice!
        if (CurrentData.unlockedAchievements.Contains(achievement.achievementID)) return;

        bool isUnlocked = false;
        int achievementTarget = GetAchievementTarget(achievement);
        if (achievementTarget > 0)
            isUnlocked = GetAchievementProgress(achievement) >= achievementTarget;

        if (isUnlocked) UnlockAchievement(achievement);
    }

    public bool TryUnlockBuildLocationAchievement(
        AchievementSO achievement,
        ContractSO completedContract)
    {
        if (achievement == null ||
            achievement.goalType != AchievementSO.GoalType.BuildLocationCompleted)
            return false;

        if (achievement.excludeTutorialContracts && completedContract != null &&
            completedContract.isTutorialContract)
            return false;

        return UnlockAchievement(achievement);
    }

    /// <summary>Developer tooling only: force the normal unlock/reward/popup pipeline.</summary>
    public bool DebugUnlockAchievement(AchievementSO achievement)
    {
        return UnlockAchievement(achievement);
    }

    private bool UnlockAchievement(AchievementSO achievement)
    {
        if (achievement == null || CurrentData == null ||
            string.IsNullOrWhiteSpace(achievement.achievementID)) return false;
        if (CurrentData.unlockedAchievements.Contains(achievement.achievementID)) return false;

        CurrentData.unlockedAchievements.Add(achievement.achievementID);

        // These methods also update the received-currency lifetime counters.
        if (achievement.bonusGold > 0) AddGold(achievement.bonusGold);
        if (achievement.bonusExp > 0) AddExp(achievement.bonusExp);

        bool hasCosmeticReward = achievement.grantsCosmeticReward &&
                                 !string.IsNullOrWhiteSpace(achievement.rewardCosmeticID);
        if (hasCosmeticReward)
        {
            if (CurrentData.unlockedCosmeticIDs == null)
                CurrentData.unlockedCosmeticIDs = new List<string>();
            if (!CurrentData.unlockedCosmeticIDs.Contains(achievement.rewardCosmeticID))
                CurrentData.unlockedCosmeticIDs.Add(achievement.rewardCosmeticID);
        }

        SaveGame();
        OnAchievementUnlocked?.Invoke(achievement);
        AchievementPopupNotification.NotifyAchievement(achievement);

        // The first achievement introduces and permanently unlocks the archive.
        // This queues the feature notification after the achievement notification.
        if (!IsFeatureUnlocked(AchievementsFeatureId))
            UnlockFeature(AchievementsFeatureId);

        if (hasCosmeticReward)
        {
            if (ItemUnlockUI.Instance != null)
            {
                ItemUnlockUI.Instance.ShowReward(
                    achievement.rewardDisplayName,
                    achievement.rewardIcon,
                    achievement.rewardCosmeticID,
                    null);
            }
            else
            {
                UnlockCosmeticReward(achievement.rewardCosmeticID, true);
            }
        }

        Debug.Log($"<color=green>ACHIEVEMENT UNLOCKED: {achievement.achievementName}!</color>");
        return true;
    }

    public void UnlockCosmeticReward(string cosmeticID, bool equipImmediately)
    {
        if (CurrentData == null || string.IsNullOrWhiteSpace(cosmeticID)) return;

        string normalizedID = cosmeticID.Trim();
        if (CurrentData.unlockedCosmeticIDs == null)
            CurrentData.unlockedCosmeticIDs = new List<string>();
        if (!CurrentData.unlockedCosmeticIDs.Contains(normalizedID))
            CurrentData.unlockedCosmeticIDs.Add(normalizedID);
        if (equipImmediately)
            CurrentData.equippedHatID = normalizedID;

        SaveGame();
        if (equipImmediately && PlayerCosmetics.Instance != null)
            PlayerCosmetics.Instance.RefreshCosmetics();
    }

    public bool IsCosmeticUnlocked(string cosmeticID)
    {
        if (CurrentData == null || string.IsNullOrWhiteSpace(cosmeticID) ||
            CurrentData.unlockedCosmeticIDs == null)
            return false;

        string normalizedID = cosmeticID.Trim();
        return CurrentData.unlockedCosmeticIDs.Exists(savedID =>
            string.Equals(savedID?.Trim(), normalizedID, StringComparison.Ordinal));
    }

    // ────────────────────────────────────────────────
    // OTHER PROGRESSION LOGIC
    // ────────────────────────────────────────────────

    public void UnlockLevel(string levelName) { if (!CurrentData.unlockedLevels.Contains(levelName)) { CurrentData.unlockedLevels.Add(levelName); SaveGame(); } }
    public void CompleteLesson(string lessonName)
    {
        if (CurrentData.completedLessons.Contains(lessonName)) return;

        CurrentData.completedLessons.Add(lessonName);
        SaveGame();
    }

    public bool UnlockLesson(string lessonId)
    {
        if (CurrentData == null || string.IsNullOrWhiteSpace(lessonId)) return false;

        string normalizedId = lessonId.Trim();
        if (CurrentData.unlockedLessonIds == null)
            CurrentData.unlockedLessonIds = new List<string>();

        if (CurrentData.unlockedLessonIds.Contains(normalizedId)) return false;

        bool previousLessonsTab = CurrentData.hasUnlockedLessonsTab;
        bool previousLessonsAlert = CurrentData.hasUnreadLessonsAlert;
        CurrentData.unlockedLessonIds.Add(normalizedId);
        CurrentData.hasUnlockedLessonsTab = true;
        CurrentData.hasUnreadLessonsAlert = true;
        if (!TrySaveGame())
        {
            CurrentData.unlockedLessonIds.Remove(normalizedId);
            CurrentData.hasUnlockedLessonsTab = previousLessonsTab;
            CurrentData.hasUnreadLessonsAlert = previousLessonsAlert;
            return false;
        }
        OnLessonUnlocked?.Invoke(normalizedId);
        OnAlmanacAlertsChanged?.Invoke();
        return true;
    }

    public bool IsLessonUnlocked(string lessonId)
    {
        return CurrentData != null && CurrentData.unlockedLessonIds != null &&
               !string.IsNullOrWhiteSpace(lessonId) &&
               CurrentData.unlockedLessonIds.Contains(lessonId.Trim());
    }

    public void MarkLessonsAlmanacRead()
    {
        if (CurrentData == null || !CurrentData.hasUnreadLessonsAlert) return;
        CurrentData.hasUnreadLessonsAlert = false;
        SaveGame();
        OnAlmanacAlertsChanged?.Invoke();
    }

    public bool DiscoverMaterial(string materialId)
    {
        if (CurrentData == null || string.IsNullOrWhiteSpace(materialId)) return false;

        string normalizedId = materialId.Trim();
        if (CurrentData.discoveredMaterialIds == null)
            CurrentData.discoveredMaterialIds = new List<string>();

        if (CurrentData.discoveredMaterialIds.Contains(normalizedId)) return false;

        bool previousMaterialsTab = CurrentData.hasUnlockedMaterialsTab;
        bool previousMaterialsAlert = CurrentData.hasUnreadMaterialsAlert;
        CurrentData.discoveredMaterialIds.Add(normalizedId);
        CurrentData.hasUnlockedMaterialsTab = true;
        CurrentData.hasUnreadMaterialsAlert = true;
        if (!TrySaveGame())
        {
            CurrentData.discoveredMaterialIds.Remove(normalizedId);
            CurrentData.hasUnlockedMaterialsTab = previousMaterialsTab;
            CurrentData.hasUnreadMaterialsAlert = previousMaterialsAlert;
            return false;
        }
        OnMaterialDiscovered?.Invoke(normalizedId);
        OnAlmanacAlertsChanged?.Invoke();
        return true;
    }

    public bool IsMaterialDiscovered(string materialId)
    {
        return CurrentData != null && CurrentData.discoveredMaterialIds != null &&
               !string.IsNullOrWhiteSpace(materialId) &&
               CurrentData.discoveredMaterialIds.Contains(materialId.Trim());
    }

    public void MarkMaterialsAlmanacRead()
    {
        if (CurrentData == null || !CurrentData.hasUnreadMaterialsAlert) return;
        CurrentData.hasUnreadMaterialsAlert = false;
        SaveGame();
        OnAlmanacAlertsChanged?.Invoke();
    }

    public void ResetLessonProgress(string lessonName, bool saveImmediately = true)
    {
        if (CurrentData == null || CurrentData.completedLessons == null ||
            string.IsNullOrWhiteSpace(lessonName)) return;

        if (!CurrentData.completedLessons.Remove(lessonName)) return;
        if (saveImmediately) SaveGame();
    }
    public void UnlockMaterialForContract(string contractName, string materialName)
    {
        string key = NormalizeContractIdentifier(contractName) + "_" + materialName;
        if (!CurrentData.unlockedContractMaterials.Contains(key))
        {
            CurrentData.unlockedContractMaterials.Add(key);
            SaveGame();
        }
    }

    public bool IsMaterialUnlockedForContract(string contractName, string materialName)
    {
        string contractId = NormalizeContractIdentifier(contractName);
        string key = contractId + "_" + materialName;
        if (CurrentData.unlockedContractMaterials.Contains(key)) return true;

        ContractSO contract = FindRegisteredContract(contractId);
        string legacyKey = contract != null ? contract.name + "_" + materialName : string.Empty;
        return !string.IsNullOrEmpty(legacyKey) &&
               CurrentData.unlockedContractMaterials.Contains(legacyKey);
    }
    public void UnlockAlmanac()
    {
        if (CurrentData.hasAlmanac) return;

        CurrentData.hasAlmanac = true;
        CurrentData.hasUnreadAlmanacUnlockAlert = true;
        CurrentData.hasUnreadContractsAlert |= HasAnyCompletedContract();
        SaveGame();
        OnAlmanacUnlocked?.Invoke();
        OnAlmanacAlertsChanged?.Invoke();
    }

    public void MarkContractsAlmanacUnread()
    {
        if (CurrentData.hasUnreadContractsAlert) return;
        CurrentData.hasUnreadContractsAlert = true;
        SaveGame();
        OnAlmanacAlertsChanged?.Invoke();
    }

    public void MarkAlmanacOpened()
    {
        if (!CurrentData.hasUnreadAlmanacUnlockAlert) return;
        CurrentData.hasUnreadAlmanacUnlockAlert = false;
        SaveGame();
        OnAlmanacAlertsChanged?.Invoke();
    }

    public void MarkContractsAlmanacRead()
    {
        if (!CurrentData.hasUnreadContractsAlert) return;
        CurrentData.hasUnreadContractsAlert = false;
        SaveGame();
        OnAlmanacAlertsChanged?.Invoke();
    }

    public void UnlockDoor(string doorID) { if (!CurrentData.unlockedDoors.Contains(doorID)) { CurrentData.unlockedDoors.Add(doorID); SaveGame(); } }
    public bool IsDoorUnlocked(string doorID) { return CurrentData.unlockedDoors.Contains(doorID); }

    private void NormalizeLoadedData()
    {
        if (CurrentData == null) CurrentData = new PlayerData();
        if (CurrentData.unlockedLessonIds == null) CurrentData.unlockedLessonIds = new List<string>();
        if (CurrentData.discoveredMaterialIds == null) CurrentData.discoveredMaterialIds = new List<string>();
        if (CurrentData.completedLessons == null) CurrentData.completedLessons = new List<string>();
        if (CurrentData.completedContracts == null) CurrentData.completedContracts = new List<string>();
        if (CurrentData.unlockedAchievements == null) CurrentData.unlockedAchievements = new List<string>();
        if (CurrentData.unlockedCosmeticIDs == null) CurrentData.unlockedCosmeticIDs = new List<string>();
        if (CurrentData.purchasedShopItemIds == null) CurrentData.purchasedShopItemIds = new List<string>();
        if (CurrentData.unlockedFeatureIds == null) CurrentData.unlockedFeatureIds = new List<string>();
        if (CurrentData.activeQuests == null) CurrentData.activeQuests = new List<TrackedTask>();
        if (CurrentData.unlockedLevels == null) CurrentData.unlockedLevels = new List<string>();
        if (CurrentData.unlockedContractMaterials == null) CurrentData.unlockedContractMaterials = new List<string>();
        if (CurrentData.unlockedDoors == null) CurrentData.unlockedDoors = new List<string>();
        if (CurrentData.savedBridges == null) CurrentData.savedBridges = new List<SavedBridgeData>();
        if (CurrentData.npcProgressions == null) CurrentData.npcProgressions = new List<NPCProgressionSaveData>();

        // Older saves only stored current EXP. Until EXP becomes spendable this
        // safely migrates it into the new lifetime "EXP Received" counter.
        CurrentData.lifetimeExpEarned = Mathf.Max(CurrentData.lifetimeExpEarned, CurrentData.exp);

        RebuildMissingBridgeCompletionRecords(false);

        if (CurrentData.unlockedLessonIds.Count > 0)
            CurrentData.hasUnlockedLessonsTab = true;
        if (CurrentData.discoveredMaterialIds.Count > 0)
            CurrentData.hasUnlockedMaterialsTab = true;
    }

    private void RebuildMissingBridgeCompletionRecords(bool logWarning = true)
    {
        completionRecordsMissingBridge.Clear();
        if (CurrentData == null || CurrentData.completedContracts == null ||
            CurrentData.savedBridges == null) return;

        foreach (string contractId in CurrentData.completedContracts)
        {
            string normalizedId = NormalizeContractIdentifier(contractId);
            SavedBridgeData rawBridge = CurrentData.savedBridges.Find(bridge =>
                bridge != null && ContractIdentifiersMatch(bridge.contractId, normalizedId));
            if (!IsBridgeDataValid(rawBridge, out _))
                completionRecordsMissingBridge.Add(normalizedId);
        }

        if (logWarning && completionRecordsMissingBridge.Count > 0)
        {
            Debug.LogWarning(
                $"[PlayerDataManager] Found {completionRecordsMissingBridge.Count} completed contract record(s) " +
                "without valid bridge geometry. They will remain incomplete until their bridges are rebuilt and saved.",
                this);
        }
    }

    public bool SaveBridgeData(string contractId, List<Point> points, List<Bar> bars, float totalSpent, float maxStress)
    {
        if (CurrentData == null || string.IsNullOrWhiteSpace(contractId) ||
            points == null || bars == null) return false;

        contractId = NormalizeContractIdentifier(contractId);

        SavedBridgeData newSave = new SavedBridgeData { 
            schemaVersion = 1,
            contractId = contractId.Trim(),
            totalSpent = totalSpent,
            maxStress = maxStress
        };

        List<Point> validPoints = new List<Point>();
        HashSet<Point> uniquePoints = new HashSet<Point>();
        foreach (Point point in points)
        {
            if (point != null && uniquePoints.Add(point)) validPoints.Add(point);
        }

        Dictionary<Point, int> pointToIndex = new Dictionary<Point, int>();
        for (int i = 0; i < validPoints.Count; i++)
        {
            Point point = validPoints[i];
            pointToIndex[point] = i;
            newSave.points.Add(new SavedPointData {
                index = i,
                position = new SerializableVector3(point.transform.position),
                isAnchor = point.isAnchor,
                originalIsAnchor = point.originalIsAnchor
            });
        }

        HashSet<Bar> uniqueBars = new HashSet<Bar>();
        foreach (Bar bar in bars)
        {
            if (bar == null || !uniqueBars.Add(bar)) continue;
            if (bar.startPoint == null || bar.endPoint == null || bar.materialData == null ||
                !pointToIndex.TryGetValue(bar.startPoint, out int startIndex) ||
                !pointToIndex.TryGetValue(bar.endPoint, out int endIndex))
            {
                Debug.LogError(
                    $"[PlayerDataManager] Bridge '{contractId}' contains a bar with missing endpoints, material, or point ownership.",
                    this);
                return false;
            }

            newSave.bars.Add(new SavedBarData {
                startPointIndex = startIndex,
                endPointIndex = endIndex,
                materialName = bar.materialData.name
            });
        }

        if (!IsBridgeDataValid(newSave, out string validationError))
        {
            Debug.LogError($"[PlayerDataManager] Bridge '{contractId}' was not saved: {validationError}", this);
            return false;
        }

        List<SavedBridgeData> previousRecords = CurrentData.savedBridges.FindAll(entry =>
            entry != null && ContractIdentifiersMatch(entry.contractId, newSave.contractId));
        CurrentData.savedBridges.RemoveAll(entry =>
            entry != null && ContractIdentifiersMatch(entry.contractId, newSave.contractId));
        CurrentData.savedBridges.Add(newSave);

        if (TrySaveGame())
        {
            // Repair legacy saves created by the old completion-before-geometry
            // order. Rewards are not granted twice, but progression may resume.
            if (completionRecordsMissingBridge.Remove(newSave.contractId))
            {
                Debug.LogWarning(
                    $"[PlayerDataManager] Repaired missing bridge geometry for completed contract '{newSave.contractId}'.",
                    this);
                OnContractCompleted?.Invoke(newSave.contractId);
            }
            return true;
        }

        // Keep memory consistent with disk when persistence fails.
        CurrentData.savedBridges.Remove(newSave);
        CurrentData.savedBridges.AddRange(previousRecords);
        return false;
    }

    public bool OwnsShopItem(string itemId)
    {
        return CurrentData != null &&
               CurrentData.purchasedShopItemIds != null &&
               !string.IsNullOrWhiteSpace(itemId) &&
               CurrentData.purchasedShopItemIds.Contains(itemId);
    }

    /// <summary>
    /// Performs a shop purchase as one save transaction. Failed saves roll the
    /// currency and ownership changes back so player data cannot desynchronize.
    /// </summary>
    public bool TryPurchaseShopItem(string itemId, int price, bool allowRepeatPurchase)
    {
        if (CurrentData == null || string.IsNullOrWhiteSpace(itemId) || price < 0)
            return false;

        if (CurrentData.purchasedShopItemIds == null)
            CurrentData.purchasedShopItemIds = new List<string>();

        bool alreadyOwned = CurrentData.purchasedShopItemIds.Contains(itemId);
        if (alreadyOwned && !allowRepeatPurchase)
            return false;
        if (CurrentData.gold < price)
            return false;

        int previousGold = CurrentData.gold;
        int previousLifetimeSpent = CurrentData.lifetimeGoldSpent;
        bool addedOwnership = false;

        CurrentData.gold -= price;
        CurrentData.lifetimeGoldSpent += price;
        if (!alreadyOwned)
        {
            CurrentData.purchasedShopItemIds.Add(itemId);
            addedOwnership = true;
        }

        if (!TrySaveGame())
        {
            CurrentData.gold = previousGold;
            CurrentData.lifetimeGoldSpent = previousLifetimeSpent;
            if (addedOwnership) CurrentData.purchasedShopItemIds.Remove(itemId);
            return false;
        }

        OnCurrencyChanged?.Invoke();
        OnShopItemPurchased?.Invoke(itemId);
        CheckAllAchievements();
        return true;
    }

    public SavedBridgeData GetSavedBridge(string contractId)
    {
        if (CurrentData == null || CurrentData.savedBridges == null ||
            string.IsNullOrWhiteSpace(contractId)) return null;

        SavedBridgeData bridge = CurrentData.savedBridges.Find(entry =>
            entry != null && ContractIdentifiersMatch(entry.contractId, contractId));
        return IsBridgeDataValid(bridge, out _) ? bridge : null;
    }

    public bool HasValidSavedBridge(string contractId)
    {
        return GetSavedBridge(contractId) != null;
    }

    public bool IsBridgeDataValid(SavedBridgeData bridge, out string error)
    {
        error = string.Empty;
        if (bridge == null) { error = "The bridge record is null."; return false; }
        if (string.IsNullOrWhiteSpace(bridge.contractId)) { error = "The contract ID is missing."; return false; }
        if (!IsFinite(bridge.totalSpent) || !IsFinite(bridge.maxStress))
        {
            error = "The saved cost or stress value is invalid.";
            return false;
        }
        if (bridge.points == null || bridge.points.Count < 2) { error = "At least two nodes are required."; return false; }
        if (bridge.bars == null || bridge.bars.Count == 0) { error = "At least one bar is required."; return false; }

        HashSet<int> pointIndices = new HashSet<int>();
        foreach (SavedPointData point in bridge.points)
        {
            if (point == null || point.position == null)
            {
                error = "A node record or position is missing.";
                return false;
            }

            if (!pointIndices.Add(point.index))
            {
                error = $"Node index {point.index} is duplicated.";
                return false;
            }

            if (!IsFinite(point.position.x) || !IsFinite(point.position.y) || !IsFinite(point.position.z))
            {
                error = $"Node index {point.index} has an invalid position.";
                return false;
            }
        }

        foreach (SavedBarData bar in bridge.bars)
        {
            if (bar == null || string.IsNullOrWhiteSpace(bar.materialName))
            {
                error = "A bar record or material ID is missing.";
                return false;
            }

            if (bar.startPointIndex == bar.endPointIndex ||
                !pointIndices.Contains(bar.startPointIndex) ||
                !pointIndices.Contains(bar.endPointIndex))
            {
                error = "A bar references invalid or identical endpoint indices.";
                return false;
            }
        }

        return true;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public void DeleteSavedBridge(string contractId)
    {
        if (CurrentData == null || CurrentData.savedBridges == null) return;
        CurrentData.savedBridges.RemoveAll(b =>
            b != null && ContractIdentifiersMatch(b.contractId, contractId));
        SaveGame();
    }

    public bool TryGetNPCProgression(string progressionId, out NPCProgressionSaveData state)
    {
        state = null;
        if (CurrentData == null || CurrentData.npcProgressions == null ||
            string.IsNullOrWhiteSpace(progressionId)) return false;

        state = CurrentData.npcProgressions.Find(entry =>
            entry != null && string.Equals(entry.progressionId, progressionId.Trim(),
                StringComparison.Ordinal));
        return state != null;
    }

    /// <summary>
    /// Persists the destination phase before travel and the settled phase after arrival.
    /// Keeping both index and ID makes saves resilient to phase-list reordering.
    /// </summary>
    public void SaveNPCProgression(
        string progressionId,
        int currentPhaseIndex,
        string currentPhaseId,
        bool wasTravelling)
    {
        if (CurrentData == null || string.IsNullOrWhiteSpace(progressionId)) return;
        if (CurrentData.npcProgressions == null)
            CurrentData.npcProgressions = new List<NPCProgressionSaveData>();

        string normalizedId = progressionId.Trim();
        NPCProgressionSaveData state = CurrentData.npcProgressions.Find(entry =>
            entry != null && string.Equals(entry.progressionId, normalizedId,
                StringComparison.Ordinal));

        if (state == null)
        {
            state = new NPCProgressionSaveData { progressionId = normalizedId };
            CurrentData.npcProgressions.Add(state);
        }

        state.currentPhaseIndex = Mathf.Max(0, currentPhaseIndex);
        state.currentPhaseId = currentPhaseId ?? string.Empty;
        state.wasTravelling = wasTravelling;
        SaveGame();
    }
}
