using System.Collections.Generic;
using UnityEngine;

public class AchievementUIManager : MonoBehaviour
{
    [Header("Panel Visibility")]
    [Tooltip("Drag the main Achievement Panel here so it can be toggled on/off.")]
    public GameObject achievementPanel;

    [Header("UI Setup")]
    [Tooltip("The Content object inside your Scroll View (must have a Vertical Layout Group)")]
    public Transform contentParent; 
    [Tooltip("The Row Prefab with the AchievementRowUI script attached")]
    public GameObject achievementRowPrefab;

    [Header("Database")]
    [Tooltip("Drag all your Achievement ScriptableObjects here!")]
    public List<AchievementSO> allAchievements = new List<AchievementSO>();

    private void Awake()
    {
        // Keep the panel closed by default when the game starts
        if (achievementPanel != null) 
        {
            achievementPanel.SetActive(false);
        }
    }

    // --- NEW: Methods to hook up to your UI Buttons ---
    public void OpenPanel()
    {
        if (achievementPanel != null) 
        {
            achievementPanel.SetActive(true);
            RefreshUI(); // Automatically populate the list every time it is opened!
        }
    }

    public void ClosePanel()
    {
        if (achievementPanel != null) 
        {
            achievementPanel.SetActive(false);
        }
    }

    public void RefreshUI()
    {
        // 1. Clear old rows to prevent duplicates
        foreach(Transform child in contentParent)
        {
            Destroy(child.gameObject);
        }

        if (PlayerDataManager.Instance == null || PlayerDataManager.Instance.CurrentData == null) return;

        PlayerData data = PlayerDataManager.Instance.CurrentData;

        // 2. Separate them into two lists for sorting
        List<AchievementSO> completedList = new List<AchievementSO>();
        List<AchievementSO> inProgressList = new List<AchievementSO>();

        foreach(AchievementSO ach in allAchievements)
        {
            if (data.unlockedAchievements.Contains(ach.achievementID))
            {
                completedList.Add(ach);
            }
            else
            {
                inProgressList.Add(ach);
            }
        }

        // 3. Spawn In-Progress achievements FIRST (so they appear at the top)
        foreach(AchievementSO ach in inProgressList)
        {
            CreateRow(ach, false);
        }

        // 4. Spawn Completed achievements LAST (so they are pushed to the bottom)
        foreach(AchievementSO ach in completedList)
        {
            CreateRow(ach, true);
        }
    }

    private void CreateRow(AchievementSO achievement, bool isCompleted)
    {
        GameObject rowObj = Instantiate(achievementRowPrefab, contentParent);
        AchievementRowUI rowUI = rowObj.GetComponent<AchievementRowUI>();

        if (rowUI != null)
        {
            // Calculate current progress using the helper method
            int currentProg = GetCurrentProgress(achievement.goalType);
            rowUI.Setup(achievement, currentProg, isCompleted);
        }
    }

    // Helper method to pull the correct stat from PlayerData based on the GoalType
    private int GetCurrentProgress(AchievementSO.GoalType type)
    {
        PlayerData data = PlayerDataManager.Instance.CurrentData;
        
        switch(type)
        {
            case AchievementSO.GoalType.TotalBridgesBuilt: 
                return data.lifetimeBridgesBuilt;
            case AchievementSO.GoalType.TotalGoldEarned: 
                return data.lifetimeGoldEarned;
            case AchievementSO.GoalType.TotalGoldSpent: 
                return data.lifetimeGoldSpent;
            case AchievementSO.GoalType.TotalExpEarned: 
                return data.exp; // Total EXP is just their current EXP
            case AchievementSO.GoalType.ContractsCompleted: 
                return data.lifetimeContractsCompleted;
            default: 
                return 0;
        }
    }
}