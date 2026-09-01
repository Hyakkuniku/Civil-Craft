using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "NewAchievement", menuName = "Bridge/Achievement")]
public class AchievementSO : ScriptableObject
{
    public enum GoalType 
    { 
        TotalBridgesBuilt, 
        [InspectorName("Gold Received")]
        TotalGoldEarned,
        [InspectorName("Gold Spent")]
        TotalGoldSpent,
        [InspectorName("EXP Received")]
        TotalExpEarned,
        ContractsCompleted,
        AllContractsInMapCompleted,
        AllContractsAcrossAllMapsCompleted,
        StoryModeContractsCompleted,
        BuildLocationCompleted
    }

    [Header("Achievement Details")]
    public string achievementID = "ACH_001";
    public string achievementName = "Master Builder";
    [TextArea(2, 4)]
    public string description = "Build 50 total bridges.";
    [Tooltip("While locked, the Almanac row hides this achievement's details and displays ??? instead.")]
    public bool hideDetailsUntilUnlocked = false;
    
    [Header("Visuals")]
    [FormerlySerializedAs("unlockedIcon")]
    [Tooltip("The achievement's permanent artwork. The same image is shown before and after completion.")]
    public Sprite achievementIcon;

    [Header("Goal Requirements")]
    public GoalType goalType = GoalType.TotalBridgesBuilt;
    [Tooltip("The target number the player needs to reach to unlock this.")]
    public int targetAmount = 50;

    [Tooltip("Used only by All Contracts In Map Completed achievements.")]
    public ContractSO.ContractMap targetContractMap = ContractSO.ContractMap.CanyonCrossing;
    [Tooltip("For contract-based goals, ignore contracts whose Tutorial Contract toggle is enabled.")]
    public bool excludeTutorialContracts = true;

    [Header("Rewards")]
    public int bonusGold = 1000;
    public int bonusExp = 500;

    [Header("Optional Cosmetic Reward")]
    [Tooltip("Show the same collectible reward flow used by the Safety Helmet when this achievement unlocks.")]
    public bool grantsCosmeticReward = false;
    [Tooltip("Must match a Cosmetic Item ID on PlayerCosmetics, for example EngineerHardHat.")]
    public string rewardCosmeticID = "";
    public string rewardDisplayName = "Achievement Reward";
    public Sprite rewardIcon;
}
