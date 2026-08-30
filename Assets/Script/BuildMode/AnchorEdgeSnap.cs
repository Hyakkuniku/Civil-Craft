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

            float landSampleX = edge.edgeX + edge.landDirectionX *
                                Mathf.Max(edgeInsetOntoLand, sampleSpacing * 0.5f);
            if (!TrySampleSurface(landSampleX, original.z, original.y, out SurfaceSample landSurface))
            {
                report = "The edge was found, but its upper land surface could not be measured.";
                transform.hasChanged = false;
                return false;
            }

            Vector3 snapped = original;
            snapped.x = edge.edgeX + edge.landDirectionX * edgeInsetOntoLand;

            Point siblingReference = matchSiblingAnchorHeight ? FindSiblingHeightReference() : null;
            snapped.y = siblingReference != null
                ? siblingReference.transform.position.y
                : landSurface.height - roadSurfaceAboveAnchor;

            transform.position = snapped;
            transform.hasChanged = false;
            report = siblingReference != null
                ? $"Snapped to edge X {snapped.x:F4} and matched '{siblingReference.name}' at Y {snapped.y:F4}."
                : $"Snapped to edge X {snapped.x:F4}; road surface matches terrain Y {landSurface.height:F4}.";
            return true;
        }
        finally
        {
            isSnapping = false;
        }
    }

    private bool TryFindClosestEdge(Vector3 origin, out EdgeResult closestEdge)
    {
        closestEdge = default;
        bool found = false;
        float bestDistance = float.MaxValue;
        int sampleCount = Mathf.CeilToInt(horizontalSearchRadius * 2f / sampleSpacing);
        float startX = origin.x - horizontalSearchRadius;

        bool previousValid = TrySampleSurface(startX, origin.z, origin.y, out SurfaceSample previous);
        for (int i = 1; i <= sampleCount; i++)
        {
            float currentX = Mathf.Min(origin.x + horizontalSearchRadius, startX + i * sampleSpacing);
            bool currentValid = TrySampleSurface(currentX, origin.z, origin.y, out SurfaceSample current);

            bool hasDrop = previousValid != currentValid ||
                           (previousValid && currentValid &&
                            Mathf.Abs(previous.height - current.height) >= minimumEdgeDrop);
            if (hasDrop)
            {
                EdgeResult refined = RefineEdge(previous, current, origin.z, origin.y);
                float distance = Mathf.Abs(refined.edgeX - origin.x);
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
        float z,
        float referenceY)
    {
        SurfaceSample upper = ChooseUpperSurface(left, right);
        bool upperIsLeft = ApproximatelySameSurface(upper, left);
        float splitHeight = left.valid && right.valid
            ? (left.height + right.height) * 0.5f
            : upper.height - minimumEdgeDrop * 0.5f;

        for (int i = 0; i < edgeRefinementSteps; i++)
        {
            float middleX = (left.x + right.x) * 0.5f;
            TrySampleSurface(middleX, z, referenceY, out SurfaceSample middle);
            bool middleIsUpper = middle.valid && middle.height >= splitHeight;

            if (middleIsUpper == upperIsLeft) left = middle;
            else right = middle;
        }

        return new EdgeResult
        {
            edgeX = (left.x + right.x) * 0.5f,
            landDirectionX = upperIsLeft ? -1f : 1f
        };
    }

    private bool TrySampleSurface(float x, float z, float referenceY, out SurfaceSample sample)
    {
        Vector3 origin = new Vector3(x, referenceY + rayStartHeight, z);
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

            sample = new SurfaceSample { valid = true, x = x, height = hit.point.y };
            return true;
        }

        sample = new SurfaceSample { valid = false, x = x, height = float.NegativeInfinity };
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
        return Mathf.Approximately(a.x, b.x) && a.valid == b.valid;
    }

    private struct SurfaceSample
    {
        public bool valid;
        public float x;
        public float height;
    }

    private struct EdgeResult
    {
        public float edgeX;
        public float landDirectionX;
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
