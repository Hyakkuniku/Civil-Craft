using System; // ADDED: We need this to use 'Action' callbacks
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class DialogueManager : MonoBehaviour
{
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI dialogueText;

    public Animator animator;

    private Queue<string> sentences;

    [SerializeField] private InputManager inputManager;
    
    // ADDED: This will store the code we want to run after the dialogue finishes
    private Action onDialogueEndCallback; 

    void Start()
    {
        sentences = new Queue<string>();
    }

    // ADDED: We added 'Action onEnd = null' so other scripts can pass in a delayed command
    public void StartDialogue (Dialogue dialogue, Action onEnd = null)
    {
        onDialogueEndCallback = onEnd; // Save the delayed command

        inputManager?.SetPlayerInputEnable(false);
        inputManager?.SetLookEnabled(false);

        animator.SetBool("isOpen", true);

        Debug.Log("Starting conversation with " + dialogue.name);

        nameText.text = dialogue.name;

        sentences.Clear();

        foreach (string sentence in dialogue.sentences)
        {
            sentences.Enqueue(sentence);
        }

        DisplayNextSentence();
    }

    public void DisplayNextSentence ()
    {
        if (sentences.Count == 0)
        {
            EndDialogue();
            return;
        }

        string sentence = sentences.Dequeue();
        dialogueText.text = sentence;
    }

    void EndDialogue()
    {
        inputManager?.SetPlayerInputEnable(true);
        inputManager?.SetLookEnabled(true);
        animator.SetBool("isOpen", false);
        Debug.Log("End of conversation");

        // ADDED: Run the delayed command (like showing the Objective UI) now that we are done!
        onDialogueEndCallback?.Invoke();
        
        // Clear it out so it doesn't accidentally run again next time
        onDialogueEndCallback = null; 
    }
}