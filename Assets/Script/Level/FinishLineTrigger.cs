using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(BoxCollider))]
public class FinishLineTrigger : MonoBehaviour
{
    [Header("Completion Settings")]
    public string[] acceptedTags = { "Player", "Vehicle" };
    public ContractSO assignedContract; 

    [Header("Events")]
    public UnityEvent OnLevelCompleted;

    private void OnTriggerEnter(Collider other)
    {
        if (assignedContract != null && assignedContract.winCondition == ContractSO.WinCondition.Timer)
        {
            return;
        }

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

        if (!isSimulating && !hasBakedBridge) return;

        if (LevelFailedManager.Instance != null && LevelFailedManager.Instance.isFailed) return;

        bool isBuilding = GameManager.Instance != null && GameManager.Instance.CurrentState == GameManager.GameState.Building;
        if (isBuilding && GameManager.Instance.CurrentContract != assignedContract) return; 

        // --- THE FIX: Lock the scoreboard FIRST ---
        if (physicsManager != null)
        {
            physicsManager.lockStressTracking = true;
        }

        // --- THE FIX: Freeze the car safely so it doesn't cause a bridge slingshot bounce! ---
        LiveLoadVehicle car = currentObj.GetComponent<LiveLoadVehicle>();
        if (car != null)
        {
            car.StopAndFreezeForWin();
        }

        OnLevelCompleted?.Invoke();

        if (assignedContract != null && ObjectiveTrackerUI.Instance != null)
        {
            ObjectiveTrackerUI.Instance.descriptionText.text = $"<color=green>Bridge Tested!</color> Return to {assignedContract.clientName}.";
        }
        
        if (LevelCompleteManager.Instance != null)
        {
            LevelCompleteManager.Instance.CompleteLevel(assignedContract);
        }
    }
}