using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class LiveLoadVehicle : MonoBehaviour
{
    [Header("Path Settings")]
    public Transform startPoint;
    public Transform endPoint;

    [Header("Engine & Chassis")]
    [Tooltip("Target top speed in m/s.")]
    public float maxSpeed = 5f;
    [Tooltip("How hard the engine rotates the wheels to pull the mass.")]
    public float engineTorque = 1500f; 
    
    [Tooltip("Fallback mass if no contract is active.")]
    public float vehicleMass = 1000f;
    
    [Tooltip("Lowers the center of gravity so the car doesn't do a backflip!")]
    public float centerOfMassOffset = -0.5f; 

    [Header("Custom Wheel Setup")]
    [Tooltip("Drag your 4 wheel GameObjects here from the hierarchy!")]
    public GameObject[] wheelObjects;
    [Tooltip("Adjust this so the cyan sphere perfectly wraps your wheel meshes.")]
    public float wheelRadius = 0.4f;
    [Tooltip("Mass of each individual wheel in kg.")]
    public float wheelMass = 50f;
    [Tooltip("Which direction the wheels spin. Change to (0,0,1) or (0,1,0) if they wobble!")]
    public Vector3 spinAxis = new Vector3(1, 0, 0); 

    [Header("System")]
    public BridgePhysicsManager physicsManager;

    private Rigidbody rb;
    private bool isDriving = false;
    private bool wasSimulating = false;

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
        
        // Initial fallback mass (will be overwritten by the contract later)
        rb.mass = vehicleMass;
        rb.isKinematic = true; 
        rb.useGravity = true; 
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic; 

        rb.centerOfMass = new Vector3(0, centerOfMassOffset, 0);

        rb.constraints = RigidbodyConstraints.FreezeRotationY | 
                         RigidbodyConstraints.FreezePositionZ;

        Collider chassisCol = GetComponent<Collider>();
        if (chassisCol != null)
        {
            PhysicMaterial slipMat = new PhysicMaterial("ChassisSlip");
            slipMat.dynamicFriction = 0f; slipMat.staticFriction = 0f; slipMat.bounciness = 0f;
            chassisCol.material = slipMat;
        }

        PhysicMaterial wheelMat = new PhysicMaterial("WheelGrip");
        wheelMat.dynamicFriction = 1f;
        wheelMat.staticFriction = 1f;
        wheelMat.frictionCombine = PhysicMaterialCombine.Maximum;
        wheelMat.bounciness = 0f;

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
            sc.radius = wheelRadius;
            sc.material = wheelMat;

            if (chassisCol != null)
            {
                Physics.IgnoreCollision(chassisCol, sc, true);
            }

            wd.rb = physWheel.AddComponent<Rigidbody>();
            wd.rb.mass = wheelMass;
            wd.rb.isKinematic = true;
            wd.rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            wd.hinge = physWheel.AddComponent<HingeJoint>();
            wd.hinge.connectedBody = rb;
            
            wd.hinge.axis = spinAxis; 
            
            JointMotor motor = wd.hinge.motor;
            motor.force = engineTorque;
            motor.freeSpin = false;
            wd.hinge.motor = motor;
            wd.hinge.useMotor = false;

            wheels.Add(wd);
        }

        if (physicsManager == null)
            physicsManager = FindObjectOfType<BridgePhysicsManager>();
    }

    private void Update()
    {
        if (physicsManager == null) return;

        if (physicsManager.isSimulating && !wasSimulating)
        {
            StartDriving();
            wasSimulating = true;
        }
        else if (!physicsManager.isSimulating && wasSimulating)
        {
            StopAndReset();
            wasSimulating = false;
        }
    }

    public void StartDriving()
    {
        // --- NEW: Dynamically fetch the vehicle weight from the Contract! ---
        if (GameManager.Instance != null && GameManager.Instance.CurrentContract != null)
        {
            vehicleMass = GameManager.Instance.CurrentContract.liveLoadWeight;
            if (rb != null) rb.mass = vehicleMass;
            Debug.Log($"<color=cyan>Vehicle mass dynamically set to {vehicleMass}kg from the Contract.</color>");
        }

        if (startPoint != null)
        {
            transform.position = startPoint.position;
            transform.rotation = startPoint.rotation;

            foreach (var w in wheels)
            {
                w.physObj.transform.localPosition = w.originalLocalPos;
                w.physObj.transform.localRotation = w.originalLocalRot;
            }
        }

        rb.isKinematic = false;
        rb.velocity = Vector3.zero;
        
        foreach (var w in wheels)
        {
            w.rb.isKinematic = false;
            w.rb.velocity = Vector3.zero;
        }

        isDriving = true;
    }

    public void StopAndReset()
    {
        isDriving = false;

        rb.isKinematic = true;
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        foreach (var w in wheels)
        {
            w.rb.isKinematic = true;
            w.rb.velocity = Vector3.zero;
            w.rb.angularVelocity = Vector3.zero;
            w.hinge.useMotor = false; 
        }

        if (startPoint != null)
        {
            transform.position = startPoint.position;
            transform.rotation = startPoint.rotation;

            foreach (var w in wheels)
            {
                w.physObj.transform.localPosition = w.originalLocalPos;
                w.physObj.transform.localRotation = w.originalLocalRot;
            }
        }
    }

    private void FixedUpdate()
    {
        if (!isDriving || endPoint == null) return;

        if (Mathf.Abs(transform.position.x - endPoint.position.x) < 0.5f)
        {
            isDriving = false;
            foreach (var w in wheels) w.hinge.useMotor = false;
            return;
        }

        bool isGrounded = false;
        foreach (var w in wheels)
        {
            if (Physics.Raycast(w.physObj.transform.position, Vector3.down, wheelRadius + 0.2f))
            {
                isGrounded = true;
                break;
            }
        }

        if (isGrounded)
        {
            float directionX = Mathf.Sign(endPoint.position.x - transform.position.x);
            float speedDegPerSec = (maxSpeed / wheelRadius) * Mathf.Rad2Deg;

            foreach (var w in wheels)
            {
                JointMotor motor = w.hinge.motor;
                motor.targetVelocity = speedDegPerSec * -directionX; 
                w.hinge.motor = motor;
                w.hinge.useMotor = true;
            }
        }
        else
        {
            foreach (var w in wheels) w.hinge.useMotor = false;
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