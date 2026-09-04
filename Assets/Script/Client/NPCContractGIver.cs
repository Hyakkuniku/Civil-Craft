using System;
using UnityEngine;
using UnityEngine.Events;

public class NPCContractGiver : Interactable
{
    [Header("Contract Assignment")]
    public ContractSO contractToGive;
    public BuildLocation targetBuildLocation;
    public CargoItem linkedCargo; 

    [Header("Tutorial Settings")]
    public bool advancesTutorial = false; 

    [Header("Events")]
    [Tooltip("Fires after the player accepts the contract and the optional continuation dialogue finishes.")]
    public UnityEvent onOfferDialogueFinished;

    private bool hasGivenContract = false;
    private bool isAwaitingContractDecision;
    [HideInInspector] public bool isContractCompleted = false; 
    [HideInInspector] public bool isFullyTurnedIn = false; 

    private Transform playerTransform;
    private DialogueManager dialogueManager;
    private Animator npcAnimator;
    private bool isLocked = false;
    private bool isProgressionInteractionLocked;
    private string progressionLockedPrompt = "Moving to the next site...";
    private bool usesProgressionPhase;
    private string progressionIdlePrompt = string.Empty;

    /// <summary>
    /// Runtime notification used by NPCProgressionManager. It deliberately stays
    /// separate from the Inspector UnityEvents so existing NPC setups are unchanged.
    /// </summary>
    public event Action<NPCContractGiver> OnNPCInteracted;

    /// <summary>
    /// Runtime notification fired after acceptance and the optional continuation
    /// dialogue. NPCProgressionManager uses it for phase-specific follow-up panels.
    /// </summary>
    public event Action<NPCContractGiver> OnOfferDialogueCompleted;

    private void Awake()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null) playerTransform = player.transform;
        
        dialogueManager = FindObjectOfType<DialogueManager>();
        npcAnimator = GetComponentInChildren<Animator>();
    }

    private void Start()
    {
        if (contractToGive != null)
        {
            isLocked = PlayerPrefs.GetInt("LockedContract_" + contractToGive.ContractID, 0) == 1;
        }

        if (contractToGive != null && PlayerDataManager.Instance != null && !isLocked)
        {
            bool isCompleted = PlayerDataManager.Instance.IsContractCompleted(contractToGive.ContractID);
            bool hasSavedBridge = PlayerDataManager.Instance.GetSavedBridge(contractToGive.ContractID) != null;
            bool hasActiveQuest = PlayerDataManager.Instance.CurrentData.activeQuests != null &&
                                  PlayerDataManager.Instance.CurrentData.activeQuests.Exists(task =>
                                      task != null && contractToGive.MatchesIdentifier(task.contractName) &&
                                      !task.isCompleted);

            if (isCompleted || hasSavedBridge || hasActiveQuest)
            {
                if (isCompleted) isFullyTurnedIn = true;
                hasGivenContract = true;
                isContractCompleted = isCompleted || hasSavedBridge;
                
                if (targetBuildLocation != null) 
                {
                    targetBuildLocation.activeContract = contractToGive;
                    if (hasSavedBridge) targetBuildLocation.LoadSavedBridge();
                }
            }
            
            if (linkedCargo != null) linkedCargo.SetWeight(contractToGive.liveLoadWeight);
        }
    }

    private void Update()
    {
        if (isProgressionInteractionLocked)
        {
            promptMessage = progressionLockedPrompt;
            return;
        }

        if (contractToGive == null)
        {
            if (usesProgressionPhase) promptMessage = progressionIdlePrompt;
            return;
        }

        if (isLocked)
        {
            promptMessage = "Contract Locked (Failed)";
            return;
        }

        if (!isFullyTurnedIn && LevelCompleteManager.Instance != null && LevelCompleteManager.Instance.IsContractPaid(contractToGive.ContractID))
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
        else if (isAwaitingContractDecision)
        {
            promptMessage = "Review Contract Offer";
        }
        else if (hasGivenContract)
        {
            promptMessage = "Talk to " + contractToGive.clientName;
        }
        else
        {
            string npcName = string.IsNullOrWhiteSpace(contractToGive.clientName)
                ? gameObject.name
                : contractToGive.clientName;
            promptMessage = "Talk to " + npcName;
        }
    }

    protected override void Intract() 
    {
        if (isProgressionInteractionLocked || isAwaitingContractDecision) return;

        FacePlayer(); 

        OnNPCInteracted?.Invoke(this);

        if (contractToGive == null) return;

        if (isLocked)
        {
            Debug.Log("Contract is locked. You failed the time limit!");
            return;
        }

        if (LevelCompleteManager.Instance != null && LevelCompleteManager.Instance.IsContractPaid(contractToGive.ContractID))
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
                }, npcAnimator);
            }
            else
            {
                ClaimReward();
            }
        }
        else if (!hasGivenContract)
        {
            BeginContractOffer();
        }
        else
        {
            if (dialogueManager != null && contractToGive.reminderDialogue != null)
            {
                contractToGive.reminderDialogue.name = contractToGive.clientName;
                dialogueManager.StartDialogue(contractToGive.reminderDialogue, () => 
                {
                    TryAdvanceTutorial();
                }, npcAnimator);
            }
            else
            {
                TryAdvanceTutorial();
            }
        }
    }

    private void BeginContractOffer()
    {
        isAwaitingContractDecision = true;

        string targetLocationName;
        if (targetBuildLocation != null)
        {
            targetLocationName = targetBuildLocation.navigationTarget != null
                ? targetBuildLocation.navigationTarget.name
                : targetBuildLocation.gameObject.name;
        }
        else
        {
            targetLocationName = gameObject.name;
        }

        if (dialogueManager == null)
            dialogueManager = FindObjectOfType<DialogueManager>();

        if (dialogueManager != null && contractToGive.offerDialogue != null)
        {
            contractToGive.offerDialogue.name = contractToGive.clientName;
            dialogueManager.StartDialogue(
                contractToGive.offerDialogue,
                () => PresentContractOffer(targetLocationName),
                npcAnimator);
        }
        else
        {
            PresentContractOffer(targetLocationName);
        }
    }

    private void PresentContractOffer(string targetLocationName)
    {
        if (ObjectiveTrackerUI.Instance != null &&
            ObjectiveTrackerUI.Instance.ShowContractOffer(
                contractToGive,
                targetLocationName,
                AcceptOfferedContract,
                CancelOfferedContract))
        {
            return;
        }

        // Scenes without an Objective UI retain a usable fallback instead of
        // leaving the NPC permanently stuck waiting for a decision.
        AcceptOfferedContract();
    }

    private void AcceptOfferedContract()
    {
        if (contractToGive == null)
        {
            isAwaitingContractDecision = false;
            return;
        }

        isAwaitingContractDecision = false;
        hasGivenContract = true;

        if (targetBuildLocation != null)
            targetBuildLocation.activeContract = contractToGive;
        if (linkedCargo != null)
            linkedCargo.SetWeight(contractToGive.liveLoadWeight);

        if (dialogueManager == null)
            dialogueManager = FindObjectOfType<DialogueManager>();

        if (dialogueManager != null && contractToGive.continueOfferDialogue != null)
        {
            contractToGive.continueOfferDialogue.name = contractToGive.clientName;
            dialogueManager.StartDialogue(
                contractToGive.continueOfferDialogue,
                CompleteContractOfferFlow,
                npcAnimator);
        }
        else
        {
            CompleteContractOfferFlow();
        }
    }

    private void CancelOfferedContract()
    {
        isAwaitingContractDecision = false;
    }

    private void CompleteContractOfferFlow()
    {
        TryAdvanceTutorial();
        onOfferDialogueFinished?.Invoke();
        OnOfferDialogueCompleted?.Invoke(this);
    }

    private void ClaimReward()
    {
        if (ObjectiveTrackerUI.Instance != null && LevelCompleteManager.Instance != null)
        {
            int gold = LevelCompleteManager.Instance.GetContractGold(contractToGive.ContractID);
            int exp = LevelCompleteManager.Instance.GetContractExp(contractToGive.ContractID);
            
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

    /// <summary>Locks only progression-driven interaction without changing failure locks.</summary>
    public void SetProgressionInteractionLocked(bool locked, string lockedPrompt = "Moving to the next site...")
    {
        isProgressionInteractionLocked = locked;
        if (!string.IsNullOrWhiteSpace(lockedPrompt)) progressionLockedPrompt = lockedPrompt;
    }

    /// <summary>Developer-menu shortcut that unlocks and assigns this contract without dialogue.</summary>
    public void DebugUnlockAndAcceptContract()
    {
        if (contractToGive == null || targetBuildLocation == null) return;

        PlayerPrefs.DeleteKey("LockedContract_" + contractToGive.ContractID);
        PlayerPrefs.Save();
        isLocked = false;
        isProgressionInteractionLocked = false;
        isAwaitingContractDecision = false;
        hasGivenContract = true;
        targetBuildLocation.activeContract = contractToGive;
        if (linkedCargo != null) linkedCargo.SetWeight(contractToGive.liveLoadWeight);
    }

    /// <summary>Reuses this giver for a new phase and rebuilds its runtime state from the save.</summary>
    public void ConfigureProgressionPhase(
        ContractSO phaseContract,
        BuildLocation phaseBuildLocation,
        CargoItem phaseCargo,
        string phaseInteractionPrompt = "")
    {
        usesProgressionPhase = true;
        progressionIdlePrompt = phaseInteractionPrompt ?? string.Empty;
        contractToGive = phaseContract;
        targetBuildLocation = phaseBuildLocation;
        linkedCargo = phaseCargo;

        hasGivenContract = false;
        isAwaitingContractDecision = false;
        isContractCompleted = false;
        isFullyTurnedIn = false;
        isLocked = phaseContract != null &&
                   PlayerPrefs.GetInt("LockedContract_" + phaseContract.ContractID, 0) == 1;

        if (phaseContract == null) return;

        bool isCompleted = PlayerDataManager.Instance != null &&
                           PlayerDataManager.Instance.IsContractCompleted(phaseContract.ContractID);
        bool hasSavedBridge = PlayerDataManager.Instance != null &&
                              PlayerDataManager.Instance.GetSavedBridge(phaseContract.ContractID) != null;
        bool hasActiveQuest = PlayerDataManager.Instance != null &&
                              PlayerDataManager.Instance.CurrentData.activeQuests != null &&
                              PlayerDataManager.Instance.CurrentData.activeQuests.Exists(task =>
                                  task != null && phaseContract.MatchesIdentifier(task.contractName) &&
                                  !task.isCompleted);

        if (isCompleted || hasSavedBridge || hasActiveQuest)
        {
            hasGivenContract = true;
            isContractCompleted = isCompleted || hasSavedBridge;
            isFullyTurnedIn = isCompleted;

            if (targetBuildLocation != null)
            {
                targetBuildLocation.activeContract = phaseContract;
                if (hasSavedBridge) targetBuildLocation.LoadSavedBridge();
            }
        }

        if (linkedCargo != null) linkedCargo.SetWeight(phaseContract.liveLoadWeight);
    }
}
