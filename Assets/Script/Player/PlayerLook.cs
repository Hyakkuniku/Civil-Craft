using UnityEngine;

public class PlayerLook : MonoBehaviour
{
    [Header("Camera Setup")]
    public Camera cam;
    [Tooltip("Create an Empty GameObject inside the player near chest/head height and drag it here.")]
    public Transform followTarget; 

    [Header("Distance & Limits")]
    public float defaultDistance = 4f;
    public float minDistance = 1f;
    public float maxDistance = 8f;
    
    [Header("Sensitivity")]
    public float xSensitivity = 30f;
    public float ySensitivity = 30f;

    [Header("Pitch Limits (Up/Down)")]
    public float minPitch = -20f;
    public float maxPitch = 70f;

    [Header("Obstacle Avoidance")]
    [Tooltip("Which layers should the camera collide with? (e.g., Environment, Ground)")]
    public LayerMask collisionMask;
    [Min(0.05f)]
    [Tooltip("Minimum probe radius around the camera. The near clip plane can automatically make this larger.")]
    [SerializeField] private float collisionRadius = 0.25f;
    [Min(0f)]
    [Tooltip("Extra space kept between the camera probe and an obstacle.")]
    [SerializeField] private float collisionPadding = 0.08f;
    [Tooltip("Uses the camera near-clip rectangle to prevent walls from clipping through screen corners.")]
    [SerializeField] private bool protectNearClipPlane = true;
    [Min(0.01f)]
    [Tooltip("How smoothly the camera returns to its normal distance after an obstacle clears.")]
    [SerializeField] private float obstacleReleaseSmoothTime = 0.18f;
    [Min(0.1f)]
    [Tooltip("Target movement beyond this distance is treated as a teleport and snaps the camera into place.")]
    [SerializeField] private float teleportSnapDistance = 2.5f;

    [HideInInspector] public bool canLook = true;

    private float yaw = 0f;
    private float pitch = 15f; // Start looking slightly down
    private float currentDistance;
    private float sensitivityMultiplier = 1f;
    private bool invertLookY;
    private float distanceSmoothVelocity;
    private Vector3 previousFollowPosition;
    private bool hasCameraPose;
    private readonly RaycastHit[] sphereCastHits = new RaycastHit[24];
    private readonly RaycastHit[] raycastHits = new RaycastHit[24];

    private void Start()
    {
        ApplySavedCameraSettings();
        currentDistance = defaultDistance;
        
        // Fallback if target is missing
        if (followTarget == null) followTarget = transform; 
        previousFollowPosition = followTarget.position;

        // Detach the camera from the player so it can orbit freely
        if (cam != null && cam.transform.parent == transform)
        {
            cam.transform.SetParent(null);
        }
    }

    public void ProcessLook(Vector2 input)
    {
        // Lock orbit input only. Do not accumulate hidden yaw/pitch that would
        // suddenly rotate the camera when the tutorial unlocks looking.
        if (!canLook || cam == null || followTarget == null ||
            (TutorialManager.Instance != null && TutorialManager.Instance.IsLookLocked)) return;

        float mouseX = input.x;
        float mouseY = input.y;

        // Calculate Orbit Angles
        yaw += (mouseX * Time.deltaTime) * xSensitivity * sensitivityMultiplier;
        float verticalDirection = invertLookY ? 1f : -1f;
        pitch += (mouseY * Time.deltaTime) * ySensitivity * sensitivityMultiplier * verticalDirection;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
    }

    public void ApplySavedCameraSettings()
    {
        sensitivityMultiplier = Mathf.Clamp(PlayerPrefs.GetFloat("CameraSensitivity", 1f), 0.5f, 2f);
        invertLookY = PlayerPrefs.GetInt("InvertLookY", 0) == 1;
    }

    private void LateUpdate()
    {
        // DO NOT move the camera if Build Mode is active and took control!
        if (!canLook || cam == null || followTarget == null) return;

        // Tutorial look locks must not freeze follow movement. The player can
        // still walk while the camera follows at the unchanged orbit angle.
        UpdateCameraPosition(false);
    }

    /// <summary>Refresh the orbit camera after teleporting, even while input is locked.</summary>
    public void SnapToFollowTarget()
    {
        UpdateCameraPosition(true);
    }

    private void UpdateCameraPosition(bool forceSnap)
    {
        if (cam == null) return;
        if (followTarget == null) followTarget = transform;

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0);
        Vector3 pivot = followTarget.position;
        Vector3 backward = rotation * Vector3.back;
        float desiredDistance = Mathf.Clamp(defaultDistance,
            Mathf.Max(0.01f, minDistance), Mathf.Max(minDistance, maxDistance));
        float collisionLimitedDistance = FindCollisionLimitedDistance(pivot, backward, desiredDistance);

        bool targetTeleported = hasCameraPose &&
            (pivot - previousFollowPosition).sqrMagnitude >
            Mathf.Max(0.1f, teleportSnapDistance) * Mathf.Max(0.1f, teleportSnapDistance);
        if (forceSnap || !hasCameraPose || targetTeleported)
        {
            currentDistance = collisionLimitedDistance;
            distanceSmoothVelocity = 0f;
        }
        else if (collisionLimitedDistance < currentDistance)
        {
            // Pull in immediately. Smoothing toward a newly discovered wall lets
            // the camera spend several frames inside it, which causes clipping.
            currentDistance = collisionLimitedDistance;
            distanceSmoothVelocity = 0f;
        }
        else
        {
            // Move back out gently so wall edges and narrow doorways do not make
            // the camera pop between near and far positions.
            currentDistance = Mathf.SmoothDamp(
                currentDistance,
                collisionLimitedDistance,
                ref distanceSmoothVelocity,
                Mathf.Max(0.01f, obstacleReleaseSmoothTime),
                Mathf.Infinity,
                Time.unscaledDeltaTime);
        }

        cam.transform.SetPositionAndRotation(pivot + backward * currentDistance, rotation);
        previousFollowPosition = pivot;
        hasCameraPose = true;
    }

    private float FindCollisionLimitedDistance(Vector3 origin, Vector3 direction, float desiredDistance)
    {
        if (collisionMask.value == 0 || desiredDistance <= 0f) return desiredDistance;

        float probeRadius = GetCameraProbeRadius();
        float nearestDistance = desiredDistance;

        int sphereHitCount = Physics.SphereCastNonAlloc(
            origin,
            probeRadius,
            direction,
            sphereCastHits,
            desiredDistance,
            collisionMask,
            QueryTriggerInteraction.Ignore);
        nearestDistance = FindNearestValidDistance(sphereCastHits, sphereHitCount, nearestDistance);

        // The centre ray catches very thin geometry that a swept sphere can miss
        // when the cast begins close to, or partly overlapping, a surface.
        int rayHitCount = Physics.RaycastNonAlloc(
            origin,
            direction,
            raycastHits,
            desiredDistance,
            collisionMask,
            QueryTriggerInteraction.Ignore);
        nearestDistance = FindNearestValidDistance(raycastHits, rayHitCount, nearestDistance);

        if (nearestDistance >= desiredDistance) return desiredDistance;

        // Obstacles are allowed to override Min Distance. Keeping the old six-unit
        // minimum in Canyon Crossing forced the camera through any closer wall.
        float emergencyMinimum = Mathf.Max(0.03f, cam.nearClipPlane * 0.2f);
        return Mathf.Clamp(nearestDistance - collisionPadding, emergencyMinimum, desiredDistance);
    }

    private float FindNearestValidDistance(RaycastHit[] hits, int count, float currentNearest)
    {
        int safeCount = Mathf.Min(count, hits.Length);
        for (int i = 0; i < safeCount; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null || IsSelfCollider(hit.collider)) continue;
            if (hit.distance < currentNearest) currentNearest = hit.distance;
        }

        return currentNearest;
    }

    private bool IsSelfCollider(Collider candidate)
    {
        Transform candidateTransform = candidate.transform;
        if (candidateTransform == transform || candidateTransform.IsChildOf(transform)) return true;

        // PlayerLook can also live on a manager while its target belongs to the
        // player hierarchy, so filter the target root independently.
        if (followTarget != null)
        {
            Transform targetRoot = followTarget.root;
            if (candidateTransform == targetRoot || candidateTransform.IsChildOf(targetRoot)) return true;
        }

        return cam != null &&
               (candidateTransform == cam.transform || candidateTransform.IsChildOf(cam.transform));
    }

    private float GetCameraProbeRadius()
    {
        float radius = Mathf.Max(0.05f, collisionRadius);
        if (!protectNearClipPlane || cam == null) return radius;

        float near = Mathf.Max(0.01f, cam.nearClipPlane);
        if (cam.orthographic)
        {
            float halfHeight = cam.orthographicSize;
            float halfWidth = halfHeight * cam.aspect;
            return Mathf.Max(radius, Mathf.Sqrt(halfWidth * halfWidth + halfHeight * halfHeight));
        }

        float nearHalfHeight = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * near;
        float nearHalfWidth = nearHalfHeight * cam.aspect;
        return Mathf.Max(radius,
            Mathf.Sqrt(nearHalfWidth * nearHalfWidth + nearHalfHeight * nearHalfHeight));
    }
}
