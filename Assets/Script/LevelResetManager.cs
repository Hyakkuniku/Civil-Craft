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

    [Tooltip("The player must remain continuously grounded for this long before a new respawn point is accepted.")]
    [SerializeField, Min(0f)] private float safeGroundedDuration = 0.75f;

    [Tooltip("Maximum downward change between automatic checkpoints. Large falls into a canyon cannot replace the checkpoint on the bank.")]
    [SerializeField, Min(0.1f)] private float maximumCheckpointDrop = 2f;

    [Tooltip("Maximum ground angle that can be used as a respawn point.")]
    [SerializeField, Range(0f, 89f)] private float maximumSafeGroundSlope = 50f;

    [Tooltip("How far below the CharacterController's feet to search for supporting ground.")]
    [SerializeField, Min(0.1f)] private float safeGroundProbeDistance = 1f;

    [Tooltip("Physics layers that may support a safe player respawn. Triggers are always ignored.")]
    [SerializeField] private LayerMask safeGroundLayers = ~0;

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
    private float groundedSince = -1f;
    private bool hasSafePlayerPose;
    private bool initializationComplete;
    private readonly RaycastHit[] groundHits = new RaycastHit[16];

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
                groundedSince = -1f;
                return;
            }

            if (groundedSince < 0f)
            {
                groundedSince = Time.unscaledTime;
                return;
            }

            if (Time.unscaledTime - groundedSince < Mathf.Max(0f, safeGroundedDuration))
                return;

            // During bridge testing, the ground under the player can disappear.
            // Keep the safe pose on the bank from immediately before simulation.
            if (bridgeManager != null && bridgeManager.isSimulating)
                return;

            if (!HasSafeWalkableGround(playerTransform.position))
                return;

            // A normal slope or staircase updates in small increments. A large
            // vertical drop means the player fell into a ravine or onto buried
            // collision and must keep the checkpoint from before the fall.
            if (hasSafePlayerPose &&
                playerTransform.position.y < lastSafePlayerPosition.y -
                Mathf.Max(0.1f, maximumCheckpointDrop))
            {
                return;
            }
        }

        lastSafePlayerPosition = playerTransform.position;
        lastSafePlayerRotation = playerTransform.rotation;
        hasSafePlayerPose = true;
        groundedSince = Time.unscaledTime;
    }

    /// <summary>
    /// Authored teleports such as minimap fast travel are trusted arrival points.
    /// Registering them explicitly lets a legitimate lower destination establish a
    /// new baseline without weakening automatic canyon-fall protection.
    /// </summary>
    public void RegisterCurrentPlayerPoseAsSafe()
    {
        ResolvePlayerReferences();
        if (playerTransform == null) return;

        lastSafePlayerPosition = playerTransform.position;
        lastSafePlayerRotation = playerTransform.rotation;
        hasSafePlayerPose = true;
        groundedSince = Time.unscaledTime;
        nextSafePositionSampleTime = Time.unscaledTime +
                                     Mathf.Max(0.05f, safePositionSampleInterval);
    }

    public bool TryGetLastSafePlayerPosition(out Vector3 position)
    {
        position = lastSafePlayerPosition;
        return useLastSafePlayerPosition && hasSafePlayerPose;
    }

    private bool HasSafeWalkableGround(Vector3 playerPosition)
    {
        if (playerController == null)
            playerController = playerTransform != null
                ? playerTransform.GetComponent<CharacterController>()
                : null;

        float bottomOffset = playerController != null
            ? playerController.center.y - playerController.height * 0.5f
            : 0f;
        float probeRadius = playerController != null
            ? Mathf.Max(0.05f, playerController.radius * 0.45f)
            : 0.15f;
        float startHeight = Mathf.Max(0.2f,
            playerController != null ? playerController.stepOffset + 0.1f : 0.35f);
        Vector3 origin = playerPosition + Vector3.up * (bottomOffset + startHeight);
        float distance = startHeight + Mathf.Max(0.1f, safeGroundProbeDistance);

        int hitCount = Physics.SphereCastNonAlloc(
            origin,
            probeRadius,
            Vector3.down,
            groundHits,
            distance,
            safeGroundLayers,
            QueryTriggerInteraction.Ignore);

        float bestDistance = float.PositiveInfinity;
        bool foundWalkableGround = false;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = groundHits[i];
            if (hit.collider == null || IsPlayerCollider(hit.collider)) continue;

            float slope = Vector3.Angle(hit.normal, Vector3.up);
            if (slope > Mathf.Clamp(maximumSafeGroundSlope, 0f, 89f)) continue;

            if (hit.distance < bestDistance)
            {
                bestDistance = hit.distance;
                foundWalkableGround = true;
            }
        }

        return foundWalkableGround;
    }

    private bool IsPlayerCollider(Collider candidate)
    {
        return candidate != null && playerTransform != null &&
               (candidate.transform == playerTransform ||
                candidate.transform.IsChildOf(playerTransform));
    }

    private void OnValidate()
    {
        safePositionSampleInterval = Mathf.Max(0.05f, safePositionSampleInterval);
        respawnHeightOffset = Mathf.Max(0f, respawnHeightOffset);
        safeHeightAboveDeathThreshold = Mathf.Max(0f, safeHeightAboveDeathThreshold);
        safeGroundedDuration = Mathf.Max(0f, safeGroundedDuration);
        maximumCheckpointDrop = Mathf.Max(0.1f, maximumCheckpointDrop);
        maximumSafeGroundSlope = Mathf.Clamp(maximumSafeGroundSlope, 0f, 89f);
        safeGroundProbeDistance = Mathf.Max(0.1f, safeGroundProbeDistance);
    }
}
