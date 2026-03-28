using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(BoxCollider))]
public class FinishLineTrigger : MonoBehaviour
{
    [Header("Completion Settings")]
    [Tooltip("Tags of objects that can trigger the win state (e.g., 'Player', 'Vehicle')")]
    public string[] acceptedTags = { "Player", "Vehicle" };

    [Header("Events")]
    public UnityEvent OnLevelCompleted;

    private bool levelCompleted = false;

    private void OnTriggerEnter(Collider other)
    {
        if (levelCompleted) return;

        foreach (string acceptedTag in acceptedTags)
        {
            if (other.CompareTag(acceptedTag))
            {
                levelCompleted = true;
                
                OnLevelCompleted?.Invoke();

                // 1. Find the NPC to update their dialogue and grab the active contract
                NPCContractGiver npc = FindObjectOfType<NPCContractGiver>();
                ContractSO activeContract = null;

                if (npc != null)
                {
                    npc.isContractCompleted = true; // Unlocks the final dialogue
                    activeContract = npc.contractToGive; // Grab the contract data!
                    
                    if (ObjectiveTrackerUI.Instance != null)
                    {
                        ObjectiveTrackerUI.Instance.descriptionText.text = $"<color=green>Bridge Tested!</color> Return to {npc.contractToGive.clientName}.";
                    }
                }
                
                // 2. Tell the Level Complete Manager to give rewards and unlock the NEXT level!
                if (LevelCompleteManager.Instance != null)
                {
                    LevelCompleteManager.Instance.CompleteLevel(activeContract);
                }
                else
                {
                    Debug.LogWarning("FinishLineTrigger could not find LevelCompleteManager! Progression will not save.");
                }
                
                break;
            }
        }
    }
}