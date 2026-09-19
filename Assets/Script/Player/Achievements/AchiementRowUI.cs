using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class AchievementRowUI : MonoBehaviour
{
    private bool layoutPassPending;
    private float displayedProgress;
    private AchievementSO currentAchievement;
    private bool currentHideDetails;

    [Header("UI Elements")]
    public Image iconImage;
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI descriptionText;
    public TextMeshProUGUI rewardText;
    private Image rewardCoinIcon;
    private Image rewardExperienceIcon;
    private TextMeshProUGUI rewardCoinValueText;
    private TextMeshProUGUI rewardExperienceValueText;
    
    [Header("Progress Bar")]
    public Image progressFill;
    public TextMeshProUGUI progressText;
    
    [Header("Completion Visuals")]
    public GameObject completedCheckmark; 
    public CanvasGroup canvasGroup; // Optional: To slightly fade out completed achievements

    public void Setup(
        AchievementSO achievement,
        int currentProgress,
        bool isCompleted,
        int targetAmountOverride = -1)
    {
        if (achievement == null) return;

        currentAchievement = achievement;

        gameObject.SetActive(true);
        ApplyReferenceLayout();
        layoutPassPending = true;

        bool hideDetails = achievement.hideDetailsUntilUnlocked && !isCompleted;
        currentHideDetails = hideDetails;

        if (titleText != null)
            titleText.SetText(hideDetails ? "???" : achievement.achievementName);
        if (descriptionText != null)
            descriptionText.SetText(hideDetails ? "???" : achievement.description);

        if (iconImage != null)
        {
            // Keep the prefab's placeholder visible until unique artwork is
            // assigned, but never swap to a separate locked/unlocked sprite.
            if (!hideDetails && achievement.achievementIcon != null)
            iconImage.sprite = achievement.achievementIcon;
            iconImage.gameObject.SetActive(true);
            iconImage.enabled = !hideDetails && iconImage.sprite != null;
            iconImage.preserveAspect = true;
            iconImage.color = Color.white;
            iconImage.material = null;
            iconImage.canvasRenderer.SetAlpha(1f);
        }
        
        EnsureRewardIcons();
        ApplyRewardLayout(achievement, hideDetails);

        if (hideDetails)
        {
            if (progressText != null) progressText.text = "???";
            SetDisplayedProgress(0f);
            if (completedCheckmark != null) completedCheckmark.SetActive(false);
            if (canvasGroup != null) canvasGroup.alpha = 1f;
            return;
        }

        if (isCompleted)
        {
            // Completed State
            if (progressText != null) progressText.text = "Completed!";
            SetDisplayedProgress(1f);
            if (completedCheckmark != null) completedCheckmark.SetActive(true);
            if (canvasGroup != null) canvasGroup.alpha = 1f;
        }
        else
        {
            // In-Progress State
            if (completedCheckmark != null) completedCheckmark.SetActive(false);
            if (canvasGroup != null) canvasGroup.alpha = 1f;
            
            // Clamp progress so it doesn't show "55/50" if they overshot it before opening the menu
            int targetAmount = targetAmountOverride >= 0
                ? targetAmountOverride
                : achievement.targetAmount;
            int clampedProgress = Mathf.Min(currentProgress, targetAmount);
            
            if (progressText != null) progressText.text = $"{clampedProgress} / {targetAmount}";
            SetDisplayedProgress(targetAmount > 0
                ? (float)clampedProgress / targetAmount
                : 0f);
        }
    }

    private void LateUpdate()
    {
        if (!layoutPassPending) return;
        layoutPassPending = false;

        if (transform.parent is RectTransform parentRect)
            LayoutRebuilder.ForceRebuildLayoutImmediate(parentRect);

        ApplyReferenceLayout();
        if (currentAchievement != null)
            ApplyRewardLayout(currentAchievement, currentHideDetails);
        ApplyProgressVisual();
        if (transform is RectTransform rowRect)
            LayoutRebuilder.ForceRebuildLayoutImmediate(rowRect);
    }

    public void ApplyReferenceLayout()
    {
        RectTransform rowRect = transform as RectTransform;
        if (rowRect != null)
        {
            rowRect.anchorMin = new Vector2(0f, 1f);
            rowRect.anchorMax = new Vector2(1f, 1f);
            rowRect.pivot = new Vector2(0.5f, 1f);
            rowRect.anchoredPosition = new Vector2(0f, rowRect.anchoredPosition.y);
            rowRect.sizeDelta = new Vector2(rowRect.sizeDelta.x, 138f);

            // A few scene variants had a zero-width Content rect during the
            // first layout pass. Without this fallback only the fixed-width
            // progress graphic protrudes into view and every label disappears.
            if (rowRect.rect.width < 1f && rowRect.parent is RectTransform parentRect &&
                parentRect.rect.width > 1f)
            {
                float horizontalPadding = 20f;
                VerticalLayoutGroup parentLayout = parentRect.GetComponent<VerticalLayoutGroup>();
                if (parentLayout != null)
                    horizontalPadding = parentLayout.padding.horizontal;

                rowRect.SetSizeWithCurrentAnchors(
                    RectTransform.Axis.Horizontal,
                    Mathf.Max(1f, parentRect.rect.width - horizontalPadding));
            }
        }

        LayoutElement layout = GetComponent<LayoutElement>();
        if (layout == null) layout = gameObject.AddComponent<LayoutElement>();
        layout.minHeight = 138f;
        layout.preferredHeight = 138f;
        layout.flexibleWidth = 1f;

        HorizontalLayoutGroup oldLayout = GetComponent<HorizontalLayoutGroup>();
        if (oldLayout != null) oldLayout.enabled = false;

        Image rowBackground = GetComponent<Image>();
        if (rowBackground != null)
        {
            rowBackground.color = new Color(1f, 0.965f, 0.84f, 0.98f);
            rowBackground.material = null;
            rowBackground.enabled = true;
            rowBackground.canvasRenderer.SetAlpha(1f);
        }

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.ignoreParentGroups = false;
        }

        Outline rowOutline = GetComponent<Outline>();
        if (rowOutline == null) rowOutline = gameObject.AddComponent<Outline>();
        rowOutline.effectColor = new Color(0.35f, 0.24f, 0.15f, 0.45f);
        rowOutline.effectDistance = new Vector2(2f, -2f);

        ConfigureRect(iconImage != null ? iconImage.rectTransform : null,
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(70f, 0f), new Vector2(92f, 92f));

        ConfigureTextRect(titleText,
            new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0f, 1f), new Vector2(148f, -12f), new Vector2(-520f, 34f),
            27f, FontStyles.Bold);

        if (descriptionText != null)
        {
            descriptionText.rectTransform.SetParent(transform, false);
            ConfigureTextRect(descriptionText,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, 1f), new Vector2(148f, -48f), new Vector2(-520f, 30f),
                21f, FontStyles.Normal);
        }

        if (rewardText != null)
        {
            rewardText.rectTransform.SetParent(transform, false);
            ConfigureTextRect(rewardText,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, 1f), new Vector2(148f, -82f), new Vector2(82f, 32f),
                20f, FontStyles.Normal);
            rewardText.color = new Color(0.30f, 0.22f, 0.14f, 1f);
        }
        EnsureRewardIcons();

        // Keep the legacy name as a fallback for scenes/prefabs not yet renamed.
        Transform divider = transform.Find("RowDivider") ?? transform.Find("GameObject");
        if (divider != null)
        {
            ConfigureRect(divider as RectTransform,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(132f, 0f), new Vector2(3f, 104f));
            Image dividerImage = divider.GetComponent<Image>();
            if (dividerImage != null)
                dividerImage.color = new Color(0.37f, 0.25f, 0.15f, 0.8f);
        }

        Transform progressContainer = progressFill != null && progressFill.transform.parent != null
            ? progressFill.transform.parent.parent
            : null;
        RectTransform progressContainerRect = progressContainer as RectTransform;
        ConfigureRect(progressContainerRect,
            new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(1f, 0.5f), new Vector2(-26f, 0f), new Vector2(340f, 92f));

        if (progressText != null)
        {
            ConfigureTextRect(progressText,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, 0f), new Vector2(0f, 36f),
                23f, FontStyles.Bold);
            progressText.alignment = TextAlignmentOptions.TopRight;
        }

        RectTransform progressBackgroundRect = progressFill != null
            ? progressFill.transform.parent as RectTransform
            : null;
        ConfigureRect(progressBackgroundRect,
            new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(0f, 20f));

        if (progressBackgroundRect != null)
        {
            Image background = progressBackgroundRect.GetComponent<Image>();
            if (background != null)
            {
                background.color = new Color(0.52f, 0.42f, 0.27f, 1f);
                background.material = null;
                background.sprite = null;
                background.type = Image.Type.Simple;
                background.raycastTarget = false;
                background.enabled = true;
                background.canvasRenderer.SetAlpha(1f);
            }

            Outline progressOutline = progressBackgroundRect.GetComponent<Outline>();
            if (progressOutline == null)
                progressOutline = progressBackgroundRect.gameObject.AddComponent<Outline>();
            progressOutline.effectColor = new Color(0.25f, 0.17f, 0.10f, 0.85f);
            progressOutline.effectDistance = new Vector2(1.5f, -1.5f);

            Mask clipMask = progressBackgroundRect.GetComponent<Mask>();
            if (clipMask == null) clipMask = progressBackgroundRect.gameObject.AddComponent<Mask>();
            clipMask.showMaskGraphic = true;
        }

        if (progressFill != null)
        {
            progressFill.color = new Color(0.88f, 0.62f, 0.18f, 1f);
            progressFill.material = null;
            progressFill.sprite = null;
            progressFill.type = Image.Type.Simple;
            progressFill.raycastTarget = false;
            progressFill.canvasRenderer.SetAlpha(1f);
            ApplyProgressVisual();
        }

        if (completedCheckmark != null)
        {
            ConfigureRect(completedCheckmark.transform as RectTransform,
                new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0.5f, 0.5f), new Vector2(113f, -26f), new Vector2(30f, 30f));
        }
    }

    private void EnsureRewardIcons()
    {
        rewardCoinIcon = EnsureRewardIcon(
            "RewardCoinIcon", CurrencyIconKind.Coin, new Vector2(244f, -98f));
        rewardExperienceIcon = EnsureRewardIcon(
            "RewardExperienceIcon", CurrencyIconKind.Experience,
            new Vector2(374f, -98f));
        rewardCoinValueText = EnsureRewardValueText("RewardCoinValue", new Vector2(264f, -82f));
        rewardExperienceValueText = EnsureRewardValueText("RewardExperienceValue", new Vector2(394f, -82f));
    }

    private void ApplyRewardLayout(AchievementSO achievement, bool hideDetails)
    {
        bool showGold = !hideDetails && achievement.bonusGold > 0;
        bool showExperience = !hideDetails && achievement.bonusExp > 0;
        bool hasReward = showGold || showExperience;

        if (rewardText != null)
        {
            rewardText.text = hideDetails ? "???" : hasReward ? "Reward" : "Completed";
            rewardText.rectTransform.sizeDelta = new Vector2(hasReward ? 82f : 130f, 32f);
        }

        if (rewardCoinIcon != null)
            rewardCoinIcon.gameObject.SetActive(showGold && rewardCoinIcon.sprite != null);
        if (rewardCoinValueText != null)
        {
            rewardCoinValueText.text = achievement.bonusGold.ToString("N0");
            rewardCoinValueText.gameObject.SetActive(showGold);
        }

        float experienceIconX = showGold ? 374f : 244f;
        float experienceValueX = showGold ? 394f : 264f;
        if (rewardExperienceIcon != null)
        {
            rewardExperienceIcon.rectTransform.anchoredPosition = new Vector2(experienceIconX, -98f);
            rewardExperienceIcon.gameObject.SetActive(showExperience && rewardExperienceIcon.sprite != null);
        }
        if (rewardExperienceValueText != null)
        {
            rewardExperienceValueText.rectTransform.anchoredPosition = new Vector2(experienceValueX, -82f);
            rewardExperienceValueText.text = achievement.bonusExp.ToString("N0");
            rewardExperienceValueText.gameObject.SetActive(showExperience);
        }
    }

    private Image EnsureRewardIcon(string name, CurrencyIconKind kind, Vector2 position)
    {
        Image image = CurrencyIconCatalog.EnsureIcon(
            transform, name, kind, new Vector2(0f, 1f), new Vector2(0f, 1f));
        if (image == null) return null;
        RectTransform rect = image.rectTransform;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(26f, 26f);
        return image;
    }

    private TextMeshProUGUI EnsureRewardValueText(string name, Vector2 position)
    {
        Transform existing = transform.Find(name);
        bool existed = existing != null;
        bool wasActive = existed && existing.gameObject.activeSelf;
        TextMeshProUGUI text;
        if (existing != null)
        {
            text = existing.GetComponent<TextMeshProUGUI>();
        }
        else
        {
            GameObject valueObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            valueObject.transform.SetParent(transform, false);
            text = valueObject.AddComponent<TextMeshProUGUI>();
        }

        if (rewardText != null) text.font = rewardText.font;
        ConfigureTextRect(text,
            new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, 1f), position, new Vector2(96f, 32f),
            20f, FontStyles.Normal);
        text.color = new Color(0.30f, 0.22f, 0.14f, 1f);
        text.enableAutoSizing = true;
        text.fontSizeMin = 15f;
        text.fontSizeMax = 20f;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.gameObject.SetActive(existed && wasActive);
        return text;
    }

    private void SetDisplayedProgress(float normalizedProgress)
    {
        displayedProgress = Mathf.Clamp01(normalizedProgress);
        ApplyProgressVisual();
    }

    private void ApplyProgressVisual()
    {
        if (progressFill == null) return;

        float normalized = Mathf.Clamp01(displayedProgress);
        RectTransform fillRect = progressFill.rectTransform;
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = new Vector2(normalized, 1f);
        fillRect.offsetMin = new Vector2(2f, 2f);
        fillRect.offsetMax = new Vector2(normalized > 0f ? -2f : 0f, -2f);
        fillRect.localScale = Vector3.one;
        progressFill.fillAmount = 1f;
        progressFill.enabled = normalized > 0.0001f;
    }

    private static void ConfigureTextRect(
        TMP_Text text,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 position,
        Vector2 size,
        float fontSize,
        FontStyles fontStyle)
    {
        if (text == null) return;
        ConfigureRect(text.rectTransform, anchorMin, anchorMax, pivot, position, size);
        text.fontSize = fontSize;
        text.enableAutoSizing = false;
        text.fontStyle = fontStyle;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.color = new Color(0.20f, 0.13f, 0.09f, 1f);
        text.margin = Vector4.zero;
        text.gameObject.SetActive(true);
        text.enabled = true;
        text.raycastTarget = false;
        text.canvasRenderer.SetAlpha(1f);
        text.SetVerticesDirty();
        text.ForceMeshUpdate(true, true);
    }

    private static void ConfigureRect(
        RectTransform rect,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 position,
        Vector2 size)
    {
        if (rect == null) return;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
    }
}
