using System.Collections;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

/// <summary>Connects authored ravine links to nearby baked banks without inventing crossings.</summary>
public sealed class RavineNavMeshLinks : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float endpointSnapRadius = 1.5f;

    private IEnumerator Start()
    {
        // All world surfaces must be registered before querying their polygons.
        yield return null;
        RepairLinks();
    }

    public void RepairLinks()
    {
        foreach (NavMeshLink link in GetComponentsInChildren<NavMeshLink>())
        {
            if (!link.isActiveAndEnabled) continue;
            if (!TryResolve(link, endpointSnapRadius, out Vector3 start, out Vector3 end))
            {
                Debug.LogWarning($"[RavineNavMeshLinks] {link.name}: could not place both endpoints on their nearby banks. Bake the world surface, then inspect this link's endpoints. No endpoints were changed.", link);
                continue;
            }
            Apply(link, start, end);
        }
    }

    public static bool TryResolve(NavMeshLink link, float radius, out Vector3 start, out Vector3 end)
    {
        // NavMeshLink uses position/rotation, not Transform scale, for its endpoints.
        Vector3 authoredStart = link.transform.position + link.transform.rotation * link.startPoint;
        Vector3 authoredEnd = link.transform.position + link.transform.rotation * link.endPoint;
        start = authoredStart;
        end = authoredEnd;
        var filter = new NavMeshQueryFilter { agentTypeID = link.agentTypeID, areaMask = NavMesh.AllAreas };
        if (!NavMesh.SamplePosition(authoredStart, out NavMeshHit a, radius, filter) ||
            !NavMesh.SamplePosition(authoredEnd, out NavMeshHit b, radius, filter)) return false;

        // Never collapse both endpoints onto one bank or reverse the crossing.
        Vector3 axis = Vector3.ProjectOnPlane(authoredEnd - authoredStart, Vector3.up);
        if (axis.sqrMagnitude < 0.01f) return false;
        Vector3 middle = (authoredStart + authoredEnd) * 0.5f;
        if (Vector3.Dot(a.position - middle, axis) >= 0f ||
            Vector3.Dot(b.position - middle, axis) <= 0f ||
            (b.position - a.position).sqrMagnitude < 0.25f) return false;
        start = a.position;
        end = b.position;
        return true;
    }

    public static void Apply(NavMeshLink link, Vector3 start, Vector3 end)
    {
        Quaternion inverse = Quaternion.Inverse(link.transform.rotation);
        link.startPoint = inverse * (start - link.transform.position);
        link.endPoint = inverse * (end - link.transform.position);
        link.bidirectional = true;
        link.UpdateLink();
    }
}
