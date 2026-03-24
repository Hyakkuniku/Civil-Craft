using UnityEngine;

public class DialogueTrigger : MonoBehaviour
{
    public Dialogue dialogue;
    [Tooltip("Check this if talking to this NPC should advance the tutorial!")]
    public bool advancesTutorial = false;

    public void TriggerDialogue()
    {
        FacePlayer(); // <-- Instantly snaps to look at you

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

    // --- Instant Look Logic ---
    private void FacePlayer()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            Vector3 targetPosition = player.transform.position;
            // Keep the Y-coordinate exactly the same so the NPC stays upright
            targetPosition.y = transform.position.y;
            transform.LookAt(targetPosition);
        }
    }
}