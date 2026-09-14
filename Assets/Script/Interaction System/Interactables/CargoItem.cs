using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class CargoItem : Interactable
{
    private Rigidbody rb;
    private bool isHeld = false;
    private Transform originalParent;
    private Animator carryAnimator;
    private PlayerMotor carryingMotor;
    private Transform leftHand, rightHand;
    private int carryLayer = -1;
    private Collider[] heldColliders;
    private bool[] colliderStates;
    private static CargoItem heldCargo;
    public static bool IsCarriedBy(Transform player) => heldCargo != null &&
        heldCargo.isHeld && heldCargo.playerTransform == player;
    [Tooltip("Offset from the midpoint of the hands, in the character's facing direction.")]
    public Vector3 handOffset = new Vector3(0f, 0.05f, 0.06f);
    public Vector3 heldRotation;
    [Range(0.05f, 1f)] public float heldScaleMultiplier = 0.4f;
    private Vector3 pickupScale;
    private Transform pickupParent;

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
        PrepareCargoColliders();
        if (string.IsNullOrWhiteSpace(promptMessage)) promptMessage = "Pick up Cargo";
        
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
        PlayerMotor motor = FindObjectOfType<PlayerMotor>();
        if (motor == null || motor.playerAnimator == null)
        {
            Debug.LogWarning("[CargoItem] Pickup needs a PlayerMotor with its Player Animator assigned.", this);
            return;
        }
        carryAnimator = motor.playerAnimator;
        leftHand = FindHand(HumanBodyBones.LeftHand, "LeftHand");
        rightHand = FindHand(HumanBodyBones.RightHand, "RightHand");
        carryLayer = carryAnimator.GetLayerIndex("Carry Pose");
        if (leftHand == null || rightHand == null || carryLayer < 0)
        {
            Debug.LogWarning($"[CargoItem] Cannot pick up: left hand={leftHand != null}, " +
                $"right hand={rightHand != null}, Carry Pose layer={carryLayer}. Check the player's rig/controller.", this);
            return;
        }
        if (heldCargo != null && heldCargo != this) heldCargo.Drop();
        heldCargo = this;
        pickupParent = transform.parent;
        pickupScale = transform.localScale;
        transform.localScale = pickupScale * Mathf.Clamp(heldScaleMultiplier, 0.05f, 1f);
        playerTransform = motor.transform;
        carryAnimator.SetLayerWeight(carryLayer, 1f);

        isHeld = true;
        carryingMotor = motor;
        carryingMotor.SetCarriedItem(this);
        promptMessage = "Drop Cargo"; 

        if (rb != null)
        {
            if (!rb.isKinematic) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            rb.isKinematic = true; 
            rb.useGravity = false;
        }
        
        heldColliders = GetComponentsInChildren<Collider>(true);
        colliderStates = new bool[heldColliders.Length];
        for (int i = 0; i < heldColliders.Length; i++)
        { colliderStates[i] = heldColliders[i].isTrigger; heldColliders[i].isTrigger = true; }
        transform.SetParent(rightHand, true);
        LateUpdate();
    }

    private Transform FindHand(HumanBodyBones humanoidBone, string genericBone)
    {
        if (carryAnimator.isHuman) return carryAnimator.GetBoneTransform(humanoidBone);
        foreach (Transform bone in carryAnimator.GetComponentsInChildren<Transform>(true))
        {
            string boneName = bone.name;
            int namespaceEnd = boneName.LastIndexOf(':');
            if (namespaceEnd >= 0) boneName = boneName.Substring(namespaceEnd + 1);
            if (string.Equals(boneName, genericBone, System.StringComparison.OrdinalIgnoreCase)) return bone;
        }
        return null;
    }

    private void Reset()
    {
        promptMessage = "Pick up Cargo";
        PrepareCargoColliders();
    }

    private void PrepareCargoColliders()
    {
        // Imported scenery commonly has concave collision. Once it becomes
        // movable cargo, PhysX requires convex meshes on its Rigidbody.
        foreach (MeshCollider mesh in GetComponentsInChildren<MeshCollider>(true))
        {
            if (mesh.sharedMesh == null || mesh.attachedRigidbody != GetComponent<Rigidbody>()) continue;
            if (!mesh.convex) mesh.convex = true;
        }
        if (GetComponentsInChildren<Collider>(true).Length == 0)
            Debug.LogWarning("[CargoItem] Add a BoxCollider sized to this item so it can rest on the ground and be picked up.", this);
    }

    private void LateUpdate()
    {
        if (!isHeld || leftHand == null || rightHand == null || carryAnimator == null) return;
        Transform facing = carryAnimator.transform;
        transform.SetPositionAndRotation((leftHand.position + rightHand.position) * .5f +
            facing.rotation * handOffset, facing.rotation * Quaternion.Euler(heldRotation));
    }

    public void Drop()
    {
        if (!isHeld) return;
        isHeld = false;
        if (carryingMotor != null) carryingMotor.SetCarriedItem(null);
        carryingMotor = null;
        if (heldCargo == this) heldCargo = null;
        if (carryAnimator != null && carryLayer >= 0) carryAnimator.SetLayerWeight(carryLayer, 0f);
        if (heldColliders != null)
            for (int i = 0; i < heldColliders.Length; i++)
                if (heldColliders[i] != null) heldColliders[i].isTrigger = colliderStates[i];
        promptMessage = "Pick up Cargo"; 

        Vector3 dropPosition = transform.position;
        Quaternion dropRotation = transform.rotation;
        transform.SetParent(pickupParent != null ? pickupParent : originalParent, false);
        // Reparent without changing the drop position or orientation.
        transform.SetPositionAndRotation(dropPosition, dropRotation);
        transform.localScale = pickupScale;
        
        if (rb != null)
        {
            rb.isKinematic = false; 
            rb.useGravity = true;
        }
    }

    private void OnDisable() { if (isHeld) Drop(); }

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
                if (bridgePiece != null && bridgePiece != rb && !bridgePiece.isKinematic)
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
