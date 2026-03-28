using UnityEngine;

public class DialogueTrigger : MonoBehaviour
{
    public Dialogue dialogue;
    [Tooltip("Check this if talking to this NPC should advance the tutorial!")]
    public bool advancesTutorial = false;

    // --- OPTIMIZATION: Cache references! ---
    private Transform playerTransform;
    private DialogueManager dialogueManager;

    private void Awake()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null) playerTransform = player.transform;
        
        dialogueManager = FindObjectOfType<DialogueManager>();
    }

    public void TriggerDialogue()
    {
        FacePlayer(); 

        if (dialogueManager == null) return;

        if (advancesTutorial && TutorialManager.Instance != null)
        {
            dialogueManager.StartDialogue(dialogue, TutorialManager.Instance.ShowNextStep);
        }
        else
        {
            dialogueManager.StartDialogue(dialogue);
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
}