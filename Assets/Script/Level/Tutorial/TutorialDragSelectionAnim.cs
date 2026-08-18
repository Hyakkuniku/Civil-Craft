using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One-off world-space selection-box demonstration for the build tutorial.
/// The cube's authored scene position, rotation, and scale define the final
/// selection volume. Attach this directly to that cube.
/// </summary>
[DisallowMultipleComponent]
public class TutorialDragSelectionAnim : MonoBehaviour
{
    private static TutorialDragSelectionAnim activeIndicator;

    [Header("3D Growth")]
    [Tooltip("Starting size as a fraction of the cube scale authored in the scene.")]
    [Range(0.001f, 1f)]
    public float startScaleMultiplier = 0.04f;

    [Tooltip("Corner held in place while the cube grows. Use -1 or 1 per axis, or 0 to grow equally on that axis.")]
    public Vector3 anchoredCorner = new Vector3(-1f, 1f, -1f);

    [Header("Timing")]
    [Min(0.01f)] public float dragDuration = 1.25f;
    [Min(0f)] public float endHoldDuration = 0.35f;
    [Min(0f)] public float restartDelay = 0.15f;
    public bool useUnscaledTime = true;

    [Header("Easing")]
    public AnimationCurve dragCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Selection Completion")]
    [Tooltip("How many player bars must exist inside the final cube before it can complete the step.")]
    [Min(1)] public int minimumRequiredBars = 1;

    [Tooltip("Extra local-space allowance around the cube when finding required bars.")]
    [Min(0f)] public float volumePadding = 0.05f;

    [Tooltip("Prevents this tutorial visual from blocking build and selection raycasts.")]
    public bool disableColliders = true;

    public Vector3 FinalSceneScale => finalLocalScale;

    private Vector3 finalLocalPosition;
    private Quaternion finalLocalRotation;
    private Vector3 finalLocalScale;
    private Matrix4x4 finalWorldToLocalMatrix;
    private Bounds localMeshBounds;
    private Renderer[] visualRenderers;
    private bool[] authoredRendererStates;
    private float phaseTime;
    private bool initialized;
    private bool selectionCompleted;
    private bool visualHiddenForDrag;

    private void Start()
    {
        // Capture the exact transform authored on the scene cube before this
        // component changes its scale or position for the first time.
        CaptureAuthoredTransform();
        SetVisualsVisible(true);
        ApplyGrowth(0f);
    }

    private void OnEnable()
    {
        activeIndicator = this;
        selectionCompleted = false;
        visualHiddenForDrag = false;
        phaseTime = 0f;
        if (initialized)
        {
            SetVisualsVisible(true);
            ApplyGrowth(0f);
        }
    }

    private void OnDisable()
    {
        if (activeIndicator == this)
            activeIndicator = null;
    }

    private void Update()
    {
        float deltaTime = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        phaseTime += deltaTime;

        if (phaseTime <= dragDuration)
        {
            float normalizedTime = Mathf.Clamp01(phaseTime / dragDuration);
            float easedTime = dragCurve != null ? dragCurve.Evaluate(normalizedTime) : normalizedTime;
            ApplyGrowth(easedTime);
            return;
        }

        if (phaseTime <= dragDuration + endHoldDuration)
        {
            ApplyGrowth(1f);
            return;
        }

        ApplyGrowth(0f);
        if (phaseTime >= dragDuration + endHoldDuration + restartDelay)
            phaseTime = 0f;
    }

    /// <summary>May be called by a TutorialStep OnStepStart UnityEvent.</summary>
    public void Show()
    {
        gameObject.SetActive(true);
        selectionCompleted = false;
        visualHiddenForDrag = false;
        phaseTime = 0f;
        if (initialized)
        {
            SetVisualsVisible(true);
            ApplyGrowth(0f);
        }
    }

    public void Hide()
    {
        RestoreAuthoredTransform();
        gameObject.SetActive(false);
    }

    /// <summary>Called by BarCreator after the Selection tool changes selection.</summary>
    public static void NotifySelectionChanged(BarCreator creator)
    {
        if (activeIndicator != null)
            activeIndicator.TryCompleteSelectionStep(creator);
    }

    /// <summary>Hides the visual while the player is drawing a selection box.</summary>
    public static void NotifySelectionDragStarted()
    {
        if (activeIndicator == null || !activeIndicator.initialized)
            return;

        activeIndicator.visualHiddenForDrag = true;
        activeIndicator.SetVisualsVisible(false);
    }

    private void TryCompleteSelectionStep(BarCreator creator)
    {
        if (!initialized || selectionCompleted || creator == null || !gameObject.activeInHierarchy)
            return;

        List<Bar> requiredBars = FindRequiredPlayerBars(creator);
        if (requiredBars.Count < minimumRequiredBars)
        {
            RestoreAfterFailedSelection();
            return;
        }

        HashSet<Bar> selected = new HashSet<Bar>(creator.selectedBars);
        foreach (Bar requiredBar in requiredBars)
        {
            if (!selected.Contains(requiredBar))
            {
                RestoreAfterFailedSelection();
                return;
            }
        }

        selectionCompleted = true;
        Hide();

        if (TutorialManager.Instance != null && TutorialManager.Instance.IsTutorialActive)
            TutorialManager.Instance.ShowNextStep();
    }

    private void RestoreAfterFailedSelection()
    {
        if (!visualHiddenForDrag)
            return;

        visualHiddenForDrag = false;
        phaseTime = 0f;
        ApplyGrowth(0f);
        SetVisualsVisible(true);
    }

    private List<Bar> FindRequiredPlayerBars(BarCreator creator)
    {
        List<Bar> required = new List<Bar>();
        HashSet<Bar> authoredBars = new HashSet<Bar>();

        BuildLocation activeLocation = GameManager.Instance != null
            ? GameManager.Instance.ActiveBuildLocation
            : null;

        if (activeLocation != null && activeLocation.bakedBars != null)
        {
            foreach (Bar bakedBar in activeLocation.bakedBars)
                if (bakedBar != null) authoredBars.Add(bakedBar);
        }

        Bar[] candidates = creator.barParent != null
            ? creator.barParent.GetComponentsInChildren<Bar>(false)
            : FindObjectsOfType<Bar>(false);

        foreach (Bar bar in candidates)
        {
            if (bar == null || !bar.gameObject.activeInHierarchy || authoredBars.Contains(bar))
                continue;
            if (bar.startPoint == null || bar.endPoint == null || bar.materialData == null)
                continue;
            if (SegmentIntersectsFinalVolume(
                    bar.startPoint.transform.position,
                    bar.endPoint.transform.position))
            {
                required.Add(bar);
            }
        }

        return required;
    }

    private bool SegmentIntersectsFinalVolume(Vector3 worldStart, Vector3 worldEnd)
    {
        Vector3 start = finalWorldToLocalMatrix.MultiplyPoint3x4(worldStart);
        Vector3 end = finalWorldToLocalMatrix.MultiplyPoint3x4(worldEnd);

        Bounds bounds = localMeshBounds;
        bounds.Expand(volumePadding * 2f);
        if (bounds.Contains(start) || bounds.Contains(end))
            return true;

        Vector3 direction = end - start;
        float enter = 0f;
        float exit = 1f;

        return IntersectsAxis(start.x, direction.x, bounds.min.x, bounds.max.x, ref enter, ref exit)
            && IntersectsAxis(start.y, direction.y, bounds.min.y, bounds.max.y, ref enter, ref exit)
            && IntersectsAxis(start.z, direction.z, bounds.min.z, bounds.max.z, ref enter, ref exit);
    }

    private static bool IntersectsAxis(
        float origin,
        float direction,
        float minimum,
        float maximum,
        ref float enter,
        ref float exit)
    {
        if (Mathf.Abs(direction) < 0.00001f)
            return origin >= minimum && origin <= maximum;

        float inverse = 1f / direction;
        float first = (minimum - origin) * inverse;
        float second = (maximum - origin) * inverse;
        if (first > second)
        {
            float swap = first;
            first = second;
            second = swap;
        }

        enter = Mathf.Max(enter, first);
        exit = Mathf.Min(exit, second);
        return enter <= exit;
    }

    private void CaptureAuthoredTransform()
    {
        if (initialized)
            return;

        finalLocalPosition = transform.localPosition;
        finalLocalRotation = transform.localRotation;
        finalLocalScale = transform.localScale;
        finalWorldToLocalMatrix = transform.worldToLocalMatrix;

        MeshFilter meshFilter = GetComponent<MeshFilter>();
        localMeshBounds = meshFilter != null && meshFilter.sharedMesh != null
            ? meshFilter.sharedMesh.bounds
            : new Bounds(Vector3.zero, Vector3.one);

        visualRenderers = GetComponentsInChildren<Renderer>(true);
        authoredRendererStates = new bool[visualRenderers.Length];
        for (int i = 0; i < visualRenderers.Length; i++)
            authoredRendererStates[i] = visualRenderers[i] != null && visualRenderers[i].enabled;

        initialized = true;

        if (!disableColliders)
            return;

        foreach (Collider collider3D in GetComponentsInChildren<Collider>(true))
            collider3D.enabled = false;
        foreach (Collider2D collider2D in GetComponentsInChildren<Collider2D>(true))
            collider2D.enabled = false;
    }

    private void SetVisualsVisible(bool visible)
    {
        if (visualRenderers == null)
            return;

        for (int i = 0; i < visualRenderers.Length; i++)
        {
            if (visualRenderers[i] != null)
                visualRenderers[i].enabled = visible && authoredRendererStates[i];
        }
    }

    private void ApplyGrowth(float normalizedGrowth)
    {
        if (!initialized)
            return;

        float scaleFactor = Mathf.Lerp(startScaleMultiplier, 1f, Mathf.Clamp01(normalizedGrowth));
        Vector3 currentScale = finalLocalScale * scaleFactor;
        Vector3 signedHalfDifference = Vector3.Scale(
            anchoredCorner,
            (finalLocalScale - currentScale) * 0.5f);

        transform.localRotation = finalLocalRotation;
        transform.localScale = currentScale;
        transform.localPosition = finalLocalPosition
            + finalLocalRotation * signedHalfDifference;
    }

    private void RestoreAuthoredTransform()
    {
        if (!initialized)
            return;

        transform.localPosition = finalLocalPosition;
        transform.localRotation = finalLocalRotation;
        transform.localScale = finalLocalScale;
    }
}
