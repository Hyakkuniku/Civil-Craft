using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Editor-only placement helper for permanent bridge anchors. It scans the
/// Environment/Ground surface horizontally and locates the nearest large height
/// discontinuity, allowing an anchor to snap to the actual collider edge rather
/// than an arbitrary mesh pivot.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(Point))]
public sealed class AnchorEdgeSnap : MonoBehaviour
{
    [Header("Automatic Placement")]
    [Tooltip("Enable this only while deliberately positioning a new permanent anchor. Existing tutorial anchors should leave this off so baked ghosts remain aligned.")]
    [SerializeField] private bool autoSnapWhenMoved = false;
    [Tooltip("When this prefab is manually dropped into a loaded scene, convert that instance into a permanent anchor. Runtime-instantiated player nodes are never changed.")]
    [SerializeField] private bool configureDroppedInstanceAsAnchor = true;
    [Tooltip("Only Environment and Ground should be selected.")]
    [SerializeField] private LayerMask surfaceLayers = (1 << 10) | (1 << 11);

    [Header("Edge Detection")]
    [Min(0.25f)] [SerializeField] private float horizontalSearchRadius = 3f;
    [Range(0.02f, 0.5f)] [SerializeField] private float sampleSpacing = 0.1f;
    [Min(0.1f)] [SerializeField] private float minimumEdgeDrop = 0.75f;
    [Range(4, 16)] [SerializeField] private int edgeRefinementSteps = 10;
    [Min(0.1f)] [SerializeField] private float rayStartHeight = 10f;
    [Min(1f)] [SerializeField] private float rayDistance = 50f;

    [Header("Final Anchor Position")]
    [Tooltip("Small positive value places the anchor slightly onto the land side of the edge.")]
    [Min(0f)] [SerializeField] private float edgeInsetOntoLand = 0.02f;
    [Tooltip("The road collider top above its anchor center. Matches Baked Road Visual Top.")]
    [SerializeField] private float roadSurfaceAboveAnchor = 0.025f;
    [Tooltip("Uses another permanent anchor under the same parent to keep the deck perfectly horizontal.")]
    [SerializeField] private bool matchSiblingAnchorHeight = true;

    private Point point;
    private bool isSnapping;

    public bool ControlsEditorPlacement
    {
        get
        {
            CachePoint();
            if (Application.isPlaying || !enabled || !autoSnapWhenMoved || point == null ||
                !gameObject.scene.IsValid())
                return false;

            return (point.isAnchor && !point.Runtime) || configureDroppedInstanceAsAnchor;
        }
    }

    public bool PreservesExistingAnchorPlacement
    {
        get
        {
            CachePoint();
            return !Application.isPlaying && point != null && point.isAnchor && !point.Runtime;
        }
    }

    public void HandleEditorTransformChanged()
    {
        if (!ControlsEditorPlacement || !transform.hasChanged || isSnapping) return;
        TrySnapToNearestEdge(out _);
    }

    [ContextMenu("Snap To Nearest Ravine Edge")]
    public void SnapToNearestEdge()
    {
        if (!TrySnapToNearestEdge(out string report))
            Debug.LogWarning($"[AnchorEdgeSnap] {report}", this);
    }

    public bool TrySnapToNearestEdge(out string report)
    {
        CachePoint();
        if (Application.isPlaying)
        {
            report = "Anchor edge snapping is Editor-only.";
            return false;
        }
        if (point == null)
        {
            report = "This GameObject has no Point component.";
            return false;
        }
        if ((!point.isAnchor || point.Runtime) && configureDroppedInstanceAsAnchor &&
            gameObject.scene.IsValid())
        {
            point.Runtime = false;
            point.isAnchor = true;
            point.originalIsAnchor = true;
            point.UpdateMaterial();
        }
        if (!point.isAnchor || point.Runtime)
        {
            report = "This Point must use Is Anchor = true and Runtime = false.";
            transform.hasChanged = false;
            return false;
        }
        if (isSnapping)
        {
            report = "A snap operation is already running.";
            return false;
        }

        isSnapping = true;
        try
        {
            Vector3 original = transform.position;
            if (!TryFindClosestEdge(original, out EdgeResult edge))
            {
                report = $"No ravine edge was found within {horizontalSearchRadius:F2} units of '{name}'.";
                transform.hasChanged = false;
                return false;
            }

            Vector3 landSamplePosition = edge.edgePoint + edge.landDirection *
                                         Mathf.Max(edgeInsetOntoLand, sampleSpacing * 0.5f);
            if (!TrySampleSurface(landSamplePosition, original.y, null, out SurfaceSample landSurface))
            {
                report = "The edge was found, but its upper land surface could not be measured.";
                transform.hasChanged = false;
                return false;
            }

            Vector3 snapped = original;
            Vector3 snappedHorizontal = edge.edgePoint + edge.landDirection * edgeInsetOntoLand;
            snapped.x = snappedHorizontal.x;
            snapped.z = snappedHorizontal.z;

            Point siblingReference = matchSiblingAnchorHeight ? FindSiblingHeightReference() : null;
            snapped.y = siblingReference != null
                ? siblingReference.transform.position.y
                : landSurface.height - roadSurfaceAboveAnchor;

            transform.position = snapped;
            transform.hasChanged = false;
            report = siblingReference != null
                ? $"Snapped to edge {snappedHorizontal.ToString("F4")} and matched '{siblingReference.name}' at Y {snapped.y:F4}."
                : $"Snapped to edge {snappedHorizontal.ToString("F4")}; road surface matches terrain Y {landSurface.height:F4}.";
            return true;
        }
        finally
        {
            isSnapping = false;
        }
    }

    /// <summary>Measure an edge without changing the anchor or its Point flags.</summary>
    public bool TryPreviewEdge(out Vector3 position, out Collider edgeSurface, out string report)
    {
        return TryPreviewEdge(edgeInsetOntoLand, out position, out edgeSurface, out report);
    }

    /// <summary>
    /// Measure an edge using an explicit land inset. Layout tools use this to
    /// place both endpoints far enough onto stable bank geometry for vehicle
    /// wheels, without changing the component until the layout is applied.
    /// </summary>
    public bool TryPreviewEdge(
        float landInset,
        out Vector3 position,
        out Collider edgeSurface,
        out string report)
    {
        return TryPreviewEdge(landInset, null, Vector3.right, out position, out edgeSurface, out report);
    }

    /// <summary>
    /// Measure an edge along a specific horizontal route and only accept surfaces
    /// belonging to the assigned bank. This prevents nearby Environment colliders
    /// from changing the result of the build-location layout helper.
    /// </summary>
    public bool TryPreviewEdge(
        float landInset,
        Transform expectedSurfaceRoot,
        Vector3 searchDirection,
        out Vector3 position,
        out Collider edgeSurface,
        out string report)
    {
        edgeSurface = null;
        position = transform.position;
        if (Application.isPlaying || !gameObject.scene.IsValid())
        {
            report = "Edge layout is only available for scene objects outside Play Mode.";
            return false;
        }
        if (!TryFindClosestEdge(position, searchDirection, expectedSurfaceRoot, out EdgeResult edge))
        {
            report = $"No edge near {name}. Move the anchor nearer the cliff or increase its Horizontal Search Radius.";
            return false;
        }
        float safeLandInset = Mathf.Max(0f, landInset);
        Vector3 samplePosition = edge.edgePoint + edge.landDirection *
                                 Mathf.Max(safeLandInset, sampleSpacing * 0.5f);
        if (!TrySampleSurface(samplePosition, position.y, expectedSurfaceRoot, out SurfaceSample surface))
        {
            report = $"Could not measure the land surface for {name}.";
            return false;
        }
        Vector3 edgePosition = edge.edgePoint + edge.landDirection * safeLandInset;
        position.x = edgePosition.x;
        position.z = edgePosition.z;
        position.y = surface.height - roadSurfaceAboveAnchor;
        edgeSurface = surface.collider;
        report = $"Measured '{surface.collider.name}' with {safeLandInset:F3} units of anchor overlap onto the bank.";
        return true;
    }

    private bool TryFindClosestEdge(Vector3 origin, out EdgeResult closestEdge)
    {
        return TryFindClosestEdge(origin, Vector3.right, null, out closestEdge);
    }

    private bool TryFindClosestEdge(
        Vector3 origin,
        Vector3 searchDirection,
        Transform expectedSurfaceRoot,
        out EdgeResult closestEdge)
    {
        closestEdge = default;
        searchDirection.y = 0f;
        if (searchDirection.sqrMagnitude < 0.000001f) return false;
        searchDirection.Normalize();

        bool found = false;
        float bestDistance = float.MaxValue;
        int sampleCount = Mathf.CeilToInt(horizontalSearchRadius * 2f / sampleSpacing);
        float startDistance = -horizontalSearchRadius;

        bool previousValid = TrySampleSurface(
            origin + searchDirection * startDistance,
            origin.y,
            expectedSurfaceRoot,
            out SurfaceSample previous);
        previous.distanceAlongSearch = startDistance;
        for (int i = 1; i <= sampleCount; i++)
        {
            float currentDistance = Mathf.Min(horizontalSearchRadius, startDistance + i * sampleSpacing);
            bool currentValid = TrySampleSurface(
                origin + searchDirection * currentDistance,
                origin.y,
                expectedSurfaceRoot,
                out SurfaceSample current);
            current.distanceAlongSearch = currentDistance;

            bool hasDrop = previousValid != currentValid ||
                           (previousValid && currentValid &&
                            Mathf.Abs(previous.height - current.height) >= minimumEdgeDrop);
            if (hasDrop)
            {
                EdgeResult refined = RefineEdge(
                    previous,
                    current,
                    origin.y,
                    expectedSurfaceRoot,
                    searchDirection);
                float distance = Mathf.Abs(refined.distanceAlongSearch);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    closestEdge = refined;
                    found = true;
                }
            }

            previous = current;
            previousValid = currentValid;
        }

        return found;
    }

    private EdgeResult RefineEdge(
        SurfaceSample left,
        SurfaceSample right,
        float referenceY,
        Transform expectedSurfaceRoot,
        Vector3 searchDirection)
    {
        SurfaceSample upper = ChooseUpperSurface(left, right);
        bool upperIsLeft = ApproximatelySameSurface(upper, left);
        float splitHeight = left.valid && right.valid
            ? (left.height + right.height) * 0.5f
            : upper.height - minimumEdgeDrop * 0.5f;

        for (int i = 0; i < edgeRefinementSteps; i++)
        {
            float middleDistance = (left.distanceAlongSearch + right.distanceAlongSearch) * 0.5f;
            Vector3 middlePosition = Vector3.Lerp(left.horizontalPosition, right.horizontalPosition, 0.5f);
            TrySampleSurface(middlePosition, referenceY, expectedSurfaceRoot, out SurfaceSample middle);
            middle.distanceAlongSearch = middleDistance;
            bool middleIsUpper = middle.valid && middle.height >= splitHeight;

            if (middleIsUpper == upperIsLeft) left = middle;
            else right = middle;
        }

        return new EdgeResult
        {
            edgePoint = Vector3.Lerp(left.horizontalPosition, right.horizontalPosition, 0.5f),
            landDirection = searchDirection * (upperIsLeft ? -1f : 1f),
            distanceAlongSearch = (left.distanceAlongSearch + right.distanceAlongSearch) * 0.5f
        };
    }

    private bool TrySampleSurface(
        Vector3 horizontalPosition,
        float referenceY,
        Transform expectedSurfaceRoot,
        out SurfaceSample sample)
    {
        Vector3 origin = new Vector3(
            horizontalPosition.x,
            referenceY + rayStartHeight,
            horizontalPosition.z);
        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            Vector3.down,
            rayStartHeight + rayDistance,
            surfaceLayers,
            QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null) continue;
            if (hit.collider.transform == transform || hit.collider.transform.IsChildOf(transform)) continue;
            if (hit.collider.GetComponentInParent<Bar>() != null) continue;
            if (expectedSurfaceRoot != null &&
                hit.collider.transform != expectedSurfaceRoot &&
                !hit.collider.transform.IsChildOf(expectedSurfaceRoot)) continue;

            sample = new SurfaceSample
            {
                valid = true,
                horizontalPosition = horizontalPosition,
                height = hit.point.y,
                collider = hit.collider
            };
            return true;
        }

        sample = new SurfaceSample
        {
            valid = false,
            horizontalPosition = horizontalPosition,
            height = float.NegativeInfinity
        };
        return false;
    }

    private Point FindSiblingHeightReference()
    {
        Transform parent = transform.parent;
        if (parent == null) return null;

        Point best = null;
        float closestDistance = float.MaxValue;
        foreach (Point candidate in parent.GetComponentsInChildren<Point>(true))
        {
            if (candidate == null || candidate == point || !candidate.isAnchor || candidate.Runtime) continue;
            float distance = Mathf.Abs(candidate.transform.position.x - transform.position.x);
            if (distance >= closestDistance) continue;
            closestDistance = distance;
            best = candidate;
        }
        return best;
    }

    private void CachePoint()
    {
        if (point == null) point = GetComponent<Point>();
    }

    private static SurfaceSample ChooseUpperSurface(SurfaceSample a, SurfaceSample b)
    {
        if (!a.valid) return b;
        if (!b.valid) return a;
        return a.height >= b.height ? a : b;
    }

    private static bool ApproximatelySameSurface(SurfaceSample a, SurfaceSample b)
    {
        return Mathf.Approximately(a.distanceAlongSearch, b.distanceAlongSearch) && a.valid == b.valid;
    }

    private struct SurfaceSample
    {
        public Collider collider;
        public bool valid;
        public Vector3 horizontalPosition;
        public float distanceAlongSearch;
        public float height;
    }

    private struct EdgeResult
    {
        public Vector3 edgePoint;
        public Vector3 landDirection;
        public float distanceAlongSearch;
    }

    private void OnDrawGizmosSelected()
    {
        CachePoint();
        if (point == null || (!configureDroppedInstanceAsAnchor && (!point.isAnchor || point.Runtime))) return;

        Gizmos.color = Color.yellow;
        Vector3 position = transform.position;
        Gizmos.DrawLine(
            position + Vector3.left * horizontalSearchRadius,
            position + Vector3.right * horizontalSearchRadius);
    }
}
