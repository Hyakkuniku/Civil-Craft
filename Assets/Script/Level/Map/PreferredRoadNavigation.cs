using UnityEngine;
using UnityEngine.AI;

/// <summary>Prefer roads only when they remain close to the direct navigable route.</summary>
public static class PreferredRoadNavigation
{
    // Limit additional walking to BOTH 10% and four world units.
    private const float MaximumExtraFraction = .1f;
    private const float MaximumExtraDistance = 4f;
    public const float DirectApproachDistance = 12f;
    public const string LinkAreaName = "RavineLink";

    public static void RefineFinalApproach(NavMeshAgent agent)
    {
        if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh ||
            agent.pathPending || !agent.hasPath || agent.isOnOffMeshLink) return;
        Vector3 destination = agent.destination;
        if (Vector3.Distance(agent.nextPosition, destination) > DirectApproachDistance) return;
        float remaining = agent.remainingDistance;
        // Keep steering/avoidance in NavMeshAgent. Do not Move/Warp/transform
        // the NPC directly or it could cut through obstacles or other actors.
        if (Calculate(agent, destination, out NavMeshPath approach) &&
            approach.status == NavMeshPathStatus.PathComplete && Length(approach) + .05f < remaining)
            agent.SetPath(approach);
    }

    public static bool Calculate(NavMeshAgent agent, Vector3 destination, out NavMeshPath path)
    {
        var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
        for (int i = 0; i < 32; i++) filter.SetAreaCost(i, agent.GetAreaCost(i));
        return Calculate(agent.nextPosition, destination, filter, out path);
    }

    // The caller supplies its original mask so a previous ground-only trip
    // cannot lock the next cross-ravine trip out of using links.
    public static bool SetDestination(NavMeshAgent agent, Vector3 destination, int allowedMask)
    {
        var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = allowedMask };
        for (int i = 0; i < 32; i++) filter.SetAreaCost(i, agent.GetAreaCost(i));
        if (!CalculateGroundFirst(agent.nextPosition, destination, filter, out NavMeshPath path, out int routeMask) ||
            path.status != NavMeshPathStatus.PathComplete) return false;
        int previousMask = agent.areaMask;
        agent.areaMask = routeMask; // Also constrain Unity's automatic repathing.
        if (agent.SetPath(path)) return true;
        agent.areaMask = previousMask;
        return false;
    }

    public static bool Calculate(Vector3 start, Vector3 destination, out NavMeshPath path)
    {
        var filter = new NavMeshQueryFilter
        {
            agentTypeID = NavMesh.GetSettingsCount() > 0 ? NavMesh.GetSettingsByIndex(0).agentTypeID : 0,
            areaMask = NavMesh.AllAreas
        };
        for (int i = 0; i < 32; i++) filter.SetAreaCost(i, NavMesh.GetAreaCost(i));
        return Calculate(start, destination, filter, out path);
    }

    private static bool Calculate(Vector3 start, Vector3 destination, NavMeshQueryFilter filter,
        out NavMeshPath path)
    {
        return CalculateGroundFirst(start, destination, filter, out path, out _);
    }

    private static bool CalculateGroundFirst(Vector3 start, Vector3 destination, NavMeshQueryFilter filter,
        out NavMeshPath path, out int routeMask)
    {
        routeMask = filter.areaMask;
        int linkArea = NavMesh.GetAreaFromName(LinkAreaName);
        if (linkArea >= 0)
        {
            var groundFilter = new NavMeshQueryFilter
            {
                agentTypeID = filter.agentTypeID,
                areaMask = filter.areaMask & ~(1 << linkArea)
            };
            for (int i = 0; i < 32; i++) groundFilter.SetAreaCost(i, filter.GetAreaCost(i));
            if (CalculateRoadPreference(start, destination, groundFilter, out path) &&
                path.status == NavMeshPathStatus.PathComplete)
            {
                routeMask = groundFilter.areaMask;
                return true;
            }
        }
        // A partial ground route does not reach the target. Permit actual
        // crossings only now, without disabling links globally for other NPCs.
        return CalculateRoadPreference(start, destination, filter, out path);
    }

    private static bool CalculateRoadPreference(Vector3 start, Vector3 destination, NavMeshQueryFilter filter,
        out NavMeshPath path)
    {
        path = new NavMeshPath();
        bool found = NavMesh.CalculatePath(start, destination, filter, path);
        int roadArea = NavMesh.GetAreaFromName("DirtPath");
        int groundArea = NavMesh.GetAreaFromName("Walkable");
        if (roadArea < 0 || groundArea < 0 || filter.GetAreaCost(roadArea) >= filter.GetAreaCost(groundArea))
            return found;

        // Remove only the road preference. Obstacle masks and link/other-area
        // penalties stay intact; this never creates a straight line through walls.
        filter.SetAreaCost(roadArea, filter.GetAreaCost(groundArea));
        var direct = new NavMeshPath();
        bool foundDirect = NavMesh.CalculatePath(start, destination, filter, direct);
        if (!foundDirect || direct.status != NavMeshPathStatus.PathComplete) return found;

        // A small road detour is still a visible dog-leg on an open plaza or
        // beside a phase marker. Prefer the unweighted route for these trips.
        bool clearApproach = !NavMesh.Raycast(start, destination, out _, filter);
        if (clearApproach || Length(direct) <= DirectApproachDistance ||
            !found || path.status != NavMeshPathStatus.PathComplete ||
            ExceedsDetourLimit(Length(path), Length(direct)))
        {
            path = direct;
            return true;
        }
        return found;
    }

    /// <summary>Remove local guide dog-legs only where the baked mesh proves a shortcut is walkable.</summary>
    public static Vector3[] GetGuideCorners(NavMeshPath path)
    {
        Vector3[] corners = path.corners;
        if (corners.Length < 3) return corners;
        var filter = new NavMeshQueryFilter
        {
            agentTypeID = NavMesh.GetSettingsCount() > 0 ? NavMesh.GetSettingsByIndex(0).agentTypeID : 0,
            areaMask = NavMesh.AllAreas
        };
        var result = new System.Collections.Generic.List<Vector3> { corners[0] };
        int current = 0;
        while (current < corners.Length - 1)
        {
            int next = current + 1;
            for (int candidate = corners.Length - 1; candidate > next; candidate--)
            {
                if (Vector3.Distance(corners[current], corners[candidate]) > DirectApproachDistance)
                    continue;
                // NavMesh rays stop at holes and link crossings, not just walls.
                if (NavMesh.Raycast(corners[current], corners[candidate], out _, filter)) continue;
                next = candidate;
                break;
            }
            result.Add(corners[next]);
            current = next;
        }
        return result.ToArray();
    }

    internal static bool ExceedsDetourLimit(float preferredLength, float directLength)
    {
        float allowance = Mathf.Min(directLength * MaximumExtraFraction, MaximumExtraDistance);
        return preferredLength > directLength + allowance + .01f;
    }

    private static float Length(NavMeshPath path)
    {
        Vector3[] corners = path.corners;
        float length = 0f;
        for (int i = 1; i < corners.Length; i++) length += Vector3.Distance(corners[i - 1], corners[i]);
        return length;
    }
}
