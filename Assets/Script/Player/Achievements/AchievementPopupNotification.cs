using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Global, queued passive toast using authored UI. Important panels take priority;
/// a toast uses clear space at the top or waits without losing its readable time.
/// </summary>
[DisallowMultipleComponent]
public sealed class AchievementPopupNotification : MonoBehaviour
{
    private enum PopupKind
    {
        Achievement,
        Feature,
        Cosmetic,
        Almanac,
        Multiplayer
    }

    private sealed class PopupRequest
    {
        public string title;
        public string detail;
        public Sprite icon;
        public PopupKind kind;
        public int goldReward;
        public int experienceReward;
    }

    private const int AbsoluteSortingOrder = 32767;
    public static AchievementPopupNotification Instance { get; private set; }
    private static readonly Queue<PopupRequest> deferredNotifications = new Queue<PopupRequest>();

    [Header("Timing")]
    [Min(0.05f)] [SerializeField] private float slideDuration = 0.35f;
    [Min(0.25f)] [SerializeField] private float visibleDuration = 3.5f;

    [Header("Position")]
    [Tooltip("Horizontal offset from safe-area center and inset from its top edge, in Canvas units.")]
    [SerializeField] private Vector2 visiblePosition = new Vector2(0f, 28f);
    [SerializeField] private Vector2 hiddenPosition = new Vector2(0f, -180f);
    [Tooltip("Safe-area inset in Canvas units. The notification never moves into the central play area.")]
    [SerializeField, Min(0f)] private float screenEdgeMargin = 16f;
    [Tooltip("Space kept between the notification and visible controls/panels, in Canvas units.")]
    [SerializeField, Min(0f)] private float protectedPanelGap = 12f;
    private Vector2 settledPosition;
    private bool hasSettledPlacement;
    private Vector2 preferredScreenCenter;
    private int preferredScreenWidth = -1, preferredScreenHeight = -1;
    private Rect preferredSafeArea, preferredPopupBounds;
    private float preferredCanvasScale = -1f;
    private float nextPlacementRetryTime;

    [Header("Achievement Colors")]
    [SerializeField] private Color backgroundColor = new Color32(249, 235, 206, 255);
    [SerializeField] private Color accentColor = new Color32(155, 88, 20, 255);
    [SerializeField] private Color primaryTextColor = new Color32(87, 55, 31, 255);
    [SerializeField] private Color secondaryTextColor = new Color32(118, 89, 64, 255);

    [Header("Feature Unlock Colors")]
    [SerializeField] private Color featureBackgroundColor = new Color32(249, 235, 206, 255);
    [SerializeField] private Color featureAccentColor = new Color32(69, 122, 76, 255);
    [SerializeField] private Color featurePrimaryTextColor = new Color32(87, 55, 31, 255);
    [SerializeField] private Color featureSecondaryTextColor = new Color32(118, 89, 64, 255);

    [Header("Cosmetic Unlock Colors")]
    [SerializeField] private Color cosmeticBackgroundColor = new Color32(249, 235, 206, 255);
    [SerializeField] private Color cosmeticAccentColor = new Color32(128, 82, 118, 255);
    [SerializeField] private Color cosmeticPrimaryTextColor = new Color32(87, 55, 31, 255);
    [SerializeField] private Color cosmeticSecondaryTextColor = new Color32(118, 89, 64, 255);

    [Header("Almanac Update Colors")]
    [SerializeField] private Color almanacBackgroundColor = new Color32(249, 235, 206, 255);
    [SerializeField] private Color almanacAccentColor = new Color32(168, 92, 33, 255);
    [SerializeField] private Color almanacPrimaryTextColor = new Color32(87, 55, 31, 255);
    [SerializeField] private Color almanacSecondaryTextColor = new Color32(118, 89, 64, 255);

    private readonly Queue<PopupRequest> pendingNotifications = new Queue<PopupRequest>();
    private Coroutine notificationRoutine;
    private Canvas popupCanvas;
    private int topSortingLayerID;

    [Header("Scene UI References")]
    [Tooltip("Assigned by the scene setup. The popup child starts inactive.")]
    [SerializeField] private GameObject popupRoot;
    [SerializeField] private RectTransform popupRect;
    [SerializeField] private CanvasGroup popupGroup;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private Image accentImage;
    [SerializeField] private Outline popupOutline;
    [SerializeField] private Image iconImage;
    [SerializeField] private TMP_Text headingText;
    [SerializeField] private TMP_Text achievementNameText;
    [SerializeField] private TMP_Text rewardText;
    [SerializeField] private Image rewardCoinIcon;
    [SerializeField] private Image rewardExperienceIcon;
    [SerializeField] private TMP_Text rewardCoinValueText;
    [SerializeField] private TMP_Text rewardExperienceValueText;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        Instance = null;
        deferredNotifications.Clear();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
        popupCanvas = GetComponent<Canvas>();
        topSortingLayerID = FindTopSortingLayerID();
        ResolveVisualReferences();
        ForceAbsoluteOverlay();
        if (!HasCompleteUIReferences())
        {
            Debug.LogError(
                "AchievementPopupNotification has missing scene UI references. " +
                "Run Tools > Civil Craft > Setup Achievement Popup In Scenes.",
                this);
            if (Instance == this) Instance = null;
            enabled = false;
            return;
        }

        popupRoot.SetActive(false);
        foreach (Graphic graphic in popupRoot.GetComponentsInChildren<Graphic>(true))
            graphic.raycastTarget = false;
        UIReservedRegionLayout.Register(popupRect);
        while (deferredNotifications.Count > 0)
            QueueNotification(deferredNotifications.Dequeue());
    }

    private void OnEnable()
    {
        Canvas.willRenderCanvases -= ForceAbsoluteOverlay;
        Canvas.willRenderCanvases += ForceAbsoluteOverlay;
        UIReservedRegionLayout.LayoutChanging -= ResetPlacementRetry;
        UIReservedRegionLayout.LayoutChanging += ResetPlacementRetry;
    }

    private void OnDisable()
    {
        Canvas.willRenderCanvases -= ForceAbsoluteOverlay;
        UIReservedRegionLayout.LayoutChanging -= ResetPlacementRetry;
    }

    private void OnDestroy()
    {
        Canvas.willRenderCanvases -= ForceAbsoluteOverlay;
        UIReservedRegionLayout.LayoutChanging -= ResetPlacementRetry;
        UIReservedRegionLayout.Unregister(popupRect);
        if (Instance == this) Instance = null;
    }

    private void LateUpdate()
    {
        if (popupRoot != null && popupRoot.activeSelf)
            ForceAbsoluteOverlay();
    }

    /// <summary>
    /// Guaranteed achievement notification entry point. Unlock code calls this
    /// directly, so a popup cannot be missed because a scene listener was late.
    /// If the persistent popup has not initialized yet, the unlock is retained
    /// and displayed as soon as a valid popup becomes available.
    /// </summary>
    public static void NotifyAchievement(AchievementSO achievement)
    {
        if (achievement == null) return;

        PopupRequest request = new PopupRequest
        {
            title = achievement.achievementName,
            detail = BuildRewardText(achievement),
            icon = achievement.achievementIcon,
            kind = PopupKind.Achievement,
            goldReward = achievement.bonusGold,
            experienceReward = achievement.bonusExp
        };

        Dispatch(request);
    }

    /// <summary>
    /// Reuses the queued achievement overlay for permanent feature rewards,
    /// with its own accent and heading.
    /// </summary>
    public static void NotifyFeatureUnlock(string featureName, Sprite icon = null)
    {
        if (string.IsNullOrWhiteSpace(featureName)) featureName = "New Feature";

        PopupRequest request = new PopupRequest
        {
            title = featureName,
            detail = "Unlocked permanently",
            icon = icon,
            kind = PopupKind.Feature
        };

        Dispatch(request);
    }

    /// <summary>Shows a cosmetic-specific follow-up after its Collect panel closes.</summary>
    public static void NotifyCosmeticUnlock(string cosmeticName, Sprite icon = null)
    {
        if (string.IsNullOrWhiteSpace(cosmeticName)) cosmeticName = "New Cosmetic";

        Dispatch(new PopupRequest
        {
            title = cosmeticName,
            detail = "Added to your cosmetics",
            icon = icon,
            kind = PopupKind.Cosmetic
        });
    }

    /// <summary>Queues a passive toast for newly saved Almanac content.</summary>
    public static void NotifyAlmanacEntry(
        string entryName,
        string entryType,
        Sprite icon = null)
    {
        if (string.IsNullOrWhiteSpace(entryName)) entryName = "New Entry";
        if (string.IsNullOrWhiteSpace(entryType)) entryType = "Entry";

        Dispatch(new PopupRequest
        {
            title = entryName.Trim(),
            detail = $"{entryType.Trim()} added to Almanac",
            icon = icon,
            kind = PopupKind.Almanac
        });
    }

    /// <summary>Uses the persistent notification overlay for a guest departure.</summary>
    public static void NotifyMultiplayerGuestLeft()
    {
        Dispatch(new PopupRequest
        {
            title = "GUEST LEFT",
            detail = "Room remains open for another player",
            kind = PopupKind.Multiplayer
        });
    }

    private static void Dispatch(PopupRequest request)
    {
        if (request == null) return;

        if (Instance != null && Instance.isActiveAndEnabled && Instance.HasCompleteUIReferences())
        {
            Instance.QueueNotification(request);
        }
        else
        {
            deferredNotifications.Enqueue(request);
        }
    }

    private void QueueNotification(PopupRequest request)
    {
        if (request == null) return;

        pendingNotifications.Enqueue(request);
        if (notificationRoutine == null)
            notificationRoutine = StartCoroutine(PlayQueuedNotifications());
    }

    /// <summary>Useful for testing the popup from another script or UnityEvent.</summary>
    public void PreviewAchievement(AchievementSO achievement)
    {
        NotifyAchievement(achievement);
    }

    private IEnumerator PlayQueuedNotifications()
    {
        while (pendingNotifications.Count > 0)
        {
            // Measure the existing card invisibly before consuming the queue entry.
            // UI changes cannot drop an unlock or use up its readable duration.
            popupGroup.alpha = 0f;
            popupRoot.SetActive(true);
            hasSettledPlacement = false;
            nextPlacementRetryTime = 0f;
            while (!TryPresentPopup())
            {
                ForceAbsoluteOverlay();
                yield return null;
            }
            PopupRequest request = pendingNotifications.Dequeue();
            Populate(request);
            ForceAbsoluteOverlay();
            popupRoot.transform.SetAsLastSibling();

            yield return AnimatePopup(0f, 1f);
            yield return HoldPopup();
            yield return AnimatePopup(1f, 0f);

            popupRoot.SetActive(false);
            popupRect.anchoredPosition = SafePosition(hiddenPosition);
            yield return new WaitForSecondsRealtime(0.12f);
        }

        notificationRoutine = null;
    }

    private IEnumerator AnimatePopup(float fromAlpha, float toAlpha)
    {
        float elapsed = 0f;
        float duration = Mathf.Max(0.05f, slideDuration);
        while (elapsed < duration)
        {
            ForceAbsoluteOverlay();
            if (!popupCanvas.enabled) { yield return null; continue; }
            elapsed += Time.unscaledDeltaTime;
            float normalized = Mathf.Clamp01(elapsed / duration);
            float eased = normalized * normalized * (3f - 2f * normalized);
            // Fade at the settled clear position: a slide across the HUD would
            // temporarily cover controls even though its final position is safe.
            popupGroup.alpha = Mathf.LerpUnclamped(fromAlpha, toAlpha, eased);
            yield return null;
        }

        popupGroup.alpha = toAlpha;
    }

    private IEnumerator HoldPopup()
    {
        float elapsed = 0f;
        while (elapsed < visibleDuration)
        {
            ForceAbsoluteOverlay();
            if (popupCanvas.enabled) elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    private static bool PriorityUIVisible =>
        DialogueManager.IsAnyDialogueVisible || LoadingScreenManager.IsLoading ||
        UIReservedRegionLayout.IsLayoutTransitioning || SessionChatUI.IsOpen || TouchScreenKeyboard.visible ||
        MultiplayerChallengeLobbyUI.IsSubmissionConfirmationOpen ||
        MultiplayerChallengeLobbyUI.IsLeaveConfirmationOpen ||
        (UIPanelCoordinator.Instance != null && UIPanelCoordinator.Instance.HasOpenPanel) ||
        (PauseManager.Instance != null && PauseManager.Instance.isPaused) ||
        (LessonUIManager.Instance != null && LessonUIManager.Instance.IsOpen) ||
        (GameManager.Instance != null && GameManager.Instance.IsTransitioning) ||
        (FusionConnectionManager.Instance != null && FusionConnectionManager.Instance.IsNetworkSceneLoading) ||
        (ClipboardManager.Instance != null && ClipboardManager.Instance.overrideConfirmPanel != null &&
         ClipboardManager.Instance.overrideConfirmPanel.activeInHierarchy);

    private bool TryPresentPopup()
    {
        if (PriorityUIVisible || popupRoot == null || !popupRoot.activeSelf ||
            popupRect == null || popupCanvas == null) return false;
        // A busy build HUD can leave the top occupied for a long time. Retry
        // that search at 8 Hz, while priority changes still suppress immediately.
        if (Time.unscaledTime < nextPlacementRetryTime) return false;

        float scale = Mathf.Max(.01f, popupCanvas.scaleFactor);
        Rect safe = Screen.safeArea;
        if (safe.width <= 0f || safe.height <= 0f)
            safe = new Rect(0f, 0f, Screen.width, Screen.height);
        if (preferredScreenWidth != Screen.width || preferredScreenHeight != Screen.height ||
            preferredSafeArea != safe || preferredPopupBounds != popupRect.rect ||
            !Mathf.Approximately(preferredCanvasScale, scale))
        {
            bool measured = UIReservedRegionLayout.TryGetScreenRect(popupRect, out Rect bounds);
            if (!measured) return false;
            // Older scene/prefab cards are bottom-anchored. Position from measured
            // screen bounds instead, so they still appear at the top without
            // rewriting authored anchors or saving scenes.
            float topInset = Mathf.Max(screenEdgeMargin, Mathf.Abs(visiblePosition.y)) * scale;
            preferredScreenCenter = new Vector2(safe.center.x + visiblePosition.x * scale,
                safe.yMax - topInset - bounds.height * .5f);
            preferredScreenWidth = Screen.width;
            preferredScreenHeight = Screen.height;
            preferredSafeArea = safe;
            preferredPopupBounds = popupRect.rect;
            preferredCanvasScale = scale;
        }

        if (hasSettledPlacement) popupRect.anchoredPosition = settledPosition;
        if (!UIReservedRegionLayout.TryFindTopEdgePlacement(popupRect, preferredScreenCenter,
                hasSettledPlacement, screenEdgeMargin * scale, protectedPanelGap * scale,
                out Vector2 center) || !UIReservedRegionLayout.TrySetScreenCenter(popupRect, center))
        {
            nextPlacementRetryTime = Time.unscaledTime + .12f;
            return false;
        }
        settledPosition = popupRect.anchoredPosition;
        hasSettledPlacement = true;
        return true;
    }

    private void ResetPlacementRetry() => nextPlacementRetryTime = 0f;

    private Vector2 SafePosition(Vector2 position)
    {
        float scale = popupCanvas != null ? Mathf.Max(.01f, popupCanvas.scaleFactor) : 1f;
        Rect safe = Screen.safeArea;
        position.x += (safe.center.x - Screen.width * .5f) / scale;
        if (position.y >= 0f) position.y += Mathf.Max(0f, safe.yMin) / scale;
        return position;
    }

    /// <summary>
    /// Reasserted immediately before Canvas rendering and every visible frame.
    /// Priority state and actual panel/control bounds are rechecked at render time
    /// too, so a panel opened later in this frame still takes precedence.
    /// </summary>
    private void ForceAbsoluteOverlay()
    {
        if (popupCanvas == null) popupCanvas = GetComponent<Canvas>();
        if (popupCanvas == null) return;

        popupCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        popupCanvas.worldCamera = null;
        popupCanvas.overrideSorting = true;
        popupCanvas.sortingLayerID = topSortingLayerID;
        popupCanvas.sortingOrder = AbsoluteSortingOrder;

        // Some scene instances were accidentally saved at scale zero. Because
        // this canvas persists between scenes, one bad source scene would make
        // every later notification run correctly but remain invisible.
        transform.localScale = Vector3.one;
        popupCanvas.enabled = TryPresentPopup();

        if (popupGroup != null)
        {
            popupGroup.ignoreParentGroups = true;
            popupGroup.interactable = false;
            popupGroup.blocksRaycasts = false;
        }

        transform.SetAsLastSibling();
        if (popupRoot != null && popupRoot.activeSelf)
            popupRoot.transform.SetAsLastSibling();
    }

    private static int FindTopSortingLayerID()
    {
        SortingLayer[] layers = SortingLayer.layers;
        int bestLayerID = 0;
        int bestLayerValue = int.MinValue;
        foreach (SortingLayer layer in layers)
        {
            int layerValue = SortingLayer.GetLayerValueFromID(layer.id);
            if (layerValue <= bestLayerValue) continue;
            bestLayerValue = layerValue;
            bestLayerID = layer.id;
        }
        return bestLayerID;
    }

    private void Populate(PopupRequest request)
    {
        ApplyStyle(request.kind);
        achievementNameText.text = request.title;
        EnsureRewardIcons();
        if (request.kind == PopupKind.Achievement)
            ApplyAchievementRewardLayout(request);
        else
            ApplyStandardDetailLayout(request.detail);

        if (iconImage != null)
        {
            iconImage.sprite = request.icon;
            iconImage.enabled = iconImage.sprite != null;
            iconImage.preserveAspect = true;
            iconImage.color = Color.white;
        }
    }

    private void ApplyStyle(PopupKind kind)
    {
        bool isFeatureUnlock = kind == PopupKind.Feature;
        bool isCosmeticUnlock = kind == PopupKind.Cosmetic;
        bool isAlmanacUpdate = kind == PopupKind.Almanac;
        bool isMultiplayer = kind == PopupKind.Multiplayer;
        Color selectedBackground = isAlmanacUpdate
            ? almanacBackgroundColor
            : isCosmeticUnlock
            ? cosmeticBackgroundColor
            : isFeatureUnlock || isMultiplayer ? featureBackgroundColor : backgroundColor;
        Color selectedAccent = isAlmanacUpdate
            ? almanacAccentColor
            : isCosmeticUnlock
            ? cosmeticAccentColor
            : isFeatureUnlock || isMultiplayer ? featureAccentColor : accentColor;
        Color selectedPrimary = isAlmanacUpdate
            ? almanacPrimaryTextColor
            : isCosmeticUnlock
            ? cosmeticPrimaryTextColor
            : isFeatureUnlock || isMultiplayer ? featurePrimaryTextColor : primaryTextColor;
        Color selectedSecondary = isAlmanacUpdate
            ? almanacSecondaryTextColor
            : isCosmeticUnlock
            ? cosmeticSecondaryTextColor
            : isFeatureUnlock || isMultiplayer ? featureSecondaryTextColor : secondaryTextColor;

        if (backgroundImage != null) backgroundImage.color = selectedBackground;
        if (accentImage != null) accentImage.color = selectedAccent;
        if (popupOutline != null) popupOutline.effectColor = primaryTextColor;
        if (headingText != null)
        {
            headingText.text = isAlmanacUpdate
                ? "ALMANAC UPDATED"
                : isCosmeticUnlock
                ? "COSMETIC UNLOCKED"
                : isMultiplayer
                ? "MULTIPLAYER"
                : isFeatureUnlock ? "FEATURE UNLOCKED" : "ACHIEVEMENT UNLOCKED";
            headingText.color = selectedAccent;
        }
        if (achievementNameText != null) achievementNameText.color = selectedPrimary;
        if (rewardText != null) rewardText.color = selectedSecondary;
    }

    private void ResolveVisualReferences()
    {
        if (popupRoot == null) return;

        if (backgroundImage == null) backgroundImage = popupRoot.GetComponent<Image>();
        if (popupOutline == null) popupOutline = popupRoot.GetComponent<Outline>();

        if (accentImage == null)
        {
            Transform accent = FindDescendant(popupRoot.transform, "GoldAccent");
            if (accent != null) accentImage = accent.GetComponent<Image>();
        }

        if (headingText == null)
        {
            Transform heading = FindDescendant(popupRoot.transform, "Heading");
            if (heading != null) headingText = heading.GetComponent<TMP_Text>();
        }
    }

    private static Transform FindDescendant(Transform parent, string objectName)
    {
        if (parent == null) return null;
        foreach (Transform child in parent)
        {
            if (child.name == objectName) return child;
            Transform nested = FindDescendant(child, objectName);
            if (nested != null) return nested;
        }
        return null;
    }

    private static string BuildRewardText(AchievementSO achievement)
    {
        if (achievement.bonusGold > 0 && achievement.bonusExp > 0)
            return $"Reward:       {achievement.bonusGold:N0}          {achievement.bonusExp:N0}";
        if (achievement.bonusGold > 0)
            return $"Reward:       {achievement.bonusGold:N0}";
        if (achievement.bonusExp > 0)
            return $"Reward:       {achievement.bonusExp:N0}";
        return "Achievement completed";
    }

    private void EnsureRewardIcons()
    {
        if (popupRoot == null) return;
        // Reward graphics are authored with the panel, never created while playing.
        if (rewardCoinIcon == null) rewardCoinIcon = FindPopupComponent<Image>("RewardCoinIcon");
        if (rewardExperienceIcon == null) rewardExperienceIcon = FindPopupComponent<Image>("RewardExperienceIcon");
        if (rewardCoinValueText == null) rewardCoinValueText = FindPopupComponent<TMP_Text>("RewardCoinValue");
        if (rewardExperienceValueText == null) rewardExperienceValueText = FindPopupComponent<TMP_Text>("RewardExperienceValue");
        if (rewardCoinIcon != null && rewardCoinIcon.sprite == null) rewardCoinIcon.sprite = CurrencyIconCatalog.Get(CurrencyIconKind.Coin);
        if (rewardExperienceIcon != null && rewardExperienceIcon.sprite == null) rewardExperienceIcon.sprite = CurrencyIconCatalog.Get(CurrencyIconKind.Experience);
    }

    private T FindPopupComponent<T>(string name) where T : Component
    {
        Transform child = popupRoot.transform.Find(name);
        return child != null ? child.GetComponent<T>() : null;
    }

    private void ApplyAchievementRewardLayout(PopupRequest request)
    {
        bool showGold = request.goldReward > 0;
        bool showExperience = request.experienceReward > 0;
        bool hasReward = showGold || showExperience;

        ConfigurePopupTextRect(
            rewardText,
            new Vector2(158f, -111f),
            new Vector2(hasReward ? 78f : 570f, 30f));
        rewardText.text = hasReward ? "Reward" : "Achievement completed";

        if (rewardCoinIcon != null)
        {
            rewardCoinIcon.rectTransform.anchoredPosition = new Vector2(250f, -44f);
            rewardCoinIcon.gameObject.SetActive(showGold && rewardCoinIcon.sprite != null);
        }
        if (rewardCoinValueText != null)
        {
            rewardCoinValueText.text = request.goldReward.ToString("N0");
            rewardCoinValueText.color = rewardText.color;
            rewardCoinValueText.gameObject.SetActive(showGold);
        }

        float experienceIconX = showGold ? 390f : 250f;
        float experienceValueX = showGold ? 408f : 268f;
        if (rewardExperienceIcon != null)
        {
            rewardExperienceIcon.rectTransform.anchoredPosition = new Vector2(experienceIconX, -44f);
            rewardExperienceIcon.gameObject.SetActive(
                showExperience && rewardExperienceIcon.sprite != null);
        }
        if (rewardExperienceValueText != null)
        {
            ConfigurePopupTextRect(
                rewardExperienceValueText,
                new Vector2(experienceValueX, -111f),
                new Vector2(110f, 30f));
            rewardExperienceValueText.text = request.experienceReward.ToString("N0");
            rewardExperienceValueText.color = rewardText.color;
            rewardExperienceValueText.gameObject.SetActive(showExperience);
        }
    }

    private void ApplyStandardDetailLayout(string detail)
    {
        ConfigurePopupTextRect(
            rewardText,
            new Vector2(158f, -111f),
            new Vector2(570f, 30f));
        rewardText.text = detail;
        if (rewardCoinIcon != null) rewardCoinIcon.gameObject.SetActive(false);
        if (rewardExperienceIcon != null) rewardExperienceIcon.gameObject.SetActive(false);
        if (rewardCoinValueText != null) rewardCoinValueText.gameObject.SetActive(false);
        if (rewardExperienceValueText != null) rewardExperienceValueText.gameObject.SetActive(false);
    }

    private static void ConfigurePopupTextRect(TMP_Text text, Vector2 topLeft, Vector2 size)
    {
        if (text == null) return;
        RectTransform rect = text.rectTransform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = topLeft;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
    }

    private bool HasCompleteUIReferences()
    {
        return popupRoot != null && popupRect != null && popupGroup != null &&
               iconImage != null && achievementNameText != null && rewardText != null;
    }
}
