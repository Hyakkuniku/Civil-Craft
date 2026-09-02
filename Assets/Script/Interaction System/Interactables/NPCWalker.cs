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

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        if (agent != null)
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

        if (agent == null || !agent.isActiveAndEnabled)
        {
            Debug.LogError($"[NPCWalker] {gameObject.name} has no active NavMeshAgent.", this);
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
            FinishWalk(false);
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
            if (agent.remainingDistance <= agent.stoppingDistance + 0.2f)
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
        FinishWalk(false);
    }

    private bool TrySetCompletePath()
    {
        if (!agent.isOnNavMesh)
        {
            if (!NavMesh.SamplePosition(transform.position, out NavMeshHit startHit,
                    navMeshSampleRadius, agent.areaMask) || !agent.Warp(startHit.position))
            {
                Debug.LogError(
                    $"[NPCWalker] {gameObject.name} is not close enough to a baked NavMesh.",
                    this);
                return false;
            }
        }

        if (!NavMesh.SamplePosition(targetDestination.position, out NavMeshHit targetHit,
                navMeshSampleRadius, agent.areaMask))
        {
            Debug.LogError(
                $"[NPCWalker] Destination '{targetDestination.name}' is outside the NavMesh.",
                targetDestination);
            return false;
        }

        NavMeshPath path = new NavMeshPath();
        bool calculated = agent.CalculatePath(targetHit.position, path);
        if (!calculated || path.status != NavMeshPathStatus.PathComplete)
        {
            Debug.LogWarning(
                $"[NPCWalker] No complete route to '{targetDestination.name}'. " +
                $"Calculated={calculated}, Status={path.status}.",
                this);
            return false;
        }

        agent.isStopped = false;
        agent.ResetPath();
        return agent.SetPath(path);
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
