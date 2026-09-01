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

    [Tooltip("Contract offered by the existing NPCContractGiver in this phase.")]
    public ContractSO contract;

    [Tooltip("Build site associated with this phase's contract.")]
    public BuildLocation targetBuildLocation;

    [Tooltip("Optional cargo whose weight should match the phase contract.")]
    public CargoItem linkedCargo;

    [Tooltip("Invoked when the player interacts with the NPC during this phase.")]
    public UnityEvent onNPCInteracted;

    [Tooltip("Prevents repeat interactions from restarting the same tutorial/event during this scene visit.")]
    public bool invokeInteractionEventOnlyOnce = true;

    [Tooltip("Invoked after the NPC arrives and this phase becomes active.")]
    public UnityEvent onNPCArrived;
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
    [Min(0.01f)] [SerializeField] private float arrivalPadding = 0.15f;
    [Min(0.1f)] [SerializeField] private float navMeshSampleRadius = 3f;
    [Min(1f)] [SerializeField] private float pathTimeout = 45f;
    [Tooltip("Prevents progression soft-locks if a target is outside the baked NavMesh.")]
    [SerializeField] private bool warpToTargetIfPathFails = true;

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
            PlayerPrefs.DeleteKey("LockedContract_" + phaseContract.name);
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

        if (navMeshAgent != null)
            navMeshAgent.autoTraverseOffMeshLink = !manuallyTraverseNavMeshLinks;
    }

    private void OnEnable()
    {
        if (contractGiver != null) contractGiver.OnNPCInteracted += HandleNPCInteracted;
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
                !PlayerDataManager.Instance.HasValidSavedBridge(phase.contract.name)) continue;

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
        if (contractGiver != null) contractGiver.OnNPCInteracted -= HandleNPCInteracted;
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
            if (contract == null || !PlayerDataManager.Instance.IsContractCompleted(contract.name))
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
        if (!string.Equals(phase.contract.name, completedContractName,
                System.StringComparison.Ordinal)) return;

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

        // Auto-collected contracts can complete in the same frame the bridge is
        // finalized. Do not calculate the NPC path against the old NavMesh.
        DynamicNavMeshUpdater navMeshUpdater = DynamicNavMeshUpdater.Instance;
        while (navMeshUpdater != null && navMeshUpdater.HasPendingOrRunningUpdate)
            yield return null;

        NPCProgressionPhase nextPhase = phases[nextPhaseIndex];
        if (nextPhase == null || nextPhase.targetLocation == null)
        {
            Debug.LogError($"[NPCProgressionManager] Phase {nextPhaseIndex} has no target location.", this);
            HandleMovementFailure(nextPhaseIndex);
            yield break;
        }

        bool destinationSet = TrySetDestination(nextPhase.targetLocation.position);
        if (!destinationSet)
        {
            HandleMovementFailure(nextPhaseIndex);
            yield break;
        }

        SetWalkingAnimation(true);
        float deadline = Time.time + pathTimeout;

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

            yield return null;
        }

        HandleMovementFailure(nextPhaseIndex);
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
        return navMeshAgent.SetDestination(hit.position);
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
    }

    private bool PlaceAtPhase(int phaseIndex)
    {
        if (phaseIndex < 0 || phaseIndex >= phases.Count ||
            phases[phaseIndex] == null || phases[phaseIndex].targetLocation == null) return false;

        Vector3 target = phases[phaseIndex].targetLocation.position;
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
                phase.linkedCargo);
            contractGiver.SetProgressionInteractionLocked(false);
        }

        SaveProgressionState(phaseIndex, false);

        if (invokeArrivalEvent) phase.onNPCArrived?.Invoke();
    }

    private void HandleNPCInteracted(NPCContractGiver sender)
    {
        NPCProgressionPhase phase = CurrentPhase;
        if (phase == null) return;

        if (phase.invokeInteractionEventOnlyOnce &&
            invokedInteractionPhases.Contains(currentPhaseIndex)) return;

        invokedInteractionPhases.Add(currentPhaseIndex);
        phase.onNPCInteracted?.Invoke();
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
            if (i > 0 && phases[i - 1] != null && phases[i - 1].targetLocation != null)
                Gizmos.DrawLine(phases[i - 1].targetLocation.position, phase.targetLocation.position);
        }
    }
}
