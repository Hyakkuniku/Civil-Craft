using UnityEngine;
using UnityEngine.Events;

public class DialogueTrigger : MonoBehaviour
{
    public Dialogue dialogue;
    [Tooltip("Check this if talking to this NPC should advance the tutorial!")]
    public bool advancesTutorial = false;

    [Header("Speaker Animation")]
    [Tooltip("Animator belonging to the NPC speaking this dialogue. Auto-detected from this object when empty.")]
    [SerializeField] private Animator speakerAnimator;

    [Header("Post-Dialogue Events")]
    [Tooltip("Fires exactly when this specific dialogue box closes.")]
    public UnityEvent onDialogueFinished; // --- NEW: Allows us to chain the UI panel! ---

    private DialogueManager dialogueManager;

    private void Awake()
    {
        dialogueManager = FindObjectOfType<DialogueManager>();
        if (speakerAnimator == null) speakerAnimator = GetComponentInChildren<Animator>();
    }

    public void TriggerDialogue()
    {
        if (dialogueManager == null) return;

        // --- THE FIX: We now invoke your custom event right when the dialogue closes ---
        dialogueManager.StartDialogue(dialogue, () => 
        {
            if (advancesTutorial && TutorialManager.Instance != null)
            {
                TutorialManager.Instance.ShowNextStep();
            }
            
            onDialogueFinished?.Invoke(); 
        }, speakerAnimator, transform);
    }

}
