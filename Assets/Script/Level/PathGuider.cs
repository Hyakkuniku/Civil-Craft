using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

[RequireComponent(typeof(LineRenderer))]
public class PathGuider : MonoBehaviour
{
    public static PathGuider Instance { get; private set; }

    [Header("Path Settings")]
    public List<Transform> waypoints; 
    public Transform player;
    public float stoppingDistance = 2.0f;
    public float heightOffset = 0.5f; 
    public float updateInterval = 0.2f; 

    private LineRenderer lineRenderer;
    private NavMeshPath path;
    private float timer = 0f;
    private int currentWaypointIndex = 0;

    private void Awake()
    {
        if (Instance == null) Instance = this;
    }

    private void Start()
    {
        lineRenderer = GetComponent<LineRenderer>();
        path = new NavMeshPath();
        lineRenderer.useWorldSpace = true; 
    }

    private void Update()
    {
        // Auto-Find Player if missing (e.g., loaded a new scene)
        if (player == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null) player = playerObj.transform;
        }

        if (player == null || waypoints == null || currentWaypointIndex >= waypoints.Count)
        {
            lineRenderer.positionCount = 0;
            return;
        }

        Transform currentTarget = waypoints[currentWaypointIndex];
        if (currentTarget == null) return;

        float distanceToTarget = Vector3.Distance(player.position, currentTarget.position);
        if (distanceToTarget <= stoppingDistance)
        {
            currentWaypointIndex++;
            if (currentWaypointIndex >= waypoints.Count)
            {
                lineRenderer.positionCount = 0;
                return;
            }
            currentTarget = waypoints[currentWaypointIndex];
            timer = updateInterval; 
        }

        timer += Time.deltaTime;
        if (timer >= updateInterval)
        {
            timer = 0f;
            CalculateAndDrawPath(currentTarget);
        }
    }

    private void CalculateAndDrawPath(Transform target)
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
                Vector3[] corners = path.corners;
                lineRenderer.positionCount = corners.Length;

                for (int i = 0; i < corners.Length; i++)
                {
                    Vector3 adjustedPoint = new Vector3(corners[i].x, corners[i].y + heightOffset, corners[i].z);
                    lineRenderer.SetPosition(i, adjustedPoint);
                }
            }
            else lineRenderer.positionCount = 0; 
        }
    }
    
    public void SetNewWaypoints(List<Transform> newWaypoints)
    {
        waypoints = newWaypoints;
        currentWaypointIndex = 0; 
        timer = updateInterval;   
    }

    public void RouteToSingleTarget(Transform singleTarget)
    {
        waypoints = new List<Transform> { singleTarget };
        currentWaypointIndex = 0;
        timer = updateInterval;
    }
}