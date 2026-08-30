using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Editor authoring helper for Poly Bridge-style endpoints. Anchors stay on one
/// canonical deck line while independently movable approach surfaces are raised
/// or lowered until their collider surface meets the finalized road surface.
/// This component performs no work at runtime.
/// </summary>
[DisallowMultipleComponent]
public sealed class BridgeAbutmentAligner : MonoBehaviour
{
    [Serializable]
    public sealed class Abutment
    {
        [Tooltip("The permanent red bridge anchor at this endpoint.")]
        public Point anchor;

        [Tooltip("Collider belonging to the small approach/abutment surface, not the entire cliff.")]
        public Collider approachCollider;

        [Tooltip("Object moved vertically during alignment. Defaults to the collider Transform.")]
        public Transform movableRoot;

        [Tooltip("World-space offset from the anchor used to probe the approach surface. Move this slightly toward the land.")]
        public Vector3 surfaceProbeOffset;

        [Tooltip("Optional per-side correction after matching the common road height.")]
        public float additionalSurfaceOffset;
    }

    [Header("Deck Reference")]
    [Tooltip("The anchor whose world Y defines the straight bridge deck line.")]
    [SerializeField] private Point referenceAnchor;

    [Tooltip("The finalized road collider top relative to its node center. Keep this equal to BridgePhysicsManager's Baked Road Visual Top.")]
    [SerializeField] private float roadSurfaceAboveAnchor = 0.025f;

    [Header("Endpoints")]
    [SerializeField] private List<Abutment> abutments = new List<Abutment>();

    [Header("Automatic Smooth Approaches")]
    [Tooltip("Optional. Used to discover the Starting and Ending Anchors automatically.")]
    [SerializeField] private BuildLocation buildLocation;
    [Tooltip("Use only Environment and Ground. Props and bridge pieces must be excluded.")]
    [SerializeField] private LayerMask environmentLayers = (1 << 10) | (1 << 11);
    [Tooltip("How far behind the anchor to measure the stable top of the terrain.")]
    [Min(0.1f)] [SerializeField] private float landProbeDistance = 0.4f;
    [Tooltip("How far the smooth collider reaches onto the bridge so wheels rise before reaching the terrain edge.")]
    [Min(0f)] [SerializeField] private float overlapIntoBridge = 0.6f;
    [Tooltip("How far onto the land the smooth collider ends. It must reach beyond the uneven cliff lip.")]
    [Min(0f)] [SerializeField] private float landEndInset = 0.1f;
    [Tooltip("Safety minimum used even by older scenes that saved shorter approach values.")]
    [Min(0.1f)] [SerializeField] private float minimumStableLandCoverage = 0.65f;
    [Tooltip("Safety minimum used even by older scenes so the wheel is already supported before it reaches the anchor.")]
    [Min(0.1f)] [SerializeField] private float minimumBridgeCoverage = 0.9f;
    [Min(0.1f)] [SerializeField] private float automaticApproachWidth = 2.4f;
    [Min(0.01f)] [SerializeField] private float automaticApproachThickness = 0.1f;
    [Tooltip("Tiny separation from the terrain. Large values create a new physical step, so runtime generation clamps this value.")]
    [Min(0f)] [SerializeField] private float surfaceClearance = 0.002f;
    [SerializeField] private PhysicMaterial automaticApproachMaterial;
    [Tooltip("Rebuilds approaches from the actual loaded colliders. This also upgrades short approaches already saved in older scenes.")]
    [SerializeField] private bool refreshApproachesOnSceneStart = true;

    [Header("Tutorial Blueprint Compatibility")]
    [Tooltip("Keeps permanent anchors on the exact endpoints stored by nearby baked GhostSegments. This prevents authoring tools from desynchronizing tutorial blueprints.")]
    [SerializeField] private bool alignAnchorsToTutorialGhosts = true;
    [Tooltip("Maximum distance an anchor may be corrected to a baked ghost endpoint.")]
    [Min(0.01f)] [SerializeField] private float maximumGhostEndpointCorrection = 2f;

    [Header("Validation")]
    [Min(0.0001f)] [SerializeField] private float allowedSurfaceError = 0.002f;
    [Min(0.1f)] [SerializeField] private float probePadding = 2f;

    public Point ReferenceAnchor => referenceAnchor;
    public IReadOnlyList<Abutment> Abutments => abutments;
    public BuildLocation Location => buildLocation != null ? buildLocation : GetComponent<BuildLocation>();
    public float TargetAnchorY => referenceAnchor != null
        ? referenceAnchor.transform.position.y
        : transform.position.y;
    public float TargetRoadSurfaceY => TargetAnchorY + roadSurfaceAboveAnchor;

    private const string GeneratedRootName = "GeneratedSmoothApproaches";

    private void Awake()
    {
        if (!Application.isPlaying) return;

        if (alignAnchorsToTutorialGhosts)
            AlignAnchorsToTutorialGhostEndpoints(out _);

        if (!refreshApproachesOnSceneStart) return;

        if (!GenerateSmoothApproachesInternal(out string report))
            Debug.LogWarning($"[BridgeAbutmentAligner] Runtime approach refresh failed: {report}", this);
    }

    private void Reset()
    {
        buildLocation = GetComponent<BuildLocation>();
        if (buildLocation == null) return;

        if (buildLocation.startingAnchors.Count > 0)
            referenceAnchor = buildLocation.startingAnchors[0];
        else if (buildLocation.endingAnchors.Count > 0)
            referenceAnchor = buildLocation.endingAnchors[0];
    }

    public bool AlignAll(out string report)
    {
        if (Application.isPlaying)
        {
            report = "Abutment alignment is Editor-only and cannot run in Play Mode.";
            return false;
        }

        if (referenceAnchor == null)
        {
            report = "Assign a Reference Anchor first.";
            return false;
        }

        if (abutments == null || abutments.Count == 0)
        {
            report = "Add at least one endpoint to the Abutments list.";
            return false;
        }

        float targetAnchorY = referenceAnchor.transform.position.y;
        int alignedAnchors = 0;
        int alignedSurfaces = 0;
        List<string> warnings = new List<string>();

        foreach (Abutment abutment in abutments)
        {
            if (abutment == null || abutment.anchor == null)
            {
                warnings.Add("An endpoint has no Anchor assigned.");
                continue;
            }

            Transform anchorTransform = abutment.anchor.transform;
            Vector3 anchorPosition = anchorTransform.position;
            if (!Mathf.Approximately(anchorPosition.y, targetAnchorY))
            {
                anchorPosition.y = targetAnchorY;
                anchorTransform.position = anchorPosition;
            }
            alignedAnchors++;
        }

        Physics.SyncTransforms();

        foreach (Abutment abutment in abutments)
        {
            if (abutment == null || abutment.anchor == null || abutment.approachCollider == null)
            {
                if (abutment != null && abutment.anchor != null)
                    warnings.Add($"'{abutment.anchor.name}' has no Approach Collider assigned.");
                continue;
            }

            Transform movable = abutment.movableRoot != null
                ? abutment.movableRoot
                : abutment.approachCollider.transform;

            if (!IsColliderPartOfMovableRoot(abutment.approachCollider, movable))
            {
                warnings.Add(
                    $"'{abutment.anchor.name}' was skipped because its Approach Collider is not a child of Movable Root '{movable.name}'.");
                continue;
            }

            if (!TryMeasureSurfaceY(abutment, out float currentSurfaceY))
            {
                warnings.Add(
                    $"Could not hit the approach surface for '{abutment.anchor.name}'. Adjust Surface Probe Offset toward the land.");
                continue;
            }

            float desiredSurfaceY = targetAnchorY + roadSurfaceAboveAnchor +
                                    abutment.additionalSurfaceOffset;
            Vector3 movablePosition = movable.position;
            movablePosition.y += desiredSurfaceY - currentSurfaceY;
            movable.position = movablePosition;
            alignedSurfaces++;
        }

        Physics.SyncTransforms();

        report = $"Aligned {alignedAnchors} anchor(s) and {alignedSurfaces} approach surface(s) " +
                 $"to deck surface Y {TargetRoadSurfaceY:F4}.";
        if (warnings.Count > 0)
            report += "\n" + string.Join("\n", warnings);

        return alignedSurfaces > 0;
    }

    public bool ValidateAlignment(out string report)
    {
        if (referenceAnchor == null)
        {
            report = "Assign a Reference Anchor first.";
            return false;
        }

        bool valid = true;
        List<string> results = new List<string>();
        float targetAnchorY = referenceAnchor.transform.position.y;

        foreach (Abutment abutment in abutments)
        {
            if (abutment == null || abutment.anchor == null)
            {
                valid = false;
                results.Add("Missing anchor reference.");
                continue;
            }

            float anchorError = Mathf.Abs(abutment.anchor.transform.position.y - targetAnchorY);
            if (anchorError > allowedSurfaceError)
            {
                valid = false;
                results.Add($"{abutment.anchor.name}: anchor is off the deck line by {anchorError:F4} units.");
            }

            if (abutment.approachCollider == null || !TryMeasureSurfaceY(abutment, out float surfaceY))
            {
                valid = false;
                results.Add($"{abutment.anchor.name}: approach surface could not be measured.");
                continue;
            }

            float desiredSurfaceY = targetAnchorY + roadSurfaceAboveAnchor +
                                    abutment.additionalSurfaceOffset;
            float surfaceError = Mathf.Abs(surfaceY - desiredSurfaceY);
            if (surfaceError > allowedSurfaceError)
            {
                valid = false;
                results.Add($"{abutment.anchor.name}: approach surface is off by {surfaceError:F4} units.");
            }
        }

        if (valid)
        {
            report = $"All anchors and approach surfaces are aligned. Road surface Y: {TargetRoadSurfaceY:F4}.";
            return true;
        }

        report = results.Count > 0
            ? string.Join("\n", results)
            : "No endpoints were configured.";
        return false;
    }

    public bool GenerateSmoothApproaches(out string report)
    {
        if (Application.isPlaying)
        {
            report = "Smooth approaches can only be generated outside Play Mode.";
            return false;
        }

        return GenerateSmoothApproachesInternal(out report);
    }

    public bool AlignAnchorsToTutorialGhostEndpoints(out string report)
    {
        BuildLocation location = Location;
        if (location == null)
        {
            report = "No Build Location is assigned.";
            return false;
        }

        List<Point> anchors = CollectLocationAnchors(location);
        GhostSegment[] ghosts = FindObjectsOfType<GhostSegment>(true);
        if (anchors.Count == 0 || ghosts.Length == 0)
        {
            report = "No anchors or scene GhostSegments were found.";
            return false;
        }

        List<Vector3> endpoints = new List<Vector3>();
        foreach (GhostSegment ghost in ghosts)
        {
            if (ghost == null || !ghost.gameObject.scene.IsValid()) continue;
            AddUniqueEndpoint(endpoints, ghost.startPos);
            AddUniqueEndpoint(endpoints, ghost.endPos);
        }

        HashSet<int> usedEndpoints = new HashSet<int>();
        int corrected = 0;
        foreach (Point anchor in anchors)
        {
            if (anchor == null) continue;

            int nearestIndex = -1;
            float nearestDistance = maximumGhostEndpointCorrection;
            for (int i = 0; i < endpoints.Count; i++)
            {
                if (usedEndpoints.Contains(i)) continue;
                float distance = Vector3.Distance(anchor.transform.position, endpoints[i]);
                if (distance >= nearestDistance) continue;
                nearestDistance = distance;
                nearestIndex = i;
            }

            if (nearestIndex < 0) continue;
            usedEndpoints.Add(nearestIndex);
            Vector3 target = endpoints[nearestIndex];
            if ((anchor.transform.position - target).sqrMagnitude > 0.000001f)
            {
                anchor.transform.position = target;
                anchor.transform.hasChanged = false;
                corrected++;
            }
        }

        Physics.SyncTransforms();
        report = $"Aligned {corrected} anchor(s) to baked tutorial ghost endpoints.";
        return corrected > 0;
    }

    private bool GenerateSmoothApproachesInternal(out string report)
    {

        if (buildLocation == null)
            buildLocation = GetComponent<BuildLocation>();
        if (buildLocation == null)
        {
            report = "Assign a Build Location or attach this component to the Build Location GameObject.";
            return false;
        }

        List<Point> anchors = CollectLocationAnchors(buildLocation);
        if (anchors.Count < 2)
        {
            report = "The Build Location needs at least two assigned Starting/Ending Anchors.";
            return false;
        }

        if (referenceAnchor == null)
            referenceAnchor = anchors[0];

        float commonAnchorY = referenceAnchor.transform.position.y;
        foreach (Point anchor in anchors)
        {
            Vector3 position = anchor.transform.position;
            position.y = commonAnchorY;
            anchor.transform.position = position;
        }

        RemoveGeneratedApproaches();
        Physics.SyncTransforms();

        GameObject rootObject = new GameObject(GeneratedRootName);
        rootObject.transform.SetParent(transform, false);

        Dictionary<Point, Point> pairedAnchors = BuildAnchorPairMap(buildLocation);
        Vector3 bridgeCenter = Vector3.zero;
        foreach (Point anchor in anchors) bridgeCenter += anchor.transform.position;
        bridgeCenter /= anchors.Count;

        int generatedCount = 0;
        List<string> warnings = new List<string>();
        foreach (Point anchor in anchors)
        {
            // A BuildLocation may contain more than one separate bridge. Using
            // one average center for every anchor makes the two endpoints beside
            // the middle island point in the wrong direction. Prefer the anchor
            // paired with this endpoint in Starting/Ending Anchors.
            Vector3 spanCenter = pairedAnchors.TryGetValue(anchor, out Point oppositeAnchor) &&
                                 oppositeAnchor != null
                ? (anchor.transform.position + oppositeAnchor.transform.position) * 0.5f
                : bridgeCenter;
            Vector3 landDirection = Vector3.ProjectOnPlane(
                anchor.transform.position - spanCenter,
                Vector3.up);
            if (landDirection.sqrMagnitude < 0.0001f)
            {
                warnings.Add($"Could not determine the land direction for '{anchor.name}'.");
                continue;
            }
            landDirection.Normalize();

            // Bridge 1 in CanyonCrossing exposed why the probe and the end of the
            // cover cannot be separate: the old collider sampled stable land at
            // 0.4 m but stopped at 0.1 m, putting the wheel back onto the jagged
            // cliff lip. Always carry the cover all the way to stable land.
            float bridgeCoverage = Mathf.Max(overlapIntoBridge, minimumBridgeCoverage);
            float stableLandCoverage = Mathf.Max(
                landEndInset,
                landProbeDistance,
                minimumStableLandCoverage);

            Vector3 landProbe = anchor.transform.position + landDirection * stableLandCoverage;
            if (!TryFindEnvironmentSurface(landProbe, rootObject.transform, out RaycastHit landHit))
            {
                warnings.Add(
                    $"No Environment/Ground collider was found {stableLandCoverage:F2} units behind '{anchor.name}'.");
                continue;
            }

            Vector3 roadTop = anchor.transform.position + Vector3.up * roadSurfaceAboveAnchor -
                              landDirection * bridgeCoverage;
            Vector3 landTop = anchor.transform.position + landDirection * stableLandCoverage;
            // More than a few millimetres of clearance becomes another bump.
            float safeClearance = Mathf.Min(surfaceClearance, 0.003f);
            landTop.y = landHit.point.y + safeClearance;
            CreateApproachCollider(rootObject.transform, anchor, roadTop, landTop, generatedCount++);
        }

        Physics.SyncTransforms();
        report = $"Generated {generatedCount} smooth approach collider(s).";
        if (warnings.Count > 0)
            report += "\n" + string.Join("\n", warnings);
        return generatedCount > 0;
    }

    public void RemoveGeneratedApproaches()
    {
        Transform existing = transform.Find(GeneratedRootName);
        if (existing == null) return;

        if (Application.isPlaying)
        {
            // Disable first so the old short collider cannot overlap the new one
            // during the frame in which Destroy is deferred.
            existing.gameObject.SetActive(false);
            Destroy(existing.gameObject);
        }
        else
        {
            DestroyImmediate(existing.gameObject);
        }
    }

    private bool TryFindEnvironmentSurface(
        Vector3 probe,
        Transform generatedRoot,
        out RaycastHit surfaceHit)
    {
        Vector3 origin = probe + Vector3.up * probePadding;
        float distance = probePadding * 2f + 20f;
        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            Vector3.down,
            distance,
            environmentLayers,
            QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null) continue;
            if (generatedRoot != null && hit.collider.transform.IsChildOf(generatedRoot)) continue;
            if (hit.collider.GetComponentInParent<Bar>() != null) continue;

            surfaceHit = hit;
            return true;
        }

        surfaceHit = default;
        return false;
    }

    private void CreateApproachCollider(
        Transform root,
        Point anchor,
        Vector3 roadTop,
        Vector3 landTop,
        int index)
    {
        Vector3 delta = landTop - roadTop;
        float length = Mathf.Max(0.05f, delta.magnitude);
        GameObject approach = new GameObject($"SmoothApproach_{index}_{anchor.name}");
        approach.transform.SetParent(root, true);

        Vector3 travelAxis = delta.normalized;
        Vector3 localUp = Vector3.ProjectOnPlane(Vector3.up, travelAxis).normalized;
        Vector3 localForward = Vector3.Cross(travelAxis, localUp).normalized;
        approach.transform.rotation = Quaternion.LookRotation(localForward, localUp);
        approach.transform.position = (roadTop + landTop) * 0.5f -
                                      approach.transform.up * (automaticApproachThickness * 0.5f);

        int bridgeLayer = LayerMask.NameToLayer("Bridge");
        if (bridgeLayer >= 0) approach.layer = bridgeLayer;

        BoxCollider collider = approach.AddComponent<BoxCollider>();
        collider.size = new Vector3(length, automaticApproachThickness, automaticApproachWidth);
        collider.isTrigger = false;
        collider.material = automaticApproachMaterial;
    }

    private static List<Point> CollectLocationAnchors(BuildLocation location)
    {
        List<Point> anchors = new List<Point>();
        foreach (Point anchor in location.startingAnchors)
            if (anchor != null && !anchors.Contains(anchor)) anchors.Add(anchor);
        foreach (Point anchor in location.endingAnchors)
            if (anchor != null && !anchors.Contains(anchor)) anchors.Add(anchor);
        return anchors;
    }

    private static Dictionary<Point, Point> BuildAnchorPairMap(BuildLocation location)
    {
        Dictionary<Point, Point> pairs = new Dictionary<Point, Point>();
        if (location == null) return pairs;

        int pairCount = Mathf.Min(
            location.startingAnchors.Count,
            location.endingAnchors.Count);
        for (int i = 0; i < pairCount; i++)
        {
            Point start = location.startingAnchors[i];
            Point end = location.endingAnchors[i];
            if (start == null || end == null || start == end) continue;

            pairs[start] = end;
            pairs[end] = start;
        }

        return pairs;
    }

    private static void AddUniqueEndpoint(List<Vector3> endpoints, Vector3 candidate)
    {
        const float duplicateToleranceSquared = 0.0001f;
        foreach (Vector3 endpoint in endpoints)
        {
            if ((endpoint - candidate).sqrMagnitude <= duplicateToleranceSquared)
                return;
        }
        endpoints.Add(candidate);
    }

    private bool TryMeasureSurfaceY(Abutment abutment, out float surfaceY)
    {
        Collider collider = abutment.approachCollider;
        Vector3 probe = abutment.anchor.transform.position + abutment.surfaceProbeOffset;
        float originY = Mathf.Max(collider.bounds.max.y, probe.y) + probePadding;
        Ray ray = new Ray(new Vector3(probe.x, originY, probe.z), Vector3.down);
        float distance = originY - collider.bounds.min.y + probePadding;

        if (collider.Raycast(ray, out RaycastHit hit, distance))
        {
            surfaceY = hit.point.y;
            return true;
        }

        surfaceY = 0f;
        return false;
    }

    private static bool IsColliderPartOfMovableRoot(Collider collider, Transform movableRoot)
    {
        Transform colliderTransform = collider.transform;
        return colliderTransform == movableRoot || colliderTransform.IsChildOf(movableRoot);
    }

    private void OnDrawGizmosSelected()
    {
        if (referenceAnchor == null) return;

        float minX = referenceAnchor.transform.position.x;
        float maxX = minX;
        foreach (Abutment abutment in abutments)
        {
            if (abutment == null || abutment.anchor == null) continue;
            float x = abutment.anchor.transform.position.x;
            minX = Mathf.Min(minX, x);
            maxX = Mathf.Max(maxX, x);

            Gizmos.color = Color.yellow;
            Vector3 probe = abutment.anchor.transform.position + abutment.surfaceProbeOffset;
            Gizmos.DrawLine(probe + Vector3.up * 0.5f, probe - Vector3.up * 0.5f);
            Gizmos.DrawWireSphere(probe, 0.08f);
        }

        Gizmos.color = Color.cyan;
        float surfaceY = TargetRoadSurfaceY;
        Gizmos.DrawLine(
            new Vector3(minX - 1f, surfaceY, referenceAnchor.transform.position.z),
            new Vector3(maxX + 1f, surfaceY, referenceAnchor.transform.position.z));
    }
}
