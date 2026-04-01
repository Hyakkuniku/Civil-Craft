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

    private void OnTriggerEnter(Collider other)
    {
        // --- NEW SAFETY: Only trigger if the physics simulation is actively running! ---
        BridgePhysicsManager physicsManager = FindObjectOfType<BridgePhysicsManager>();
        if (physicsManager != null && !physicsManager.isSimulating) return;

        foreach (string acceptedTag in acceptedTags)
        {
            if (other.CompareTag(acceptedTag))
            {
                OnLevelCompleted?.Invoke();

                // 1. Stop searching for random NPCs! Grab the exact contract from the GameManager.
                ContractSO activeContract = null;
                if (GameManager.Instance != null)
                {
                    activeContract = GameManager.Instance.CurrentContract;
                }

                // 2. Safely update the objective tracker using the Contract data
                if (activeContract != null && ObjectiveTrackerUI.Instance != null)
                {
                    ObjectiveTrackerUI.Instance.descriptionText.text = $"<color=green>Bridge Tested!</color> Return to {activeContract.clientName}.";
                }
                
                // 3. Tell the Level Complete Manager to pop up and calculate rewards!
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