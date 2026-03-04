using UnityEngine;

public class PlayerRespawn : MonoBehaviour
{
    [Header("Respawn Settings")]
    [Tooltip("The Y-axis height at which the player will 'die' and respawn.")]
    public float fallThreshold = -15f; 
    
    private Vector3 respawnPosition;
    private Quaternion respawnRotation;
    
    // Cached references for movement components
    private Rigidbody rb;
    private CharacterController cc;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        cc = GetComponent<CharacterController>();
        
        // Set the initial safe point to wherever the player starts the level
        SetRespawnPoint(transform.position, transform.rotation);
    }

    private void Update()
    {
        // Check if the player has fallen past the threshold into the void
        if (transform.position.y < fallThreshold)
        {
            Respawn();
        }
    }

    public void SetRespawnPoint(Vector3 safePosition, Quaternion safeRotation)
    {
        respawnPosition = safePosition;
        respawnRotation = safeRotation;
    }

    public void Respawn()
    {
        Debug.Log("<color=red>Player fell! Respawning at the last safe point.</color>");

        // 1. Disable CharacterController temporarily so Unity allows the teleport
        if (cc != null) cc.enabled = false;

        // 2. Teleport the player
        transform.position = respawnPosition;
        transform.rotation = respawnRotation;

        // 3. Reset any falling momentum if using a Rigidbody
        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        // 4. Turn the CharacterController back on
        if (cc != null) cc.enabled = true;
    }
}