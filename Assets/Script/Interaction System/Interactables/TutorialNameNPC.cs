using UnityEngine;
using UnityEngine.Events;

public class TutorialNameNPC : Interactable
{
    [Header("Dialogues (Phase 1: Registration)")]
    public Dialogue askNameDialogue;
    
    [Header("Dialogues (Phase 2: Get Almanac)")]
    public Dialogue fetchAlmanacDialogue;
    public Dialogue reminderAlmanacDialogue;

    [Header("Dialogues (Phase 3: Reward and Exit House)")]
    public Dialogue rewardDialogue;
    public Dialogue finalHouseDialogue;

    [Header("Tutorial Settings")]
    public bool advancesTutorial = false;

    [Tooltip("Tutorial that must be completed before the Almanac can be returned to Professor Bhan.")]
    [SerializeField] private string requiredAlmanacTutorialId = "Sequence_Alamnac";

    [Tooltip("House tutorial saved when Professor Bhan's final dialogue finishes.")]
    [SerializeField] private string houseTutorialId = "Sequence_House";

    [Tooltip("Optional reminder used when the player owns the Almanac but has not reviewed it yet.")]
    [SerializeField] private Dialogue reviewAlmanacDialogue;

    [Header("Movement")]
    public NPCWalker npcWalker;

    // --- NEW: UI Reward Integration ---
    [Header("Cosmetic Reward")]
    public string rewardHatID = "EngineeringHardHat";
    [Tooltip("The name of the item shown on the UI popup")]
    public string rewardDisplayName = "Engineer's Hard Hat";
    [Tooltip("The 2D picture of the hat for the UI popup")]
    public Sprite rewardSprite; 
    
    [Tooltip("Fires after the final house dialogue finishes, immediately before the NPC walks away.")]
    public UnityEvent onFinalDialogueFinished;

    private DialogueManager dialogueManager;
    private Animator npcAnimator;
    private NameRegistrationUI nameUI;

    private bool hasGivenFetchQuest = false;
    private bool isCompletingHouseInteraction = false;
    private bool isWalkingAway = false; // --- NEW: Tracks if the NPC is leaving ---
    private Dialogue fallbackReviewAlmanacDialogue;

    private void Awake()
    {
        dialogueManager = FindObjectOfType<DialogueManager>();
        npcAnimator = GetComponentInChildren<Animator>();
        nameUI = FindObjectOfType<NameRegistrationUI>(true); 
    }

    private void Update()
    {
        // --- THE FIX: Stop updating the prompt once they start walking ---
        if (isCompletingHouseInteraction || isWalkingAway)
        {
            promptMessage = "";
            return;
        }

        if (PlayerDataManager.Instance == null) return;

        bool hasName = PlayerDataManager.Instance.CurrentData.playerName != "Guest" && !string.IsNullOrEmpty(PlayerDataManager.Instance.CurrentData.playerName);
        bool hasBook = PlayerDataManager.Instance.CurrentData.hasAlmanac;

        if (!hasName) promptMessage = "Talk To Professor Bhan";
        else if (!hasBook) promptMessage = "Ask about the book";
        else if (!HasCompletedRequiredAlmanacTutorial()) promptMessage = "Review the Almanac";
        else promptMessage = "Show the Almanac";
    }

    protected override void Intract()
    {
        // --- THE FIX: Block interaction if they are walking away ---
        if (isCompletingHouseInteraction || isWalkingAway) return;

        if (dialogueManager == null) return;

        string currentName = PlayerDataManager.Instance != null ? PlayerDataManager.Instance.CurrentData.playerName : "Guest";
        bool hasBook = PlayerDataManager.Instance != null && PlayerDataManager.Instance.CurrentData.hasAlmanac;

        if (currentName == "Guest" || string.IsNullOrEmpty(currentName))
        {
            dialogueManager.StartDialogue(askNameDialogue, () => 
            {
                if (nameUI != null) nameUI.ShowNamePrompt();
            }, npcAnimator, transform);
        }
        else if (!hasBook)
        {
            if (!hasGivenFetchQuest)
            {
                dialogueManager.StartDialogue(fetchAlmanacDialogue, () => 
                {
                    hasGivenFetchQuest = true;
                    if (advancesTutorial && TutorialManager.Instance != null) TutorialManager.Instance.ShowNextStep();
                }, npcAnimator, transform);
            }
            else
            {
                dialogueManager.StartDialogue(reminderAlmanacDialogue, null, npcAnimator, transform);
            }
        }
        else
        {
            // Owning the book is not enough: reviewing it is a required quest
            // action. Keep Bhan available and reopen the Almanac after the
            // reminder instead of allowing the reward/exit flow to run early.
            if (!HasCompletedRequiredAlmanacTutorial())
            {
                dialogueManager.StartDialogue(
                    GetReviewAlmanacDialogue(),
                    OpenAlmanacForRequiredReview,
                    npcAnimator,
                    transform);
                return;
            }

            isCompletingHouseInteraction = true;
            promptMessage = "";

            if (rewardDialogue != null)
                dialogueManager.StartDialogue(
                    rewardDialogue,
                    ShowRewardThenFinalDialogue,
                    npcAnimator,
                    transform);
            else
                ShowRewardThenFinalDialogue();
        }
    }

    private void ShowRewardThenFinalDialogue()
    {
        bool alreadyOwnsReward = PlayerDataManager.Instance != null &&
                                 PlayerDataManager.Instance.IsCosmeticUnlocked(rewardHatID);

        if (ItemUnlockUI.Instance != null &&
            !string.IsNullOrEmpty(rewardHatID) &&
            !alreadyOwnsReward)
        {
            ItemUnlockUI.Instance.ShowReward(rewardDisplayName, rewardSprite, rewardHatID, () =>
            {
                AchievementPopupNotification.NotifyCosmeticUnlock(rewardDisplayName, rewardSprite);
                StartFinalHouseDialogue();
            });
            return;
        }

        if (!alreadyOwnsReward && !string.IsNullOrEmpty(rewardHatID))
        {
            if (PlayerCosmetics.Instance != null)
                PlayerCosmetics.Instance.UnlockAndEquipHat(rewardHatID);
            else if (PlayerDataManager.Instance != null)
                PlayerDataManager.Instance.UnlockCosmeticReward(rewardHatID, true);

            AchievementPopupNotification.NotifyCosmeticUnlock(rewardDisplayName, rewardSprite);
        }

        StartFinalHouseDialogue();
    }

    private void StartFinalHouseDialogue()
    {
        if (dialogueManager != null && finalHouseDialogue != null)
            dialogueManager.StartDialogue(
                finalHouseDialogue,
                FinishHouseInteraction,
                npcAnimator,
                transform);
        else
            FinishHouseInteraction();
    }

    private void FinishHouseInteraction()
    {
        CompleteHouseTutorialSafely();

        isWalkingAway = true;
        promptMessage = "";

        Collider myCollider = GetComponent<Collider>();
        if (myCollider != null) myCollider.enabled = false;

        onFinalDialogueFinished?.Invoke();

        if (npcWalker != null)
            npcWalker.StartWalking();
    }

    private bool HasCompletedRequiredAlmanacTutorial()
    {
        if (PlayerDataManager.Instance == null ||
            string.IsNullOrWhiteSpace(requiredAlmanacTutorialId))
        {
            // Test scenes without save data should not become permanently gated.
            return true;
        }

        return PlayerDataManager.Instance.CurrentData.completedLessons.Contains(
            requiredAlmanacTutorialId);
    }

    private Dialogue GetReviewAlmanacDialogue()
    {
        if (reviewAlmanacDialogue != null && reviewAlmanacDialogue.sentences != null &&
            reviewAlmanacDialogue.sentences.Length > 0)
        {
            return reviewAlmanacDialogue;
        }

        if (fallbackReviewAlmanacDialogue == null)
        {
            fallbackReviewAlmanacDialogue = new Dialogue
            {
                name = "Professor Bhan",
                sentences = new[]
                {
                    "Before we continue, open the Almanac and review it.",
                    "It will serve as your engineering record throughout your journey."
                }
            };
        }

        return fallbackReviewAlmanacDialogue;
    }

    private void OpenAlmanacForRequiredReview()
    {
        if (AlmanacManager.Instance != null)
        {
            // A normal HUD-button click advances Sequence_House through the
            // TutorialManager's tracked-button listener. Bhan opens the book
            // directly, so perform the equivalent quest synchronization first.
            // Using the final instruction also repairs reloads that restarted
            // Sequence_House at an earlier step after the book was collected.
            if (TutorialManager.Instance != null)
                TutorialManager.Instance.ShowFinalStepIfPlaying(houseTutorialId);

            AlmanacManager.Instance.OpenAlmanac();
            return;
        }

        Debug.LogWarning(
            "[TutorialNameNPC] The Almanac is required, but no AlmanacManager is available.",
            this);
    }

    private void CompleteHouseTutorialSafely()
    {
        if (!advancesTutorial || string.IsNullOrWhiteSpace(houseTutorialId)) return;

        bool completedActiveSequence = TutorialManager.Instance != null &&
                                       TutorialManager.Instance.CompleteTutorialIfPlaying(houseTutorialId);

        // A reload can restart the house sequence at an earlier instruction.
        // The completed mandatory Almanac review plus Bhan's final dialogue is
        // authoritative, so persist the house result even if that sequence was
        // not the currently displayed tutorial.
        if (!completedActiveSequence && PlayerDataManager.Instance != null)
            PlayerDataManager.Instance.CompleteLesson(houseTutorialId);
    }

    public void OnNameRegistered()
    {
        if (dialogueManager != null)
        {
            dialogueManager.StartDialogue(fetchAlmanacDialogue, () => 
            {
                hasGivenFetchQuest = true;
                
                if (advancesTutorial && TutorialManager.Instance != null)
                {
                    TutorialManager.Instance.ShowNextStep();
                }
            }, npcAnimator, transform);
        }
    }
}
