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
    
    private float currentMotorSpeed = 0f;
    private PhysicMaterial wheelMat; 
    private bool isInspectionWindowOpen;

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
        
        rb.mass = vehicleMass;
        rb.isKinematic = true; 
        rb.useGravity = true; 
        rb.collisionDetectionMode = CollisionDetectionMode.Discrete; 

        rb.centerOfMass = new Vector3(0, centerOfMassOffset, 0);
        rb.constraints = RigidbodyConstraints.FreezeRotationY | RigidbodyConstraints.FreezePositionZ;
        rb.sleepThreshold = 0f;
        rb.maxDepenetrationVelocity = 10f; 

        Collider chassisCol = GetComponent<Collider>();
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
        if (physicsManager != null)
        {
            physicsManager.OnSettlePhaseStarted += HandleSettlePhaseStarted;
            physicsManager.OnSimulationStarted += HandleSimulationStarted;
            physicsManager.OnSimulationStopped += HandleSimulationStopped;
        }

        if (assignedContract != null && PlayerDataManager.Instance != null)
        {
            if (PlayerDataManager.Instance.GetSavedBridge(assignedContract.name) != null || 
                PlayerDataManager.Instance.CurrentData.completedContracts.Contains(assignedContract.name))
            {
                isParkedAtFinish = true;
                
                if (endPoint != null)
                {
                    rb.position = endPoint.position;
                    rb.rotation = endPoint.rotation;
                    transform.position = endPoint.position;
                    transform.rotation = endPoint.rotation;

                    foreach (var w in wheels)
                    {
                        w.physObj.transform.localPosition = w.originalLocalPos;
                        w.physObj.transform.localRotation = w.originalLocalRot;
                    }
                }
            }
        }
    }

    private void OnDestroy()
    {
        if (physicsManager != null)
        {
            physicsManager.OnSettlePhaseStarted -= HandleSettlePhaseStarted;
            physicsManager.OnSimulationStarted -= HandleSimulationStarted;
            physicsManager.OnSimulationStopped -= HandleSimulationStopped;
        }
    }

    private void BuildWheelPhysics()
    {
        Collider chassisCol = GetComponent<Collider>();

        foreach (var w in wheels)
        {
            if (w.rb == null)
            {
                w.rb = w.physObj.AddComponent<Rigidbody>();
                w.rb.mass = wheelMass; 
                w.rb.isKinematic = true; 
                w.rb.collisionDetectionMode = CollisionDetectionMode.Discrete; 
                w.rb.sleepThreshold = 0f; 
                w.rb.maxDepenetrationVelocity = 10f; 
            }

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
            if (w.hinge != null) { Destroy(w.hinge); w.hinge = null; }
            if (w.rb != null) { Destroy(w.rb); w.rb = null; }
        }
    }

    private void HandleSettlePhaseStarted()
    {
        if (GameManager.Instance != null && assignedContract != null && GameManager.Instance.CurrentContract != assignedContract) return;

        hasReachedEnd = false; 
        isParkedAtFinish = false; 
        isBrakingAtFinish = false;
        settledAtFinishTimer = 0f;
        currentMotorSpeed = 0f; 

        if (assignedContract != null) { vehicleMass = assignedContract.liveLoadWeight; if (rb != null) rb.mass = vehicleMass; }

        StripWheelPhysics(); 

        rb.isKinematic = true;

        if (startPoint != null)
        {
            rb.position = startPoint.position;
            rb.rotation = startPoint.rotation;
            transform.position = startPoint.position;
            transform.rotation = startPoint.rotation;

            foreach (var w in wheels)
            {
                w.physObj.transform.localPosition = w.originalLocalPos;
                w.physObj.transform.localRotation = w.originalLocalRot;
            }
        }

        BuildWheelPhysics(); 

        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero; 
        
        rb.ResetCenterOfMass();
        rb.centerOfMass = new Vector3(0, centerOfMassOffset, 0);
        rb.ResetInertiaTensor(); 
        
        foreach (var w in wheels)
        {
            w.rb.velocity = Vector3.zero;
            w.rb.angularVelocity = Vector3.zero; 
            w.rb.ResetCenterOfMass();
            w.rb.ResetInertiaTensor();
        }

        Physics.SyncTransforms(); 
    }

    private void HandleSimulationStarted()
    {
        if (GameManager.Instance != null && assignedContract != null && GameManager.Instance.CurrentContract != assignedContract) return;
        
        rb.isKinematic = false;
        rb.WakeUp();
        foreach (var w in wheels)
        {
            if (w.rb == null) continue;
            w.rb.isKinematic = false;
            w.rb.WakeUp();
        }
        
        isBrakingAtFinish = false;
        settledAtFinishTimer = 0f;
        isDriving = true; 
    }

    private void HandleSimulationStopped()
    {
        StopAndReset();
    }

    private void Update()
    {
        promptMessage = "Inspect " + vehicleName;
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
            if (vehicleNameText != null) vehicleNameText.text = vehicleName;
            
            float displayWeight = assignedContract != null ? assignedContract.liveLoadWeight : vehicleMass;
            if (vehicleWeightText != null) vehicleWeightText.text = $"Weight: {displayWeight} kg";
            if (vehicleSpeedText != null) vehicleSpeedText.text = $"Top Speed: {maxSpeed} m/s";

            temporarilyHiddenPanels.Clear();
            foreach (GameObject ui in uiElementsToHide)
            {
                if (ui != null && ui.activeSelf) { temporarilyHiddenPanels.Add(ui); ui.SetActive(false); }
            }

            vehicleInfoPanel.SetActive(true);
            isInspectionWindowOpen = true;

            InputManager inputObj = FindObjectOfType<InputManager>();
            if (inputObj != null) { inputObj.SetPlayerInputEnable(false); inputObj.SetLookEnabled(false); }

            PlayerMotor player = FindObjectOfType<PlayerMotor>();
            if (player != null) player.enabled = false;
        }
    }

    public void CloseInfoPanel()
    {
        bool wasInspectionOpen = isInspectionWindowOpen ||
                                 (vehicleInfoPanel != null && vehicleInfoPanel.activeSelf);
        isInspectionWindowOpen = false;

        if (vehicleInfoPanel != null) vehicleInfoPanel.SetActive(false);

        foreach (GameObject ui in temporarilyHiddenPanels) if (ui != null) ui.SetActive(true);
        temporarilyHiddenPanels.Clear();

        InputManager inputObj = FindObjectOfType<InputManager>();
        if (inputObj != null) { inputObj.SetPlayerInputEnable(true); inputObj.SetLookEnabled(true); }

        PlayerMotor player = FindObjectOfType<PlayerMotor>();
        if (player != null) player.enabled = true;

        // --- THE FIX: Advance the tutorial exactly when the player finishes reading and closes the panel! ---
        if (advancesTutorial && TutorialManager.Instance != null)
        {
            TutorialManager.Instance.ShowNextStep();
        }

        // Keep this last: listeners may immediately open another modal window
        // (such as LessonUI), which should become the active UI state.
        if (wasInspectionOpen)
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

        rb.isKinematic = true;
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        
        rb.ResetCenterOfMass();
        rb.centerOfMass = new Vector3(0, centerOfMassOffset, 0);
        rb.ResetInertiaTensor();

        if (!isParkedAtFinish && startPoint != null)
        {
            rb.position = startPoint.position;
            rb.rotation = startPoint.rotation;
            transform.position = startPoint.position;
            transform.rotation = startPoint.rotation;

            foreach (var w in wheels)
            {
                w.physObj.transform.localPosition = w.originalLocalPos;
                w.physObj.transform.localRotation = w.originalLocalRot;
            }
        }

        StripWheelPhysics(); 

        rb.Sleep(); 
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

        float driveDirectionX = Mathf.Sign(endPoint.position.x - startPoint.position.x);
        bool reachedEnd = (driveDirectionX > 0 && transform.position.x >= endPoint.position.x) || 
                          (driveDirectionX < 0 && transform.position.x <= endPoint.position.x);

        if (reachedEnd)
        {
            if (!hasReachedEnd)
            {
                BeginFinishBraking();
            }
            return; 
        }

        float directionX = Mathf.Sign(endPoint.position.x - transform.position.x);
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
