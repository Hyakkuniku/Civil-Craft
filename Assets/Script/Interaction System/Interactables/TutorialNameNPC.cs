using UnityEngine;

public class TutorialNameNPC : Interactable
{
    [Header("Dialogues (Phase 1: Registration)")]
    [Tooltip("The dialogue played when the NPC asks for the player's name.")]
    public Dialogue askNameDialogue;
    
    [Header("Dialogues (Phase 2: Get Almanac)")]
    [Tooltip("The dialogue played immediately after they register their name.")]
    public Dialogue fetchAlmanacDialogue;
    [Tooltip("The dialogue played if they talk to him again before grabbing the book.")]
    public Dialogue reminderAlmanacDialogue;

    [Header("Dialogues (Phase 3: Exit House)")]
    [Tooltip("The dialogue played after they grab the book and talk to him again.")]
    public Dialogue finalHouseDialogue;

    [Header("Tutorial Settings")]
    [Tooltip("Does talking to this NPC at the end of the quest advance the tutorial?")]
    public bool advancesTutorial = false;

    // --- NEW: Link to the Walker Script! ---
    [Header("Movement")]
    [Tooltip("Drag the NPCWalker script here so we can tell him to walk!")]
    public NPCWalker npcWalker;

    private DialogueManager dialogueManager;
    private NameRegistrationUI nameUI;
    private Transform playerTransform;

    // Track the quest state locally
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
        // Dynamically change the hover text based on the quest state!
        if (PlayerDataManager.Instance == null) return;

        bool hasName = PlayerDataManager.Instance.CurrentData.playerName != "Guest" && !string.IsNullOrEmpty(PlayerDataManager.Instance.CurrentData.playerName);
        bool hasBook = PlayerDataManager.Instance.CurrentData.hasAlmanac;

        if (!hasName)
        {
            promptMessage = "Talk to NPC";
        }
        else if (!hasBook)
        {
            promptMessage = "Ask about the book";
        }
        else
        {
            promptMessage = "Show the Almanac";
        }
    }

    protected override void Intract()
    {
        FacePlayer();

        if (dialogueManager == null) return;

        string currentName = PlayerDataManager.Instance != null ? PlayerDataManager.Instance.CurrentData.playerName : "Guest";
        bool hasBook = PlayerDataManager.Instance != null && PlayerDataManager.Instance.CurrentData.hasAlmanac;

        // STATE 1: The name is still the default "Guest", they haven't registered yet!
        if (currentName == "Guest" || string.IsNullOrEmpty(currentName))
        {
            dialogueManager.StartDialogue(askNameDialogue, () => 
            {
                if (nameUI != null) nameUI.ShowNamePrompt();
            });
        }
        // STATE 2: They have a name, but NO ALMANAC
        else if (!hasBook)
        {
            if (!hasGivenFetchQuest)
            {
                // First time giving them the quest
                dialogueManager.StartDialogue(fetchAlmanacDialogue, () => 
                {
                    hasGivenFetchQuest = true;
                    // --- OPTIONAL: Advance tutorial here if your sequence expects it! ---
                    if (advancesTutorial && TutorialManager.Instance != null) TutorialManager.Instance.ShowNextStep();
                });
            }
            else
            {
                // They clicked on him again without getting the book
                dialogueManager.StartDialogue(reminderAlmanacDialogue, null);
            }
        }
        // STATE 3: They have a name AND they have the Almanac!
        else
        {
            dialogueManager.StartDialogue(finalHouseDialogue, () => 
            {
                // The NPC says "Follow me outside!"
                if (advancesTutorial && TutorialManager.Instance != null)
                {
                    TutorialManager.Instance.ShowNextStep();
                }

                // --- THE FIX: Tell the NPC to walk to the door! ---
                if (npcWalker != null)
                {
                    npcWalker.StartWalking();
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

    // Call this from the NameRegistrationUI's "On Name Confirmed" UnityEvent!
    public void OnNameRegistered()
    {
        if (dialogueManager != null)
        {
            dialogueManager.StartDialogue(fetchAlmanacDialogue, () => 
            {
                hasGivenFetchQuest = true;
                
                // Advance the tutorial (this triggers the rock path to point to the Almanac!)
                if (advancesTutorial && TutorialManager.Instance != null)
                {
                    TutorialManager.Instance.ShowNextStep();
                }
            });
        }
    }
}