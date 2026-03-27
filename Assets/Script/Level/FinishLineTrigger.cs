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

                if (LevelCompleteManager.Instance != null)
                {
                    LevelCompleteManager.Instance.ShowLevelCompleteScreen();
                }

                // --- NEW: Tell the NPC and UI that the job is done! ---
                NPCContractGiver npc = FindObjectOfType<NPCContractGiver>();
                if (npc != null)
                {
                    npc.isContractCompleted = true; // Unlocks the final dialogue
                    
                    if (ObjectiveTrackerUI.Instance != null)
                    {
                        ObjectiveTrackerUI.Instance.descriptionText.text = $"<color=green>Bridge Tested!</color> Return to {npc.contractToGive.clientName}.";
                    }
                }
                
                break;
            }
        }
    }
}