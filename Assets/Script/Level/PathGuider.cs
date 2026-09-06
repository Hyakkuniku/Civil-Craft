using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

public class PathGuider : MonoBehaviour
{
    public static PathGuider Instance { get; private set; }

    [Header("Rock Trail Settings")]
    [Tooltip("Add as many different 3D Rock Prefabs here as you want!")]
    public List<GameObject> rockPrefabs = new List<GameObject>();
    
    [Tooltip("How much space (in meters) should be between each rock?")]
    public float rockSpacing = 1.5f;
    [Tooltip("Distance at which the player 'picks up' or clears the rock as they walk over it.")]
    public float rockPickupDistance = 2.0f;
    [Tooltip("Randomize rock rotation so they look natural?")]
    public bool randomizeRotation = true;

    [Header("Visual Effects")]
    [Tooltip("Uncheck this to turn off the sparkle effects completely.")]
    public bool enableSparkles = true; 
    [Tooltip("Drop a Sparkle/Glow Particle System Prefab here to attach to every rock!")]
    public GameObject sparklePrefab;
    [Tooltip("Makes the rocks larger or smaller overall. (1.5 = 50% larger)")]
    public float rockScaleMultiplier = 1.0f;

    // --- NEW: Centralized Wave Animation Settings ---
    [Header("Wave Animation (Flows to Target)")]
    [Tooltip("If TRUE, the rocks will animate in a wave flowing toward the destination.")]
    public bool animateWave = true;
    [Tooltip("How fast the wave travels down the path.")]
    public float waveSpeed = 1.5f;
    [Tooltip("The delay offset between each rock in the line.")]
    public float waveStep = 0.2f;
    [Tooltip("The smallest size the rock shrinks to before growing again.")]
    [Range(0f, 1f)] public float minWaveScale = 0.2f;

    [Header("Dynamic Recalculation")]
    public float offPathTolerance = 4.0f;
    public float offPathCheckInterval = 0.5f;
    [Tooltip("Allows the guide to lead toward the reachable edge of a disconnected area. Cached routes prevent partial paths from flickering at NavMesh seams.")]
    [SerializeField] private bool allowPartialNavMeshPaths = true;
    [Tooltip("First radius used to project the player and target onto navigation.")]
    [Min(0.1f)] [SerializeField] private float navMeshSampleRadius = 2f;
    [Tooltip("Fallback projection radius used when the player is farther away from a baked NavMesh.")]
    [Min(0.1f)] [SerializeField] private float maxNavMeshSampleRadius = 12f;
    [Tooltip("If local projection fails, use the closest vertex from every loaded NavMesh so Navigate works from anywhere in the scene.")]
    [SerializeField] private bool useGlobalNavMeshFallback = true;
    [Tooltip("Minimum delay between route rebuilds. This prevents flashing at overlapping NavMesh seams.")]
    [Min(0.1f)] [SerializeField] private float recalculationCooldown = 1.5f;

    [Header("Path Settings")]
    public List<GuiderWaypoint> waypoints; 
    public Transform player;
    public float stoppingDistance = 2.0f;

    [Header("Terrain Hugging")]
    public float pathResolution = 0.5f;
    public float heightOffset = 0.1f; 
    public LayerMask groundLayer;
    [Tooltip("Layer used by finalized player-built bridge roads. It is added to Ground Layer automatically.")]
    [SerializeField] private string walkableBridgeLayer = "Bridge";
    [Tooltip("Small visible gap kept between a guide marker and every surface.")]
    [Min(0f)] [SerializeField] private float markerSurfaceClearance = 0.03f;
    [Tooltip("Additional lift on bridge roads so markers do not become buried by thick road visuals.")]
    [Min(0f)] [SerializeField] private float bridgeMarkerExtraHeight = 0.15f;
    [Tooltip("Vertical distance searched above and below each NavMesh path point for its matching visible surface.")]
    [Min(1f)] [SerializeField] private float surfaceProbeHeight = 25f;
    [Tooltip("Reject colliders that are vertically far from the NavMesh point. This prevents markers jumping onto another stacked surface.")]
    [Min(0.1f)] [SerializeField] private float maxSurfaceHeightDifference = 4f;

    private NavMeshPath path;
    private int currentWaypointIndex = 0;
    
    // --- THE FIX: We track the object and its Base Scale together now! ---
    private class TrackedRock
    {
        public GameObject obj;
        public Vector3 baseScale;
    }
    private List<TrackedRock> activeRocks = new List<TrackedRock>(); 
    private GameObject rockContainer; 
    
    private Transform currentlyTargetedWaypoint;
    private float offPathTimer = 0f;
    private float nextAllowedRecalculationTime;
    private readonly List<Vector3> currentRoutePoints = new List<Vector3>();
    private readonly RaycastHit[] surfaceHits = new RaycastHit[16];
    private DynamicNavMeshUpdater subscribedNavMeshUpdater;

    private void Awake()
    {
        if (Instance == null) Instance = this;
    }

    private void Start()
    {
        path = new NavMeshPath();
    }

    private void Update()
    {
        EnsureNavMeshUpdateSubscription();

        if (player == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null) player = playerObj.transform;
        }

        if (player == null || waypoints == null || currentWaypointIndex >= waypoints.Count)
        {
            ClearRocks();
            return;
        }

        Transform currentTarget = waypoints[currentWaypointIndex].target;
        if (currentTarget == null) return;

        if (Vector3.Distance(player.position, currentTarget.position) <= stoppingDistance)
        {
            bool shouldAdvanceTutorial = waypoints[currentWaypointIndex].advancesTutorial;

            currentWaypointIndex++;
            currentlyTargetedWaypoint = null; 
            ClearRocks();

            if (shouldAdvanceTutorial && TutorialManager.Instance != null)
            {
                TutorialManager.Instance.ShowNextStep();
            }

            return;
        }

        if (currentlyTargetedWaypoint != currentTarget)
        {
            currentlyTargetedWaypoint = currentTarget;
            GenerateRockPath(currentTarget);
        }
        else
        {
            offPathTimer += Time.deltaTime;
            if (offPathTimer >= offPathCheckInterval)
            {
                offPathTimer = 0f;
                if (Time.unscaledTime >= nextAllowedRecalculationTime &&
                    IsPlayerOffPath(currentTarget))
                {
                    GeneratePathBackToTrail(currentTarget);
                }
            }
        }

        HandleRockPickup();
        
        // --- NEW: Call the perfectly synced animation every frame ---
        AnimateRocks();
    }

    // --- THE FIX: The new, completely shake-free centralized wave logic! ---
    private void AnimateRocks()
    {
        float timeVal = Time.time * waveSpeed;
        
        for (int i = 0; i < activeRocks.Count; i++)
        {
            if (activeRocks[i].obj == null) continue;

            if (animateWave)
            {
                // A sine wave is continuous at the loop boundary. The previous
                // fractional sawtooth jumped from full size to minimum size and
                // looked like the entire guide was flickering.
                float phase = (timeVal - (i * waveStep)) * Mathf.PI * 2f;
                float wave01 = 0.5f + (0.5f * Mathf.Sin(phase));
                float scaleMult = Mathf.Lerp(minWaveScale, 1f, wave01);
                Vector3 targetScale = activeRocks[i].baseScale * scaleMult;
                
                // Smooth Lerp towards the target completely kills any jitter/shake
                activeRocks[i].obj.transform.localScale = Vector3.Lerp(activeRocks[i].obj.transform.localScale, targetScale, Time.deltaTime * 15f);
            }
            else
            {
                // Standard smooth pop-in if the wave is turned off
                activeRocks[i].obj.transform.localScale = Vector3.Lerp(activeRocks[i].obj.transform.localScale, activeRocks[i].baseScale, Time.deltaTime * 10f);
            }
        }
    }

    private bool IsPlayerOffPath(Transform target)
    {
        if (currentRoutePoints.Count == 0)
        {
            return Vector3.Distance(player.position, target.position) > (stoppingDistance + rockPickupDistance);
        }

        // Test against the cached route rather than spawned rocks. Rocks are
        // intentionally removed as the player reaches them, so using them made
        // the full trail get destroyed and recreated every few frames.
        Vector2 playerXZ = new Vector2(player.position.x, player.position.z);
        float closestSqrDistance = float.MaxValue;

        if (currentRoutePoints.Count == 1)
        {
            Vector3 onlyPoint = currentRoutePoints[0];
            closestSqrDistance = (playerXZ - new Vector2(onlyPoint.x, onlyPoint.z)).sqrMagnitude;
        }
        else
        {
            for (int i = 0; i < currentRoutePoints.Count - 1; i++)
            {
                Vector3 start = currentRoutePoints[i];
                Vector3 end = currentRoutePoints[i + 1];
                float sqrDistance = SqrDistanceToSegmentXZ(
                    playerXZ,
                    new Vector2(start.x, start.z),
                    new Vector2(end.x, end.z));
                if (sqrDistance < closestSqrDistance)
                    closestSqrDistance = sqrDistance;
            }
        }

        return closestSqrDistance > offPathTolerance * offPathTolerance;
    }

    private static float SqrDistanceToSegmentXZ(Vector2 point, Vector2 start, Vector2 end)
    {
        Vector2 segment = end - start;
        float segmentLengthSqr = segment.sqrMagnitude;
        if (segmentLengthSqr <= Mathf.Epsilon) return (point - start).sqrMagnitude;

        float t = Mathf.Clamp01(Vector2.Dot(point - start, segment) / segmentLengthSqr);
        return (point - (start + segment * t)).sqrMagnitude;
    }

    private void GenerateRockPath(Transform target)
    {
        // Calculating against the old world data while a newly completed bridge
        // is still baking produces a cached partial route that ends at the ravine.
        // The completion callback below retries automatically on fresh data.
        if (DynamicNavMeshUpdater.Instance != null &&
            DynamicNavMeshUpdater.Instance.HasPendingOrRunningUpdate)
        {
            return;
        }

        nextAllowedRecalculationTime = Time.unscaledTime + recalculationCooldown;

        if (!TrySampleNavMeshPosition(player.position, out Vector3 safeStart) ||
            !TrySampleNavMeshPosition(target.position, out Vector3 safeTarget))
            return;

        List<Vector3> points = null;
        if (NavMesh.CalculatePath(safeStart, safeTarget, NavMesh.AllAreas, path))
        {
            if (path.status == NavMeshPathStatus.PathComplete ||
                (allowPartialNavMeshPaths && path.status == NavMeshPathStatus.PathPartial))
            {
                points = GenerateSmoothTerrainPath(path.corners);
                if (GetRouteLength(points) >= Mathf.Max(rockSpacing, pathResolution))
                    PrependPlayerConnector(points, safeStart);
            }
        }

        // A disconnected NavMesh can return a partial path containing only its
        // start point. That creates one marker under the player, which is picked
        // up immediately and looks like Navigate did nothing. On the first route
        // request, provide a terrain-projected direction trail until a usable
        // NavMesh route (or generated bridge link) becomes available.
        if (points == null || GetRouteLength(points) < Mathf.Max(rockSpacing, pathResolution))
        {
            if (currentRoutePoints.Count > 0) return;
            points = GenerateSmoothTerrainPath(new[] { player.position, target.position });
        }

        if (points.Count == 0) return;

        // Keep a valid existing guide visible until its replacement is ready.
        // Clearing before CalculatePath caused visible flashing whenever
        // sampling briefly failed at a surface boundary.
        ClearRocks();
        currentRoutePoints.AddRange(points);
        SpawnRocksAlongPath(points, false);
    }

    private static float GetRouteLength(List<Vector3> points)
    {
        if (points == null || points.Count < 2) return 0f;

        float length = 0f;
        for (int i = 1; i < points.Count; i++)
            length += Vector3.Distance(points[i - 1], points[i]);
        return length;
    }

    private bool TrySampleNavMeshPosition(Vector3 position, out Vector3 sampledPosition)
    {
        float radius = Mathf.Max(0.1f, navMeshSampleRadius);
        float maximumRadius = Mathf.Max(radius, maxNavMeshSampleRadius);

        while (radius <= maximumRadius + 0.01f)
        {
            if (NavMesh.SamplePosition(position, out NavMeshHit hit, radius, NavMesh.AllAreas))
            {
                sampledPosition = hit.position;
                return true;
            }

            radius = Mathf.Min(radius * 2f, maximumRadius);
            if (Mathf.Approximately(radius, maximumRadius))
            {
                if (NavMesh.SamplePosition(position, out NavMeshHit finalHit, maximumRadius, NavMesh.AllAreas))
                {
                    sampledPosition = finalHit.position;
                    return true;
                }
                break;
            }
        }

        sampledPosition = position;
        if (!useGlobalNavMeshFallback) return false;

        NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
        if (triangulation.vertices == null || triangulation.vertices.Length == 0)
            return false;

        float closestSqrDistance = float.MaxValue;
        for (int i = 0; i < triangulation.vertices.Length; i++)
        {
            float sqrDistance = (triangulation.vertices[i] - position).sqrMagnitude;
            if (sqrDistance >= closestSqrDistance) continue;

            closestSqrDistance = sqrDistance;
            sampledPosition = triangulation.vertices[i];
        }

        return closestSqrDistance < float.MaxValue;
    }

    private void PrependPlayerConnector(List<Vector3> points, Vector3 safeStart)
    {
        float connectorDistance = Vector3.Distance(player.position, safeStart);
        if (connectorDistance <= Mathf.Max(0.25f, pathResolution)) return;

        int segments = Mathf.Max(1, Mathf.CeilToInt(connectorDistance / Mathf.Max(0.25f, pathResolution)));
        List<Vector3> connector = new List<Vector3>(segments);
        for (int i = 0; i < segments; i++)
        {
            float t = (float)i / segments;
            connector.Add(Vector3.Lerp(player.position, safeStart, t));
        }

        points.InsertRange(0, connector);
    }

    private void GeneratePathBackToTrail(Transform targetDestination)
    {
        // Replacing the route is deterministic across world/bridge NavMesh seams.
        // The previous splice logic could preserve an old suffix and repeatedly
        // prepend new branches, creating the scattered/circling guide pattern.
        GenerateRockPath(targetDestination);
    }

    private List<Vector3> GenerateSmoothTerrainPath(Vector3[] corners)
    {
        List<Vector3> points = new List<Vector3>();

        for (int i = 0; i < corners.Length - 1; i++)
        {
            Vector3 start = corners[i];
            Vector3 end = corners[i + 1];
            float distance = Vector3.Distance(start, end);
            
            int segments = Mathf.Max(1, Mathf.CeilToInt(distance / pathResolution));

            for (int j = 0; j < segments; j++)
            {
                float t = (float)j / segments;
                points.Add(start + (end - start) * t); 
            }
        }
        
        if (corners.Length > 0)
        {
            points.Add(corners[corners.Length - 1]);
        }
        return points;
    }

    private void SpawnRocksAlongPath(List<Vector3> pathPoints, bool insertAtFront)
    {
        if (rockPrefabs == null || rockPrefabs.Count == 0 || pathPoints.Count == 0) return;

        if (rockContainer == null) 
        {
            rockContainer = new GameObject("RockTrail_Container");
        }

        float distanceSinceLastRock = rockSpacing; 
        Vector3 lastPos = pathPoints[0];

        List<TrackedRock> newlySpawnedRocks = new List<TrackedRock>();

        foreach (Vector3 point in pathPoints)
        {
            float dist = Vector3.Distance(lastPos, point);
            distanceSinceLastRock += dist;

            if (distanceSinceLastRock >= rockSpacing)
            {
                bool foundSurface = TryFindMatchingSurface(point, out RaycastHit hit);
                Vector3 spawnPos = foundSurface
                    ? hit.point + hit.normal * heightOffset
                    : point + Vector3.up * (heightOffset + markerSurfaceClearance);

                bool isTooCloseToExisting = false;
                foreach (TrackedRock existingRock in activeRocks)
                {
                    if (existingRock.obj == null) continue;
                    
                    if (Vector3.Distance(spawnPos, existingRock.obj.transform.position) < (rockSpacing * 0.8f))
                    {
                        isTooCloseToExisting = true;
                        break;
                    }
                }

                if (isTooCloseToExisting)
                {
                    distanceSinceLastRock = 0f;
                    lastPos = point;
                    continue;
                }

                Quaternion slopeRotation = foundSurface
                    ? Quaternion.FromToRotation(Vector3.up, hit.normal)
                    : Quaternion.identity;

                if (randomizeRotation)
                {
                    slopeRotation *= Quaternion.Euler(0, Random.Range(0f, 360f), 0);
                }

                GameObject randomPrefab = rockPrefabs[Random.Range(0, rockPrefabs.Count)];
                GameObject newRock = Instantiate(randomPrefab, spawnPos, slopeRotation);

                Vector3 desiredScale = newRock.transform.localScale * rockScaleMultiplier;
                newRock.transform.localScale = desiredScale;
                if (foundSurface)
                    LiftMarkerAboveSurface(newRock, hit);
                else
                    LiftMarkerAboveHeight(newRock, point.y + markerSurfaceClearance);

                if (enableSparkles && sparklePrefab != null)
                {
                    Instantiate(sparklePrefab, newRock.transform.position, Quaternion.identity, newRock.transform);
                }

                newRock.transform.localScale = Vector3.zero;
                newRock.transform.SetParent(rockContainer.transform);

                // Bundle it up with its scale target and track it!
                TrackedRock tr = new TrackedRock { obj = newRock, baseScale = desiredScale };
                newlySpawnedRocks.Add(tr);

                distanceSinceLastRock = 0f;
            }
            lastPos = point;
        }

        if (insertAtFront)
        {
            activeRocks.InsertRange(0, newlySpawnedRocks);
        }
        else
        {
            activeRocks.AddRange(newlySpawnedRocks);
        }
    }

    private void HandleRockPickup()
    {
        int highestTouchedIndex = -1;

        for (int i = 0; i < activeRocks.Count; i++)
        {
            GameObject rock = activeRocks[i].obj;
            if (rock == null) continue;

            if (Vector3.Distance(player.position, rock.transform.position) <= rockPickupDistance)
            {
                highestTouchedIndex = i;
            }
        }

        if (highestTouchedIndex != -1)
        {
            for (int i = highestTouchedIndex; i >= 0; i--)
            {
                if (activeRocks[i].obj != null) Destroy(activeRocks[i].obj);
                activeRocks.RemoveAt(i);
            }
        }
    }

    private void ClearRocks()
    {
        foreach (TrackedRock rock in activeRocks)
        {
            if (rock.obj != null) Destroy(rock.obj);
        }
        activeRocks.Clear();
        
        if (rockContainer != null)
        {
            Destroy(rockContainer);
            rockContainer = null;
        }

        currentRoutePoints.Clear();
    }

    private bool TryFindMatchingSurface(Vector3 navMeshPoint, out RaycastHit bestHit)
    {
        float probeHeight = Mathf.Max(1f, surfaceProbeHeight);
        Vector3 origin = navMeshPoint + Vector3.up * probeHeight;
        int hitCount = Physics.RaycastNonAlloc(
            origin,
            Vector3.down,
            surfaceHits,
            probeHeight * 2f,
            GetGuideSurfaceMask(),
            QueryTriggerInteraction.Ignore);

        int bestIndex = -1;
        float bestDifference = float.MaxValue;
        for (int i = 0; i < hitCount; i++)
        {
            float difference = Mathf.Abs(surfaceHits[i].point.y - navMeshPoint.y);
            if (difference <= maxSurfaceHeightDifference && difference < bestDifference)
            {
                bestDifference = difference;
                bestIndex = i;
            }
        }

        if (bestIndex >= 0)
        {
            bestHit = surfaceHits[bestIndex];
            return true;
        }

        bestHit = default;
        return false;
    }

    private int GetGuideSurfaceMask()
    {
        int mask = groundLayer.value;
        int bridgeLayer = LayerMask.NameToLayer(walkableBridgeLayer);
        if (bridgeLayer >= 0) mask |= 1 << bridgeLayer;

        // A zero mask previously made the prefab silently spawn no guide at all.
        // Keep custom scene masks authoritative, but provide a useful fallback
        // when a newly-created PathGuider has not been configured yet.
        return mask != 0 ? mask : Physics.DefaultRaycastLayers;
    }

    private void LiftMarkerAboveSurface(GameObject marker, RaycastHit surfaceHit)
    {
        if (marker == null) return;

        Renderer[] renderers = marker.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            marker.transform.position += Vector3.up * GetSurfaceClearance(surfaceHit);
            return;
        }

        Bounds combinedBounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            combinedBounds.Encapsulate(renderers[i].bounds);

        float desiredBottom = surfaceHit.point.y + GetSurfaceClearance(surfaceHit);
        float lift = desiredBottom - combinedBounds.min.y;
        if (lift > 0f)
            marker.transform.position += Vector3.up * lift;
    }

    private static void LiftMarkerAboveHeight(GameObject marker, float desiredBottom)
    {
        if (marker == null) return;

        Renderer[] renderers = marker.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return;

        Bounds combinedBounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            combinedBounds.Encapsulate(renderers[i].bounds);

        float lift = desiredBottom - combinedBounds.min.y;
        if (lift > 0f) marker.transform.position += Vector3.up * lift;
    }

    private float GetSurfaceClearance(RaycastHit surfaceHit)
    {
        float clearance = markerSurfaceClearance;
        int bridgeLayer = LayerMask.NameToLayer(walkableBridgeLayer);
        if (bridgeLayer >= 0 && surfaceHit.collider != null &&
            surfaceHit.collider.gameObject.layer == bridgeLayer)
        {
            clearance += bridgeMarkerExtraHeight;
        }

        return clearance;
    }

    private void EnsureNavMeshUpdateSubscription()
    {
        DynamicNavMeshUpdater currentUpdater = DynamicNavMeshUpdater.Instance;
        if (subscribedNavMeshUpdater == currentUpdater) return;

        if (subscribedNavMeshUpdater != null &&
            subscribedNavMeshUpdater.onNavMeshUpdateCompleted != null)
            subscribedNavMeshUpdater.onNavMeshUpdateCompleted.RemoveListener(
                HandleBridgeNavMeshUpdated);

        subscribedNavMeshUpdater = currentUpdater;
        if (subscribedNavMeshUpdater != null &&
            subscribedNavMeshUpdater.onNavMeshUpdateCompleted != null)
            subscribedNavMeshUpdater.onNavMeshUpdateCompleted.AddListener(
                HandleBridgeNavMeshUpdated);
    }

    private void HandleBridgeNavMeshUpdated()
    {
        // Force the active route to be calculated again. This catches both a
        // newly completed bridge and a reconstructed bridge loaded from a save.
        currentlyTargetedWaypoint = null;
        offPathTimer = 0f;
        ClearRocks();
    }

    private void OnDestroy()
    {
        if (subscribedNavMeshUpdater != null &&
            subscribedNavMeshUpdater.onNavMeshUpdateCompleted != null)
            subscribedNavMeshUpdater.onNavMeshUpdateCompleted.RemoveListener(
                HandleBridgeNavMeshUpdated);

        if (Instance == this) Instance = null;
    }
    
    public void SetNewWaypoints(List<GuiderWaypoint> newWaypoints)
    {
        waypoints = newWaypoints;
        currentWaypointIndex = 0; 
        currentlyTargetedWaypoint = null;
        ClearRocks();
    }

    public void RouteToSingleTarget(Transform singleTarget)
    {
        waypoints = new List<GuiderWaypoint> 
        { 
            new GuiderWaypoint { target = singleTarget, advancesTutorial = false } 
        };
        currentWaypointIndex = 0;
        currentlyTargetedWaypoint = null;
        ClearRocks();
    }
}

[System.Serializable]
public class GuiderWaypoint
{
    [Tooltip("The physical location the guide should lead to.")]
    public Transform target;
    
    [Tooltip("If TRUE, reaching this specific waypoint will advance the tutorial to the next step.")]
    public bool advancesTutorial = true; 
}
