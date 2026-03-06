using System; 
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
    
    // NEW: We need references to your interact scripts
    private PlayerInteract playerInteract;
    private PlayerUI playerUI;
    
    private Action onDialogueEndCallback; 

    void Awake()
    {
        // Automatically find these scripts in the scene when the game starts
        playerInteract = FindObjectOfType<PlayerInteract>();
        playerUI = FindObjectOfType<PlayerUI>();
    }

    void Start()
    {
        sentences = new Queue<string>();
    }

    public void StartDialogue (Dialogue dialogue, Action onEnd = null)
    {
        onDialogueEndCallback = onEnd; 

        // Disable movement and camera look
        inputManager?.SetPlayerInputEnable(false);
        inputManager?.SetLookEnabled(false);

        // NEW: Turn off the interaction scanner so new buttons don't spawn
        if (playerInteract != null) playerInteract.enabled = false;
        
        // NEW: Instantly clear any buttons that are currently on the screen
        if (playerUI != null) playerUI.UpdateButtons(new List<Interactable>());

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
        // Restore movement and camera look
        inputManager?.SetPlayerInputEnable(true);
        inputManager?.SetLookEnabled(true);
        
        // NEW: Turn the interaction scanner back on so we can interact again
        if (playerInteract != null) playerInteract.enabled = true;

        animator.SetBool("isOpen", false);
        Debug.Log("End of conversation");

        onDialogueEndCallback?.Invoke();
        
        onDialogueEndCallback = null; 
    }
}