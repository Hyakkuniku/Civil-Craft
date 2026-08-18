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

    [Header("Path Settings")]
    public List<GuiderWaypoint> waypoints; 
    public Transform player;
    public float stoppingDistance = 2.0f;

    [Header("Terrain Hugging")]
    public float pathResolution = 0.5f;
    public float heightOffset = 0.1f; 
    public LayerMask groundLayer;

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
                if (IsPlayerOffPath(currentTarget))
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
                // Fractional looping timer (0.0 to 1.0)
                // Subtracting i * waveStep makes the wave flow exactly from the Player (0) to Target (N)
                float t = timeVal - (i * waveStep);
                t = t - Mathf.Floor(t); // Loops perfectly from 0.0 to 1.0

                // Map the timer to our scale
                float scaleMult = Mathf.Lerp(minWaveScale, 1f, t);
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
        int validRockCount = 0;
        float minDistToRock = float.MaxValue;

        foreach (TrackedRock rock in activeRocks)
        {
            if (rock.obj == null) continue;

            validRockCount++;
            float dist = Vector3.Distance(player.position, rock.obj.transform.position);
            if (dist < minDistToRock)
            {
                minDistToRock = dist;
            }
        }

        if (validRockCount == 0)
        {
            return Vector3.Distance(player.position, target.position) > (stoppingDistance + rockPickupDistance);
        }

        return minDistToRock > offPathTolerance;
    }

    private void GenerateRockPath(Transform target)
    {
        ClearRocks();

        NavMeshHit hit;
        Vector3 safeStart = player.position;
        Vector3 safeTarget = target.position;

        if (NavMesh.SamplePosition(player.position, out hit, 5f, NavMesh.AllAreas)) safeStart = hit.position;
        if (NavMesh.SamplePosition(target.position, out hit, 5f, NavMesh.AllAreas)) safeTarget = hit.position;

        if (NavMesh.CalculatePath(safeStart, safeTarget, NavMesh.AllAreas, path))
        {
            if (path.status == NavMeshPathStatus.PathComplete || path.status == NavMeshPathStatus.PathPartial)
            {
                List<Vector3> points = GenerateSmoothTerrainPath(path.corners);
                SpawnRocksAlongPath(points, false);
            }
        }
    }

    private void GeneratePathBackToTrail(Transform targetDestination)
    {
        int closestIndex = -1;
        float minDistToRock = float.MaxValue;

        for (int i = 0; i < activeRocks.Count; i++)
        {
            if (activeRocks[i].obj == null) continue;
            float dist = Vector3.Distance(player.position, activeRocks[i].obj.transform.position);
            if (dist < minDistToRock)
            {
                minDistToRock = dist;
                closestIndex = i;
            }
        }

        if (closestIndex == -1)
        {
            GenerateRockPath(targetDestination);
            return;
        }

        for (int i = closestIndex - 1; i >= 0; i--)
        {
            if (activeRocks[i].obj != null) Destroy(activeRocks[i].obj);
            activeRocks.RemoveAt(i);
        }

        NavMeshHit hit;
        Vector3 safeStart = player.position;
        Vector3 safeTarget = activeRocks[0].obj.transform.position; 

        if (NavMesh.SamplePosition(player.position, out hit, 5f, NavMesh.AllAreas)) safeStart = hit.position;
        if (NavMesh.SamplePosition(safeTarget, out hit, 5f, NavMesh.AllAreas)) safeTarget = hit.position;

        NavMeshPath returnPath = new NavMeshPath();
        if (NavMesh.CalculatePath(safeStart, safeTarget, NavMesh.AllAreas, returnPath))
        {
            if (returnPath.status == NavMeshPathStatus.PathComplete || returnPath.status == NavMeshPathStatus.PathPartial)
            {
                List<Vector3> points = GenerateSmoothTerrainPath(returnPath.corners);
                SpawnRocksAlongPath(points, true); 
            }
        }
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
                Vector3 rayOrigin = new Vector3(point.x, point.y + 10f, point.z);
                if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 20f, groundLayer))
                {
                    Vector3 spawnPos = hit.point + new Vector3(0, heightOffset, 0);

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
                    
                    Quaternion slopeRotation = Quaternion.FromToRotation(Vector3.up, hit.normal);
                    
                    if (randomizeRotation)
                    {
                        slopeRotation *= Quaternion.Euler(0, Random.Range(0f, 360f), 0);
                    }

                    GameObject randomPrefab = rockPrefabs[Random.Range(0, rockPrefabs.Count)];
                    GameObject newRock = Instantiate(randomPrefab, spawnPos, slopeRotation);

                    Vector3 desiredScale = newRock.transform.localScale * rockScaleMultiplier;

                    if (enableSparkles && sparklePrefab != null)
                    {
                        Instantiate(sparklePrefab, newRock.transform.position, Quaternion.identity, newRock.transform);
                    }
                    
                    newRock.transform.localScale = Vector3.zero;
                    newRock.transform.SetParent(rockContainer.transform);
                    
                    // Bundle it up with its scale target and track it!
                    TrackedRock tr = new TrackedRock { obj = newRock, baseScale = desiredScale };
                    newlySpawnedRocks.Add(tr);
                }

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
        }
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