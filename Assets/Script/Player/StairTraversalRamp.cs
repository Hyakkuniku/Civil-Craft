using UnityEngine;

/// <summary>
/// Adds an invisible, walkable slope over a staircase at runtime. Attach this
/// to the stair object and place the local endpoints on the lower and upper
/// walking surfaces. The visible stairs and their existing colliders stay intact.
/// </summary>
[DisallowMultipleComponent]
public sealed class StairTraversalRamp : MonoBehaviour
{
    [Header("Walkable Surface (local to this stair object)")]
    [SerializeField, Tooltip("Center of the lower end of the walkable stairs.")]
    private Vector3 lowerPoint = new Vector3(0f, 0f, -1f);

    [SerializeField, Tooltip("Center of the upper landing.")]
    private Vector3 upperPoint = new Vector3(0f, 1f, 1f);

    [SerializeField, Min(0.1f), Tooltip("Width of the invisible walking surface.")]
    private float width = 2f;

    [SerializeField, Min(0.01f), Tooltip("Thickness of the invisible ramp collider.")]
    private float thickness = 0.08f;

    [SerializeField, Min(0f), Tooltip("Lift the ramp slightly above the visible steps so their edges cannot snag the player.")]
    private float surfaceClearance = 0.05f;

    [SerializeField, Min(0f), Tooltip("Extend the ramp before the first step so the player enters it smoothly.")]
    private float lowerApproach = 0.25f;

    private BoxCollider walkCollider;

    private void OnEnable()
    {
        if (!Application.isPlaying) return;

        if (walkCollider == null)
            CreateWalkCollider();
        else
            walkCollider.enabled = true;
    }

    private void OnDisable()
    {
        if (walkCollider != null)
            walkCollider.enabled = false;
    }

    private void OnDestroy()
    {
        if (Application.isPlaying && walkCollider != null)
            Destroy(walkCollider.gameObject);
    }

    private void CreateWalkCollider()
    {
        if (!TryGetRamp(out Vector3 center, out Quaternion rotation,
                out Vector3 size, out float slope))
        {
            Debug.LogWarning("[StairTraversalRamp] Set distinct lower and upper points with horizontal separation.", this);
            return;
        }

        GameObject ramp = new GameObject("Walkable Stair Ramp (runtime)");
        ramp.layer = gameObject.layer;
        ramp.transform.SetParent(transform, false);
        ramp.transform.localPosition = center;
        ramp.transform.localRotation = rotation;
        walkCollider = ramp.AddComponent<BoxCollider>();
        walkCollider.size = size;

        // The player prefab currently has a 45-degree CharacterController
        // slope limit. A steeper surface may still block movement.
        if (slope > 45f)
            Debug.LogWarning("[StairTraversalRamp] Ramp is steeper than the player's 45-degree slope limit. Lengthen its run or lower its rise.", this);
    }

    private bool TryGetRamp(out Vector3 center, out Quaternion rotation,
        out Vector3 size, out float slope)
    {
        Vector3 direction = upperPoint - lowerPoint;
        float horizontalRun = new Vector2(direction.x, direction.z).magnitude;
        float length = direction.magnitude;
        center = Vector3.zero;
        rotation = Quaternion.identity;
        size = Vector3.zero;
        slope = 0f;
        if (horizontalRun < 0.01f || length < 0.01f) return false;

        slope = Mathf.Atan2(Mathf.Abs(direction.y), horizontalRun) * Mathf.Rad2Deg;
        rotation = Quaternion.LookRotation(direction / length, Vector3.up);

        Vector3 walkNormal = rotation * Vector3.up;
        Vector3 surfaceMiddle = (lowerPoint + upperPoint) * 0.5f +
            Vector3.up * surfaceClearance - direction / length * (lowerApproach * 0.5f);
        center = surfaceMiddle - walkNormal * (thickness * 0.5f);
        size = new Vector3(Mathf.Max(0.1f, width), Mathf.Max(0.01f, thickness),
            length + Mathf.Max(0f, lowerApproach));
        return true;
    }

    private void OnDrawGizmosSelected()
    {
        if (!TryGetRamp(out Vector3 center, out Quaternion rotation,
                out Vector3 size, out float slope)) return;

        Matrix4x4 previousMatrix = Gizmos.matrix;
        Color previousColor = Gizmos.color;
        Gizmos.matrix = transform.localToWorldMatrix * Matrix4x4.TRS(center, rotation, Vector3.one);
        Gizmos.color = slope > 45f ? new Color(1f, 0.35f, 0.2f, 0.9f)
            : new Color(0.2f, 0.9f, 0.5f, 0.9f);
        Gizmos.DrawWireCube(Vector3.zero, size);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawSphere(lowerPoint, 0.07f);
        Gizmos.DrawSphere(upperPoint, 0.07f);
        Gizmos.matrix = previousMatrix;
        Gizmos.color = previousColor;
    }

    private void OnValidate()
    {
        width = Mathf.Max(0.1f, width);
        thickness = Mathf.Max(0.01f, thickness);
        surfaceClearance = Mathf.Max(0f, surfaceClearance);
        lowerApproach = Mathf.Max(0f, lowerApproach);
    }
}
