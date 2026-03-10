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

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        physicsManager = FindObjectOfType<BridgePhysicsManager>();
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
        else
        {
            // If simulation stops, cut the engine (gravity and friction will slow it down)
            isDriving = false;
        }
    }
}