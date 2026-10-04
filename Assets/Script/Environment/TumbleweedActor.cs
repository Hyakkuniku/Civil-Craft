using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pooled, collider-free rolling ambience. Its rendered underside follows real
/// terrain; unsupported travel is recycled rather than floating across gaps.
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
    private Vector3 localVisualCenter;
    private Renderer[] visualRenderers;
    private Vector3[] contactOffsets;
    private bool geometryCached;
    private float movementSpeed;
    private float rollSpeed;
    private float groundOffset;
    private float groundProbeHeight;
    private float groundProbeDepth;
    private float groundFollowSpeed;
    private float lastGroundY;
    private float recycleAt;
    private float nextVisibilityCheck;
    private float offscreenSince = -1f;
    private float offscreenRecycleDelay;
    private float maximumCameraDistanceSquared;
    private bool configured;

    internal TumbleweedSpawnArea SourceArea => sourceArea;

    private void Awake()
    {
        CacheGeometry();
    }

    private void CacheGeometry()
    {
        if (geometryCached) return;
        baseScale = transform.localScale;
        visualRenderers = GetComponentsInChildren<Renderer>(true);
        if (visualRenderers.Length > 0)
        {
            Bounds bounds = visualRenderers[0].bounds;
            for (int i = 1; i < visualRenderers.Length; i++) bounds.Encapsulate(visualRenderers[i].bounds);
            localVisualCenter = transform.InverseTransformPoint(bounds.center);
        }
        // Read the tiny prop mesh once. A rotated Renderer AABB overestimates a
        // round weed's support height and would visibly lift it above the floor.
        var points = new List<Vector3>();
        foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh mesh = filter.sharedMesh;
            if (mesh == null || !mesh.isReadable) continue;
            Matrix4x4 meshToRoot = transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            foreach (Vector3 vertex in mesh.vertices) points.Add(meshToRoot.MultiplyPoint3x4(vertex));
        }
        if (points.Count > 0)
        {
            Bounds localBounds = new Bounds(points[0], Vector3.zero);
            for (int i = 1; i < points.Count; i++) localBounds.Encapsulate(points[i]);
            localVisualCenter = localBounds.center;
            contactOffsets = new Vector3[points.Count];
            for (int i = 0; i < points.Count; i++) contactOffsets[i] = points[i] - localVisualCenter;
        }
        geometryCached = true;
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
        CacheGeometry();
        owner = newOwner;
        sourceArea = area;
        visibilityCamera = camera;
        groundLayers = newGroundLayers;
        travelDirection = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
        if (travelDirection.sqrMagnitude < 0.0001f) travelDirection = Vector3.right;
        rollAxis = Vector3.Cross(Vector3.up, travelDirection).normalized;
        movementSpeed = Mathf.Max(0.01f, speed);
        rollSpeed = degreesPerSecond;
        // Older scenes used 0.2 as a guessed pivot height. Bounds now supply the
        // true support height, so retain only a small anti-intersection clearance.
        groundOffset = Mathf.Clamp(newGroundOffset, 0f, 0.02f);
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
        transform.SetPositionAndRotation(position, Quaternion.identity);
        transform.localScale = baseScale * Mathf.Max(0.05f, scaleMultiplier);
        lastGroundY = position.y;
        configured = true;
        if (!TumbleweedGroundProbe.TryFindGround(position + Vector3.up * groundProbeHeight,
                groundProbeHeight + groundProbeDepth, groundLayers, transform, out RaycastHit initialHit) ||
            Mathf.Abs(initialHit.point.y - position.y) > 0.08f)
        {
            Recycle();
            return;
        }
        PlaceVisualOnGround(initialHit.point, initialHit.normal);
        gameObject.SetActive(true);
    }

    private void Update()
    {
        Tick(Time.deltaTime);
    }

    internal void Tick(float delta)
    {
        if (!configured) return;
        if (Time.time >= recycleAt)
        {
            Recycle();
            return;
        }

        if (delta <= 0f) return;
        // Keep support checks close together even after a slow mobile frame.
        delta = Mathf.Min(delta, 0.4f);
        int steps = Mathf.Clamp(Mathf.CeilToInt(delta / 0.05f), 1, 8);
        float stepDelta = delta / steps;
        for (int step = 0; step < steps; step++)
        {
            Vector3 nextCenter = transform.TransformPoint(localVisualCenter) +
                                 travelDirection * movementSpeed * stepDelta;
            Vector3 probeOrigin = new Vector3(nextCenter.x, lastGroundY + groundProbeHeight, nextCenter.z);
            if (!TumbleweedGroundProbe.TryFindGround(probeOrigin, groundProbeHeight + groundProbeDepth,
                    groundLayers, transform, out RaycastHit hit))
            {
                Recycle();
                return;
            }
            // Follow continuous slopes, not cliffs or suddenly taller props.
            float slopeAllowance = 0.08f + movementSpeed * stepDelta * 1.733f;
            float rateAllowance = 0.08f + groundFollowSpeed * stepDelta;
            if (Mathf.Abs(hit.point.y - lastGroundY) > Mathf.Min(slopeAllowance, rateAllowance))
            {
                Recycle();
                return;
            }
            transform.Rotate(rollAxis, rollSpeed * stepDelta, Space.World);
            lastGroundY = hit.point.y;
            nextCenter.y = hit.point.y;
            PlaceVisualOnGround(nextCenter, hit.normal);
        }

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

    private void PlaceVisualOnGround(Vector3 groundPoint, Vector3 normal)
    {
        Vector3 center = transform.TransformPoint(localVisualCenter);
        float lowest = float.PositiveInfinity;
        if (contactOffsets != null)
        {
            Vector3 localProjection = transform.localToWorldMatrix.transpose.MultiplyVector(normal);
            for (int i = 0; i < contactOffsets.Length; i++)
                lowest = Mathf.Min(lowest, Vector3.Dot(localProjection, contactOffsets[i]));
        }
        else
        {
            Vector3 absoluteNormal = new Vector3(Mathf.Abs(normal.x), Mathf.Abs(normal.y), Mathf.Abs(normal.z));
            for (int i = 0; i < visualRenderers.Length; i++)
            {
                if (visualRenderers[i] == null) continue;
                Bounds bounds = visualRenderers[i].bounds;
                lowest = Mathf.Min(lowest, Vector3.Dot(normal, bounds.center - center) -
                    Vector3.Dot(absoluteNormal, bounds.extents));
            }
        }
        float supportHeight = lowest < float.PositiveInfinity ? Mathf.Max(0f, -lowest) : 0f;
        Vector3 targetCenter = groundPoint + Vector3.up * ((supportHeight + groundOffset) / Mathf.Max(0.5f, normal.y));
        // Rotating an imported/off-center pivot must not orbit the visual around it.
        transform.position += targetCenter - center;
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
        if (currentOwner != null) currentOwner.Recycle(this, currentArea);
        else gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        configured = false;
    }
}
