using UnityEngine;

public class TutorialNameNPC : Interactable
{
    [Header("Dialogues")]
    [Tooltip("The dialogue played when the NPC asks for the player's name.")]
    public Dialogue askNameDialogue;
    
    [Tooltip("The dialogue played after the name is typed, OR if they already have a name.")]
    public Dialogue greetingDialogue;

    [Header("Tutorial Settings")]
    public bool advancesTutorial = false;

    private DialogueManager dialogueManager;
    private NameRegistrationUI nameUI;
    private Transform playerTransform;

    private void Awake()
    {
        dialogueManager = FindObjectOfType<DialogueManager>();
        nameUI = FindObjectOfType<NameRegistrationUI>(true); 
        
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null) playerTransform = player.transform;
    }

    private void Update()
    {
        // Dynamically change the hover text based on if they know you!
        if (PlayerDataManager.Instance != null && PlayerDataManager.Instance.CurrentData.playerName != "Guest")
        {
            promptMessage = "Talk to";
        }
    }

    protected override void Intract()
    {
        FacePlayer();

        if (dialogueManager == null) return;

        string currentName = PlayerDataManager.Instance != null ? PlayerDataManager.Instance.CurrentData.playerName : "Guest";

        // SCENARIO 1: The name is still the default "Guest", they haven't registered yet!
        if (currentName == "Guest" || string.IsNullOrEmpty(currentName))
        {
            dialogueManager.StartDialogue(askNameDialogue, () => 
            {
                if (nameUI != null) nameUI.ShowNamePrompt();
            });
        }
        // SCENARIO 2: The player already has a name, just play the greeting!
        else
        {
            dialogueManager.StartDialogue(greetingDialogue, () => 
            {
                if (advancesTutorial && TutorialManager.Instance != null)
                {
                    TutorialManager.Instance.ShowNextStep();
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

    // Call this from the NameRegistrationUI's "On Name Confirmed" event!
    public void OnNameRegistered()
    {
        if (dialogueManager != null)
        {
            dialogueManager.StartDialogue(greetingDialogue, () => 
            {
                if (advancesTutorial && TutorialManager.Instance != null)
                {
                    TutorialManager.Instance.ShowNextStep();
                }
            });
        }
    }
}