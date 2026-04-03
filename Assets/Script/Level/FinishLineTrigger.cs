using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(BoxCollider))]
public class FinishLineTrigger : MonoBehaviour
{
    [Header("Completion Settings")]
    [Tooltip("Tags of objects that can trigger the win state (e.g., 'Player', 'Vehicle')")]
    public string[] acceptedTags = { "Player", "Vehicle" };

    [Tooltip("Drag the ContractSO for THIS specific ravine here.")]
    public ContractSO assignedContract; 

    [Header("Events")]
    public UnityEvent OnLevelCompleted;

    private void OnTriggerEnter(Collider other)
    {
        Transform rootObj = other.attachedRigidbody != null ? other.attachedRigidbody.transform : other.transform.root;
        
        bool hasValidTag = false;
        Transform currentObj = other.transform;
        
        while (currentObj != null)
        {
            foreach (string acceptedTag in acceptedTags)
            {
                if (currentObj.CompareTag(acceptedTag))
                {
                    hasValidTag = true;
                    break;
                }
            }
            if (hasValidTag) break;
            currentObj = currentObj.parent;
        }
        
        if (!hasValidTag) return; 

        BridgePhysicsManager physicsManager = FindObjectOfType<BridgePhysicsManager>();
        bool isSimulating = physicsManager != null && physicsManager.isSimulating;

        bool hasBakedBridge = false;
        BuildLocation[] allLocations = FindObjectsOfType<BuildLocation>();
        foreach (BuildLocation loc in allLocations)
        {
            if (loc.activeContract == assignedContract && loc.bakedBars.Count > 0)
            {
                hasBakedBridge = true;
                break;
            }
        }

        if (!isSimulating && !hasBakedBridge)
        {
            Debug.LogWarning("<b>[Finish Line]</b> Hit by player/vehicle, but the bridge is NOT actively simulating and NO baked bridge exists! Ignoring.");
            return;
        }

        if (LevelFailedManager.Instance != null && LevelFailedManager.Instance.isFailed)
        {
            Debug.LogWarning("<b>[Finish Line]</b> Hit by player/vehicle, but the level is already marked as FAILED! Ignoring.");
            return;
        }

        // --- THE FIX: Smart Contract Check ---
        // We ONLY strictly ask the GameManager if we are actively inside Build Mode.
        bool isBuilding = GameManager.Instance != null && GameManager.Instance.CurrentState == GameManager.GameState.Building;
        if (isBuilding && GameManager.Instance.CurrentContract != assignedContract)
        {
            string activeName = GameManager.Instance.CurrentContract != null ? GameManager.Instance.CurrentContract.name : "None";
            string assignedName = assignedContract != null ? assignedContract.name : "None (Inspector is empty!)";
            Debug.LogWarning($"<b>[Finish Line]</b> Contract Mismatch! You are playing: '{activeName}', but this finish line requires: '{assignedName}'");
            return; 
        }

        // SUCCESS!
        Debug.Log("<color=green><b>[Finish Line]</b> Valid trigger hit! Firing Level Complete sequence...</color>");
        
        OnLevelCompleted?.Invoke();

        if (assignedContract != null && ObjectiveTrackerUI.Instance != null)
        {
            ObjectiveTrackerUI.Instance.descriptionText.text = $"<color=green>Bridge Tested!</color> Return to {assignedContract.clientName}.";
        }
        
        if (LevelCompleteManager.Instance != null)
        {
            // Always pass the assignedContract directly, bypassing the empty GameManager memory!
            LevelCompleteManager.Instance.CompleteLevel(assignedContract);
        }
        else
        {
            Debug.LogError("<b>[Finish Line]</b> CRITICAL: Could not find LevelCompleteManager in the scene!");
        }
    }
}