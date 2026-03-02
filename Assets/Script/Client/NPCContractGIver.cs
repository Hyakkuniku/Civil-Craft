using UnityEngine;

public class NPCContractGiver : Interactable
{
    [Header("Contract Assignment")]
    public ContractSO contractToGive;
    
    [Tooltip("The specific build zone this contract belongs to.")]
    public BuildLocation targetBuildLocation;

    protected override void Intract()
    {
        if (contractToGive == null)
        {
            Debug.LogWarning("This NPC doesn't have a contract assigned!");
            return;
        }

        // 1. Assign the contract to the build zone immediately so the game knows it's active
        if (targetBuildLocation != null)
        {
            targetBuildLocation.activeContract = contractToGive;
            Debug.Log($"<color=green>Contract Accepted!</color> Budget set to ${contractToGive.budget} for {targetBuildLocation.gameObject.name}");
        }

        // 2. Play the NPC's dialogue and wait for it to finish before showing the UI
        DialogueManager dm = FindObjectOfType<DialogueManager>();
        if (dm != null && contractToGive.offerDialogue != null)
        {
            contractToGive.offerDialogue.name = contractToGive.clientName;
            
            // We use '() => { code }' to create an inline function that gets sent to the DialogueManager
            dm.StartDialogue(contractToGive.offerDialogue, () => 
            {
                // This code will ONLY run when EndDialogue() is called!
                if (ObjectiveTrackerUI.Instance != null)
                {
                    ObjectiveTrackerUI.Instance.SetObjective(contractToGive);
                }
            });
        }
        else
        {
            // Fallback: If there's no DialogueManager in the scene, just show the UI immediately
            if (ObjectiveTrackerUI.Instance != null)
            {
                ObjectiveTrackerUI.Instance.SetObjective(contractToGive);
            }
        }
    }
}