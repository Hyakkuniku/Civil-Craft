using UnityEngine;

[CreateAssetMenu(fileName = "NewAchievement", menuName = "Bridge/Achievement")]
public class AchievementSO : ScriptableObject
{
    public enum GoalType 
    { 
        TotalBridgesBuilt, 
        TotalGoldEarned, 
        TotalGoldSpent, 
        TotalExpEarned, 
        ContractsCompleted 
    }

    [Header("Achievement Details")]
    public string achievementID = "ACH_001";
    public string achievementName = "Master Builder";
    [TextArea(2, 4)]
    public string description = "Build 50 total bridges.";
    
    [Header("Visuals")]
    public Sprite unlockedIcon;
    public Sprite lockedIcon;

    [Header("Goal Requirements")]
    public GoalType goalType = GoalType.TotalBridgesBuilt;
    [Tooltip("The target number the player needs to reach to unlock this.")]
    public int targetAmount = 50;

    [Header("Rewards")]
    public int bonusGold = 1000;
    public int bonusExp = 500;
}