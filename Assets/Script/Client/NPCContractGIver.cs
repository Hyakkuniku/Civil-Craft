using UnityEngine;

public class NPCContractGiver : Interactable
{
    [Header("Contract Assignment")]
    public ContractSO contractToGive;
    public BuildLocation targetBuildLocation;
    public CargoItem linkedCargo; 

    [Header("Tutorial Settings")]
    [Tooltip("If checked, the tutorial will advance EVERY time the player finishes talking to this NPC.")]
    public bool advancesTutorial = false; 

    private bool hasGivenContract = false;
    [HideInInspector] public bool isContractCompleted = false; 

    // --- OPTIMIZATION: Cache references! ---
    private Transform playerTransform;
    private DialogueManager dialogueManager;

    private void Awake()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null) playerTransform = player.transform;
        
        dialogueManager = FindObjectOfType<DialogueManager>();
    }

    protected override void Intract() 
    {
        FacePlayer(); 

        if (contractToGive == null) return;

        // SCENARIO 1: The contract is completely finished
        if (isContractCompleted)
        {
            if (dialogueManager != null && contractToGive.finishedContractDialogue != null)
            {
                contractToGive.finishedContractDialogue.name = contractToGive.clientName;
                dialogueManager.StartDialogue(contractToGive.finishedContractDialogue, () => 
                {
                    if (ObjectiveTrackerUI.Instance != null)
                        ObjectiveTrackerUI.Instance.ShowCompleteButton();
                        
                    // Advance tutorial after dialogue finishes
                    TryAdvanceTutorial();
                });
            }
            else
            {
                TryAdvanceTutorial();
            }
            promptMessage = "Contract Complete!";
        }
        // SCENARIO 2: The player is talking to them for the FIRST time to get the contract
        else if (!hasGivenContract)
        {
            if (targetBuildLocation != null) targetBuildLocation.activeContract = contractToGive;
            if (linkedCargo != null) linkedCargo.SetWeight(contractToGive.liveLoadWeight);

            if (dialogueManager != null && contractToGive.offerDialogue != null)
            {
                contractToGive.offerDialogue.name = contractToGive.clientName;
                
                dialogueManager.StartDialogue(contractToGive.offerDialogue, () => 
                {
                    if (ObjectiveTrackerUI.Instance != null) ObjectiveTrackerUI.Instance.SetObjective(contractToGive);
                    
                    // Advance tutorial after dialogue finishes
                    TryAdvanceTutorial();
                });
            }
            else
            {
                if (ObjectiveTrackerUI.Instance != null) ObjectiveTrackerUI.Instance.SetObjective(contractToGive);
                TryAdvanceTutorial();
            }

            hasGivenContract = true;
            promptMessage = "Talk to " + contractToGive.clientName;
        }
        // SCENARIO 3: The player talks to them AGAIN before finishing the bridge (Reminder)
        else
        {
            if (dialogueManager != null && contractToGive.reminderDialogue != null)
            {
                contractToGive.reminderDialogue.name = contractToGive.clientName;
                dialogueManager.StartDialogue(contractToGive.reminderDialogue, () => 
                {
                    // Advance tutorial after the reminder dialogue finishes
                    TryAdvanceTutorial();
                });
            }
            else
            {
                TryAdvanceTutorial();
            }
        }
    }

    private void TryAdvanceTutorial()
    {
        if (advancesTutorial && TutorialManager.Instance != null)
        {
            TutorialManager.Instance.ShowNextStep();
        }
    }

    private void FacePlayer()
    {
        if (playerTransform != null)
        {
            Vector3 targetPosition = playerTransform.position;
            targetPosition.y = transform.position.y;
            transform.LookAt(targetPosition);
        }
    }
}