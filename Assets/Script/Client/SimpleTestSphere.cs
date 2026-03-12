using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class SimpleTestSphere : MonoBehaviour
{
    [Header("Testing Physics")]
    [Tooltip("How hard the sphere tries to spin forward")]
    public float rollTorque = 5000f; 
    public float maxSpeed = 10f;
    
    [HideInInspector] public bool isDriving = false;

    private Rigidbody rb;
    private BridgePhysicsManager physicsManager;

    // --- NEW: Variables to track state and starting position ---
    private Vector3 initialPosition;
    private Quaternion initialRotation;
    private bool wasSimulating = false;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        physicsManager = FindObjectOfType<BridgePhysicsManager>();

        // Save starting position so we can teleport back when simulation stops
        initialPosition = transform.position;
        initialRotation = transform.rotation;

        // Force it to be kinematic (frozen) when the game starts
        rb.isKinematic = true;
    }

    private void Update()
    {
        if (physicsManager == null) return;

        // --- NEW: Watch for simulation state changes ---
        if (physicsManager.isSimulating && !wasSimulating)
        {
            // Simulation just started: unfreeze the sphere so gravity grabs it
            rb.isKinematic = false;
            wasSimulating = true;
        }
        else if (!physicsManager.isSimulating && wasSimulating)
        {
            // Simulation just stopped: freeze it, kill momentum, and teleport back
            rb.isKinematic = true;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            transform.position = initialPosition;
            transform.rotation = initialRotation;

            isDriving = false;
            wasSimulating = false;
        }
    }

    // Your UI Button calls this!
    public void StartDriving()
    {
        if (physicsManager != null && physicsManager.isSimulating)
        {
            isDriving = true;
            Debug.Log("<color=green>TEST SPHERE IS ROLLING!</color>");
        }
        else
        {
            Debug.LogWarning("Cannot move! Hit the Simulate button first.");
        }
    }

    private void FixedUpdate()
    {
        // If we have permission to drive, spin the sphere!
        if (physicsManager != null && physicsManager.isSimulating && isDriving)
        {
            Vector3 flatVelocity = new Vector3(rb.velocity.x, 0, rb.velocity.z);
            
            if (flatVelocity.magnitude < maxSpeed)
            {
                // AddTorque around the local X-axis makes it roll forward along the Z-axis
                rb.AddTorque(transform.right * rollTorque * Time.fixedDeltaTime, ForceMode.Acceleration);
            }
        }
    }
}