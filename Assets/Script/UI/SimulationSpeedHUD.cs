using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Solo simulation playback controls. The physics manager owns speed and its lifetime.</summary>
[DisallowMultipleComponent]
public sealed class SimulationSpeedHUD : MonoBehaviour
{
    public const string PanelName = "Simulation Playback Speed";
    public const float PanelWidth = 328f;
    public const float PanelHeight = 112f;
    private const float VisualButtonSize = 76f;
    private const float ButtonCenter = 104f;
    private const float ControlGap = 20f;

    [SerializeField] private BridgePhysicsManager physics;
    [SerializeField] private RectTransform panel;
    [SerializeField] private RectTransform simulationControls;
    [SerializeField] private Button slower;
    [SerializeField] private Button faster;
    [SerializeField] private TextMeshProUGUI speedLabel;
    [SerializeField] private Image toolbarBackground;
    [SerializeField, HideInInspector] private int toolbarStyleVersion;
    [SerializeField] private RectTransform[] protectedBudget = new RectTransform[0];
    private readonly Vector3[] corners = new Vector3[4];
    private float nextLayout;
    private bool registered;

    public BridgePhysicsManager Physics => physics;
    public RectTransform Panel => panel;
    public RectTransform SimulationControls => simulationControls;
    public Button Slower => slower;
    public Button Faster => faster;
    public TextMeshProUGUI SpeedLabel => speedLabel;
    public Image ToolbarBackground => toolbarBackground;
    public bool UsesToolbarStyle => toolbarStyleVersion == 2;

    private void Awake()
    {
        if (panel != null) panel.gameObject.SetActive(false);
        BindButtons();
    }

    private void OnEnable() { BindButtons(); nextLayout = 0f; }

    private void LateUpdate()
    {
        if (panel == null || slower == null || faster == null || speedLabel == null) return;
        bool visible = physics != null && physics.CanAdjustSimulationPlayback &&
            !DialogueManager.IsAnyDialogueActive && !QuizUIManager.IsAnyQuizActive &&
            (UIPanelCoordinator.Instance == null || !UIPanelCoordinator.Instance.HasOpenPanel) &&
            !UIReservedRegionLayout.IsModalTransitioning;
        SetVisible(visible);
        if (!visible) return;
        if (speedLabel.text != physics.SimulationPlaybackLabel) speedLabel.text = physics.SimulationPlaybackLabel;
        slower.interactable = physics.SimulationPlaybackStep > SimulationPlaybackState.MinimumStep;
        faster.interactable = physics.SimulationPlaybackStep < SimulationPlaybackState.MaximumStep;
        if (Time.unscaledTime < nextLayout) return;
        nextLayout = Time.unscaledTime + .1f;
        ApplyLayout(new Vector2(Screen.width, Screen.height), Screen.safeArea, Screen.dpi, Application.isMobilePlatform);
    }

    private void SetVisible(bool visible)
    {
        if (panel.gameObject.activeSelf != visible)
        {
            panel.gameObject.SetActive(visible);
            nextLayout = 0f;
            UIReservedRegionLayout.NotifyLayoutChanging();
        }
        if (!registered) { UIReservedRegionLayout.Register(panel); registered = true; }
    }

    private void OnDisable()
    {
        if (panel != null) panel.gameObject.SetActive(false);
        if (registered) UIReservedRegionLayout.Unregister(panel);
        registered = false;
        nextLayout = 0f;
    }

    private void OnDestroy()
    {
        if (registered) UIReservedRegionLayout.Unregister(panel);
        // The visual panel is a sibling of the authored Start/Stop controls.
        if (Application.isPlaying && panel != null) Destroy(panel.gameObject);
    }

    public void SlowDown() { if (CanClick()) physics.ChangeSimulationPlayback(-1); }
    public void FastForward() { if (CanClick()) physics.ChangeSimulationPlayback(1); }

    private bool CanClick() => physics != null && physics.CanAdjustSimulationPlayback &&
        panel != null && panel.gameObject.activeInHierarchy &&
        !DialogueManager.IsAnyDialogueActive && !QuizUIManager.IsAnyQuizActive &&
        (UIPanelCoordinator.Instance == null || !UIPanelCoordinator.Instance.HasOpenPanel) &&
        !UIReservedRegionLayout.IsModalTransitioning;

    private void BindButtons()
    {
        if (!Application.isPlaying) return;
        Bind(slower, nameof(SlowDown), SlowDown);
        Bind(faster, nameof(FastForward), FastForward);
    }

    private void Bind(Button button, string method, UnityEngine.Events.UnityAction action)
    {
        if (button == null) return;
        button.onClick.RemoveListener(action);
        for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
            if (button.onClick.GetPersistentTarget(i) == this && button.onClick.GetPersistentMethodName(i) == method) return;
        button.onClick.AddListener(action);
    }

    /// <summary>Uses cached authored references, never a scene search. Also used by detached layout checks.</summary>
    public void ApplyLayout(Vector2 screenSize, Rect safeArea, float dpi, bool mobile)
    {
        if (panel == null || !(panel.parent is RectTransform parent)) return;
        Canvas canvas = panel.GetComponentInParent<Canvas>(true);
        if (canvas == null) return;
        Vector2 previousPosition = panel.anchoredPosition, previousSize = panel.sizeDelta;
        float scale = Mathf.Max(.001f, canvas.scaleFactor);
        CanvasScaler scaler = canvas.rootCanvas != null ? canvas.rootCanvas.GetComponent<CanvasScaler>() : null;
        if (canvas.renderMode != RenderMode.WorldSpace && scaler != null && scaler.enabled &&
            scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize)
        {
            float width = screenSize.x / Mathf.Max(1f, scaler.referenceResolution.x);
            float height = screenSize.y / Mathf.Max(1f, scaler.referenceResolution.y);
            scale = scaler.screenMatchMode == CanvasScaler.ScreenMatchMode.Expand ? Mathf.Min(width, height) :
                scaler.screenMatchMode == CanvasScaler.ScreenMatchMode.Shrink ? Mathf.Max(width, height) :
                Mathf.Pow(2f, Mathf.Lerp(Mathf.Log(Mathf.Max(.001f, width), 2f),
                    Mathf.Log(Mathf.Max(.001f, height), 2f), scaler.matchWidthOrHeight));
        }
        if (safeArea.width <= 0f || safeArea.height <= 0f) safeArea = new Rect(Vector2.zero, screenSize);
        // On a detached world canvas, its logical size explicitly represents the target screen.
        if (canvas.renderMode == RenderMode.WorldSpace && parent.rect.width > 0f)
            scale = screenSize.x / parent.rect.width;
        float touch = GameplayPauseButtonLayout.TouchSize(scale, screenSize, dpi, mobile);
        float arrowSize = Mathf.Max(VisualButtonSize, touch);
        float widthNeeded = Mathf.Max(PanelWidth, arrowSize * 2f + 88f);
        float arrowCenter = (widthNeeded - arrowSize) * .5f;
        float halfWidth = widthNeeded * .5f;
        float halfHeight = Mathf.Max(PanelHeight, arrowSize) * .5f;
        panel.anchorMin = panel.anchorMax = new Vector2(0f, 1f);
        panel.pivot = Vector2.one * .5f;
        panel.localScale = Vector3.one;
        panel.localRotation = Quaternion.identity;
        panel.sizeDelta = new Vector2(widthNeeded, halfHeight * 2f);
        LayoutArrow(slower, -arrowCenter, arrowSize);
        LayoutArrow(faster, arrowCenter, arrowSize);

        Rect bounds = BoundsInParent(simulationControls, parent);
        Rect safe = parent.rect;
        safe.xMin += (safeArea.xMin + 16f) / scale;
        safe.xMax -= (screenSize.x - safeArea.xMax + 16f) / scale;
        safe.yMax -= (screenSize.y - safeArea.yMax + 16f) / scale;
        safe.yMin += (safeArea.yMin + 16f) / scale;
        Vector2 center = simulationControls != null
            ? new Vector2(bounds.xMin - ControlGap - halfWidth, bounds.center.y)
            : new Vector2(safe.xMax - halfWidth, safe.yMax - halfHeight);
        center.x = Mathf.Clamp(center.x, safe.xMin + halfWidth, safe.xMax - halfWidth);
        center.y = Mathf.Clamp(center.y, safe.yMin + halfHeight, safe.yMax - halfHeight);
        Rect envelope = Centered(center, halfWidth, halfHeight);
        bool needsSecondRow = simulationControls != null && envelope.Overlaps(bounds);
        foreach (RectTransform budget in protectedBudget)
            if (budget != null && budget.gameObject.activeInHierarchy &&
                envelope.Overlaps(Expand(BoundsInParent(budget, parent), 12f))) needsSecondRow = true;
        // Narrow tablets retain the same right-hand control cluster on a second row.
        if (needsSecondRow && simulationControls != null)
        {
            center.x = Mathf.Clamp(bounds.xMax - halfWidth, safe.xMin + halfWidth, safe.xMax - halfWidth);
            center.y = Mathf.Clamp(bounds.yMin - ControlGap - halfHeight, safe.yMin + halfHeight, safe.yMax - halfHeight);
        }
        panel.anchoredPosition = new Vector2(center.x - parent.rect.xMin, center.y - parent.rect.yMax);
        if (Application.isPlaying && (panel.anchoredPosition != previousPosition || panel.sizeDelta != previousSize))
            UIReservedRegionLayout.NotifyLayoutChanging();
    }

    private static void LayoutArrow(Button button, float center, float touch)
    {
        if (button == null) return;
        RectTransform rect = (RectTransform)button.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
        rect.anchoredPosition = new Vector2(center, 0f);
        rect.sizeDelta = Vector2.one * VisualButtonSize;
        Graphic image = button.GetComponent<Image>();
        if (image != null) image.raycastPadding = Vector4.one * (-(touch - VisualButtonSize) * .5f);
    }

    private Rect BoundsInParent(RectTransform target, RectTransform parent)
    {
        if (target == null) return new Rect();
        target.GetWorldCorners(corners);
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
        for (int i = 0; i < corners.Length; i++)
        {
            Vector3 point = parent.InverseTransformPoint(corners[i]);
            min = Vector2.Min(min, point); max = Vector2.Max(max, point);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private static Rect Centered(Vector2 center, float halfWidth, float halfHeight) =>
        new Rect(center.x - halfWidth, center.y - halfHeight, halfWidth * 2f, halfHeight * 2f);
    private static Rect Expand(Rect rect, float amount) =>
        Rect.MinMaxRect(rect.xMin - amount, rect.yMin - amount, rect.xMax + amount, rect.yMax + amount);

    public static SimulationSpeedHUD Ensure(BuildUIController controller, TMP_FontAsset fallbackFont = null, Sprite background = null)
    {
        if (controller == null) return null;
        SimulationSpeedHUD hud = controller.GetComponent<SimulationSpeedHUD>();
        if (hud == null) hud = controller.gameObject.AddComponent<SimulationSpeedHUD>();
        TMP_Text text = controller.stressText != null ? controller.stressText : controller.actionLogText;
        Canvas canvas = controller.playPauseButtonImage != null ? controller.playPauseButtonImage.GetComponentInParent<Canvas>(true) :
            text != null ? text.GetComponentInParent<Canvas>(true) : null;
        Transform parent = canvas != null ? canvas.transform : controller.transform;
        if (hud.panel == null)
        {
            TMP_FontAsset font = text != null && text.font != null ? text.font : fallbackFont != null ? fallbackFont : TMP_Settings.defaultFontAsset;
            if (font == null) return hud;
            hud.CreatePanel(parent, font, background);
        }
        else if (canvas != null && hud.panel.parent != parent) hud.panel.SetParent(parent, false);
        hud.physics = controller.physicsManager;
        hud.simulationControls = controller.simulationControlsPanel != null ? controller.simulationControlsPanel.transform as RectTransform :
            FindControlGroup(controller.playPauseButtonImage);
        hud.protectedBudget = new[] { controller.budgetFillBar != null ? controller.budgetFillBar.rectTransform.parent as RectTransform : null,
            controller.usedBudgetText != null ? controller.usedBudgetText.rectTransform : null,
            controller.maxBudgetText != null ? controller.maxBudgetText.rectTransform : null };
        hud.ApplyToolbarStyle(controller, background);
        hud.BindButtons();
        if (!Application.isPlaying) hud.panel.gameObject.SetActive(false);
        return hud;
    }

    private static RectTransform FindControlGroup(Image playImage)
    {
        if (playImage == null) return null;
        for (Transform parent = playImage.transform.parent; parent != null && parent.GetComponent<Canvas>() == null; parent = parent.parent)
            if (parent.GetComponent<Image>() != null || parent.GetComponent<CanvasGroup>() != null) return parent as RectTransform;
        return playImage.rectTransform;
    }

    private void CreatePanel(Transform parent, TMP_FontAsset font, Sprite background)
    {
        panel = NewRect(PanelName, parent);
        panel.sizeDelta = new Vector2(PanelWidth, PanelHeight);
        Image backdrop = panel.gameObject.AddComponent<Image>();
        backdrop.sprite = background;
        backdrop.type = Image.Type.Sliced;
        backdrop.color = Color.clear;
        backdrop.raycastTarget = false;
        slower = CreateButton("Slower", "<<", -ButtonCenter, font, background);
        faster = CreateButton("Faster", ">>", ButtonCenter, font, background);
        speedLabel = CreateText("Speed", panel, "0x", font, 34f);
        speedLabel.rectTransform.sizeDelta = new Vector2(118f, 80f);
        speedLabel.color = Color.white;
        panel.gameObject.SetActive(false);
    }

    /// <summary>Reuse the actual build toolbar's skin for authored and repaired controls.</summary>
    public void ApplyToolbarStyle(BuildUIController controller, Sprite fallbackBackground = null)
    {
        if (panel == null || slower == null || faster == null || speedLabel == null) return;
        RectTransform reference = controller != null && controller.simulationControlsPanel != null
            ? controller.simulationControlsPanel.transform as RectTransform
            : controller != null && controller.playPauseButtonImage != null
                ? FindControlGroup(controller.playPauseButtonImage) : simulationControls;
        Image source = reference != null ? reference.GetComponent<Image>() : null;
        Button sourceButton = controller != null ? controller.simulationButton : null;
        if (sourceButton == null && controller != null && controller.playPauseButtonImage != null)
            sourceButton = controller.playPauseButtonImage.GetComponentInParent<Button>(true);
        Color iconColor = controller != null && controller.playPauseButtonImage != null
            ? controller.playPauseButtonImage.color : Color.white;
        if (toolbarBackground == null)
        {
            Transform existing = panel.Find("Toolbar Background");
            toolbarBackground = existing != null ? existing.GetComponent<Image>() : null;
            if (toolbarBackground == null)
            {
                RectTransform rect = NewRect("Toolbar Background", panel);
                toolbarBackground = rect.gameObject.AddComponent<Image>();
            }
        }
        RectTransform background = toolbarBackground.rectTransform;
        background.SetAsFirstSibling();
        background.anchorMin = new Vector2(0f, .5f);
        background.anchorMax = new Vector2(1f, .5f);
        background.pivot = Vector2.one * .5f;
        background.anchoredPosition = Vector2.zero;
        background.sizeDelta = new Vector2(0f, reference != null && reference.rect.height > 0f
            ? reference.rect.height : PanelHeight);
        toolbarBackground.sprite = source != null ? source.sprite : fallbackBackground;
        toolbarBackground.color = source != null ? source.color : new Color(0f, 0f, 0f, .8352941f);
        toolbarBackground.material = source != null ? source.material : null;
        toolbarBackground.type = source != null ? source.type : Image.Type.Sliced;
        toolbarBackground.pixelsPerUnitMultiplier = source != null ? source.pixelsPerUnitMultiplier : 1f;
        toolbarBackground.raycastTarget = false;
        Image oldBackground = panel.GetComponent<Image>();
        if (oldBackground != null) { oldBackground.color = Color.clear; oldBackground.raycastTarget = false; }
        ColorBlock colors = sourceButton != null ? sourceButton.colors : ColorBlock.defaultColorBlock;
        foreach (Button button in new[] { slower, faster })
        {
            Image hitArea = button.GetComponent<Image>();
            if (hitArea != null)
            {
                hitArea.sprite = null;
                hitArea.material = null;
                hitArea.type = Image.Type.Simple;
                hitArea.color = Color.clear;
                hitArea.raycastTarget = true;
            }
            TextMeshProUGUI caption = button.GetComponentInChildren<TextMeshProUGUI>(true);
            if (caption == null) continue;
            caption.color = iconColor;
            caption.fontSize = 60f;
            caption.raycastTarget = false;
            button.targetGraphic = caption;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = colors;
            ((RectTransform)button.transform).sizeDelta = Vector2.one * VisualButtonSize;
        }
        speedLabel.color = iconColor;
        speedLabel.fontSize = 34f;
        toolbarStyleVersion = 2;
    }

    private Button CreateButton(string name, string caption, float x, TMP_FontAsset font, Sprite background)
    {
        RectTransform rect = NewRect(name, panel);
        rect.sizeDelta = Vector2.one * VisualButtonSize;
        rect.anchoredPosition = new Vector2(x, 0f);
        Image image = rect.gameObject.AddComponent<Image>();
        image.sprite = background; image.type = Image.Type.Sliced;
        image.color = Color.clear;
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        TextMeshProUGUI label = CreateText("Label", rect, caption, font, 60f);
        label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = new Vector2(2f, 0f); label.rectTransform.offsetMax = new Vector2(-2f, 0f);
        return button;
    }

    private static TextMeshProUGUI CreateText(string name, Transform parent, string text, TMP_FontAsset font, float size)
    {
        RectTransform rect = NewRect(name, parent);
        TextMeshProUGUI label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font; label.text = text; label.fontSize = size;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = false; label.overflowMode = TextOverflowModes.Overflow;
        label.raycastTarget = false;
        return label;
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        RectTransform rect = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
        rect.localScale = Vector3.one;
        rect.gameObject.layer = parent != null ? parent.gameObject.layer : 5;
        return rect;
    }
}
