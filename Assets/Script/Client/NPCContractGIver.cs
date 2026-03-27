using UnityEngine;

public class NPCContractGiver : Interactable
{
    [Header("Contract Assignment")]
    public ContractSO contractToGive;
    public BuildLocation targetBuildLocation;
    
    public CargoItem linkedCargo; 

    [Header("Tutorial Settings")]
    public bool advancesTutorial = false; 

    private bool hasGivenContract = false;
    
    // --- NEW: Tracks if the finish line has been crossed! ---
    [HideInInspector] public bool isContractCompleted = false; 

    protected override void Intract() 
    {
        FacePlayer(); 

        if (contractToGive == null) return;
        
        DialogueManager dm = FindObjectOfType<DialogueManager>();

        // Scenario C: The bridge was tested successfully! 
        if (isContractCompleted)
        {
            if (dm != null && contractToGive.finishedContractDialogue != null)
            {
                contractToGive.finishedContractDialogue.name = contractToGive.clientName;
                dm.StartDialogue(contractToGive.finishedContractDialogue, () => 
                {
                    // Show the 'Complete' button on the UI only AFTER they finish talking
                    if (ObjectiveTrackerUI.Instance != null)
                        ObjectiveTrackerUI.Instance.ShowCompleteButton();
                });
            }
            promptMessage = "Contract Complete!";
        }
        // Scenario A: We haven't given the contract yet
        else if (!hasGivenContract)
        {
            if (targetBuildLocation != null)
                targetBuildLocation.activeContract = contractToGive;

            if (linkedCargo != null)
                linkedCargo.SetWeight(contractToGive.liveLoadWeight); // Updated variable

            if (dm != null && contractToGive.offerDialogue != null)
            {
                contractToGive.offerDialogue.name = contractToGive.clientName;
                
                dm.StartDialogue(contractToGive.offerDialogue, () => 
                {
                    if (ObjectiveTrackerUI.Instance != null)
                        ObjectiveTrackerUI.Instance.SetObjective(contractToGive);

                    if (advancesTutorial && TutorialManager.Instance != null)
                        TutorialManager.Instance.ShowNextStep();
                });
            }
            else
            {
                if (ObjectiveTrackerUI.Instance != null)
                    ObjectiveTrackerUI.Instance.SetObjective(contractToGive);

                if (advancesTutorial && TutorialManager.Instance != null)
                    TutorialManager.Instance.ShowNextStep();
            }

            hasGivenContract = true;
            promptMessage = "Talk to " + contractToGive.clientName;
        }
        // Scenario B: We already gave the contract, but it isn't finished yet
        else
        {
            if (dm != null && contractToGive.reminderDialogue != null)
            {
                contractToGive.reminderDialogue.name = contractToGive.clientName;
                dm.StartDialogue(contractToGive.reminderDialogue);
            }
        }
    }

    private void FacePlayer()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            Vector3 targetPosition = player.transform.position;
            targetPosition.y = transform.position.y;
            transform.LookAt(targetPosition);
        }
    }
}