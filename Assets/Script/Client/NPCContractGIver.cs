using UnityEngine;

public class NPCContractGiver : Interactable
{
    [Header("Contract Assignment")]
    public ContractSO contractToGive;
    public BuildLocation targetBuildLocation;
    
    // ADDED: Link the physical cargo box in the scene to this NPC
    public CargoItem linkedCargo; 

    protected override void Intract()
    {
        if (contractToGive == null) return;

        if (targetBuildLocation != null)
            targetBuildLocation.activeContract = contractToGive;

        // ADDED: Set the cargo weight!
        if (linkedCargo != null)
            linkedCargo.SetWeight(contractToGive.cargoWeight);

        DialogueManager dm = FindObjectOfType<DialogueManager>();
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
    }
}