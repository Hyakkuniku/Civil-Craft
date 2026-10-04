using UnityEngine;

/// <summary>Shared, allocation-free terrain query for spawning and rolling ambience.</summary>
public static class TumbleweedGroundProbe
{
    private static readonly RaycastHit[] Hits = new RaycastHit[32];
    private static readonly int ExcludedLayers = LayerMask.GetMask("Ignore Raycast", "Player", "Water",
        "UI", "Interactable", "MapNodes", "Node", "Bridge");

    public static bool TryFindGround(Vector3 origin, float distance, LayerMask layers,
        Transform ignoreRoot, out RaycastHit ground)
    {
        ground = default;
        int mask = layers.value & ~ExcludedLayers;
        if (mask == 0 || distance <= 0f) return false;
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, Hits, distance, mask,
            QueryTriggerInteraction.Ignore);
        // A truncated, unordered hit set cannot guarantee the nearest support.
        if (count >= Hits.Length) return false;
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = Hits[i];
            Collider collider = hit.collider;
            if (collider == null || collider.isTrigger || collider.attachedRigidbody != null ||
                collider is CharacterController || hit.normal.y < 0.5f ||
                (ignoreRoot != null && collider.transform.IsChildOf(ignoreRoot)) || hit.distance >= nearest)
                continue;
            nearest = hit.distance;
            ground = hit;
        }
        return nearest < float.PositiveInfinity;
    }
}
