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
    [Header("Contract and Travel")]
    public ContractSO assignedContract;
    [Tooltip("World-space movement direction. The placed boat transform is the starting pose.")]
    [SerializeField] private Vector3 travelDirection = Vector3.back;
    [Tooltip("Distance traveled before the boat is hidden. No endpoint object is needed.")]
    [SerializeField, Min(0.01f)] private float travelDistance = 100f;
    [SerializeField, Min(0.01f)] private float speed = 3f;
    [SerializeField, Min(0f)] private float startDelay;
    [Tooltip("Optional. Leave empty to find the scene's bridge simulation manager.")]
    [SerializeField] private BridgePhysicsManager physicsManager;

    [Header("Bridge Impact")]
    [Tooltip("Use the manual box instead of the visible hull mesh. Leave off for mesh-shaped impacts.")]
    [SerializeField] private bool useManualHull;
    [Tooltip("Optional: assign the hull Mesh Filter for exact impacts. When empty, the largest visible hull/body mesh is chosen automatically.")]
    [SerializeField] private MeshFilter impactHullMesh;
    [Tooltip("Boat-local impact box center when Use Manual Hull is enabled.")]
    [SerializeField] private Vector3 hullCenter;
    [SerializeField] private Vector3 hullSize = new Vector3(2f, 1f, 4f);
    [Tooltip("Must include the layers used by simulated bridge bars.")]
    [SerializeField] private LayerMask bridgeCollisionLayers = ~0;
    [Tooltip("Prevent the boat from physically pushing other objects. Mesh Colliders stay enabled during crossing so exact impact checks work.")]
    [FormerlySerializedAs("disableBoatPhysicalColliders")]
    [SerializeField] private bool preventBoatPhysicalContacts = true;
    [Tooltip("Radius around the first struck member that can also be damaged.")]
    [SerializeField, Min(0.1f)] private float impactRadius = 5f;
    [Tooltip("Maximum number of members released at one impact, including the first.")]
    [SerializeField, Range(1, 8)] private int maxBrokenMembers = 4;
    [Tooltip("Minimum boat travel between separate impacts. Keeps adjacent colliders from causing a burst every physics step.")]
    [SerializeField, Min(0f)] private float impactSpacing = 3f;
    [Tooltip("Small downward velocity given to intact members near contact so the surrounding bridge visibly yields. Set to 0 to disable.")]
    [SerializeField, Min(0f)] private float impactDeflectionSpeed = 0.45f;
    [Tooltip("Sideways velocity applied to a released pier so it topples instead of remaining balanced upright after the boat breaks its joints.")]
    [SerializeField, Min(0.1f)] private float pierToppleSpeed = 1.5f;

    private readonly RaycastHit[] sweepHits = new RaycastHit[64];
    private readonly Collider[] overlapHits = new Collider[64];
    private readonly List<Collider> bridgeColliders = new List<Collider>(128);
    private readonly List<Bar> impactedBars = new List<Bar>(8);
    private readonly List<float> impactedDistances = new List<float>(8);
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
    private Vector3 authoredPosition;
    private Quaternion authoredRotation;
    private Vector3 crossingDirection;
    private float distanceTraveled;
    private float lastImpactDistance = float.NegativeInfinity;

    private bool HasTravelDirection => travelDirection.sqrMagnitude > 0.000001f;

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
        authoredPosition = transform.position;
        authoredRotation = transform.rotation;
        // This FBX has no authored collider. A renderer-wide box includes empty
        // space around the curved bow and can damage a bridge too early.
        MeshFilter hull = FindImpactHullMesh();
        MeshCollider hullCollider = !useManualHull && hull != null
            ? GetOrCreateHullCollider(hull)
            : null;
#if UNITY_EDITOR
        if (hullCollider != null)
            Debug.Log($"[BoatBridgeCrossing] Using visible hull mesh '{hull.sharedMesh.name}' on '{hull.name}' for impact checks.", this);
#endif
        boatMeshColliders = hullCollider != null
            ? new[] { hullCollider }
            : System.Array.Empty<MeshCollider>();
        boatMeshWorldBounds = new Bounds[boatMeshColliders.Length];
        for (int i = 0; i < boatMeshColliders.Length; i++)
            if (boatMeshColliders[i] != null && boatMeshColliders[i].sharedMesh != null)
                hasBoatMeshColliders = true;
        authoredRendererStates = new bool[boatRenderers.Length];
        for (int i = 0; i < boatRenderers.Length; i++)
            authoredRendererStates[i] = boatRenderers[i].enabled;
        RefreshHull();
        if (!useManualHull && !hasBoatMeshColliders)
            Debug.LogError("[BoatBridgeCrossing] No usable hull mesh was found. Impact checks are disabled to avoid false box collisions. Assign Impact Hull Mesh or enable Use Manual Hull and size its box explicitly.", this);
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
        LevelCompleteManager.SimulationSucceeded += HandleSimulationSucceeded;
    }

    private void Start()
    {
        // Also catches a manager instantiated after this boat's OnEnable.
        BindSimulationManager();
        RefreshHull();
    }

    private void OnDisable()
    {
        LevelCompleteManager.SimulationSucceeded -= HandleSimulationSucceeded;
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
        bool showOutline = !finishedCrossing && !moving && HasTravelDirection &&
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

        if (!HasTravelDirection)
        {
            Debug.LogWarning("[BoatBridgeCrossing] Set a nonzero Travel Direction on the boat.", this);
            return;
        }

        activeLocation = GameManager.Instance.ActiveBuildLocation;
        CacheBridgeColliders();
        finishedCrossing = false;
        SetBoatVisible(true);
        transform.SetPositionAndRotation(authoredPosition, authoredRotation);
        crossingDirection = travelDirection.normalized;
        distanceTraveled = 0f;
        lastImpactDistance = float.NegativeInfinity;
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
        transform.SetPositionAndRotation(authoredPosition, authoredRotation);
        // Hide only after a crossing has finished during simulation. Returning
        // to build mode restores the authored boat and its selection outline.
        finishedCrossing = false;
        SetBoatVisible(true);
    }

    private void HandleSimulationSucceeded(ContractSO completedContract, BuildLocation completedLocation)
    {
        if (!moving || !MatchesContract(assignedContract, completedContract) ||
            (completedLocation != null && completedLocation != activeLocation)) return;

        // The vehicle reaching its finish line completes the test. The boat is
        // an obstacle, not another goal, so stop its impact checks immediately
        // without hiding it or waiting for its full travel distance.
        moving = false;
        SetMeshQueriesActive(false);
    }

    private void FixedUpdate()
    {
        if (!moving || subscribedManager == null || !subscribedManager.isSimulating ||
            activeLocation == null || GameManager.Instance == null ||
            GameManager.Instance.ActiveBuildLocation != activeLocation ||
            !MatchesContract(assignedContract, GameManager.Instance.CurrentContract) ||
            !HasTravelDirection)
            return;

        if (delayRemaining > 0f)
        {
            delayRemaining -= Time.fixedDeltaTime;
            return;
        }

        Vector3 previousCenter = transform.TransformPoint(activeHullCenter);
        Vector3 previousPosition = transform.position;
        float step = Mathf.Min(speed * Time.fixedDeltaTime,
            Mathf.Max(0f, travelDistance - distanceTraveled));
        Vector3 nextPosition = previousPosition + crossingDirection * step;
        transform.position = nextPosition;

        bool impactSpacingElapsed =
            distanceTraveled - lastImpactDistance >= impactSpacing;
        if (impactSpacingElapsed)
        {
            if (useManualHull)
                TryBreakWithBox(previousCenter);
            else if (hasBoatMeshColliders)
                TryBreakWithMeshColliders(previousPosition, nextPosition);
        }
        else if (!useManualHull && hasBoatMeshColliders)
        {
            // Do not let an earlier beam/road impact make the boat phase through
            // a pier encountered during the spacing cooldown. Pier checks are
            // cheap and an already-broken pier is rejected by the normal filter.
            TryBreakWithMeshColliders(previousPosition, nextPosition, true);
        }

        distanceTraveled += step;
        if (distanceTraveled < travelDistance) return;
        moving = false;
        SetMeshQueriesActive(false);
        finishedCrossing = true;
        SetBoatVisible(false);
    }

    private bool TryBreakWithBox(Vector3 previousCenter)
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
            if (nearestBridgeHit >= 0 && TryBreakHit(sweepHits[nearestBridgeHit].collider,
                    nextCenter))
            {
                return true;
            }
        }

        int overlaps = Physics.OverlapBoxNonAlloc(nextCenter, halfExtents,
            overlapHits, transform.rotation, bridgeCollisionLayers,
            QueryTriggerInteraction.Ignore);
        WarnIfQueryFilled(overlaps, overlapHits.Length);
        for (int i = 0; i < overlaps; i++)
            if (TryBreakHit(overlapHits[i], nextCenter)) return true;

        return false;
    }

    private bool TryBreakHit(Collider hit, Vector3 boatContactCenter)
    {
        if (!TryGetActiveBridgeBar(hit, out Bar bar))
            return false;

        Vector3 impactPoint = hit.ClosestPoint(boatContactCenter);
        return TryBreakBarAtPoint(bar, impactPoint);
    }

    private bool TryBreakBarAtPoint(Bar bar, Vector3 impactPoint)
    {
        if (bar == null) return false;
        CollectImpactArea(bar, impactPoint);
        if (subscribedManager.BreakMembersFromExternalImpact(impactedBars,
                "boat impact") == 0) return false;

        ApplyBrokenMemberImpactMotion(impactPoint);
        ApplyLocalImpactDeflection(impactPoint);
        lastImpactDistance = distanceTraveled;
        return true;
    }

    private void CollectImpactArea(Bar struckBar, Vector3 impactPoint)
    {
        impactedBars.Clear();
        impactedDistances.Clear();
        impactedBars.Add(struckBar);
        impactedDistances.Add(0f);
        float radiusSquared = impactRadius * impactRadius;

        foreach (BarStressHandler member in subscribedManager.activeStressHandlers)
        {
            Bar candidate = member != null ? member.Bar : null;
            if (candidate == null || candidate == struckBar || member.isBroken ||
                !activeLocation.Owns(candidate) || candidate.startPoint == null ||
                candidate.endPoint == null) continue;

            float distanceSquared = DistanceSquaredToBar(candidate, impactPoint);
            if (distanceSquared > radiusSquared) continue;

            int index = 1;
            while (index < impactedDistances.Count &&
                   impactedDistances[index] <= distanceSquared) index++;
            if (index >= maxBrokenMembers) continue;
            impactedBars.Insert(index, candidate);
            impactedDistances.Insert(index, distanceSquared);
            if (impactedBars.Count <= maxBrokenMembers) continue;
            impactedBars.RemoveAt(maxBrokenMembers);
            impactedDistances.RemoveAt(maxBrokenMembers);
        }
    }

    /// <summary>
    /// The normal break path releases every joint, but a vertical pier can remain
    /// balanced on its foundation when gravity is its only post-break force. Give
    /// newly released members the motion of the impact; piers receive an in-plane
    /// lateral impulse so the 2D bridge constraints still allow them to topple.
    /// </summary>
    private void ApplyBrokenMemberImpactMotion(Vector3 impactPoint)
    {
        for (int i = 0; i < impactedBars.Count; i++)
        {
            Bar bar = impactedBars[i];
            if (bar == null || bar.materialData == null) continue;
            BarStressHandler member = bar.GetComponent<BarStressHandler>();
            if (member == null || !member.isBroken) continue;

            Rigidbody body = bar.GetComponent<Rigidbody>();
            if (body == null || body.isKinematic) continue;

            if (bar.materialData.isPier)
            {
                Vector3 axis = bar.endPoint != null && bar.startPoint != null
                    ? bar.endPoint.transform.position - bar.startPoint.transform.position
                    : Vector3.up;
                axis.z = 0f;
                Vector3 axisDirection = axis.sqrMagnitude > 0.000001f
                    ? axis.normalized : Vector3.up;
                Vector3 lateral = Vector3.Cross(Vector3.forward, axisDirection);
                if (lateral.sqrMagnitude <= 0.000001f) lateral = Vector3.left;

                // AddForceAtPosition produces both the shove and the rotation a
                // monolithic pier needs to visibly fall away from its foundation.
                body.AddForceAtPosition(lateral.normalized * body.mass * pierToppleSpeed,
                    impactPoint, ForceMode.Impulse);
                Vector3 rotationAxis = Vector3.Cross(axisDirection, lateral.normalized);
                float torqueLever = Mathf.Max(0.5f, axis.magnitude * 0.25f);
                body.AddTorque(rotationAxis * body.mass * pierToppleSpeed * torqueLever,
                    ForceMode.Impulse);
            }
            else if (impactDeflectionSpeed > 0f)
            {
                body.AddForce(Vector3.down * body.mass * impactDeflectionSpeed,
                    ForceMode.Impulse);
            }

            body.WakeUp();
        }
    }

    private void ApplyLocalImpactDeflection(Vector3 impactPoint)
    {
        if (impactDeflectionSpeed <= 0f) return;
        float radiusSquared = impactRadius * impactRadius;
        foreach (BarStressHandler member in subscribedManager.activeStressHandlers)
        {
            Bar bar = member != null ? member.Bar : null;
            if (bar == null || member.isBroken || !activeLocation.Owns(bar) ||
                bar.startPoint == null || bar.endPoint == null) continue;

            float distanceSquared = DistanceSquaredToBar(bar, impactPoint);
            if (distanceSquared > radiusSquared) continue;
            Rigidbody body = bar.GetComponent<Rigidbody>();
            if (body == null || body.isKinematic) continue;

            float falloff = 1f - 0.5f * Mathf.Sqrt(distanceSquared) / impactRadius;
            float desiredDownSpeed = impactDeflectionSpeed * falloff;
            float additionalSpeed = Mathf.Min(desiredDownSpeed,
                Mathf.Max(0f, desiredDownSpeed + body.velocity.y));
            if (additionalSpeed <= 0f) continue;
            body.AddForce(Vector3.down * body.mass * additionalSpeed, ForceMode.Impulse);
            body.WakeUp();
        }
    }

    private static float DistanceSquaredToBar(Bar bar, Vector3 position)
    {
        Vector3 start = bar.startPoint.transform.position;
        Vector3 segment = bar.endPoint.transform.position - start;
        float lengthSquared = segment.sqrMagnitude;
        float fraction = lengthSquared > 0.000001f
            ? Mathf.Clamp01(Vector3.Dot(position - start, segment) / lengthSquared)
            : 0f;
        return (position - (start + segment * fraction)).sqrMagnitude;
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

    private bool TryBreakWithMeshColliders(
        Vector3 previousPosition,
        Vector3 nextPosition,
        bool piersOnly = false)
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

            // The imported hull is open and non-convex. A pier can visibly pass
            // through its interior without ComputePenetration reporting a surface
            // overlap. Test the solid swept hull volume directly against every
            // active pier's structural centerline before relying on colliders.
            if (TryBreakPierThroughHullVolume(boatMeshWorldBounds[i], previousOffset))
                return true;

            // Piers are checked first. At a busy bridge cross-section the hull
            // can overlap a road, braces, and a pier in the same fixed step; the
            // former first-hit return could consume the cooldown before the pier
            // was ever considered.
            int passCount = piersOnly ? 1 : 2;
            for (int pass = 0; pass < passCount; pass++)
            {
                bool requirePier = pass == 0;
                foreach (Collider bridgeCollider in bridgeColliders)
                {
                    if (bridgeCollider == null || !bridgeCollider.enabled ||
                        !bridgeCollider.gameObject.activeInHierarchy ||
                        !swept.Intersects(bridgeCollider.bounds) ||
                        !TryGetActiveBridgeBar(bridgeCollider, out Bar candidate)) continue;

                    bool isPier = candidate.materialData != null &&
                        candidate.materialData.isPier;
                    if (isPier != requirePier) continue;

                    // Check the mesh itself at several points along this physics
                    // step. The bounds above only reject distant members.
                    for (int sample = 1; sample <= 4; sample++)
                    {
                        float fraction = sample * 0.25f;
                        Vector3 sampleOffset = previousOffset * (1f - fraction);
                        Bounds sampledHullBounds = boatMeshWorldBounds[i];
                        sampledHullBounds.center += sampleOffset;

                        bool exactContact = Physics.ComputePenetration(
                            bridgeCollider, bridgeCollider.transform.position,
                            bridgeCollider.transform.rotation,
                            boatMesh, boatMesh.transform.position + sampleOffset,
                            boatMesh.transform.rotation, out _, out _);

                        // ComputePenetration is unreliable for the imported
                        // non-convex boat hull against a thin runtime pier box.
                        // For piers only, use the already-tight hull-mesh bounds
                        // as a conservative fallback so a visible strike cannot
                        // silently pass through the foundation.
                        bool pierBoundsContact = isPier &&
                            sampledHullBounds.Intersects(bridgeCollider.bounds);
                        if ((!exactContact && !pierBoundsContact) ||
                            !TryBreakHit(bridgeCollider, sampledHullBounds.center)) continue;

                        return true;
                    }
                }
            }
        }
        return false;
    }

    private bool TryBreakPierThroughHullVolume(
        Bounds currentHullBounds,
        Vector3 previousOffset)
    {
        for (int sample = 1; sample <= 4; sample++)
        {
            float fraction = sample * 0.25f;
            Bounds sampledHullBounds = currentHullBounds;
            sampledHullBounds.center += previousOffset * (1f - fraction);

            foreach (BarStressHandler member in subscribedManager.activeStressHandlers)
            {
                Bar pier = member != null ? member.Bar : null;
                if (pier == null || member.isBroken || pier.materialData == null ||
                    !pier.materialData.isPier || !activeLocation.Owns(pier) ||
                    pier.startPoint == null || pier.endPoint == null) continue;

                if (!TryGetSegmentBoundsContact(
                        pier.startPoint.transform.position,
                        pier.endPoint.transform.position,
                        sampledHullBounds,
                        out Vector3 impactPoint)) continue;

                if (TryBreakBarAtPoint(pier, impactPoint)) return true;
            }
        }

        return false;
    }

    private static bool TryGetSegmentBoundsContact(
        Vector3 start,
        Vector3 end,
        Bounds bounds,
        out Vector3 contactPoint)
    {
        contactPoint = start;
        if (bounds.Contains(start)) return true;
        if (bounds.Contains(end))
        {
            contactPoint = end;
            return true;
        }

        Vector3 segment = end - start;
        float length = segment.magnitude;
        if (length <= 0.000001f) return false;

        Ray ray = new Ray(start, segment / length);
        if (!bounds.IntersectRay(ray, out float distance) || distance > length)
            return false;

        contactPoint = ray.GetPoint(distance);
        return true;
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

    private MeshFilter FindImpactHullMesh()
    {
        if (impactHullMesh != null && impactHullMesh.transform.IsChildOf(transform) &&
            impactHullMesh.sharedMesh != null)
            return impactHullMesh;

        MeshFilter best = null;
        int bestPriority = -1;
        float bestVolume = 0f;
        foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter == null || filter.sharedMesh == null ||
                !filter.gameObject.activeInHierarchy ||
                !filter.TryGetComponent(out MeshRenderer renderer) || !renderer.enabled)
                continue;

            string objectName = filter.name;
            string meshName = filter.sharedMesh.name;
            string materialName = renderer.sharedMaterial != null
                ? renderer.sharedMaterial.name : string.Empty;
            int priority = ContainsHullWord(objectName, meshName, "hull") ||
                materialName.IndexOf("hull", System.StringComparison.OrdinalIgnoreCase) >= 0 ? 3 :
                ContainsHullWord(objectName, meshName, "body") ? 2 :
                ContainsHullWord(objectName, meshName, "boat") ? 1 : 0;
            Vector3 size = renderer.bounds.size;
            float volume = size.x * size.y * size.z;
            if (priority < bestPriority || (priority == bestPriority && volume <= bestVolume))
                continue;
            best = filter;
            bestPriority = priority;
            bestVolume = volume;
        }
        return best;
    }

    private static bool ContainsHullWord(string objectName, string meshName, string word)
    {
        return objectName.IndexOf(word, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            meshName.IndexOf(word, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static MeshCollider GetOrCreateHullCollider(MeshFilter hull)
    {
        MeshCollider collider = hull.GetComponent<MeshCollider>();
        if (collider == null) collider = hull.gameObject.AddComponent<MeshCollider>();
        collider.sharedMesh = hull.sharedMesh;
        collider.convex = false;
        return collider;
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
    }

    private void OnValidate()
    {
        speed = Mathf.Max(0.01f, speed);
        startDelay = Mathf.Max(0f, startDelay);
        travelDistance = Mathf.Max(0.01f, travelDistance);
        impactRadius = Mathf.Max(0.1f, impactRadius);
        maxBrokenMembers = Mathf.Clamp(maxBrokenMembers, 1, 8);
        impactSpacing = Mathf.Max(0f, impactSpacing);
        impactDeflectionSpeed = Mathf.Max(0f, impactDeflectionSpeed);
        pierToppleSpeed = Mathf.Max(0.1f, pierToppleSpeed);
        hullSize = new Vector3(Mathf.Max(0.01f, hullSize.x),
            Mathf.Max(0.01f, hullSize.y), Mathf.Max(0.01f, hullSize.z));
    }

    private void OnDrawGizmosSelected()
    {
        if (HasTravelDirection)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(transform.position,
                transform.position + travelDirection.normalized * travelDistance);
            Gizmos.DrawWireSphere(transform.position, 0.25f);
        }

        if (!useManualHull)
        {
            MeshFilter hull = FindImpactHullMesh();
            if (hull != null)
            {
                Gizmos.color = Color.yellow;
                Matrix4x4 hullMatrix = Gizmos.matrix;
                Gizmos.matrix = hull.transform.localToWorldMatrix;
                Gizmos.DrawWireMesh(hull.sharedMesh);
                Gizmos.matrix = hullMatrix;
            }
            return;
        }

        Gizmos.color = Color.yellow;
        Matrix4x4 previous = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(hullCenter, hullSize);
        Gizmos.matrix = previous;
    }
}
