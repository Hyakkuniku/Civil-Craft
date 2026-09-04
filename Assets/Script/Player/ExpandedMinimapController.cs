using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Turns the existing corner minimap into a mobile-friendly, animated map.
/// The component installs itself at runtime so every scene that contains a
/// MinimapPanel and MinimapCamera receives the same behaviour.
/// </summary>
[DisallowMultipleComponent]
public sealed class ExpandedMinimapController : MonoBehaviour
{
    private sealed class MarkerView
    {
        public BuildLocation location;
        public RectTransform root;
        public RectTransform diamond;
        public Image image;
        public TMP_Text label;
        public Button button;
    }

    [Header("Scene References (auto-detected when empty)")]
    [SerializeField] private RectTransform minimapPanel;
    [SerializeField] private RawImage mapImage;
    [SerializeField] private Camera minimapCamera;
    [SerializeField] private MinimapFollow minimapFollow;
    [SerializeField] private Button enlargeButton;

    [Header("Expanded Layout")]
    [SerializeField] private Vector2 expandedAnchorMin = new Vector2(0.04f, 0.06f);
    [SerializeField] private Vector2 expandedAnchorMax = new Vector2(0.96f, 0.94f);
    [SerializeField, Min(0.05f)] private float animationDuration = 0.3f;
    [SerializeField] private AnimationCurve animationCurve = null;

    [Header("Map Navigation")]
    [SerializeField, Min(1f)] private float minimumZoom = 6f;
    [SerializeField, Min(2f)] private float maximumZoom = 90f;
    [SerializeField, Min(0f)] private float boundsPadding = 12f;
    [SerializeField] private bool frameBuildLocationsWhenOpened = true;
    [SerializeField, Range(768, 2048)] private int expandedTextureLongEdge = 1280;

    [Header("Build Location Markers")]
    [SerializeField] private Color availableLocationColor = new Color(1f, 0.62f, 0.08f, 1f);
    [SerializeField] private Color completedLocationColor = new Color(0.28f, 0.72f, 0.34f, 1f);
    [SerializeField] private Color lockedLocationColor = new Color(0.62f, 0.58f, 0.50f, 1f);
    [SerializeField] private Color activeLocationColor = new Color(1f, 0.82f, 0.12f, 1f);
    [SerializeField] private Color navigationLocationColor = new Color(0.15f, 0.72f, 0.92f, 1f);

    [Header("Location Actions")]
    [Tooltip("Applied only when a Build Location does not have an explicit Fast Travel Target.")]
    [SerializeField] private Vector3 fallbackFastTravelOffset = new Vector3(0f, 1f, 0f);

    private readonly List<MarkerView> markers = new List<MarkerView>();
    private RectTransform markerLayer;
    private GameObject controlsRoot;
    private CanvasGroup controlsCanvasGroup;
    private Canvas owningCanvas;
    private TMP_FontAsset uiFont;
    private RectTransform locationActionRoot;
    private TMP_Text selectedLocationLabel;
    private TMP_Text locationActionLabel;
    private Button locationActionButton;
    private BuildLocation selectedLocation;
    private BuildLocation navigationDestination;

    private Vector2 compactAnchorMin;
    private Vector2 compactAnchorMax;
    private Vector2 compactAnchoredPosition;
    private Vector2 compactSizeDelta;
    private Vector2 compactPivot;
    private Vector3 compactCameraPosition;
    private Quaternion compactCameraRotation;
    private float compactOrthographicSize;
    private RenderTexture compactTargetTexture;
    private Texture compactMapTexture;
    private RenderTexture expandedTargetTexture;

    private InputManager inputManager;
    private bool restoreMovementInput;
    private bool restoreLookInput;
    private bool inputCaptured;
    private bool isExpanded;
    private bool isAnimating;
    private Coroutine animationRoutine;

    private Vector3 framedCenter;
    private float framedSize;
    private float minWorldX;
    private float maxWorldX;
    private float minWorldZ;
    private float maxWorldZ;
    private bool hasWorldBounds;
    private bool pinchActive;
    private float previousPinchDistance;

    public bool IsExpanded => isExpanded || isAnimating;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneBootstrap()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Camera camera = null;
        RectTransform panel = null;

        foreach (Transform candidate in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (candidate == null || candidate.gameObject.scene != scene) continue;

            if (candidate.name == "MinimapCamera")
                camera = candidate.GetComponent<Camera>();
            else if (candidate.name == "MinimapPanel")
                panel = candidate as RectTransform;
        }

        if (camera == null || panel == null) return;

        ExpandedMinimapController controller = camera.GetComponent<ExpandedMinimapController>();
        if (controller == null)
            controller = camera.gameObject.AddComponent<ExpandedMinimapController>();

        controller.minimapCamera = camera;
        controller.minimapPanel = panel;
        controller.ResolveReferencesAndBuildUI();
    }

    private void Awake()
    {
        if (animationCurve == null || animationCurve.length == 0)
            animationCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        if (minimapCamera == null)
            minimapCamera = GetComponent<Camera>();
    }

    private void Start()
    {
        ResolveReferencesAndBuildUI();
        RebuildLocationMarkers();
    }

    private void OnDestroy()
    {
        if (enlargeButton != null)
            enlargeButton.onClick.RemoveListener(ToggleExpanded);

        RestorePlayerInput();
        if (minimapFollow != null)
            minimapFollow.SetManualView(false);
        RestoreCompactRenderTexture();
    }

    private void Update()
    {
        if (!isExpanded || isAnimating) return;

        HandlePinchZoom();
        UpdateMarkerPositions();

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            CloseExpandedMap();
    }

    private void LateUpdate()
    {
        if (!isExpanded || isAnimating) return;
        UpdateMarkerPositions();
    }

    public void ToggleExpanded()
    {
        if (isExpanded) CloseExpandedMap();
        else OpenExpandedMap();
    }

    public void OpenExpandedMap()
    {
        ResolveReferencesAndBuildUI();
        if (isExpanded || isAnimating || minimapPanel == null ||
            mapImage == null || minimapCamera == null ||
            !minimapPanel.gameObject.activeInHierarchy)
            return;

        CaptureCompactState();
        CaptureAndDisablePlayerInput();

        if (UIPanelCoordinator.Instance != null)
            UIPanelCoordinator.Instance.OpenPanel(minimapPanel.gameObject, false);

        minimapPanel.SetAsLastSibling();
        if (minimapFollow != null)
            minimapFollow.SetManualView(true, true);
        else
            minimapCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        PrepareExpandedRenderTexture();
        BuildWorldBoundsAndFraming();
        RebuildLocationMarkers();
        SetExpandedControlsVisible(true);
        SetExpandButtonVisible(false);

        isExpanded = true;
        StartMapAnimation(true);
    }

    public void CloseExpandedMap()
    {
        if (!isExpanded || isAnimating) return;
        StartMapAnimation(false);
    }

    public void CenterOnPlayer()
    {
        if (!isExpanded || minimapFollow == null || minimapFollow.player == null) return;

        Vector3 center = minimapFollow.player.position;
        center.y = minimapCamera.transform.position.y;
        SetCameraCenterClamped(center);
    }

    public void ZoomIn()
    {
        SetZoom(minimapCamera != null ? minimapCamera.orthographicSize * 0.75f : minimumZoom);
    }

    public void ZoomOut()
    {
        SetZoom(minimapCamera != null ? minimapCamera.orthographicSize / 0.75f : maximumZoom);
    }

    internal void PanByCanvasDelta(Vector2 canvasDelta)
    {
        if (!isExpanded || isAnimating || minimapCamera == null || mapImage == null)
            return;

        if (Touchscreen.current != null && CountPressedTouches(Touchscreen.current) > 1)
            return;

        float imageHeight = Mathf.Max(1f, mapImage.rectTransform.rect.height);
        float imageWidth = Mathf.Max(1f, mapImage.rectTransform.rect.width);
        float worldPerCanvasUnitY = (minimapCamera.orthographicSize * 2f) / imageHeight;
        float worldPerCanvasUnitX = (minimapCamera.orthographicSize * 2f * GetMapAspect()) / imageWidth;

        Vector3 cameraRight = Vector3.ProjectOnPlane(minimapCamera.transform.right, Vector3.up).normalized;
        Vector3 cameraUp = Vector3.ProjectOnPlane(minimapCamera.transform.up, Vector3.up).normalized;
        Vector3 movement = -cameraRight * canvasDelta.x * worldPerCanvasUnitX -
                           cameraUp * canvasDelta.y * worldPerCanvasUnitY;

        SetCameraCenterClamped(minimapCamera.transform.position + movement);
    }

    internal void ZoomFromScroll(float scrollAmount)
    {
        if (!isExpanded || Mathf.Approximately(scrollAmount, 0f)) return;
        float zoomFactor = Mathf.Pow(0.88f, scrollAmount);
        SetZoom(minimapCamera.orthographicSize * zoomFactor);
    }

    private void ResolveReferencesAndBuildUI()
    {
        if (minimapCamera == null)
            minimapCamera = GetComponent<Camera>();
        if (minimapFollow == null && minimapCamera != null)
            minimapFollow = minimapCamera.GetComponent<MinimapFollow>();

        if (minimapPanel == null)
        {
            foreach (RectTransform rect in Resources.FindObjectsOfTypeAll<RectTransform>())
            {
                if (rect != null && rect.name == "MinimapPanel" &&
                    (minimapCamera == null || rect.gameObject.scene == minimapCamera.gameObject.scene))
                {
                    minimapPanel = rect;
                    break;
                }
            }
        }

        if (minimapPanel == null) return;

        owningCanvas = minimapPanel.GetComponentInParent<Canvas>(true);
        if (mapImage == null)
            mapImage = minimapPanel.GetComponentInChildren<RawImage>(true);
        if (uiFont == null)
        {
            TMP_Text existingText = minimapPanel.GetComponentInChildren<TMP_Text>(true);
            if (existingText == null)
                existingText = FindObjectOfType<TMP_Text>(true);
            if (existingText != null) uiFont = existingText.font;
        }

        if (enlargeButton == null)
        {
            foreach (Button button in minimapPanel.GetComponentsInChildren<Button>(true))
            {
                if (button != null && button.name == "EnlargeMinimap")
                {
                    enlargeButton = button;
                    break;
                }
            }
        }

        if (enlargeButton == null)
            enlargeButton = CreateInvisibleExpandButton();

        enlargeButton.onClick.RemoveListener(ToggleExpanded);
        enlargeButton.onClick.AddListener(ToggleExpanded);

        if (mapImage != null)
        {
            mapImage.raycastTarget = true;
            ExpandedMinimapInputSurface surface = mapImage.GetComponent<ExpandedMinimapInputSurface>();
            if (surface == null) surface = mapImage.gameObject.AddComponent<ExpandedMinimapInputSurface>();
            surface.Owner = this;
        }

        EnsureMarkerLayer();
        EnsureControls();
        SetExpandedControlsVisible(false);
    }

    private Button CreateInvisibleExpandButton()
    {
        GameObject buttonObject = new GameObject("EnlargeMinimap", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        buttonObject.layer = minimapPanel.gameObject.layer;
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.SetParent(minimapPanel, false);
        Stretch(rect, Vector2.zero, Vector2.zero);

        Image image = buttonObject.GetComponent<Image>();
        image.color = Color.clear;
        image.raycastTarget = true;
        return buttonObject.GetComponent<Button>();
    }

    private void EnsureMarkerLayer()
    {
        if (mapImage == null || markerLayer != null) return;

        Transform existing = mapImage.transform.Find("BuildLocationMarkers");
        if (existing != null)
            markerLayer = existing as RectTransform;
        else
        {
            GameObject layer = new GameObject("BuildLocationMarkers", typeof(RectTransform));
            layer.layer = mapImage.gameObject.layer;
            markerLayer = layer.GetComponent<RectTransform>();
            markerLayer.SetParent(mapImage.rectTransform, false);
            Stretch(markerLayer, Vector2.zero, Vector2.zero);
        }
        markerLayer.SetAsLastSibling();
    }

    private void EnsureControls()
    {
        if (controlsRoot != null || minimapPanel == null) return;

        controlsRoot = new GameObject("ExpandedMapControls", typeof(RectTransform), typeof(CanvasGroup));
        controlsRoot.layer = minimapPanel.gameObject.layer;
        RectTransform rootRect = controlsRoot.GetComponent<RectTransform>();
        rootRect.SetParent(minimapPanel, false);
        Stretch(rootRect, Vector2.zero, Vector2.zero);
        controlsCanvasGroup = controlsRoot.GetComponent<CanvasGroup>();

        CreateControlButton("CloseMap", "X", new Vector2(1f, 1f), new Vector2(-48f, -48f), CloseExpandedMap);
        CreateControlButton("ZoomIn", "+", new Vector2(1f, 0f), new Vector2(-48f, 108f), ZoomIn);
        CreateControlButton("ZoomOut", "−", new Vector2(1f, 0f), new Vector2(-48f, 48f), ZoomOut);
        CreateControlButton("CenterPlayer", "◎", new Vector2(0f, 0f), new Vector2(48f, 48f), CenterOnPlayer);
        CreateLocationActionPanel(rootRect);

        GameObject titleObject = new GameObject("MapInstructions", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        titleObject.layer = minimapPanel.gameObject.layer;
        RectTransform titleRect = titleObject.GetComponent<RectTransform>();
        titleRect.SetParent(rootRect, false);
        titleRect.anchorMin = new Vector2(0.18f, 1f);
        titleRect.anchorMax = new Vector2(0.82f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -18f);
        titleRect.sizeDelta = new Vector2(0f, 54f);

        TextMeshProUGUI title = titleObject.GetComponent<TextMeshProUGUI>();
        title.text = "MAP   •   Drag to pan   •   Pinch to zoom";
        title.alignment = TextAlignmentOptions.Center;
        title.color = new Color(0.20f, 0.12f, 0.07f, 1f);
        title.enableAutoSizing = true;
        title.fontSizeMin = 16f;
        title.fontSizeMax = 30f;
        title.raycastTarget = false;
        if (uiFont != null) title.font = uiFont;

        controlsRoot.transform.SetAsLastSibling();
    }

    private void CreateLocationActionPanel(RectTransform parent)
    {
        GameObject panelObject = new GameObject(
            "LocationActionPanel",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Outline));
        panelObject.layer = minimapPanel.gameObject.layer;
        locationActionRoot = panelObject.GetComponent<RectTransform>();
        locationActionRoot.SetParent(parent, false);
        locationActionRoot.anchorMin = locationActionRoot.anchorMax = new Vector2(0.5f, 0f);
        locationActionRoot.pivot = new Vector2(0.5f, 0f);
        locationActionRoot.anchoredPosition = new Vector2(0f, 18f);
        locationActionRoot.sizeDelta = new Vector2(560f, 92f);

        Image background = panelObject.GetComponent<Image>();
        background.color = new Color(0.98f, 0.92f, 0.78f, 0.98f);
        Outline outline = panelObject.GetComponent<Outline>();
        outline.effectColor = new Color(0.34f, 0.19f, 0.10f, 1f);
        outline.effectDistance = new Vector2(3f, -3f);

        GameObject selectedObject = new GameObject(
            "SelectedLocation",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        selectedObject.layer = minimapPanel.gameObject.layer;
        RectTransform selectedRect = selectedObject.GetComponent<RectTransform>();
        selectedRect.SetParent(locationActionRoot, false);
        selectedRect.anchorMin = new Vector2(0f, 0f);
        selectedRect.anchorMax = new Vector2(0.62f, 1f);
        selectedRect.offsetMin = new Vector2(18f, 10f);
        selectedRect.offsetMax = new Vector2(-12f, -10f);

        selectedLocationLabel = selectedObject.GetComponent<TextMeshProUGUI>();
        selectedLocationLabel.text = "Select a build location";
        selectedLocationLabel.alignment = TextAlignmentOptions.MidlineLeft;
        selectedLocationLabel.color = new Color(0.22f, 0.12f, 0.07f, 1f);
        selectedLocationLabel.fontStyle = FontStyles.Bold;
        selectedLocationLabel.enableAutoSizing = true;
        selectedLocationLabel.fontSizeMin = 13f;
        selectedLocationLabel.fontSizeMax = 26f;
        selectedLocationLabel.raycastTarget = false;
        if (uiFont != null) selectedLocationLabel.font = uiFont;

        GameObject buttonObject = new GameObject(
            "LocationActionButton",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Button),
            typeof(Outline));
        buttonObject.layer = minimapPanel.gameObject.layer;
        RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
        buttonRect.SetParent(locationActionRoot, false);
        buttonRect.anchorMin = new Vector2(0.62f, 0f);
        buttonRect.anchorMax = new Vector2(1f, 1f);
        buttonRect.offsetMin = new Vector2(4f, 12f);
        buttonRect.offsetMax = new Vector2(-12f, -12f);

        Image buttonBackground = buttonObject.GetComponent<Image>();
        buttonBackground.color = availableLocationColor;
        Outline buttonOutline = buttonObject.GetComponent<Outline>();
        buttonOutline.effectColor = new Color(0.34f, 0.19f, 0.10f, 1f);
        buttonOutline.effectDistance = new Vector2(2f, -2f);

        locationActionButton = buttonObject.GetComponent<Button>();
        locationActionButton.targetGraphic = buttonBackground;
        locationActionButton.onClick.AddListener(PerformSelectedLocationAction);

        GameObject labelObject = new GameObject(
            "Label",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        labelObject.layer = minimapPanel.gameObject.layer;
        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.SetParent(buttonRect, false);
        Stretch(labelRect, new Vector2(8f, 4f), new Vector2(-8f, -4f));

        locationActionLabel = labelObject.GetComponent<TextMeshProUGUI>();
        locationActionLabel.text = "SELECT";
        locationActionLabel.alignment = TextAlignmentOptions.Center;
        locationActionLabel.color = Color.white;
        locationActionLabel.fontStyle = FontStyles.Bold;
        locationActionLabel.enableAutoSizing = true;
        locationActionLabel.fontSizeMin = 12f;
        locationActionLabel.fontSizeMax = 24f;
        locationActionLabel.raycastTarget = false;
        if (uiFont != null) locationActionLabel.font = uiFont;

        UpdateLocationActionPanel();
    }

    private void CreateControlButton(string objectName, string text, Vector2 anchor, Vector2 position, UnityEngine.Events.UnityAction action)
    {
        GameObject buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        buttonObject.layer = minimapPanel.gameObject.layer;
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.SetParent(controlsRoot.transform, false);
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(58f, 58f);

        Image background = buttonObject.GetComponent<Image>();
        background.color = new Color(0.96f, 0.88f, 0.68f, 0.98f);
        Outline outline = buttonObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.34f, 0.19f, 0.10f, 1f);
        outline.effectDistance = new Vector2(3f, -3f);

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = background;
        button.onClick.AddListener(action);

        GameObject textObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObject.layer = minimapPanel.gameObject.layer;
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.SetParent(rect, false);
        Stretch(textRect, Vector2.zero, Vector2.zero);

        TextMeshProUGUI label = textObject.GetComponent<TextMeshProUGUI>();
        label.text = text;
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(0.28f, 0.15f, 0.08f, 1f);
        label.fontSize = 35f;
        label.fontStyle = FontStyles.Bold;
        label.raycastTarget = false;
        if (uiFont != null) label.font = uiFont;
    }

    private void RebuildLocationMarkers()
    {
        EnsureMarkerLayer();
        if (markerLayer == null || minimapCamera == null) return;

        foreach (MarkerView marker in markers)
        {
            if (marker.root != null) Destroy(marker.root.gameObject);
        }
        markers.Clear();

        if (selectedLocation != null && selectedLocation.gameObject.scene != gameObject.scene)
            selectedLocation = null;
        if (navigationDestination != null && navigationDestination.gameObject.scene != gameObject.scene)
            navigationDestination = null;

        foreach (BuildLocation location in Resources.FindObjectsOfTypeAll<BuildLocation>())
        {
            if (location == null || location.gameObject.scene != gameObject.scene) continue;
            CreateLocationMarker(location);
        }

        UpdateMarkerPositions();
    }

    private void CreateLocationMarker(BuildLocation location)
    {
        GameObject rootObject = new GameObject("Marker_" + location.name, typeof(RectTransform), typeof(Button));
        rootObject.layer = markerLayer.gameObject.layer;
        RectTransform root = rootObject.GetComponent<RectTransform>();
        root.SetParent(markerLayer, false);
        root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
        root.pivot = new Vector2(0.5f, 0.5f);
        root.sizeDelta = new Vector2(44f, 44f);

        GameObject diamondObject = new GameObject("Highlight", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline));
        diamondObject.layer = markerLayer.gameObject.layer;
        RectTransform diamond = diamondObject.GetComponent<RectTransform>();
        diamond.SetParent(root, false);
        diamond.anchorMin = diamond.anchorMax = new Vector2(0.5f, 0.5f);
        diamond.sizeDelta = new Vector2(24f, 24f);
        diamond.localRotation = Quaternion.Euler(0f, 0f, 45f);

        Image image = diamondObject.GetComponent<Image>();
        image.color = GetMarkerColor(location);
        image.raycastTarget = true;
        Outline outline = diamondObject.GetComponent<Outline>();
        outline.effectColor = new Color(0.22f, 0.12f, 0.06f, 1f);
        outline.effectDistance = new Vector2(2f, -2f);

        Button button = rootObject.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => SelectLocation(location));

        GameObject labelObject = new GameObject("LocationName", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        labelObject.layer = markerLayer.gameObject.layer;
        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.SetParent(root, false);
        labelRect.anchorMin = new Vector2(0.5f, 0.5f);
        labelRect.anchorMax = new Vector2(0.5f, 0.5f);
        labelRect.pivot = new Vector2(0f, 0.5f);
        labelRect.anchoredPosition = new Vector2(20f, 0f);
        labelRect.sizeDelta = new Vector2(260f, 44f);

        TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
        label.text = GetLocationLabel(location);
        label.alignment = TextAlignmentOptions.Left;
        label.color = new Color(0.16f, 0.09f, 0.04f, 1f);
        label.fontStyle = FontStyles.Bold;
        label.enableAutoSizing = true;
        label.fontSizeMin = 12f;
        label.fontSizeMax = 24f;
        label.raycastTarget = false;
        if (uiFont != null) label.font = uiFont;

        markers.Add(new MarkerView
        {
            location = location,
            root = root,
            diamond = diamond,
            image = image,
            label = label,
            button = button
        });
    }

    private void UpdateMarkerPositions()
    {
        if (markerLayer == null || minimapCamera == null) return;

        Rect rect = markerLayer.rect;
        float pulse = 1f + Mathf.Sin(Time.unscaledTime * 4.5f) * 0.12f;
        foreach (MarkerView marker in markers)
        {
            if (marker.location == null || marker.root == null) continue;

            Vector3 viewport = minimapCamera.WorldToViewportPoint(GetLocationWorldPosition(marker.location));
            bool visible = viewport.z > 0f && viewport.x >= 0.015f && viewport.x <= 0.985f &&
                           viewport.y >= 0.015f && viewport.y <= 0.985f;
            marker.root.gameObject.SetActive(visible);
            if (!visible) continue;

            marker.root.anchoredPosition = new Vector2(
                (viewport.x - 0.5f) * rect.width,
                (viewport.y - 0.5f) * rect.height);
            marker.image.color = GetMarkerColor(marker.location);
            marker.label.text = GetLocationLabel(marker.location);
            bool isNavigationDestination = marker.location == navigationDestination;
            marker.label.gameObject.SetActive(isExpanded || isNavigationDestination);
            marker.button.interactable = isExpanded;
            bool shouldPulse = isExpanded || isNavigationDestination;
            marker.diamond.localScale = Vector3.one * (shouldPulse ? pulse : 0.78f);
        }

        UpdateLocationActionPanel();
    }

    private void SelectLocation(BuildLocation location)
    {
        if (!isExpanded || location == null) return;
        selectedLocation = location;
        Vector3 center = GetLocationWorldPosition(location);
        center.y = minimapCamera.transform.position.y;
        SetCameraCenterClamped(center);
        UpdateLocationActionPanel();
    }

    private void UpdateLocationActionPanel()
    {
        if (locationActionButton == null || selectedLocationLabel == null || locationActionLabel == null)
            return;

        bool hasSelection = selectedLocation != null;
        locationActionButton.interactable = hasSelection;

        Image background = locationActionButton.targetGraphic as Image;
        if (!hasSelection)
        {
            selectedLocationLabel.text = "Select a build location";
            locationActionLabel.text = "SELECT";
            if (background != null) background.color = lockedLocationColor;
            return;
        }

        bool completed = IsLocationCompleted(selectedLocation);
        selectedLocationLabel.text = GetLocationLabel(selectedLocation) +
                                     (completed ? "\n<color=#4B9E55>COMPLETED</color>" : "\n<color=#C47922>NOT COMPLETED</color>");
        locationActionLabel.text = completed ? "FAST TRAVEL" : "NAVIGATE";
        if (background != null)
            background.color = completed ? completedLocationColor : availableLocationColor;
    }

    private void PerformSelectedLocationAction()
    {
        if (selectedLocation == null) return;

        if (IsLocationCompleted(selectedLocation))
            FastTravelToLocation(selectedLocation);
        else
            NavigateToLocation(selectedLocation);
    }

    private void NavigateToLocation(BuildLocation location)
    {
        if (location == null) return;

        Transform target = location.navigationTarget != null
            ? location.navigationTarget.transform
            : location.transform;

        if (PathGuider.Instance == null)
        {
            Debug.LogWarning("[ExpandedMinimap] No PathGuider is active, so a route could not be created.", this);
            return;
        }

        navigationDestination = location;
        PathGuider.Instance.RouteToSingleTarget(target);
        UpdateMarkerPositions();
        CloseExpandedMap();
    }

    private void FastTravelToLocation(BuildLocation location)
    {
        if (location == null) return;

        Transform player = minimapFollow != null ? minimapFollow.player : null;
        if (player == null)
        {
            PlayerMotor motor = FindObjectOfType<PlayerMotor>(true);
            if (motor != null) player = motor.transform;
        }

        if (player == null)
        {
            Debug.LogWarning("[ExpandedMinimap] Fast Travel could not find the player.", this);
            return;
        }

        Transform target = location.fastTravelTarget != null
            ? location.fastTravelTarget
            : location.navigationTarget != null
                ? location.navigationTarget.transform
                : location.transform;
        Vector3 destination = target.position;
        if (location.fastTravelTarget == null)
            destination += fallbackFastTravelOffset;

        CharacterController characterController = player.GetComponent<CharacterController>();
        bool restoreCharacterController = characterController != null && characterController.enabled;
        if (restoreCharacterController) characterController.enabled = false;

        Rigidbody body = player.GetComponent<Rigidbody>();
        if (body != null)
        {
            if (!body.isKinematic)
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            body.position = destination;
            body.rotation = target.rotation;
        }

        player.SetPositionAndRotation(destination, target.rotation);
        Physics.SyncTransforms();

        if (restoreCharacterController) characterController.enabled = true;

        navigationDestination = null;
        if (PathGuider.Instance != null)
            PathGuider.Instance.SetNewWaypoints(new List<GuiderWaypoint>());

        CloseExpandedMap();
    }

    private void BuildWorldBoundsAndFraming()
    {
        hasWorldBounds = false;
        Vector3 sum = Vector3.zero;
        int count = 0;

        foreach (BuildLocation location in Resources.FindObjectsOfTypeAll<BuildLocation>())
        {
            if (location == null || location.gameObject.scene != gameObject.scene) continue;
            Vector3 point = GetLocationWorldPosition(location);
            EncapsulateWorldPoint(point);
            sum += point;
            count++;
        }

        if (minimapFollow != null && minimapFollow.player != null)
        {
            Vector3 playerPoint = minimapFollow.player.position;
            EncapsulateWorldPoint(playerPoint);
            sum += playerPoint;
            count++;
        }

        if (!hasWorldBounds)
        {
            Vector3 current = minimapCamera.transform.position;
            minWorldX = current.x - 30f;
            maxWorldX = current.x + 30f;
            minWorldZ = current.z - 30f;
            maxWorldZ = current.z + 30f;
            hasWorldBounds = true;
        }

        minWorldX -= boundsPadding;
        maxWorldX += boundsPadding;
        minWorldZ -= boundsPadding;
        maxWorldZ += boundsPadding;

        framedCenter = count > 0 ? sum / count : minimapCamera.transform.position;
        framedCenter.x = (minWorldX + maxWorldX) * 0.5f;
        framedCenter.z = (minWorldZ + maxWorldZ) * 0.5f;
        framedCenter.y = minimapCamera.transform.position.y;

        float aspect = GetMapAspect();
        float halfWidth = (maxWorldX - minWorldX) * 0.5f;
        float halfHeight = (maxWorldZ - minWorldZ) * 0.5f;
        framedSize = Mathf.Clamp(Mathf.Max(halfHeight, halfWidth / Mathf.Max(0.1f, aspect)), minimumZoom, maximumZoom);

        if (!frameBuildLocationsWhenOpened)
        {
            framedCenter = compactCameraPosition;
            framedSize = compactOrthographicSize;
        }
    }

    private void EncapsulateWorldPoint(Vector3 point)
    {
        if (!hasWorldBounds)
        {
            minWorldX = maxWorldX = point.x;
            minWorldZ = maxWorldZ = point.z;
            hasWorldBounds = true;
            return;
        }

        minWorldX = Mathf.Min(minWorldX, point.x);
        maxWorldX = Mathf.Max(maxWorldX, point.x);
        minWorldZ = Mathf.Min(minWorldZ, point.z);
        maxWorldZ = Mathf.Max(maxWorldZ, point.z);
    }

    private void HandlePinchZoom()
    {
        Touchscreen touchscreen = Touchscreen.current;
        if (touchscreen == null || CountPressedTouches(touchscreen) < 2)
        {
            pinchActive = false;
            return;
        }

        Vector2 first = touchscreen.touches[0].position.ReadValue();
        Vector2 second = touchscreen.touches[1].position.ReadValue();
        float distance = Vector2.Distance(first, second);

        if (pinchActive && previousPinchDistance > 0.01f)
        {
            float ratio = previousPinchDistance / Mathf.Max(1f, distance);
            SetZoom(minimapCamera.orthographicSize * ratio);
        }

        previousPinchDistance = distance;
        pinchActive = true;
    }

    private static int CountPressedTouches(Touchscreen touchscreen)
    {
        int count = 0;
        foreach (UnityEngine.InputSystem.Controls.TouchControl touch in touchscreen.touches)
        {
            if (touch.press.isPressed) count++;
        }
        return count;
    }

    private void SetZoom(float size)
    {
        if (!isExpanded || minimapCamera == null) return;
        minimapCamera.orthographicSize = Mathf.Clamp(size, minimumZoom, maximumZoom);
        SetCameraCenterClamped(minimapCamera.transform.position);
        UpdateMarkerPositions();
    }

    private void SetCameraCenterClamped(Vector3 center)
    {
        if (minimapCamera == null) return;
        center.y = minimapCamera.transform.position.y;

        if (hasWorldBounds)
        {
            float halfHeight = minimapCamera.orthographicSize;
            float halfWidth = halfHeight * GetMapAspect();
            center.x = ClampAxis(center.x, minWorldX, maxWorldX, halfWidth);
            center.z = ClampAxis(center.z, minWorldZ, maxWorldZ, halfHeight);
        }

        if (minimapFollow != null)
            minimapFollow.SetManualCenter(center);
        else
            minimapCamera.transform.position = center;
    }

    private static float ClampAxis(float value, float minimum, float maximum, float halfView)
    {
        float low = minimum + halfView;
        float high = maximum - halfView;
        return low <= high ? Mathf.Clamp(value, low, high) : (minimum + maximum) * 0.5f;
    }

    private float GetMapAspect()
    {
        if (minimapCamera != null && minimapCamera.targetTexture != null &&
            minimapCamera.targetTexture.height > 0)
        {
            return (float)minimapCamera.targetTexture.width / minimapCamera.targetTexture.height;
        }
        return minimapCamera != null ? Mathf.Max(0.1f, minimapCamera.aspect) : 1f;
    }

    private void CaptureCompactState()
    {
        compactAnchorMin = minimapPanel.anchorMin;
        compactAnchorMax = minimapPanel.anchorMax;
        compactAnchoredPosition = minimapPanel.anchoredPosition;
        compactSizeDelta = minimapPanel.sizeDelta;
        compactPivot = minimapPanel.pivot;
        compactCameraPosition = minimapCamera.transform.position;
        compactCameraRotation = minimapCamera.transform.rotation;
        compactOrthographicSize = minimapCamera.orthographicSize;
        compactTargetTexture = minimapCamera.targetTexture;
        compactMapTexture = mapImage != null ? mapImage.texture : null;
    }

    private void PrepareExpandedRenderTexture()
    {
        RestoreCompactRenderTexture();
        if (minimapCamera == null || mapImage == null || minimapPanel == null) return;

        RectTransform parent = minimapPanel.parent as RectTransform;
        float targetWidth = parent != null
            ? parent.rect.width * Mathf.Abs(expandedAnchorMax.x - expandedAnchorMin.x)
            : Screen.width * 0.92f;
        float targetHeight = parent != null
            ? parent.rect.height * Mathf.Abs(expandedAnchorMax.y - expandedAnchorMin.y)
            : Screen.height * 0.88f;
        float aspect = Mathf.Clamp(targetWidth / Mathf.Max(1f, targetHeight), 0.5f, 2.5f);

        int width;
        int height;
        if (aspect >= 1f)
        {
            width = expandedTextureLongEdge;
            height = Mathf.Max(512, Mathf.RoundToInt(width / aspect));
        }
        else
        {
            height = expandedTextureLongEdge;
            width = Mathf.Max(512, Mathf.RoundToInt(height * aspect));
        }

        RenderTextureDescriptor descriptor = compactTargetTexture != null
            ? compactTargetTexture.descriptor
            : new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 24);
        descriptor.width = width;
        descriptor.height = height;
        descriptor.msaaSamples = 2;
        descriptor.useMipMap = false;
        descriptor.autoGenerateMips = false;

        expandedTargetTexture = new RenderTexture(descriptor)
        {
            name = "ExpandedMinimap_Runtime",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        expandedTargetTexture.Create();
        minimapCamera.targetTexture = expandedTargetTexture;
        mapImage.texture = expandedTargetTexture;
    }

    private void RestoreCompactRenderTexture()
    {
        if (expandedTargetTexture == null) return;

        if (minimapCamera != null) minimapCamera.targetTexture = compactTargetTexture;
        if (mapImage != null) mapImage.texture = compactMapTexture;
        expandedTargetTexture.Release();
        Destroy(expandedTargetTexture);
        expandedTargetTexture = null;
    }

    private void StartMapAnimation(bool opening)
    {
        if (animationRoutine != null) StopCoroutine(animationRoutine);
        animationRoutine = StartCoroutine(AnimateMap(opening));
    }

    private IEnumerator AnimateMap(bool opening)
    {
        isAnimating = true;
        Vector2 fromAnchorMin = minimapPanel.anchorMin;
        Vector2 fromAnchorMax = minimapPanel.anchorMax;
        Vector2 fromPosition = minimapPanel.anchoredPosition;
        Vector2 fromSize = minimapPanel.sizeDelta;
        Vector2 fromPivot = minimapPanel.pivot;
        Vector3 fromCameraPosition = minimapCamera.transform.position;
        Quaternion fromCameraRotation = minimapCamera.transform.rotation;
        float fromCameraSize = minimapCamera.orthographicSize;

        Vector2 toAnchorMin = opening ? expandedAnchorMin : compactAnchorMin;
        Vector2 toAnchorMax = opening ? expandedAnchorMax : compactAnchorMax;
        Vector2 toPosition = opening ? Vector2.zero : compactAnchoredPosition;
        Vector2 toSize = opening ? Vector2.zero : compactSizeDelta;
        Vector2 toPivot = opening ? new Vector2(0.5f, 0.5f) : compactPivot;
        Vector3 toCameraPosition = opening ? framedCenter : compactCameraPosition;
        Quaternion toCameraRotation = opening ? Quaternion.Euler(90f, 0f, 0f) : compactCameraRotation;
        float toCameraSize = opening ? framedSize : compactOrthographicSize;

        float elapsed = 0f;
        while (elapsed < animationDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float normalized = Mathf.Clamp01(elapsed / animationDuration);
            float t = animationCurve.Evaluate(normalized);

            minimapPanel.anchorMin = Vector2.LerpUnclamped(fromAnchorMin, toAnchorMin, t);
            minimapPanel.anchorMax = Vector2.LerpUnclamped(fromAnchorMax, toAnchorMax, t);
            minimapPanel.anchoredPosition = Vector2.LerpUnclamped(fromPosition, toPosition, t);
            minimapPanel.sizeDelta = Vector2.LerpUnclamped(fromSize, toSize, t);
            minimapPanel.pivot = Vector2.LerpUnclamped(fromPivot, toPivot, t);
            minimapCamera.transform.position = Vector3.LerpUnclamped(fromCameraPosition, toCameraPosition, t);
            minimapCamera.transform.rotation = Quaternion.SlerpUnclamped(fromCameraRotation, toCameraRotation, t);
            minimapCamera.orthographicSize = Mathf.LerpUnclamped(fromCameraSize, toCameraSize, t);
            if (controlsCanvasGroup != null) controlsCanvasGroup.alpha = opening ? t : 1f - t;
            UpdateMarkerPositions();
            yield return null;
        }

        minimapPanel.anchorMin = toAnchorMin;
        minimapPanel.anchorMax = toAnchorMax;
        minimapPanel.anchoredPosition = toPosition;
        minimapPanel.sizeDelta = toSize;
        minimapPanel.pivot = toPivot;
        minimapCamera.transform.position = toCameraPosition;
        minimapCamera.transform.rotation = toCameraRotation;
        minimapCamera.orthographicSize = toCameraSize;
        isAnimating = false;
        animationRoutine = null;

        if (opening)
        {
            UpdateMarkerPositions();
            yield break;
        }

        FinishClosingMap();
    }

    private void FinishClosingMap()
    {
        isExpanded = false;
        pinchActive = false;
        SetExpandedControlsVisible(false);

        if (minimapFollow != null)
        {
            minimapFollow.SetManualView(false);
            minimapFollow.SnapToPlayer();
        }

        RestoreCompactRenderTexture();

        if (UIPanelCoordinator.Instance != null)
            UIPanelCoordinator.Instance.ClosePanel(minimapPanel.gameObject);

        // ClosePanel deactivates managed panels. The minimap returns to its compact
        // HUD state immediately, then the unlock controller applies its saved gate.
        minimapPanel.gameObject.SetActive(true);
        MinimapUnlockController.RefreshAll();
        SetExpandButtonVisible(minimapPanel.gameObject.activeInHierarchy);
        RestorePlayerInput();
        UpdateMarkerPositions();
    }

    private void CaptureAndDisablePlayerInput()
    {
        inputManager = FindObjectOfType<InputManager>();
        if (inputManager == null) return;

        restoreMovementInput = inputManager.IsPlayerInputEnabled;
        restoreLookInput = inputManager.IsLookInputEnabled;
        inputCaptured = true;
        inputManager.SetPlayerInputEnable(false);
        inputManager.SetLookEnabled(false);
    }

    private void RestorePlayerInput()
    {
        if (!inputCaptured) return;
        if (inputManager != null)
        {
            inputManager.SetPlayerInputEnable(restoreMovementInput);
            inputManager.SetLookEnabled(restoreLookInput);
        }
        inputCaptured = false;
        inputManager = null;
    }

    private void SetExpandedControlsVisible(bool visible)
    {
        if (controlsRoot != null) controlsRoot.SetActive(visible);
        if (controlsCanvasGroup != null)
        {
            controlsCanvasGroup.alpha = visible ? 0f : 0f;
            controlsCanvasGroup.interactable = visible;
            controlsCanvasGroup.blocksRaycasts = visible;
        }
    }

    private void SetExpandButtonVisible(bool visible)
    {
        if (enlargeButton != null) enlargeButton.gameObject.SetActive(visible);
    }

    private Color GetMarkerColor(BuildLocation location)
    {
        if (location == navigationDestination)
            return navigationLocationColor;
        if (GameManager.Instance != null && GameManager.Instance.ActiveBuildLocation == location)
            return activeLocationColor;
        if (IsLocationCompleted(location))
            return completedLocationColor;
        if (location.activeContract != null)
            return availableLocationColor;
        return lockedLocationColor;
    }

    private static bool IsLocationCompleted(BuildLocation location)
    {
        if (location == null) return false;

        if (location.activeContract != null && PlayerDataManager.Instance != null &&
            PlayerDataManager.Instance.HasContractCompletionRecord(location.activeContract.ContractID))
        {
            return true;
        }

        return location.bakedBars != null && location.bakedBars.Count > 0;
    }

    private static string GetLocationLabel(BuildLocation location)
    {
        if (location == null) return "Build Location";
        string label = location.activeContract != null ? location.activeContract.name : location.name;
        return string.IsNullOrWhiteSpace(label) ? "Build Location" : label.Replace('_', ' ');
    }

    private static Vector3 GetLocationWorldPosition(BuildLocation location)
    {
        if (location == null) return Vector3.zero;
        if (location.navigationTarget != null) return location.navigationTarget.transform.position;

        Vector3 total = Vector3.zero;
        int count = 0;
        if (location.startingAnchors != null)
        {
            foreach (Point anchor in location.startingAnchors)
            {
                if (anchor == null) continue;
                total += anchor.transform.position;
                count++;
            }
        }
        if (location.endingAnchors != null)
        {
            foreach (Point anchor in location.endingAnchors)
            {
                if (anchor == null) continue;
                total += anchor.transform.position;
                count++;
            }
        }
        return count > 0 ? total / count : location.transform.position;
    }

    private static void Stretch(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }
}

/// <summary>Forwards Unity UI pointer gestures from the RawImage to the map camera.</summary>
[DisallowMultipleComponent]
public sealed class ExpandedMinimapInputSurface : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler
{
    internal ExpandedMinimapController Owner { get; set; }
    private Canvas canvas;

    private void Awake()
    {
        canvas = GetComponentInParent<Canvas>();
    }

    public void OnBeginDrag(PointerEventData eventData) { }

    public void OnDrag(PointerEventData eventData)
    {
        float scaleFactor = canvas != null ? Mathf.Max(0.01f, canvas.scaleFactor) : 1f;
        Owner?.PanByCanvasDelta(eventData.delta / scaleFactor);
    }

    public void OnEndDrag(PointerEventData eventData) { }

    public void OnScroll(PointerEventData eventData)
    {
        Owner?.ZoomFromScroll(eventData.scrollDelta.y);
    }
}
