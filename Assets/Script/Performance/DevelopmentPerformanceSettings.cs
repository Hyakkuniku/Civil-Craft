using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using TMPro;
using UnityEngine.UI;
#endif

/// <summary>Session-only diagnostics. Release builds create no controls or overlay.</summary>
public sealed class DevelopmentPerformanceSettings : MonoBehaviour
{
    public static void Ensure(SettingsManager manager)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (manager == null || manager.settingsPanel == null || manager.shadowsToggle == null) return;
        var panel = manager.settingsPanel.GetComponent<DevelopmentPerformanceSettings>();
        if (panel == null) panel = manager.settingsPanel.AddComponent<DevelopmentPerformanceSettings>();
        panel.Initialize(manager.shadowsToggle);
#endif
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public static bool? OcclusionOverride { get; private set; }
    public static bool ShowFps { get; private set; }
    static CanyonOcclusionProfile activeProfile;
    static GameObject overlay;
    Toggle occlusionToggle, fpsToggle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetSession()
    {
        OcclusionOverride = null;
        ShowFps = false;
        // Retain surviving references when domain/scene reload is disabled.
        if (overlay != null) overlay.SetActive(false);
    }
    public static void Register(CanyonOcclusionProfile profile)
    {
        if (profile.gameObject.scene.name == CanyonOcclusionProfile.TargetSceneName) activeProfile = profile;
    }
    public static void Unregister(CanyonOcclusionProfile profile)
    {
        if (activeProfile == profile) activeProfile = null;
    }
    public static string OcclusionStatus
    {
        get
        {
            if (activeProfile == null) return "Occlusion: CanyonCrossing only";
            if (!activeProfile.OcclusionRequested) return "Occlusion: OFF";
            if (!activeProfile.bakeVerified || activeProfile.bakedData == null) return "Occlusion: bake unavailable";
            Camera camera = activeProfile.gameplayCamera;
            if (camera == null) return "Occlusion: camera unavailable";
            if (!activeProfile.ContainsCameraPosition(camera.transform.position)) return "Occlusion: outside baked area";
            return camera.useOcclusionCulling ? "Occlusion: ACTIVE" : "Occlusion: suspended";
        }
    }
    public void Initialize(Toggle template)
    {
        if (occlusionToggle == null)
        {
            occlusionToggle = CloneRow(template, "DevelopmentOcclusion", "Test Occlusion (Dev)");
            occlusionToggle.onValueChanged.AddListener(SetOcclusion);
            fpsToggle = CloneRow(template, "DevelopmentFps", "Show FPS Counter (Dev)");
            fpsToggle.onValueChanged.AddListener(SetFps);
            TMP_Text label = template.transform.parent.GetComponentInChildren<TMP_Text>(true);
            if (Application.isPlaying) CreateOverlay(label != null ? label.font : TMP_Settings.defaultFontAsset);
            CozyModalLayout layout = GetComponent<CozyModalLayout>();
            if (layout != null) layout.ApplyLayout();
        }
        RefreshControls();
    }
    void OnEnable() => RefreshControls();
    void RefreshControls()
    {
        if (occlusionToggle != null) occlusionToggle.SetIsOnWithoutNotify(OcclusionOverride ?? (activeProfile != null && activeProfile.enableGameplayOcclusion));
        if (fpsToggle != null) fpsToggle.SetIsOnWithoutNotify(ShowFps);
    }
    static void SetOcclusion(bool enabled)
    {
        OcclusionOverride = enabled;
        if (activeProfile != null && activeProfile.gameplayCamera != null &&
            activeProfile.gameplayCamera.TryGetComponent(out CanyonCameraOcclusionPolicy policy)) policy.RefreshPolicy();
    }
    static void SetFps(bool enabled)
    {
        ShowFps = enabled;
        if (overlay != null) overlay.SetActive(enabled);
    }
    static Toggle CloneRow(Toggle source, string name, string caption)
    {
        GameObject row = Instantiate(source.transform.parent.gameObject, source.transform.parent.parent, false);
        row.name = name + "Row";
        Toggle toggle = row.GetComponentInChildren<Toggle>(true);
        toggle.name = name + "Toggle";
        toggle.onValueChanged = new Toggle.ToggleEvent(); // Never inherit the shadows callback.
        toggle.group = null;
        toggle.SetIsOnWithoutNotify(false);
        toggle.interactable = true;
        foreach (TMP_Text text in row.GetComponentsInChildren<TMP_Text>(true))
            if (text.name == "Label") { text.text = caption; text.richText = false; }
        row.SetActive(true);
        return toggle;
    }
    static void CreateOverlay(TMP_FontAsset font)
    {
        if (overlay != null) return;
        overlay = new GameObject("Development Performance Overlay", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        overlay.SetActive(false);
        DontDestroyOnLoad(overlay);
        Canvas canvas = overlay.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;
        CanvasScaler scaler = overlay.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 1f;
        CanvasGroup group = overlay.GetComponent<CanvasGroup>();
        group.interactable = false; group.blocksRaycasts = false;
        RectTransform safe = NewRect("Safe Area", overlay.transform);
        RectTransform badge = NewRect("Counter", safe);
        badge.anchorMin = badge.anchorMax = badge.pivot = new Vector2(.5f, 1f);
        badge.anchoredPosition = new Vector2(0f, -12f);
        badge.sizeDelta = new Vector2(450f, 76f);
        Image background = badge.gameObject.AddComponent<Image>();
        background.color = new Color32(45, 33, 25, 220); background.raycastTarget = false;
        RectTransform textRect = NewRect("FPS", badge);
        textRect.offsetMin = new Vector2(10, 4); textRect.offsetMax = new Vector2(-10, -4);
        TextMeshProUGUI text = textRect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font; text.fontSize = 26; text.alignment = TextAlignmentOptions.Center;
        text.color = new Color32(255, 249, 238, 255); text.raycastTarget = false; text.richText = false;
        FPSViewer viewer = textRect.gameObject.AddComponent<FPSViewer>();
        viewer.fpsText = text; viewer.developmentOverlay = true; viewer.safeAreaRoot = safe; viewer.updateInterval = .5f;
        overlay.SetActive(ShowFps);
    }
    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return rect;
    }
#endif
}
