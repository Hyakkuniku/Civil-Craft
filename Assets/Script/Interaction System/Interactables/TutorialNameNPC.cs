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
    public string rewardHatID = "EngineerHardHat";
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

    private void Awake()
    {
        dialogueManager = FindObjectOfType<DialogueManager>();
        nameUI = FindObjectOfType<NameRegistrationUI>(true); 
        
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null) playerTransform = player.transform;
    }

    private void Update()
    {
        if (PlayerDataManager.Instance == null) return;

        bool hasName = PlayerDataManager.Instance.CurrentData.playerName != "Guest" && !string.IsNullOrEmpty(PlayerDataManager.Instance.CurrentData.playerName);
        bool hasBook = PlayerDataManager.Instance.CurrentData.hasAlmanac;

        if (!hasName) promptMessage = "Talk to NPC";
        else if (!hasBook) promptMessage = "Ask about the book";
        else promptMessage = "Show the Almanac";
    }

    protected override void Intract()
    {
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

                if (npcWalker != null)
                {
                    npcWalker.StartWalking();
                }

                // --- THE FIX: Summon the UI Popup instead of instantly equipping! ---
                if (ItemUnlockUI.Instance != null && !string.IsNullOrEmpty(rewardHatID))
                {
                    ItemUnlockUI.Instance.ShowReward(rewardDisplayName, rewardSprite, rewardHatID, () => 
                    {
                        onFinalDialogueFinished?.Invoke();
                    });
                }
                else
                {
                    // Fallback just in case you forgot to add the UI Canvas to the scene!
                    if (PlayerCosmetics.Instance != null && !string.IsNullOrEmpty(rewardHatID))
                    {
                        PlayerCosmetics.Instance.UnlockAndEquipHat(rewardHatID);
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