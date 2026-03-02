using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class CargoItem : Interactable
{
    private Rigidbody rb;
    private bool isHeld = false;
    private Transform originalParent;

    [Header("Cargo Settings")]
    [Tooltip("The base weight of this cargo in kg. This can be overridden by an NPC Contract.")]
    public float cargoWeight = 50f;
    
    [Header("Holding Settings")]
    [Tooltip("The empty GameObject attached to the Player's Camera where the cargo sits when held.")]
    public Transform playerHoldPoint; 

    // ADDED: Tracks the player's body so we know where to push down
    private Transform playerTransform; 

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        originalParent = transform.parent;
        
        if (rb != null)
        {
            rb.mass = cargoWeight;
        }
    }

    public void SetWeight(float newWeight)
    {
        cargoWeight = newWeight;
        if (rb != null) rb.mass = cargoWeight;
    }

    protected override void Intract()
    {
        if (!isHeld)
        {
            PickUp();
        }
        else
        {
            Drop();
        }
    }

    private void PickUp()
    {
        if (playerHoldPoint == null && Camera.main != null)
        {
            playerHoldPoint = Camera.main.transform;
        }

        // Identify the actual player body (usually the root of the camera)
        if (playerHoldPoint != null)
        {
            playerTransform = playerHoldPoint.root; 
        }

        isHeld = true;
        promptMessage = "Drop Cargo"; 

        if (rb != null)
        {
            rb.isKinematic = true; 
            rb.useGravity = false;
        }
        
        if (playerHoldPoint != null)
        {
            transform.SetParent(playerHoldPoint);
            transform.localPosition = new Vector3(0, 0, 1.5f); 
            transform.localRotation = Quaternion.identity;
        }
    }

    public void Drop()
    {
        isHeld = false;
        promptMessage = "Pick Up Cargo"; 

        transform.SetParent(originalParent);
        
        if (rb != null)
        {
            rb.isKinematic = false; 
            rb.useGravity = true;
        }
    }

    // ADDED: This forces the cargo's weight onto the bridge while you carry it!
    private void FixedUpdate()
    {
        if (isHeld && playerTransform != null)
        {
            // Shoot an invisible ray down from slightly above the player's feet
            Ray ray = new Ray(playerTransform.position + Vector3.up * 0.5f, Vector3.down);
            
            // Check if the player is standing on something (within 2 meters down)
            if (Physics.Raycast(ray, out RaycastHit hit, 2f))
            {
                // Check if the thing they are standing on is a physical object (like our bridge bars)
                Rigidbody bridgePiece = hit.collider.attachedRigidbody;
                if (bridgePiece != null)
                {
                    // Calculate actual downward force (Force = Mass * Gravity)
                    float downwardForce = cargoWeight * Mathf.Abs(Physics.gravity.y);
                    
                    // Push down on the bridge piece exactly where the player's feet are touching it!
                    bridgePiece.AddForceAtPosition(Vector3.down * downwardForce, hit.point, ForceMode.Force);
                }
            }
        }
    }
}