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

    [Header("Dynamic Recalculation")]
    [Tooltip("If the player strays this far (in meters) from the trail, it will draw new rocks to guide them back.")]
    public float offPathTolerance = 4.0f;
    [Tooltip("How often (in seconds) to check if the player left the path.")]
    public float offPathCheckInterval = 0.5f;

    [Header("Path Settings")]
    public List<GuiderWaypoint> waypoints; 
    public Transform player;
    public float stoppingDistance = 2.0f;

    [Header("Terrain Hugging")]
    [Tooltip("How detailed the path should be calculated.")]
    public float pathResolution = 0.5f;
    [Tooltip("How high the rocks should sit above the ground so they don't clip inside it.")]
    public float heightOffset = 0.1f; 
    [Tooltip("Set this to your Ground/Terrain layer!")]
    public LayerMask groundLayer;

    private NavMeshPath path;
    private int currentWaypointIndex = 0;
    
    private List<GameObject> activeRocks = new List<GameObject>(); 
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
    }

    private bool IsPlayerOffPath(Transform target)
    {
        int validRockCount = 0;
        float minDistToRock = float.MaxValue;

        foreach (GameObject rock in activeRocks)
        {
            if (rock == null) continue;

            validRockCount++;
            float dist = Vector3.Distance(player.position, rock.transform.position);
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

        // Find the index of the closest rock
        for (int i = 0; i < activeRocks.Count; i++)
        {
            if (activeRocks[i] == null) continue;
            float dist = Vector3.Distance(player.position, activeRocks[i].transform.position);
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

        // --- PATH CUTTING FIX: Destroy all rocks the player bypassed! ---
        for (int i = closestIndex - 1; i >= 0; i--)
        {
            if (activeRocks[i] != null) Destroy(activeRocks[i]);
            activeRocks.RemoveAt(i);
        }

        NavMeshHit hit;
        Vector3 safeStart = player.position;
        Vector3 safeTarget = activeRocks[0].transform.position; // Target the new first rock

        if (NavMesh.SamplePosition(player.position, out hit, 5f, NavMesh.AllAreas)) safeStart = hit.position;
        if (NavMesh.SamplePosition(safeTarget, out hit, 5f, NavMesh.AllAreas)) safeTarget = hit.position;

        NavMeshPath returnPath = new NavMeshPath();
        if (NavMesh.CalculatePath(safeStart, safeTarget, NavMesh.AllAreas, returnPath))
        {
            if (returnPath.status == NavMeshPathStatus.PathComplete || returnPath.status == NavMeshPathStatus.PathPartial)
            {
                List<Vector3> points = GenerateSmoothTerrainPath(returnPath.corners);
                SpawnRocksAlongPath(points, true); // True = Insert at the front of the list
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

        List<GameObject> newlySpawnedRocks = new List<GameObject>();

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
                    foreach (GameObject existingRock in activeRocks)
                    {
                        if (existingRock == null) continue;
                        
                        if (Vector3.Distance(spawnPos, existingRock.transform.position) < (rockSpacing * 0.8f))
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
                    
                    // --- ANIMATION FIX: Inject the magic growing animation script ---
                    newRock.AddComponent<RockSpawnAnimation>();

                    newRock.transform.SetParent(rockContainer.transform);
                    newlySpawnedRocks.Add(newRock);
                }

                distanceSinceLastRock = 0f;
            }
            lastPos = point;
        }

        // Put the new rocks into our tracking list in the correct order
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

        // Find the furthest rock down the path that the player is touching
        for (int i = 0; i < activeRocks.Count; i++)
        {
            GameObject rock = activeRocks[i];
            if (rock == null) continue;

            if (Vector3.Distance(player.position, rock.transform.position) <= rockPickupDistance)
            {
                highestTouchedIndex = i;
            }
        }

        // --- PATH CUTTING FIX: Delete that rock AND everything behind it! ---
        if (highestTouchedIndex != -1)
        {
            for (int i = highestTouchedIndex; i >= 0; i--)
            {
                if (activeRocks[i] != null) Destroy(activeRocks[i]);
                activeRocks.RemoveAt(i);
            }
        }
    }

    private void ClearRocks()
    {
        foreach (GameObject rock in activeRocks)
        {
            if (rock != null) Destroy(rock);
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

// --- NEW CLASS: Makes the rocks grow smoothly instead of popping! ---
public class RockSpawnAnimation : MonoBehaviour
{
    private Vector3 targetScale;
    private float speed = 8f;

    private void Start()
    {
        targetScale = transform.localScale;
        transform.localScale = Vector3.zero; // Start invisible
    }

    private void Update()
    {
        transform.localScale = Vector3.Lerp(transform.localScale, targetScale, Time.deltaTime * speed);
        
        // Once it's basically full size, snap it perfectly and delete this script to save performance
        if (Vector3.Distance(transform.localScale, targetScale) < 0.05f)
        {
            transform.localScale = targetScale;
            Destroy(this); 
        }
    }
}