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
    
    [Header("Animation Settings")]
    [Tooltip("Drag the NPC's Animator here")]
    public Animator animator;
    [Tooltip("The exact name of the boolean parameter in your Animator that makes them walk")]
    public string walkAnimParameter = "isWalking";

    [Header("Events")]
    [Tooltip("What happens when they reach the door? (e.g., Turn off the NPC!)")]
    public UnityEvent onDestinationReached;

    private NavMeshAgent agent;

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

        // Check if the agent is placed on a valid NavMesh
        if (!agent.isOnNavMesh)
        {
            Debug.LogError($"[NPCWalker] {gameObject.name} is NOT placed on a baked NavMesh! Please place the NPC on a NavMesh floor.");
            return;
        }

        StartCoroutine(WalkRoutine());
    }

    private IEnumerator WalkRoutine()
    {
        // 1. Tell the agent to start moving
        agent.isStopped = false;
        agent.SetDestination(targetDestination.position);

        if (animator != null) 
        {
            animator.SetBool(walkAnimParameter, true);
        }

        // --- THE FIX: Wait 1 frame so Unity has time to initialize the path calculation ---
        yield return null;

        // 2. Wait while Unity is calculating the path
        while (agent.pathPending)
        {
            yield return null;
        }

        // 3. Monitor the walk loop safely
        bool isWalking = true;
        while (isWalking)
        {
            // Only trigger arrival if the distance is close AND path is complete
            if (agent.remainingDistance <= agent.stoppingDistance + 0.2f)
            {
                if (!agent.hasPath || agent.velocity.sqrMagnitude < 0.05f)
                {
                    isWalking = false;
                }
            }
            yield return null;
        }

        // 4. Arrived at door!
        if (animator != null) 
        {
            animator.SetBool(walkAnimParameter, false);
        }

        onDestinationReached?.Invoke();
        
        // Hide the NPC after reaching the target
        gameObject.SetActive(false); 
    }
}