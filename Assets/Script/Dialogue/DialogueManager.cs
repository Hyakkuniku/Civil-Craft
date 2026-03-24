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

    [Header("Typewriter Settings")]
    [Tooltip("Time delay between each letter appearing.")]
    public float typingSpeed = 0.03f; 

    private Queue<string> sentences;

    [SerializeField] private InputManager inputManager;
    
    private PlayerInteract playerInteract;
    private PlayerUI playerUI;
    
    private Action onDialogueEndCallback; 

    // --- NEW: State tracking for the typewriter effect ---
    private bool isTyping = false;
    private Coroutine typingCoroutine;

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

        // Turn off the interaction scanner so new buttons don't spawn
        if (playerInteract != null) playerInteract.enabled = false;
        
        // Instantly clear any buttons that are currently on the screen
        if (playerUI != null) playerUI.UpdateButtons(new List<Interactable>());

        animator.SetBool("isOpen", true);

        Debug.Log("Starting conversation with " + dialogue.name);

        nameText.text = dialogue.name;

        sentences.Clear();

        foreach (string sentence in dialogue.sentences)
        {
            sentences.Enqueue(sentence);
        }

        // Reset the typing state to be safe when starting a new conversation
        isTyping = false;
        DisplayNextSentence();
    }

    public void DisplayNextSentence ()
    {
        // --- SKIP LOGIC ---
        // If the text is currently typing out, stop the animation and show the whole text instantly.
        if (isTyping)
        {
            if (typingCoroutine != null) StopCoroutine(typingCoroutine);
            
            dialogueText.maxVisibleCharacters = 99999; // Instantly reveal all characters
            isTyping = false;
            return;
        }

        // --- NORMAL NEXT SENTENCE LOGIC ---
        if (sentences.Count == 0)
        {
            EndDialogue();
            return;
        }

        string sentence = sentences.Dequeue();
        
        // Stop any previous coroutine just in case, and start typing the new sentence
        if (typingCoroutine != null) StopCoroutine(typingCoroutine);
        typingCoroutine = StartCoroutine(TypeSentence(sentence));
    }

    private IEnumerator TypeSentence(string sentence)
    {
        isTyping = true;
        
        // Set the full text into the UI, but hide it completely by setting visible characters to 0
        dialogueText.text = sentence;
        dialogueText.maxVisibleCharacters = 0;
        
        // Force TMPro to update so we can get the correct character count (ignoring rich text tags)
        dialogueText.ForceMeshUpdate();
        int totalVisibleCharacters = dialogueText.textInfo.characterCount;

        for (int i = 0; i <= totalVisibleCharacters; i++)
        {
            dialogueText.maxVisibleCharacters = i;
            yield return new WaitForSeconds(typingSpeed);
        }

        isTyping = false;
    }

    void EndDialogue()
    {
        // Restore movement and camera look
        inputManager?.SetPlayerInputEnable(true);
        inputManager?.SetLookEnabled(true);
        
        // Turn the interaction scanner back on so we can interact again
        if (playerInteract != null) playerInteract.enabled = true;

        animator.SetBool("isOpen", false);
        Debug.Log("End of conversation");

        onDialogueEndCallback?.Invoke();
        onDialogueEndCallback = null; 
    }
}