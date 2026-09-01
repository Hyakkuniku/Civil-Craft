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
    private const int AbsoluteSortingOrder = 32767;
    public static AchievementPopupNotification Instance { get; private set; }
    private static readonly Queue<AchievementSO> deferredAchievements = new Queue<AchievementSO>();

    [Header("Timing")]
    [Min(0.05f)] [SerializeField] private float slideDuration = 0.35f;
    [Min(0.25f)] [SerializeField] private float visibleDuration = 3.5f;

    [Header("Position")]
    [SerializeField] private Vector2 visiblePosition = new Vector2(0f, -36f);
    [SerializeField] private Vector2 hiddenPosition = new Vector2(0f, 190f);

    [Header("Colors")]
    [SerializeField] private Color backgroundColor = new Color(0.18f, 0.12f, 0.10f, 0.97f);
    [SerializeField] private Color accentColor = new Color(0.93f, 0.66f, 0.24f, 1f);
    [SerializeField] private Color primaryTextColor = new Color(1f, 0.94f, 0.78f, 1f);
    [SerializeField] private Color secondaryTextColor = new Color(0.88f, 0.82f, 0.70f, 1f);

    private readonly Queue<AchievementSO> pendingAchievements = new Queue<AchievementSO>();
    private Coroutine notificationRoutine;
    private Canvas popupCanvas;
    private int topSortingLayerID;

    [Header("Scene UI References")]
    [Tooltip("Assigned by the scene setup. The popup child starts inactive.")]
    [SerializeField] private GameObject popupRoot;
    [SerializeField] private RectTransform popupRect;
    [SerializeField] private CanvasGroup popupGroup;
    [SerializeField] private Image iconImage;
    [SerializeField] private TMP_Text achievementNameText;
    [SerializeField] private TMP_Text rewardText;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        Instance = null;
        deferredAchievements.Clear();
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
        while (deferredAchievements.Count > 0)
            QueueAchievement(deferredAchievements.Dequeue());
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

        if (Instance != null && Instance.isActiveAndEnabled && Instance.HasCompleteUIReferences())
        {
            Instance.QueueAchievement(achievement);
        }
        else
        {
            deferredAchievements.Enqueue(achievement);
        }
    }

    private void QueueAchievement(AchievementSO achievement)
    {
        if (achievement == null) return;

        pendingAchievements.Enqueue(achievement);
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
        while (pendingAchievements.Count > 0)
        {
            AchievementSO achievement = pendingAchievements.Dequeue();
            Populate(achievement);
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

    private void Populate(AchievementSO achievement)
    {
        achievementNameText.text = achievement.achievementName;
        rewardText.text = BuildRewardText(achievement);

        if (iconImage != null)
        {
            iconImage.sprite = achievement.achievementIcon;
            iconImage.enabled = iconImage.sprite != null;
            iconImage.preserveAspect = true;
        }
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
