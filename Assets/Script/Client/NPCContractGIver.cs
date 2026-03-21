using UnityEngine;

public class NPCContractGiver : Interactable
{
    [Header("Contract Assignment")]
    public ContractSO contractToGive;
    public BuildLocation targetBuildLocation;
    
    public CargoItem linkedCargo; 

    [Header("Tutorial Settings")]
    [Tooltip("Check this if talking to this NPC should advance the tutorial!")]
    public bool advancesTutorial = false; // <-- NEW: Tutorial Checkbox

    private bool hasGivenContract = false;

    protected override void Intract() // Keeping your exact spelling here!
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
                
                // Start the dialogue, and run this block of code when it finishes!
                dm.StartDialogue(contractToGive.offerDialogue, () => 
                {
                    if (ObjectiveTrackerUI.Instance != null)
                        ObjectiveTrackerUI.Instance.SetObjective(contractToGive);

                    // <-- NEW: Advance the tutorial after they finish talking! -->
                    if (advancesTutorial && TutorialManager.Instance != null)
                    {
                        TutorialManager.Instance.ShowNextStep();
                    }
                });
            }
            else
            {
                if (ObjectiveTrackerUI.Instance != null)
                    ObjectiveTrackerUI.Instance.SetObjective(contractToGive);

                // <-- NEW: Advance tutorial even if there is no dialogue text -->
                if (advancesTutorial && TutorialManager.Instance != null)
                {
                    TutorialManager.Instance.ShowNextStep();
                }
            }

            hasGivenContract = true;
            
            promptMessage = "Talk to " + contractToGive.clientName;
        }
        // Scenario B: We already gave the contract
        else
        {
            if (dm != null && contractToGive.reminderDialogue != null)
            {
                contractToGive.reminderDialogue.name = contractToGive.clientName;
                dm.StartDialogue(contractToGive.reminderDialogue);
            }
        }
    }
}