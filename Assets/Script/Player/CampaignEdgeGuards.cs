using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public sealed class CampaignEdgeGuards : MonoBehaviour
{
    public const string LayerName = "WorldEdgeGuard";
    public const int LayerIndex = 13;
    public const int GuardMask = 1 << LayerIndex;
    private const float ContactHold = 0.15f;
    private const float FadeTime = 0.35f;
    private const float LookAheadTime = 0.14f;
    private const float MaxLookAhead = 1.1f;
    private const float DropZoneProbeDepth = 6f;
    private const float MaximumStepDown = 1.15f;
    private const float RailLength = 3f;
    private const float LandingLift = 0.15f;
    private const float RecoveryCellWidth = 64f;

    [Serializable]
    public struct RecoveryArea
    {
        public Bounds bounds;
        public Vector3 nearBank;
        public Vector3 farBank;
    }

    [SerializeField] private Transform guardRoot;
    [SerializeField] private Shader railShader;
    [SerializeField] private RecoveryArea[] recoveryAreas = Array.Empty<RecoveryArea>();
    [SerializeField] private Vector3 campaignSpawn;
    [SerializeField] private Quaternion campaignSpawnRotation = Quaternion.identity;
    [SerializeField] private ExpandedMinimapController expandedMap;
    [SerializeField] private CinematicDirector[] cinematics = Array.Empty<CinematicDirector>();

    private CharacterController controller;
    private PlayerMotor motor;
    private PhysicsScene physicsScene;
    private int groundMask;
    private readonly Collider[] clearanceHits = new Collider[32];
    private readonly RaycastHit[] footingHits = new RaycastHit[16];
    private readonly RaycastHit[] approachHits = new RaycastHit[16];
    private readonly RaycastHit[] stepHits = new RaycastHit[12];
    private readonly Dictionary<int, List<int>> recoveryCells = new Dictionary<int, List<int>>();
    private Pose lastBank;
    private bool hasLastBank;
    private float nextFallCheck;

    private Mesh railMesh;
    private Material railMaterial;
    private MaterialPropertyBlock railProperties;
    private int colorId;
    private BoxCollider contact;
    private Vector3 contactPoint;
    private Vector3 dropDirection;
    private float contactTime = float.NegativeInfinity;
    private int contactFrame = -1;
    private float contactStrength;

    public static int ExcludeFrom(int layers) => layers & ~GuardMask;

    /// <summary>Map navigation follows the authored campaign perimeter even when guard collisions are off.</summary>
    public bool TryGetMapBounds(out Bounds bounds)
    {
        bounds = default;
        if (guardRoot == null) return false;
        bool found = false;
        foreach (BoxCollider guard in guardRoot.GetComponentsInChildren<BoxCollider>(true))
        {
            if (guard.gameObject.layer != LayerIndex) continue;
            Vector3 half = guard.size * .5f;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 local = guard.center + new Vector3(
                    (corner & 1) == 0 ? -half.x : half.x,
                    (corner & 2) == 0 ? -half.y : half.y,
                    (corner & 4) == 0 ? -half.z : half.z);
                Vector3 world = guard.transform.TransformPoint(local);
                if (!float.IsFinite(world.x) || !float.IsFinite(world.y) || !float.IsFinite(world.z)) continue;
                if (found) bounds.Encapsulate(world);
                else { bounds = new Bounds(world, Vector3.zero); found = true; }
            }
        }
        return found && bounds.size.x > .1f && bounds.size.z > .1f;
    }

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        motor = GetComponent<PlayerMotor>();
        physicsScene = gameObject.scene.GetPhysicsScene();
        groundMask = ExcludeFrom(Physics.DefaultRaycastLayers) & ~LayerMask.GetMask("Water");
        for (int i = 0; i < recoveryAreas.Length; i++)
        {
            Bounds bounds = recoveryAreas[i].bounds;
            for (int cell = RecoveryCell(bounds.min.x); cell <= RecoveryCell(bounds.max.x); cell++)
            {
                if (!recoveryCells.TryGetValue(cell, out List<int> entries))
                    recoveryCells.Add(cell, entries = new List<int>());
                entries.Add(i);
            }
        }
    }

    private bool IsFreeRoam()
    {
        GameManager game = GameManager.Instance;
        return game != null && game.CurrentState == GameManager.GameState.Normal && !game.IsTransitioning &&
            !LoadingScreenManager.IsLoading && controller != null && controller.enabled &&
            Time.timeScale > 0f && (PauseManager.Instance == null || !PauseManager.Instance.isPaused) &&
            Array.TrueForAll(cinematics, cinematic => cinematic == null || !cinematic.IsPlaying);
    }

    private void Start() => ApplyEdgeProtection();

    private bool ApplyEdgeProtection()
    {
        GameManager game = GameManager.Instance;
        if (guardRoot == null || game == null) return false;

        bool preventFalls = game.PreventWalkingOffEdges;
        if (guardRoot.gameObject.activeSelf != preventFalls)
            guardRoot.gameObject.SetActive(preventFalls);
        return preventFalls;
    }

    internal Vector3 LimitMovement(Vector3 horizontalVelocity, float deltaTime, bool jumping)
    {
        Vector3 movement = horizontalVelocity * deltaTime;
        if (!ApplyEdgeProtection() || movement.sqrMagnitude < 0.000001f || !CanShowRail()) return movement;

        if (horizontalVelocity.sqrMagnitude >= 0.04f && TryFindApproachingGuard(horizontalVelocity, out RaycastHit hit))
            RememberContact((BoxCollider)hit.collider, hit.point, -Vector3.Dot(horizontalVelocity.normalized, hit.normal));

        return !jumping && controller.isGrounded
            ? LimitStepOverDrop(movement, horizontalVelocity.magnitude)
            : movement;
    }

    private Vector3 LimitStepOverDrop(Vector3 movement, float speed)
    {
        float distance = movement.magnitude;
        Vector3 direction = movement / distance;
        Vector3 position = transform.position;
        // A virtual foot leads the next step. Only look for drops in the
        // authored recovery corridors, where stepping down is unsafe.
        float lead = Mathf.Clamp(speed * 0.045f, 0.25f, 0.5f);
        Vector3 end = position + direction * (distance + lead);
        if (!IsInRecoveryArea(end - Vector3.up * DropZoneProbeDepth)) return movement;

        CapsuleAt(controller, position, transform.rotation, out Vector3 bottom, out _, out float radius);
        Vector3 feet = bottom - Vector3.up * radius;
        float stepHeight = controller.stepOffset * Mathf.Abs(transform.lossyScale.y) + 0.15f;
        float minimumUp = Mathf.Cos(Mathf.Min(89f, controller.slopeLimit + 1f) * Mathf.Deg2Rad);
        bool HasSurface(float travel)
        {
            Vector3 sample = feet + direction * travel;
            float above = Mathf.Max(stepHeight, travel + 0.12f);
            int count = physicsScene.Raycast(sample + Vector3.up * above, Vector3.down, stepHits,
                above + MaximumStepDown, groundMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = stepHits[i];
                Collider support = hit.collider;
                if (hit.normal.y >= minimumUp && !support.transform.IsChildOf(transform) &&
                    !Physics.GetIgnoreLayerCollision(gameObject.layer, support.gameObject.layer)) return true;
            }
            return false;
        }

        // Small seams between two supported pieces are still walkable.
        bool HasFooting(float travel) => HasSurface(travel) ||
            (HasSurface(travel + 0.12f) && HasSurface(Mathf.Max(0f, travel - 0.12f)));

        if (HasFooting(distance + lead)) return movement;

        float safe = 0f;
        float unsafeDistance = distance;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            float middle = (safe + unsafeDistance) * 0.5f;
            if (HasFooting(middle + lead)) safe = middle;
            else unsafeDistance = middle;
        }
        RememberContact(null, feet + direction * (safe + lead), 0f, direction);
        return direction * safe;
    }

    private bool TryFindApproachingGuard(Vector3 velocity, out RaycastHit nearest)
    {
        nearest = default;
        Vector3 direction = velocity.normalized;
        float distance = Mathf.Min(MaxLookAhead, velocity.magnitude * LookAheadTime);
        CapsuleAt(controller, transform.position, transform.rotation, out Vector3 bottom, out Vector3 top, out float radius);
        // Use a slightly smaller capsule so existing floor and wall contacts
        // do not cover up the next obstacle in the direction of movement.
        float inset = Mathf.Clamp(controller.skinWidth, 0.02f, radius * 0.2f);
        int count = physicsScene.CapsuleCast(bottom, top, radius - inset, direction, approachHits,
            distance + inset, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        if (count == approachHits.Length) return false;

        float closest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = approachHits[i];
            Collider obstacle = hit.collider;
            int layer = obstacle.gameObject.layer;
            if (hit.distance > closest || obstacle.transform.IsChildOf(transform) ||
                Vector3.Dot(direction, hit.normal) >= -0.08f) continue;
            if (layer != LayerIndex && Physics.GetIgnoreLayerCollision(gameObject.layer, layer)) continue;
            // At equal distances, let solid scenery obscure the warning.
            if (hit.distance == closest && layer == LayerIndex) continue;
            nearest = hit;
            closest = hit.distance;
        }
        return nearest.collider is BoxCollider box && box.transform.IsChildOf(guardRoot) && Mathf.Abs(nearest.normal.y) <= 0.5f;
    }

    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (guardRoot == null || !(hit.collider is BoxCollider box) || !box.transform.IsChildOf(guardRoot) ||
            Mathf.Abs(hit.normal.y) > 0.5f) return;
        Vector3 horizontalMove = Vector3.ProjectOnPlane(hit.moveDirection, Vector3.up);
        float strength = -Vector3.Dot(horizontalMove, hit.normal);
        if (strength > 0.08f)
            RememberContact(box, hit.point, strength + 2f);
    }

    private void RememberContact(BoxCollider box, Vector3 point, float strength, Vector3 drop = default)
    {
        // Move can report several contacts at a corner. Keep the strongest
        // opposing face. Physical contacts take precedence over the look-ahead.
        if (box == contact) strength += 0.08f;
        if (contactFrame == Time.frameCount && strength < contactStrength) return;
        contact = box;
        contactPoint = point;
        dropDirection = drop;
        contactTime = Time.unscaledTime;
        contactFrame = Time.frameCount;
        contactStrength = strength;
    }

    private bool CanShowRail() => IsFreeRoam() &&
        !DialogueManager.IsAnyDialogueActive && !QuizUIManager.IsAnyQuizActive &&
        !RecordingCameraShortcut.BlocksMovement(this) &&
        (expandedMap == null || !expandedMap.IsExpanded) &&
        (UIPanelCoordinator.Instance == null || !UIPanelCoordinator.Instance.HasOpenPanel);

    private void LateUpdate()
    {
        bool preventFalls = ApplyEdgeProtection();
        if (contact == null && dropDirection == Vector3.zero) return;
        float age = Time.unscaledTime - contactTime;
        if (!preventFalls || age >= ContactHold + FadeTime || !CanShowRail())
        {
            ClearContact();
            return;
        }

        Camera camera = GameManager.Instance.MainCamera;
        if (camera == null || !camera.isActiveAndEnabled || railShader == null) return;
        DrawRail(camera, age);
    }

    private void DrawRail(Camera camera, float age)
    {
        if (railMesh == null) CreateRail();

        float alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((age - ContactHold) / FadeTime));
        railProperties.SetColor(colorId, new Color(1f, 0.72f, 0.24f, 0.75f * alpha));
        var draw = new RenderParams(railMaterial)
        {
            camera = camera,
            matProps = railProperties,
            shadowCastingMode = ShadowCastingMode.Off,
            receiveShadows = false,
            lightProbeUsage = LightProbeUsage.Off
        };
        Graphics.RenderMesh(draw, railMesh, 0, RailMatrix());
    }

    private Matrix4x4 RailMatrix()
    {
        if (contact == null)
            return Matrix4x4.TRS(contactPoint, Quaternion.LookRotation(dropDirection), new Vector3(2f, 1f, 1f));

        Transform wall = contact.transform;
        Vector3 localHit = wall.InverseTransformPoint(contactPoint) - contact.center;
        float halfWall = contact.size.x * 0.5f;
        float halfRail = Mathf.Min(halfWall, RailLength * 0.5f);
        localHit.x = Mathf.Clamp(localHit.x, -halfWall + halfRail, halfWall - halfRail);
        localHit.y = Mathf.Clamp(localHit.y - 0.55f, -contact.size.y * 0.5f, contact.size.y * 0.5f - 1.1f);
        float side = Mathf.Sign(wall.InverseTransformPoint(transform.position).z - contact.center.z);
        localHit.z = (contact.size.z * 0.5f + 0.02f) * side;
        return wall.localToWorldMatrix *
            Matrix4x4.TRS(localHit + contact.center, Quaternion.identity, new Vector3(halfRail * 2f, 1f, 1f));
    }

    private void CreateRail()
    {
        railMaterial = new Material(railShader) { hideFlags = HideFlags.HideAndDontSave };
        railProperties = new MaterialPropertyBlock();
        colorId = Shader.PropertyToID("_Color");
        railMesh = new Mesh { name = "Campaign edge contact rail", hideFlags = HideFlags.HideAndDontSave };
        // Two narrow horizontal bars and three posts in a single, depth-tested draw.
        var vertices = new Vector3[20];
        var triangles = new int[30];
        void Quad(int index, float left, float right, float bottom, float top)
        {
            int v = index * 4, t = index * 6;
            vertices[v] = new Vector3(left, bottom, 0f);
            vertices[v + 1] = new Vector3(right, bottom, 0f);
            vertices[v + 2] = new Vector3(right, top, 0f);
            vertices[v + 3] = new Vector3(left, top, 0f);
            triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
            triangles[t + 3] = v; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
        }
        Quad(0, -0.5f, 0.5f, 0.40f, 0.46f);
        Quad(1, -0.5f, 0.5f, 0.91f, 0.99f);
        Quad(2, -0.50f, -0.47f, 0f, 0.91f);
        Quad(3, -0.015f, 0.015f, 0f, 0.91f);
        Quad(4, 0.47f, 0.50f, 0f, 0.91f);
        railMesh.vertices = vertices;
        railMesh.triangles = triangles;
        railMesh.RecalculateBounds();
    }

    private void ClearContact()
    {
        contact = null;
        dropDirection = Vector3.zero;
        contactFrame = -1;
    }

    private void OnDisable() => ClearContact();

    private void OnDestroy()
    {
        if (railMesh != null) Destroy(railMesh);
        if (railMaterial != null) Destroy(railMaterial);
    }

    public bool OverlapsGuard(Vector3 position)
    {
        if (GameManager.Instance != null && !GameManager.Instance.PreventWalkingOffEdges) return false;
        CharacterController capsule = controller != null ? controller : GetComponent<CharacterController>();
        CapsuleAt(capsule, position, transform.rotation, out Vector3 bottom, out Vector3 top, out float radius);
        return Physics.CheckCapsule(bottom, top, radius, GuardMask, QueryTriggerInteraction.Ignore);
    }

    public void RememberBank(Vector3 position, Quaternion rotation, Collider support)
    {
        if (support == null || support.attachedRigidbody != null || support.gameObject.layer == LayerMask.NameToLayer("Bridge") ||
            support.GetComponentInParent<Bar>() != null || IsInRecoveryArea(position) || OverlapsGuard(position)) return;
        lastBank = new Pose(position, rotation);
        hasLastBank = true;
    }

    public bool TryRecoverOverworldPlayer(bool hasSafePose, Vector3 safePosition, Quaternion safeRotation)
    {
        if (guardRoot == null || !IsFreeRoam() || controller.isGrounded || Time.unscaledTime < nextFallCheck) return false;
        nextFallCheck = Time.unscaledTime + 0.08f;
        Vector3 position = transform.position;

        if (!recoveryCells.TryGetValue(RecoveryCell(position.x), out List<int> entries)) return false;
        foreach (int index in entries)
        {
            RecoveryArea area = recoveryAreas[index];
            if (!area.bounds.Contains(position)) continue;
            Vector3 origin = hasLastBank ? lastBank.position : hasSafePose ? safePosition : campaignSpawn;
            Vector3 across = Vector3.ProjectOnPlane(area.farBank - area.nearBank, Vector3.up);
            Vector3 middle = (area.nearBank + area.farBank) * 0.5f;
            bool IsOnNearSide(Vector3 point) => Vector3.Dot(point - middle, across) <= 0f;
            bool nearSide = IsOnNearSide(origin);
            Vector3 fallback = nearSide ? area.nearBank : area.farBank;
            Pose landing;
            bool found =
                (hasSafePose && IsOnNearSide(safePosition) == nearSide &&
                    TryGetLanding(new Pose(safePosition, safeRotation), out landing)) ||
                (hasLastBank && TryGetLanding(lastBank, out landing)) ||
                TryGetLanding(new Pose(fallback, transform.rotation), out landing) ||
                TryGetLanding(new Pose(campaignSpawn, campaignSpawnRotation), out landing);
            if (!found) return false;

            controller.enabled = false;
            transform.SetPositionAndRotation(landing.position, landing.rotation);
            controller.enabled = true;
            if (motor != null) motor.ResetTestMotion();
            ClearContact();
            if (PathGuider.Instance != null) PathGuider.Instance.RefreshFromPlayerPosition();
            return true;
        }
        return false;
    }

    private bool IsInRecoveryArea(Vector3 position)
    {
        if (recoveryCells.TryGetValue(RecoveryCell(position.x), out List<int> entries))
            foreach (int index in entries)
                if (recoveryAreas[index].bounds.Contains(position)) return true;
        return false;
    }

    private static int RecoveryCell(float x) => Mathf.FloorToInt(x / RecoveryCellWidth);

    private bool TryGetLanding(Pose candidate, out Pose landing)
    {
        landing = default;
        if (IsInRecoveryArea(candidate.position)) return false;
        CapsuleAt(controller, candidate.position, candidate.rotation, out Vector3 bottom, out _, out float radius);
        Vector3 feet = bottom - Vector3.up * radius;
        int count = Physics.RaycastNonAlloc(feet + Vector3.up * 0.5f, Vector3.down, footingHits, 1.65f,
            ExcludeFrom(Physics.DefaultRaycastLayers) & ~LayerMask.GetMask("Water"), QueryTriggerInteraction.Ignore);
        if (count == footingHits.Length) return false;

        float best = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = footingHits[i];
            if (hit.collider.transform.IsChildOf(transform) || hit.collider.attachedRigidbody != null ||
                hit.distance >= best || Vector3.Angle(hit.normal, Vector3.up) > controller.slopeLimit) continue;
            best = hit.distance;
        }
        if (float.IsPositiveInfinity(best)) return false;

        Vector3 resolved = candidate.position + Vector3.up * (0.5f - best + LandingLift);
        if (IsInRecoveryArea(resolved)) return false;
        CapsuleAt(controller, resolved, candidate.rotation, out bottom, out Vector3 top, out radius);
        count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, clearanceHits, Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        if (count == clearanceHits.Length) return false;
        for (int i = 0; i < count; i++)
        {
            Collider obstacle = clearanceHits[i];
            if (obstacle.transform.IsChildOf(transform)) continue;
            if (((1 << obstacle.gameObject.layer) & GuardMask) != 0 ||
                !Physics.GetIgnoreLayerCollision(gameObject.layer, obstacle.gameObject.layer)) return false;
        }
        landing = new Pose(resolved, candidate.rotation);
        return true;
    }

    private static void CapsuleAt(CharacterController capsule, Vector3 position, Quaternion rotation,
        out Vector3 bottom, out Vector3 top, out float radius)
    {
        Vector3 scale = capsule.transform.lossyScale;
        radius = capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        float offset = Mathf.Max(0f, capsule.height * Mathf.Abs(scale.y) * 0.5f - radius);
        Vector3 center = position + rotation * Vector3.Scale(capsule.center, scale);
        bottom = center - Vector3.up * offset;
        top = center + Vector3.up * offset;
    }
}
