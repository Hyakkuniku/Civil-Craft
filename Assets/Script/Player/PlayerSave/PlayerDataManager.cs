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
        string json = JsonUtility.ToJson(CurrentData, true);
        File.WriteAllText(saveFilePath, json);
    }

    public void LoadGame()
    {
        if (File.Exists(saveFilePath))
        {
            string json = File.ReadAllText(saveFilePath);
            CurrentData = JsonUtility.FromJson<PlayerData>(json);
        }
        else
        {
            CurrentData = new PlayerData();
            CurrentData.playerName = PlayerPrefs.GetString("SavedPlayerName", "Guest"); 
        }

        NormalizeLoadedData();
        SaveGame();
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

    public void CompleteContract(string contractName) 
    {
        if (CurrentData == null || string.IsNullOrWhiteSpace(contractName)) return;

        // Older save files may not contain this list yet.
        if (CurrentData.completedContracts == null)
            CurrentData.completedContracts = new List<string>();

        if (!IsContractCompleted(contractName))
        { 
            CurrentData.completedContracts.Add(contractName); 
            CurrentData.lifetimeContractsCompleted++; 
            CurrentData.hasUnlockedContractsTab = true;
            CurrentData.hasUnreadContractsAlert = true;
            CurrentData.hasUnreadObjectiveAlert = true;
            SaveGame();
            OnAlmanacAlertsChanged?.Invoke();
            OnObjectiveAlertsChanged?.Invoke();
            OnContractCompleted?.Invoke(contractName);
            CheckAllAchievements(); 
        } 
    }

    public bool IsContractCompleted(string contractName)
    {
        return CurrentData != null &&
               CurrentData.completedContracts != null &&
               !string.IsNullOrWhiteSpace(contractName) &&
               CurrentData.completedContracts.Contains(contractName);
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
        CurrentData.hasUnreadContractsAlert |= CurrentData.completedContracts.Count > 0;
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

        if (CurrentData.unlockedLessonIds.Count > 0)
            CurrentData.hasUnlockedLessonsTab = true;
    }

    public void SaveBridgeData(string contractId, List<Point> points, List<Bar> bars, float totalSpent, float maxStress)
    {
        CurrentData.savedBridges.RemoveAll(b => b.contractId == contractId); 

        SavedBridgeData newSave = new SavedBridgeData { 
            contractId = contractId,
            totalSpent = totalSpent,
            maxStress = maxStress
        };
        
        Dictionary<Point, int> pointToIndex = new Dictionary<Point, int>();
        
        for(int i = 0; i < points.Count; i++)
        {
            pointToIndex[points[i]] = i;
            newSave.points.Add(new SavedPointData {
                index = i,
                position = new SerializableVector3(points[i].transform.position),
                isAnchor = points[i].isAnchor,
                originalIsAnchor = points[i].originalIsAnchor
            });
        }

        foreach(Bar b in bars)
        {
            if(b.startPoint != null && b.endPoint != null && pointToIndex.ContainsKey(b.startPoint) && pointToIndex.ContainsKey(b.endPoint))
            {
                newSave.bars.Add(new SavedBarData {
                    startPointIndex = pointToIndex[b.startPoint],
                    endPointIndex = pointToIndex[b.endPoint],
                    materialName = b.materialData.name
                });
            }
        }

        CurrentData.savedBridges.Add(newSave);
        SaveGame();
    }

    public SavedBridgeData GetSavedBridge(string contractId)
    {
        return CurrentData.savedBridges.Find(b => b.contractId == contractId);
    }

    public void DeleteSavedBridge(string contractId)
    {
        CurrentData.savedBridges.RemoveAll(b => b.contractId == contractId);
        SaveGame();
    }
}
