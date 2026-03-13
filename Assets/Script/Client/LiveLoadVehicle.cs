using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class LiveLoadVehicle : MonoBehaviour
{
    [Header("Path Settings")]
    [Tooltip("Where the vehicle spawns when driving starts.")]
    public Transform startPoint;
    [Tooltip("The destination the vehicle is driving towards.")]
    public Transform endPoint;

    [Header("Vehicle Settings")]
    [Tooltip("Target top speed.")]
    public float speed = 5f;
    [Tooltip("How hard the engine pushes to reach top speed.")]
    public float accelerationForce = 10f;
    public float vehicleMass = 1000f;
    [Tooltip("How far down to check for the bridge. Adjust if your vehicle is tall.")]
    public float groundedRaycastLength = 1.5f;

    [Header("System")]
    public BridgePhysicsManager physicsManager;

    private Rigidbody rb;
    private Collider vehicleCollider;
    private bool isDriving = false;
    private bool wasSimulating = false;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        vehicleCollider = GetComponent<Collider>();
        
        // Configure the Rigidbody for bridge physics
        rb.mass = vehicleMass;
        rb.isKinematic = true; 
        rb.useGravity = true; // Ensure gravity is ON
        
        // Continuous Dynamic is required so heavy, fast objects don't clip through thin bridge bars
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic; 

        // --- STABILITY FIX 1: Lock Rotation ---
        // Lock rotation so the vehicle doesn't nose-dive into the bridge joints.
        // Lock Z position so it doesn't fall off the side of the 2D bridge.
        rb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionZ;

        // --- STABILITY FIX 2: Zero-Friction Sled ---
        // Automatically apply a frictionless material so it glides smoothly over seams
        if (vehicleCollider != null)
        {
            PhysicMaterial smoothSledMat = new PhysicMaterial("SmoothSled");
            smoothSledMat.dynamicFriction = 0f;
            smoothSledMat.staticFriction = 0f;
            smoothSledMat.frictionCombine = PhysicMaterialCombine.Minimum;
            smoothSledMat.bounciness = 0f;
            smoothSledMat.bounceCombine = PhysicMaterialCombine.Minimum;
            
            vehicleCollider.material = smoothSledMat;
        }

        if (physicsManager == null)
            physicsManager = FindObjectOfType<BridgePhysicsManager>();
    }

    private void Update()
    {
        if (physicsManager == null) return;

        // Auto-start driving when Simulation begins
        if (physicsManager.isSimulating && !wasSimulating)
        {
            StartDriving();
            wasSimulating = true;
        }
        // Auto-reset when Simulation stops
        else if (!physicsManager.isSimulating && wasSimulating)
        {
            StopAndReset();
            wasSimulating = false;
        }
    }

    public void StartDriving()
    {
        if (startPoint != null)
        {
            transform.position = startPoint.position;
            transform.rotation = startPoint.rotation;
        }

        rb.isKinematic = false;
        rb.velocity = Vector3.zero; // Clear any weird leftover momentum
        isDriving = true;
    }

    public void StopAndReset()
    {
        isDriving = false;
        rb.isKinematic = true;
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        if (startPoint != null)
        {
            transform.position = startPoint.position;
            transform.rotation = startPoint.rotation;
        }
    }

    private void FixedUpdate()
    {
        if (!isDriving || endPoint == null) return;

        // Stop the vehicle if it reaches the destination's X coordinate
        if (Mathf.Abs(transform.position.x - endPoint.position.x) < 0.5f)
        {
            isDriving = false;
            rb.velocity = new Vector3(0, rb.velocity.y, 0); // Stop horizontal movement
            return;
        }

        // Shoot a laser down to see if we are standing on a bridge or ground.
        bool isGrounded = Physics.Raycast(transform.position, Vector3.down, groundedRaycastLength);

        if (isGrounded)
        {
            // Calculate horizontal direction (ignoring Y so it doesn't try to fly)
            Vector3 direction = (endPoint.position - transform.position);
            direction.y = 0; 
            direction.Normalize();

            // --- STABILITY FIX 3: Physical Push instead of Infinite Velocity ---
            // Check current horizontal speed
            Vector3 flatVelocity = new Vector3(rb.velocity.x, 0, rb.velocity.z);

            // Only push forward if we haven't reached top speed yet
            if (flatVelocity.magnitude < speed)
            {
                // Push the vehicle forward smoothly. We multiply by mass so heavy objects actually move.
                rb.AddForce(direction * accelerationForce * vehicleMass, ForceMode.Force);
            }
        }
    }

    // Draws a line in the editor so you can see the Grounded Check distance
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawLine(transform.position, transform.position + (Vector3.down * groundedRaycastLength));
    }
}