using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Put this on the boat prefab root. A matching contract makes the boat cross
/// during that contract's bridge test; only a completed crossing hides it.
/// </summary>
[DisallowMultipleComponent]
public sealed class BoatBridgeCrossing : MonoBehaviour
{
    [Header("Contract and Route")]
    public ContractSO assignedContract;
    [Tooltip("Scene marker for the boat's starting pose. Keep route markers outside the boat hierarchy.")]
    public Transform startPoint;
    public Transform endPoint;
    [SerializeField, Min(0.01f)] private float speed = 3f;
    [SerializeField, Min(0f)] private float startDelay;
    [Tooltip("Optional. Leave empty to find the scene's bridge simulation manager.")]
    [SerializeField] private BridgePhysicsManager physicsManager;

    [Header("Bridge Impact")]
    [Tooltip("Use the manual box instead of the boat's Mesh Colliders. Leave off for accurate mesh-shaped impacts.")]
    [SerializeField] private bool useManualHull;
    [Tooltip("Boat-local impact box center when Use Manual Hull is enabled.")]
    [SerializeField] private Vector3 hullCenter;
    [SerializeField] private Vector3 hullSize = new Vector3(2f, 1f, 4f);
    [Tooltip("Must include the layers used by simulated bridge bars.")]
    [SerializeField] private LayerMask bridgeCollisionLayers = ~0;
    [Tooltip("Prevent the boat from physically pushing other objects. Mesh Colliders stay enabled during crossing so exact impact checks work.")]
    [FormerlySerializedAs("disableBoatPhysicalColliders")]
    [SerializeField] private bool preventBoatPhysicalContacts = true;

    private readonly RaycastHit[] sweepHits = new RaycastHit[64];
    private readonly Collider[] overlapHits = new Collider[64];
    private readonly List<Collider> bridgeColliders = new List<Collider>(128);
    private BridgePhysicsManager subscribedManager;
    private BuildLocation activeLocation;
    private BridgeSelectionOutline buildModeOutline;
    private Renderer[] boatRenderers;
    private MeshCollider[] boatMeshColliders;
    private Bounds[] boatMeshWorldBounds;
    private bool hasBoatMeshColliders;
    private bool[] authoredRendererStates;
    private Vector3 activeHullCenter;
    private Vector3 activeHullSize;
    private bool moving;
    private bool finishedCrossing;
    private float delayRemaining;
    private bool warnedFullQuery;

    private bool HasIndependentRoute => startPoint != null && endPoint != null &&
        !startPoint.IsChildOf(transform) && !endPoint.IsChildOf(transform);

    private void Awake()
    {
        // Existing workbench components must be removed during migration; moving
        // a Build Location would move the entire bridge site with the boat.
        if (GetComponent<BuildLocation>() != null)
        {
            Debug.LogError("[BoatBridgeCrossing] Move this component from the Build Location onto the boat prefab root.", this);
            enabled = false;
            return;
        }

        boatRenderers = GetComponentsInChildren<Renderer>(true);
        boatMeshColliders = GetComponentsInChildren<MeshCollider>(true);
        boatMeshWorldBounds = new Bounds[boatMeshColliders.Length];
        for (int i = 0; i < boatMeshColliders.Length; i++)
            if (boatMeshColliders[i] != null && boatMeshColliders[i].sharedMesh != null)
                hasBoatMeshColliders = true;
        authoredRendererStates = new bool[boatRenderers.Length];
        for (int i = 0; i < boatRenderers.Length; i++)
            authoredRendererStates[i] = boatRenderers[i].enabled;
        RefreshHull();

        if (preventBoatPhysicalContacts)
        {
            // Disabling a MeshCollider removes the shape ComputePenetration
            // needs. Exclude solver contacts instead, and enable the shape only
            // while the boat is checking bridge impacts.
            foreach (Collider boatCollider in GetComponentsInChildren<Collider>(true))
            {
                if (boatCollider is MeshCollider)
                {
                    boatCollider.includeLayers = 0;
                    boatCollider.excludeLayers = ~0;
                    boatCollider.layerOverridePriority = int.MaxValue;
                }
                boatCollider.enabled = false;
            }
        }
    }

    private void OnEnable()
    {
        BindSimulationManager();
    }

    private void Start()
    {
        // Also catches a manager instantiated after this boat's OnEnable.
        BindSimulationManager();
        if (HasIndependentRoute)
            transform.SetPositionAndRotation(startPoint.position, startPoint.rotation);
        RefreshHull();
    }

    private void OnDisable()
    {
        moving = false;
        SetMeshQueriesActive(false);
        UnbindSimulationManager();
        DisposeOutline();
    }

    private void OnDestroy()
    {
        UnbindSimulationManager();
        DisposeOutline();
    }

    private void BindSimulationManager()
    {
        if (physicsManager == null)
            physicsManager = FindObjectOfType<BridgePhysicsManager>();
        if (physicsManager == null || subscribedManager == physicsManager) return;

        UnbindSimulationManager();
        subscribedManager = physicsManager;
        subscribedManager.OnSimulationStarted += HandleSimulationStarted;
        subscribedManager.OnSimulationStopped += HandleSimulationStopped;
    }

    private void UnbindSimulationManager()
    {
        if (subscribedManager == null) return;
        subscribedManager.OnSimulationStarted -= HandleSimulationStarted;
        subscribedManager.OnSimulationStopped -= HandleSimulationStopped;
        subscribedManager = null;
    }

    private void Update()
    {
        bool showOutline = !finishedCrossing && !moving && HasIndependentRoute &&
            GameManager.Instance != null &&
            GameManager.Instance.CurrentState == GameManager.GameState.Building &&
            !GameManager.Instance.IsTransitioning &&
            GameManager.Instance.ActiveBuildLocation != null &&
            MatchesContract(assignedContract, GameManager.Instance.CurrentContract) &&
            (subscribedManager == null || !subscribedManager.IsSimulationActive);

        if (showOutline && buildModeOutline == null)
            buildModeOutline = new BridgeSelectionOutline(transform);
        buildModeOutline?.SetBuildModeVisualOnlyVisible(showOutline);
    }

    private static bool MatchesContract(ContractSO assigned, ContractSO current)
    {
        return assigned != null && current != null &&
            (assigned == current ||
             string.Equals(assigned.ContractID, current.ContractID,
                 System.StringComparison.Ordinal));
    }

    private void HandleSimulationStarted()
    {
        if (!MatchesContract(assignedContract,
                GameManager.Instance != null ? GameManager.Instance.CurrentContract : null) ||
            GameManager.Instance.ActiveBuildLocation == null)
            return;

        if (!HasIndependentRoute)
        {
            Debug.LogWarning("[BoatBridgeCrossing] Assign Start Point and End Point outside the boat prefab hierarchy.", this);
            return;
        }

        activeLocation = GameManager.Instance.ActiveBuildLocation;
        CacheBridgeColliders();
        finishedCrossing = false;
        SetBoatVisible(true);
        transform.SetPositionAndRotation(startPoint.position, startPoint.rotation);
        SetMeshQueriesActive(true);
        RefreshHull();
        delayRemaining = startDelay;
        warnedFullQuery = false;
        moving = true;
        buildModeOutline?.SetBuildModeVisualOnlyVisible(false);
    }

    private void HandleSimulationStopped()
    {
        moving = false;
        SetMeshQueriesActive(false);
        activeLocation = null;
        bridgeColliders.Clear();
        delayRemaining = 0f;
        if (startPoint != null)
            transform.SetPositionAndRotation(startPoint.position, startPoint.rotation);
        // A completed crossing stays hidden. Retry makes it visible again when
        // the next simulation starts; an interrupted crossing remains visible.
        SetBoatVisible(!finishedCrossing);
    }

    private void FixedUpdate()
    {
        if (!moving || subscribedManager == null || !subscribedManager.isSimulating ||
            activeLocation == null || GameManager.Instance == null ||
            GameManager.Instance.ActiveBuildLocation != activeLocation ||
            !MatchesContract(assignedContract, GameManager.Instance.CurrentContract) ||
            !HasIndependentRoute)
            return;

        if (delayRemaining > 0f)
        {
            delayRemaining -= Time.fixedDeltaTime;
            return;
        }

        Vector3 previousCenter = transform.TransformPoint(activeHullCenter);
        Vector3 previousPosition = transform.position;
        Vector3 nextPosition = Vector3.MoveTowards(previousPosition, endPoint.position,
            speed * Time.fixedDeltaTime);
        transform.position = nextPosition;

        if (!useManualHull && hasBoatMeshColliders)
        {
            if (TryBreakWithMeshColliders(previousPosition, nextPosition)) return;
        }
        else if (TryBreakWithBox(previousCenter, previousPosition, nextPosition))
        {
            return;
        }

        if (nextPosition != endPoint.position) return;
        moving = false;
        SetMeshQueriesActive(false);
        finishedCrossing = true;
        SetBoatVisible(false);
    }

    private bool TryBreakWithBox(Vector3 previousCenter, Vector3 previousPosition,
        Vector3 nextPosition)
    {
        Vector3 scale = transform.lossyScale;
        Vector3 halfExtents = new Vector3(
            Mathf.Abs(activeHullSize.x * scale.x),
            Mathf.Abs(activeHullSize.y * scale.y),
            Mathf.Abs(activeHullSize.z * scale.z)) * 0.5f;
        Vector3 nextCenter = transform.TransformPoint(activeHullCenter);
        Vector3 travel = nextCenter - previousCenter;

        if (travel.sqrMagnitude > 0.000001f)
        {
            int count = Physics.BoxCastNonAlloc(previousCenter, halfExtents,
                travel.normalized, sweepHits, transform.rotation, travel.magnitude,
                bridgeCollisionLayers, QueryTriggerInteraction.Ignore);
            WarnIfQueryFilled(count, sweepHits.Length);
            int nearestBridgeHit = -1;
            float nearestDistance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                if (sweepHits[i].distance >= nearestDistance ||
                    !TryGetActiveBridgeBar(sweepHits[i].collider, out _)) continue;
                nearestBridgeHit = i;
                nearestDistance = sweepHits[i].distance;
            }
            if (nearestBridgeHit >= 0 && TryBreakHit(sweepHits[nearestBridgeHit].collider))
            {
                transform.position = Vector3.MoveTowards(previousPosition, nextPosition,
                    Mathf.Max(0f, nearestDistance));
                return true;
            }
        }

        int overlaps = Physics.OverlapBoxNonAlloc(nextCenter, halfExtents,
            overlapHits, transform.rotation, bridgeCollisionLayers,
            QueryTriggerInteraction.Ignore);
        WarnIfQueryFilled(overlaps, overlapHits.Length);
        for (int i = 0; i < overlaps; i++)
            if (TryBreakHit(overlapHits[i])) return true;

        return false;
    }

    private bool TryBreakHit(Collider hit)
    {
        if (!TryGetActiveBridgeBar(hit, out Bar bar) ||
            !subscribedManager.TryBreakMemberFromExternalImpact(bar, "boat impact"))
            return false;

        moving = false;
        SetMeshQueriesActive(false);
        return true;
    }

    private bool TryGetActiveBridgeBar(Collider hit, out Bar bar)
    {
        bar = null;
        if (hit == null || subscribedManager == null || activeLocation == null ||
            BridgePhysicsManager.DebugInvincibleBridge) return false;
        bar = hit.GetComponentInParent<Bar>();
        if (bar == null || !bar.isActiveAndEnabled || !activeLocation.Owns(bar)) return false;
        BarStressHandler member = bar.GetComponent<BarStressHandler>();
        return member != null && !member.isBroken &&
            subscribedManager.activeStressHandlers.Contains(member);
    }

    private void CacheBridgeColliders()
    {
        bridgeColliders.Clear();
        if (subscribedManager == null || activeLocation == null) return;
        foreach (BarStressHandler member in subscribedManager.activeStressHandlers)
        {
            Bar bar = member != null ? member.Bar : null;
            if (bar == null || !activeLocation.Owns(bar)) continue;
            foreach (Collider bridgeCollider in bar.GetComponentsInChildren<Collider>(true))
                if (bridgeCollider != null && bridgeCollider.enabled &&
                    bridgeCollider.gameObject.activeInHierarchy &&
                    (bridgeCollisionLayers.value & (1 << bridgeCollider.gameObject.layer)) != 0)
                    bridgeColliders.Add(bridgeCollider);
        }
    }

    private bool TryBreakWithMeshColliders(Vector3 previousPosition, Vector3 nextPosition)
    {
        Vector3 previousOffset = previousPosition - nextPosition;
        for (int i = 0; i < boatMeshColliders.Length; i++)
        {
            MeshCollider boatMesh = boatMeshColliders[i];
            if (boatMesh == null || boatMesh.sharedMesh == null) continue;
            boatMeshWorldBounds[i] = GetMeshWorldBounds(boatMesh);
            Bounds swept = boatMeshWorldBounds[i];
            swept.Encapsulate(boatMeshWorldBounds[i].min + previousOffset);
            swept.Encapsulate(boatMeshWorldBounds[i].max + previousOffset);

            foreach (Collider bridgeCollider in bridgeColliders)
            {
                if (bridgeCollider == null || !bridgeCollider.enabled ||
                    !bridgeCollider.gameObject.activeInHierarchy ||
                    !swept.Intersects(bridgeCollider.bounds)) continue;

                // Check the mesh itself at several points along this physics
                // step. The bounds above only reject distant members.
                for (int sample = 1; sample <= 4; sample++)
                {
                    float fraction = sample * 0.25f;
                    Vector3 sampleOffset = previousOffset * (1f - fraction);
                    if (!Physics.ComputePenetration(
                            bridgeCollider, bridgeCollider.transform.position,
                            bridgeCollider.transform.rotation,
                            boatMesh, boatMesh.transform.position + sampleOffset,
                            boatMesh.transform.rotation, out _, out _) ||
                        !TryBreakHit(bridgeCollider)) continue;

                    transform.position = Vector3.Lerp(previousPosition, nextPosition, fraction);
                    return true;
                }
            }
        }
        return false;
    }

    private static Bounds GetMeshWorldBounds(MeshCollider meshCollider)
    {
        Bounds local = meshCollider.sharedMesh.bounds;
        Vector3 lower = local.min;
        Vector3 upper = local.max;
        Bounds world = new Bounds(meshCollider.transform.TransformPoint(lower), Vector3.zero);
        for (int x = 0; x < 2; x++)
        for (int y = 0; y < 2; y++)
        for (int z = 0; z < 2; z++)
            world.Encapsulate(meshCollider.transform.TransformPoint(new Vector3(
                x == 0 ? lower.x : upper.x,
                y == 0 ? lower.y : upper.y,
                z == 0 ? lower.z : upper.z)));
        return world;
    }

    private void SetMeshQueriesActive(bool active)
    {
        if (boatMeshColliders == null || useManualHull) return;
        foreach (MeshCollider boatMesh in boatMeshColliders)
        {
            if (boatMesh == null || boatMesh.sharedMesh == null) continue;
            if (active || preventBoatPhysicalContacts)
                boatMesh.enabled = active;
        }
    }

    private void SetBoatVisible(bool visible)
    {
        if (boatRenderers == null) return;
        for (int i = 0; i < boatRenderers.Length; i++)
            if (boatRenderers[i] != null)
                boatRenderers[i].enabled = visible && authoredRendererStates[i];
        if (!visible) buildModeOutline?.SetBuildModeVisualOnlyVisible(false);
    }

    private void DisposeOutline()
    {
        if (buildModeOutline == null) return;
        buildModeOutline.Dispose();
        buildModeOutline = null;
    }

    private void WarnIfQueryFilled(int count, int capacity)
    {
        if (warnedFullQuery || count < capacity) return;
        warnedFullQuery = true;
        Debug.LogWarning("[BoatBridgeCrossing] Impact query buffer filled. Narrow Bridge Collision Layers so bridge members cannot be missed.", this);
    }

    private void RefreshHull()
    {
        activeHullCenter = hullCenter;
        activeHullSize = hullSize;
        if (!useManualHull && !hasBoatMeshColliders &&
            TryFitHullToRenderers(boatRenderers,
                out Vector3 fittedCenter, out Vector3 fittedSize))
        {
            activeHullCenter = fittedCenter;
            activeHullSize = fittedSize;
        }
    }

    private bool TryFitHullToRenderers(Renderer[] renderers,
        out Vector3 fittedCenter, out Vector3 fittedSize)
    {
        Vector3 minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        Vector3 maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        bool found = false;

        if (renderers != null)
        {
            foreach (Renderer boatRenderer in renderers)
            {
                if (boatRenderer == null) continue;
                Bounds bounds = boatRenderer.bounds;
                if (bounds.size.sqrMagnitude <= 0.000001f) continue;
                Vector3 lower = bounds.min;
                Vector3 upper = bounds.max;
                for (int x = 0; x < 2; x++)
                for (int y = 0; y < 2; y++)
                for (int z = 0; z < 2; z++)
                {
                    Vector3 corner = transform.InverseTransformPoint(new Vector3(
                        x == 0 ? lower.x : upper.x,
                        y == 0 ? lower.y : upper.y,
                        z == 0 ? lower.z : upper.z));
                    minimum = Vector3.Min(minimum, corner);
                    maximum = Vector3.Max(maximum, corner);
                }
                found = true;
            }
        }

        fittedCenter = found ? (minimum + maximum) * 0.5f : hullCenter;
        fittedSize = found ? maximum - minimum : hullSize;
        return found;
    }

    private void OnValidate()
    {
        speed = Mathf.Max(0.01f, speed);
        startDelay = Mathf.Max(0f, startDelay);
        hullSize = new Vector3(Mathf.Max(0.01f, hullSize.x),
            Mathf.Max(0.01f, hullSize.y), Mathf.Max(0.01f, hullSize.z));
    }

    private void OnDrawGizmosSelected()
    {
        if (startPoint != null && endPoint != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(startPoint.position, endPoint.position);
            Gizmos.DrawWireSphere(startPoint.position, 0.25f);
            Gizmos.DrawWireSphere(endPoint.position, 0.25f);
        }

        // Mesh-collider crossings do not use a box. Show the yellow wireframe
        // only when the fallback/manual box is actually used.
        if (!useManualHull && GetComponentInChildren<MeshCollider>(true) != null)
            return;

        Gizmos.color = Color.yellow;
        Matrix4x4 previous = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;
        Vector3 gizmoCenter = hullCenter;
        Vector3 gizmoSize = hullSize;
        if (!useManualHull &&
            TryFitHullToRenderers(GetComponentsInChildren<Renderer>(true),
                out Vector3 fittedCenter, out Vector3 fittedSize))
        {
            gizmoCenter = fittedCenter;
            gizmoSize = fittedSize;
        }
        Gizmos.DrawWireCube(gizmoCenter, gizmoSize);
        Gizmos.matrix = previous;
    }
}
