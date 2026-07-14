using System.Collections.Generic;
using UnityEngine;
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

    [Header("System")]
    public BridgePhysicsManager physicsManager;

    private Rigidbody rb;
    private bool isDriving = false;
    private bool hasReachedEnd = false; 

    [HideInInspector] public bool isParkedAtFinish = false;
    
    private float currentMotorSpeed = 0f;
    private PhysicMaterial wheelMat; // Cached material

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

        // Set initial Center of Mass
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

        wheelMat = new PhysicMaterial("WheelGrip");
        wheelMat.dynamicFriction = 1f; wheelMat.staticFriction = 1f; 
        wheelMat.frictionCombine = PhysicMaterialCombine.Maximum; wheelMat.bounciness = 0f;

        // Create the physical wheel objects, but DO NOT add joints or rigidbodies yet!
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

    // --- NEW: Cleanly builds the physics components ---
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

    // --- NEW: Strips physics components to flush PhysX cache ---
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
        currentMotorSpeed = 0f; 

        if (assignedContract != null) { vehicleMass = assignedContract.liveLoadWeight; if (rb != null) rb.mass = vehicleMass; }

        StripWheelPhysics(); // Purge old caches

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

        BuildWheelPhysics(); // Rebuild fresh joints

        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero; 
        
        // FIX 1: Reapply the Custom Center of Mass!
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
        foreach (var w in wheels) if (w.rb != null) w.rb.isKinematic = false;
        
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

            InputManager inputObj = FindObjectOfType<InputManager>();
            if (inputObj != null) { inputObj.SetPlayerInputEnable(false); inputObj.SetLookEnabled(false); }

            PlayerMotor player = FindObjectOfType<PlayerMotor>();
            if (player != null) player.enabled = false;
        }
    }

    public void CloseInfoPanel()
    {
        if (vehicleInfoPanel != null) vehicleInfoPanel.SetActive(false);

        foreach (GameObject ui in temporarilyHiddenPanels) if (ui != null) ui.SetActive(true);
        temporarilyHiddenPanels.Clear();

        InputManager inputObj = FindObjectOfType<InputManager>();
        if (inputObj != null) { inputObj.SetPlayerInputEnable(true); inputObj.SetLookEnabled(true); }

        PlayerMotor player = FindObjectOfType<PlayerMotor>();
        if (player != null) player.enabled = true;
    }

    public void StopAndFreezeForWin()
    {
        isDriving = false;
        isParkedAtFinish = true; 
        currentMotorSpeed = 0f;
        
        rb.isKinematic = true;
        foreach (var w in wheels) 
        { 
            if (w.rb != null) w.rb.isKinematic = true; 
            if (w.hinge != null) w.hinge.useMotor = false; 
        }
        rb.Sleep(); 
    }

    public void StopAndReset()
    {
        isDriving = false;
        currentMotorSpeed = 0f;

        rb.isKinematic = true;
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        
        // FIX 1: Reapply Custom Center of Mass!
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

        StripWheelPhysics(); // FIX 2: Destroy physics to flush PhysX

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
                hasReachedEnd = true; 
                isDriving = false;
                currentMotorSpeed = 0f;
                foreach (var w in wheels) { if (w.hinge != null) w.hinge.useMotor = false; }
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