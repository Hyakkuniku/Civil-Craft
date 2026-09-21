using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;
using TMPro;

[DefaultExecutionOrder(-40)] 
[RequireComponent(typeof(Rigidbody))]
public class LiveLoadVehicle : Interactable 
{
    [Header("Vehicle Information UI")]
    public string vehicleName = "Heavy Transport";
    public GameObject vehicleInfoPanel;
    public TextMeshProUGUI vehicleNameText;
    public TextMeshProUGUI vehicleWeightText;
    public TextMeshProUGUI vehicleSpeedText;

    [Header("Gameplay Elements to Hide")]
    public List<GameObject> uiElementsToHide = new List<GameObject>();
    private List<GameObject> temporarilyHiddenPanels = new List<GameObject>();

    [Header("Open World Settings")]
    public ContractSO assignedContract; 
    private readonly List<VehicleCargoSlot> cargoSlots = new List<VehicleCargoSlot>();
    [Header("Smart Cargo Loading")]
    [Tooltip("Creates one comfortable truck-level loading interaction instead of requiring the player to reach each small slot.")]
    [SerializeField] private bool useSmartCargoLoadingZone = true;
    [Min(0.5f), Tooltip("Extra horizontal reach added around the rendered truck bounds.")]
    [SerializeField] private float cargoLoadingZonePadding = 1.5f;
    private VehicleCargoLoadingZone smartCargoLoadingZone;
    public bool UsesSmartCargoLoadingZone => smartCargoLoadingZone != null && smartCargoLoadingZone.isActiveAndEnabled;
    public bool AllowsCargo => assignedContract != null &&
        assignedContract.liveLoadMode == ContractSO.LiveLoadMode.Vehicle && assignedContract.allowVehicleCargo;
    public bool CanChangeCargo => isActiveAndEnabled && AllowsCargo && !isDriving &&
        (physicsManager == null || !physicsManager.IsSimulationActive) &&
        (GameManager.Instance == null || GameManager.Instance.CurrentState == GameManager.GameState.Normal);
    public int LoadedCargoCount
    {
        get
        {
            int count = 0;
            foreach (var slot in cargoSlots)
                if (slot != null && slot.LoadedCargo != null && slot.LoadedCargo.gameObject.activeInHierarchy) count++;
            return count;
        }
    }
    public float PayloadWeight
    {
        get
        {
            if (!AllowsCargo) return 0f;
            float total = 0f;
            foreach (var slot in cargoSlots) if (slot != null) total += slot.LoadedWeight;
            return total;
        }
    }
    public float TotalTestWeight => Mathf.Max(0.01f,
        (assignedContract != null ? assignedContract.liveLoadWeight : vehicleMass) + PayloadWeight);
    public void RegisterCargoSlot(VehicleCargoSlot slot)
    {
        if (slot != null && !cargoSlots.Contains(slot)) cargoSlots.Add(slot);
    }

    public bool TryGetCompatibleCargoSlot(CargoItem cargo, out VehicleCargoSlot result)
    {
        result = null;
        if (cargo == null || cargo.playerCargoContract != assignedContract) return false;

        // An explicitly authored cargo-to-slot match always wins.
        foreach (VehicleCargoSlot slot in cargoSlots)
        {
            if (slot != null && slot.acceptedCargo == cargo && slot.CanAccept(cargo))
            {
                result = slot;
                return true;
            }
        }

        // Generic empty slots are the safe fallback.
        foreach (VehicleCargoSlot slot in cargoSlots)
        {
            if (slot != null && slot.acceptedCargo == null && slot.CanAccept(cargo))
            {
                result = slot;
                return true;
            }
        }
        return false;
    }

    public bool TryLoadCargoFromSmartZone(CargoItem cargo)
    {
        return TryGetCompatibleCargoSlot(cargo, out VehicleCargoSlot slot) && slot.TryLoadCargo(cargo);
    }
    public void RefreshCargoMass()
    {
        if (rb != null) rb.mass = TotalTestWeight;
    }

    [Header("Cargo Completion Events")]
    [Tooltip("Invoked once when a player loading action reaches this contract's Minimum Loaded Cargo count.")]
    [SerializeField] private UnityEvent onRequiredCargoLoaded;

    public event Action RequiredCargoLoaded;
    private bool requiredCargoLoadedInvoked;

    /// <summary>
    /// Called by a cargo slot after it has successfully secured an item. Save
    /// restoration deliberately uses RefreshCargoMass instead, so loading a
    /// scene cannot replay the event and move an NPC backwards.
    /// </summary>
    public void NotifyCargoLoaded()
    {
        RefreshCargoMass();
        if (requiredCargoLoadedInvoked || !AllowsCargo || assignedContract.minimumLoadedCargo <= 0 ||
            LoadedCargoCount < assignedContract.minimumLoadedCargo) return;

        requiredCargoLoadedInvoked = true;
        AdvanceCargoNPCPhase();
        onRequiredCargoLoaded?.Invoke();
        RequiredCargoLoaded?.Invoke();
    }

    private void AdvanceCargoNPCPhase()
    {
        if (string.IsNullOrWhiteSpace(assignedContract.cargoLoadedNPCPhaseId)) return;

        NPCProgressionManager npc = NPCProgressionManager.FindByProgressionSaveId(
            assignedContract.cargoUnlockProgressionId);
        if (npc == null)
        {
            Debug.LogWarning(
                $"[Vehicle Cargo] Required cargo is loaded, but no active NPC uses progression ID " +
                $"'{assignedContract.cargoUnlockProgressionId}'.", this);
            return;
        }

        npc.MoveToPhaseById(assignedContract.cargoLoadedNPCPhaseId);
    }
    public static LiveLoadVehicle FindActiveForContract(ContractSO contract)
    {
        if (contract == null) return null;
        foreach (var vehicle in FindObjectsOfType<LiveLoadVehicle>())
            if (vehicle.isActiveAndEnabled && vehicle.assignedContract == contract) return vehicle;
        return null;
    }
    public static float GetContractTestWeight(ContractSO contract)
    {
        if (contract == null) return 1000f;
        if (contract.liveLoadMode != ContractSO.LiveLoadMode.Vehicle || !contract.allowVehicleCargo)
            return contract.liveLoadWeight;
        LiveLoadVehicle vehicle = FindActiveForContract(contract);
        return vehicle != null ? vehicle.TotalTestWeight : contract.liveLoadWeight;
    }

    // --- NEW: Tutorial Integration ---
    [Header("Tutorial Settings")]
    [Tooltip("If checked, closing the Info Panel will automatically advance the active tutorial.")]
    public bool advancesTutorial = false;

    [Header("Inspection Events")]
    [Tooltip("Invoked only after an inspection window that was actually open is closed.")]
    [SerializeField] private UnityEvent onInspectionWindowClosed;

    public event Action InspectionWindowClosed;

    [Header("Path Settings")]
    public Transform startPoint;
    public Transform endPoint;
    [Tooltip("When enabled, bridge testing teleports the cart to Start. Disable this when the cart's authored scene position is already the intended starting pose.")]
    [SerializeField] private bool resetToStartPointBeforeSimulation = true;
    [Tooltip("Use marker rotations when resetting or loading a parked vehicle. Disable to preserve the scene-authored facing.")]
    [SerializeField] private bool useWaypointRotation = true;

    [Header("Engine & Chassis")]
    public float maxSpeed = 5f;
    public float engineTorque = 1500f; 
    public float vehicleMass = 1000f;
    public float centerOfMassOffset = -0.5f; 

    [Header("Custom Wheel Setup")]
    public GameObject[] wheelObjects;
    public float wheelRadius = 0.4f;
    public float wheelMass = 50f;
    public Vector3 spinAxis = new Vector3(1, 0, 0); 

    [Header("Finish Braking")]
    [Min(0f)] public float brakeTorque = 3000f;
    [Min(0.001f)] public float stoppedLinearSpeed = 0.08f;
    [Min(0.001f)] public float stoppedAngularSpeed = 0.15f;
    [Min(0f)] public float wheelGroundCheckDistance = 0.12f;
    [Min(0f)] public float requiredSettledTime = 0.5f;

    [Header("NPC Avoidance")]
    [Tooltip("Adds a moving NavMeshObstacle so NavMeshAgents steer around the vehicle.")]
    [SerializeField] private bool configureNPCObstacle = true;
    [Min(0f)] [SerializeField] private float npcObstaclePadding = 0.2f;
    [SerializeField] private NavMeshObstacle npcObstacle;

    [Header("System")]
    public BridgePhysicsManager physicsManager;

    private Rigidbody rb;
    private bool isDriving = false;
    private bool hasReachedEnd = false; 
    private bool isBrakingAtFinish = false;
    private float settledAtFinishTimer = 0f;

    [HideInInspector] public bool isParkedAtFinish = false;
    public bool IsFinishBraking => isBrakingAtFinish;
    public bool HasReachedEnd => hasReachedEnd;
    public bool IsDriving => isDriving;
    public float CurrentSpeed => rb != null && !rb.isKinematic ? rb.velocity.magnitude : 0f;
    public float NormalizedRouteProgress
    {
        get
        {
            if (startPoint == null || endPoint == null) return 0f;
            GetWaypointPose(startPoint, out Vector3 routeStart, out _);
            GetWaypointPose(endPoint, out Vector3 routeEnd, out _);
            Vector3 route = routeEnd - routeStart;
            if (route.sqrMagnitude < 0.0001f) return 0f;
            return Mathf.Clamp01(Vector3.Dot(transform.position - routeStart, route) / route.sqrMagnitude);
        }
    }
    
    private float currentMotorSpeed = 0f;
    private PhysicMaterial wheelMat; 
    private bool isInspectionWindowOpen;
    private bool inspectionUsesPanelCoordinator;
    private static LiveLoadVehicle activeInspectionVehicle;
    private Vector3 authoredStartPosition;
    private Quaternion authoredStartRotation;
    private Vector3 authoredStartPointLocalPosition;
    private Quaternion authoredStartPointLocalRotation = Quaternion.identity;
    private bool hasAuthoredStartPointPose;
    private bool hideWhenBuildModeCloses;
    private bool visibleForBuildReplay;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetInspectionSession()
    {
        activeInspectionVehicle = null;
    }

    private class WheelData
    {
        public GameObject physObj;
        public Rigidbody rb;
        public HingeJoint hinge;
        public Vector3 originalLocalPos;
        public Quaternion originalLocalRot;
    }
    private List<WheelData> wheels = new List<WheelData>();

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        authoredStartPosition = transform.position;
        authoredStartRotation = transform.rotation;
        CaptureAuthoredStartPointPose();
        
        rb.mass = vehicleMass;
        rb.isKinematic = true; 
        rb.useGravity = true; 
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        rb.centerOfMass = new Vector3(0, centerOfMassOffset, 0);
        rb.constraints = RigidbodyConstraints.FreezeRotationY | RigidbodyConstraints.FreezePositionZ;
        rb.sleepThreshold = 0f;
        rb.maxDepenetrationVelocity = 10f; 

        // Imported vehicle models often put their chassis collider on a child mesh.
        Collider chassisCol = GetComponent<Collider>() ?? GetComponentInChildren<Collider>();
        if (chassisCol != null)
        {
            PhysicMaterial slipMat = new PhysicMaterial("ChassisSlip");
            slipMat.dynamicFriction = 0f; slipMat.staticFriction = 0f; slipMat.bounciness = 0f;
            chassisCol.material = slipMat;
        }

        ConfigureNPCNavMeshObstacle(chassisCol);

        wheelMat = new PhysicMaterial("WheelGrip");
        wheelMat.dynamicFriction = 1f; wheelMat.staticFriction = 1f; 
        wheelMat.frictionCombine = PhysicMaterialCombine.Maximum; wheelMat.bounciness = 0f;

        foreach (GameObject visualWheel in wheelObjects)
        {
            if (visualWheel == null) continue;

            Renderer rend = visualWheel.GetComponentInChildren<Renderer>();
            if (rend == null) continue;
            Vector3 trueCenter = rend.bounds.center;

            GameObject physWheel = new GameObject(visualWheel.name + "_PhysicsAxle");
            physWheel.transform.position = trueCenter;
            physWheel.transform.rotation = visualWheel.transform.rotation;
            physWheel.transform.SetParent(transform);
            visualWheel.transform.SetParent(physWheel.transform, true);

            WheelData wd = new WheelData();
            wd.physObj = physWheel;
            wd.originalLocalPos = physWheel.transform.localPosition;
            wd.originalLocalRot = physWheel.transform.localRotation;

            Collider oldCol = visualWheel.GetComponent<Collider>();
            if (oldCol != null) Destroy(oldCol);

            SphereCollider sc = physWheel.AddComponent<SphereCollider>();
            sc.radius = wheelRadius; sc.material = wheelMat;

            if (chassisCol != null) Physics.IgnoreCollision(chassisCol, sc, true);

            wheels.Add(wd);
        }

        if (physicsManager == null) physicsManager = FindObjectOfType<BridgePhysicsManager>();
        if (vehicleInfoPanel != null) vehicleInfoPanel.SetActive(false);
    }

    private void ConfigureNPCNavMeshObstacle(Collider chassisCollider)
    {
        if (!configureNPCObstacle) return;

        if (npcObstacle == null)
            npcObstacle = GetComponent<NavMeshObstacle>();
        if (npcObstacle == null)
            npcObstacle = gameObject.AddComponent<NavMeshObstacle>();

        npcObstacle.shape = NavMeshObstacleShape.Box;

        if (chassisCollider is BoxCollider boxCollider)
        {
            npcObstacle.center = boxCollider.center;
            npcObstacle.size = boxCollider.size + Vector3.one * (npcObstaclePadding * 2f);
        }
        else if (chassisCollider != null)
        {
            Vector3 scale = transform.lossyScale;
            Vector3 worldSize = chassisCollider.bounds.size;
            npcObstacle.center = transform.InverseTransformPoint(chassisCollider.bounds.center);
            npcObstacle.size = new Vector3(
                worldSize.x / Mathf.Max(Mathf.Abs(scale.x), 0.0001f),
                worldSize.y / Mathf.Max(Mathf.Abs(scale.y), 0.0001f),
                worldSize.z / Mathf.Max(Mathf.Abs(scale.z), 0.0001f)) +
                Vector3.one * (npcObstaclePadding * 2f);
        }

        // Moving obstacles should use local avoidance. Carving a moving physics
        // vehicle would repeatedly rebuild holes and destabilize agent paths.
        npcObstacle.carving = false;
        npcObstacle.carveOnlyStationary = true;
    }

    private void Start()
    {
        CreateSmartCargoLoadingZone();

        if (physicsManager != null)
        {
            physicsManager.OnSettlePhaseStarted += HandleSettlePhaseStarted;
            physicsManager.OnSimulationStarted += HandleSimulationStarted;
            physicsManager.OnSimulationStopped += HandleSimulationStopped;
        }

        // PlayerDataManager loads persistent data in Awake, before any Start call,
        // so completed live loads can be hidden before the first rendered frame.
        if (!visibleForBuildReplay && HasSavedBridgeForAssignedContract())
        {
            HideForSavedBridge();
        }
    }

    private void CreateSmartCargoLoadingZone()
    {
        if (!useSmartCargoLoadingZone || !AllowsCargo || cargoSlots.Count == 0) return;

        smartCargoLoadingZone = GetComponentInChildren<VehicleCargoLoadingZone>(true);
        if (smartCargoLoadingZone != null)
        {
            smartCargoLoadingZone.Initialize(this);
            return;
        }

        Bounds localBounds = CalculateLocalCargoSlotBounds();
        float padding = Mathf.Max(0.5f, cargoLoadingZonePadding);
        GameObject zoneObject = new GameObject("Smart Cargo Loading Zone (Runtime)");
        zoneObject.hideFlags = HideFlags.DontSave;
        zoneObject.transform.SetParent(transform, false);
        zoneObject.transform.localPosition = localBounds.center;
        Vector3 inheritedScale = transform.lossyScale;
        zoneObject.transform.localScale = new Vector3(
            SafeScaleReciprocal(inheritedScale.x),
            SafeScaleReciprocal(inheritedScale.y),
            SafeScaleReciprocal(inheritedScale.z));
        int interactableLayer = LayerMask.NameToLayer("Interactable");
        zoneObject.layer = interactableLayer >= 0 ? interactableLayer : gameObject.layer;

        Vector3 slotSpanInWorld = new Vector3(
            Mathf.Abs(localBounds.size.x * inheritedScale.x),
            Mathf.Abs(localBounds.size.y * inheritedScale.y),
            Mathf.Abs(localBounds.size.z * inheritedScale.z));
        BoxCollider trigger = zoneObject.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.center = Vector3.zero;
        trigger.size = new Vector3(
            Mathf.Clamp(slotSpanInWorld.x + padding * 2f, 2f, 8f),
            Mathf.Clamp(slotSpanInWorld.y + 1f, 2.5f, 5f),
            Mathf.Clamp(slotSpanInWorld.z + padding * 2f, 3f, 8f));
        smartCargoLoadingZone = zoneObject.AddComponent<VehicleCargoLoadingZone>();
        smartCargoLoadingZone.Initialize(this);
    }

    private Bounds CalculateLocalCargoSlotBounds()
    {
        bool found = false;
        Bounds bounds = new Bounds(Vector3.zero, new Vector3(2f, 2.5f, 3f));
        foreach (VehicleCargoSlot slot in cargoSlots)
        {
            if (slot == null) continue;
            Vector3 localPoint = transform.InverseTransformPoint(slot.transform.position);
            if (!found) { bounds = new Bounds(localPoint, Vector3.zero); found = true; }
            else bounds.Encapsulate(localPoint);
        }
        if (!found) return bounds;

        return bounds;
    }

    private static float SafeScaleReciprocal(float scale)
    {
        return 1f / Mathf.Max(0.0001f, Mathf.Abs(scale));
    }

    private void OnDestroy()
    {
        if (activeInspectionVehicle == this)
            activeInspectionVehicle = null;

        if (physicsManager != null)
        {
            physicsManager.OnSettlePhaseStarted -= HandleSettlePhaseStarted;
            physicsManager.OnSimulationStarted -= HandleSimulationStarted;
            physicsManager.OnSimulationStopped -= HandleSimulationStopped;
        }
    }

    private void BuildWheelPhysics()
    {
        Collider chassisCol = GetComponent<Collider>() ?? GetComponentInChildren<Collider>();

        foreach (var w in wheels)
        {
            if (w.rb == null)
            {
                w.rb = w.physObj.AddComponent<Rigidbody>();
                w.rb.mass = wheelMass; 
                w.rb.isKinematic = true; 
                w.rb.interpolation = RigidbodyInterpolation.Interpolate;
                w.rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                w.rb.sleepThreshold = 0f; 
                w.rb.maxDepenetrationVelocity = 10f; 
            }

            ApplySimulationSolverSettings(w.rb);

            if (w.hinge == null)
            {
                w.hinge = w.physObj.AddComponent<HingeJoint>();
                w.hinge.connectedBody = rb;
                w.hinge.axis = spinAxis; 
                
                JointMotor motor = w.hinge.motor;
                motor.force = engineTorque; 
                motor.freeSpin = false;
                w.hinge.motor = motor; 
                w.hinge.useMotor = false;
            }
            
            Collider wheelCol = w.physObj.GetComponent<Collider>();
            if (chassisCol != null && wheelCol != null) Physics.IgnoreCollision(chassisCol, wheelCol, true);
        }
    }

    private void StripWheelPhysics()
    {
        foreach (var w in wheels)
        {
            // The replacement wheel rig is built immediately after this method.
            // Deferred Destroy can leave the old and new physics components in the
            // same simulation step, making repeat tests depend on render timing.
            if (w.hinge != null)
            {
                w.hinge.connectedBody = null;
                DestroyImmediate(w.hinge);
                w.hinge = null;
            }

            if (w.rb != null)
            {
                ClearDynamicVelocity(w.rb);
                w.rb.isKinematic = true;
                DestroyImmediate(w.rb);
                w.rb = null;
            }
        }
    }

    private void HandleSettlePhaseStarted()
    {
        if (IsPlayerCargoContract()) return;
        if (GameManager.Instance != null && assignedContract != null && GameManager.Instance.CurrentContract != assignedContract) return;

        hasReachedEnd = false; 
        isParkedAtFinish = false; 
        isBrakingAtFinish = false;
        settledAtFinishTimer = 0f;
        currentMotorSpeed = 0f; 

        RefreshCargoMass();

        StripWheelPhysics(); 

        ClearDynamicVelocity(rb);
        rb.isKinematic = true;
        ApplySimulationSolverSettings(rb);

        ResetToSimulationStartPose();

        BuildWheelPhysics(); 

        
        rb.ResetCenterOfMass();
        rb.centerOfMass = new Vector3(0, centerOfMassOffset, 0);
        rb.ResetInertiaTensor(); 
        
        foreach (var w in wheels)
        {
            ClearDynamicVelocity(w.rb);
            w.rb.ResetCenterOfMass();
            w.rb.ResetInertiaTensor();
        }

        Physics.SyncTransforms(); 
    }

    private void ApplySimulationSolverSettings(Rigidbody body)
    {
        if (body == null) return;

        int positionIterations = physicsManager != null
            ? Mathf.Max(1, physicsManager.physicsSolverIterations)
            : Mathf.Max(1, Physics.defaultSolverIterations);

        body.solverIterations = positionIterations;
        body.solverVelocityIterations = Mathf.Max(1, Physics.defaultSolverVelocityIterations);
    }

    private void HandleSimulationStarted()
    {
        if (IsPlayerCargoContract()) return;
        if (GameManager.Instance != null && assignedContract != null && GameManager.Instance.CurrentContract != assignedContract) return;
        
        rb.isKinematic = false;
        ClearDynamicVelocity(rb);
        rb.WakeUp();
        foreach (var w in wheels)
        {
            if (w.rb == null) continue;
            w.rb.isKinematic = false;
            ClearDynamicVelocity(w.rb);
            w.rb.WakeUp();
        }
        
        isBrakingAtFinish = false;
        settledAtFinishTimer = 0f;
        isDriving = true; 
    }

    private void HandleSimulationStopped()
    {
        if (IsPlayerCargoContract()) return;
        StopAndReset();
    }

    private static bool IsPlayerCargoContract()
    {
        return GameManager.Instance != null && GameManager.Instance.CurrentContract != null &&
            GameManager.Instance.CurrentContract.liveLoadMode == ContractSO.LiveLoadMode.PlayerCarriedCargo;
    }

    private void Update()
    {
        if (hideWhenBuildModeCloses &&
            (GameManager.Instance == null ||
             (!GameManager.Instance.IsInBuildMode() && !GameManager.Instance.IsTransitioning)))
        {
            HideForSavedBridge();
            return;
        }

        promptMessage = "Inspect " + vehicleName;
    }

    /// <summary>
    /// Called by Save & Continue after bridge data has been persisted. The actual
    /// hide waits for the build transition to finish so GameManager cannot restore
    /// the vehicle from its pre-build active-state snapshot.
    /// </summary>
    public static void ScheduleHideForSavedContract(ContractSO contract)
    {
        if (!IsVehicleContract(contract)) return;

        foreach (LiveLoadVehicle vehicle in FindLoadedVehicles())
        {
            if (!MatchesContract(vehicle.assignedContract, contract)) continue;
            vehicle.visibleForBuildReplay = false;
            vehicle.hideWhenBuildModeCloses = true;

            if (GameManager.Instance == null ||
                (!GameManager.Instance.IsInBuildMode() && !GameManager.Instance.IsTransitioning))
                vehicle.HideForSavedBridge();
        }
    }

    /// <summary>Restores only the live load belonging to the bridge being redesigned.</summary>
    public static void ShowForContractReplay(ContractSO contract)
    {
        if (!IsVehicleContract(contract)) return;

        foreach (LiveLoadVehicle vehicle in FindLoadedVehicles())
        {
            if (!MatchesContract(vehicle.assignedContract, contract)) continue;
            vehicle.visibleForBuildReplay = true;
            vehicle.hideWhenBuildModeCloses = false;
            if (!vehicle.gameObject.activeSelf) vehicle.gameObject.SetActive(true);
            vehicle.ResetForBuildReplay();
        }
    }

    private void HideForSavedBridge()
    {
        hideWhenBuildModeCloses = false;
        visibleForBuildReplay = false;

        if (isInspectionWindowOpen) CloseInfoPanelInternal(false);
        ResetForBuildReplay();
        if (npcObstacle != null) npcObstacle.enabled = false;
        gameObject.SetActive(false);
    }

    private void ResetForBuildReplay()
    {
        isDriving = false;
        hasReachedEnd = false;
        isParkedAtFinish = false;
        isBrakingAtFinish = false;
        settledAtFinishTimer = 0f;
        currentMotorSpeed = 0f;

        if (rb != null)
        {
            ClearDynamicVelocity(rb);
            rb.isKinematic = true;
            ResetToSimulationStartPose();
            StripWheelPhysics();
            rb.Sleep();
        }

        if (npcObstacle != null) npcObstacle.enabled = configureNPCObstacle;
        RefreshCargoMass();
        Physics.SyncTransforms();
    }

    private bool HasSavedBridgeForAssignedContract()
    {
        return IsVehicleContract(assignedContract) &&
               PlayerDataManager.Instance != null &&
               PlayerDataManager.Instance.HasValidSavedBridge(assignedContract.ContractID);
    }

    private static bool IsVehicleContract(ContractSO contract)
    {
        return contract != null && contract.liveLoadMode == ContractSO.LiveLoadMode.Vehicle;
    }

    private static bool MatchesContract(ContractSO left, ContractSO right)
    {
        if (left == null || right == null) return false;
        if (left == right) return true;
        return !string.IsNullOrWhiteSpace(left.ContractID) &&
               string.Equals(left.ContractID, right.ContractID, StringComparison.Ordinal);
    }

    private static IEnumerable<LiveLoadVehicle> FindLoadedVehicles()
    {
        foreach (LiveLoadVehicle vehicle in Resources.FindObjectsOfTypeAll<LiveLoadVehicle>())
        {
            if (vehicle != null && vehicle.gameObject.scene.IsValid() &&
                vehicle.gameObject.scene.isLoaded)
                yield return vehicle;
        }
    }

    protected override void Intract()
    {
        LessonTrigger lessonTrigger = GetComponent<LessonTrigger>();
        if (lessonTrigger != null && lessonTrigger.ReplaceExistingInteraction &&
            lessonTrigger.TryShowLesson())
        {
            return;
        }

        if (vehicleInfoPanel != null)
        {
            // Multiple vehicles can intentionally share one inspection panel. The
            // panel's serialized Close button may point at Vehicle 1, so remember
            // which vehicle actually opened it and route Close to that instance.
            activeInspectionVehicle = this;

            VehicleInspectionPanel inspectionPanel = vehicleInfoPanel.GetComponent<VehicleInspectionPanel>();
            if (inspectionPanel != null)
                inspectionPanel.ApplyLayout(vehicleNameText, vehicleWeightText, vehicleSpeedText);

            if (vehicleNameText != null) vehicleNameText.text = vehicleName;
            
            float displayWeight = TotalTestWeight;
            if (vehicleWeightText != null)
            {
                vehicleWeightText.text = AllowsCargo
                    ? $"<size=20><color=#9B6A3E>LIVE LOAD</color></size>\n" +
                      $"<size=42><b>{displayWeight:N0} kg</b></size>\n" +
                      $"<size=19><color=#6B4A36>Vehicle {vehicleMass:N0} kg  \u2022  Cargo {PayloadWeight:N0} kg</color></size>"
                    : $"<size=20><color=#9B6A3E>LIVE LOAD</color></size>\n" +
                      $"<size=42><b>{displayWeight:N0} kg</b></size>\n" +
                      "<size=19><color=#6B4A36>Required test weight</color></size>";
            }

            if (vehicleSpeedText != null)
            {
                vehicleSpeedText.text =
                    "<size=20><color=#9B6A3E>TOP SPEED</color></size>\n" +
                    $"<size=42><b>{maxSpeed:0.#} m/s</b></size>\n" +
                    "<size=19><color=#6B4A36>Simulation travel speed</color></size>";
            }

            inspectionUsesPanelCoordinator = UIPanelCoordinator.Instance != null;
            temporarilyHiddenPanels.Clear();
            if (inspectionUsesPanelCoordinator)
            {
                UIPanelCoordinator.Instance.OpenPanel(vehicleInfoPanel);
            }
            else
            {
                foreach (GameObject ui in uiElementsToHide)
                {
                    if (ui != null && ui.activeSelf)
                    {
                        temporarilyHiddenPanels.Add(ui);
                        ui.SetActive(false);
                    }
                }

                vehicleInfoPanel.SetActive(true);
            }
            isInspectionWindowOpen = true;

            InputManager inputObj = FindObjectOfType<InputManager>();
            if (inputObj != null) { inputObj.SetPlayerInputEnable(false); inputObj.SetLookEnabled(false); }

            PlayerMotor player = FindObjectOfType<PlayerMotor>();
            if (player != null) player.enabled = false;
        }
    }

    public void CloseInfoPanel()
    {
        // A shared panel can have one persistent UnityEvent target. Always close
        // the vehicle that most recently opened the panel, regardless of which
        // vehicle happens to be assigned to that serialized button event.
        if (activeInspectionVehicle != null && activeInspectionVehicle != this)
        {
            activeInspectionVehicle.CloseInfoPanelInternal(true);
            return;
        }

        CloseInfoPanelInternal(true);
    }

    public static void CloseActiveInspectionPanel(GameObject panel)
    {
        // Scope by panel so a stale/double click cannot close a different modal.
        LiveLoadVehicle owner = activeInspectionVehicle;
        if (owner == null || owner.vehicleInfoPanel != panel || !owner.isInspectionWindowOpen) return;
        owner.CloseInfoPanelInternal(true);
    }

    private void CloseInfoPanelInternal(bool invokeCompletionEvents)
    {
        if (!isInspectionWindowOpen) return;
        bool wasInspectionOpen = isInspectionWindowOpen ||
                                 (vehicleInfoPanel != null && vehicleInfoPanel.activeSelf);
        isInspectionWindowOpen = false;

        if (inspectionUsesPanelCoordinator && UIPanelCoordinator.Instance != null)
        {
            UIPanelCoordinator.Instance.ClosePanel(vehicleInfoPanel);
        }
        else
        {
            if (vehicleInfoPanel != null) vehicleInfoPanel.SetActive(false);
            foreach (GameObject ui in temporarilyHiddenPanels)
            {
                if (ui != null) ui.SetActive(true);
            }
        }

        inspectionUsesPanelCoordinator = false;
        temporarilyHiddenPanels.Clear();

        if (activeInspectionVehicle == this)
            activeInspectionVehicle = null;

        InputManager inputObj = FindObjectOfType<InputManager>();
        if (inputObj != null) { inputObj.SetPlayerInputEnable(true); inputObj.SetLookEnabled(true); }

        PlayerMotor player = FindObjectOfType<PlayerMotor>();
        if (player != null) player.enabled = true;

        // --- THE FIX: Advance the tutorial exactly when the player finishes reading and closes the panel! ---
        if (invokeCompletionEvents && wasInspectionOpen && advancesTutorial && TutorialManager.Instance != null)
        {
            TutorialManager.Instance.ShowNextStep();
        }

        // Keep this last: listeners may immediately open another modal window
        // (such as LessonUI), which should become the active UI state.
        if (invokeCompletionEvents && wasInspectionOpen)
        {
            onInspectionWindowClosed?.Invoke();
            InspectionWindowClosed?.Invoke();
        }
    }

    public void StopAndFreezeForWin()
    {
        BeginFinishBraking();
    }

    public void BeginFinishBraking()
    {
        if (rb == null || rb.isKinematic || isBrakingAtFinish || isParkedAtFinish) return;

        isDriving = false;
        hasReachedEnd = true;
        isParkedAtFinish = true;
        isBrakingAtFinish = true;
        settledAtFinishTimer = 0f;
        currentMotorSpeed = 0f;

        ApplyFinishBrakes();
    }

    public void StopAndReset()
    {
        isDriving = false;
        isBrakingAtFinish = false;
        settledAtFinishTimer = 0f;
        currentMotorSpeed = 0f;

        if (rb == null) return;
        ClearDynamicVelocity(rb);
        rb.isKinematic = true;
        
        rb.ResetCenterOfMass();
        rb.centerOfMass = new Vector3(0, centerOfMassOffset, 0);
        rb.ResetInertiaTensor();

        if (!isParkedAtFinish)
            ResetToSimulationStartPose();

        StripWheelPhysics(); 

        rb.Sleep(); 
    }

    private static void ClearDynamicVelocity(Rigidbody body)
    {
        if (body == null || body.isKinematic) return;
        body.velocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
    }

    private void ResetToSimulationStartPose()
    {
        Vector3 targetPosition = authoredStartPosition;
        Quaternion targetRotation = authoredStartRotation;

        if (resetToStartPointBeforeSimulation && startPoint != null)
            GetWaypointPose(startPoint, out targetPosition, out targetRotation);

        ApplyVehiclePose(targetPosition, targetRotation);
    }

    private void CaptureAuthoredStartPointPose()
    {
        if (startPoint == null)
        {
            hasAuthoredStartPointPose = false;
            return;
        }

        // Start/End markers describe the route, while imported vehicle roots are
        // rarely located at the wheel contact point. Preserve the scene-authored
        // root offset so waypoint resets cannot make a vehicle float or shift.
        // Markers are sometimes scaled to make their Scene gizmos easier to see.
        // Ignore that visual scale when calculating the vehicle root offset.
        authoredStartPointLocalPosition = Quaternion.Inverse(startPoint.rotation) *
                                          (authoredStartPosition - startPoint.position);
        authoredStartPointLocalRotation = Quaternion.Inverse(startPoint.rotation) * authoredStartRotation;
        hasAuthoredStartPointPose = true;
    }

    private void GetWaypointPose(Transform waypoint, out Vector3 position, out Quaternion rotation)
    {
        if (waypoint == null || !hasAuthoredStartPointPose)
        {
            position = authoredStartPosition;
            rotation = authoredStartRotation;
            return;
        }

        position = waypoint.position + waypoint.rotation * authoredStartPointLocalPosition;
        rotation = useWaypointRotation
            ? waypoint.rotation * authoredStartPointLocalRotation
            : authoredStartRotation;
    }

    private void ApplyVehiclePose(Vector3 position, Quaternion rotation)
    {
        rb.position = position;
        rb.rotation = rotation;
        transform.SetPositionAndRotation(position, rotation);

        foreach (WheelData wheel in wheels)
        {
            wheel.physObj.transform.localPosition = wheel.originalLocalPos;
            wheel.physObj.transform.localRotation = wheel.originalLocalRot;
        }
    }

    public void EmergencyStop()
    {
        isDriving = false;
        currentMotorSpeed = 0f;
        foreach (var w in wheels)
        {
            if (w.hinge == null) continue;
            JointMotor motor = w.hinge.motor;
            motor.targetVelocity = 0; 
            w.hinge.motor = motor;
            w.hinge.useMotor = true; 
        }
    }

    private void FixedUpdate()
    {
        if (endPoint == null || startPoint == null) return;

        if (isBrakingAtFinish)
        {
            ApplyFinishBrakes();

            if (VehicleIsSettled() && AllWheelsAreGrounded())
                settledAtFinishTimer += Time.fixedDeltaTime;
            else
                settledAtFinishTimer = 0f;

            if (settledAtFinishTimer >= requiredSettledTime)
                FinishParkingAfterPhysicsSettles();

            return;
        }

        if (isParkedAtFinish) return;

        if (!isDriving)
        {
            if (!rb.isKinematic) 
            {
                foreach (var w in wheels)
                {
                    if (w.hinge == null) continue;
                    JointMotor motor = w.hinge.motor;
                    motor.targetVelocity = 0; 
                    w.hinge.motor = motor;
                    w.hinge.useMotor = true;
                }
            }
            return;
        }

        GetWaypointPose(startPoint, out Vector3 alignedStartPosition, out _);
        GetWaypointPose(endPoint, out Vector3 alignedEndPosition, out _);
        float driveDirectionX = Mathf.Sign(alignedEndPosition.x - alignedStartPosition.x);
        bool reachedEnd = (driveDirectionX > 0 && transform.position.x >= alignedEndPosition.x) ||
                          (driveDirectionX < 0 && transform.position.x <= alignedEndPosition.x);

        if (reachedEnd)
        {
            if (!hasReachedEnd)
            {
                BeginFinishBraking();
            }
            return; 
        }

        float directionX = Mathf.Sign(alignedEndPosition.x - transform.position.x);
        float targetSpeedDegPerSec = (maxSpeed / wheelRadius) * Mathf.Rad2Deg;

        float accelerationRate = targetSpeedDegPerSec * 2f * Time.fixedDeltaTime; 
        currentMotorSpeed = Mathf.MoveTowards(currentMotorSpeed, targetSpeedDegPerSec, accelerationRate);

        foreach (var w in wheels)
        {
            if (w.hinge == null) continue;
            JointMotor motor = w.hinge.motor;
            motor.targetVelocity = currentMotorSpeed * -directionX; 
            w.hinge.motor = motor;
            w.hinge.useMotor = true;
        }
    }

    private void ApplyFinishBrakes()
    {
        foreach (WheelData wheel in wheels)
        {
            if (wheel.hinge == null) continue;
            JointMotor motor = wheel.hinge.motor;
            motor.targetVelocity = 0f;
            motor.force = brakeTorque;
            motor.freeSpin = false;
            wheel.hinge.motor = motor;
            wheel.hinge.useMotor = true;
        }
    }

    private bool VehicleIsSettled()
    {
        if (rb.velocity.magnitude > stoppedLinearSpeed || rb.angularVelocity.magnitude > stoppedAngularSpeed)
            return false;

        foreach (WheelData wheel in wheels)
        {
            if (wheel.rb == null) continue;
            if (wheel.rb.velocity.magnitude > stoppedLinearSpeed ||
                wheel.rb.angularVelocity.magnitude > stoppedAngularSpeed)
                return false;
        }

        return true;
    }

    private bool AllWheelsAreGrounded()
    {
        foreach (WheelData wheel in wheels)
        {
            if (wheel.physObj == null) continue;

            RaycastHit[] hits = Physics.RaycastAll(
                wheel.physObj.transform.position,
                Vector3.down,
                wheelRadius + wheelGroundCheckDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);

            bool grounded = false;
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == null || IsVehicleRigidbody(hit.rigidbody)) continue;
                grounded = true;
                break;
            }

            if (!grounded) return false;
        }

        return wheels.Count > 0;
    }

    private bool IsVehicleRigidbody(Rigidbody candidate)
    {
        if (candidate == rb) return true;
        foreach (WheelData wheel in wheels)
        {
            if (candidate != null && candidate == wheel.rb) return true;
        }
        return false;
    }

    private void FinishParkingAfterPhysicsSettles()
    {
        isBrakingAtFinish = false;
        settledAtFinishTimer = 0f;

        foreach (WheelData wheel in wheels)
        {
            if (wheel.hinge != null) wheel.hinge.useMotor = false;
            if (wheel.rb != null) wheel.rb.Sleep();
        }

        rb.Sleep();
    }

    private void OnDrawGizmosSelected()
    {
        if (wheelObjects == null) return;
        
        Gizmos.color = Color.cyan;
        foreach (GameObject w in wheelObjects)
        {
            if (w != null)
            {
                Renderer rend = w.GetComponentInChildren<Renderer>();
                if (rend != null) Gizmos.DrawWireSphere(rend.bounds.center, wheelRadius);
                else Gizmos.DrawWireSphere(w.transform.position, wheelRadius);
            }
        }
    }
}
