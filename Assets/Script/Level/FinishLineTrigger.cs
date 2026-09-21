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

    private void Reset() { GetComponent<BoxCollider>().isTrigger = true; }

    private void OnTriggerEnter(Collider other)
    {
        // A scene can contain finish zones for several contracts. Only the active
        // contract may react, including legacy vehicle zones during a cargo test.
        if (GameManager.Instance != null && GameManager.Instance.CurrentContract != null &&
            assignedContract != GameManager.Instance.CurrentContract) return;
        if (assignedContract != null && assignedContract.liveLoadMode == ContractSO.LiveLoadMode.PlayerCarriedCargo)
        {
            // Player cargo tests finish through an explicit Place Cargo interaction.
            // Merely crossing this vehicle finish trigger must not complete delivery.
            return;
        }
        if (GameManager.Instance != null && GameManager.Instance.IsCargoTestActive) return;
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
        if (!ContractsMatch(car.assignedContract, assignedContract)) return;

        BridgePhysicsManager physicsManager = FindObjectOfType<BridgePhysicsManager>();
        
        // --- THE FIX 3: ONLY allow triggering if an active simulation is currently running ---
        if (physicsManager == null || !physicsManager.isSimulating) return;

        if (LevelFailedManager.Instance != null && LevelFailedManager.Instance.isFailed) return;

        // Reaching the trigger is not a valid structural pass if the bridge only
        // survived by folding or sagging beyond the serviceability limit.
        if (!BridgePhysicsManager.DebugInvincibleBridge &&
            !physicsManager.IsDeterministicStructureStable)
        {
            if (LevelFailedManager.Instance != null)
            {
                LevelFailedManager.Instance.TriggerLevelFailed("Structurally Unstable Bridge!");
            }
            return;
        }

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
            LevelCompleteManager.Instance.CompleteLevelForVehicle(assignedContract, car);
        }
    }

    private static bool ContractsMatch(ContractSO left, ContractSO right)
    {
        if (left == null || right == null) return false;
        if (left == right) return true;
        return !string.IsNullOrWhiteSpace(left.ContractID) &&
               string.Equals(left.ContractID, right.ContractID, System.StringComparison.Ordinal);
    }
}
