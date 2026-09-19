using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;
using System.Collections;

[RequireComponent(typeof(NavMeshAgent))]
public class NPCWalker : MonoBehaviour
{
    [Header("Navigation Settings")]
    [Tooltip("Where should the NPC walk to? (Drag the Door object here)")]
    public Transform targetDestination;
    [Min(0.1f)] public float navMeshSampleRadius = 2f;
    [Min(1f)] public float maximumWalkTime = 45f;
    [Min(0.25f)] public float stalledRepathDelay = 1.5f;
    [Min(0.001f)] public float stalledMovementTolerance = 0.03f;
    [Min(0)] public int maximumRepathAttempts = 3;
    
    [Header("Animation Settings")]
    [Tooltip("Drag the NPC's Animator here")]
    public Animator animator;
    [Tooltip("The exact name of the boolean parameter in your Animator that makes them walk")]
    public string walkAnimParameter = "isWalking";

    [Header("Events")]
    [Tooltip("What happens when they reach the door? (e.g., Turn off the NPC!)")]
    public UnityEvent onDestinationReached;
    public UnityEvent onMovementFailed;

    private NavMeshAgent agent;
    private Coroutine walkRoutine;
    private Vector3 resolvedDestination;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
        {
            agent.isStopped = true; 
        }
    }

    public void StartWalking()
    {
        if (targetDestination == null)
        {
            Debug.LogWarning($"[NPCWalker] {gameObject.name} has no target destination assigned!");
            return;
        }

        if (agent == null)
        {
            Debug.LogError($"[NPCWalker] {gameObject.name} has no NavMeshAgent.", this);
            return;
        }

        if (walkRoutine != null)
            StopCoroutine(walkRoutine);

        walkRoutine = StartCoroutine(WalkRoutine());
    }

    private IEnumerator WalkRoutine()
    {
        if (!TrySetCompletePath())
        {
            RecoverAtDestination();
            yield break;
        }

        if (animator != null) 
            animator.SetBool(walkAnimParameter, true);

        yield return null;

        while (agent.pathPending)
            yield return null;

        float deadline = Time.time + maximumWalkTime;
        float lastProgressTime = Time.time;
        Vector3 lastProgressPosition = transform.position;
        int repathAttempts = 0;

        while (Time.time < deadline)
        {
            float arrivalDistance = agent.stoppingDistance + 0.2f;
            Vector3 destinationOffset = transform.position - resolvedDestination;
            destinationOffset.y = 0f;
            if (agent.remainingDistance <= arrivalDistance &&
                destinationOffset.sqrMagnitude <= arrivalDistance * arrivalDistance)
            {
                if (!agent.hasPath || agent.velocity.sqrMagnitude < 0.05f)
                {
                    FinishWalk(true);
                    yield break;
                }
            }

            if (!agent.pathPending && agent.pathStatus != NavMeshPathStatus.PathComplete)
                break;

            float toleranceSquared = stalledMovementTolerance * stalledMovementTolerance;
            if ((transform.position - lastProgressPosition).sqrMagnitude >= toleranceSquared)
            {
                lastProgressPosition = transform.position;
                lastProgressTime = Time.time;
            }
            else if (!agent.pathPending && agent.hasPath && !agent.isOnOffMeshLink &&
                     Time.time - lastProgressTime >= stalledRepathDelay)
            {
                repathAttempts++;
                if (repathAttempts > maximumRepathAttempts || !TrySetCompletePath())
                    break;

                lastProgressPosition = transform.position;
                lastProgressTime = Time.time;
                deadline = Time.time + maximumWalkTime;
            }

            yield return null;
        }

        Debug.LogWarning(
            $"[NPCWalker] {gameObject.name} could not complete its route to " +
            $"{targetDestination.name}. Path status: {agent.pathStatus}.",
            this);
        RecoverAtDestination();
    }

    private void RecoverAtDestination()
    {
        if (targetDestination == null)
        {
            FinishWalk(false);
            return;
        }

        if (agent != null && agent.enabled)
        {
            if (agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.ResetPath();
            }
            agent.enabled = false;
        }

        transform.SetPositionAndRotation(
            targetDestination.position,
            targetDestination.rotation);
        Debug.LogWarning(
            $"[NPCWalker] {name} teleported to '{targetDestination.name}' after its " +
            "route failed, preventing the dependent sequence from becoming stuck.",
            this);
        onMovementFailed?.Invoke();
        FinishWalk(true);
    }

    private bool TrySetCompletePath()
    {
        if (!agent.enabled || !agent.isOnNavMesh)
        {
            if (!TrySampleLocalNavMesh(transform.position, out NavMeshHit startHit))
            {
                Debug.LogError(
                    $"[NPCWalker] {gameObject.name} has no NavMesh directly under its authored position; it will not snap to a distant edge.",
                    this);
                return false;
            }

            if (!agent.enabled) agent.enabled = true;
            if (!agent.Warp(startHit.position)) return false;
        }

        float horizontalTolerance = Mathf.Min(
            navMeshSampleRadius,
            Mathf.Max(0.5f, agent.radius));
        float verticalTolerance = Mathf.Min(
            navMeshSampleRadius,
            Mathf.Max(1f, agent.height));
        if (!PreferredRoadNavigation.SetDestinationNearTarget(
                agent,
                targetDestination.position,
                navMeshSampleRadius,
                horizontalTolerance,
                verticalTolerance,
                agent.areaMask,
                out resolvedDestination))
        {
            Debug.LogWarning(
                $"[NPCWalker] No complete route ends at '{targetDestination.name}'. " +
                "Check that its marker is directly above the intended NavMesh.",
                this);
            return false;
        }

        agent.isStopped = false;
        return true;
    }

    private bool TrySampleLocalNavMesh(Vector3 position, out NavMeshHit hit)
    {
        hit = default;
        if (agent == null) return false;
        var filter = new NavMeshQueryFilter
        { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
        if (!NavMesh.SamplePosition(position, out hit, navMeshSampleRadius, filter))
            return false;

        Vector3 delta = hit.position - position;
        return new Vector2(delta.x, delta.z).sqrMagnitude <= 0.25f * 0.25f &&
               Mathf.Abs(delta.y) <= 1f;
    }

    private void FinishWalk(bool arrived)
    {
        if (animator != null && !string.IsNullOrWhiteSpace(walkAnimParameter))
            animator.SetBool(walkAnimParameter, false);

        if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }

        walkRoutine = null;
        if (!arrived)
        {
            onMovementFailed?.Invoke();
            return;
        }

        onDestinationReached?.Invoke();
        gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        if (walkRoutine != null)
        {
            StopCoroutine(walkRoutine);
            walkRoutine = null;
        }

        if (animator != null && !string.IsNullOrWhiteSpace(walkAnimParameter))
            animator.SetBool(walkAnimParameter, false);
    }
}
