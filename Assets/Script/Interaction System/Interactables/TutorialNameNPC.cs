using UnityEngine;
using UnityEngine.Events;

public class TutorialNameNPC : Interactable
{
    [Header("Dialogues (Phase 1: Registration)")]
    public Dialogue askNameDialogue;
    
    [Header("Dialogues (Phase 2: Get Almanac)")]
    public Dialogue fetchAlmanacDialogue;
    public Dialogue reminderAlmanacDialogue;

    [Header("Dialogues (Phase 3: Exit House)")]
    public Dialogue finalHouseDialogue;

    [Header("Tutorial Settings")]
    public bool advancesTutorial = false;

    [Header("Movement")]
    public NPCWalker npcWalker;

    // --- NEW: UI Reward Integration ---
    [Header("Cosmetic Reward")]
    public string rewardHatID = "EngineeringHardHat";
    [Tooltip("The name of the item shown on the UI popup")]
    public string rewardDisplayName = "Engineer's Hard Hat";
    [Tooltip("The 2D picture of the hat for the UI popup")]
    public Sprite rewardSprite; 
    
    [Tooltip("Fires AFTER the player clicks 'Collect' on the UI popup")]
    public UnityEvent onFinalDialogueFinished;

    private DialogueManager dialogueManager;
    private NameRegistrationUI nameUI;
    private Transform playerTransform;

    private bool hasGivenFetchQuest = false;
    private bool isWalkingAway = false; // --- NEW: Tracks if the NPC is leaving ---

    private void Awake()
    {
        dialogueManager = FindObjectOfType<DialogueManager>();
        nameUI = FindObjectOfType<NameRegistrationUI>(true); 
        
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null) playerTransform = player.transform;
    }

    private void Update()
    {
        // --- THE FIX: Stop updating the prompt once they start walking ---
        if (isWalkingAway)
        {
            promptMessage = "";
            return;
        }

        if (PlayerDataManager.Instance == null) return;

        bool hasName = PlayerDataManager.Instance.CurrentData.playerName != "Guest" && !string.IsNullOrEmpty(PlayerDataManager.Instance.CurrentData.playerName);
        bool hasBook = PlayerDataManager.Instance.CurrentData.hasAlmanac;

        if (!hasName) promptMessage = "Talk to NPC";
        else if (!hasBook) promptMessage = "Ask about the book";
        else promptMessage = "Show the Almanac";
    }

    protected override void Intract()
    {
        // --- THE FIX: Block interaction if they are walking away ---
        if (isWalkingAway) return;

        FacePlayer();

        if (dialogueManager == null) return;

        string currentName = PlayerDataManager.Instance != null ? PlayerDataManager.Instance.CurrentData.playerName : "Guest";
        bool hasBook = PlayerDataManager.Instance != null && PlayerDataManager.Instance.CurrentData.hasAlmanac;

        if (currentName == "Guest" || string.IsNullOrEmpty(currentName))
        {
            dialogueManager.StartDialogue(askNameDialogue, () => 
            {
                if (nameUI != null) nameUI.ShowNamePrompt();
            });
        }
        else if (!hasBook)
        {
            if (!hasGivenFetchQuest)
            {
                dialogueManager.StartDialogue(fetchAlmanacDialogue, () => 
                {
                    hasGivenFetchQuest = true;
                    if (advancesTutorial && TutorialManager.Instance != null) TutorialManager.Instance.ShowNextStep();
                });
            }
            else
            {
                dialogueManager.StartDialogue(reminderAlmanacDialogue, null);
            }
        }
        else
        {
            dialogueManager.StartDialogue(finalHouseDialogue, () => 
            {
                if (advancesTutorial && TutorialManager.Instance != null)
                {
                    TutorialManager.Instance.ShowNextStep();
                }

                // --- THE FIX: Disable interactions and collider instantly ---
                isWalkingAway = true;
                promptMessage = "";
                Collider myCollider = GetComponent<Collider>();
                if (myCollider != null) myCollider.enabled = false;

                if (npcWalker != null)
                {
                    npcWalker.StartWalking();
                }

                bool alreadyOwnsReward = PlayerDataManager.Instance != null &&
                                         PlayerDataManager.Instance.IsCosmeticUnlocked(rewardHatID);

                if (ItemUnlockUI.Instance != null &&
                    !string.IsNullOrEmpty(rewardHatID) &&
                    !alreadyOwnsReward)
                {
                    ItemUnlockUI.Instance.ShowReward(rewardDisplayName, rewardSprite, rewardHatID, () => 
                    {
                        AchievementPopupNotification.NotifyCosmeticUnlock(rewardDisplayName, rewardSprite);
                        onFinalDialogueFinished?.Invoke();
                    });
                }
                else
                {
                    if (!alreadyOwnsReward && !string.IsNullOrEmpty(rewardHatID))
                    {
                        if (PlayerCosmetics.Instance != null)
                            PlayerCosmetics.Instance.UnlockAndEquipHat(rewardHatID);
                        else if (PlayerDataManager.Instance != null)
                            PlayerDataManager.Instance.UnlockCosmeticReward(rewardHatID, true);

                        AchievementPopupNotification.NotifyCosmeticUnlock(rewardDisplayName, rewardSprite);
                    }
                    onFinalDialogueFinished?.Invoke();
                }
            });
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
            });
        }
    }
}
