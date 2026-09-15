using UnityEngine;

/// <summary>
/// An authored volume in which TumbleweedSpawner may create tumbleweeds.
/// This component has no collider and therefore cannot interfere with gameplay physics.
/// </summary>
[DisallowMultipleComponent]
public sealed class TumbleweedSpawnArea : MonoBehaviour
{
    [Header("Spawn Volume")]
    [SerializeField] private Vector3 center;
    [SerializeField] private Vector3 size = new Vector3(18f, 4f, 18f);
    [Min(1)] [SerializeField] private int maximumActive = 4;
    [Min(0.01f)] [SerializeField] private float spawnWeight = 1f;

    [Header("Travel")]
    [Tooltip("Direction in this area's local space. The Scene gizmo arrow shows the result.")]
    [SerializeField] private Vector3 localTravelDirection = Vector3.right;
    [Range(0f, 60f)] [SerializeField] private float directionSpread = 12f;

    private int activeCount;

    public int ActiveCount => activeCount;
    public int MaximumActive => Mathf.Max(1, maximumActive);
    public float SpawnWeight => Mathf.Max(0.01f, spawnWeight);
    public bool HasCapacity => isActiveAndEnabled && activeCount < MaximumActive;

    public Bounds WorldBounds
    {
        get
        {
            Vector3 safeSize = new Vector3(
                Mathf.Max(0.01f, Mathf.Abs(size.x)),
                Mathf.Max(0.01f, Mathf.Abs(size.y)),
                Mathf.Max(0.01f, Mathf.Abs(size.z)));
            Vector3 extents = safeSize * 0.5f;
            Vector3 first = transform.TransformPoint(center - extents);
            Bounds bounds = new Bounds(first, Vector3.zero);

            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 corner = center + Vector3.Scale(extents, new Vector3(x, y, z));
                bounds.Encapsulate(transform.TransformPoint(corner));
            }

            return bounds;
        }
    }

    public bool IsEligible(
        Plane[] cameraPlanes,
        Vector3 observerPosition,
        float maximumObserverDistance)
    {
        if (!HasCapacity || cameraPlanes == null || cameraPlanes.Length == 0) return false;

        Bounds bounds = WorldBounds;
        if (!GeometryUtility.TestPlanesAABB(cameraPlanes, bounds)) return false;
        return maximumObserverDistance <= 0f ||
               bounds.SqrDistance(observerPosition) <= maximumObserverDistance * maximumObserverDistance;
    }

    public bool TryGetVisibleGroundPoint(
        Camera camera,
        LayerMask groundLayers,
        float groundProbeHeight,
        float groundProbeDepth,
        float viewportPadding,
        int attempts,
        out Vector3 point)
    {
        point = default;
        if (camera == null) return false;

        Vector3 halfSize = new Vector3(
            Mathf.Max(0.01f, Mathf.Abs(size.x)) * 0.5f,
            Mathf.Max(0.01f, Mathf.Abs(size.y)) * 0.5f,
            Mathf.Max(0.01f, Mathf.Abs(size.z)) * 0.5f);

        for (int attempt = 0; attempt < Mathf.Max(1, attempts); attempt++)
        {
            Vector3 localPoint = center + new Vector3(
                Random.Range(-halfSize.x, halfSize.x),
                halfSize.y,
                Random.Range(-halfSize.z, halfSize.z));
            Vector3 probeOrigin = transform.TransformPoint(localPoint) +
                                  Vector3.up * Mathf.Max(0.1f, groundProbeHeight);

            if (!Physics.Raycast(
                    probeOrigin,
                    Vector3.down,
                    out RaycastHit hit,
                    Mathf.Max(0.2f, groundProbeDepth + groundProbeHeight + size.y),
                    groundLayers,
                    QueryTriggerInteraction.Ignore))
            {
                continue;
            }

            Vector3 viewport = camera.WorldToViewportPoint(hit.point);
            float padding = Mathf.Clamp(viewportPadding, 0f, 0.45f);
            if (viewport.z <= camera.nearClipPlane ||
                viewport.x < padding || viewport.x > 1f - padding ||
                viewport.y < padding || viewport.y > 1f - padding)
            {
                continue;
            }

            point = hit.point;
            return true;
        }

        return false;
    }

    public Vector3 GetTravelDirection()
    {
        Vector3 direction = transform.TransformDirection(localTravelDirection);
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) direction = transform.right;
        direction.y = 0f;
        direction.Normalize();
        return Quaternion.AngleAxis(
            Random.Range(-directionSpread, directionSpread),
            Vector3.up) * direction;
    }

    internal void RegisterSpawn()
    {
        activeCount++;
    }

    internal void RegisterRecycle()
    {
        activeCount = Mathf.Max(0, activeCount - 1);
    }

    private void OnDisable()
    {
        activeCount = 0;
    }

    private void OnValidate()
    {
        size.x = Mathf.Max(0.01f, Mathf.Abs(size.x));
        size.y = Mathf.Max(0.01f, Mathf.Abs(size.y));
        size.z = Mathf.Max(0.01f, Mathf.Abs(size.z));
        maximumActive = Mathf.Max(1, maximumActive);
        spawnWeight = Mathf.Max(0.01f, spawnWeight);
    }

    private void OnDrawGizmosSelected()
    {
        Matrix4x4 oldMatrix = Gizmos.matrix;
        Color oldColor = Gizmos.color;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0.78f, 0.52f, 0.18f, 0.8f);
        Gizmos.DrawWireCube(center, size);

        Vector3 direction = localTravelDirection;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) direction = Vector3.right;
        direction.Normalize();
        float arrowLength = Mathf.Max(1f, Mathf.Min(size.x, size.z) * 0.35f);
        Vector3 start = center;
        Vector3 end = start + direction * arrowLength;
        Gizmos.DrawLine(start, end);
        Gizmos.DrawLine(end, end - direction * 0.3f + Vector3.forward * 0.2f);
        Gizmos.DrawLine(end, end - direction * 0.3f - Vector3.forward * 0.2f);

        Gizmos.matrix = oldMatrix;
        Gizmos.color = oldColor;
    }
}
