using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Global, queued achievement toast. It builds a lightweight overlay at runtime,
/// so every scene receives notifications without duplicating scene UI setup.
/// </summary>
[DisallowMultipleComponent]
public sealed class AchievementPopupNotification : MonoBehaviour
{
    private enum PopupKind
    {
        Achievement,
        Feature,
        Cosmetic,
        Almanac
    }

    private sealed class PopupRequest
    {
        public string title;
        public string detail;
        public Sprite icon;
        public PopupKind kind;
    }

    private const int AbsoluteSortingOrder = 32767;
    public static AchievementPopupNotification Instance { get; private set; }
    private static readonly Queue<PopupRequest> deferredNotifications = new Queue<PopupRequest>();

    [Header("Timing")]
    [Min(0.05f)] [SerializeField] private float slideDuration = 0.35f;
    [Min(0.25f)] [SerializeField] private float visibleDuration = 3.5f;

    [Header("Position")]
    [SerializeField] private Vector2 visiblePosition = new Vector2(0f, -36f);
    [SerializeField] private Vector2 hiddenPosition = new Vector2(0f, 190f);

    [Header("Achievement Colors")]
    [SerializeField] private Color backgroundColor = new Color(0.18f, 0.12f, 0.10f, 0.97f);
    [SerializeField] private Color accentColor = new Color(0.93f, 0.66f, 0.24f, 1f);
    [SerializeField] private Color primaryTextColor = new Color(1f, 0.94f, 0.78f, 1f);
    [SerializeField] private Color secondaryTextColor = new Color(0.88f, 0.82f, 0.70f, 1f);

    [Header("Feature Unlock Colors")]
    [SerializeField] private Color featureBackgroundColor = new Color(0.08f, 0.20f, 0.22f, 0.97f);
    [SerializeField] private Color featureAccentColor = new Color(0.27f, 0.84f, 0.72f, 1f);
    [SerializeField] private Color featurePrimaryTextColor = new Color(0.88f, 1f, 0.96f, 1f);
    [SerializeField] private Color featureSecondaryTextColor = new Color(0.67f, 0.91f, 0.85f, 1f);

    [Header("Cosmetic Unlock Colors")]
    [SerializeField] private Color cosmeticBackgroundColor = new Color(0.19f, 0.11f, 0.24f, 0.97f);
    [SerializeField] private Color cosmeticAccentColor = new Color(0.79f, 0.52f, 0.96f, 1f);
    [SerializeField] private Color cosmeticPrimaryTextColor = new Color(0.98f, 0.92f, 1f, 1f);
    [SerializeField] private Color cosmeticSecondaryTextColor = new Color(0.86f, 0.75f, 0.92f, 1f);

    [Header("Almanac Update Colors")]
    [SerializeField] private Color almanacBackgroundColor = new Color(0.20f, 0.14f, 0.08f, 0.97f);
    [SerializeField] private Color almanacAccentColor = new Color(0.88f, 0.61f, 0.25f, 1f);
    [SerializeField] private Color almanacPrimaryTextColor = new Color(1f, 0.95f, 0.82f, 1f);
    [SerializeField] private Color almanacSecondaryTextColor = new Color(0.91f, 0.82f, 0.65f, 1f);

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
        while (deferredNotifications.Count > 0)
            QueueNotification(deferredNotifications.Dequeue());
    }

    private void OnEnable()
    {
        Canvas.willRenderCanvases -= ForceAbsoluteOverlay;
        Canvas.willRenderCanvases += ForceAbsoluteOverlay;
    }

    private void OnDisable()
    {
        Canvas.willRenderCanvases -= ForceAbsoluteOverlay;
    }

    private void OnDestroy()
    {
        Canvas.willRenderCanvases -= ForceAbsoluteOverlay;
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
            kind = PopupKind.Achievement
        };

        Dispatch(request);
    }

    /// <summary>
    /// Reuses the guaranteed achievement overlay for permanent feature rewards,
    /// while applying a distinct teal presentation and heading.
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

    /// <summary>Shows a guaranteed top-most toast for newly saved Almanac content.</summary>
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
            PopupRequest request = pendingNotifications.Dequeue();
            Populate(request);
            ForceAbsoluteOverlay();
            popupRoot.SetActive(true);
            popupRoot.transform.SetAsLastSibling();
            popupGroup.alpha = 0f;
            popupRect.anchoredPosition = hiddenPosition;

            yield return AnimatePopup(hiddenPosition, visiblePosition, 0f, 1f);
            yield return new WaitForSecondsRealtime(visibleDuration);
            yield return AnimatePopup(visiblePosition, hiddenPosition, 1f, 0f);

            popupRoot.SetActive(false);
            yield return new WaitForSecondsRealtime(0.12f);
        }

        notificationRoutine = null;
    }

    private IEnumerator AnimatePopup(Vector2 from, Vector2 to, float fromAlpha, float toAlpha)
    {
        float elapsed = 0f;
        float duration = Mathf.Max(0.05f, slideDuration);
        while (elapsed < duration)
        {
            ForceAbsoluteOverlay();
            elapsed += Time.unscaledDeltaTime;
            float normalized = Mathf.Clamp01(elapsed / duration);
            float eased = normalized * normalized * (3f - 2f * normalized);
            popupRect.anchoredPosition = Vector2.LerpUnclamped(from, to, eased);
            popupGroup.alpha = Mathf.LerpUnclamped(fromAlpha, toAlpha, eased);
            yield return null;
        }

        popupRect.anchoredPosition = to;
        popupGroup.alpha = toAlpha;
    }

    /// <summary>
    /// Reasserted immediately before Canvas rendering and every visible frame.
    /// This protects the toast from modal managers that disable/sort canvases
    /// after the achievement was queued.
    /// </summary>
    private void ForceAbsoluteOverlay()
    {
        if (popupCanvas == null) popupCanvas = GetComponent<Canvas>();
        if (popupCanvas == null) return;

        popupCanvas.enabled = true;
        popupCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        popupCanvas.worldCamera = null;
        popupCanvas.overrideSorting = true;
        popupCanvas.sortingLayerID = topSortingLayerID;
        popupCanvas.sortingOrder = AbsoluteSortingOrder;

        // Some scene instances were accidentally saved at scale zero. Because
        // this canvas persists between scenes, one bad source scene would make
        // every later notification run correctly but remain invisible.
        transform.localScale = Vector3.one;

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
        rewardText.text = request.detail;

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
        Color selectedBackground = isAlmanacUpdate
            ? almanacBackgroundColor
            : isCosmeticUnlock
            ? cosmeticBackgroundColor
            : isFeatureUnlock ? featureBackgroundColor : backgroundColor;
        Color selectedAccent = isAlmanacUpdate
            ? almanacAccentColor
            : isCosmeticUnlock
            ? cosmeticAccentColor
            : isFeatureUnlock ? featureAccentColor : accentColor;
        Color selectedPrimary = isAlmanacUpdate
            ? almanacPrimaryTextColor
            : isCosmeticUnlock
            ? cosmeticPrimaryTextColor
            : isFeatureUnlock ? featurePrimaryTextColor : primaryTextColor;
        Color selectedSecondary = isAlmanacUpdate
            ? almanacSecondaryTextColor
            : isCosmeticUnlock
            ? cosmeticSecondaryTextColor
            : isFeatureUnlock ? featureSecondaryTextColor : secondaryTextColor;

        if (backgroundImage != null) backgroundImage.color = selectedBackground;
        if (accentImage != null) accentImage.color = selectedAccent;
        if (popupOutline != null) popupOutline.effectColor = selectedAccent;
        if (headingText != null)
        {
            headingText.text = isAlmanacUpdate
                ? "ALMANAC UPDATED"
                : isCosmeticUnlock
                ? "COSMETIC UNLOCKED"
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
            return $"Reward: ₱{achievement.bonusGold}  •  {achievement.bonusExp} EXP";
        if (achievement.bonusGold > 0)
            return $"Reward: ₱{achievement.bonusGold}";
        if (achievement.bonusExp > 0)
            return $"Reward: {achievement.bonusExp} EXP";
        return "Achievement completed";
    }

    private bool HasCompleteUIReferences()
    {
        return popupRoot != null && popupRect != null && popupGroup != null &&
               iconImage != null && achievementNameText != null && rewardText != null;
    }
}
