using UnityEngine;

/// <summary>
/// Cheap visual motion for a pooled tumbleweed. It uses no Rigidbody and only
/// performs an occasional ground/view check while active.
/// </summary>
[DisallowMultipleComponent]
public sealed class TumbleweedActor : MonoBehaviour
{
    private TumbleweedSpawner owner;
    private TumbleweedSpawnArea sourceArea;
    private Camera visibilityCamera;
    private LayerMask groundLayers;
    private Vector3 travelDirection;
    private Vector3 rollAxis;
    private Vector3 baseScale;
    private float movementSpeed;
    private float rollSpeed;
    private float groundOffset;
    private float groundProbeHeight;
    private float groundProbeDepth;
    private float groundFollowSpeed;
    private float nextGroundProbe;
    private float desiredGroundY;
    private bool hasGroundHeight;
    private float recycleAt;
    private float nextVisibilityCheck;
    private float offscreenSince = -1f;
    private float offscreenRecycleDelay;
    private float maximumCameraDistanceSquared;
    private bool configured;

    internal TumbleweedSpawnArea SourceArea => sourceArea;

    private void Awake()
    {
        baseScale = transform.localScale;
    }

    internal void Launch(
        TumbleweedSpawner newOwner,
        TumbleweedSpawnArea area,
        Camera camera,
        Vector3 position,
        Vector3 direction,
        LayerMask newGroundLayers,
        float speed,
        float degreesPerSecond,
        float scaleMultiplier,
        float lifetime,
        float newGroundOffset,
        float newGroundProbeHeight,
        float newGroundProbeDepth,
        float newGroundFollowSpeed,
        float newOffscreenRecycleDelay,
        float maximumCameraDistance)
    {
        owner = newOwner;
        sourceArea = area;
        visibilityCamera = camera;
        groundLayers = newGroundLayers;
        travelDirection = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
        if (travelDirection.sqrMagnitude < 0.0001f) travelDirection = Vector3.right;
        rollAxis = Vector3.Cross(Vector3.up, travelDirection).normalized;
        movementSpeed = Mathf.Max(0.01f, speed);
        rollSpeed = degreesPerSecond;
        groundOffset = newGroundOffset;
        groundProbeHeight = Mathf.Max(0.1f, newGroundProbeHeight);
        groundProbeDepth = Mathf.Max(0.2f, newGroundProbeDepth);
        groundFollowSpeed = Mathf.Max(0.1f, newGroundFollowSpeed);
        offscreenRecycleDelay = Mathf.Max(0f, newOffscreenRecycleDelay);
        maximumCameraDistanceSquared = maximumCameraDistance > 0f
            ? maximumCameraDistance * maximumCameraDistance
            : 0f;
        recycleAt = Time.time + Mathf.Max(0.1f, lifetime);
        nextVisibilityCheck = Time.time + 0.35f;
        offscreenSince = -1f;
        transform.SetPositionAndRotation(position + Vector3.up * groundOffset, Quaternion.identity);
        transform.localScale = baseScale * Mathf.Max(0.05f, scaleMultiplier);
        desiredGroundY = transform.position.y;
        hasGroundHeight = true;
        nextGroundProbe = Time.time;
        configured = true;
        gameObject.SetActive(true);
    }

    private void Update()
    {
        if (!configured) return;
        if (Time.time >= recycleAt)
        {
            Recycle();
            return;
        }

        float delta = Time.deltaTime;
        Vector3 nextPosition = transform.position + travelDirection * movementSpeed * delta;
        if (Time.time >= nextGroundProbe)
        {
            nextGroundProbe = Time.time + 0.12f;
            Vector3 probeOrigin = nextPosition + Vector3.up * groundProbeHeight;
            hasGroundHeight = Physics.Raycast(
                probeOrigin,
                Vector3.down,
                out RaycastHit hit,
                groundProbeHeight + groundProbeDepth,
                groundLayers,
                QueryTriggerInteraction.Ignore);
            if (hasGroundHeight) desiredGroundY = hit.point.y + groundOffset;
        }

        if (hasGroundHeight)
        {
            nextPosition.y = Mathf.MoveTowards(
                transform.position.y,
                desiredGroundY,
                groundFollowSpeed * delta);
        }

        transform.position = nextPosition;
        transform.Rotate(rollAxis, rollSpeed * delta, Space.World);

        if (Time.time >= nextVisibilityCheck)
        {
            nextVisibilityCheck = Time.time + 0.35f;
            if (ShouldRemainActive())
            {
                offscreenSince = -1f;
            }
            else
            {
                if (offscreenSince < 0f) offscreenSince = Time.time;
                if (Time.time - offscreenSince >= offscreenRecycleDelay) Recycle();
            }
        }
    }

    private bool ShouldRemainActive()
    {
        if (visibilityCamera == null || !visibilityCamera.isActiveAndEnabled) return false;
        Vector3 viewport = visibilityCamera.WorldToViewportPoint(transform.position);
        bool visible = viewport.z > visibilityCamera.nearClipPlane &&
                       viewport.x >= -0.1f && viewport.x <= 1.1f &&
                       viewport.y >= -0.1f && viewport.y <= 1.1f;
        if (!visible) return false;
        return maximumCameraDistanceSquared <= 0f ||
               (visibilityCamera.transform.position - transform.position).sqrMagnitude <=
               maximumCameraDistanceSquared;
    }

    private void Recycle()
    {
        if (!configured) return;
        configured = false;
        TumbleweedSpawner currentOwner = owner;
        TumbleweedSpawnArea currentArea = sourceArea;
        owner = null;
        sourceArea = null;
        currentOwner?.Recycle(this, currentArea);
    }

    private void OnDisable()
    {
        configured = false;
    }
}
