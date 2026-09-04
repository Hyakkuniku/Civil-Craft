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

    [Header("Dialogue Window")]
    [Tooltip("The complete DialogueBox root. It is kept inactive until dialogue starts.")]
    [SerializeField] private GameObject dialogueBox;
    [Tooltip("Time allowed for the closing animation before the box is fully disabled.")]
    [Min(0f)] [SerializeField] private float closeHideDelay = 0.5f;

    [Header("Typewriter Settings")]
    public float typingSpeed = 0.03f; 

    [Header("UI Management")]
    public List<GameObject> elementsToHide = new List<GameObject>();

    private Queue<string> sentences;
    [SerializeField] private InputManager inputManager;
    private PlayerInteract playerInteract;
    private PlayerUI playerUI;
    private Action onDialogueEndCallback; 

    private bool isTyping = false;
    private Coroutine typingCoroutine;
    private Coroutine hideDialogueCoroutine;
    
    private WaitForSeconds cachedTypingWait;

    void Awake()
    {
        playerInteract = FindObjectOfType<PlayerInteract>();
        playerUI = FindObjectOfType<PlayerUI>();
        ResolveDialogueBox();

        // The panel used to remain active below the screen. Tall/wide aspect
        // ratios could expose its top edge, so keep it completely inactive.
        if (dialogueBox != null)
            dialogueBox.SetActive(false);
    }

    private void OnValidate()
    {
        ResolveDialogueBox();
        if (!Application.isPlaying && dialogueBox != null && dialogueBox != gameObject)
            dialogueBox.SetActive(false);
    }

    void Start()
    {
        sentences = new Queue<string>();
        cachedTypingWait = new WaitForSeconds(typingSpeed); 
    }

    public void StartDialogue (Dialogue dialogue, Action onEnd = null)
    {
        if (dialogue == null) return;

        ResolveDialogueBox();
        if (hideDialogueCoroutine != null)
        {
            StopCoroutine(hideDialogueCoroutine);
            hideDialogueCoroutine = null;
        }

        if (dialogueBox != null)
            dialogueBox.SetActive(true);

        onDialogueEndCallback = onEnd; 
        inputManager?.SetPlayerInputEnable(false);
        inputManager?.SetLookEnabled(false);

        if (playerInteract != null) playerInteract.enabled = false;
        if (playerUI != null) playerUI.UpdateButtons(new List<Interactable>());

        foreach (GameObject obj in elementsToHide)
        {
            if (obj != null) obj.SetActive(false);
        }

        if (animator != null)
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

        // --- THE MAGIC FIX: Inject the player's name into the text! ---
        if (PlayerDataManager.Instance != null)
        {
            sentence = sentence.Replace("{PlayerName}", PlayerDataManager.Instance.CurrentData.playerName);
        }
        
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
            yield return cachedTypingWait; 
        }

        isTyping = false;
    }

    void EndDialogue()
    {
        inputManager?.SetPlayerInputEnable(true);
        inputManager?.SetLookEnabled(true);
        if (playerInteract != null) playerInteract.enabled = true;

        if (animator != null)
            animator.SetBool("isOpen", false);

        if (hideDialogueCoroutine != null)
            StopCoroutine(hideDialogueCoroutine);
        hideDialogueCoroutine = StartCoroutine(HideDialogueBoxAfterClose());

        foreach (GameObject obj in elementsToHide)
        {
            if (obj != null) obj.SetActive(true);
        }

        // Clear the completed callback before invoking it. A callback is allowed
        // to start the next dialogue immediately; clearing afterward would erase
        // that new dialogue's completion callback and break ordered chains.
        Action completedCallback = onDialogueEndCallback;
        onDialogueEndCallback = null;
        completedCallback?.Invoke();
    }

    private IEnumerator HideDialogueBoxAfterClose()
    {
        if (closeHideDelay > 0f)
            yield return new WaitForSecondsRealtime(closeHideDelay);

        if (dialogueBox != null)
            dialogueBox.SetActive(false);
        hideDialogueCoroutine = null;
    }

    private void ResolveDialogueBox()
    {
        if (dialogueBox == null && animator != null)
            dialogueBox = animator.gameObject;
    }
}
