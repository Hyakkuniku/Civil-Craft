using UnityEngine;

public class NPCContractGiver : Interactable
{
    [Header("Contract Assignment")]
    public ContractSO contractToGive;
    public BuildLocation targetBuildLocation;
    
    public CargoItem linkedCargo; 

    // NEW: A true/false memory switch to track if we already gave the job
    private bool hasGivenContract = false;

    protected override void Intract()
    {
        if (contractToGive == null) return;
        
        DialogueManager dm = FindObjectOfType<DialogueManager>();

        // Scenario A: We haven't given the contract yet
        if (!hasGivenContract)
        {
            if (targetBuildLocation != null)
                targetBuildLocation.activeContract = contractToGive;

            if (linkedCargo != null)
                linkedCargo.SetWeight(contractToGive.cargoWeight);

            if (dm != null && contractToGive.offerDialogue != null)
            {
                contractToGive.offerDialogue.name = contractToGive.clientName;
                dm.StartDialogue(contractToGive.offerDialogue, () => 
                {
                    if (ObjectiveTrackerUI.Instance != null)
                        ObjectiveTrackerUI.Instance.SetObjective(contractToGive);
                });
            }
            else
            {
                if (ObjectiveTrackerUI.Instance != null)
                    ObjectiveTrackerUI.Instance.SetObjective(contractToGive);
            }

            // NEW: Flip the switch so we don't give the job again!
            hasGivenContract = true;
            
            // Optional: Change the button text from "Accept Job" to "Talk" 
            promptMessage = "Talk to " + contractToGive.clientName;
        }
        // Scenario B: We already gave the contract
        else
        {
            // Just play the reminder dialogue, don't reset the cargo or objective
            if (dm != null && contractToGive.reminderDialogue != null)
            {
                contractToGive.reminderDialogue.name = contractToGive.clientName;
                dm.StartDialogue(contractToGive.reminderDialogue);
            }
        }
    }
}