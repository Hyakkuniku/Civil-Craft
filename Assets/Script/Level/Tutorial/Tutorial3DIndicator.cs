using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class Tutorial3DIndicator : MonoBehaviour
{
    private static readonly HashSet<Tutorial3DIndicator> activeIndicators =
        new HashSet<Tutorial3DIndicator>();

    [Header("Target Position")]
    [Tooltip("World-space offset from the highlighted anchor.")]
    public Vector3 positionOffset = Vector3.zero;
    [Tooltip("Keep following the anchor if it moves.")]
    public bool followTarget = true;

    [Header("Bobbing")]
    public bool useBobbing = true;
    public Vector3 bobAxis = Vector3.up;
    [Min(0f)] public float bobDistance = 0.35f;
    [Min(0f)] public float bobSpeed = 3f;

    [Header("Spinning")]
    public bool useSpinning = true;
    public Vector3 spinAxis = Vector3.up;
    public float spinDegreesPerSecond = 90f;

    [Header("Scale Pulse")]
    public bool useScalePulse = true;
    [Range(0f, 0.9f)] public float pulseAmount = 0.15f;
    [Min(0f)] public float pulseSpeed = 4f;

    [Header("Safety")]
    [Tooltip("Prevents the visual from blocking clicks on the real anchor.")]
    public bool disableColliders = true;
    [Tooltip("The scene indicator hides itself until ShowAtPosition is called.")]
    public bool startHidden = true;

    public Transform CurrentTarget => targetAnchor;

    private Transform targetAnchor;
    private Vector3 stationaryTargetPosition;
    private Vector3 originalLocalScale;
    private Quaternion originalLocalRotation;
    private float animationStartTime;
    private bool initialized;

    private void Awake()
    {
        EnsureInitialized();
        // ShowAtPosition can be invoked while this GameObject is inactive. In that
        // case it assigns the target before activation, so Awake must not hide it.
        if (startHidden && targetAnchor == null) Hide();
    }

    private void OnEnable()
    {
        EnsureInitialized();
        activeIndicators.Add(this);
    }

    private void OnDisable()
    {
        activeIndicators.Remove(this);
    }

    private void OnDestroy()
    {
        activeIndicators.Remove(this);
    }

    private void Update()
    {
        if (targetAnchor == null)
        {
            Hide();
            return;
        }

        float elapsed = Time.unscaledTime - animationStartTime;
        Vector3 targetPosition = followTarget ? targetAnchor.position : stationaryTargetPosition;
        Vector3 bobOffset = Vector3.zero;

        if (useBobbing && bobAxis.sqrMagnitude > 0f)
            bobOffset = bobAxis.normalized * (Mathf.Sin(elapsed * bobSpeed) * bobDistance);

        transform.position = targetPosition + positionOffset + bobOffset;

        if (useSpinning && spinAxis.sqrMagnitude > 0f)
            transform.Rotate(spinAxis.normalized, spinDegreesPerSecond * Time.unscaledDeltaTime, Space.World);

        if (useScalePulse)
        {
            float scaleMultiplier = 1f + Mathf.Sin(elapsed * pulseSpeed) * pulseAmount;
            transform.localScale = originalLocalScale * scaleMultiplier;
        }
    }

    /// <summary>Use this method from a TutorialStep OnStepStart UnityEvent.</summary>
    public void ShowAtPosition(Transform target)
    {
        if (target == null)
        {
            Debug.LogWarning("Tutorial3DIndicator cannot show because no target anchor was assigned.", this);
            Hide();
            return;
        }

        EnsureInitialized();
        targetAnchor = target;
        stationaryTargetPosition = target.position;
        animationStartTime = Time.unscaledTime;
        transform.localRotation = originalLocalRotation;
        transform.localScale = originalLocalScale;
        transform.position = target.position + positionOffset;
        gameObject.SetActive(true);
    }

    public void Hide()
    {
        targetAnchor = null;
        if (initialized)
        {
            transform.localScale = originalLocalScale;
            transform.localRotation = originalLocalRotation;
        }

        if (gameObject.activeSelf) gameObject.SetActive(false);
    }

    public static void NotifyAnchorClicked(Transform clickedAnchor)
    {
        if (clickedAnchor == null || activeIndicators.Count == 0) return;

        Tutorial3DIndicator[] snapshot = new Tutorial3DIndicator[activeIndicators.Count];
        activeIndicators.CopyTo(snapshot);

        foreach (Tutorial3DIndicator indicator in snapshot)
        {
            if (indicator == null || indicator.targetAnchor == null) continue;

            Transform target = indicator.targetAnchor;
            if (clickedAnchor == target || clickedAnchor.IsChildOf(target) || target.IsChildOf(clickedAnchor))
                indicator.Hide();
        }
    }

    public static void HideAll()
    {
        if (activeIndicators.Count == 0) return;

        Tutorial3DIndicator[] snapshot = new Tutorial3DIndicator[activeIndicators.Count];
        activeIndicators.CopyTo(snapshot);
        foreach (Tutorial3DIndicator indicator in snapshot)
            if (indicator != null) indicator.Hide();
    }

    private void EnsureInitialized()
    {
        if (initialized) return;

        originalLocalScale = transform.localScale;
        originalLocalRotation = transform.localRotation;
        initialized = true;

        if (!disableColliders) return;

        foreach (Collider collider3D in GetComponentsInChildren<Collider>(true))
            collider3D.enabled = false;
        foreach (Collider2D collider2D in GetComponentsInChildren<Collider2D>(true))
            collider2D.enabled = false;
    }
}
