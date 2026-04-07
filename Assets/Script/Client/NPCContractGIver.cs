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
    [HideInInspector] public bool isContractCompleted = false; 
    [HideInInspector] public bool isFullyTurnedIn = false; 

    private Transform playerTransform;
    private DialogueManager dialogueManager;

    private void Awake()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null) playerTransform = player.transform;
        
        dialogueManager = FindObjectOfType<DialogueManager>();
        
        // Removed the hardcoded activeContract assignment here so the bridge stays locked by default!
    }

    private void Start()
    {
        // --- THE FIX: Only unlock the bridge on load if the player already built it previously ---
        if (contractToGive != null && PlayerDataManager.Instance != null)
        {
            bool isCompleted = PlayerDataManager.Instance.CurrentData.completedContracts.Contains(contractToGive.name);
            bool hasSavedBridge = PlayerDataManager.Instance.GetSavedBridge(contractToGive.name) != null;

            if (isCompleted || hasSavedBridge)
            {
                if (isCompleted) isFullyTurnedIn = true;
                hasGivenContract = true;
                isContractCompleted = true;
                
                if (targetBuildLocation != null) 
                {
                    targetBuildLocation.activeContract = contractToGive;
                    targetBuildLocation.LoadSavedBridge();
                }
            }
            
            if (linkedCargo != null) linkedCargo.SetWeight(contractToGive.liveLoadWeight);
        }
    }

    private void Update()
    {
        if (contractToGive == null) return;

        if (!isFullyTurnedIn && LevelCompleteManager.Instance != null && LevelCompleteManager.Instance.IsContractPaid(contractToGive.name))
        {
            isFullyTurnedIn = true;
        }

        if (isFullyTurnedIn)
        {
            promptMessage = "Bridge Completed!";
        }
        else if (isContractCompleted)
        {
            promptMessage = "Turn in Contract!";
        }
        else if (hasGivenContract)
        {
            promptMessage = "Talk to " + contractToGive.clientName;
        }
        else
        {
            promptMessage = "Accept Contract";
        }
    }

    protected override void Intract() 
    {
        FacePlayer(); 

        if (contractToGive == null) return;

        if (LevelCompleteManager.Instance != null && LevelCompleteManager.Instance.IsContractPaid(contractToGive.name))
        {
            isFullyTurnedIn = true;
        }

        if (isFullyTurnedIn)
        {
            Debug.Log("This NPC has no more jobs for you.");
            return;
        }

        if (isContractCompleted)
        {
            if (dialogueManager != null && contractToGive.finishedContractDialogue != null)
            {
                contractToGive.finishedContractDialogue.name = contractToGive.clientName;
                dialogueManager.StartDialogue(contractToGive.finishedContractDialogue, () => 
                {
                    ClaimReward();
                });
            }
            else
            {
                ClaimReward();
            }
        }
        else if (!hasGivenContract)
        {
            // Now, talking to the NPC actually unlocks the build zone
            if (targetBuildLocation != null) targetBuildLocation.activeContract = contractToGive;
            if (linkedCargo != null) linkedCargo.SetWeight(contractToGive.liveLoadWeight);

            if (dialogueManager != null && contractToGive.offerDialogue != null)
            {
                contractToGive.offerDialogue.name = contractToGive.clientName;
                
                dialogueManager.StartDialogue(contractToGive.offerDialogue, () => 
                {
                    if (ObjectiveTrackerUI.Instance != null) ObjectiveTrackerUI.Instance.SetObjective(contractToGive);
                    TryAdvanceTutorial();
                });
            }
            else
            {
                if (ObjectiveTrackerUI.Instance != null) ObjectiveTrackerUI.Instance.SetObjective(contractToGive);
                TryAdvanceTutorial();
            }

            hasGivenContract = true;
        }
        else
        {
            if (dialogueManager != null && contractToGive.reminderDialogue != null)
            {
                contractToGive.reminderDialogue.name = contractToGive.clientName;
                dialogueManager.StartDialogue(contractToGive.reminderDialogue, () => 
                {
                    TryAdvanceTutorial();
                });
            }
            else
            {
                TryAdvanceTutorial();
            }
        }
    }

    private void ClaimReward()
    {
        if (ObjectiveTrackerUI.Instance != null && LevelCompleteManager.Instance != null)
        {
            int gold = LevelCompleteManager.Instance.GetContractGold(contractToGive.name);
            int exp = LevelCompleteManager.Instance.GetContractExp(contractToGive.name);
            
            if (gold == 0 && exp == 0) 
            {
                gold = contractToGive.goldReward;
                exp = contractToGive.expReward;
            }

            ObjectiveTrackerUI.Instance.ShowCompleteButton(gold, exp, this);
        }
        TryAdvanceTutorial();
    }

    private void TryAdvanceTutorial()
    {
        if (advancesTutorial && TutorialManager.Instance != null) TutorialManager.Instance.ShowNextStep();
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