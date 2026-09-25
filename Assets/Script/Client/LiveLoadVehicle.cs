using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;
using UnityEngine.Serialization;
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
    public bool CanChangeCargo => isActiveAndEnabled && !isRemoteMultiplayerRepresentation && AllowsCargo && !isDriving &&
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
            float total = 0f;
            foreach (VehicleCargoSlot slot in cargoSlots)
            {
                if (slot == null) continue;
                if (slot.LoadedCargo != null)
                {
                    total += slot.LoadedWeight;
                    continue;
                }

                // Later crossings reuse a truck whose supplies are already
                // sitting in its authored slots. These crates are visual cargo,
                // not new pickups, but their mass still belongs in the test.
                CargoItem authoredCargo = slot.acceptedCargo;
                if (!AllowsCargo && authoredCargo != null &&
                    authoredCargo.transform.IsChildOf(slot.transform) &&
                    authoredCargo.gameObject.activeInHierarchy)
                    total += Mathf.Max(0f, authoredCargo.cargoWeight);
            }
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
            if (vehicle.isActiveAndEnabled && MatchesContract(vehicle.assignedContract, contract)) return vehicle;
        return null;
    }

    /// <summary>
    /// Returns the world-space physical envelope used to decide how much of this
    /// vehicle is currently supported by the bridge. Trigger volumes such as the
    /// interaction and smart-loading zones are deliberately excluded.
    /// </summary>
    public bool TryGetPhysicalBounds(out Bounds bounds)
    {
        bounds = new Bounds(transform.position, Vector3.zero);
        bool found = false;

        // The wheel contact envelope is the best approximation of how much
        // vehicle weight has transferred from the bank onto the bridge.
        foreach (WheelData wheel in wheels)
        {
            Collider wheelCollider = wheel != null && wheel.physObj != null
                ? wheel.physObj.GetComponent<Collider>()
                : null;
            if (wheelCollider == null || !wheelCollider.enabled ||
                !wheelCollider.gameObject.activeInHierarchy)
                continue;

            if (!found)
            {
                bounds = wheelCollider.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(wheelCollider.bounds);
            }
        }

        if (found) return true;

        // Fallback for a vehicle authored without wheel objects.
        foreach (Collider vehicleCollider in GetComponentsInChildren<Collider>())
        {
            if (vehicleCollider == null || !vehicleCollider.enabled ||
                vehicleCollider.isTrigger || !vehicleCollider.gameObject.activeInHierarchy)
                continue;

            if (!found)
            {
                bounds = vehicleCollider.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(vehicleCollider.bounds);
            }
        }

        return found;
    }
    public static float GetContractTestWeight(ContractSO contract)
    {
        if (contract == null) return 1000f;
        if (contract.liveLoadMode != ContractSO.LiveLoadMode.Vehicle)
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
    [Tooltip("Freeze only chassis Z rotation while the live load drives and brakes. X stays free so the wheel hinges can propel the vehicle.")]
    [FormerlySerializedAs("lockChassisRotationWhileDriving")]
    [SerializeField] private bool freezeChassisZRotationWhileDriving = true;
    [Tooltip("Moves an unusually large imported-model scale from this Rigidbody root into its direct children at startup, preserving the visible model while keeping the physics root near unit scale.")]
    [SerializeField] private bool normalizeOverscaledPhysicsRoot = true;
    [Min(2f)] [SerializeField] private float physicsRootScaleThreshold = 10f;

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
    private RigidbodyConstraints normalChassisConstraints;
    private bool isDriving = false;
    private bool hasReachedEnd = false; 
    private bool isBrakingAtFinish = false;
    private bool participatingInCurrentSimulation;
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

    public float ExpectedRouteHeight
    {
        get
        {
            if (startPoint == null || endPoint == null) return authoredStartPosition.y;
            GetWaypointPose(startPoint, out Vector3 routeStart, out _);
            GetWaypointPose(endPoint, out Vector3 routeEnd, out _);
            return Mathf.Lerp(routeStart.y, routeEnd.y, NormalizedRouteProgress);
        }
    }

    /// <summary>
    /// Returns how far the vehicle's physical wheel envelope is above or below
    /// its authored route. The reference offset is captured after the vehicle is
    /// reset for a simulation, so imported models whose root is far from their
    /// wheels (such as Vance's scaled cargo truck) are handled correctly.
    /// </summary>
    public bool TryGetVerticalRouteDeviation(out float deviation, out float physicalHeight)
    {
        physicalHeight = transform.position.y;
        if (TryGetPhysicalBounds(out Bounds physicalBounds))
            physicalHeight = physicalBounds.center.y;

        if (!hasPhysicalRouteHeightOffset)
            CapturePhysicalRouteHeightOffset();

        deviation = physicalHeight - (ExpectedRouteHeight + physicalRouteHeightOffset);
        return hasPhysicalRouteHeightOffset;
    }
    
    private float currentMotorSpeed = 0f;
    private PhysicMaterial wheelMat; 
    private bool isInspectionWindowOpen;
    private bool inspectionUsesPanelCoordinator;
    private bool inspectionOpenedFromBuildMode;
    private BridgeSelectionOutline buildModeInspectionOutline;
    private static LiveLoadVehicle activeInspectionVehicle;
    private Vector3 authoredStartPosition;
    private Quaternion authoredStartRotation;
    private Vector3 authoredStartPointLocalPosition;
    private Quaternion authoredStartPointLocalRotation = Quaternion.identity;
    private bool hasAuthoredStartPointPose;
    private bool hideWhenBuildModeCloses;
    private bool visibleForBuildReplay;
    private bool isRemoteMultiplayerRepresentation;
    private bool hasSessionParkedPose;
    private Vector3 sessionParkedPosition;
    private Quaternion sessionParkedRotation;
    private float physicalRouteHeightOffset;
    private bool hasPhysicalRouteHeightOffset;

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
        EnsureAuthoredReferences(true);
        rb = GetComponent<Rigidbody>();
        // Scene-authored vehicles may arrive dynamic. Freeze the body before
        // changing an imported root scale so PhysX never observes an intermediate
        // collider hierarchy during scene startup.
        rb.isKinematic = true;
        NormalizePhysicsRootScale();
        authoredStartPosition = transform.position;
        authoredStartRotation = transform.rotation;
        CaptureAuthoredStartPointPose();
        
        rb.mass = vehicleMass;
        rb.useGravity = true; 
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        ApplyConfiguredCenterOfMass();
        normalChassisConstraints = RigidbodyConstraints.FreezeRotationY | RigidbodyConstraints.FreezePositionZ;
        rb.constraints = normalChassisConstraints;
        rb.sleepThreshold = 0f;
        rb.maxDepenetrationVelocity = 10f; 

        // Cargo slots and interaction zones are triggers, not the chassis. Apply
        // low friction to every solid body collider, including imported child
        // meshes, so a body corner cannot catch a flexing road seam.
        Collider chassisCol = null;
        PhysicMaterial slipMat = new PhysicMaterial("ChassisSlip");
        slipMat.dynamicFriction = 0f;
        slipMat.staticFriction = 0f;
        slipMat.bounciness = 0f;
        foreach (Collider bodyCollider in GetComponentsInChildren<Collider>(true))
        {
            if (bodyCollider == null || bodyCollider.isTrigger || IsVisualWheelCollider(bodyCollider) ||
                IsAuthoredWheelAxleCollider(bodyCollider))
                continue;
            if (chassisCol == null) chassisCol = bodyCollider;
            bodyCollider.material = slipMat;
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

            // A truck saved as a prefab may already contain the axle objects
            // created by an earlier run. Reuse them: wrapping the visible wheel
            // again leaves the old SphereCollider welded to the chassis, so the
            // truck rests on fixed spheres instead of its driven wheels.
            Transform authoredAxle = visualWheel.transform.parent;
            bool reuseAxle = authoredAxle != null && authoredAxle.parent == transform &&
                             authoredAxle.name == visualWheel.name + "_PhysicsAxle" &&
                             authoredAxle.GetComponent<SphereCollider>() != null;
            GameObject physWheel;
            if (reuseAxle)
            {
                physWheel = authoredAxle.gameObject;
            }
            else
            {
                physWheel = new GameObject(visualWheel.name + "_PhysicsAxle");
                physWheel.transform.position = rend.bounds.center;
                physWheel.transform.rotation = visualWheel.transform.rotation;
                physWheel.transform.SetParent(transform);
                visualWheel.transform.SetParent(physWheel.transform, true);
            }

            WheelData wd = new WheelData();
            wd.physObj = physWheel;
            wd.originalLocalPos = physWheel.transform.localPosition;
            wd.originalLocalRot = physWheel.transform.localRotation;

            Collider oldCol = visualWheel.GetComponent<Collider>();
            if (oldCol != null) Destroy(oldCol);

            SphereCollider sc = physWheel.GetComponent<SphereCollider>();
            if (sc == null) sc = physWheel.AddComponent<SphereCollider>();
            sc.radius = wheelRadius; sc.material = wheelMat;

            if (chassisCol != null) Physics.IgnoreCollision(chassisCol, sc, true);

            wheels.Add(wd);
        }

        CapturePhysicalRouteHeightOffset();

        if (physicsManager == null) physicsManager = FindObjectOfType<BridgePhysicsManager>();
        if (vehicleInfoPanel != null) vehicleInfoPanel.SetActive(false);
    }

    /// <summary>
    /// Keeps a replacement FBX from silently disconnecting the gameplay authored
    /// on the vehicle prefab. Scene overrides can legitimately hold the route and
    /// UI references, while wheels and cargo slots must always belong to this
    /// vehicle hierarchy.
    /// </summary>
    private void EnsureAuthoredReferences(bool reportProblems)
    {
        foreach (VehicleCargoSlot slot in GetComponentsInChildren<VehicleCargoSlot>(true))
        {
            if (slot == null) continue;
            slot.vehicle = this;
            if (slot.cargoSocket == null) slot.cargoSocket = slot.transform;
            RegisterCargoSlot(slot);
        }

        List<GameObject> resolvedWheels = new List<GameObject>();
        if (wheelObjects != null)
        {
            foreach (GameObject wheel in wheelObjects)
            {
                if (wheel == null || !wheel.transform.IsChildOf(transform) ||
                    wheel.GetComponentInChildren<Renderer>(true) == null ||
                    resolvedWheels.Contains(wheel))
                    continue;

                resolvedWheels.Add(wheel);
            }
        }

        // Blender replacements commonly preserve useful Tire/Wheel object names
        // even though Unity loses the serialized references when the old meshes
        // are removed. Reconnect only explicitly named rendered children so body
        // or cargo meshes can never be mistaken for wheels.
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null) continue;
            GameObject candidate = renderer.gameObject;
            string candidateName = candidate.name;
            bool namedWheel = candidateName.IndexOf("wheel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              candidateName.IndexOf("tire", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!namedWheel || resolvedWheels.Contains(candidate)) continue;
            resolvedWheels.Add(candidate);
        }

        resolvedWheels.Sort((left, right) =>
            string.Compare(left.name, right.name, StringComparison.OrdinalIgnoreCase));
        wheelObjects = resolvedWheels.ToArray();

        if (reportProblems && wheelObjects.Length < 2)
        {
            Debug.LogWarning(
                $"[LiveLoadVehicle] '{name}' has only {wheelObjects.Length} resolved wheel object(s). " +
                "Name imported wheel meshes with 'Wheel' or 'Tire', or assign Wheel Objects in the Inspector.",
                this);
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        EnsureAuthoredReferences(false);
    }
#endif

    private void NormalizePhysicsRootScale()
    {
        if (!normalizeOverscaledPhysicsRoot) return;

        Vector3 rootScale = transform.localScale;
        float largestAxis = Mathf.Max(
            Mathf.Abs(rootScale.x),
            Mathf.Abs(rootScale.y),
            Mathf.Abs(rootScale.z));
        if (largestAxis <= Mathf.Max(2f, physicsRootScaleThreshold)) return;
        bool isUniform = Mathf.Abs(Mathf.Abs(rootScale.x) - largestAxis) <= largestAxis * 0.001f &&
                         Mathf.Abs(Mathf.Abs(rootScale.y) - largestAxis) <= largestAxis * 0.001f &&
                         Mathf.Abs(Mathf.Abs(rootScale.z) - largestAxis) <= largestAxis * 0.001f;
        if (!isUniform)
        {
            Debug.LogWarning(
                $"[LiveLoadVehicle] '{name}' has a non-uniform oversized physics-root scale " +
                $"({rootScale}). Normalize that hierarchy in the scene before simulation.", this);
            return;
        }

        // Preserve every direct child's local-to-world matrix while transferring
        // the imported model scale away from the Rigidbody transform. Vance's
        // truck is authored at roughly 246x; leaving that on the dynamic root can
        // destabilize compound colliders and generated wheel rigidbodies.
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            child.localPosition = Vector3.Scale(child.localPosition, rootScale);
            child.localScale = Vector3.Scale(child.localScale, rootScale);
        }

        transform.localScale = Vector3.one;
        Physics.SyncTransforms();
        Debug.Log(
            $"[LiveLoadVehicle] Normalized oversized physics root '{name}' from {rootScale} to (1, 1, 1). " +
            "Child world transforms were preserved.", this);
    }

    private void ConfigureNPCNavMeshObstacle(Collider chassisCollider)
    {
        if (!configureNPCObstacle) return;

        if (npcObstacle == null)
            npcObstacle = GetComponent<NavMeshObstacle>();
        if (npcObstacle == null)
            npcObstacle = gameObject.AddComponent<NavMeshObstacle>();

        npcObstacle.shape = NavMeshObstacleShape.Box;

        if (chassisCollider != null)
        {
            Vector3 scale = transform.lossyScale;
            Vector3 worldSize = chassisCollider.bounds.size;
            npcObstacle.center = transform.InverseTransformPoint(chassisCollider.bounds.center);
            npcObstacle.size = new Vector3(
                (worldSize.x + npcObstaclePadding * 2f) /
                    Mathf.Max(Mathf.Abs(scale.x), 0.0001f),
                (worldSize.y + npcObstaclePadding * 2f) /
                    Mathf.Max(Mathf.Abs(scale.y), 0.0001f),
                (worldSize.z + npcObstaclePadding * 2f) /
                    Mathf.Max(Mathf.Abs(scale.z), 0.0001f));
        }

        // Moving obstacles should use local avoidance. Carving a moving physics
        // vehicle would repeatedly rebuild holes and destabilize agent paths.
        npcObstacle.carving = false;
        npcObstacle.carveOnlyStationary = true;
    }

    private void Start()
    {
        ReuseSceneInspectionPanelIfUnassigned();
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

    private void ReuseSceneInspectionPanelIfUnassigned()
    {
        if (vehicleInfoPanel != null || assignedContract == null) return;

        // A copied vehicle prefab cannot serialize references to scene UI.
        // Reuse the already-authored panel from another vehicle in this scene.
        foreach (LiveLoadVehicle other in FindObjectsOfType<LiveLoadVehicle>(true))
        {
            if (other == null || other == this || other.gameObject.scene != gameObject.scene ||
                other.vehicleInfoPanel == null) continue;

            vehicleInfoPanel = other.vehicleInfoPanel;
            vehicleNameText = other.vehicleNameText;
            vehicleWeightText = other.vehicleWeightText;
            vehicleSpeedText = other.vehicleSpeedText;
            break;
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
        DisposeBuildModeInspectionOutline();
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
        List<Collider> chassisColliders = GetSolidChassisColliders();

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
            if (wheelCol != null)
                foreach (Collider chassisCollider in chassisColliders)
                    Physics.IgnoreCollision(chassisCollider, wheelCol, true);
        }
    }

    private bool IsVisualWheelCollider(Collider candidate)
    {
        if (wheelObjects == null) return false;
        foreach (GameObject visualWheel in wheelObjects)
            if (visualWheel != null && candidate.transform.IsChildOf(visualWheel.transform))
                return true;
        return false;
    }

    private bool IsAuthoredWheelAxleCollider(Collider candidate)
    {
        if (candidate == null || candidate.transform.parent != transform ||
            !(candidate is SphereCollider) || wheelObjects == null) return false;

        foreach (GameObject visualWheel in wheelObjects)
            if (visualWheel != null && visualWheel.transform.parent == candidate.transform &&
                candidate.name == visualWheel.name + "_PhysicsAxle") return true;
        return false;
    }

    private List<Collider> GetSolidChassisColliders()
    {
        List<Collider> colliders = new List<Collider>();
        foreach (Collider candidate in GetComponentsInChildren<Collider>(true))
        {
            if (candidate != null && candidate.enabled && !candidate.isTrigger &&
                !IsVisualWheelCollider(candidate) && !IsPhysicsWheelCollider(candidate))
                colliders.Add(candidate);
        }
        return colliders;
    }

    private bool IsPhysicsWheelCollider(Collider candidate)
    {
        foreach (WheelData wheel in wheels)
            if (wheel.physObj != null && candidate.transform.IsChildOf(wheel.physObj.transform))
                return true;
        return false;
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
        if (!CanParticipateInCurrentSimulation() || IsPlayerCargoContract()) return;
        participatingInCurrentSimulation = true;

        hasReachedEnd = false; 
        isParkedAtFinish = false; 
        isBrakingAtFinish = false;
        settledAtFinishTimer = 0f;
        currentMotorSpeed = 0f; 

        RefreshCargoMass();

        StripWheelPhysics(); 

        ClearDynamicVelocity(rb);
        rb.isKinematic = true;
        SetDrivingRotationConstraint(false);
        ApplySimulationSolverSettings(rb);

        ResetToSimulationStartPose();

        BuildWheelPhysics(); 
        IgnoreNonRoadBridgeContacts();

        
        rb.ResetCenterOfMass();
        ApplyConfiguredCenterOfMass();
        rb.ResetInertiaTensor(); 
        
        foreach (var w in wheels)
        {
            ClearDynamicVelocity(w.rb);
            w.rb.ResetCenterOfMass();
            w.rb.ResetInertiaTensor();
        }

        Physics.SyncTransforms(); 
        CapturePhysicalRouteHeightOffset();
    }

    private void CapturePhysicalRouteHeightOffset()
    {
        float physicalHeight = transform.position.y;
        if (TryGetPhysicalBounds(out Bounds physicalBounds))
            physicalHeight = physicalBounds.center.y;

        physicalRouteHeightOffset = physicalHeight - ExpectedRouteHeight;
        hasPhysicalRouteHeightOffset = true;
    }

    private void ApplySimulationSolverSettings(Rigidbody body)
    {
        if (body == null) return;

        int positionIterations = physicsManager != null
            ? Mathf.Max(1, physicsManager.physicsSolverIterations)
            : Mathf.Max(1, Physics.defaultSolverIterations);

        body.solverIterations = positionIterations;
        body.solverVelocityIterations = physicsManager != null
            ? Mathf.Max(1, physicsManager.physicsSolverVelocityIterations)
            : Mathf.Max(1, Physics.defaultSolverVelocityIterations);
    }

    private void ApplyConfiguredCenterOfMass()
    {
        if (rb == null) return;

        // Rigidbody.centerOfMass follows the transform's rotation but explicitly
        // ignores its scale. Convert a world-vertical offset with rotation only;
        // Transform.InverseTransformVector would incorrectly divide it by the
        // imported model scale. This also keeps rotated vehicle roots from moving
        // their centre of mass sideways instead of downward.
        rb.centerOfMass = Quaternion.Inverse(transform.rotation) *
                          (Vector3.up * centerOfMassOffset);
    }

    private void HandleSimulationStarted()
    {
        // Every LiveLoadVehicle listens to the shared physics manager. Never let
        // an unassigned or different contract's vehicle react to that global
        // event; otherwise a stray cart can start driving and later be reported
        // as the active contract's failed live load.
        if (!CanParticipateInCurrentSimulation() || IsPlayerCargoContract()) return;
        participatingInCurrentSimulation = true;

        if (isInspectionWindowOpen && inspectionOpenedFromBuildMode)
            CloseInfoPanelInternal(false);
        buildModeInspectionOutline?.SetBuildModeVisualOnlyVisible(false);

        IgnoreNonRoadBridgeContacts();
        
        SetDrivingRotationConstraint(true);
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

    private void IgnoreNonRoadBridgeContacts()
    {
        // Apply before the bridge's settling ticks as well as when driving starts.
        // Kinematic vehicle colliders can otherwise push side trusses out of
        // place before the wheels ever begin moving.
        physicsManager?.IgnoreVehicleContactsWithNonRoadMembers(
            GetComponentsInChildren<Collider>());
    }

    private void HandleSimulationStopped()
    {
        if (!participatingInCurrentSimulation) return;
        participatingInCurrentSimulation = false;
        StopAndReset();
    }

    private static bool IsPlayerCargoContract()
    {
        return GameManager.Instance != null && GameManager.Instance.CurrentContract != null &&
            GameManager.Instance.CurrentContract.liveLoadMode == ContractSO.LiveLoadMode.PlayerCarriedCargo;
    }

    private void Update()
    {
        UpdateBuildModeInspectionOutline();

        if (hideWhenBuildModeCloses &&
            (GameManager.Instance == null ||
             (!GameManager.Instance.IsInBuildMode() && !GameManager.Instance.IsTransitioning)))
        {
            HideForSavedBridge();
            return;
        }

        promptMessage = "Inspect " + vehicleName;
    }

    private void UpdateBuildModeInspectionOutline()
    {
        bool shouldHighlight = IsBuildModeInspectionAvailable();
        if (shouldHighlight && buildModeInspectionOutline == null)
            buildModeInspectionOutline = new BridgeSelectionOutline(transform);
        buildModeInspectionOutline?.SetBuildModeVisualOnlyVisible(shouldHighlight);
    }

    private bool IsBuildModeInspectionAvailable()
    {
        if (vehicleInfoPanel == null || isDriving || GameManager.Instance == null ||
            GameManager.Instance.CurrentState != GameManager.GameState.Building ||
            GameManager.Instance.IsTransitioning ||
            (physicsManager != null && physicsManager.IsSimulationActive))
            return false;

        ContractSO currentContract = GameManager.Instance.CurrentContract;
        return currentContract != null && MatchesContract(assignedContract, currentContract);
    }

    private void DisposeBuildModeInspectionOutline()
    {
        if (buildModeInspectionOutline == null) return;
        buildModeInspectionOutline.Dispose();
        buildModeInspectionOutline = null;
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
            if (vehicle.gameObject.scene.name == "Multiplayer" && vehicle.isParkedAtFinish)
            {
                vehicle.sessionParkedPosition = vehicle.transform.position;
                vehicle.sessionParkedRotation = vehicle.transform.rotation;
                vehicle.hasSessionParkedPose = true;
            }
            vehicle.visibleForBuildReplay = true;
            vehicle.hideWhenBuildModeCloses = false;
            if (!vehicle.gameObject.activeSelf) vehicle.gameObject.SetActive(true);
            vehicle.ResetForBuildReplay();
        }
    }

    public static void RestoreSessionVehicleAfterCancelledRedesign(ContractSO contract)
    {
        if (!IsVehicleContract(contract)) return;
        foreach (LiveLoadVehicle vehicle in FindLoadedVehicles())
        {
            if (vehicle.gameObject.scene.name != "Multiplayer" ||
                !MatchesContract(vehicle.assignedContract, contract) ||
                !vehicle.hasSessionParkedPose) continue;

            vehicle.hasSessionParkedPose = false;
            vehicle.StopAndReset();
            vehicle.transform.SetPositionAndRotation(
                vehicle.sessionParkedPosition, vehicle.sessionParkedRotation);
            vehicle.hasReachedEnd = true;
            vehicle.isParkedAtFinish = true;
            Physics.SyncTransforms();
        }
    }

    public void SetRemoteMultiplayerRepresentation(bool remote)
    {
        isRemoteMultiplayerRepresentation = remote;
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
            SetDrivingRotationConstraint(false);
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
        return gameObject.scene.name != "Multiplayer" && IsVehicleContract(assignedContract) &&
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

    private bool CanParticipateInCurrentSimulation()
    {
        ContractSO currentContract = GameManager.Instance != null
            ? GameManager.Instance.CurrentContract
            : null;

        // A vehicle with no contract is scenery, not a simulation participant.
        // Requiring a real active contract also prevents global simulation events
        // fired during scene transitions from waking any vehicle.
        return currentContract != null && MatchesContract(assignedContract, currentContract);
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

        OpenInspectionPanel(false);
    }

    /// <summary>
    /// Opens the existing scene-authored inspection panel when the build canvas
    /// receives a click or tap over this vehicle. The caller consumes the pointer
    /// event so the same gesture cannot also edit the bridge.
    /// </summary>
    public static bool TryOpenBuildModeInspection(Vector2 screenPosition, Camera buildCamera)
    {
        if (buildCamera == null || GameManager.Instance == null ||
            GameManager.Instance.CurrentState != GameManager.GameState.Building ||
            GameManager.Instance.IsTransitioning)
            return false;

        Ray ray = buildCamera.ScreenPointToRay(screenPosition);
        RaycastHit[] hits = Physics.RaycastAll(
            ray,
            buildCamera.farClipPlane,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Collide);
        Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));

        ContractSO currentContract = GameManager.Instance.CurrentContract;
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null) continue;
            LiveLoadVehicle vehicle = hit.collider.GetComponentInParent<LiveLoadVehicle>();
            if (vehicle == null || !vehicle.isActiveAndEnabled || vehicle.vehicleInfoPanel == null) continue;
            if (!vehicle.IsBuildModeInspectionAvailable()) continue;
            if (!MatchesContract(vehicle.assignedContract, currentContract))
                continue;

            if (vehicle.isInspectionWindowOpen && activeInspectionVehicle == vehicle) return true;
            vehicle.OpenInspectionPanel(true);
            return true;
        }

        return false;
    }

    private void OpenInspectionPanel(bool openedFromBuildMode)
    {
        if (vehicleInfoPanel != null)
        {
            // Multiple vehicles can intentionally share one inspection panel. The
            // panel's serialized Close button may point at Vehicle 1, so remember
            // which vehicle actually opened it and route Close to that instance.
            activeInspectionVehicle = this;
            inspectionOpenedFromBuildMode = openedFromBuildMode;

            VehicleInspectionPanel inspectionPanel = vehicleInfoPanel.GetComponent<VehicleInspectionPanel>();
            if (inspectionPanel != null)
                inspectionPanel.ApplyLayout(vehicleNameText, vehicleWeightText, vehicleSpeedText);

            if (vehicleNameText != null) vehicleNameText.text = vehicleName;
            
            float payloadWeight = PayloadWeight;
            float displayWeight = TotalTestWeight;
            float baseWeight = assignedContract != null ? assignedContract.liveLoadWeight : vehicleMass;
            if (vehicleWeightText != null)
            {
                vehicleWeightText.text = AllowsCargo || payloadWeight > 0f
                    ? $"<size=20><color=#9B6A3E>LIVE LOAD</color></size>\n" +
                      $"<size=42><b>{displayWeight:N0} kg</b></size>\n" +
                      $"<size=19><color=#6B4A36>Vehicle {baseWeight:N0} kg  \u2022  Cargo {payloadWeight:N0} kg</color></size>"
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

            // Build mode uses this as a lightweight overlay. Sending it through
            // the full-screen coordinator disables the build canvas, including
            // the blueprint background and HUD.
            inspectionUsesPanelCoordinator = !openedFromBuildMode && UIPanelCoordinator.Instance != null;
            temporarilyHiddenPanels.Clear();
            if (inspectionUsesPanelCoordinator)
            {
                UIPanelCoordinator.Instance.OpenPanel(vehicleInfoPanel);
            }
            else if (openedFromBuildMode)
            {
                vehicleInfoPanel.SetActive(true);
                vehicleInfoPanel.transform.SetAsLastSibling();
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
        bool wasBuildModeInspection = inspectionOpenedFromBuildMode;
        isInspectionWindowOpen = false;
        inspectionOpenedFromBuildMode = false;

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
        bool returningToBuildMode = GameManager.Instance != null && GameManager.Instance.IsInBuildMode();
        if (inputObj != null)
        {
            inputObj.SetPlayerInputEnable(!returningToBuildMode);
            inputObj.SetLookEnabled(!returningToBuildMode);
        }

        PlayerMotor player = FindObjectOfType<PlayerMotor>();
        if (player != null) player.enabled = !returningToBuildMode;

        // --- THE FIX: Advance the tutorial exactly when the player finishes reading and closes the panel! ---
        if (invokeCompletionEvents && wasInspectionOpen && !wasBuildModeInspection &&
            advancesTutorial && TutorialManager.Instance != null)
        {
            TutorialManager.Instance.ShowNextStep();
        }

        // Keep this last: listeners may immediately open another modal window
        // (such as LessonUI), which should become the active UI state.
        if (invokeCompletionEvents && wasInspectionOpen && !wasBuildModeInspection)
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
        SetDrivingRotationConstraint(false);

        if (!isParkedAtFinish)
            ResetToSimulationStartPose();

        rb.ResetCenterOfMass();
        ApplyConfiguredCenterOfMass();
        rb.ResetInertiaTensor();

        StripWheelPhysics(); 

        rb.Sleep(); 
    }

    private void SetDrivingRotationConstraint(bool driving)
    {
        if (rb == null) return;
        rb.constraints = normalChassisConstraints |
            (driving && freezeChassisZRotationWhileDriving
                ? RigidbodyConstraints.FreezeRotationZ
                : RigidbodyConstraints.None);
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
