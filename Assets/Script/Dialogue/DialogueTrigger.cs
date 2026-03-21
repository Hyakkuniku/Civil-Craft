using UnityEngine;

public class DialogueTrigger : MonoBehaviour
{
    public Dialogue dialogue;
    [Tooltip("Check this if talking to this NPC should advance the tutorial!")]
    public bool advancesTutorial = false;

    public void TriggerDialogue()
    {
        if (advancesTutorial && TutorialManager.Instance != null)
        {
            // Start dialogue, and tell it to advance the tutorial when it finishes!
            FindObjectOfType<DialogueManager>().StartDialogue(dialogue, TutorialManager.Instance.ShowNextStep);
        }
        else
        {
            // Normal dialogue
            FindObjectOfType<DialogueManager>().StartDialogue(dialogue);
        }
    }
}