using System.Text;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Read-only diagnostics for an agent that stops, receives a partial path, or
/// cannot reach a dynamically baked bridge. This component never changes the
/// agent's active path.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
public sealed class NavMeshPathDebugger : MonoBehaviour
{
    [Header("Optional Target")]
    [SerializeField] private Transform debugTarget;

    [Header("Automatic Diagnostics")]
    [SerializeField] private bool diagnoseAutomatically = true;
    [Min(0.1f)] [SerializeField] private float stalledDuration = 1.5f;
    [Min(0.01f)] [SerializeField] private float stalledVelocity = 0.05f;
    [Min(0.05f)] [SerializeField] private float minimumRemainingDistance = 0.5f;
    [Min(0.1f)] [SerializeField] private float logCooldown = 3f;

    [Header("Sampling")]
    [Min(0.05f)] [SerializeField] private float targetSampleRadius = 1f;
    [Min(0.01f)] [SerializeField] private float gizmoRadius = 0.15f;

    private NavMeshAgent agent;
    private NavMeshPath lastDiagnosticPath;
    private Vector3 lastRequestedDestination;
    private Vector3 lastSampledDestination;
    private Vector3 lastReachablePoint;
    private bool hasSampledDestination;
    private float stalledTimer;
    private float nextAllowedLogTime;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        lastDiagnosticPath = new NavMeshPath();
    }

    private void Update()
    {
        if (!diagnoseAutomatically || agent == null || !agent.enabled ||
            agent.pathPending || agent.isOnOffMeshLink)
        {
            stalledTimer = 0f;
            return;
        }

        bool statusFailed = agent.hasPath &&
                            agent.pathStatus != NavMeshPathStatus.PathComplete;
        bool isFarFromDestination =
            agent.remainingDistance > agent.stoppingDistance + minimumRemainingDistance;
        bool missingPathToAssignedTarget = debugTarget != null && !agent.hasPath &&
            Vector3.Distance(transform.position, debugTarget.position) >
            agent.stoppingDistance + minimumRemainingDistance;
        bool appearsStalled = agent.hasPath && isFarFromDestination &&
                              agent.velocity.sqrMagnitude <= stalledVelocity * stalledVelocity;

        stalledTimer = (appearsStalled || missingPathToAssignedTarget)
            ? stalledTimer + Time.unscaledDeltaTime
            : 0f;
        if ((statusFailed || stalledTimer >= stalledDuration) &&
            Time.unscaledTime >= nextAllowedLogTime)
        {
            DiagnoseCurrentPath();
            nextAllowedLogTime = Time.unscaledTime + logCooldown;
            stalledTimer = 0f;
        }
    }

    public void SetDebugTarget(Transform target)
    {
        debugTarget = target;
    }

    [ContextMenu("Diagnose Current Path")]
    public void DiagnoseCurrentPath()
    {
        if (agent == null) agent = GetComponent<NavMeshAgent>();

        StringBuilder report = new StringBuilder();
        report.AppendLine($"[NavMeshPathDebugger] Agent: {name}");
        report.AppendLine(
            $"Enabled={agent.enabled}, OnNavMesh={agent.isOnNavMesh}, " +
            $"PathPending={agent.pathPending}, HasPath={agent.hasPath}, " +
            $"CurrentStatus={agent.pathStatus}, OnLink={agent.isOnOffMeshLink}");

        if (!agent.enabled)
        {
            report.AppendLine("CAUSE: The NavMeshAgent component is disabled.");
            Debug.LogError(report.ToString(), this);
            return;
        }

        if (!agent.isOnNavMesh)
        {
            report.AppendLine(
                "CAUSE: The agent is not placed on a NavMesh built for its Agent Type. " +
                "Check Agent Type ID, spawn position, Surface bounds, and base offset.");
            Debug.LogError(report.ToString(), this);
            return;
        }

        lastRequestedDestination = debugTarget != null
            ? debugTarget.position
            : agent.destination;
        report.AppendLine(
            $"Position={transform.position:F3}, RequestedDestination={lastRequestedDestination:F3}, " +
            $"RemainingDistance={agent.remainingDistance:F3}, Velocity={agent.velocity.magnitude:F3}");

        hasSampledDestination = NavMesh.SamplePosition(
            lastRequestedDestination,
            out NavMeshHit sampledTarget,
            targetSampleRadius,
            agent.areaMask);

        if (!hasSampledDestination)
        {
            report.AppendLine(
                $"CAUSE: No NavMesh was found within {targetSampleRadius:F2} units of the destination. " +
                "The target may be outside the Surface volume/layer mask or above a missing NavMesh island.");
            Debug.LogError(report.ToString(), this);
            return;
        }

        lastSampledDestination = sampledTarget.position;
        report.AppendLine(
            $"SampledDestination={lastSampledDestination:F3}, " +
            $"SampleOffset={Vector3.Distance(lastRequestedDestination, lastSampledDestination):F3}, " +
            $"SampledArea={sampledTarget.mask}");

        lastDiagnosticPath.ClearCorners();
        bool calculated = agent.CalculatePath(lastSampledDestination, lastDiagnosticPath);
        Vector3[] corners = lastDiagnosticPath.corners;
        lastReachablePoint = corners.Length > 0
            ? corners[corners.Length - 1]
            : transform.position;

        report.AppendLine(
            $"CalculatePathReturned={calculated}, CalculatedStatus={lastDiagnosticPath.status}, " +
            $"Corners={corners.Length}, LastReachablePoint={lastReachablePoint:F3}");

        switch (lastDiagnosticPath.status)
        {
            case NavMeshPathStatus.PathComplete:
                report.AppendLine(
                    "RESULT: The global path is complete. If the agent still stops, inspect local avoidance, " +
                    "a carving obstacle, stopping distance, acceleration, or a collider physically blocking the character.");
                break;

            case NavMeshPathStatus.PathPartial:
                report.AppendLine(
                    "RESULT: The start is valid, but the destination is on a disconnected NavMesh island. " +
                    "The last reachable point marks the seam to inspect for a voxel gap, excessive slope, " +
                    "insufficient bridge width, or a missing/misaligned NavMeshLink.");
                AppendBoundaryInformation(report);
                break;

            default:
                report.AppendLine(
                    "RESULT: Unity could not create a usable path. Check matching Agent Type IDs, area masks, " +
                    "whether both endpoints are on the same loaded NavMeshData, and whether the destination sampled " +
                    "onto the wrong vertical floor.");
                break;
        }

        Debug.Log(
            lastDiagnosticPath.status == NavMeshPathStatus.PathComplete
                ? report.ToString()
                : $"<color=orange>{report}</color>",
            this);
    }

    private void AppendBoundaryInformation(StringBuilder report)
    {
        if (NavMesh.FindClosestEdge(
                lastReachablePoint, out NavMeshHit edge, agent.areaMask))
        {
            report.AppendLine(
                $"ClosestBoundary={edge.position:F3}, BoundaryDistance={edge.distance:F3}, " +
                $"BoundaryNormal={edge.normal:F3}");
        }

        if (NavMesh.Raycast(
                lastReachablePoint,
                lastSampledDestination,
                out NavMeshHit blockage,
                agent.areaMask))
        {
            report.AppendLine(
                $"NavMeshRaycastBlockedAt={blockage.position:F3}, " +
                $"DistanceToRequestedTarget={Vector3.Distance(blockage.position, lastRequestedDestination):F3}");
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (lastDiagnosticPath == null) return;

        Vector3[] corners = lastDiagnosticPath.corners;
        Gizmos.color = lastDiagnosticPath.status == NavMeshPathStatus.PathComplete
            ? Color.green
            : lastDiagnosticPath.status == NavMeshPathStatus.PathPartial
                ? Color.yellow
                : Color.red;

        for (int i = 0; i < corners.Length; i++)
        {
            Gizmos.DrawSphere(corners[i], gizmoRadius);
            if (i > 0) Gizmos.DrawLine(corners[i - 1], corners[i]);
        }

        if (hasSampledDestination)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(lastSampledDestination, gizmoRadius * 1.5f);
        }

        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(lastRequestedDestination, gizmoRadius * 1.5f);
    }
}
