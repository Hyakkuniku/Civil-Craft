using UnityEngine;
using UnityEngine.Events; 
using System.Collections.Generic;

public class LevelResetManager : MonoBehaviour
{
    [System.Serializable]
    public class ResetableObject
    {
        [Tooltip("Drag the object here (Player, Cargo, etc.)")]
        public Transform objectTransform;
        
        [HideInInspector] public Vector3 startPos;
        [HideInInspector] public Quaternion startRot;
        [HideInInspector] public Transform startParent; 
        [HideInInspector] public Rigidbody rb;
        [HideInInspector] public CharacterController cc;
    }

    [Header("Death Settings")]
    [Tooltip("Drag the Player in here so the script can track their height.")]
    public Transform playerTransform;
    
    [Tooltip("The Y-axis height at which the player dies and the reset triggers.")]
    public float deathThreshold = -15f;

    [Header("Objects to Reset (Square One)")]
    [Tooltip("Add the Player, Cargo, and any vehicles to this list.")]
    public List<ResetableObject> objectsToReset = new List<ResetableObject>();

    [Header("Custom Reset Actions")]
    [Tooltip("Use this to call the 'Drop' function on your player's grabbing script.")]
    public UnityEvent onReset; 

    private void Start()
    {
        // Take a "snapshot" of where everything is
        foreach (var obj in objectsToReset)
        {
            if (obj.objectTransform != null)
            {
                obj.startPos = obj.objectTransform.position;
                obj.startRot = obj.objectTransform.rotation;
                obj.startParent = obj.objectTransform.parent; 
                
                // Cache physics components
                obj.rb = obj.objectTransform.GetComponent<Rigidbody>();
                obj.cc = obj.objectTransform.GetComponent<CharacterController>();
            }
        }
    }

    private void Update()
    {
        // Constantly check if the player has fallen past the death line
        if (playerTransform != null && playerTransform.position.y < deathThreshold)
        {
            TriggerReset();
        }
    }

    public void TriggerReset()
    {
        Debug.Log("<color=cyan>Player fell! Resetting player and cargo, stopping simulation...</color>");

        // 1. Tell the player's scripts to let go of the item!
        onReset?.Invoke();

        // --- THE FIX: We MUST stop the physics simulation, otherwise the bridge stays broken! ---
        BridgePhysicsManager bridgeManager = FindObjectOfType<BridgePhysicsManager>();
        if (bridgeManager != null && bridgeManager.isSimulating)
        {
            bridgeManager.StopPhysicsAndReset();
            
            BarCreator bc = FindObjectOfType<BarCreator>();
            if (bc != null) bc.isSimulating = false;
        }

        // 2. Reset all registered objects (Player, Cargo) back to the ledge
        foreach (var obj in objectsToReset)
        {
            if (obj.objectTransform == null) continue;

            // Disable CharacterController to allow Unity to teleport it
            if (obj.cc != null) obj.cc.enabled = false;

            // Un-stick the object from the player's hand
            obj.objectTransform.SetParent(obj.startParent);

            // Teleport back to the exact starting spot
            obj.objectTransform.position = obj.startPos;
            obj.objectTransform.rotation = obj.startRot;

            // Kill falling momentum
            if (obj.rb != null)
            {
                obj.rb.velocity = Vector3.zero;
                obj.rb.angularVelocity = Vector3.zero;
            }

            // Turn CharacterController back on
            if (obj.cc != null) obj.cc.enabled = true;
        }
    }
}