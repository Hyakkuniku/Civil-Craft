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
    private bool isLocked = false;

    private void Awake()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null) playerTransform = player.transform;
        
        dialogueManager = FindObjectOfType<DialogueManager>();
    }

    private void Start()
    {
        if (contractToGive != null)
        {
            isLocked = PlayerPrefs.GetInt("LockedContract_" + contractToGive.name, 0) == 1;
        }

        if (contractToGive != null && PlayerDataManager.Instance != null && !isLocked)
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

        if (isLocked)
        {
            promptMessage = "Contract Locked (Failed)";
            return;
        }

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

        if (isLocked)
        {
            Debug.Log("Contract is locked. You failed the time limit!");
            return;
        }

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
            if (targetBuildLocation != null) targetBuildLocation.activeContract = contractToGive;
            if (linkedCargo != null) linkedCargo.SetWeight(contractToGive.liveLoadWeight);

            // --- THE FIX: Look for the custom Navigation Target first! ---
            string targetLocName = "";
            if (targetBuildLocation != null)
            {
                targetLocName = targetBuildLocation.navigationTarget != null ? targetBuildLocation.navigationTarget.name : targetBuildLocation.gameObject.name;
            }
            else
            {
                targetLocName = gameObject.name; 
            }

            if (dialogueManager != null && contractToGive.offerDialogue != null)
            {
                contractToGive.offerDialogue.name = contractToGive.clientName;
                
                dialogueManager.StartDialogue(contractToGive.offerDialogue, () => 
                {
                    if (ObjectiveTrackerUI.Instance != null) ObjectiveTrackerUI.Instance.SetObjective(contractToGive, targetLocName);
                    TryAdvanceTutorial();
                });
            }
            else
            {
                if (ObjectiveTrackerUI.Instance != null) ObjectiveTrackerUI.Instance.SetObjective(contractToGive, targetLocName);
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