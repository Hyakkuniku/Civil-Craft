using UnityEngine;
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

    private void Start()
    {
        // The moment the scene starts, take a "snapshot" of where everything is
        foreach (var obj in objectsToReset)
        {
            if (obj.objectTransform != null)
            {
                obj.startPos = obj.objectTransform.position;
                obj.startRot = obj.objectTransform.rotation;
                
                // Cache physics components so we can reset momentum later
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
        Debug.Log("<color=cyan>Player fell! Resetting level (keeping bridge progress)...</color>");

        // 1. Reset all registered objects (Player, Cargo, etc.) back to square one
        foreach (var obj in objectsToReset)
        {
            if (obj.objectTransform == null) continue;

            // Disable CharacterController to allow Unity to teleport it
            if (obj.cc != null) obj.cc.enabled = false;

            // Teleport back to the exact starting spot
            obj.objectTransform.position = obj.startPos;
            obj.objectTransform.rotation = obj.startRot;

            // Kill any falling momentum so they don't keep falling after teleporting
            if (obj.rb != null)
            {
                obj.rb.velocity = Vector3.zero;
                obj.rb.angularVelocity = Vector3.zero;
            }

            // Turn CharacterController back on
            if (obj.cc != null) obj.cc.enabled = true;
        }

        // 2. Reset the bridge physics! 
        // This stops the simulation and snaps the bridge pieces back to their un-broken, built state.
        if (BuildUIController.Instance != null)
        {
            BuildUIController.Instance.OnRestartButtonClicked();
        }
    }
}