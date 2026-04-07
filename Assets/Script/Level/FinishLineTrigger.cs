using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(BoxCollider))]
public class FinishLineTrigger : MonoBehaviour
{
    [Header("Completion Settings")]
    public string[] acceptedTags = { "Vehicle" }; // Only vehicles should trigger this!
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
        
        // --- THE FIX 1: Ensure it's specifically the vehicle triggering the win ---
        LiveLoadVehicle car = rootObj.GetComponentInChildren<LiveLoadVehicle>();
        if (car == null) car = rootObj.GetComponentInParent<LiveLoadVehicle>();
        
        if (car == null) return; // The player character walking into this will be ignored.
        
        // --- THE FIX 2: Ensure the vehicle belongs to this specific finish line's contract ---
        if (car.assignedContract != null && assignedContract != null && car.assignedContract != assignedContract) return;

        BridgePhysicsManager physicsManager = FindObjectOfType<BridgePhysicsManager>();
        
        // --- THE FIX 3: ONLY allow triggering if an active simulation is currently running ---
        if (physicsManager == null || !physicsManager.isSimulating) return;

        if (LevelFailedManager.Instance != null && LevelFailedManager.Instance.isFailed) return;

        physicsManager.lockStressTracking = true;

        // Freeze the car safely and tell it to stay parked!
        car.StopAndFreezeForWin();

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