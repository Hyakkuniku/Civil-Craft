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

    public void Hide()
    {
        target = null;
        gameObject.SetActive(false);
    }

    private void Update()
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

        // Offset in canvas/screen space instead of the target's local space.
        // This keeps arrows accurate for buttons with non-uniform nested scales.
        float bounce = Mathf.Sin(Time.unscaledTime * bounceSpeed) * bounceAmount;
        float rotationRadians = rectTransform.eulerAngles.z * Mathf.Deg2Rad;
        Vector2 bounceDirection = new Vector2(
            -Mathf.Sin(rotationRadians),
            Mathf.Cos(rotationRadians));
        screenPoint += (customOffset + bounceDirection * bounce) * canvasScale;

        RectTransform pointerParent = rectTransform.parent as RectTransform;
        Camera pointerCamera = pointerRootCanvas != null &&
                               pointerRootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? pointerRootCanvas.worldCamera
            : null;

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
