using UnityEngine;
using UnityEngine.UI;

public class TutorialPointer : MonoBehaviour
{
    [Header("Bounce Settings")]
    public float bounceSpeed = 8f;
    public float bounceAmount = 15f;

    [Header("Rendering")]
    [Tooltip("Keeps the pointer above other screen-space canvases, including tutorial warning panels.")]
    [SerializeField] private bool renderOnTop = true;
    [SerializeField] private int pointerSortingOrder = 32000;

    private RectTransform target;
    private Vector2 customOffset;
    private RectTransform rectTransform;
    private Canvas pointerCanvas;
    private bool fitBuildTarget;
    private float buildPreferredRotation;
    private readonly Vector3[] targetCorners = new Vector3[4];

    private bool IsSuppressedByModal
    {
        get
        {
            UIPanelCoordinator coordinator = UIPanelCoordinator.Instance;
            return coordinator != null && coordinator.HasOpenPanel &&
                   !coordinator.IsTargetInsideTopPanel(target);
        }
    }

    public bool IsPointingAt(RectTransform candidate)
    {
        return candidate != null && target == candidate && gameObject.activeInHierarchy;
    }

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();

        if (renderOnTop)
        {
            pointerCanvas = GetComponent<Canvas>();
            if (pointerCanvas == null) pointerCanvas = gameObject.AddComponent<Canvas>();
            pointerCanvas.overrideSorting = true;
            pointerCanvas.sortingOrder = pointerSortingOrder;
        }

        // Tutorial arrows are visual guidance only. They must never intercept the
        // button or build-area pointer event that they are pointing toward.
        foreach (Graphic graphic in GetComponentsInChildren<Graphic>(true))
            graphic.raycastTarget = false;

        CanvasGroup canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup != null)
        {
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
    }

    private void OnEnable()
    {
        // SAFETY FIX: If a parent Canvas turns off and on, Unity forces all children to turn on.
        // We must instantly hide if we have no target so we don't float in mid-air!
        if (target == null)
        {
            gameObject.SetActive(false);
            return;
        }

        RefreshModalVisibility();
    }

    public void PointAt(RectTransform newTarget, Vector2 offset)
    {
        fitBuildTarget = false;
        if (newTarget == null)
        {
            Hide();
            return;
        }

        target = newTarget;
        customOffset = offset;

        gameObject.SetActive(true);
        RefreshModalVisibility();
        transform.SetAsLastSibling();
        if (!IsSuppressedByModal) UpdatePosition();
    }

    public void PointAt(RectTransform newTarget)
    {
        fitBuildTarget = false;
        if (newTarget == null)
        {
            Hide();
            return;
        }

        target = newTarget;
        customOffset = Vector2.zero;

        gameObject.SetActive(true);
        RefreshModalVisibility();
        transform.SetAsLastSibling();
        if (!IsSuppressedByModal) UpdatePosition();
    }

    /// <summary>Build-only edge placement. Keeps existing non-build authored offsets unchanged.</summary>
    public void PointAtBuildTarget(RectTransform newTarget, Vector2 offset, float rotation)
    {
        if (newTarget == null || !newTarget.gameObject.activeInHierarchy) { Hide(); return; }
        fitBuildTarget = true;
        buildPreferredRotation = rotation;
        target = newTarget;
        customOffset = offset;
        gameObject.SetActive(true);
        RefreshModalVisibility();
        transform.SetAsLastSibling();
        if (!IsSuppressedByModal) UpdatePosition();
    }

    public void Hide()
    {
        target = null;
        fitBuildTarget = false;
        gameObject.SetActive(false);
    }

    private void LateUpdate()
    {
        if (target == null || !target.gameObject.activeInHierarchy)
        {
            Hide();
            return;
        }

        // TutorialManager intentionally refreshes its target every frame. Keep the
        // pointer hidden over unrelated modals, but allow it when the tutorial is
        // explicitly pointing at a control inside the currently open modal.
        RefreshModalVisibility();
        if (IsSuppressedByModal) return;

        UpdatePosition();
    }

    private void RefreshModalVisibility()
    {
        if (pointerCanvas != null)
            pointerCanvas.enabled = !IsSuppressedByModal;
    }

    private void UpdatePosition()
    {
        if (target == null || rectTransform == null) return;

        Canvas targetCanvas = target.GetComponentInParent<Canvas>();
        Canvas targetRootCanvas = targetCanvas != null ? targetCanvas.rootCanvas : null;
        Camera targetCamera = targetRootCanvas != null &&
                              targetRootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? targetRootCanvas.worldCamera
            : null;

        Vector3 targetCenterWorld = target.TransformPoint(target.rect.center);
        Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(
            targetCamera,
            targetCenterWorld);

        Canvas pointerRootCanvas = pointerCanvas != null ? pointerCanvas.rootCanvas : null;
        float canvasScale = pointerRootCanvas != null
            ? Mathf.Max(0.0001f, pointerRootCanvas.scaleFactor)
            : 1f;

        Camera pointerCamera = pointerRootCanvas != null &&
                               pointerRootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? pointerRootCanvas.worldCamera : null;
        if (fitBuildTarget)
        {
            target.GetWorldCorners(targetCorners);
            Vector2 lower = RectTransformUtility.WorldToScreenPoint(targetCamera, targetCorners[0]);
            Vector2 upper = lower;
            for (int i = 1; i < targetCorners.Length; i++)
            {
                Vector2 point = RectTransformUtility.WorldToScreenPoint(targetCamera, targetCorners[i]);
                lower = Vector2.Min(lower, point); upper = Vector2.Max(upper, point);
            }
            // Scroll-content rectangles can be much wider than the visible
            // material toolbar. Aim at the visible clipped area, not its hidden
            // off-screen content center.
            RectMask2D clip = target.GetComponentInParent<RectMask2D>();
            if (clip != null && clip.isActiveAndEnabled)
            {
                RectTransform clipRect = clip.transform as RectTransform;
                clipRect.GetWorldCorners(targetCorners);
                Vector2 clipMin = RectTransformUtility.WorldToScreenPoint(targetCamera, targetCorners[0]), clipMax = clipMin;
                for (int i = 1; i < targetCorners.Length; i++)
                {
                    Vector2 point = RectTransformUtility.WorldToScreenPoint(targetCamera, targetCorners[i]);
                    clipMin = Vector2.Min(clipMin, point); clipMax = Vector2.Max(clipMax, point);
                }
                lower = Vector2.Max(lower, clipMin); upper = Vector2.Min(upper, clipMax);
                if (lower.x >= upper.x || lower.y >= upper.y)
                {
                    if (pointerCanvas != null) pointerCanvas.enabled = false;
                    return;
                }
            }
            Vector3 center = rectTransform.TransformPoint(rectTransform.rect.center);
            Vector2 centerPixel = RectTransformUtility.WorldToScreenPoint(pointerCamera, center);
            float width = (RectTransformUtility.WorldToScreenPoint(pointerCamera,
                rectTransform.TransformPoint(rectTransform.rect.center + Vector2.right * rectTransform.rect.width)) - centerPixel).magnitude;
            float height = (RectTransformUtility.WorldToScreenPoint(pointerCamera,
                rectTransform.TransformPoint(rectTransform.rect.center + Vector2.up * rectTransform.rect.height)) - centerPixel).magnitude;
            Rect safe = pointerCamera != null && pointerCamera.targetTexture != null ? pointerCamera.pixelRect : Screen.safeArea;
            if (safe.width <= 0 || safe.height <= 0) safe = new Rect(0, 0, Screen.width, Screen.height);
            float margin = 8f * canvasScale;
            var placement = BuildTutorialPointerLayout.Resolve((lower.x + upper.x) * .5f, (lower.y + upper.y) * .5f,
                upper.x - lower.x, upper.y - lower.y, width, height,
                customOffset.x * canvasScale, customOffset.y * canvasScale, buildPreferredRotation,
                bounceAmount * canvasScale, 10f * canvasScale,
                safe.xMin + margin, safe.yMin + margin, safe.xMax - margin, safe.yMax - margin);
            rectTransform.localEulerAngles = new Vector3(0, 0, placement.rotation);
            screenPoint = new Vector2(placement.x, placement.y);
        }

        // Offset in canvas/screen space instead of the target's local space.
        // This keeps arrows accurate for buttons with non-uniform nested scales.
        float bounce = Mathf.Sin(Time.unscaledTime * bounceSpeed) * bounceAmount;
        float rotationRadians = rectTransform.eulerAngles.z * Mathf.Deg2Rad;
        Vector2 bounceDirection = new Vector2(
            -Mathf.Sin(rotationRadians),
            Mathf.Cos(rotationRadians));
        screenPoint += ((fitBuildTarget ? Vector2.zero : customOffset) + bounceDirection * bounce) * canvasScale;
        if (fitBuildTarget)
            screenPoint -= RectTransformUtility.WorldToScreenPoint(pointerCamera, rectTransform.TransformPoint(rectTransform.rect.center)) -
                           RectTransformUtility.WorldToScreenPoint(pointerCamera, rectTransform.position);

        RectTransform pointerParent = rectTransform.parent as RectTransform;
        if (pointerParent != null && RectTransformUtility.ScreenPointToWorldPointInRectangle(
                pointerParent,
                screenPoint,
                pointerCamera,
                out Vector3 pointerWorldPosition))
        {
            rectTransform.position = pointerWorldPosition;
        }
        else
        {
            rectTransform.position = screenPoint;
        }
    }
}
