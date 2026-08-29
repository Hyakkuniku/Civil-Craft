using UnityEngine;
using System.IO;
using System;
using System.Collections.Generic;

[DisallowMultipleComponent]
public class PlayerDataManager : MonoBehaviour
{
    public static PlayerDataManager Instance { get; private set; }
    public PlayerData CurrentData { get; private set; }
    
    [Header("Achievements Database")]
    [Tooltip("Drag ALL your AchievementSO files here so the game can automatically check them!")]
    public List<AchievementSO> allGameAchievements = new List<AchievementSO>();

    public Action OnAlmanacUnlocked;
    public Action OnAlmanacAlertsChanged;
    public Action OnObjectiveAlertsChanged;
    public Action<AchievementSO> OnAchievementUnlocked; 
    /// <summary>Raised once when a contract is newly added to persistent completion data.</summary>
    public Action<string> OnContractCompleted;
    /// <summary>Raised once when a LessonData ID is newly added to the archive.</summary>
    public Action<string> OnLessonUnlocked;
    
    // Optional: Useful if you have a top-right Gold UI that needs to refresh immediately!
    public Action OnCurrencyChanged; 
    
    private string saveFilePath;
    private bool isCheckingAchievements = false; // Prevents infinite loops!
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
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void SaveGame()
    {
        TrySaveGame();
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
    /// Completes and optionally rewards a bridge contract in one save operation.
    /// Completion is rejected unless validated bridge geometry already exists.
    /// Returns true only when a new completion was persisted successfully.
    /// </summary>
    public bool CompleteContract(
        string contractName,
        int goldReward = 0,
        int expReward = 0,
        bool countBridgeBuilt = false)
    {
        if (CurrentData == null || string.IsNullOrWhiteSpace(contractName)) return false;

        // Older save files may not contain this list yet.
        if (CurrentData.completedContracts == null)
            CurrentData.completedContracts = new List<string>();

        if (!HasValidSavedBridge(contractName))
        {
            Debug.LogError(
                $"[PlayerDataManager] Refusing to complete '{contractName}' because no valid saved bridge geometry exists.",
                this);
            return false;
        }

        if (CurrentData.completedContracts.Contains(contractName)) return false;

        int previousGold = CurrentData.gold;
        int previousExp = CurrentData.exp;
        int previousLifetimeGold = CurrentData.lifetimeGoldEarned;
        int previousBridgeCount = CurrentData.lifetimeBridgesBuilt;
        int previousContractCount = CurrentData.lifetimeContractsCompleted;
        bool previousContractsTab = CurrentData.hasUnlockedContractsTab;
        bool previousContractsAlert = CurrentData.hasUnreadContractsAlert;
        bool previousObjectiveAlert = CurrentData.hasUnreadObjectiveAlert;

        CurrentData.completedContracts.Add(contractName);
        CurrentData.lifetimeContractsCompleted++;
        CurrentData.gold += Mathf.Max(0, goldReward);
        CurrentData.exp += Mathf.Max(0, expReward);
        CurrentData.lifetimeGoldEarned += Mathf.Max(0, goldReward);
        if (countBridgeBuilt) CurrentData.lifetimeBridgesBuilt++;
        CurrentData.hasUnlockedContractsTab = true;
        CurrentData.hasUnreadContractsAlert = true;
        CurrentData.hasUnreadObjectiveAlert = true;

        if (!TrySaveGame())
        {
            CurrentData.completedContracts.Remove(contractName);
            CurrentData.gold = previousGold;
            CurrentData.exp = previousExp;
            CurrentData.lifetimeGoldEarned = previousLifetimeGold;
            CurrentData.lifetimeBridgesBuilt = previousBridgeCount;
            CurrentData.lifetimeContractsCompleted = previousContractCount;
            CurrentData.hasUnlockedContractsTab = previousContractsTab;
            CurrentData.hasUnreadContractsAlert = previousContractsAlert;
            CurrentData.hasUnreadObjectiveAlert = previousObjectiveAlert;
            return false;
        }

        if (goldReward > 0 || expReward > 0) OnCurrencyChanged?.Invoke();
        OnAlmanacAlertsChanged?.Invoke();
        OnObjectiveAlertsChanged?.Invoke();
        OnContractCompleted?.Invoke(contractName);
        CheckAllAchievements();
        return true;
    }

    public bool IsContractCompleted(string contractName)
    {
        return CurrentData != null &&
               CurrentData.completedContracts != null &&
               !string.IsNullOrWhiteSpace(contractName) &&
               CurrentData.completedContracts.Contains(contractName) &&
               HasValidSavedBridge(contractName);
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

        switch (achievement.goalType)
        {
            case AchievementSO.GoalType.TotalBridgesBuilt:
                if (CurrentData.lifetimeBridgesBuilt >= achievement.targetAmount) isUnlocked = true;
                break;
            case AchievementSO.GoalType.TotalGoldEarned:
                if (CurrentData.lifetimeGoldEarned >= achievement.targetAmount) isUnlocked = true;
                break;
            case AchievementSO.GoalType.TotalGoldSpent:
                if (CurrentData.lifetimeGoldSpent >= achievement.targetAmount) isUnlocked = true;
                break;
            case AchievementSO.GoalType.TotalExpEarned:
                if (CurrentData.exp >= achievement.targetAmount) isUnlocked = true;
                break;
            case AchievementSO.GoalType.ContractsCompleted:
                if (CurrentData.lifetimeContractsCompleted >= achievement.targetAmount) isUnlocked = true;
                break;
        }

        if (isUnlocked)
        {
            CurrentData.unlockedAchievements.Add(achievement.achievementID);
            
            // Give the player their bonus rewards!
            if (achievement.bonusGold > 0) AddGold(achievement.bonusGold);
            if (achievement.bonusExp > 0) AddExp(achievement.bonusExp);
            
            SaveGame();
            
            // Tell the UI to show a popup!
            OnAchievementUnlocked?.Invoke(achievement);
            Debug.Log($"<color=green>ACHIEVEMENT UNLOCKED: {achievement.achievementName}!</color>");
        }
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

        CurrentData.unlockedLessonIds.Add(normalizedId);
        CurrentData.hasUnlockedLessonsTab = true;
        CurrentData.hasUnreadLessonsAlert = true;
        SaveGame();
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

    public void ResetLessonProgress(string lessonName, bool saveImmediately = true)
    {
        if (CurrentData == null || CurrentData.completedLessons == null ||
            string.IsNullOrWhiteSpace(lessonName)) return;

        if (!CurrentData.completedLessons.Remove(lessonName)) return;
        if (saveImmediately) SaveGame();
    }
    public void UnlockMaterialForContract(string contractName, string materialName) { string key = contractName + "_" + materialName; if (!CurrentData.unlockedContractMaterials.Contains(key)) { CurrentData.unlockedContractMaterials.Add(key); SaveGame(); } }
    public bool IsMaterialUnlockedForContract(string contractName, string materialName) { string key = contractName + "_" + materialName; return CurrentData.unlockedContractMaterials.Contains(key); }
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
        if (CurrentData.completedLessons == null) CurrentData.completedLessons = new List<string>();
        if (CurrentData.completedContracts == null) CurrentData.completedContracts = new List<string>();
        if (CurrentData.unlockedAchievements == null) CurrentData.unlockedAchievements = new List<string>();
        if (CurrentData.activeQuests == null) CurrentData.activeQuests = new List<TrackedTask>();
        if (CurrentData.unlockedLevels == null) CurrentData.unlockedLevels = new List<string>();
        if (CurrentData.unlockedContractMaterials == null) CurrentData.unlockedContractMaterials = new List<string>();
        if (CurrentData.unlockedDoors == null) CurrentData.unlockedDoors = new List<string>();
        if (CurrentData.savedBridges == null) CurrentData.savedBridges = new List<SavedBridgeData>();
        if (CurrentData.npcProgressions == null) CurrentData.npcProgressions = new List<NPCProgressionSaveData>();

        completionRecordsMissingBridge.Clear();
        foreach (string contractName in CurrentData.completedContracts)
        {
            SavedBridgeData rawBridge = CurrentData.savedBridges.Find(bridge =>
                bridge != null && bridge.contractId == contractName);
            if (!IsBridgeDataValid(rawBridge, out _))
                completionRecordsMissingBridge.Add(contractName);
        }

        if (completionRecordsMissingBridge.Count > 0)
        {
            Debug.LogWarning(
                $"[PlayerDataManager] Found {completionRecordsMissingBridge.Count} completed contract record(s) " +
                "without valid bridge geometry. They will remain incomplete until their bridges are rebuilt and saved.",
                this);
        }

        if (CurrentData.unlockedLessonIds.Count > 0)
            CurrentData.hasUnlockedLessonsTab = true;
    }

    public bool SaveBridgeData(string contractId, List<Point> points, List<Bar> bars, float totalSpent, float maxStress)
    {
        if (CurrentData == null || string.IsNullOrWhiteSpace(contractId) ||
            points == null || bars == null) return false;

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
            entry != null && entry.contractId == newSave.contractId);
        CurrentData.savedBridges.RemoveAll(entry =>
            entry != null && entry.contractId == newSave.contractId);
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

    public SavedBridgeData GetSavedBridge(string contractId)
    {
        if (CurrentData == null || CurrentData.savedBridges == null ||
            string.IsNullOrWhiteSpace(contractId)) return null;

        SavedBridgeData bridge = CurrentData.savedBridges.Find(entry =>
            entry != null && entry.contractId == contractId);
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
        CurrentData.savedBridges.RemoveAll(b => b != null && b.contractId == contractId);
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
