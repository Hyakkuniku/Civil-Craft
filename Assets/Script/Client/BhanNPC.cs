using UnityEngine;

public class BhanNPC : Interactable
{
    [Header("Contract Assignment")]
    public ContractSO contractToGive;
    public BuildLocation targetBuildLocation;
    public CargoItem linkedCargo; 

    [Header("Tutorial Settings")]
    public bool advancesTutorial = false; 
    public bool requiresAlmanacFirst = false;
    public Dialogue needAlmanacDialogue;

    private bool hasGivenContract = false;
    [HideInInspector] public bool isContractCompleted = false; 

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

        if (isContractCompleted)
        {
            if (dialogueManager != null && contractToGive.finishedContractDialogue != null)
            {
                contractToGive.finishedContractDialogue.name = contractToGive.clientName;
                dialogueManager.StartDialogue(contractToGive.finishedContractDialogue, () => 
                {
                    if (ObjectiveTrackerUI.Instance != null) ObjectiveTrackerUI.Instance.ShowCompleteButton();
                });
            }
            promptMessage = "Contract Complete!";
            return;
        }
        
        if (requiresAlmanacFirst && PlayerDataManager.Instance != null && !PlayerDataManager.Instance.CurrentData.hasAlmanac)
        {
            if (dialogueManager != null && needAlmanacDialogue != null)
            {
                needAlmanacDialogue.name = contractToGive.clientName;
                dialogueManager.StartDialogue(needAlmanacDialogue, () => 
                {
                    if (advancesTutorial && TutorialManager.Instance != null) TutorialManager.Instance.ShowNextStep();
                });
            }
            return; 
        }

        if (!hasGivenContract)
        {
            if (targetBuildLocation != null) targetBuildLocation.activeContract = contractToGive;
            if (linkedCargo != null) linkedCargo.SetWeight(contractToGive.liveLoadWeight);

            if (dialogueManager != null && contractToGive.offerDialogue != null)
            {
                contractToGive.offerDialogue.name = contractToGive.clientName;
                dialogueManager.StartDialogue(contractToGive.offerDialogue, () => 
                {
                    if (ObjectiveTrackerUI.Instance != null) ObjectiveTrackerUI.Instance.SetObjective(contractToGive);
                    if (advancesTutorial && TutorialManager.Instance != null) TutorialManager.Instance.ShowNextStep();
                });
            }
            else
            {
                if (ObjectiveTrackerUI.Instance != null) ObjectiveTrackerUI.Instance.SetObjective(contractToGive);
                if (advancesTutorial && TutorialManager.Instance != null) TutorialManager.Instance.ShowNextStep();
            }

            hasGivenContract = true;
            promptMessage = "Talk to " + contractToGive.clientName;
        }
        else
        {
            if (dialogueManager != null && contractToGive.reminderDialogue != null)
            {
                contractToGive.reminderDialogue.name = contractToGive.clientName;
                dialogueManager.StartDialogue(contractToGive.reminderDialogue);
            }
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