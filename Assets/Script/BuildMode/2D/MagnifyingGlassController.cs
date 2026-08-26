using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Displays a RenderTexture magnifier centered on the current bridge-building
/// pointer and moves its UI window away from the player's hand.
/// </summary>
[DisallowMultipleComponent]
public sealed class MagnifyingGlassController : MonoBehaviour
{
    [Header("Camera And UI")]
    [SerializeField] private Camera magnifierCamera;
    [Tooltip("Optional. BarCreator's active build camera is preferred when assigned.")]
    [SerializeField] private Camera sourceCamera;
    [SerializeField] private BarCreator barCreator;
    [Tooltip("The complete frame/background to show and hide, not only the RawImage.")]
    [SerializeField] private GameObject magnifierRoot;
    [SerializeField] private RawImage magnifierImage;

    [Header("UI Sorting")]
    [Tooltip("Creates or configures a nested Canvas so the magnifier renders above other HUD canvases.")]
    [SerializeField] private bool forceTopMostCanvas = true;
    [Range(-32768, 32767)] [SerializeField] private int topMostSortingOrder = 32760;

    [Header("Blueprint Grid Overlay")]
    [Tooltip("The existing GridCanvas used by the active build location. If omitted, it is found automatically.")]
    [SerializeField] private Canvas sourceGridCanvas;
    [Tooltip("Mirrors the existing visual GridCanvas into the magnifier camera.")]
    [SerializeField] private bool includeBlueprintGrid = true;

    [Header("Screen Positions")]
    [Tooltip("Empty RectTransform marking the center of the top-left magnifier position.")]
    [SerializeField] private RectTransform topLeftSlot;
    [Tooltip("Empty RectTransform marking the center of the top-right magnifier position.")]
    [SerializeField] private RectTransform topRightSlot;
    [Min(0f)]
    [Tooltip("Extra screen-space distance kept between the finger and magnifier frame.")]
    [SerializeField] private float handAvoidancePaddingPixels = 140f;

    [Header("Magnification")]
    [Min(1f)] [SerializeField] private float magnification = 3f;
    [Min(0.01f)]
    [Tooltip("Fallback distance used only if the tracked point is behind the source camera.")]
    [SerializeField] private float minimumCameraDistance = 0.1f;
    [SerializeField] private bool copySourceCameraRotation = true;
    [SerializeField] private bool copySourceCameraCullingMask;
    [SerializeField] private bool copySourceCameraClipPlanes = true;

    [Header("Fallback Pointer Projection")]
    [Tooltip("Used when UpdateTrackedPosition is not being called by BarCreator.")]
    [SerializeField] private float fallbackBuildPlaneZ;

    [Header("Platform")]
    [Tooltip("Prevents the magnifier appearing in desktop builds.")]
    [SerializeField] private bool mobileOnly = true;
    [Tooltip("Allows mouse testing while running inside the Unity Editor.")]
    [SerializeField] private bool showInEditorForTesting = true;
    [SerializeField] private bool hideOnAwake = true;

    private RectTransform magnifierRect;
    private Canvas topMostCanvas;
    private RectTransform currentSlot;
    private Vector2 trackedScreenPosition;
    private Vector3 trackedWorldPosition;
    private bool hasTrackedWorldPosition;
    private bool isVisible;
    private bool referencesInitialized;
    private int lastExternalTrackingFrame = -1;
    private readonly Vector3[] uiWorldCorners = new Vector3[4];
    private Canvas magnifierGridCanvas;
    private Graphic[] sourceGridGraphics;
    private Graphic[] magnifierGridGraphics;

    public bool IsVisible => isVisible;

    private void Awake()
    {
        InitializeReferences();

        if (hideOnAwake && !isVisible && magnifierRoot != null)
            magnifierRoot.SetActive(false);
    }

    private void InitializeReferences()
    {
        if (referencesInitialized) return;
        referencesInitialized = true;

        if (barCreator == null)
            barCreator = FindObjectOfType<BarCreator>();

        if (sourceGridCanvas == null)
            sourceGridCanvas = FindSourceGridCanvas();

        if (magnifierRoot == null && magnifierImage != null)
            magnifierRoot = magnifierImage.gameObject;

        if (magnifierRoot != null)
        {
            magnifierRect = magnifierRoot.GetComponent<RectTransform>();
            ConfigureTopMostCanvas();
        }

        if (magnifierImage != null)
        {
            if (magnifierImage.texture == null && magnifierCamera != null)
                magnifierImage.texture = magnifierCamera.targetTexture;
        }

        // The entire magnifier frame is visual feedback and must not consume
        // the drag when it relocates beneath an already-moving finger.
        if (magnifierRoot != null)
        {
            foreach (Graphic graphic in magnifierRoot.GetComponentsInChildren<Graphic>(true))
                graphic.raycastTarget = false;
        }

        if (magnifierCamera != null)
            magnifierCamera.enabled = false;
    }

    private void LateUpdate()
    {
        if (!isVisible) return;

        if (TryGetCurrentPointerPosition(out Vector2 pointerPosition))
            trackedScreenPosition = pointerPosition;

        if (lastExternalTrackingFrame != Time.frameCount)
            TryProjectPointerToBuildPlane(trackedScreenPosition, out trackedWorldPosition);

        if (hasTrackedWorldPosition)
            UpdateMagnifierCamera(trackedWorldPosition);

        SyncBlueprintGridOverlay();

        UpdateAvoidance(trackedScreenPosition);
    }

    /// <summary>
    /// Shows the magnifier using the current touch or mouse position.
    /// Suitable for TutorialStep UnityEvents and other parameterless callbacks.
    /// </summary>
    public void ShowMagnifier()
    {
        InitializeReferences();

        if (!TryGetCurrentPointerPosition(out Vector2 screenPosition))
            screenPosition = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        ShowMagnifier(screenPosition);
    }

    public void ShowMagnifier(Vector2 screenPosition)
    {
        InitializeReferences();
        if (!CanDisplayOnCurrentPlatform()) return;

        trackedScreenPosition = screenPosition;
        hasTrackedWorldPosition = TryProjectPointerToBuildPlane(
            screenPosition,
            out trackedWorldPosition);
        ActivateMagnifier();
    }

    /// <summary>
    /// Preferred bridge-builder entry point. The world position can already
    /// include grid, node, tutorial, budget, and maximum-length snapping.
    /// </summary>
    public void ShowMagnifier(Vector2 screenPosition, Vector3 worldPosition)
    {
        InitializeReferences();
        if (!CanDisplayOnCurrentPlatform()) return;

        trackedScreenPosition = screenPosition;
        trackedWorldPosition = worldPosition;
        hasTrackedWorldPosition = true;
        lastExternalTrackingFrame = Time.frameCount;
        ActivateMagnifier();
    }

    /// <summary>
    /// Updates the exact point being previewed by BarCreator while dragging.
    /// </summary>
    public void UpdateTrackedPosition(Vector2 screenPosition, Vector3 worldPosition)
    {
        if (!isVisible) return;

        trackedScreenPosition = screenPosition;
        trackedWorldPosition = worldPosition;
        hasTrackedWorldPosition = true;
        lastExternalTrackingFrame = Time.frameCount;
    }

    public void HideMagnifier()
    {
        isVisible = false;
        hasTrackedWorldPosition = false;
        lastExternalTrackingFrame = -1;

        if (magnifierCamera != null)
            magnifierCamera.enabled = false;

        SetBlueprintGridOverlayActive(false);

        if (magnifierRoot != null)
            magnifierRoot.SetActive(false);
    }

    private void ActivateMagnifier()
    {
        if (magnifierCamera == null || magnifierRoot == null || magnifierRect == null)
        {
            Debug.LogWarning(
                "MagnifyingGlassController needs a Magnifier Camera and Magnifier Root.",
                this);
            return;
        }

        if (magnifierCamera.targetTexture == null)
        {
            Debug.LogWarning(
                "The magnifier camera has no Target Texture assigned.",
                magnifierCamera);
            return;
        }

        if (magnifierImage != null && magnifierImage.texture != magnifierCamera.targetTexture)
            magnifierImage.texture = magnifierCamera.targetTexture;

        EnsureBlueprintGridOverlay();
        isVisible = true;
        magnifierRoot.SetActive(true);
        BringMagnifierToFront();
        magnifierCamera.enabled = true;
        SnapToFarthestSlot(trackedScreenPosition);

        if (hasTrackedWorldPosition)
            UpdateMagnifierCamera(trackedWorldPosition);

        SyncBlueprintGridOverlay();
    }

    private void ConfigureTopMostCanvas()
    {
        if (!forceTopMostCanvas || magnifierRoot == null) return;

        topMostCanvas = magnifierRoot.GetComponent<Canvas>();
        if (topMostCanvas == null)
            topMostCanvas = magnifierRoot.AddComponent<Canvas>();

        topMostCanvas.overrideSorting = true;
        topMostCanvas.sortingOrder = topMostSortingOrder;
    }

    private void BringMagnifierToFront()
    {
        if (magnifierRoot == null) return;

        magnifierRoot.transform.SetAsLastSibling();
        if (topMostCanvas == null && forceTopMostCanvas)
            ConfigureTopMostCanvas();

        if (topMostCanvas != null)
        {
            topMostCanvas.overrideSorting = true;
            topMostCanvas.sortingOrder = topMostSortingOrder;
        }
    }

    private Camera ResolveSourceCamera()
    {
        if (barCreator != null)
        {
            Camera buildCamera = barCreator.GetActiveCamera();
            if (buildCamera != null && buildCamera != magnifierCamera)
                return buildCamera;
        }

        if (sourceCamera != null && sourceCamera != magnifierCamera)
            return sourceCamera;

        Camera mainCamera = Camera.main;
        return mainCamera != magnifierCamera ? mainCamera : null;
    }

    private bool TryProjectPointerToBuildPlane(Vector2 screenPosition, out Vector3 worldPosition)
    {
        Camera cameraToUse = ResolveSourceCamera();
        if (cameraToUse == null)
        {
            worldPosition = Vector3.zero;
            hasTrackedWorldPosition = false;
            return false;
        }

        float buildPlaneZ = fallbackBuildPlaneZ;
        if (barCreator != null)
        {
            if (barCreator.currentStartPoint != null)
                buildPlaneZ = barCreator.currentStartPoint.transform.position.z;
            else if (Point.AllPoints.Count > 0 && Point.AllPoints[0] != null)
                buildPlaneZ = Point.AllPoints[0].transform.position.z;
        }

        Plane buildPlane = new Plane(Vector3.back, new Vector3(0f, 0f, buildPlaneZ));
        Ray pointerRay = cameraToUse.ScreenPointToRay(screenPosition);
        if (!buildPlane.Raycast(pointerRay, out float distance))
        {
            worldPosition = Vector3.zero;
            hasTrackedWorldPosition = false;
            return false;
        }

        worldPosition = pointerRay.GetPoint(distance);
        hasTrackedWorldPosition = true;
        return true;
    }

    private void UpdateMagnifierCamera(Vector3 targetWorldPosition)
    {
        Camera cameraToCopy = ResolveSourceCamera();
        if (cameraToCopy == null || magnifierCamera == null) return;

        Quaternion targetRotation = copySourceCameraRotation
            ? cameraToCopy.transform.rotation
            : magnifierCamera.transform.rotation;
        Vector3 forward = targetRotation * Vector3.forward;

        float distanceFromViewPlane = Vector3.Dot(
            targetWorldPosition - cameraToCopy.transform.position,
            cameraToCopy.transform.forward);
        if (distanceFromViewPlane < minimumCameraDistance)
            distanceFromViewPlane = Mathf.Max(
                minimumCameraDistance,
                Vector3.Distance(cameraToCopy.transform.position, targetWorldPosition));

        magnifierCamera.transform.SetPositionAndRotation(
            targetWorldPosition - forward * distanceFromViewPlane,
            targetRotation);

        magnifierCamera.orthographic = cameraToCopy.orthographic;
        if (cameraToCopy.orthographic)
        {
            magnifierCamera.orthographicSize =
                Mathf.Max(0.01f, cameraToCopy.orthographicSize / magnification);
        }
        else
        {
            magnifierCamera.fieldOfView = Mathf.Clamp(
                cameraToCopy.fieldOfView / magnification,
                1f,
                179f);
        }

        if (copySourceCameraCullingMask)
            magnifierCamera.cullingMask = cameraToCopy.cullingMask;

        if (copySourceCameraClipPlanes)
        {
            magnifierCamera.nearClipPlane = cameraToCopy.nearClipPlane;
            magnifierCamera.farClipPlane = cameraToCopy.farClipPlane;
        }

        RenderTexture texture = magnifierCamera.targetTexture;
        if (texture != null && texture.height > 0)
            magnifierCamera.aspect = (float)texture.width / texture.height;

        UpdateBlueprintGridCameraSettings();
    }

    private Canvas FindSourceGridCanvas()
    {
        BuildLocation activeLocation = GameManager.Instance != null
            ? GameManager.Instance.ActiveBuildLocation
            : null;
        if (activeLocation != null && activeLocation.gridImage != null)
        {
            Canvas locationCanvas = activeLocation.gridImage.GetComponentInParent<Canvas>(true);
            if (locationCanvas != null)
                return locationCanvas;
        }

        foreach (Canvas canvas in FindObjectsOfType<Canvas>(true))
        {
            if (canvas.name == "GridCanvas")
                return canvas;
        }

        return null;
    }

    private void EnsureBlueprintGridOverlay()
    {
        if (!includeBlueprintGrid || magnifierGridCanvas != null || magnifierCamera == null)
            return;

        if (sourceGridCanvas == null)
            sourceGridCanvas = FindSourceGridCanvas();
        if (sourceGridCanvas == null)
            return;

        GameObject clone = Instantiate(sourceGridCanvas.gameObject);
        clone.name = sourceGridCanvas.name + " (Magnifier Only)";
        clone.transform.SetParent(null, false);

        magnifierGridCanvas = clone.GetComponent<Canvas>();
        if (magnifierGridCanvas == null)
        {
            Destroy(clone);
            return;
        }

        magnifierGridCanvas.renderMode = RenderMode.ScreenSpaceCamera;
        magnifierGridCanvas.worldCamera = magnifierCamera;

        foreach (GraphicRaycaster raycaster in clone.GetComponentsInChildren<GraphicRaycaster>(true))
            raycaster.enabled = false;

        sourceGridGraphics = sourceGridCanvas.GetComponentsInChildren<Graphic>(true);
        magnifierGridGraphics = clone.GetComponentsInChildren<Graphic>(true);
        foreach (Graphic graphic in magnifierGridGraphics)
            graphic.raycastTarget = false;

        UpdateBlueprintGridCameraSettings();
        clone.SetActive(false);
    }

    private void UpdateBlueprintGridCameraSettings()
    {
        if (magnifierGridCanvas == null || magnifierCamera == null)
            return;

        float minimumDistance = magnifierCamera.nearClipPlane + 0.01f;
        float maximumDistance = Mathf.Max(
            minimumDistance,
            magnifierCamera.farClipPlane - 0.01f);

        magnifierGridCanvas.worldCamera = magnifierCamera;
        magnifierGridCanvas.planeDistance = Mathf.Clamp(
            sourceGridCanvas != null ? sourceGridCanvas.planeDistance : minimumDistance,
            minimumDistance,
            maximumDistance);
    }

    private void SyncBlueprintGridOverlay()
    {
        if (!includeBlueprintGrid)
        {
            SetBlueprintGridOverlayActive(false);
            return;
        }

        EnsureBlueprintGridOverlay();
        if (magnifierGridCanvas == null || sourceGridCanvas == null)
            return;

        bool shouldShow = isVisible
            && magnifierCamera != null
            && magnifierCamera.enabled
            && sourceGridCanvas.gameObject.activeInHierarchy;
        SetBlueprintGridOverlayActive(shouldShow);
        if (!shouldShow || sourceGridGraphics == null || magnifierGridGraphics == null)
            return;

        int count = Mathf.Min(sourceGridGraphics.Length, magnifierGridGraphics.Length);
        for (int i = 0; i < count; i++)
        {
            Graphic source = sourceGridGraphics[i];
            Graphic mirrored = magnifierGridGraphics[i];
            if (source == null || mirrored == null)
                continue;

            mirrored.gameObject.SetActive(source.gameObject.activeSelf);
            mirrored.enabled = source.enabled;
            mirrored.color = source.color;
            mirrored.material = source.material;
        }
    }

    private void SetBlueprintGridOverlayActive(bool active)
    {
        if (magnifierGridCanvas != null
            && magnifierGridCanvas.gameObject.activeSelf != active)
        {
            magnifierGridCanvas.gameObject.SetActive(active);
        }
    }

    private void OnDestroy()
    {
        if (magnifierGridCanvas != null)
            Destroy(magnifierGridCanvas.gameObject);
    }

    private void UpdateAvoidance(Vector2 pointerScreenPosition)
    {
        if (magnifierRect == null || topLeftSlot == null || topRightSlot == null)
            return;

        // Keep the window attached to its slot if resolution, safe-area, or
        // orientation changes while the player is still dragging.
        PositionAtSlot(currentSlot);

        float distanceToWindow = DistanceToRectInScreenPixels(
            pointerScreenPosition,
            magnifierRect);
        if (distanceToWindow <= handAvoidancePaddingPixels)
            SnapToFarthestSlot(pointerScreenPosition);
    }

    private void SnapToFarthestSlot(Vector2 pointerScreenPosition)
    {
        if (magnifierRect == null) return;

        RectTransform chosenSlot;
        if (topLeftSlot == null) chosenSlot = topRightSlot;
        else if (topRightSlot == null) chosenSlot = topLeftSlot;
        else
        {
            float leftDistance = (GetRectCenterScreenPoint(topLeftSlot) - pointerScreenPosition).sqrMagnitude;
            float rightDistance = (GetRectCenterScreenPoint(topRightSlot) - pointerScreenPosition).sqrMagnitude;
            chosenSlot = leftDistance >= rightDistance ? topLeftSlot : topRightSlot;
        }

        if (chosenSlot == null) return;

        currentSlot = chosenSlot;
        PositionAtSlot(chosenSlot);
    }

    private void PositionAtSlot(RectTransform slot)
    {
        if (magnifierRect == null || slot == null) return;
        magnifierRect.position = slot.TransformPoint(slot.rect.center);
    }

    private float DistanceToRectInScreenPixels(
        Vector2 screenPoint,
        RectTransform rectTransform)
    {
        rectTransform.GetWorldCorners(uiWorldCorners);
        Camera uiCamera = GetCanvasCamera(rectTransform);

        Vector2 minimum = RectTransformUtility.WorldToScreenPoint(uiCamera, uiWorldCorners[0]);
        Vector2 maximum = minimum;
        for (int i = 1; i < uiWorldCorners.Length; i++)
        {
            Vector2 corner = RectTransformUtility.WorldToScreenPoint(uiCamera, uiWorldCorners[i]);
            minimum = Vector2.Min(minimum, corner);
            maximum = Vector2.Max(maximum, corner);
        }

        Vector2 closest = new Vector2(
            Mathf.Clamp(screenPoint.x, minimum.x, maximum.x),
            Mathf.Clamp(screenPoint.y, minimum.y, maximum.y));
        return Vector2.Distance(screenPoint, closest);
    }

    private static Vector2 GetRectCenterScreenPoint(RectTransform rectTransform)
    {
        Vector3 centerWorld = rectTransform.TransformPoint(rectTransform.rect.center);
        return RectTransformUtility.WorldToScreenPoint(
            GetCanvasCamera(rectTransform),
            centerWorld);
    }

    private static Camera GetCanvasCamera(RectTransform rectTransform)
    {
        Canvas canvas = rectTransform != null ? rectTransform.GetComponentInParent<Canvas>() : null;
        Canvas rootCanvas = canvas != null ? canvas.rootCanvas : null;
        return rootCanvas != null && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? rootCanvas.worldCamera
            : null;
    }

    private static bool TryGetCurrentPointerPosition(out Vector2 position)
    {
        Touchscreen touchscreen = Touchscreen.current;
        if (touchscreen != null && touchscreen.primaryTouch.press.isPressed)
        {
            position = touchscreen.primaryTouch.position.ReadValue();
            return true;
        }

        Pointer pointer = Pointer.current;
        if (pointer != null)
        {
            position = pointer.position.ReadValue();
            return true;
        }

        position = Vector2.zero;
        return false;
    }

    private bool CanDisplayOnCurrentPlatform()
    {
        if (!mobileOnly || Application.isMobilePlatform) return true;

#if UNITY_EDITOR
        return showInEditorForTesting;
#else
        return false;
#endif
    }
}
