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
    [Tooltip("Radius around the first struck member that can also be damaged.")]
    [SerializeField, Min(0.1f)] private float impactRadius = 5f;
    [Tooltip("Maximum number of members released at one impact, including the first.")]
    [SerializeField, Range(1, 8)] private int maxBrokenMembers = 4;
    [Tooltip("Minimum boat travel between separate impacts. Keeps adjacent colliders from causing a burst every physics step.")]
    [SerializeField, Min(0f)] private float impactSpacing = 3f;
    [Tooltip("Small downward velocity given to intact members near contact so the surrounding bridge visibly yields. Set to 0 to disable.")]
    [SerializeField, Min(0f)] private float impactDeflectionSpeed = 0.45f;
    [Tooltip("Optional authored effect. When empty, a small pooled wood-impact particle effect is created once.")]
    [SerializeField] private ParticleSystem impactEffect;
    [Tooltip("URP transparent particle material for the fallback impact effect. Assign a material asset so it is included in mobile builds.")]
    [SerializeField] private Material impactMaterial;
    [Tooltip("World-space size of the fallback wood splinters.")]
    [SerializeField, Min(0.1f)] private float impactParticleSize = 0.9f;
    [Tooltip("Moves the impact burst slightly toward the build camera so the boat hull does not hide it.")]
    [SerializeField, Min(0f)] private float impactCameraOffset = 2f;

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
    private Material generatedImpactMaterial;

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
        boatMeshColliders = GetComponentsInChildren<MeshCollider>(true);
        boatMeshWorldBounds = new Bounds[boatMeshColliders.Length];
        for (int i = 0; i < boatMeshColliders.Length; i++)
            if (boatMeshColliders[i] != null && boatMeshColliders[i].sharedMesh != null)
                hasBoatMeshColliders = true;
        authoredRendererStates = new bool[boatRenderers.Length];
        for (int i = 0; i < boatRenderers.Length; i++)
            authoredRendererStates[i] = boatRenderers[i].enabled;
        RefreshHull();
        if (impactEffect == null) CreateDefaultImpactEffect();

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
        if (generatedImpactMaterial != null) Destroy(generatedImpactMaterial);
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
        if (impactEffect != null) impactEffect.Clear(true);
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
        if (impactEffect != null) impactEffect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        transform.SetPositionAndRotation(authoredPosition, authoredRotation);
        // Hide only after a crossing has finished during simulation. Returning
        // to build mode restores the authored boat and its selection outline.
        finishedCrossing = false;
        SetBoatVisible(true);
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

        if (distanceTraveled - lastImpactDistance >= impactSpacing)
        {
            if (!useManualHull && hasBoatMeshColliders)
                TryBreakWithMeshColliders(previousPosition, nextPosition);
            else
                TryBreakWithBox(previousCenter);
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
        CollectImpactArea(bar, impactPoint);
        if (subscribedManager.BreakMembersFromExternalImpact(impactedBars,
                "boat impact") == 0) return false;

        ApplyLocalImpactDeflection(impactPoint);
        lastImpactDistance = distanceTraveled;
        PlayImpactEffect(impactPoint);
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
                        !TryBreakHit(bridgeCollider,
                            boatMeshWorldBounds[i].center + sampleOffset)) continue;

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

    private void CreateDefaultImpactEffect()
    {
        // One reusable system per boat; impacts emit into world space so
        // earlier splinters do not follow the moving hull.
        GameObject effectObject = new GameObject("Boat Impact Effect");
        effectObject.transform.SetParent(transform, false);
        effectObject.layer = gameObject.layer;
        impactEffect = effectObject.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = impactEffect.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Local;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 1.1f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3f);
        main.startSize = new ParticleSystem.MinMaxCurve(
            impactParticleSize * 0.6f, impactParticleSize);
        main.startColor = new Color(1f, 0.75f, 0.43f, 1f);
        main.gravityModifier = 0.75f;
        main.maxParticles = 64;

        ParticleSystem.EmissionModule emission = impactEffect.emission;
        emission.rateOverTime = 0f;
        emission.rateOverDistance = 0f;
        ParticleSystem.ShapeModule shape = impactEffect.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.2f;

        ParticleSystemRenderer particleRenderer = effectObject.GetComponent<ParticleSystemRenderer>();
        if (impactMaterial != null)
            particleRenderer.sharedMaterial = impactMaterial;
        else
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader != null)
            {
                generatedImpactMaterial = new Material(shader);
                generatedImpactMaterial.SetFloat("_Surface", 1f);
                generatedImpactMaterial.SetFloat("_SrcBlend", 5f);
                generatedImpactMaterial.SetFloat("_DstBlend", 10f);
                generatedImpactMaterial.SetFloat("_ZWrite", 0f);
                generatedImpactMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                generatedImpactMaterial.SetOverrideTag("RenderType", "Transparent");
                generatedImpactMaterial.renderQueue = 3000;
                particleRenderer.sharedMaterial = generatedImpactMaterial;
            }
        }
        impactEffect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void PlayImpactEffect(Vector3 contactPoint)
    {
        if (impactEffect == null) return;
        Camera buildCamera = activeLocation != null ? activeLocation.locationCamera : null;
        impactEffect.transform.position = buildCamera != null
            ? contactPoint - buildCamera.transform.forward * impactCameraOffset
            : contactPoint;
        if (!impactEffect.isPlaying) impactEffect.Play();
        impactEffect.Emit(16);
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
        travelDistance = Mathf.Max(0.01f, travelDistance);
        impactRadius = Mathf.Max(0.1f, impactRadius);
        maxBrokenMembers = Mathf.Clamp(maxBrokenMembers, 1, 8);
        impactSpacing = Mathf.Max(0f, impactSpacing);
        impactDeflectionSpeed = Mathf.Max(0f, impactDeflectionSpeed);
        impactParticleSize = Mathf.Max(0.1f, impactParticleSize);
        impactCameraOffset = Mathf.Max(0f, impactCameraOffset);
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
