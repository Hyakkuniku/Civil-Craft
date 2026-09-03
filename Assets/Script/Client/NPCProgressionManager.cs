using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

[System.Serializable]
public class NPCProgressionPhase
{
    [Tooltip("Stable label used for debugging and Inspector organization.")]
    public string phaseId = "Phase";

    [Tooltip("World position where the NPC waits during this phase.")]
    public Transform targetLocation;

    [Tooltip("Optional ordered walking points used before Target Location when Waypoint movement is selected.")]
    public List<Transform> travelWaypoints = new List<Transform>();

    [Tooltip("Contract offered by the existing NPCContractGiver in this phase.")]
    public ContractSO contract;

    [Tooltip("Build site associated with this phase's contract.")]
    public BuildLocation targetBuildLocation;

    [Tooltip("Optional cargo whose weight should match the phase contract.")]
    public CargoItem linkedCargo;

    [Header("Optional Phase Dialogue")]
    [Tooltip("Optional dialogue for this phase. This is especially useful for travel-only phases with no Contract or Tutorial.")]
    public Dialogue phaseDialogue;

    [Tooltip("Interaction prompt shown while this contract-free phase is active.")]
    public string dialoguePrompt = "Talk";

    [Tooltip("Play the phase dialogue automatically when the NPC arrives. When disabled, the player starts it by interacting with the NPC.")]
    public bool playDialogueOnArrival;

    [Tooltip("Allow the player to replay this phase dialogue after it has finished once.")]
    public bool repeatDialogue = true;

    [Tooltip("Optional permanent feature ID granted after this dialogue finishes. It defaults to minimap for this sequence; clear it for no unlock.")]
    public string unlockFeatureIdAfterDialogue = "minimap";

    [Tooltip("Name shown by the Collect Reward popup for the dialogue feature unlock.")]
    public string featureUnlockDisplayName = "Minimap";

    [Tooltip("Optional artwork shown inside the Collect Reward popup.")]
    public Sprite featureUnlockIcon;

    [Tooltip("Require the player to press Collect before granting the feature and showing its notification.")]
    public bool showFeatureCollectPopup = true;

    [Header("Optional Material Introduction")]
    [Tooltip("Optional single material retained for existing phases. It is shown before Additional Material Introductions.")]
    public BridgeMaterialSO materialIntroduction;

    [Tooltip("Additional materials shown sequentially after the dialogue. Pressing the button advances to the next material.")]
    public List<BridgeMaterialSO> materialIntroductions = new List<BridgeMaterialSO>();

    [Tooltip("Label used instead of COLLECT on a material introduction.")]
    public string materialIntroductionButtonLabel = "GOT IT";

    [Tooltip("Show this material introduction only once during this scene visit, even if the dialogue can repeat.")]
    public bool introduceMaterialOnlyOnce = true;

    [Tooltip("Invoked after the player dismisses the material introduction panel.")]
    public UnityEvent onMaterialIntroductionClosed;

    [Tooltip("Invoked when the optional phase dialogue closes.")]
    public UnityEvent onDialogueFinished;

    [Tooltip("Invoked when the player interacts with the NPC during this phase.")]
    public UnityEvent onNPCInteracted;

    [Tooltip("Prevents repeat interactions from restarting the same tutorial/event during this scene visit.")]
    public bool invokeInteractionEventOnlyOnce = true;

    [Tooltip("Invoked after the NPC arrives and this phase becomes active.")]
    public UnityEvent onNPCArrived;
}

public enum NPCProgressionMovementMode
{
    NavMesh,
    Waypoints
}

/// <summary>
/// Moves one existing NPCContractGiver through an ordered contract sequence.
/// Contract completion is restored from PlayerData and also observed live.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
public class NPCProgressionManager : MonoBehaviour
{
    [Header("Required References")]
    [SerializeField] private NPCContractGiver contractGiver;
    [SerializeField] private NavMeshAgent navMeshAgent;
    [SerializeField] private Animator animator;
    [SerializeField] private DialogueManager dialogueManager;

    [Header("Progression")]
    [SerializeField] private List<NPCProgressionPhase> phases = new List<NPCProgressionPhase>();
    [Tooltip("Stable, unique save key for this NPC sequence. Never change it after release.")]
    [SerializeField] private string progressionSaveId = "MainContractNPC";
    [Tooltip("Save the current/destination phase in PlayerData JSON.")]
    [SerializeField] private bool persistProgression = true;
    [Tooltip("Reconstruct valid bridges belonging to earlier phase locations when this scene loads.")]
    [SerializeField] private bool restoreSavedPhaseBridgesOnStart = true;
    [Tooltip("Place the NPC at the save-resolved phase when this scene loads.")]
    [SerializeField] private bool placeAtResolvedPhaseOnStart = true;
    [Tooltip("Automatically listen for PlayerDataManager.CompleteContract calls.")]
    [SerializeField] private bool automaticallyAdvanceOnContractCompletion = true;

    [Header("Movement")]
    [Tooltip("Waypoints moves the NPC directly over walkable colliders and does not require a NavMesh path.")]
    [SerializeField] private NPCProgressionMovementMode movementMode = NPCProgressionMovementMode.NavMesh;
    [Min(0.01f)] [SerializeField] private float arrivalPadding = 0.15f;
    [Min(0.1f)] [SerializeField] private float navMeshSampleRadius = 3f;
    [Min(1f)] [SerializeField] private float pathTimeout = 45f;
    [Tooltip("Prevents progression soft-locks if a target is outside the baked NavMesh.")]
    [SerializeField] private bool warpToTargetIfPathFails = true;
    [Tooltip("Seconds without meaningful movement before the NPC recalculates its route.")]
    [Min(0.25f)] [SerializeField] private float stalledRepathDelay = 1.5f;
    [Tooltip("World-space movement that counts as forward progress.")]
    [Min(0.001f)] [SerializeField] private float stalledMovementTolerance = 0.03f;
    [Tooltip("Maximum automatic route recalculations before movement is reported as failed.")]
    [Min(0)] [SerializeField] private int maximumRepathAttempts = 3;

    [Header("Waypoint Movement (No NavMesh)")]
    [Min(0.01f)] [SerializeField] private float waypointMovementSpeed = 2.5f;
    [Min(1f)] [SerializeField] private float waypointTurnSpeed = 260f;
    [Min(0.01f)] [SerializeField] private float waypointArrivalDistance = 0.1f;
    [Tooltip("Ground, Environment, and Bridge by default. The NPC follows the top collider below each step.")]
    [SerializeField] private LayerMask waypointGroundLayers = (1 << 9) | (1 << 10) | (1 << 11);
    [Min(0.1f)] [SerializeField] private float waypointGroundProbeHeight = 2f;
    [Min(0.1f)] [SerializeField] private float waypointGroundProbeDepth = 6f;
    [SerializeField] private float waypointGroundOffset;
    [Tooltip("Stops safely instead of walking through empty air if no bridge or ground exists below the route.")]
    [SerializeField] private bool requireGroundForWaypointMovement = true;
    [Min(0f)] [SerializeField] private float missingGroundGraceTime = 0.5f;
    [Tooltip("Road nodes this close are treated as the same junction even when they are separate Point objects.")]
    [Min(0f)] [SerializeField] private float waypointRoadNodeWeldDistance = 0.25f;
    [Tooltip("Maximum walkable land gap that may connect separate road sections in one completed build site.")]
    [Min(0f)] [SerializeField] private float waypointRoadGroundConnectionDistance = 20f;
    [Tooltip("Spacing between downward ground checks while connecting separate road sections.")]
    [Min(0.1f)] [SerializeField] private float waypointGroundConnectionSampleSpacing = 0.75f;

    [Header("Animation")]
    [SerializeField] private string walkingBoolParameter = "isWalking";

    [Header("NavMesh Link Traversal")]
    [Tooltip("Moves across NavMeshLink segments at the agent's normal Speed instead of Unity's automatic traversal speed.")]
    [SerializeField] private bool manuallyTraverseNavMeshLinks = true;
    [Tooltip("Rotate toward the link endpoint while crossing.")]
    [SerializeField] private bool rotateWhileTraversingLinks = true;

    [Header("Interaction")]
    [SerializeField] private string travellingPrompt = "Moving to the next site...";

    [Header("Events")]
    public UnityEvent onProgressionFinished;
    public UnityEvent onMovementFailed;

    private readonly HashSet<int> invokedInteractionPhases = new HashSet<int>();
    private readonly HashSet<int> completedDialoguePhases = new HashSet<int>();
    private readonly HashSet<int> introducedMaterialPhases = new HashSet<int>();
    private Coroutine movementRoutine;
    private int currentPhaseIndex = -1;
    private bool subscribedToCompletion;
    private bool manualLinkTraversalActive;
    private bool savedAgentUpdatePosition;
    private bool savedAgentUpdateRotation;

    public int CurrentPhaseIndex => currentPhaseIndex;
    public int PhaseCount => phases != null ? phases.Count : 0;
    public bool IsTravelling => movementRoutine != null;
    public NPCProgressionPhase CurrentPhase =>
        currentPhaseIndex >= 0 && currentPhaseIndex < phases.Count
            ? phases[currentPhaseIndex]
            : null;

    public string GetPhaseDisplayName(int phaseIndex)
    {
        if (phases == null || phaseIndex < 0 || phaseIndex >= phases.Count)
            return $"Phase {phaseIndex}";

        NPCProgressionPhase phase = phases[phaseIndex];
        if (phase == null) return $"Phase {phaseIndex}: Missing";

        string label = string.IsNullOrWhiteSpace(phase.phaseId)
            ? $"Phase {phaseIndex}"
            : phase.phaseId;
        string contractName = phase.contract != null ? phase.contract.name : "No Contract";
        return $"{phaseIndex}: {label} - {contractName}";
    }

    public bool TryGetContractForBuildLocation(BuildLocation location, out ContractSO contract)
    {
        contract = null;
        if (location == null || phases == null) return false;

        foreach (NPCProgressionPhase phase in phases)
        {
            if (phase == null || phase.targetBuildLocation != location || phase.contract == null) continue;
            contract = phase.contract;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Developer-menu entry point. Stops travel, snaps to the phase target, and
    /// configures the existing NPCContractGiver as though that phase was reached.
    /// </summary>
    public bool DebugWarpToPhase(int phaseIndex)
    {
        if (phases == null || phaseIndex < 0 || phaseIndex >= phases.Count ||
            phases[phaseIndex] == null || phases[phaseIndex].targetLocation == null)
        {
            return false;
        }

        // Debug warping intentionally bypasses normal lesson/progression spawn
        // gates. CanyonCrossing's NPCSpawnerCondition can disable this entire
        // GameObject before the developer menu is opened.
        NPCSpawnerCondition[] spawnConditions = GetComponentsInParent<NPCSpawnerCondition>(true);
        foreach (NPCSpawnerCondition condition in spawnConditions)
        {
            if (condition != null) condition.enabled = false;
        }

        SetHierarchyActiveForDebug(transform);

        if (movementRoutine != null)
        {
            StopCoroutine(movementRoutine);
            movementRoutine = null;
        }

        RestoreAgentAfterLinkTraversal(false);
        if (navMeshAgent != null && navMeshAgent.enabled && navMeshAgent.isOnNavMesh)
        {
            navMeshAgent.isStopped = true;
            navMeshAgent.ResetPath();
        }

        SetWalkingAnimation(false);
        if (!PlaceAtPhase(phaseIndex)) return false;

        ContractSO phaseContract = phases[phaseIndex].contract;
        if (phaseContract != null)
        {
            PlayerPrefs.DeleteKey("LockedContract_" + phaseContract.ContractID);
            PlayerPrefs.Save();
        }

        invokedInteractionPhases.Remove(phaseIndex);
        ActivatePhase(phaseIndex, true);
        return true;
    }

    private void Awake()
    {
        if (contractGiver == null) contractGiver = GetComponent<NPCContractGiver>();
        if (navMeshAgent == null) navMeshAgent = GetComponent<NavMeshAgent>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (dialogueManager == null) dialogueManager = FindObjectOfType<DialogueManager>();

        if (movementMode == NPCProgressionMovementMode.Waypoints && navMeshAgent != null)
        {
            // A live agent continually writes to the Transform, so it must not
            // compete with the deterministic waypoint mover.
            navMeshAgent.enabled = false;
        }
        else if (navMeshAgent != null)
            navMeshAgent.autoTraverseOffMeshLink = !manuallyTraverseNavMeshLinks;
    }

    private void OnEnable()
    {
        if (contractGiver != null)
        {
            contractGiver.OnNPCInteracted += HandleNPCInteracted;
            contractGiver.OnOfferDialogueCompleted += HandleOfferDialogueCompleted;
        }
        TrySubscribeToContractCompletion();
    }

    private void Start()
    {
        TrySubscribeToContractCompletion();

        if (phases == null || phases.Count == 0)
        {
            Debug.LogWarning("[NPCProgressionManager] No phases are configured.", this);
            return;
        }

        TryRestoreSavedProgression(
            out currentPhaseIndex, out bool wasTravellingWhenSaved);

        if (restoreSavedPhaseBridgesOnStart)
            RestoreSavedPhaseBridges();

        // Waypoint travel can resume an interrupted transition without either
        // teleporting the NPC or requiring the completion event to fire again.
        // The saved index represents the destination phase.
        if (wasTravellingWhenSaved &&
            movementMode == NPCProgressionMovementMode.Waypoints &&
            currentPhaseIndex > 0)
        {
            int destinationPhaseIndex = currentPhaseIndex;
            int departurePhaseIndex = destinationPhaseIndex - 1;
            PlaceAtPhase(departurePhaseIndex);
            ActivatePhase(departurePhaseIndex, false);
            movementRoutine = StartCoroutine(MoveToPhaseRoutine(destinationPhaseIndex));
            return;
        }

        // A mid-travel save must always resolve at its committed destination,
        // even if normal load-time placement was disabled for editor testing.
        if (placeAtResolvedPhaseOnStart || wasTravellingWhenSaved)
            PlaceAtPhase(currentPhaseIndex);

        // Activating settles a mid-travel save and writes wasTravelling = false.
        ActivatePhase(currentPhaseIndex, false);
    }

    private void RestoreSavedPhaseBridges()
    {
        if (PlayerDataManager.Instance == null) return;

        HashSet<BuildLocation> restoredLocations = new HashSet<BuildLocation>();
        for (int i = 0; i < phases.Count; i++)
        {
            NPCProgressionPhase phase = phases[i];
            if (phase == null || phase.contract == null || phase.targetBuildLocation == null ||
                !PlayerDataManager.Instance.HasValidSavedBridge(phase.contract.ContractID)) continue;

            if (!restoredLocations.Add(phase.targetBuildLocation))
            {
                Debug.LogWarning(
                    $"[NPCProgressionManager] More than one saved phase points to build location " +
                    $"'{phase.targetBuildLocation.name}'. Only one baked bridge can own that location at a time.",
                    this);
                continue;
            }

            phase.targetBuildLocation.activeContract = phase.contract;
            if (!phase.targetBuildLocation.LoadSavedBridge())
            {
                Debug.LogError(
                    $"[NPCProgressionManager] Saved bridge '{phase.contract.name}' could not be reconstructed at " +
                    $"'{phase.targetBuildLocation.name}'.", this);
            }
        }
    }

    private void OnDisable()
    {
        if (contractGiver != null)
        {
            contractGiver.OnNPCInteracted -= HandleNPCInteracted;
            contractGiver.OnOfferDialogueCompleted -= HandleOfferDialogueCompleted;
        }
        UnsubscribeFromContractCompletion();

        if (movementRoutine != null)
        {
            StopCoroutine(movementRoutine);
            movementRoutine = null;
        }

        SetWalkingAnimation(false);
        RestoreAgentAfterLinkTraversal(false);
        if (contractGiver != null) contractGiver.SetProgressionInteractionLocked(false);
    }

    private void TrySubscribeToContractCompletion()
    {
        if (!automaticallyAdvanceOnContractCompletion || subscribedToCompletion ||
            PlayerDataManager.Instance == null) return;

        PlayerDataManager.Instance.OnContractCompleted += HandleContractCompleted;
        subscribedToCompletion = true;
    }

    private void UnsubscribeFromContractCompletion()
    {
        if (!subscribedToCompletion) return;
        if (PlayerDataManager.Instance != null)
            PlayerDataManager.Instance.OnContractCompleted -= HandleContractCompleted;
        subscribedToCompletion = false;
    }

    private int ResolvePhaseIndexFromSave()
    {
        if (PlayerDataManager.Instance == null) return 0;

        for (int i = 0; i < phases.Count; i++)
        {
            ContractSO contract = phases[i] != null ? phases[i].contract : null;
            if (contract == null || !PlayerDataManager.Instance.IsContractCompleted(contract.ContractID))
                return i;
        }

        return phases.Count - 1;
    }

    private bool TryRestoreSavedProgression(
        out int resolvedIndex,
        out bool wasTravellingWhenSaved)
    {
        resolvedIndex = ResolvePhaseIndexFromSave();
        wasTravellingWhenSaved = false;
        if (!persistProgression || PlayerDataManager.Instance == null ||
            string.IsNullOrWhiteSpace(progressionSaveId)) return false;

        if (!PlayerDataManager.Instance.TryGetNPCProgression(
                progressionSaveId, out NPCProgressionSaveData savedState))
        {
            // Migrate older saves that only tracked completed contracts.
            SaveProgressionState(resolvedIndex, false);
            return false;
        }

        wasTravellingWhenSaved = savedState.wasTravelling;

        int idMatch = FindPhaseIndexById(savedState.currentPhaseId);
        if (idMatch >= 0)
        {
            resolvedIndex = idMatch;
        }
        else
        {
            resolvedIndex = Mathf.Clamp(savedState.currentPhaseIndex, 0, phases.Count - 1);
            if (!string.IsNullOrWhiteSpace(savedState.currentPhaseId))
            {
                Debug.LogWarning(
                    $"[NPCProgressionManager] Saved phase '{savedState.currentPhaseId}' no longer exists. " +
                    $"Falling back to phase index {resolvedIndex}.", this);
            }
        }

        return true;
    }

    private int FindPhaseIndexById(string phaseId)
    {
        if (string.IsNullOrWhiteSpace(phaseId)) return -1;

        int match = -1;
        for (int i = 0; i < phases.Count; i++)
        {
            NPCProgressionPhase phase = phases[i];
            if (phase != null && string.Equals(phase.phaseId, phaseId,
                    System.StringComparison.Ordinal))
            {
                // Duplicate IDs are not stable enough for restoration. Falling
                // back to the stored index preserves older Inspector setups.
                if (match >= 0)
                {
                    Debug.LogWarning(
                        $"[NPCProgressionManager] Phase ID '{phaseId}' is duplicated. " +
                        "Using the saved phase index instead.", this);
                    return -1;
                }

                match = i;
            }
        }

        return match;
    }

    private void SaveProgressionState(int phaseIndex, bool wasTravelling)
    {
        if (!persistProgression || PlayerDataManager.Instance == null ||
            string.IsNullOrWhiteSpace(progressionSaveId) ||
            phaseIndex < 0 || phaseIndex >= phases.Count) return;

        NPCProgressionPhase phase = phases[phaseIndex];
        PlayerDataManager.Instance.SaveNPCProgression(
            progressionSaveId,
            phaseIndex,
            phase != null ? phase.phaseId : string.Empty,
            wasTravelling);
    }

    private void HandleContractCompleted(string completedContractName)
    {
        NPCProgressionPhase phase = CurrentPhase;
        if (phase == null || phase.contract == null) return;
        if (!phase.contract.MatchesIdentifier(completedContractName)) return;

        AdvanceToNextContract();
    }

    /// <summary>
    /// Inspector-callable entry point. Call this after the current phase contract
    /// is completed if automatic completion listening is disabled.
    /// </summary>
    public void AdvanceToNextContract()
    {
        if (IsTravelling || phases == null || phases.Count == 0) return;

        int nextPhaseIndex = currentPhaseIndex + 1;
        if (nextPhaseIndex >= phases.Count)
        {
            onProgressionFinished?.Invoke();
            return;
        }

        movementRoutine = StartCoroutine(MoveToPhaseRoutine(nextPhaseIndex));
    }

    /// <summary>Useful for testing a phase from a UnityEvent or custom debug button.</summary>
    public void MoveToPhase(int phaseIndex)
    {
        if (IsTravelling || phaseIndex < 0 || phaseIndex >= phases.Count) return;
        movementRoutine = StartCoroutine(MoveToPhaseRoutine(phaseIndex));
    }

    private IEnumerator MoveToPhaseRoutine(int nextPhaseIndex)
    {
        // Commit the destination before the first movement frame. If the game is
        // closed anywhere during travel, the next load snaps to this safe phase.
        SaveProgressionState(nextPhaseIndex, true);

        // Lock immediately, then yield once so StartCoroutine can safely assign
        // movementRoutine before any early failure path tries to clear it.
        if (contractGiver != null)
            contractGiver.SetProgressionInteractionLocked(true, travellingPrompt);
        yield return null;

        NPCProgressionPhase nextPhase = phases[nextPhaseIndex];
        if (nextPhase == null || nextPhase.targetLocation == null)
        {
            Debug.LogError($"[NPCProgressionManager] Phase {nextPhaseIndex} has no target location.", this);
            HandleMovementFailure(nextPhaseIndex);
            yield break;
        }

        if (movementMode == NPCProgressionMovementMode.Waypoints)
        {
            yield return MoveToPhaseByWaypoints(nextPhaseIndex, nextPhase);
            yield break;
        }

        // Auto-collected contracts can complete in the same frame the bridge is
        // finalized. Do not calculate the NPC path against the old NavMesh.
        DynamicNavMeshUpdater navMeshUpdater = DynamicNavMeshUpdater.Instance;
        while (navMeshUpdater != null && navMeshUpdater.HasPendingOrRunningUpdate)
            yield return null;

        bool destinationSet = TrySetDestination(nextPhase.targetLocation.position);
        if (!destinationSet)
        {
            HandleMovementFailure(nextPhaseIndex);
            yield break;
        }

        SetWalkingAnimation(true);
        float deadline = Time.time + pathTimeout;
        float lastProgressTime = Time.time;
        Vector3 lastProgressPosition = transform.position;
        int repathAttempts = 0;

        yield return null;
        while (navMeshAgent.pathPending && Time.time < deadline)
            yield return null;

        while (Time.time < deadline)
        {
            if (manuallyTraverseNavMeshLinks && navMeshAgent.isOnOffMeshLink)
            {
                yield return TraverseCurrentNavMeshLink();
                // A long bridge link should not consume the normal path timeout.
                deadline = Time.time + pathTimeout;
                lastProgressTime = Time.time;
                lastProgressPosition = transform.position;
                continue;
            }

            if (!navMeshAgent.pathPending &&
                navMeshAgent.remainingDistance <= navMeshAgent.stoppingDistance + arrivalPadding &&
                (!navMeshAgent.hasPath || navMeshAgent.velocity.sqrMagnitude <= 0.05f))
            {
                CompleteArrival(nextPhaseIndex);
                yield break;
            }

            if (!navMeshAgent.pathPending && navMeshAgent.pathStatus == NavMeshPathStatus.PathInvalid)
                break;

            float progressToleranceSquared =
                stalledMovementTolerance * stalledMovementTolerance;
            if ((transform.position - lastProgressPosition).sqrMagnitude >= progressToleranceSquared)
            {
                lastProgressPosition = transform.position;
                lastProgressTime = Time.time;
            }
            else if (!navMeshAgent.pathPending && navMeshAgent.hasPath &&
                     navMeshAgent.remainingDistance >
                         navMeshAgent.stoppingDistance + arrivalPadding &&
                     Time.time - lastProgressTime >= stalledRepathDelay)
            {
                repathAttempts++;
                if (repathAttempts > maximumRepathAttempts ||
                    !TrySetDestination(nextPhase.targetLocation.position))
                {
                    Debug.LogWarning(
                        $"[NPCProgressionManager] NPC stalled while travelling to " +
                        $"phase {nextPhaseIndex} after {repathAttempts} route attempts.",
                        this);
                    break;
                }

                lastProgressPosition = transform.position;
                lastProgressTime = Time.time;
                deadline = Time.time + pathTimeout;
            }

            yield return null;
        }

        HandleMovementFailure(nextPhaseIndex);
    }

    private IEnumerator MoveToPhaseByWaypoints(
        int nextPhaseIndex,
        NPCProgressionPhase nextPhase)
    {
        SetWalkingAnimation(true);

        List<Vector3> routePositions = new List<Vector3>();
        bool hasInspectorRoute = nextPhase.travelWaypoints != null &&
                                 nextPhase.travelWaypoints.Exists(point => point != null);

        if (hasInspectorRoute)
        {
            foreach (Transform waypoint in nextPhase.travelWaypoints)
                if (waypoint != null) routePositions.Add(waypoint.position);
        }
        else
        {
            // A completed bridge already contains the exact road-node graph the
            // player created. Following that graph makes waypoint travel work for
            // straight, sloped, and irregular bridges without hand-authored points.
            NPCProgressionPhase departurePhase = CurrentPhase;
            BuildLocation completedLocation = departurePhase != null
                ? departurePhase.targetBuildLocation
                : null;
            TryAppendBakedRoadRoute(
                completedLocation,
                transform.position,
                nextPhase.targetLocation.position,
                routePositions);
        }

        // The phase target is always last. Inspector points are therefore a full
        // route override, while an empty list automatically follows the baked road.
        routePositions.Add(nextPhase.targetLocation.position);

        foreach (Vector3 destination in routePositions)
        {
            if ((destination - transform.position).sqrMagnitude <=
                waypointArrivalDistance * waypointArrivalDistance) continue;

            float segmentDistance = Vector3.Distance(transform.position, destination);
            float segmentTimeout = Mathf.Max(
                pathTimeout,
                segmentDistance / Mathf.Max(0.01f, waypointMovementSpeed) + 10f);
            float deadline = Time.time + segmentTimeout;
            float missingGroundSince = -1f;

            while (Time.time < deadline)
            {
                Vector3 flatOffset = destination - transform.position;
                flatOffset.y = 0f;
                if (flatOffset.sqrMagnitude <=
                    waypointArrivalDistance * waypointArrivalDistance)
                {
                    break;
                }

                Vector3 flatDirection = flatOffset.normalized;
                float step = waypointMovementSpeed * Time.deltaTime;
                Vector3 candidate = transform.position +
                                    flatDirection * Mathf.Min(step, flatOffset.magnitude);

                if (TryProjectWaypointToGround(candidate, out Vector3 groundedCandidate))
                {
                    candidate.y = groundedCandidate.y;
                    missingGroundSince = -1f;
                }
                else
                {
                    if (missingGroundSince < 0f) missingGroundSince = Time.time;
                    if (requireGroundForWaypointMovement &&
                        Time.time - missingGroundSince > missingGroundGraceTime)
                    {
                        Debug.LogError(
                            $"[NPCProgressionManager] Waypoint route to phase {nextPhaseIndex} has no " +
                            $"Ground, Environment, or Bridge collider below {candidate}.",
                            this);
                        HandleMovementFailure(nextPhaseIndex);
                        yield break;
                    }

                    // Preserve a smooth height transition across tiny collider
                    // seams instead of snapping or accumulating vertical drift.
                    candidate.y = Mathf.MoveTowards(
                        transform.position.y,
                        destination.y,
                        waypointMovementSpeed * Time.deltaTime);
                }

                transform.position = candidate;
                Quaternion desiredRotation = Quaternion.LookRotation(flatDirection, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    desiredRotation,
                    waypointTurnSpeed * Time.deltaTime);
                yield return null;
            }

            Vector3 remaining = destination - transform.position;
            remaining.y = 0f;
            if (remaining.sqrMagnitude > waypointArrivalDistance * waypointArrivalDistance)
            {
                Debug.LogWarning(
                    $"[NPCProgressionManager] Waypoint travel timed out before phase {nextPhaseIndex}.",
                    this);
                HandleMovementFailure(nextPhaseIndex);
                yield break;
            }
        }

        Vector3 finalPosition = nextPhase.targetLocation.position;
        if (TryProjectWaypointToGround(finalPosition, out Vector3 groundedFinalPosition))
            finalPosition.y = groundedFinalPosition.y;

        transform.SetPositionAndRotation(finalPosition, nextPhase.targetLocation.rotation);
        CompleteArrival(nextPhaseIndex);
    }

    private bool TryAppendBakedRoadRoute(
        BuildLocation buildLocation,
        Vector3 routeStart,
        Vector3 routeEnd,
        List<Vector3> routePositions)
    {
        if (buildLocation == null || buildLocation.bakedBars == null ||
            routePositions == null) return false;

        Dictionary<Point, List<Point>> adjacency =
            new Dictionary<Point, List<Point>>();
        int bridgeLayer = LayerMask.NameToLayer("Bridge");

        foreach (Bar bar in buildLocation.bakedBars)
        {
            if (bar == null || !bar.gameObject.activeInHierarchy ||
                bar.materialData == null || !bar.materialData.isRoad) continue;

            if (bridgeLayer >= 0) SetLayerRecursively(bar.transform, bridgeLayer);

            if (bar.startPoint == null || bar.endPoint == null)
                bar.AutoRepairEndpoints();
            if (bar.startPoint == null || bar.endPoint == null ||
                bar.startPoint == bar.endPoint) continue;

            AddRoadConnection(adjacency, bar.startPoint, bar.endPoint);
            AddRoadConnection(adjacency, bar.endPoint, bar.startPoint);
        }

        if (adjacency.Count == 0) return false;

        // A build location may contain multiple bridge spans separated by a
        // solid island/platform. It may also contain visually snapped nodes that
        // are different Point instances. Join both cases before pathfinding, but
        // only join a larger gap when every sample has walkable ground beneath it.
        int weldedNodeCount = ConnectCoincidentRoadNodes(adjacency);
        Physics.SyncTransforms();
        int groundedConnectionCount = ConnectRoadSectionsAcrossGround(adjacency);

        Point entry = FindClosestRoadPoint(adjacency.Keys, routeStart);
        Point exit = FindClosestRoadPoint(adjacency.Keys, routeEnd);
        if (entry == null || exit == null) return false;

        Dictionary<Point, float> distances = new Dictionary<Point, float>();
        Dictionary<Point, Point> previous = new Dictionary<Point, Point>();
        HashSet<Point> unvisited = new HashSet<Point>();
        foreach (Point point in adjacency.Keys)
        {
            if (point == null) continue;
            distances[point] = point == entry ? 0f : float.PositiveInfinity;
            unvisited.Add(point);
        }

        while (unvisited.Count > 0)
        {
            Point current = null;
            float currentDistance = float.PositiveInfinity;
            foreach (Point candidate in unvisited)
            {
                float candidateDistance = distances[candidate];
                if (candidateDistance >= currentDistance) continue;
                current = candidate;
                currentDistance = candidateDistance;
            }

            if (current == null || float.IsPositiveInfinity(currentDistance)) break;
            unvisited.Remove(current);
            if (current == exit) break;

            foreach (Point neighbor in adjacency[current])
            {
                if (neighbor == null || !unvisited.Contains(neighbor)) continue;
                float candidateDistance = currentDistance +
                    Vector3.Distance(current.transform.position, neighbor.transform.position);
                if (candidateDistance >= distances[neighbor]) continue;
                distances[neighbor] = candidateDistance;
                previous[neighbor] = current;
            }
        }

        if (entry != exit && !previous.ContainsKey(exit))
        {
            int componentCount = CollectRoadComponents(adjacency).Count;
            Debug.LogWarning(
                $"[NPCProgressionManager] The baked road graph at '{buildLocation.name}' is disconnected. " +
                $"{componentCount} road sections remain after safe ground checks. " +
                "Falling back to the direct waypoint route.",
                buildLocation);
            return false;
        }

        List<Point> reversedPath = new List<Point>();
        Point pathPoint = exit;
        reversedPath.Add(pathPoint);
        while (pathPoint != entry)
        {
            pathPoint = previous[pathPoint];
            reversedPath.Add(pathPoint);
        }
        reversedPath.Reverse();

        foreach (Point roadPoint in reversedPath)
        {
            if (roadPoint == null) continue;
            Vector3 pointPosition = roadPoint.transform.position;
            if (routePositions.Count > 0 &&
                (routePositions[routePositions.Count - 1] - pointPosition).sqrMagnitude <= 0.0001f)
                continue;
            routePositions.Add(pointPosition);
        }

        Debug.Log(
            $"[NPCProgressionManager] Following {reversedPath.Count} baked road waypoints " +
            $"from '{buildLocation.name}' without NavMesh " +
            $"({weldedNodeCount} snapped-node and {groundedConnectionCount} ground connection(s)).",
            this);
        return routePositions.Count > 0;
    }

    private int ConnectCoincidentRoadNodes(
        Dictionary<Point, List<Point>> adjacency)
    {
        if (adjacency == null || waypointRoadNodeWeldDistance <= 0f) return 0;

        List<Point> points = new List<Point>(adjacency.Keys);
        float maximumDistanceSquared =
            waypointRoadNodeWeldDistance * waypointRoadNodeWeldDistance;
        int connectionCount = 0;

        for (int firstIndex = 0; firstIndex < points.Count; firstIndex++)
        {
            Point first = points[firstIndex];
            if (first == null) continue;

            for (int secondIndex = firstIndex + 1; secondIndex < points.Count; secondIndex++)
            {
                Point second = points[secondIndex];
                if (second == null || adjacency[first].Contains(second)) continue;
                if ((first.transform.position - second.transform.position).sqrMagnitude >
                    maximumDistanceSquared) continue;

                AddRoadConnection(adjacency, first, second);
                AddRoadConnection(adjacency, second, first);
                connectionCount++;
            }
        }

        return connectionCount;
    }

    private int ConnectRoadSectionsAcrossGround(
        Dictionary<Point, List<Point>> adjacency)
    {
        if (adjacency == null || waypointRoadGroundConnectionDistance <= 0f) return 0;

        int connectionCount = 0;
        float maximumDistanceSquared = waypointRoadGroundConnectionDistance *
                                       waypointRoadGroundConnectionDistance;

        while (true)
        {
            List<List<Point>> components = CollectRoadComponents(adjacency);
            if (components.Count <= 1) break;

            List<RoadGroundConnectionCandidate> candidates =
                new List<RoadGroundConnectionCandidate>();

            for (int firstComponentIndex = 0;
                 firstComponentIndex < components.Count;
                 firstComponentIndex++)
            {
                for (int secondComponentIndex = firstComponentIndex + 1;
                     secondComponentIndex < components.Count;
                     secondComponentIndex++)
                {
                    foreach (Point first in components[firstComponentIndex])
                    {
                        if (first == null) continue;
                        foreach (Point second in components[secondComponentIndex])
                        {
                            if (second == null) continue;

                            Vector3 flatOffset = second.transform.position -
                                                 first.transform.position;
                            flatOffset.y = 0f;
                            float distanceSquared = flatOffset.sqrMagnitude;
                            if (distanceSquared > maximumDistanceSquared) continue;

                            candidates.Add(new RoadGroundConnectionCandidate(
                                first,
                                second,
                                distanceSquared));
                        }
                    }
                }
            }

            candidates.Sort((left, right) =>
                left.distanceSquared.CompareTo(right.distanceSquared));

            bool connectedASection = false;
            foreach (RoadGroundConnectionCandidate candidate in candidates)
            {
                if (!HasWalkableGroundConnection(
                        candidate.first.transform.position,
                        candidate.second.transform.position)) continue;

                AddRoadConnection(adjacency, candidate.first, candidate.second);
                AddRoadConnection(adjacency, candidate.second, candidate.first);
                connectionCount++;
                connectedASection = true;
                break;
            }

            if (!connectedASection) break;
        }

        return connectionCount;
    }

    private bool HasWalkableGroundConnection(Vector3 start, Vector3 end)
    {
        Vector3 flatOffset = end - start;
        flatOffset.y = 0f;
        float distance = flatOffset.magnitude;
        if (distance <= waypointRoadNodeWeldDistance) return true;

        int segmentCount = Mathf.Max(
            2,
            Mathf.CeilToInt(distance /
                            Mathf.Max(0.1f, waypointGroundConnectionSampleSpacing)));

        // Endpoints already belong to baked road bars. Only the space between
        // them needs to be proven safe; an empty ravine fails on its first
        // unsupported sample while a solid middle platform succeeds.
        for (int sampleIndex = 1; sampleIndex < segmentCount; sampleIndex++)
        {
            float progress = sampleIndex / (float)segmentCount;
            Vector3 samplePosition = Vector3.Lerp(start, end, progress);
            if (!TryProjectWaypointToGround(samplePosition, out _)) return false;
        }

        return true;
    }

    private static List<List<Point>> CollectRoadComponents(
        Dictionary<Point, List<Point>> adjacency)
    {
        List<List<Point>> components = new List<List<Point>>();
        if (adjacency == null || adjacency.Count == 0) return components;

        HashSet<Point> unvisited = new HashSet<Point>(adjacency.Keys);
        Queue<Point> pending = new Queue<Point>();

        while (unvisited.Count > 0)
        {
            Point start = null;
            foreach (Point candidate in unvisited)
            {
                start = candidate;
                break;
            }

            if (start == null) break;

            List<Point> component = new List<Point>();
            unvisited.Remove(start);
            pending.Enqueue(start);

            while (pending.Count > 0)
            {
                Point current = pending.Dequeue();
                component.Add(current);

                if (!adjacency.TryGetValue(current, out List<Point> neighbours)) continue;
                foreach (Point neighbour in neighbours)
                {
                    if (neighbour == null || !unvisited.Remove(neighbour)) continue;
                    pending.Enqueue(neighbour);
                }
            }

            components.Add(component);
        }

        return components;
    }

    private readonly struct RoadGroundConnectionCandidate
    {
        public readonly Point first;
        public readonly Point second;
        public readonly float distanceSquared;

        public RoadGroundConnectionCandidate(
            Point first,
            Point second,
            float distanceSquared)
        {
            this.first = first;
            this.second = second;
            this.distanceSquared = distanceSquared;
        }
    }

    private static void AddRoadConnection(
        Dictionary<Point, List<Point>> adjacency,
        Point from,
        Point to)
    {
        if (!adjacency.TryGetValue(from, out List<Point> neighbors))
        {
            neighbors = new List<Point>();
            adjacency.Add(from, neighbors);
        }

        if (!neighbors.Contains(to)) neighbors.Add(to);
        if (!adjacency.ContainsKey(to)) adjacency.Add(to, new List<Point>());
    }

    private static Point FindClosestRoadPoint(
        Dictionary<Point, List<Point>>.KeyCollection points,
        Vector3 position)
    {
        Point closest = null;
        float closestDistance = float.PositiveInfinity;
        foreach (Point point in points)
        {
            if (point == null) continue;
            float distance = (point.transform.position - position).sqrMagnitude;
            if (distance >= closestDistance) continue;
            closestDistance = distance;
            closest = point;
        }

        return closest;
    }

    private static void SetLayerRecursively(Transform root, int layer)
    {
        if (root == null) return;
        root.gameObject.layer = layer;
        for (int childIndex = 0; childIndex < root.childCount; childIndex++)
            SetLayerRecursively(root.GetChild(childIndex), layer);
    }

    private bool TryProjectWaypointToGround(Vector3 position, out Vector3 groundedPosition)
    {
        Vector3 rayOrigin = position + Vector3.up * waypointGroundProbeHeight;
        float rayDistance = waypointGroundProbeHeight + waypointGroundProbeDepth;
        if (Physics.Raycast(
                rayOrigin,
                Vector3.down,
                out RaycastHit hit,
                rayDistance,
                waypointGroundLayers,
                QueryTriggerInteraction.Ignore))
        {
            groundedPosition = position;
            groundedPosition.y = hit.point.y + waypointGroundOffset;
            return true;
        }

        groundedPosition = position;
        return false;
    }

    private IEnumerator TraverseCurrentNavMeshLink()
    {
        if (navMeshAgent == null || !navMeshAgent.enabled ||
            !navMeshAgent.isOnNavMesh || !navMeshAgent.isOnOffMeshLink) yield break;

        OffMeshLinkData linkData = navMeshAgent.currentOffMeshLinkData;
        Vector3 destination = linkData.endPos + Vector3.up * navMeshAgent.baseOffset;

        savedAgentUpdatePosition = navMeshAgent.updatePosition;
        savedAgentUpdateRotation = navMeshAgent.updateRotation;
        navMeshAgent.updatePosition = false;
        navMeshAgent.updateRotation = false;
        manualLinkTraversalActive = true;

        while (manualLinkTraversalActive && navMeshAgent != null && navMeshAgent.enabled)
        {
            Vector3 offset = destination - transform.position;
            if (offset.sqrMagnitude <= 0.0001f) break;

            float step = Mathf.Max(0.01f, navMeshAgent.speed) * Time.deltaTime;
            transform.position = Vector3.MoveTowards(transform.position, destination, step);
            navMeshAgent.nextPosition = transform.position;

            if (rotateWhileTraversingLinks && offset.sqrMagnitude > 0.0001f)
            {
                Vector3 flatDirection = Vector3.ProjectOnPlane(offset, Vector3.up);
                if (flatDirection.sqrMagnitude > 0.0001f)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(flatDirection.normalized, Vector3.up);
                    transform.rotation = Quaternion.RotateTowards(
                        transform.rotation,
                        targetRotation,
                        navMeshAgent.angularSpeed * Time.deltaTime);
                }
            }

            yield return null;
        }

        if (navMeshAgent != null && navMeshAgent.enabled && navMeshAgent.isOnNavMesh)
        {
            transform.position = destination;
            navMeshAgent.nextPosition = destination;
            if (navMeshAgent.isOnOffMeshLink) navMeshAgent.CompleteOffMeshLink();
        }

        RestoreAgentAfterLinkTraversal(false);
    }

    private void RestoreAgentAfterLinkTraversal(bool completeLink)
    {
        if (!manualLinkTraversalActive) return;

        manualLinkTraversalActive = false;
        if (navMeshAgent == null || !navMeshAgent.enabled) return;

        if (completeLink && navMeshAgent.isOnNavMesh && navMeshAgent.isOnOffMeshLink)
            navMeshAgent.CompleteOffMeshLink();

        navMeshAgent.updatePosition = savedAgentUpdatePosition;
        navMeshAgent.updateRotation = savedAgentUpdateRotation;
        if (navMeshAgent.isOnNavMesh) navMeshAgent.nextPosition = transform.position;
    }

    private bool TrySetDestination(Vector3 targetPosition)
    {
        if (navMeshAgent == null || !navMeshAgent.enabled || !navMeshAgent.isOnNavMesh)
        {
            Debug.LogError("[NPCProgressionManager] NPC is not standing on a baked NavMesh.", this);
            return false;
        }

        if (!NavMesh.SamplePosition(targetPosition, out NavMeshHit hit,
                navMeshSampleRadius, navMeshAgent.areaMask))
        {
            Debug.LogError("[NPCProgressionManager] Target is outside the NPC's NavMesh area.", this);
            return false;
        }

        navMeshAgent.isStopped = false;
        navMeshAgent.ResetPath();

        NavMeshPath path = new NavMeshPath();
        bool calculated = navMeshAgent.CalculatePath(hit.position, path);
        if (!calculated || path.status != NavMeshPathStatus.PathComplete)
        {
            Debug.LogWarning(
                $"[NPCProgressionManager] No complete NavMesh route to '{hit.position}'. " +
                $"Calculated={calculated}, Status={path.status}.",
                this);
            return false;
        }

        return navMeshAgent.SetPath(path);
    }

    private void CompleteArrival(int phaseIndex)
    {
        if (navMeshAgent != null && navMeshAgent.enabled && navMeshAgent.isOnNavMesh)
        {
            navMeshAgent.isStopped = true;
            navMeshAgent.ResetPath();
        }

        SetWalkingAnimation(false);
        movementRoutine = null;
        ActivatePhase(phaseIndex, true);
    }

    private void HandleMovementFailure(int phaseIndex)
    {
        SetWalkingAnimation(false);
        onMovementFailed?.Invoke();

        if (warpToTargetIfPathFails && PlaceAtPhase(phaseIndex))
        {
            Debug.LogWarning("[NPCProgressionManager] Path failed; NPC was moved to the phase target to prevent a progression lock.", this);
            movementRoutine = null;
            ActivatePhase(phaseIndex, true);
            return;
        }

        movementRoutine = null;
        if (contractGiver != null) contractGiver.SetProgressionInteractionLocked(false);
        if (currentPhaseIndex >= 0) SaveProgressionState(currentPhaseIndex, false);
    }

    private bool PlaceAtPhase(int phaseIndex)
    {
        if (phaseIndex < 0 || phaseIndex >= phases.Count ||
            phases[phaseIndex] == null || phases[phaseIndex].targetLocation == null) return false;

        Vector3 target = phases[phaseIndex].targetLocation.position;
        if (movementMode == NPCProgressionMovementMode.Waypoints)
        {
            if (TryProjectWaypointToGround(target, out Vector3 groundedTarget))
                target.y = groundedTarget.y;

            transform.SetPositionAndRotation(
                target,
                phases[phaseIndex].targetLocation.rotation);
            return true;
        }

        if (navMeshAgent != null && navMeshAgent.isActiveAndEnabled &&
            NavMesh.SamplePosition(target, out NavMeshHit hit, navMeshSampleRadius,
                navMeshAgent.areaMask))
        {
            if (navMeshAgent.Warp(hit.position))
            {
                transform.rotation = phases[phaseIndex].targetLocation.rotation;
                navMeshAgent.isStopped = true;
                navMeshAgent.ResetPath();
                return true;
            }

            transform.position = hit.position;
            transform.rotation = phases[phaseIndex].targetLocation.rotation;
            return true;
        }

        transform.SetPositionAndRotation(target, phases[phaseIndex].targetLocation.rotation);
        return true;
    }

    private static void SetHierarchyActiveForDebug(Transform target)
    {
        if (target == null) return;

        // Activate parents first so activating the NPC itself is effective even
        // if it was grouped under a disabled progression container.
        Stack<Transform> hierarchy = new Stack<Transform>();
        Transform current = target;
        while (current != null)
        {
            hierarchy.Push(current);
            current = current.parent;
        }

        while (hierarchy.Count > 0)
            hierarchy.Pop().gameObject.SetActive(true);
    }

    private void ActivatePhase(int phaseIndex, bool invokeArrivalEvent)
    {
        if (phaseIndex < 0 || phaseIndex >= phases.Count) return;

        currentPhaseIndex = phaseIndex;
        NPCProgressionPhase phase = phases[phaseIndex];
        if (phase == null) return;

        if (contractGiver != null)
        {
            contractGiver.ConfigureProgressionPhase(
                phase.contract,
                phase.targetBuildLocation,
                phase.linkedCargo,
                phase.contract == null && phase.phaseDialogue != null
                    ? phase.dialoguePrompt
                    : string.Empty);
            contractGiver.SetProgressionInteractionLocked(false);
        }

        SaveProgressionState(phaseIndex, false);

        if (invokeArrivalEvent)
        {
            phase.onNPCArrived?.Invoke();
            if (phase.playDialogueOnArrival)
                TryStartPhaseDialogue(phaseIndex, phase);
        }
    }

    private void HandleNPCInteracted(NPCContractGiver sender)
    {
        NPCProgressionPhase phase = CurrentPhase;
        if (phase == null) return;

        bool shouldInvokeInteractionEvent =
            !phase.invokeInteractionEventOnlyOnce ||
            !invokedInteractionPhases.Contains(currentPhaseIndex);

        if (shouldInvokeInteractionEvent)
        {
            invokedInteractionPhases.Add(currentPhaseIndex);
            phase.onNPCInteracted?.Invoke();
        }

        // Contract phases already own their offer/reminder/completion dialogue.
        // Optional phase dialogue is therefore reserved for contract-free phases
        // so the two dialogue flows cannot open over each other.
        if (phase.contract == null)
            TryStartPhaseDialogue(currentPhaseIndex, phase);
    }

    private void TryStartPhaseDialogue(int phaseIndex, NPCProgressionPhase phase)
    {
        if (phase == null || phase.phaseDialogue == null) return;
        if (!phase.repeatDialogue && completedDialoguePhases.Contains(phaseIndex)) return;

        if (dialogueManager == null)
            dialogueManager = FindObjectOfType<DialogueManager>();

        if (dialogueManager == null)
        {
            Debug.LogWarning(
                $"[NPCProgressionManager] Phase '{phase.phaseId}' has dialogue but no DialogueManager exists in the scene.",
                this);
            return;
        }

        if (!phase.repeatDialogue) completedDialoguePhases.Add(phaseIndex);
        dialogueManager.StartDialogue(
            phase.phaseDialogue,
            () => HandlePhaseDialogueFinished(phaseIndex, phase));
    }

    private void HandlePhaseDialogueFinished(int phaseIndex, NPCProgressionPhase phase)
    {
        if (phase == null) return;

        bool featureAlreadyUnlocked = PlayerDataManager.Instance != null &&
                                      PlayerDataManager.Instance.IsFeatureUnlocked(
                                          phase.unlockFeatureIdAfterDialogue);
        if (!string.IsNullOrWhiteSpace(phase.unlockFeatureIdAfterDialogue) &&
            !featureAlreadyUnlocked)
        {
            if (phase.showFeatureCollectPopup && ItemUnlockUI.Instance != null)
            {
                string displayName = string.IsNullOrWhiteSpace(phase.featureUnlockDisplayName)
                    ? phase.unlockFeatureIdAfterDialogue
                    : phase.featureUnlockDisplayName;

                ItemUnlockUI.Instance.ShowReward(
                    displayName,
                    phase.featureUnlockIcon,
                    string.Empty,
                    () => GrantPhaseDialogueFeature(phase));
            }
            else
            {
                GrantPhaseDialogueFeature(phase);
            }
        }

        TryShowMaterialIntroduction(phaseIndex, phase);

        phase.onDialogueFinished?.Invoke();
    }

    private void HandleOfferDialogueCompleted(NPCContractGiver giver)
    {
        if (giver != contractGiver || currentPhaseIndex < 0 || currentPhaseIndex >= phases.Count)
            return;

        TryShowMaterialIntroduction(currentPhaseIndex, phases[currentPhaseIndex]);
    }

    private void TryShowMaterialIntroduction(int phaseIndex, NPCProgressionPhase phase)
    {
        if (phase == null) return;
        if (phase.introduceMaterialOnlyOnce && introducedMaterialPhases.Contains(phaseIndex)) return;

        List<BridgeMaterialSO> materialsToShow = GetPhaseMaterialIntroductions(phase);
        if (materialsToShow.Count == 0) return;

        if (phase.introduceMaterialOnlyOnce)
            introducedMaterialPhases.Add(phaseIndex);

        if (ItemUnlockUI.Instance != null)
        {
            for (int i = 0; i < materialsToShow.Count; i++)
            {
                bool isLastMaterial = i == materialsToShow.Count - 1;
                System.Action onDismiss = null;
                if (isLastMaterial)
                    onDismiss = () => phase.onMaterialIntroductionClosed?.Invoke();

                ItemUnlockUI.Instance.ShowMaterialIntroduction(
                    materialsToShow[i],
                    onDismiss,
                    phase.materialIntroductionButtonLabel);
            }
        }
        else
        {
            Debug.LogWarning(
                $"[NPCProgressionManager] Cannot introduce {materialsToShow.Count} material(s) because ItemUnlockUI is unavailable.",
                this);
            phase.onMaterialIntroductionClosed?.Invoke();
        }
    }

    private static List<BridgeMaterialSO> GetPhaseMaterialIntroductions(NPCProgressionPhase phase)
    {
        List<BridgeMaterialSO> result = new List<BridgeMaterialSO>();
        if (phase == null) return result;

        if (phase.materialIntroduction != null)
            result.Add(phase.materialIntroduction);

        if (phase.materialIntroductions == null) return result;

        foreach (BridgeMaterialSO material in phase.materialIntroductions)
        {
            if (material != null && !result.Contains(material))
                result.Add(material);
        }

        return result;
    }

    private void GrantPhaseDialogueFeature(NPCProgressionPhase phase)
    {
        if (phase == null || string.IsNullOrWhiteSpace(phase.unlockFeatureIdAfterDialogue))
            return;

        if (PlayerDataManager.Instance != null)
        {
            // Save the feature after Collect. UnlockFeature queues the compact
            // follow-up notification above every other UI.
            PlayerDataManager.Instance.UnlockFeature(phase.unlockFeatureIdAfterDialogue);
        }
        else
        {
            Debug.LogWarning(
                $"[NPCProgressionManager] Cannot unlock feature '{phase.unlockFeatureIdAfterDialogue}' because PlayerDataManager is unavailable.",
                this);
        }
    }

    private void SetWalkingAnimation(bool walking)
    {
        if (animator != null && !string.IsNullOrWhiteSpace(walkingBoolParameter))
            animator.SetBool(walkingBoolParameter, walking);
    }

    private void OnDrawGizmosSelected()
    {
        if (phases == null) return;

        Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.9f);
        for (int i = 0; i < phases.Count; i++)
        {
            NPCProgressionPhase phase = phases[i];
            if (phase == null || phase.targetLocation == null) continue;

            Gizmos.DrawWireSphere(phase.targetLocation.position, 0.35f);

            Vector3 previousPoint = i > 0 && phases[i - 1] != null &&
                                    phases[i - 1].targetLocation != null
                ? phases[i - 1].targetLocation.position
                : transform.position;

            if (phase.travelWaypoints != null)
            {
                foreach (Transform waypoint in phase.travelWaypoints)
                {
                    if (waypoint == null) continue;
                    Gizmos.DrawLine(previousPoint, waypoint.position);
                    Gizmos.DrawWireSphere(waypoint.position, 0.2f);
                    previousPoint = waypoint.position;
                }
            }

            Gizmos.DrawLine(previousPoint, phase.targetLocation.position);
        }
    }
}
