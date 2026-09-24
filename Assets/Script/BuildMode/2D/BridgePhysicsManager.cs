using System; 
using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;

[DefaultExecutionOrder(-50)] 
public class BridgePhysicsManager : MonoBehaviour
{
    private static readonly ProfilerMarker PhysicsGraphMarker =
        new ProfilerMarker("CivilCraft.Simulation.PhysicsGraph");
    private static readonly ProfilerMarker CollisionSetupMarker =
        new ProfilerMarker("CivilCraft.Simulation.CollisionSetup");
    private static readonly ProfilerMarker StressAnalysisMarker =
        new ProfilerMarker("CivilCraft.Simulation.StressAnalysis");

    /// <summary>Development-menu override. Normal gameplay must leave this false.</summary>
    public static bool DebugInvincibleBridge { get; set; }

    public event Action OnSettlePhaseStarted;
    public event Action OnSimulationStarted;
    public event Action OnSimulationStopped;
    public event Action<BarStressHandler> OnFirstMemberBroken;

    [Header("Physics Settings")]
    public float barColliderThickness = 0.2f;
    public int physicsSolverIterations = 40; 
    [Min(1)] public int physicsSolverVelocityIterations = 20;
    public int settleFramesAmount = 60;
    [Tooltip("On mobile, limits long physics catch-up bursts during a bridge test. It does not change the fixed timestep or contract stress calculation.")]
    [Min(0.02f)] [SerializeField] private float mobileMaximumSimulationDeltaTime = 0.08f;

    [Header("Road Collision")]
    [Tooltip("Physical thickness shared by simulated and finalized roads. This keeps wheel support continuous while the bridge flexes.")]
    [Min(0.02f)] [SerializeField] private float bakedRoadColliderThickness = 0.12f;
    [Tooltip("Permanent physical width of a saved road. Keep this wider than the player's CharacterController diameter.")]
    [Min(0.1f)] [SerializeField] private float bakedRoadColliderWidth = 2.4f;
    [Tooltip("Total overlap between adjacent road colliders. Without overlap, flexing road joints expose a lip that can stop or flip live-load vehicles.")]
    [Min(0f)] [SerializeField] private float bakedRoadColliderSeamOverlap = 0.2f;
    [Tooltip("Overlap used while the bridge is flexing under a live load. This should exceed the distance a fast wheel travels in one physics step.")]
    [Min(0f)] [SerializeField] private float simulatedRoadColliderSeamOverlap = 0.6f;
    [Tooltip("Local height of the road's visible top surface before the permanent collider is thickened.")]
    [SerializeField] private float bakedRoadVisualTop = 0.025f;

    [Header("Visual Bridge Motion")]
    [Tooltip("Keep simulated bridge members in the X/Y construction plane without changing the deterministic stress formula or thresholds.")]
    [SerializeField] private bool constrainVisualBridgeToPlane = true;
    [Tooltip("Linear damping for moving bridge bars and nodes. Higher values calm visual oscillation.")]
    [Min(0f)] [SerializeField] private float visualLinearDrag = 0.5f;
    [Tooltip("Angular damping for moving bridge bars and nodes. Higher values reduce violent spinning after a member breaks.")]
    [Min(0f)] [SerializeField] private float visualAngularDrag = 1.5f;

    [Header("Stress Sampling")]
    [Tooltip("Number of fixed-physics samples used by the current-stress display. This is a rolling average, never a stored maximum.")]
    [Min(1)] public int stressSmoothingFrames = 10;
    [Tooltip("Ignores tiny endpoint-length changes when deciding whether a bar is in tension or compression.")]
    [Min(0f)] public float stressDirectionDeadZone = 0.0005f;
    [Tooltip("Legacy compatibility setting. Runtime tests now show total dead plus live load so the visible stress matches structural failure calculations.")]
    public bool displayLiveLoadStressOnly = false;
    [Tooltip("Snaps force readings back to their settled dead-load value inside this relative tolerance, removing PhysX resting jitter.")]
    [Range(0f, 0.25f)] public float deadLoadReturnTolerance = 0.02f;
    [Tooltip("Minimum force tolerance, in Newtons, used when returning to the settled dead-load value.")]
    [Min(0f)] public float deadLoadReturnToleranceNewtons = 1f;

    [Header("Deterministic Stress Analysis")]
    [Tooltip("Uses a quantized quasi-static truss solver for stress display, scoring, and failure. PhysX remains visual only.")]
    public bool useDeterministicStressAnalysis = true;
    [Tooltip("Fixed vehicle positions evaluated across the road. More samples improve peak accuracy without changing repeatability.")]
    [Range(11, 201)] public int deterministicLoadSamples = 101;

    [Header("Stress Visualizer Colors")]
    public bool enableVisualizer = true;
    public Color warningColor = Color.yellow;
    public Color criticalColor = Color.red;
    public Color brokenColor = Color.black;

    [HideInInspector] public bool isSimulating = false;
    [HideInInspector] public bool lockStressTracking = false; 
    public bool IsSimulationActive => isSimulating || pendingSimulationStart;
    
    [HideInInspector] public List<BarStressHandler> activeStressHandlers = new List<BarStressHandler>();
    [Tooltip("Peak of the same dead-load-adjusted value shown by the stress visualizer. Used by the result screen.")]
    [HideInInspector] public float peakDisplayedStressThisRun = 0f;
    [Tooltip("Peak total structural stress, including dead load. Used for failure and contract limits.")]
    [HideInInspector] public float peakStressThisRun = 0f;
    public bool HadBrokenPartsThisRun { get; private set; }
    public bool HasLiveLoadEngagedThisRun { get; private set; }
    public string FirstMemberFailureDescription { get; private set; }
    public bool FirstMemberFailedUnderDeadLoad { get; private set; }
    /// <summary>
    /// Optional contract limits below material capacity wait for the truck to
    /// reach the bridge. Actual 100% member failure is never delayed.
    /// </summary>
    public bool IsContractStressLimitArmed
    {
        get
        {
            ContractSO contract = GameManager.Instance != null
                ? GameManager.Instance.CurrentContract
                : null;
            return contract == null || contract.liveLoadMode != ContractSO.LiveLoadMode.Vehicle ||
                   HasLiveLoadEngagedThisRun;
        }
    }
    public bool IsDeterministicStructureStable =>
        !useDeterministicStressAnalysis ||
        (deterministicAnalysisPrepared && deterministicStructureStable);
    public void RecordBrokenPart(BarStressHandler brokenMember)
    {
        if (HadBrokenPartsThisRun) return;
        HadBrokenPartsThisRun = true;
        FirstMemberFailedUnderDeadLoad = !HasLiveLoadEngagedThisRun;
        FirstMemberFailureDescription = brokenMember != null
            ? brokenMember.FailureDescription
            : "A structural member exceeded its capacity.";
        OnFirstMemberBroken?.Invoke(brokenMember);
    }

    /// <summary>
    /// Guarantees that a structural failure reported by the level rules has a
    /// visible physical consequence. This deliberately reuses BarStressHandler's
    /// normal break path instead of maintaining a second destruction system.
    /// </summary>
    public bool EnsureVisibleStructuralFailure(string cause)
    {
        if (!IsSimulationActive || BridgePhysicsManager.DebugInvincibleBridge)
            return false;

        BarStressHandler mostStressed = null;
        float highestStress = float.NegativeInfinity;
        foreach (BarStressHandler handler in activeStressHandlers)
        {
            if (handler == null) continue;
            if (handler.isBroken)
            {
                handler.WakeFailureBodies();
                return true;
            }

            float stress = handler.currentStructuralStressPercent;
            if (mostStressed == null || stress > highestStress)
            {
                mostStressed = handler;
                highestStress = stress;
            }
        }

        if (mostStressed == null) return false;
        string failureCause = string.IsNullOrWhiteSpace(cause)
            ? "Structural capacity exceeded"
            : cause;
        return mostStressed.ForceBreakForFailure(failureCause);
    }

    /// <summary>
    /// Keep the vehicle's wheels and chassis in contact with road bars, but not
    /// the side trusses. Those non-road bars transmit load through the bridge
    /// joints and are still evaluated for stress; their colliders should not
    /// trap a truck wider than the road's physical lane.
    /// </summary>
    public void IgnoreVehicleContactsWithNonRoadMembers(IEnumerable<Collider> vehicleColliders)
    {
        if (vehicleColliders == null) return;

        foreach (Bar bar in deterministicBars)
        {
            if (bar == null || !bar.gameObject.activeInHierarchy ||
                bar.materialData == null || bar.materialData.isRoad)
                continue;

            foreach (Collider memberCollider in bar.GetComponentsInChildren<Collider>())
            {
                if (memberCollider == null || !memberCollider.enabled || memberCollider.isTrigger)
                    continue;

                foreach (Collider vehicleCollider in vehicleColliders)
                {
                    if (vehicleCollider == null || !vehicleCollider.enabled ||
                        vehicleCollider.isTrigger)
                        continue;
                    Physics.IgnoreCollision(vehicleCollider, memberCollider, true);
                }
            }
        }
    }

    private HashSet<Point> simPoints = new HashSet<Point>();
    private HashSet<Bar> simBars = new HashSet<Bar>();

    private List<Point> deterministicPoints = new List<Point>();
    private List<Bar> deterministicBars = new List<Bar>();

    private bool pendingSimulationStart = false;
    private bool needsPhysicsRelease = false; 
    private int currentSettleFrame = 0;

    private PhysicMaterial sharedRoadPhysicsMat;
    private bool deterministicPhysicsOverridesApplied;
    private bool previousAutoSyncTransforms;
    private float previousMaximumDeltaTime;
    private DeterministicBridgeStressSolver.Result deterministicStressResult;
    private LiveLoadVehicle deterministicLiveLoadVehicle;
    private float deterministicRoadMinX;
    private float deterministicRoadMaxX;
    private bool deterministicAnalysisPrepared;
    private bool deterministicStructureStable;

    /// <summary>
    /// Reports where the active contract vehicle really is relative to the
    /// authored road, rather than estimating bridge entry from route time.
    /// loadFactor is zero off the bridge and rises as its wheel envelope enters.
    /// </summary>
    public bool TryGetCurrentVehicleRoadState(out float roadProgress, out float loadFactor)
    {
        roadProgress = 0f;
        loadFactor = 0f;

        ContractSO contract = GameManager.Instance != null
            ? GameManager.Instance.CurrentContract
            : null;
        LiveLoadVehicle vehicle = deterministicLiveLoadVehicle != null
            ? deterministicLiveLoadVehicle
            : LiveLoadVehicle.FindActiveForContract(contract);
        if (vehicle == null || !TryResolveRoadSpan()) return false;

        float vehicleMinX;
        float vehicleMaxX;
        bool hasPhysicalBounds = vehicle.TryGetPhysicalBounds(out Bounds vehicleBounds);
        if (hasPhysicalBounds)
        {
            vehicleMinX = vehicleBounds.min.x;
            vehicleMaxX = vehicleBounds.max.x;
        }
        else
        {
            vehicleMinX = vehicle.transform.position.x;
            vehicleMaxX = vehicleMinX;
        }

        float overlapMin = Mathf.Max(vehicleMinX, deterministicRoadMinX);
        float overlapMax = Mathf.Min(vehicleMaxX, deterministicRoadMaxX);
        float overlap = Mathf.Max(0f, overlapMax - overlapMin);
        float vehicleLength = vehicleMaxX - vehicleMinX;

        if (vehicleLength > 0.01f)
            loadFactor = Mathf.Clamp01(overlap / vehicleLength);
        else
            loadFactor = vehicleMinX >= deterministicRoadMinX && vehicleMinX <= deterministicRoadMaxX
                ? 1f
                : 0f;

        float supportedLoadX = overlap > 0f
            ? (overlapMin + overlapMax) * 0.5f
            : (vehicleMinX + vehicleMaxX) * 0.5f;

        // Horizontal overlap alone is not proof that the live load is on the
        // bridge. A vehicle falling through the ravine still shares the same X
        // coordinate. Require its wheel envelope to remain close to an actual
        // road segment before applying live load or showing traversal lessons.
        if (loadFactor > 0f && hasPhysicalBounds &&
            TryGetRoadSurfaceHeight(supportedLoadX, vehicleBounds.min.y, out float roadSurfaceY))
        {
            float verticalTolerance = Mathf.Max(0.75f, vehicleBounds.size.y * 1.5f);
            if (Mathf.Abs(vehicleBounds.min.y - roadSurfaceY) > verticalTolerance)
                loadFactor = 0f;
        }
        else if (loadFactor > 0f && hasPhysicalBounds)
        {
            loadFactor = 0f;
        }

        roadProgress = Mathf.InverseLerp(
            deterministicRoadMinX,
            deterministicRoadMaxX,
            supportedLoadX);
        return true;
    }

    private bool TryGetRoadSurfaceHeight(float worldX, float vehicleBottomY, out float roadY)
    {
        roadY = 0f;
        bool found = false;
        float closestVerticalDistance = float.PositiveInfinity;

        foreach (Bar bar in deterministicBars)
        {
            if (bar == null || bar.materialData == null || !bar.materialData.isRoad ||
                bar.startPoint == null || bar.endPoint == null || !bar.gameObject.activeInHierarchy)
                continue;

            Vector3 start = bar.startPoint.transform.position;
            Vector3 end = bar.endPoint.transform.position;
            float minX = Mathf.Min(start.x, end.x);
            float maxX = Mathf.Max(start.x, end.x);
            if (worldX < minX - 0.05f || worldX > maxX + 0.05f) continue;

            float segmentY = Mathf.Abs(end.x - start.x) <= 0.001f
                ? Mathf.Max(start.y, end.y)
                : Mathf.Lerp(start.y, end.y, Mathf.InverseLerp(start.x, end.x, worldX));
            float distance = Mathf.Abs(vehicleBottomY - segmentY);
            if (distance >= closestVerticalDistance) continue;

            closestVerticalDistance = distance;
            roadY = segmentY;
            found = true;
        }

        return found;
    }

    // --- Deterministic Spatial Comparers ---
    private class SpatialPointComparer : IComparer<Point>
    {
        public int Compare(Point a, Point b)
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a == null) return -1;
            if (b == null) return 1;

            Vector3 posA = a.transform.position;
            Vector3 posB = b.transform.position;

            float tolerance = 0.001f;

            if (Mathf.Abs(posA.x - posB.x) > tolerance) return posA.x.CompareTo(posB.x);
            if (Mathf.Abs(posA.y - posB.y) > tolerance) return posA.y.CompareTo(posB.y);
            if (Mathf.Abs(posA.z - posB.z) > tolerance) return posA.z.CompareTo(posB.z);

            return a.GetInstanceID().CompareTo(b.GetInstanceID());
        }
    }

    private class SpatialBarComparer : IComparer<Bar>
    {
        private SpatialPointComparer pointComparer = new SpatialPointComparer();

        public int Compare(Bar a, Bar b)
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a == null) return -1;
            if (b == null) return 1;

            Point a1 = a.startPoint;
            Point a2 = a.endPoint;
            if (pointComparer.Compare(a1, a2) > 0) { a1 = a.endPoint; a2 = a.startPoint; }

            Point b1 = b.startPoint;
            Point b2 = b.endPoint;
            if (pointComparer.Compare(b1, b2) > 0) { b1 = b.endPoint; b2 = b.startPoint; }

            int p1Compare = pointComparer.Compare(a1, b1);
            if (p1Compare != 0) return p1Compare;

            int p2Compare = pointComparer.Compare(a2, b2);
            if (p2Compare != 0) return p2Compare;

            if (a.materialData != null && b.materialData != null)
            {
                int matCompare = string.Compare(a.materialData.name, b.materialData.name, StringComparison.Ordinal);
                if (matCompare != 0) return matCompare;
            }

            return a.GetInstanceID().CompareTo(b.GetInstanceID()); 
        }
    }

    private void Awake()
    {
        // Older scenes serialized the former live-load-only display rule. The
        // current rule deliberately shows self-weight as soon as settling ends.
        displayLiveLoadStressOnly = false;
        sharedRoadPhysicsMat = new PhysicMaterial("BridgeRoadGrip");
        sharedRoadPhysicsMat.dynamicFriction = 1f;
        sharedRoadPhysicsMat.staticFriction = 1f;
        sharedRoadPhysicsMat.frictionCombine = PhysicMaterialCombine.Maximum;
        sharedRoadPhysicsMat.bounciness = 0f;
    }

    private void Start()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnEnterBuildMode.AddListener(HandleEnterBuildMode);
            GameManager.Instance.OnExitBuildMode.AddListener(HandleExitBuildMode);
        }
    }

    private void OnDestroy()
    {
        RestoreGlobalPhysicsSettings();

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnEnterBuildMode.RemoveListener(HandleEnterBuildMode);
            GameManager.Instance.OnExitBuildMode.RemoveListener(HandleExitBuildMode);
        }
    }

    private void FixedUpdate()
    {
        if (pendingSimulationStart)
        {
            if (needsPhysicsRelease)
            {
                needsPhysicsRelease = false;
                Physics.SyncTransforms(); 

                foreach (Bar bar in deterministicBars)
                {
                    if (bar != null)
                    {
                        Rigidbody rb = bar.GetComponent<Rigidbody>();
                        if (rb != null)
                        {
                            rb.useGravity = true;
                            rb.isKinematic = false;
                            rb.WakeUp();
                        }
                    }
                }

                foreach (Point p in deterministicPoints)
                {
                    if (p != null && !p.isAnchor)
                    {
                        Rigidbody rb = p.GetComponent<Rigidbody>();
                        if (rb != null)
                        {
                            rb.useGravity = true;
                            rb.isKinematic = false;
                            rb.WakeUp();
                        }
                    }
                }

                OnSettlePhaseStarted?.Invoke(); 
            }

            // Joint.currentForce can jitter slightly even after a bridge has
            // visually settled. Keep the final rolling window instead of using
            // one arbitrary fixed tick as the dead-load baseline for the entire
            // run. Skip the release tick because PhysX has not simulated the
            // newly dynamic bridge yet.
            if (currentSettleFrame > 0)
            {
                foreach (var handler in activeStressHandlers)
                {
                    if (handler != null) handler.SampleSettlingForce();
                }
            }

            currentSettleFrame++;
            
            if (currentSettleFrame >= settleFramesAmount)
            {
                pendingSimulationStart = false;
                isSimulating = true;
                bool hasDeterministicStress = deterministicStressResult != null &&
                    deterministicStressResult.IsValid && deterministicStructureStable;
                // Peaks must describe load positions that have actually occurred,
                // not the solver's precomputed worst case somewhere later on the
                // crossing.
                peakDisplayedStressThisRun = 0f;
                // Structural failure follows the deterministic load position over
                // time; do not fail immediately because a later sample is unsafe.
                peakStressThisRun = 0f;
                lockStressTracking = false;
                
                foreach (var handler in activeStressHandlers)
                {
                    if (handler != null) handler.BeginTracking();
                }
                
                OnSimulationStarted?.Invoke(); 
            }
            return; 
        }

        if (isSimulating && !lockStressTracking)
        {
            bool hasDeterministicStress = deterministicStressResult != null &&
                deterministicStressResult.IsValid && deterministicStructureStable;
            bool hasUnstableDeterministicStructure = useDeterministicStressAnalysis &&
                deterministicAnalysisPrepared && !deterministicStructureStable;
            int deterministicSampleIndex = 0;
            float deterministicLoadFactor = 0f;
            bool hasVehicleRoadState = TryGetCurrentVehicleRoadState(
                out float vehicleRoadProgress,
                out float vehicleRoadLoadFactor);
            if (hasVehicleRoadState && vehicleRoadLoadFactor > 0.01f)
                HasLiveLoadEngagedThisRun = true;
            if (hasDeterministicStress)
            {
                deterministicLoadFactor = vehicleRoadLoadFactor;
                if (hasVehicleRoadState && deterministicLoadFactor > 0f)
                {
                    deterministicSampleIndex = Mathf.Clamp(
                        Mathf.RoundToInt(vehicleRoadProgress *
                                         (deterministicStressResult.Samples.Length - 1)),
                        0,
                        deterministicStressResult.Samples.Length - 1);
                }
            }
            else if (hasUnstableDeterministicStructure)
                deterministicLoadFactor = vehicleRoadLoadFactor;
            float currentStructuralMax = 0f;
            float currentDisplayedMax = 0f;
            foreach (var handler in activeStressHandlers)
            {
                if (handler == null) continue;
                
                // PhysX still moves the bridge, but a deterministic sample is
                // authoritative for stress. Avoid reading and recoloring every
                // joint just before replacing that result in the same tick.
                if (hasDeterministicStress && deterministicStressResult.TryGetStress(
                        handler.Bar,
                        deterministicSampleIndex,
                        out float displayedStress,
                        out float structuralStress,
                        out bool isTension))
                {
                    if (deterministicStressResult.TryGetDeadLoadStress(
                            handler.Bar,
                            out float deadDisplayedStress,
                            out float deadStructuralStress,
                            out bool deadIsTension))
                    {
                        displayedStress = Mathf.Lerp(
                            deadDisplayedStress,
                            displayedStress,
                            deterministicLoadFactor);
                        structuralStress = Mathf.Lerp(
                            deadStructuralStress,
                            structuralStress,
                            deterministicLoadFactor);
                        if (deterministicLoadFactor <= 0f) isTension = deadIsTension;
                    }
                    else
                    {
                        displayedStress *= deterministicLoadFactor;
                        structuralStress *= deterministicLoadFactor;
                    }

                    handler.ApplyDeterministicStress(
                        displayedStress,
                        structuralStress,
                        isTension);
                }
                else if (hasUnstableDeterministicStructure && deterministicLoadFactor > 0.01f)
                {
                    // A mechanism can fold without generating large axial force.
                    // Show it as unsafe instead of rewarding the low force reading.
                    // This synthetic 100% marker is not a measured member overload,
                    // so let the physical mechanism fold instead of detaching every bar.
                    handler.ApplyDeterministicStress(1f, 1f, false, false);
                }
                else
                {
                    // Retain the PhysX visual fallback for a member absent from
                    // the deterministic result, without letting it break the bridge.
                    handler.EvaluateStress(
                        !hasDeterministicStress &&
                        !hasUnstableDeterministicStructure);
                }
                
                if (handler.isBroken)
                {
                    currentStructuralMax = Mathf.Max(currentStructuralMax, 1f);
                    currentDisplayedMax = Mathf.Max(currentDisplayedMax, 1f);
                }
                else
                {
                    currentStructuralMax = Mathf.Max(
                        currentStructuralMax,
                        handler.currentStructuralStressPercent);
                    currentDisplayedMax = Mathf.Max(
                        currentDisplayedMax,
                        handler.currentStressPercent);
                }
            }

            peakStressThisRun = Mathf.Max(peakStressThisRun, currentStructuralMax);
            peakDisplayedStressThisRun = Mathf.Max(
                peakDisplayedStressThisRun,
                Mathf.Clamp01(currentDisplayedMax));
        }
    }

    private void GatherActiveBridgeData(out HashSet<Point> outPoints, out HashSet<Bar> outBars)
    {
        outPoints = new HashSet<Point>();
        outBars = new HashSet<Bar>();

        BuildLocation activeLocation = GameManager.Instance != null
            ? GameManager.Instance.ActiveBuildLocation
            : null;

        foreach (Point p in Point.AllPoints)
        {
            if (p != null && p.gameObject.activeSelf && p.enabled &&
                (activeLocation == null || activeLocation.Owns(p)))
            {
                outPoints.Add(p);
            }
        }

        foreach (Point p in outPoints)
        {
            foreach (Bar b in p.ConnectedBars)
            {
                if (b != null && b.gameObject.activeSelf && b.enabled &&
                    (activeLocation == null || activeLocation.Owns(b)))
                {
                    outBars.Add(b);
                }
            }
        }

        foreach (Bar b in outBars)
        {
            if (b.startPoint != null && b.startPoint.enabled) outPoints.Add(b.startPoint);
            if (b.endPoint != null && b.endPoint.enabled) outPoints.Add(b.endPoint);
        }
    }

    private void HandleEnterBuildMode()
    {
        if (isSimulating || pendingSimulationStart) StopPhysicsAndReset();
        else SetNodesVisible(true);
    }

    private void HandleExitBuildMode()
    {
        SetNodesVisible(false);
    }

    private void SetNodesVisible(bool isVisible)
    {
        HashSet<Point> points;
        HashSet<Bar> bars;
        GatherActiveBridgeData(out points, out bars);

        foreach (Point p in Point.AllPoints)
        {
            Renderer r = p.GetComponentInChildren<Renderer>();
            if (r != null) r.enabled = isVisible && p.gameObject.activeSelf && points.Contains(p);
        }
    }

    public void ActivatePhysics()
    {
        if (isSimulating || pendingSimulationStart) return;
        HadBrokenPartsThisRun = false;
        HasLiveLoadEngagedThisRun = false;
        FirstMemberFailureDescription = string.Empty;
        FirstMemberFailedUnderDeadLoad = false;

        // Build locations may contain endpoint ramps saved by older revisions.
        // Clear those legacy invisible colliders before releasing the bridge.
        BridgeAbutmentAligner activeAbutmentAligner = null;
        if (GameManager.Instance != null && GameManager.Instance.ActiveBuildLocation != null)
        {
            activeAbutmentAligner =
                GameManager.Instance.ActiveBuildLocation.GetComponent<BridgeAbutmentAligner>();
            if (activeAbutmentAligner != null &&
                !activeAbutmentAligner.RefreshRuntimeApproaches(out string approachReport))
            {
                Debug.LogWarning(
                    $"[BridgePhysicsManager] Could not refresh the active bridge approaches: {approachReport}",
                    activeAbutmentAligner);
            }
        }
        
        activeStressHandlers.Clear(); 
        peakDisplayedStressThisRun = 0f;
        peakStressThisRun = 0f;
        lockStressTracking = false;

        GatherActiveBridgeData(out simPoints, out simBars);

        deterministicPoints = new List<Point>(simPoints);
        deterministicPoints.Sort(new SpatialPointComparer());

        deterministicBars = new List<Bar>(simBars);
        deterministicBars.Sort(new SpatialBarComparer());

        foreach (Point p in deterministicPoints)
        {
            p.preSimPos = p.transform.position;
            p.preSimRot = p.transform.rotation;
            p.preSimParent = p.transform.parent;

            Renderer r = p.GetComponentInChildren<Renderer>();
            if (r != null) r.enabled = false;
        }

        foreach (Bar b in deterministicBars)
        {
            // Existing, pasted, redone, and newly drawn bars must all enter
            // simulation with the same endpoint order and transform orientation.
            b.NormalizeEndpointOrder();
            b.preSimPos = b.transform.position;
            b.preSimRot = b.transform.rotation;
        }

        ApplyDeterministicPhysicsSettings();
        Physics.SyncTransforms();

        // Re-evaluate scene-authored and active-pier support before building the
        // physics graph.
        foreach (Point point in deterministicPoints)
        {
            if (point != null) point.EvaluateAnchorState();
        }

        using (PhysicsGraphMarker.Auto())
        {
            SetupBarsPhysics(deterministicBars);
            SetupDirectConnections(deterministicBars, deterministicPoints);
            ReleaseUnsupportedRoadJoints(deterministicBars, deterministicPoints);
        }
        using (CollisionSetupMarker.Auto())
        {
            ResolveAdjacentCollisions(deterministicBars);
            if (activeAbutmentAligner != null)
                activeAbutmentAligner.IgnoreCollisionsWithBridge(CollectStructuralColliders());
        }
        ResetPhysicsState();
        using (StressAnalysisMarker.Auto())
            PrepareDeterministicStressAnalysis();

        needsPhysicsRelease = true;
        currentSettleFrame = 0;
        pendingSimulationStart = true;
    }

    private List<Collider> CollectStructuralColliders()
    {
        List<Collider> colliders = new List<Collider>();
        foreach (Bar bar in deterministicBars)
        {
            if (bar != null)
                colliders.AddRange(bar.GetComponentsInChildren<Collider>(true));
        }

        foreach (Point point in deterministicPoints)
        {
            if (point != null)
                colliders.AddRange(point.GetComponentsInChildren<Collider>(true));
        }

        return colliders;
    }

    public void StopPhysicsAndReset()
    {
        if (!isSimulating && !pendingSimulationStart) return;
        
        isSimulating = false;
        pendingSimulationStart = false;
        OnSimulationStopped?.Invoke(); 

        activeStressHandlers.Clear();

        foreach (Bar bar in deterministicBars)
        {
            if (bar == null) continue;
            foreach (Joint j in bar.GetComponentsInChildren<Joint>()) { j.connectedBody = null; DestroyImmediate(j); }
            foreach (Rigidbody rb in bar.GetComponentsInChildren<Rigidbody>()) DestroyImmediate(rb);
        }

        foreach (Point p in deterministicPoints)
        {
            if (p == null) continue;
            foreach (Joint j in p.GetComponentsInChildren<Joint>()) { j.connectedBody = null; DestroyImmediate(j); }
            foreach (Rigidbody rb in p.GetComponentsInChildren<Rigidbody>()) DestroyImmediate(rb);
            foreach (CapsuleCollider cc in p.GetComponents<CapsuleCollider>()) DestroyImmediate(cc);
        }

        bool isCurrentlyBuilding = GameManager.Instance != null && GameManager.Instance.CurrentState == GameManager.GameState.Building;

        foreach (Point p in deterministicPoints)
        {
            if (p == null) continue;
            
            Collider[] cols = p.GetComponentsInChildren<Collider>();
            foreach(var col in cols) col.enabled = true; 

            p.transform.SetParent(p.preSimParent);
            p.transform.position = p.preSimPos;
            p.transform.rotation = p.preSimRot;

            Renderer r = p.GetComponentInChildren<Renderer>();
            if (r != null) r.enabled = isCurrentlyBuilding && p.gameObject.activeSelf;
        }

        foreach (Bar bar in deterministicBars)
        {
            if (bar == null) continue;
            
            if (bar.materialData != null && bar.materialData.isPier)
            {
                bar.RemovePierCapColliders();

                foreach (Transform child in bar.transform)
                {
                    if (child.name.StartsWith("VisualSegment"))
                    {
                        Renderer segRend = child.GetComponentInChildren<Renderer>();
                        if (segRend != null)
                        {
                            BoxCollider bc = segRend.GetComponent<BoxCollider>();
                            if (bc != null) { bc.enabled = false; DestroyImmediate(bc); }
                        }
                    }
                }
            }
            else
            {
                BoxCollider[] parentCols = bar.GetComponents<BoxCollider>();
                foreach (BoxCollider c in parentCols) { c.enabled = false; DestroyImmediate(c); }
            }

            BarStressHandler stress = bar.GetComponent<BarStressHandler>();
            if (stress != null) DestroyImmediate(stress);

            bar.transform.position = bar.preSimPos;
            bar.transform.rotation = bar.preSimRot;
            
            if (bar.gameObject.activeSelf && bar.startPoint != null && bar.endPoint != null)
            {
                bar.StartPosition = bar.startPoint.transform.position;
            }
        }

        simPoints.Clear();
        simBars.Clear();
        deterministicPoints.Clear();
        deterministicBars.Clear();
        deterministicStressResult = null;
        deterministicLiveLoadVehicle = null;
        deterministicRoadMinX = 0f;
        deterministicRoadMaxX = 0f;
        deterministicAnalysisPrepared = false;
        deterministicStructureStable = false;

        Physics.SyncTransforms();
        RestoreGlobalPhysicsSettings();
    }

    private void ApplyDeterministicPhysicsSettings()
    {
        if (!deterministicPhysicsOverridesApplied)
        {
            previousAutoSyncTransforms = Physics.autoSyncTransforms;
            previousMaximumDeltaTime = Time.maximumDeltaTime;
            deterministicPhysicsOverridesApplied = true;
        }

        // Setup transform changes are synchronized explicitly. Avoiding implicit
        // sync points keeps physics setup independent from render-frame timing.
        Physics.autoSyncTransforms = false;
        // Keep expensive iterations on the simulated bridge and vehicle only.
        // Changing the global defaults would also burden unrelated scene bodies.
        if (Application.isMobilePlatform)
            Time.maximumDeltaTime = Mathf.Min(previousMaximumDeltaTime,
                Mathf.Max(Time.fixedDeltaTime, mobileMaximumSimulationDeltaTime));
    }

    private void RestoreGlobalPhysicsSettings()
    {
        if (!deterministicPhysicsOverridesApplied) return;

        Physics.autoSyncTransforms = previousAutoSyncTransforms;
        Time.maximumDeltaTime = previousMaximumDeltaTime;
        deterministicPhysicsOverridesApplied = false;
    }

    /// <summary>
    /// Clears dynamic state only from the bridge bodies participating in the
    /// current simulation. Do not replace this with FindObjectsOfType<Rigidbody>:
    /// that would also reset the player, NPCs, vehicles, and world props.
    /// </summary>
    public void ResetPhysicsState()
    {
        HashSet<Rigidbody> simulationBodies = new HashSet<Rigidbody>();

        foreach (Bar bar in deterministicBars)
        {
            if (bar == null) continue;
            foreach (Rigidbody body in bar.GetComponentsInChildren<Rigidbody>(true))
                if (body != null) simulationBodies.Add(body);
        }

        foreach (Point point in deterministicPoints)
        {
            if (point == null) continue;
            foreach (Rigidbody body in point.GetComponentsInChildren<Rigidbody>(true))
                if (body != null) simulationBodies.Add(body);
        }

        foreach (Rigidbody body in simulationBodies)
        {
            // All bridge bodies are deliberately held kinematic until the first
            // controlled fixed tick, so no force can leak into the new run.
            if (!body.isKinematic)
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            body.isKinematic = true;
            body.position = body.transform.position;
            body.rotation = body.transform.rotation;
            body.ResetInertiaTensor();
            body.Sleep();
        }

        Physics.SyncTransforms();
    }

    private void PrepareDeterministicStressAnalysis()
    {
        deterministicStressResult = null;
        deterministicLiveLoadVehicle = null;
        deterministicRoadMinX = float.PositiveInfinity;
        deterministicRoadMaxX = float.NegativeInfinity;
        deterministicAnalysisPrepared = false;
        deterministicStructureStable = false;
        if (!useDeterministicStressAnalysis) return;

        deterministicAnalysisPrepared = true;

        ContractSO contract = GameManager.Instance != null ? GameManager.Instance.CurrentContract : null;
        float liveLoadKg = LiveLoadVehicle.GetContractTestWeight(contract);
        deterministicLiveLoadVehicle = LiveLoadVehicle.FindActiveForContract(contract);
        bool hasRoadSpan = TryResolveRoadSpan();
        deterministicStressResult = DeterministicBridgeStressSolver.Analyze(
            deterministicPoints,
            deterministicBars,
            liveLoadKg,
            displayLiveLoadStressOnly,
            deterministicLoadSamples);

        if (deterministicStressResult == null || !deterministicStressResult.IsValid)
        {
            Debug.LogWarning(
                "[BridgePhysicsManager] Deterministic stress analysis could not solve this bridge; " +
                "the bridge will be treated as structurally unstable instead of using a forgiving PhysX score.", this);
            deterministicStressResult = null;
        }
        else
        {
            deterministicStructureStable = deterministicStressResult.IsStructurallyStable;
            if (!deterministicStructureStable)
            {
                Debug.LogWarning(
                    "[BridgePhysicsManager] The bridge contains a free structural mechanism. " +
                    "Low axial force from folding will not count as bridge strength.", this);
            }
        }

        if (deterministicLiveLoadVehicle == null ||
            !hasRoadSpan)
        {
            Debug.LogWarning(
                "[BridgePhysicsManager] Deterministic live load has no matching active vehicle or valid road span. " +
                "Only the bridge's dead load will be evaluated until the setup is corrected.", this);
        }
    }

    private void GetDeterministicLoadState(out int sampleIndex, out float loadFactor)
    {
        sampleIndex = 0;
        loadFactor = 0f;
        if (deterministicStressResult == null || deterministicStressResult.Samples.Length == 0 ||
            !TryGetCurrentVehicleRoadState(out float progress, out loadFactor))
            return;
        if (loadFactor <= 0f) return;

        sampleIndex = Mathf.Clamp(
            Mathf.RoundToInt(progress * (deterministicStressResult.Samples.Length - 1)),
            0,
            deterministicStressResult.Samples.Length - 1);
    }

    private bool TryResolveRoadSpan()
    {
        if (!float.IsNaN(deterministicRoadMinX) && !float.IsInfinity(deterministicRoadMinX) &&
            !float.IsNaN(deterministicRoadMaxX) && !float.IsInfinity(deterministicRoadMaxX) &&
            deterministicRoadMaxX > deterministicRoadMinX)
            return true;

        deterministicRoadMinX = float.PositiveInfinity;
        deterministicRoadMaxX = float.NegativeInfinity;
        foreach (Bar bar in deterministicBars)
        {
            if (bar == null || bar.materialData == null || !bar.materialData.isRoad ||
                bar.startPoint == null || bar.endPoint == null)
                continue;

            deterministicRoadMinX = Mathf.Min(
                deterministicRoadMinX,
                bar.startPoint.transform.position.x,
                bar.endPoint.transform.position.x);
            deterministicRoadMaxX = Mathf.Max(
                deterministicRoadMaxX,
                bar.startPoint.transform.position.x,
                bar.endPoint.transform.position.x);
        }

        return !float.IsNaN(deterministicRoadMinX) && !float.IsInfinity(deterministicRoadMinX) &&
               !float.IsNaN(deterministicRoadMaxX) && !float.IsInfinity(deterministicRoadMaxX) &&
               deterministicRoadMaxX > deterministicRoadMinX;
    }

    public bool BakeBridge(ContractSO contract = null)
    {
        HashSet<Point> bakePoints = new HashSet<Point>();
        HashSet<Bar> bakeBars = new HashSet<Bar>();

        BuildLocation targetLoc = null;

        if (contract != null)
        {
            BuildLocation[] allLocs = Resources.FindObjectsOfTypeAll<BuildLocation>();
            foreach (var loc in allLocs)
            {
                if (loc.gameObject.scene.name != null && loc.activeContract == contract)
                {
                    targetLoc = loc;
                    break;
                }
            }
        }
        else if (GameManager.Instance != null && GameManager.Instance.ActiveBuildLocation != null)
        {
            targetLoc = GameManager.Instance.ActiveBuildLocation;
        }

        if (targetLoc == null)
        {
            Debug.LogError("[BridgePhysicsManager] Cannot bake: no matching build location was found.", this);
            return false;
        }

        foreach (Point p in Point.AllPoints)
        {
            if (p != null && p.gameObject.activeSelf && p.enabled && targetLoc.Owns(p))
            {
                bakePoints.Add(p);
                foreach (Bar b in p.ConnectedBars)
                {
                    if (b != null && b.gameObject.activeSelf && b.enabled && targetLoc.Owns(b))
                        bakeBars.Add(b);
                }
            }
        }

        foreach (Point anchor in targetLoc.startingAnchors)
        {
            if (anchor != null) { bakePoints.Add(anchor); Queue<Point> q = new Queue<Point>(); q.Enqueue(anchor); ProcessQueue(q); }
        }
        
        foreach (Point anchor in targetLoc.endingAnchors)
        {
            if (anchor != null && !bakePoints.Contains(anchor)) { bakePoints.Add(anchor); Queue<Point> q = new Queue<Point>(); q.Enqueue(anchor); ProcessQueue(q); }
        }

        void ProcessQueue(Queue<Point> queue)
        {
            while (queue.Count > 0)
            {
                Point current = queue.Dequeue();
                foreach (Bar b in current.ConnectedBars)
                {
                    if (b != null && b.gameObject.activeSelf && targetLoc.Owns(b) &&
                        !bakeBars.Contains(b))
                    {
                        bakeBars.Add(b);
                        Point neighbor = (b.startPoint == current) ? b.endPoint : b.startPoint;
                        if (neighbor != null && targetLoc.Owns(neighbor) && !bakePoints.Contains(neighbor))
                        {
                            bakePoints.Add(neighbor);
                            queue.Enqueue(neighbor);
                        }
                    }
                }
            }
        }

        foreach(Bar b in targetLoc.bakedBars) { if (b != null) bakeBars.Add(b); }
        foreach(Point p in targetLoc.bakedPoints) { if (p != null) bakePoints.Add(p); }

        if (bakePoints.Count < 2 || bakeBars.Count == 0)
        {
            Debug.LogError(
                $"[BridgePhysicsManager] Refusing to bake '{targetLoc.name}': the captured bridge has " +
                $"{bakePoints.Count} point(s) and {bakeBars.Count} bar(s).", this);
            return false;
        }

        foreach (Bar bar in bakeBars)
        {
            if (bar == null || bar.startPoint == null || bar.endPoint == null ||
                bar.materialData == null || !bakePoints.Contains(bar.startPoint) ||
                !bakePoints.Contains(bar.endPoint))
            {
                Debug.LogError(
                    $"[BridgePhysicsManager] Refusing to bake '{targetLoc.name}': a bar has invalid endpoints or material data.",
                    this);
                return false;
            }
        }

        foreach (Point p in bakePoints)
            if (p != null) p.AssignOwner(targetLoc, true);
        foreach (Bar b in bakeBars)
            if (b != null) b.AssignOwner(targetLoc, true);

        targetLoc.bakedPoints.Clear();
        targetLoc.bakedBars.Clear();

        foreach (Point p in bakePoints)
        {
            if (p == null) continue;
            foreach (Rigidbody rb in p.GetComponentsInChildren<Rigidbody>())
            {
                ClearDynamicVelocity(rb);
                rb.isKinematic = true;
                rb.useGravity = false;
            }
        }
        foreach (Bar b in bakeBars)
        {
            if (b == null) continue;
            foreach (Rigidbody rb in b.GetComponentsInChildren<Rigidbody>())
            {
                ClearDynamicVelocity(rb);
                rb.isKinematic = true;
                rb.useGravity = false;
            }
        }

        foreach (Point p in bakePoints)
        {
            if (p == null) continue;
            foreach (var j in p.GetComponentsInChildren<Joint>()) Destroy(j);
            foreach (var rb in p.GetComponentsInChildren<Rigidbody>()) Destroy(rb);
            p.enabled = false; 
            targetLoc.bakedPoints.Add(p); 
        }

        foreach (Bar b in bakeBars)
        {
            if (b == null) continue;
            foreach (var j in b.GetComponentsInChildren<Joint>()) Destroy(j);
            foreach (var rb in b.GetComponentsInChildren<Rigidbody>()) Destroy(rb);
            if (b.GetComponent<BarStressHandler>() != null) Destroy(b.GetComponent<BarStressHandler>());
            EnsurePermanentBakedRoadCollider(b);
            b.enabled = false; 
            targetLoc.bakedBars.Add(b); 
        }

        activeStressHandlers.Clear();
        isSimulating = false;
        pendingSimulationStart = false;
        simPoints.Clear();
        simBars.Clear();
        deterministicPoints.Clear();
        deterministicBars.Clear();

        RestoreGlobalPhysicsSettings();
        Physics.SyncTransforms();

        if (DynamicNavMeshUpdater.Instance != null)
            DynamicNavMeshUpdater.Instance.UpdateWalkableNavMeshForLocation(targetLoc);

        OnSimulationStopped?.Invoke();
        return true;
    }

    private void EnsurePermanentBakedRoadCollider(Bar bar)
    {
        if (bar == null || bar.materialData == null || !bar.materialData.isRoad ||
            bar.startPoint == null || bar.endPoint == null) return;

        BoxCollider[] existingColliders = bar.GetComponents<BoxCollider>();
        BoxCollider roadCollider = existingColliders.Length > 0
            ? existingColliders[0]
            : bar.gameObject.AddComponent<BoxCollider>();

        // Remove redundant parent road colliders left by an earlier simulation.
        for (int i = 1; i < existingColliders.Length; i++)
        {
            if (existingColliders[i] != null)
            {
                existingColliders[i].enabled = false;
                Destroy(existingColliders[i]);
            }
        }

        float length = Vector3.Distance(
            bar.startPoint.transform.position,
            bar.endPoint.transform.position);

        roadCollider.size = new Vector3(
            Mathf.Max(0.05f, length + bakedRoadColliderSeamOverlap),
            bakedRoadColliderThickness,
            Mathf.Max(bakedRoadColliderWidth, bar.visualSize.z));
        roadCollider.center = new Vector3(
            0f,
            bakedRoadVisualTop - bakedRoadColliderThickness * 0.5f,
            0f);
        roadCollider.isTrigger = false;
        roadCollider.enabled = true;
        roadCollider.material = sharedRoadPhysicsMat;

        int bridgeLayer = LayerMask.NameToLayer("Bridge");
        if (bridgeLayer >= 0) bar.gameObject.layer = bridgeLayer;
    }

    public float GetMaxBridgeStress()
    {
        float maxStress = 0f;
        foreach (var handler in activeStressHandlers)
        {
            if (handler == null) continue;
            if (handler.isBroken) return 1f; 

            if (handler.currentStressPercent > maxStress)
            {
                maxStress = handler.currentStressPercent;
            }
        }
        return Mathf.Clamp01(maxStress); 
    }

    /// <summary>
    /// Returns the highest value produced by GetMaxBridgeStress during this run.
    /// This deliberately follows the visualizer's dead-load display setting;
    /// peakStressThisRun remains the total structural value used for failure.
    /// </summary>
    public float GetPeakDisplayedBridgeStress()
    {
        return Mathf.Clamp01(peakDisplayedStressThisRun);
    }

    private void SetupBarsPhysics(List<Bar> activeBars)
    {
        foreach (Bar bar in activeBars)
        {
            if (bar.GetComponent<Rigidbody>() == null) ApplyPhysicsToBar(bar);
        }
    }

    private static void ClearDynamicVelocity(Rigidbody body)
    {
        // Kinematic bodies reject velocity writes. Reset moving bodies before freezing them.
        if (body == null || body.isKinematic) return;
        body.velocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
    }

    private void ApplyPhysicsToBar(Bar bar)
    {
        bar.NormalizeEndpointOrder();
        Point p1 = bar.startPoint;
        Point p2 = bar.endPoint;
        if (p1 == null || p2 == null || !p1.gameObject.activeSelf || !p2.gameObject.activeSelf)
            return;

        if (!bar.materialData.isRope)
        {
            float length = Vector3.Distance(p1.transform.position, p2.transform.position);
            
            Rigidbody barRb = bar.GetComponent<Rigidbody>();
            if (barRb == null) barRb = bar.gameObject.AddComponent<Rigidbody>();
            
            ClearDynamicVelocity(barRb);
            barRb.isKinematic = true;
            barRb.useGravity = true;
            
            barRb.mass = length * bar.materialData.GetPlacedMassPerMeter();
            ConfigureVisualBridgeBody(barRb);
            barRb.interpolation = RigidbodyInterpolation.Interpolate;
            barRb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            barRb.sleepThreshold = 0f;
            barRb.maxDepenetrationVelocity = 2f;

            BoxCollider[] oldCols = bar.GetComponents<BoxCollider>();
            foreach(var c in oldCols) { c.enabled = false; DestroyImmediate(c); }

            if (bar.materialData.isPier)
            {
                bar.RemovePierCapColliders();

                foreach (Transform child in bar.transform)
                {
                    if (child.name.StartsWith("VisualSegment"))
                    {
                        Renderer segRend = child.GetComponentInChildren<Renderer>();
                        if (segRend != null && segRend.gameObject.GetComponent<Collider>() == null)
                        {
                            segRend.gameObject.AddComponent<BoxCollider>();
                        }
                    }
                }
            }
            else
            {
                int spawnCount = bar.materialData.isDualBeam ? 2 : 1;
                for (int i = 0; i < spawnCount; i++)
                {
                    BoxCollider col = bar.gameObject.AddComponent<BoxCollider>();
                    
                    float thickness = bar.materialData.isRoad
                        ? Mathf.Max(0.05f, bakedRoadColliderThickness)
                        : barColliderThickness;
                    float depth = bar.visualSize.z; 

                    if (!bar.materialData.isDualBeam && depth < 2.0f) depth = 2.0f; 
                    else if (bar.materialData.isDualBeam && depth < 0.2f) depth = 0.2f;

                    float zOffsetValue = bar.materialData.isDualBeam ? ((i == 0) ? bar.materialData.zOffset : -bar.materialData.zOffset) : 0f;
                    // Dynamic road bars rotate independently as the bridge flexes.
                    // Shortening their colliders exposes a vertical lip at every
                    // joint; a driven wheel can catch that lip and pitch the cart
                    // upward. Adjacent bridge colliders ignore each other below,
                    // so a small road overlap is stable and keeps the lane continuous.
                    float physicsLength = bar.materialData.isRoad
                        ? length + Mathf.Max(
                            bakedRoadColliderSeamOverlap,
                            simulatedRoadColliderSeamOverlap)
                        : length - 0.02f;
                    
                    col.size = new Vector3(physicsLength, thickness, depth);
                    float centerY = bar.materialData.isRoad
                        ? bakedRoadVisualTop - thickness * 0.5f
                        : 0f;
                    col.center = new Vector3(0, centerY, zOffsetValue);
                    
                    if (bar.materialData.isRoad) col.material = sharedRoadPhysicsMat;
                }
            }
        }

        bool isConnectedToTerrainAnchor = p1.IsPermanentAnchor || p2.IsPermanentAnchor;

        if (isConnectedToTerrainAnchor)
        {
            bar.gameObject.layer = LayerMask.NameToLayer("Node"); 
        }
        else
        {
            bar.gameObject.layer = LayerMask.NameToLayer("Bridge"); 
        }

        BarStressHandler stressHandler = bar.GetComponent<BarStressHandler>();
        if (stressHandler == null) stressHandler = bar.gameObject.AddComponent<BarStressHandler>();
        
        stressHandler.Setup(bar.materialData, p1, p2);
        activeStressHandlers.Add(stressHandler);
    }

    private void SetupDirectConnections(List<Bar> activeBars, List<Point> activePoints)
    {
        foreach (Point p in activePoints)
        {
            if (!p.gameObject.activeSelf || p.ConnectedBars.Count == 0) continue; 

            // --- FIX 2: Sort the local Point connections! ---
            // This guarantees floating-point mass math and PhysX Joint Evaluation 
            // happen in the exact same sequence regardless of load/draw order.
            List<Bar> sortedConnectedBars = new List<Bar>(p.ConnectedBars);
            sortedConnectedBars.Sort(new SpatialBarComparer());

            Collider[] oldCols = p.GetComponents<Collider>();
            foreach(var col in oldCols) col.enabled = false; 

            Rigidbody nodeRb = p.GetComponent<Rigidbody>();
            if (nodeRb == null) nodeRb = p.gameObject.AddComponent<Rigidbody>();
            
            ClearDynamicVelocity(nodeRb);
            nodeRb.isKinematic = true;
            nodeRb.useGravity = !p.isAnchor;
            ConfigureVisualBridgeBody(nodeRb);
            
            if (!p.isAnchor)
            {
                float calculatedMass = 0.5f;
                
                // USE THE SORTED LIST
                foreach (Bar bar in sortedConnectedBars)
                {
                    if (bar == null || !bar.gameObject.activeSelf) continue; 
                    
                    float len = Vector3.Distance(bar.startPoint.transform.position, bar.endPoint.transform.position);
                    // Solid members already carry their own Rigidbody mass. Ropes have no
                    // Rigidbody, so distribute only their mass equally to the endpoint nodes.
                    if (bar.materialData.isRope)
                        calculatedMass += (len * bar.materialData.GetPlacedMassPerMeter()) * 0.5f;
                }
                
                nodeRb.mass = calculatedMass;
                nodeRb.interpolation = RigidbodyInterpolation.Interpolate;

                nodeRb.sleepThreshold = 0f;
                nodeRb.maxDepenetrationVelocity = 2f;
            }


            // USE THE SORTED LIST
            foreach (Bar bar in sortedConnectedBars)
            {
                if (bar == null || !bar.gameObject.activeSelf) continue; 

                if (!bar.materialData.isRope) AttachJoint(bar.gameObject, nodeRb, bar.materialData, p.transform.position);
            }

            // Road bars overlap at their endpoints, so a separate node collider
            // is unnecessary. The former cross-lane capsule stayed fixed at an
            // anchor while the deck flexed and became a small wheel ridge.
        }

        foreach (Bar rope in activeBars)
        {
            if (!rope.materialData.isRope) continue;

            Rigidbody rbA = rope.startPoint.GetComponent<Rigidbody>();
            Rigidbody rbB = rope.endPoint.GetComponent<Rigidbody>();

            if (rbA != null && rbB != null)
            {
                SpringJoint ropeSpring = rbA.gameObject.AddComponent<SpringJoint>();
                ropeSpring.connectedBody = rbB;
                ropeSpring.autoConfigureConnectedAnchor = false;
                ropeSpring.enablePreprocessing = false; 

                ropeSpring.anchor = rbA.transform.InverseTransformPoint(rope.startPoint.transform.position);
                ropeSpring.connectedAnchor = rbB.transform.InverseTransformPoint(rope.endPoint.transform.position);

                float length = Vector3.Distance(rope.startPoint.transform.position, rope.endPoint.transform.position);

                ropeSpring.maxDistance = length;
                ropeSpring.minDistance = 0f;
                ropeSpring.spring = rope.materialData.spring > 0 ? rope.materialData.spring : 5000f;
                ropeSpring.damper = rope.materialData.damper > 0 ? rope.materialData.damper : 500f; 

                BarStressHandler stressHandler = rope.GetComponent<BarStressHandler>();
                if (stressHandler != null) stressHandler.SetRopeJoint(ropeSpring);
            }
        } 
    }

    private void ConfigureVisualBridgeBody(Rigidbody body)
    {
        body.solverIterations = Mathf.Max(1, physicsSolverIterations);
        body.solverVelocityIterations = Mathf.Max(1, physicsSolverVelocityIterations);
        body.constraints = constrainVisualBridgeToPlane
            ? RigidbodyConstraints.FreezePositionZ |
              RigidbodyConstraints.FreezeRotationX |
              RigidbodyConstraints.FreezeRotationY
            : RigidbodyConstraints.None;
        body.drag = visualLinearDrag;
        body.angularDrag = visualAngularDrag;
    }

    /// <summary>
    /// Road pieces are the driving surface, not a replacement for the supporting
    /// frame. A road joint may remain connected only when that point has a load
    /// path to an anchor through a beam, rope, or pier. A single road piece can
    /// still span between two genuine supports within its existing max length.
    /// </summary>
    private void ReleaseUnsupportedRoadJoints(List<Bar> activeBars, List<Point> activePoints)
    {
        HashSet<Bar> activeBarSet = new HashSet<Bar>(activeBars);
        HashSet<Point> structurallySupportedPoints = new HashSet<Point>();
        Queue<Point> searchQueue = new Queue<Point>();

        foreach (Point point in activePoints)
        {
            if (point == null || !point.gameObject.activeSelf || !point.isAnchor) continue;
            if (structurallySupportedPoints.Add(point)) searchQueue.Enqueue(point);
        }

        // Do not traverse road pieces: otherwise an unsupported road-only chain
        // would incorrectly make every deck joint appear structurally supported.
        while (searchQueue.Count > 0)
        {
            Point current = searchQueue.Dequeue();
            foreach (Bar bar in current.ConnectedBars)
            {
                if (bar == null || !activeBarSet.Contains(bar) || !bar.gameObject.activeSelf ||
                    bar.materialData == null || bar.materialData.isRoad)
                    continue;

                Point neighbor = null;
                if (bar.startPoint == current) neighbor = bar.endPoint;
                else if (bar.endPoint == current) neighbor = bar.startPoint;

                if (neighbor != null && structurallySupportedPoints.Add(neighbor))
                    searchQueue.Enqueue(neighbor);
            }
        }

        int releasedJointCount = 0;
        foreach (Point point in activePoints)
        {
            if (point == null || structurallySupportedPoints.Contains(point)) continue;

            Rigidbody nodeBody = point.GetComponent<Rigidbody>();
            if (nodeBody == null) continue;

            foreach (Bar road in point.ConnectedBars)
            {
                if (road == null || !activeBarSet.Contains(road) || !road.gameObject.activeSelf ||
                    road.materialData == null || !road.materialData.isRoad)
                    continue;

                foreach (Joint joint in road.GetComponents<Joint>())
                {
                    if (joint == null || joint.connectedBody != nodeBody) continue;

                    // Remove this before the controlled release. Deferred destruction
                    // can survive into a batched fixed step and change the solver graph.
                    joint.connectedBody = null;
                    DestroyImmediate(joint);
                    releasedJointCount++;
                }
            }
        }

        if (releasedJointCount > 0)
        {
            Debug.Log(
                $"[BridgePhysicsManager] Released {releasedJointCount} unsupported road joint(s). " +
                "Road joints require a structural load path to an anchor.", this);
        }
    }

    private void AttachJoint(GameObject barObj, Rigidbody targetRb, BridgeMaterialSO mat, Vector3 anchorWorldPosition)
    {
        Rigidbody barRb = barObj != null ? barObj.GetComponent<Rigidbody>() : null;
        if (barRb == null || targetRb == null || mat == null)
            return;

        int jointCount = mat.isDualBeam ? 2 : 1;

        for (int i = 0; i < jointCount; i++)
        {
            float zOffsetValue = mat.isDualBeam ? ((i == 0) ? mat.zOffset : -mat.zOffset) : 0f;
            Vector3 finalAnchorWorld = anchorWorldPosition + new Vector3(0, 0, zOffsetValue);

            if (mat.useSpring)
            {
                SpringJoint spring = barObj.AddComponent<SpringJoint>();
                spring.connectedBody = targetRb;
                spring.enablePreprocessing = false; 
                ConfigureJointAnchors(spring, barRb, targetRb, finalAnchorWorld);
                spring.spring = mat.spring;
                spring.damper = mat.damper;
                spring.minDistance = 0f;
                spring.maxDistance = 0f;
            }
            else
            {
                HingeJoint hinge = barObj.AddComponent<HingeJoint>();
                hinge.connectedBody = targetRb;
                hinge.enablePreprocessing = false; 
                ConfigureJointAnchors(hinge, barRb, targetRb, finalAnchorWorld);
                hinge.axis = barObj.transform.InverseTransformDirection(Vector3.forward).normalized;
            }
        }
    }

    private static void ConfigureJointAnchors(
        Joint joint,
        Rigidbody barBody,
        Rigidbody nodeBody,
        Vector3 worldAnchor)
    {
        joint.autoConfigureConnectedAnchor = false;
        joint.enableCollision = false;
        joint.breakForce = Mathf.Infinity;
        joint.breakTorque = Mathf.Infinity;

        // Both local anchors are derived from the exact same world point. Their
        // world positions therefore coincide before either body is released.
        joint.anchor = barBody.transform.InverseTransformPoint(worldAnchor);
        joint.connectedAnchor = nodeBody.transform.InverseTransformPoint(worldAnchor);
    }

    private void ResolveAdjacentCollisions(List<Bar> activeBars)
    {
        List<Collider> bridgeCols = new List<Collider>();
        foreach(Bar b in activeBars)
        {
            if (b == null) continue;
            foreach (Collider collider in b.GetComponentsInChildren<Collider>())
            {
                // Disabled and trigger volumes cannot push bridge parts apart.
                // Excluding them avoids unnecessary pairwise physics calls.
                if (collider != null && collider.enabled && !collider.isTrigger &&
                    collider.gameObject.activeInHierarchy)
                    bridgeCols.Add(collider);
            }
        }

        for (int i = 0; i < bridgeCols.Count; i++)
        {
            for (int j = i + 1; j < bridgeCols.Count; j++)
            {
                Physics.IgnoreCollision(bridgeCols[i], bridgeCols[j], true);
            }
        }
    }
}
// Note: BarStressHandler remains exactly the same and has been omitted here to save context space, 
// as it was fully provided and correctly updated in our previous message!

public class BarStressHandler : MonoBehaviour
{
    private BridgePhysicsManager manager; 
    private BridgeMaterialSO material;
    private Point p1;
    private Point p2;
    private Bar myBar;
    
    private float restLength;
    private Joint[] joints; 
    private SpringJoint ropeJoint; 
    
    [HideInInspector] public bool isBroken = false;
    [HideInInspector] public float currentStressPercent = 0f;
    [HideInInspector] public float currentStructuralStressPercent = 0f;
    
    private float smoothedForce = 0f;
    private float settledDeadLoadForce = 0f;
    private bool canTrackStress = false; 
    private bool isCurrentlyInTension;

    private Queue<float> forceHistory = new Queue<float>();
    private Queue<float> settlingForceHistory = new Queue<float>();
    private int smoothingFrames = 10;

    private Renderer[] childRenderers;
    private Color[] originalColors;
    private bool stressVisualDirty;
    public Bar Bar => myBar;
    public string FailureCause { get; private set; }
    public float FailureForceNewtons { get; private set; }
    public string FailureDescription
    {
        get
        {
            string memberName = material != null ? material.GetDisplayName() : "Structural member";
            string cause = string.IsNullOrWhiteSpace(FailureCause) ? "excessive force" : FailureCause;
            float percent = Mathf.Max(1f, currentStructuralStressPercent) * 100f;
            return $"{memberName} failed from {cause.ToLowerInvariant()} at {percent:0.#}% of capacity.";
        }
    }

    /// <summary>
    /// Used by BridgePhysicsManager when a contract-level failure was detected
    /// before the sampled member managed to detach itself. BreakBar remains the
    /// single implementation responsible for releasing connections and visuals.
    /// </summary>
    public bool ForceBreakForFailure(string cause)
    {
        if (isBroken)
        {
            WakeFailureBodies();
            return true;
        }
        if (material == null || myBar == null) return false;

        CacheJointsIfNeeded();
        Joint sampledJoint = material.isRope
            ? ropeJoint
            : joints != null && joints.Length > 0 ? joints[0] : null;
        float limit = isCurrentlyInTension
            ? material.maxTension
            : material.GetCompressionLimit(restLength);
        float force = Mathf.Max(0f, limit) * Mathf.Max(1f, currentStructuralStressPercent);
        BreakBar(cause, force, sampledJoint);
        return isBroken;
    }

    public void WakeFailureBodies()
    {
        WakeVisualBodiesAtFailure();
    }

    public void Setup(BridgeMaterialSO mat, Point point1, Point point2)
    {
        manager = FindObjectOfType<BridgePhysicsManager>(); 
        material = mat;
        p1 = point1;
        p2 = point2;
        myBar = GetComponent<Bar>();
        smoothingFrames = manager != null ? Mathf.Max(1, manager.stressSmoothingFrames) : 10;
        
        restLength = Vector3.Distance(p1.transform.position, p2.transform.position);
        isCurrentlyInTension = false;
        settlingForceHistory.Clear();

        childRenderers = GetComponentsInChildren<Renderer>();
        originalColors = new Color[childRenderers.Length];
        
        for (int i = 0; i < childRenderers.Length; i++)
        {
            if (childRenderers[i].material.HasProperty("_Color"))
                originalColors[i] = childRenderers[i].material.color;
            else if (childRenderers[i].material.HasProperty("_BaseColor"))
                originalColors[i] = childRenderers[i].material.GetColor("_BaseColor");
            else
                originalColors[i] = Color.white;
        }

        if (mat.isRope && myBar != null)
            myBar.SetRopeSimulationVisual(true, restLength);
    }

    public void SetRopeJoint(SpringJoint joint)
    {
        ropeJoint = joint;
    }

    public void BeginTracking()
    {
        canTrackStress = true;
        CacheJointsIfNeeded();

        // The bridge has already settled for BridgePhysicsManager.settleFramesAmount
        // fixed steps. Average the final settling samples so one arbitrary PhysX
        // resting impulse cannot shift the live-load-only result for the whole run.
        // Fall back to a direct read when tracking is started without a settle phase.
        settledDeadLoadForce = settlingForceHistory.Count > 0
            ? AverageSamples(settlingForceHistory)
            : ReadCurrentForce();
        smoothedForce = settledDeadLoadForce;
        currentStressPercent = 0f;
        currentStructuralStressPercent = 0f;
        
        forceHistory.Clear();
        for (int i = 0; i < smoothingFrames; i++)
            forceHistory.Enqueue(settledDeadLoadForce);
    }

    public void SampleSettlingForce()
    {
        if (isBroken || p1 == null || p2 == null || material == null) return;

        CacheJointsIfNeeded();
        if (!material.isRope && (joints == null || joints.Length == 0)) return;

        settlingForceHistory.Enqueue(ReadCurrentForce());
        while (settlingForceHistory.Count > smoothingFrames)
            settlingForceHistory.Dequeue();
    }

    private static float AverageSamples(IEnumerable<float> samples)
    {
        float total = 0f;
        int count = 0;
        foreach (float sample in samples)
        {
            total += sample;
            count++;
        }

        return count > 0 ? total / count : 0f;
    }

    private void OnDestroy()
    {
        if (material != null && material.isRope && myBar != null)
            myBar.SetRopeSimulationVisual(false);
        if (childRenderers == null) return;
        for (int i = 0; i < childRenderers.Length; i++)
        {
            if (childRenderers[i] != null) SetBarColor(originalColors[i], i);
        }
    }

    private void LateUpdate()
    {
        // Several fixed ticks can run before one rendered frame on mobile.
        // Color the latest stress once per frame instead of writing materials
        // for intermediate values the player can never see.
        if (stressVisualDirty)
        {
            stressVisualDirty = false;
            if (!isBroken && manager != null && manager.enableVisualizer)
                UpdateStressVisuals();
        }

        if (material == null || !material.isRope || isBroken ||
            myBar == null || p1 == null || p2 == null) return;
        myBar.StartPosition = p1.transform.position;
        myBar.UpdateCreatingBar(p2.transform.position);
    }

    public void EvaluateStress(bool allowBreaking = true)
    {
        if (!canTrackStress || isBroken || p1 == null || p2 == null) return;

        CacheJointsIfNeeded();
        if (!material.isRope && (joints == null || joints.Length == 0)) return;

        float currentLength = Vector3.Distance(p1.transform.position, p2.transform.position);
        float lengthDelta = currentLength - restLength;
        float directionDeadZone = manager != null ? manager.stressDirectionDeadZone : 0.0005f;

        // Retain the previous state inside the dead zone. Without hysteresis,
        // microscopic solver jitter can swap dissimilar tension/compression limits.
        if (lengthDelta > directionDeadZone) isCurrentlyInTension = true;
        else if (lengthDelta < -directionDeadZone) isCurrentlyInTension = false;

        bool isTension = isCurrentlyInTension;
        float maxForceThisFrame = ReadCurrentForce();
        
        Joint breakingJoint = null;
        string breakCause = "";

        forceHistory.Enqueue(maxForceThisFrame);
        if (forceHistory.Count > smoothingFrames) forceHistory.Dequeue();

        float averagedForce = AverageSamples(forceHistory);

        float relativeTolerance = manager != null ? manager.deadLoadReturnTolerance : 0.02f;
        float minimumTolerance = manager != null ? manager.deadLoadReturnToleranceNewtons : 1f;
        float returnTolerance = Mathf.Max(minimumTolerance, settledDeadLoadForce * relativeTolerance);

        // PhysX resting contacts can fluctuate slightly forever. Snap only values
        // already within a narrow band of the calibrated dead load; real residual
        // deformation or oscillation is deliberately not hidden.
        smoothedForce = Mathf.Abs(averagedForce - settledDeadLoadForce) <= returnTolerance
            ? settledDeadLoadForce
            : averagedForce;

        float tensionLimit = material.maxTension;
        float compressionLimit = material.GetCompressionLimit(restLength);

        if (material.isRope)
        {
            if (isTension && smoothedForce >= tensionLimit)
            {
                breakingJoint = ropeJoint;
                breakCause = "Tension (Rope Snapped)";
            }
        }
        else
        {
            if (isTension && smoothedForce >= tensionLimit)
            {
                breakingJoint = joints[0]; 
                breakCause = "Tension (Pulled apart)";
            }
            else if (!isTension && smoothedForce >= compressionLimit)
            {
                breakingJoint = joints[0];
                breakCause = material.isPier
                    ? "Compression (Pier Buckled)"
                    : "Compression (Buckled)";
            }
        }

        float stressLimit = isTension ? tensionLimit : compressionLimit;
        if (stressLimit <= 0f) stressLimit = 1f; 

        float totalStructuralForce = material.isRope && !isTension ? 0f : smoothedForce;
        currentStructuralStressPercent =
            Mathf.Round((totalStructuralForce / stressLimit) * 1000f) / 1000f;

        if (material.isRope && !isTension) 
        {
            currentStressPercent = 0f; 
        }
        else 
        {
            float displayedForce = manager != null && manager.displayLiveLoadStressOnly
                ? Mathf.Max(0f, smoothedForce - settledDeadLoadForce)
                : smoothedForce;
            float rawPercent = displayedForce / stressLimit;
            currentStressPercent = Mathf.Round(rawPercent * 1000f) / 1000f;
        }

        if (manager != null && manager.enableVisualizer)
        {
            stressVisualDirty = true;
        }

        if (allowBreaking && breakingJoint != null && !isBroken && !BridgePhysicsManager.DebugInvincibleBridge)
        {
            BreakBar(breakCause, smoothedForce, breakingJoint);
        }
    }

    public void ApplyDeterministicStress(
        float displayedRatio,
        float structuralRatio,
        bool isTension,
        bool allowBreaking = true)
    {
        if (!canTrackStress || isBroken || material == null) return;

        isCurrentlyInTension = isTension;
        currentStressPercent = Mathf.Max(0f, displayedRatio);
        currentStructuralStressPercent = Mathf.Max(0f, structuralRatio);

        if (manager != null && manager.enableVisualizer) stressVisualDirty = true;
        if (!allowBreaking || currentStructuralStressPercent < 1f ||
            BridgePhysicsManager.DebugInvincibleBridge) return;

        CacheJointsIfNeeded();
        Joint breakingJoint = material.isRope
            ? ropeJoint
            : joints != null && joints.Length > 0 ? joints[0] : null;

        float limit = isTension ? material.maxTension : material.GetCompressionLimit(restLength);
        string cause = material.isRope
            ? "Tension (Rope Snapped)"
            : isTension
                ? "Tension (Pulled apart)"
                : material.isPier ? "Compression (Pier Buckled)" : "Compression (Buckled)";
        BreakBar(cause, Mathf.Max(0f, limit) * currentStructuralStressPercent, breakingJoint);
    }

    private void CacheJointsIfNeeded()
    {
        if (material != null && !material.isRope && (joints == null || joints.Length == 0))
            joints = GetComponents<Joint>();
    }

    private float ReadCurrentForce()
    {
        if (material == null) return 0f;

        if (material.isRope)
            return ropeJoint != null ? ropeJoint.currentForce.magnitude : 0f;

        float maximumForce = 0f;
        if (joints == null) return maximumForce;

        foreach (Joint joint in joints)
        {
            if (joint == null) continue;
            maximumForce = Mathf.Max(maximumForce, joint.currentForce.magnitude);
        }

        return maximumForce;
    }

    private void UpdateStressVisuals()
    {
        if (childRenderers == null || childRenderers.Length == 0) return;

        for (int i = 0; i < childRenderers.Length; i++)
        {
            Color stressColor;

            if (currentStressPercent < 0.5f)
            {
                stressColor = Color.Lerp(originalColors[i], manager.warningColor, currentStressPercent * 2f);
            }
            else
            {
                stressColor = Color.Lerp(manager.warningColor, manager.criticalColor, (currentStressPercent - 0.5f) * 2f);
            }

            SetBarColor(stressColor, i);
        }
    }

    private void BreakBar(string cause, float force, Joint brokenJoint)
    {
        if (isBroken) return;
        isBroken = true;
        FailureCause = cause;
        FailureForceNewtons = Mathf.Max(0f, force);
        if (manager != null) manager.RecordBrokenPart(this);
        currentStressPercent = Mathf.Max(1f, currentStressPercent);
        currentStructuralStressPercent = Mathf.Max(1f, currentStructuralStressPercent);

        ReleaseAllFailedMemberConnections(brokenJoint);
        WakeVisualBodiesAtFailure();
        
        for (int i = 0; i < childRenderers.Length; i++) SetBarColor(manager.brokenColor, i);
        
        if (material.isRope && myBar != null)
        {
            myBar.StartPosition = p1.transform.position;
            myBar.UpdateCreatingBar(p1.transform.position + (Vector3.down * restLength));
        }
    }

    /// <summary>
    /// A black member represents a complete structural failure. Release every
    /// connection at both endpoints so it can no longer transfer load through a
    /// hidden surviving hinge. Adjacent intact members remain joined to their
    /// shared Point; only the failed member is detached.
    /// </summary>
    private void ReleaseAllFailedMemberConnections(Joint failedJoint)
    {
        if (material != null && material.isRope)
        {
            Joint ropeConnection = ropeJoint != null ? ropeJoint : failedJoint;
            if (ropeConnection != null) DestroyImmediate(ropeConnection);
            ropeJoint = null;
            return;
        }

        // Solid and dual-beam joints are authored on the Bar itself. Destroying
        // the complete set releases both endpoints and both parallel rails.
        foreach (Joint joint in GetComponentsInChildren<Joint>(true))
        {
            if (joint != null) DestroyImmediate(joint);
        }

        // Defensive fallback for a legacy setup whose sampled joint is not on
        // the Bar root.
        if (failedJoint != null && failedJoint != ropeJoint)
            DestroyImmediate(failedJoint);
        joints = Array.Empty<Joint>();
    }

    private void WakeVisualBodiesAtFailure()
    {
        // Destroying a joint does not always wake every body in a settled
        // island. Let gravity immediately animate the detached member and the
        // still-connected neighbors; deterministic breakage was decided above.
        Rigidbody failedBody = GetComponent<Rigidbody>();
        if (failedBody != null)
        {
            // Simulation bars are normally dynamic already. This fallback covers
            // legacy or partially authored members that would otherwise remain
            // suspended after their joints were removed.
            if (failedBody.isKinematic && manager != null && manager.IsSimulationActive)
            {
                failedBody.isKinematic = false;
                failedBody.useGravity = true;
            }
            if (!failedBody.isKinematic) failedBody.WakeUp();
        }

        WakePointAndNeighbors(p1);
        if (p2 != p1) WakePointAndNeighbors(p2);
    }

    private void WakePointAndNeighbors(Point point)
    {
        if (point == null) return;
        Rigidbody pointBody = point.GetComponent<Rigidbody>();
        if (pointBody != null && !pointBody.isKinematic) pointBody.WakeUp();

        foreach (Bar neighbor in point.ConnectedBars)
        {
            if (neighbor == null || neighbor == myBar) continue;
            Rigidbody neighborBody = neighbor.GetComponent<Rigidbody>();
            if (neighborBody != null && !neighborBody.isKinematic)
                neighborBody.WakeUp();
        }
    }

    private void SetBarColor(Color targetColor, int index)
    {
        if (childRenderers[index] == null) return;

        if (childRenderers[index].material.HasProperty("_Color"))
        {
            childRenderers[index].material.color = targetColor;
        }
        else if (childRenderers[index].material.HasProperty("_BaseColor"))
        {
            childRenderers[index].material.SetColor("_BaseColor", targetColor);
        }
    }
}
