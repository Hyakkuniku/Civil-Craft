using UnityEngine;
using UnityEngine.Events; 
using System.Collections;
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

    [Header("Player Respawn")]
    [Tooltip("Respawn the player at their latest grounded position instead of the scene's initial spawn.")]
    [SerializeField] private bool useLastSafePlayerPosition = true;

    [Tooltip("How often the grounded player position is remembered. This is kept in memory and does not write the save file.")]
    [SerializeField, Min(0.05f)] private float safePositionSampleInterval = 0.2f;

    [Tooltip("Places the CharacterController slightly above the remembered ground to avoid spawning inside it.")]
    [SerializeField, Min(0f)] private float respawnHeightOffset = 0.15f;

    [Tooltip("Positions too close to the death height are never accepted as safe.")]
    [SerializeField, Min(0f)] private float safeHeightAboveDeathThreshold = 2f;

    [Header("Objects to Reset (Square One)")]
    [Tooltip("Add the Player, Cargo, and any vehicles to this list.")]
    public List<ResetableObject> objectsToReset = new List<ResetableObject>();

    [Header("Custom Reset Actions")]
    [Tooltip("Use this to call the 'Drop' function on your player's grabbing script.")]
    public UnityEvent onReset; 

    private CharacterController playerController;
    private BridgePhysicsManager bridgeManager;
    private Vector3 lastSafePlayerPosition;
    private Quaternion lastSafePlayerRotation;
    private float nextSafePositionSampleTime;
    private bool hasSafePlayerPose;
    private bool initializationComplete;

    private IEnumerator Start()
    {
        ResolvePlayerReferences();
        bridgeManager = FindObjectOfType<BridgePhysicsManager>(true);

        // PlayerSpawnManager also runs in Start. Waiting one frame ensures the
        // fallback snapshot is the door/save arrival position, not the authored
        // scene spawn that existed before PlayerSpawnManager finished.
        yield return null;

        CacheResetObjects();
        CaptureSafePlayerPose(true);
        initializationComplete = true;
    }

    private void ResolvePlayerReferences()
    {
        if (playerTransform == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
                playerTransform = player.transform;
        }

        if (playerTransform == null)
            return;

        playerController = playerTransform.GetComponent<CharacterController>();

        bool playerAlreadyRegistered = objectsToReset.Exists(
            resetable => resetable != null && resetable.objectTransform == playerTransform);
        if (!playerAlreadyRegistered)
            objectsToReset.Add(new ResetableObject { objectTransform = playerTransform });
    }

    private void CacheResetObjects()
    {
        // Take a fallback snapshot for cargo, vehicles, and any scene objects.
        foreach (var obj in objectsToReset)
        {
            if (obj == null || obj.objectTransform == null) continue;

            obj.startPos = obj.objectTransform.position;
            obj.startRot = obj.objectTransform.rotation;
            obj.startParent = obj.objectTransform.parent;

            // Cache physics components
            obj.rb = obj.objectTransform.GetComponent<Rigidbody>();
            obj.cc = obj.objectTransform.GetComponent<CharacterController>();
        }
    }

    private void Update()
    {
        if (!initializationComplete)
            return;

        CaptureSafePlayerPose(false);

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
        if (bridgeManager == null)
            bridgeManager = FindObjectOfType<BridgePhysicsManager>(true);

        if (bridgeManager != null && bridgeManager.isSimulating)
        {
            bridgeManager.StopPhysicsAndReset();
            
            BarCreator bc = FindObjectOfType<BarCreator>();
            if (bc != null) bc.isSimulating = false;
        }

        // 2. Reset all registered objects (Player, Cargo) back to the ledge
        foreach (var obj in objectsToReset)
        {
            if (obj == null || obj.objectTransform == null) continue;

            bool isPlayer = obj.objectTransform == playerTransform;
            Vector3 resetPosition = isPlayer && useLastSafePlayerPosition && hasSafePlayerPose
                ? lastSafePlayerPosition + Vector3.up * respawnHeightOffset
                : obj.startPos;
            Quaternion resetRotation = isPlayer && useLastSafePlayerPosition && hasSafePlayerPose
                ? lastSafePlayerRotation
                : obj.startRot;

            // Disable CharacterController to allow Unity to teleport it
            if (obj.cc != null) obj.cc.enabled = false;

            // Un-stick the object from the player's hand
            obj.objectTransform.SetParent(obj.startParent);

            // The player returns to their latest safe ground. Other registered
            // objects keep their original reset behavior.
            obj.objectTransform.position = resetPosition;
            obj.objectTransform.rotation = resetRotation;

            // Kill falling momentum
            if (obj.rb != null)
            {
                obj.rb.velocity = Vector3.zero;
                obj.rb.angularVelocity = Vector3.zero;
            }

            // Turn CharacterController back on
            if (obj.cc != null) obj.cc.enabled = true;

            if (isPlayer)
            {
                PlayerMotor motor = obj.objectTransform.GetComponent<PlayerMotor>();
                if (motor != null)
                    motor.ResetTestMotion();
            }
        }

        // The guide's cached path begins at the position where the player fell.
        // Preserve its destination/progress, but rebuild it from the respawned
        // player position on the next frame.
        if (PathGuider.Instance != null)
            PathGuider.Instance.RefreshFromPlayerPosition();
    }

    private void CaptureSafePlayerPose(bool force)
    {
        if (!useLastSafePlayerPosition || playerTransform == null)
            return;

        if (!force)
        {
            if (Time.unscaledTime < nextSafePositionSampleTime)
                return;

            nextSafePositionSampleTime =
                Time.unscaledTime + Mathf.Max(0.05f, safePositionSampleInterval);

            if (playerTransform.position.y <=
                deathThreshold + Mathf.Max(0f, safeHeightAboveDeathThreshold))
            {
                return;
            }

            if (playerController == null)
                playerController = playerTransform.GetComponent<CharacterController>();
            if (playerController != null &&
                (!playerController.enabled || !playerController.isGrounded))
            {
                return;
            }

            // During bridge testing, the ground under the player can disappear.
            // Keep the safe pose on the bank from immediately before simulation.
            if (bridgeManager != null && bridgeManager.isSimulating)
                return;
        }

        lastSafePlayerPosition = playerTransform.position;
        lastSafePlayerRotation = playerTransform.rotation;
        hasSafePlayerPose = true;
    }

    private void OnValidate()
    {
        safePositionSampleInterval = Mathf.Max(0.05f, safePositionSampleInterval);
        respawnHeightOffset = Mathf.Max(0f, respawnHeightOffset);
        safeHeightAboveDeathThreshold = Mathf.Max(0f, safeHeightAboveDeathThreshold);
    }
}
