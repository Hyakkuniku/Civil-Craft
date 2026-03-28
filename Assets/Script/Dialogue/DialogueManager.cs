using System; 
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
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

    private bool isTyping = false;
    private Coroutine typingCoroutine;
    
    // --- OPTIMIZATION: Cache the wait timer so we don't generate garbage! ---
    private WaitForSeconds cachedTypingWait;

    void Awake()
    {
        playerInteract = FindObjectOfType<PlayerInteract>();
        playerUI = FindObjectOfType<PlayerUI>();
    }

    void Start()
    {
        sentences = new Queue<string>();
        // Create the memory object ONCE here!
        cachedTypingWait = new WaitForSeconds(typingSpeed); 
    }

    public void StartDialogue (Dialogue dialogue, Action onEnd = null)
    {
        onDialogueEndCallback = onEnd; 
        inputManager?.SetPlayerInputEnable(false);
        inputManager?.SetLookEnabled(false);

        if (playerInteract != null) playerInteract.enabled = false;
        if (playerUI != null) playerUI.UpdateButtons(new List<Interactable>());

        animator.SetBool("isOpen", true);
        nameText.text = dialogue.name;
        sentences.Clear();

        foreach (string sentence in dialogue.sentences)
        {
            sentences.Enqueue(sentence);
        }

        isTyping = false;
        DisplayNextSentence();
    }

    public void DisplayNextSentence ()
    {
        if (isTyping)
        {
            if (typingCoroutine != null) StopCoroutine(typingCoroutine);
            dialogueText.maxVisibleCharacters = 99999; 
            isTyping = false;
            return;
        }

        if (sentences.Count == 0)
        {
            EndDialogue();
            return;
        }

        string sentence = sentences.Dequeue();
        
        if (typingCoroutine != null) StopCoroutine(typingCoroutine);
        typingCoroutine = StartCoroutine(TypeSentence(sentence));
    }

    private IEnumerator TypeSentence(string sentence)
    {
        isTyping = true;
        dialogueText.text = sentence;
        dialogueText.maxVisibleCharacters = 0;
        dialogueText.ForceMeshUpdate();
        int totalVisibleCharacters = dialogueText.textInfo.characterCount;

        for (int i = 0; i <= totalVisibleCharacters; i++)
        {
            dialogueText.maxVisibleCharacters = i;
            // Use the cached wait instead of 'new'!
            yield return cachedTypingWait; 
        }

        isTyping = false;
    }

    void EndDialogue()
    {
        inputManager?.SetPlayerInputEnable(true);
        inputManager?.SetLookEnabled(true);
        if (playerInteract != null) playerInteract.enabled = true;

        animator.SetBool("isOpen", false);
        onDialogueEndCallback?.Invoke();
        onDialogueEndCallback = null; 
    }
}