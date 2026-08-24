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
    public bool IsTravelling => movementRoutine != null;
    public NPCProgressionPhase CurrentPhase =>
        currentPhaseIndex >= 0 && currentPhaseIndex < phases.Count
            ? phases[currentPhaseIndex]
            : null;

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

        currentPhaseIndex = ResolvePhaseIndexFromSave();

        if (placeAtResolvedPhaseOnStart)
            PlaceAtPhase(currentPhaseIndex);

        ActivatePhase(currentPhaseIndex, false);
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
        if (navMeshAgent != null && navMeshAgent.enabled &&
            NavMesh.SamplePosition(target, out NavMeshHit hit, navMeshSampleRadius,
                navMeshAgent.areaMask))
        {
            if (navMeshAgent.isOnNavMesh) return navMeshAgent.Warp(hit.position);
            transform.position = hit.position;
            return true;
        }

        transform.position = target;
        return true;
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
