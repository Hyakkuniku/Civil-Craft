using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

public class PathGuider : MonoBehaviour
{
    public static PathGuider Instance { get; private set; }

    [Header("Wasp 3D Object Settings")]
    [Tooltip("Drag your 3D Wasp, Drone, or Fairy Prefab here!")]
    public Transform waspObject;
    [Tooltip("How fast the wasp flies to the objective")]
    public float waspSpeed = 8f;
    [Tooltip("How smoothly the wasp rotates to face the path")]
    public float waspTurnSpeed = 10f;
    [Tooltip("How long to wait before the wasp shoots out from the player again")]
    public float respawnDelay = 1.5f;

    [Header("Path Settings")]
    public List<Transform> waypoints; 
    public Transform player;
    public float stoppingDistance = 2.0f;

    [Header("Terrain Hugging")]
    [Tooltip("How detailed the path should be. 1.0 is great.")]
    public float pathResolution = 1.0f;
    [Tooltip("How high the wasp hovers above the ground")]
    public float heightOffset = 1.5f; 
    [Tooltip("Set this to your Ground/Terrain layer!")]
    public LayerMask groundLayer;

    private NavMeshPath path;
    private int currentWaypointIndex = 0;
    
    // Wasp Navigation Memory
    private List<Vector3> detailedPathPoints = new List<Vector3>();
    private int currentWaspNodeIndex = 0;
    private float respawnTimer = 0f;
    private bool isWaspActive = false;

    private void Awake()
    {
        if (Instance == null) Instance = this;
    }

    private void Start()
    {
        path = new NavMeshPath();
        
        // Hide the wasp until a path is active
        if (waspObject != null) waspObject.gameObject.SetActive(false);
    }

    private void Update()
    {
        // 1. Auto-Find Player if missing
        if (player == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null) player = playerObj.transform;
        }

        // 2. Shut off if no player or no waypoints
        if (player == null || waypoints == null || currentWaypointIndex >= waypoints.Count)
        {
            if (waspObject != null && isWaspActive)
            {
                isWaspActive = false;
                waspObject.gameObject.SetActive(false);
            }
            return;
        }

        Transform currentTarget = waypoints[currentWaypointIndex];
        if (currentTarget == null) return;

        // 3. Check if player reached the objective
        if (Vector3.Distance(player.position, currentTarget.position) <= stoppingDistance)
        {
            currentWaypointIndex++;
            isWaspActive = false; 
            if (waspObject != null) waspObject.gameObject.SetActive(false);
            return;
        }

        // 4. Handle the Wasp Lifecycle
        HandleWaspLifecycle(currentTarget);
    }

    private void HandleWaspLifecycle(Transform target)
    {
        if (waspObject == null) return;

        // STATE 1: Wasp is invisible, waiting to shoot out
        if (!isWaspActive)
        {
            respawnTimer += Time.deltaTime;
            if (respawnTimer >= respawnDelay)
            {
                // THE FIX: Calculate the path ONLY right before the wasp spawns!
                CalculatePath(target);

                // If a valid path to the objective was found...
                if (detailedPathPoints.Count > 0)
                {
                    isWaspActive = true;
                    // Snap the wasp to the player's CURRENT position
                    waspObject.position = player.position + new Vector3(0, heightOffset, 0);
                    waspObject.gameObject.SetActive(true);
                    currentWaspNodeIndex = 0;
                }
                
                respawnTimer = 0f; // Reset the timer
            }
        }
        // STATE 2: Wasp is actively flying
        else
        {
            FlyWasp();
        }
    }

    private void FlyWasp()
    {
        Vector3 targetNode = detailedPathPoints[currentWaspNodeIndex];

        // Move the Wasp
        waspObject.position = Vector3.MoveTowards(waspObject.position, targetNode, waspSpeed * Time.deltaTime);

        // Rotate the Wasp smoothly
        Vector3 direction = (targetNode - waspObject.position).normalized;
        if (direction != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            waspObject.rotation = Quaternion.Slerp(waspObject.rotation, targetRotation, waspTurnSpeed * Time.deltaTime);
        }

        // Check if we reached the current node
        if (Vector3.Distance(waspObject.position, targetNode) < 0.2f)
        {
            currentWaspNodeIndex++;
            
            // Did we reach the very end of the path?
            if (currentWaspNodeIndex >= detailedPathPoints.Count)
            {
                isWaspActive = false; // Despawn the wasp and trigger State 1 again
                waspObject.gameObject.SetActive(false);
            }
        }
    }

    private void CalculatePath(Transform target)
    {
        NavMeshHit hit;
        Vector3 safeStart = player.position;
        Vector3 safeTarget = target.position;

        if (NavMesh.SamplePosition(player.position, out hit, 5f, NavMesh.AllAreas)) safeStart = hit.position;
        if (NavMesh.SamplePosition(target.position, out hit, 5f, NavMesh.AllAreas)) safeTarget = hit.position;

        if (NavMesh.CalculatePath(safeStart, safeTarget, NavMesh.AllAreas, path))
        {
            if (path.status == NavMeshPathStatus.PathComplete || path.status == NavMeshPathStatus.PathPartial)
            {
                GenerateSmoothTerrainPath(path.corners);
            }
            else
            {
                detailedPathPoints.Clear(); // No path found
            }
        }
    }

    private void GenerateSmoothTerrainPath(Vector3[] corners)
    {
        detailedPathPoints.Clear();

        for (int i = 0; i < corners.Length - 1; i++)
        {
            Vector3 start = corners[i];
            Vector3 end = corners[i + 1];
            float distance = Vector3.Distance(start, end);
            
            int segments = Mathf.Max(1, Mathf.CeilToInt(distance / pathResolution));

            for (int j = 0; j < segments; j++)
            {
                float t = (float)j / segments;
                detailedPathPoints.Add(SnapToGround(Vector3.Lerp(start, end, t)));
            }
        }
        
        if (corners.Length > 0)
        {
            detailedPathPoints.Add(SnapToGround(corners[corners.Length - 1]));
        }
    }

    private Vector3 SnapToGround(Vector3 position)
    {
        Vector3 rayOrigin = new Vector3(position.x, position.y + 10f, position.z);
        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 20f, groundLayer))
        {
            return hit.point + new Vector3(0, heightOffset, 0);
        }
        return position + new Vector3(0, heightOffset, 0);
    }
    
    // --- Existing API kept intact so other scripts don't break! ---
    public void SetNewWaypoints(List<Transform> newWaypoints)
    {
        waypoints = newWaypoints;
        currentWaypointIndex = 0; 
        ResetWasp();
    }

    public void RouteToSingleTarget(Transform singleTarget)
    {
        waypoints = new List<Transform> { singleTarget };
        currentWaypointIndex = 0;
        ResetWasp();
    }

    private void ResetWasp()
    {
        // Instantly forces the wasp to shoot out from the player on a new objective
        isWaspActive = false;
        respawnTimer = respawnDelay; 
        if (waspObject != null) waspObject.gameObject.SetActive(false);
    }
}