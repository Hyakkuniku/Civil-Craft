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
    public TextMeshProUGUI secondaryCurrencyText;
    public GameObject achievementSectionRoot;
    public RectTransform overviewCardRect;
    public Button achievementPanelButton;

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
        ResolveProfileBindings();
        if (achievementPanelButton != null)
        {
            achievementPanelButton.onClick.RemoveListener(OpenAchievementPanel);
            achievementPanelButton.onClick.AddListener(OpenAchievementPanel);
        }
        RefreshStats();
    }

    private void OnDisable()
    {
        if (achievementPanelButton != null)
            achievementPanelButton.onClick.RemoveListener(OpenAchievementPanel);
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
        if (secondaryCurrencyText != null) secondaryCurrencyText.text = "0";
        if (totalGoldEarnedText != null)
            totalGoldEarnedText.text = "₱" + data.lifetimeGoldEarned.ToString("N0");

        if (expProgressFill != null)
        {
            float progress = isMaxRank
                ? 1f
                : Mathf.Clamp01((float)data.exp / nextRankThreshold);

            // A sprite-less Unity Image does not visually honour fillAmount.
            // Drive the right anchor instead so this works with the simple
            // coloured rectangle used by the Almanac design.
            expProgressFill.type = Image.Type.Simple;
            RectTransform fillRect = expProgressFill.rectTransform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            fillRect.pivot = new Vector2(0f, 0.5f);
        }

        if (expRemainingText != null)
            expRemainingText.text = isMaxRank
                ? "Maximum rank reached"
                : Mathf.Max(0, nextRankThreshold - data.exp).ToString("N0") +
                  " EXP to next rank";

        PopulateAchievementSummary(data);
    }

    public void OpenAchievementPanel()
    {
        AchievementUIManager achievementManager = FindObjectOfType<AchievementUIManager>(true);
        if (achievementManager != null)
        {
            achievementManager.OpenPanel();
            return;
        }

        Debug.LogWarning("The Almanac could not find an AchievementUIManager in this scene.", this);
    }

    private void ResolveProfileBindings()
    {
        Transform design = FindDescendant(transform, "ProfileStatsDesign");
        if (design == null) design = transform;

        Transform fill = FindDescendant(design, "ExpProgressFill");
        if (fill != null)
            expProgressFill = fill.GetComponent<Image>();

        Transform achievementCard = FindDescendant(design, "AchievementSummaryCard");
        if (achievementCard != null)
        {
            Image cardImage = achievementCard.GetComponent<Image>();
            if (cardImage != null) cardImage.raycastTarget = true;

            Button cardButton = achievementCard.GetComponent<Button>();
            if (cardButton == null)
                cardButton = achievementCard.gameObject.AddComponent<Button>();
            if (cardImage != null) cardButton.targetGraphic = cardImage;
            achievementPanelButton = cardButton;

            Transform obsoleteButton = FindDescendant(achievementCard, "OpenAchievementsButton");
            if (obsoleteButton != null && obsoleteButton != achievementCard)
                obsoleteButton.gameObject.SetActive(false);
        }
    }

    private static Transform FindDescendant(Transform parent, string objectName)
    {
        if (parent == null) return null;
        foreach (Transform child in parent)
        {
            if (child.name == objectName) return child;
            Transform found = FindDescendant(child, objectName);
            if (found != null) return found;
        }
        return null;
    }

    private void PopulateAchievementSummary(PlayerData data)
    {
        int unlockedCount = data.unlockedAchievements != null
            ? data.unlockedAchievements.Count
            : 0;
        AchievementSO latestAchievement = null;

        if (achievementSectionRoot != null)
            achievementSectionRoot.SetActive(unlockedCount > 0);

        if (overviewCardRect != null)
        {
            overviewCardRect.anchorMin = new Vector2(
                overviewCardRect.anchorMin.x,
                unlockedCount > 0 ? 0.42f : 0.10f);
            overviewCardRect.offsetMin = Vector2.zero;
        }

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
