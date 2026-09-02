using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class AlmanacPlayerStats : MonoBehaviour
{
    [Header("Profile Card Layout")]
    [Tooltip("When enabled, text fields contain values only because their labels are supplied by the designed cards.")]
    public bool useProfileCardLayout;
    public Image expProgressFill;
    public TextMeshProUGUI expRemainingText;
    public TextMeshProUGUI achievementsSummaryText;
    public Image latestAchievementIcon;

    [Header("UI Text Fields")]
    public TextMeshProUGUI playerNameText;
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI goldText;
    public TextMeshProUGUI expText;
    public TextMeshProUGUI bridgesBuiltText;

    [Header("New Lifetime Stats (Optional)")]
    public TextMeshProUGUI contractsCompletedText; 
    public TextMeshProUGUI totalGoldEarnedText;

    private void OnEnable()
    {
        Subscribe();
        RefreshStats();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    public void RefreshStats()
    {
        if (PlayerDataManager.Instance == null || PlayerDataManager.Instance.CurrentData == null) 
            return;

        PlayerData data = PlayerDataManager.Instance.CurrentData;

        if (useProfileCardLayout)
        {
            PopulateProfileCards(data);
            return;
        }

        if (playerNameText != null) playerNameText.text = "Engineer: " + data.playerName;
        if (titleText != null) titleText.text = "Rank: " + data.GetTitle();
        if (goldText != null) goldText.text = "Gold: " + data.gold.ToString("N0");
        if (expText != null) expText.text = "EXP: " + data.exp.ToString("N0");
        if (bridgesBuiltText != null) bridgesBuiltText.text = "Bridges Built: " + data.lifetimeBridgesBuilt;
        if (contractsCompletedText != null) contractsCompletedText.text = "Contracts Done: " + data.lifetimeContractsCompleted;
        if (totalGoldEarnedText != null) totalGoldEarnedText.text = "Lifetime Earnings: " + data.lifetimeGoldEarned.ToString("N0");
    }

    private void PopulateProfileCards(PlayerData data)
    {
        int nextRankThreshold = GetNextRankThreshold(data.exp);
        bool isMaxRank = nextRankThreshold <= 0;

        if (playerNameText != null) playerNameText.text = "Engineer: " + data.playerName;
        if (titleText != null) titleText.text = data.GetTitle();
        if (goldText != null) goldText.text = "₱" + data.gold.ToString("N0");
        if (expText != null)
            expText.text = isMaxRank
                ? data.exp.ToString("N0") + " EXP"
                : data.exp.ToString("N0") + " / " + nextRankThreshold.ToString("N0");
        if (bridgesBuiltText != null) bridgesBuiltText.text = data.lifetimeBridgesBuilt.ToString("N0");
        if (contractsCompletedText != null)
            contractsCompletedText.text = data.lifetimeContractsCompleted.ToString("N0");
        if (totalGoldEarnedText != null)
            totalGoldEarnedText.text = "₱" + data.lifetimeGoldEarned.ToString("N0");

        if (expProgressFill != null)
        {
            expProgressFill.type = Image.Type.Filled;
            expProgressFill.fillMethod = Image.FillMethod.Horizontal;
            expProgressFill.fillOrigin = 0;
            expProgressFill.fillAmount = isMaxRank
                ? 1f
                : Mathf.Clamp01((float)data.exp / nextRankThreshold);
        }

        if (expRemainingText != null)
            expRemainingText.text = isMaxRank
                ? "Maximum rank reached"
                : Mathf.Max(0, nextRankThreshold - data.exp).ToString("N0") +
                  " EXP to next rank";

        PopulateAchievementSummary(data);
    }

    private void PopulateAchievementSummary(PlayerData data)
    {
        int unlockedCount = data.unlockedAchievements != null
            ? data.unlockedAchievements.Count
            : 0;
        AchievementSO latestAchievement = null;

        if (unlockedCount > 0 && PlayerDataManager.Instance.allGameAchievements != null)
        {
            string latestId = data.unlockedAchievements[unlockedCount - 1];
            latestAchievement = PlayerDataManager.Instance.allGameAchievements.Find(
                achievement => achievement != null && achievement.achievementID == latestId);
        }

        if (achievementsSummaryText != null)
        {
            achievementsSummaryText.text = unlockedCount == 0
                ? "No achievements yet.\nKeep building to earn your first achievement!"
                : unlockedCount.ToString("N0") +
                  (unlockedCount == 1 ? " achievement unlocked" : " achievements unlocked") +
                  (latestAchievement != null
                      ? "\nLatest: " + latestAchievement.achievementName
                      : string.Empty);
        }

        if (latestAchievementIcon != null)
        {
            latestAchievementIcon.sprite = latestAchievement != null
                ? latestAchievement.achievementIcon
                : null;
            latestAchievementIcon.enabled = latestAchievementIcon.sprite != null;
            latestAchievementIcon.preserveAspect = true;
            latestAchievementIcon.color = Color.white;
        }
    }

    private void Subscribe()
    {
        if (PlayerDataManager.Instance == null) return;
        PlayerDataManager.Instance.OnCurrencyChanged -= RefreshStats;
        PlayerDataManager.Instance.OnCurrencyChanged += RefreshStats;
        PlayerDataManager.Instance.OnContractCompleted -= HandleContractCompleted;
        PlayerDataManager.Instance.OnContractCompleted += HandleContractCompleted;
        PlayerDataManager.Instance.OnAchievementUnlocked -= HandleAchievementUnlocked;
        PlayerDataManager.Instance.OnAchievementUnlocked += HandleAchievementUnlocked;
    }

    private void Unsubscribe()
    {
        if (PlayerDataManager.Instance == null) return;
        PlayerDataManager.Instance.OnCurrencyChanged -= RefreshStats;
        PlayerDataManager.Instance.OnContractCompleted -= HandleContractCompleted;
        PlayerDataManager.Instance.OnAchievementUnlocked -= HandleAchievementUnlocked;
    }

    private void HandleContractCompleted(string contractId)
    {
        RefreshStats();
    }

    private void HandleAchievementUnlocked(AchievementSO achievement)
    {
        RefreshStats();
    }

    private static int GetNextRankThreshold(int exp)
    {
        if (exp < 100) return 100;
        if (exp < 300) return 300;
        if (exp < 600) return 600;
        if (exp < 1000) return 1000;
        return -1;
    }
}
