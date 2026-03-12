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
    public float speed = 5f;
    public float vehicleMass = 1000f;
    [Tooltip("How far down to check for the bridge. Adjust if your vehicle is tall.")]
    public float groundedRaycastLength = 1.5f;

    [Header("System")]
    public BridgePhysicsManager physicsManager;

    private Rigidbody rb;
    private bool isDriving = false;
    private bool wasSimulating = false;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        
        // Configure the Rigidbody for bridge physics
        rb.mass = vehicleMass;
        rb.isKinematic = true; 
        rb.useGravity = true; // Ensure gravity is ON
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

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

        // --- THE FIX: GROUNDED CHECK ---
        // Shoot a laser down to see if we are standing on a bridge or ground.
        bool isGrounded = Physics.Raycast(transform.position, Vector3.down, groundedRaycastLength);

        if (isGrounded)
        {
            // Calculate horizontal direction (ignoring Y so it doesn't try to fly)
            Vector3 direction = (endPoint.position - transform.position);
            direction.y = 0; 
            direction.Normalize();

            // Drive forward, but preserve the natural Y velocity (gravity)
            rb.velocity = new Vector3(direction.x * speed, rb.velocity.y, 0f);
        }
        // If isGrounded is false (no bridge), we do nothing. The vehicle's forward momentum 
        // will naturally die out, and gravity will pull it straight down into the ravine!
    }

    // Draws a line in the editor so you can see the Grounded Check distance
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawLine(transform.position, transform.position + (Vector3.down * groundedRaycastLength));
    }
}