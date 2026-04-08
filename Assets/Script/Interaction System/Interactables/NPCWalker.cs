using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

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
    private bool isWalking = false;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        // Make sure they don't start walking immediately!
        agent.isStopped = true; 
    }

    // We will call this from your TutorialNameNPC script!
    public void StartWalking()
    {
        if (targetDestination != null)
        {
            agent.isStopped = false;
            agent.SetDestination(targetDestination.position);
            isWalking = true;
            
            if (animator != null) 
            {
                animator.SetBool(walkAnimParameter, true);
            }
        }
        else
        {
            Debug.LogWarning("NPC Walker has no target destination!");
        }
    }

    private void Update()
    {
        if (isWalking)
        {
            // Check if the NPC has reached the door
            if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.1f)
            {
                if (!agent.hasPath || agent.velocity.sqrMagnitude == 0f)
                {
                    isWalking = false;
                    
                    if (animator != null) 
                    {
                        animator.SetBool(walkAnimParameter, false);
                    }

                    // Fire the event (we will set this to hide the NPC)
                    onDestinationReached?.Invoke();
                    
                    // Hide the NPC
                    gameObject.SetActive(false); 
                }
            }
        }
    }
}