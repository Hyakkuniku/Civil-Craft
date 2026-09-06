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
    [Tooltip("Keeps the dialogue readable without covering most of a landscape mobile screen.")]
    [Range(0.65f, 1f)] [SerializeField] private float landscapeMobileScale = 0.84f;

    [Header("Typewriter Settings")]
    public float typingSpeed = 0.03f; 

    [Header("NPC Animation")]
    [Tooltip("Bool parameter used by the speaking NPC's Animator while this dialogue is open.")]
    [SerializeField] private string speakerTalkingBoolParameter = "isTalking";
    [Tooltip("Walking is stopped before the talking animation begins when this bool exists on the NPC Animator.")]
    [SerializeField] private string speakerWalkingBoolParameter = "isWalking";

    [Header("UI Management")]
    public List<GameObject> elementsToHide = new List<GameObject>();

    private Queue<string> sentences;
    [SerializeField] private InputManager inputManager;
    private PlayerInteract playerInteract;
    private PlayerUI playerUI;
    private Action onDialogueEndCallback; 
    private Animator activeSpeakerAnimator;

    private bool isTyping = false;
    private Coroutine typingCoroutine;
    private Coroutine hideDialogueCoroutine;
    
    private WaitForSeconds cachedTypingWait;

    void Awake()
    {
        playerInteract = FindObjectOfType<PlayerInteract>();
        playerUI = FindObjectOfType<PlayerUI>();
        ResolveDialogueBox();
        ApplyLandscapeMobileLayout();

        // The panel used to remain active below the screen. Tall/wide aspect
        // ratios could expose its top edge, so keep it completely inactive.
        if (dialogueBox != null)
            dialogueBox.SetActive(false);
    }

    private void OnValidate()
    {
        ResolveDialogueBox();
        ApplyLandscapeMobileLayout();
        if (!Application.isPlaying && dialogueBox != null && dialogueBox != gameObject)
            dialogueBox.SetActive(false);
    }

    /// <summary>
    /// Applies the same compact dialogue presentation in every gameplay scene.
    /// Scaling the complete root preserves all authored text/image spacing and the
    /// existing open/close animation, which only animates anchored Y position.
    /// </summary>
    public void ApplyLandscapeMobileLayout()
    {
        if (dialogueBox == null) return;

        RectTransform dialogueRect = dialogueBox.transform as RectTransform;
        if (dialogueRect == null) return;

        float scale = Mathf.Clamp(landscapeMobileScale, 0.65f, 1f);
        dialogueRect.localScale = new Vector3(scale, scale, 1f);

        Vector2 position = dialogueRect.anchoredPosition;
        position.x = 0f;
        dialogueRect.anchoredPosition = position;
    }

    void Start()
    {
        sentences = new Queue<string>();
        cachedTypingWait = new WaitForSeconds(typingSpeed); 
    }

    public void StartDialogue(Dialogue dialogue, Action onEnd = null, Animator speakerAnimator = null)
    {
        if (dialogue == null) return;

        SetActiveSpeaker(speakerAnimator);

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
        StopActiveSpeakerTalking();

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

    private void OnDisable()
    {
        StopActiveSpeakerTalking();
    }

    private void SetActiveSpeaker(Animator speakerAnimator)
    {
        StopActiveSpeakerTalking();
        activeSpeakerAnimator = speakerAnimator;

        if (activeSpeakerAnimator == null) return;

        if (HasBoolParameter(activeSpeakerAnimator, speakerWalkingBoolParameter))
            activeSpeakerAnimator.SetBool(speakerWalkingBoolParameter, false);

        if (HasBoolParameter(activeSpeakerAnimator, speakerTalkingBoolParameter))
            activeSpeakerAnimator.SetBool(speakerTalkingBoolParameter, true);
    }

    private void StopActiveSpeakerTalking()
    {
        if (activeSpeakerAnimator != null &&
            HasBoolParameter(activeSpeakerAnimator, speakerTalkingBoolParameter))
        {
            activeSpeakerAnimator.SetBool(speakerTalkingBoolParameter, false);
        }

        activeSpeakerAnimator = null;
    }

    private static bool HasBoolParameter(Animator targetAnimator, string parameterName)
    {
        if (targetAnimator == null || string.IsNullOrWhiteSpace(parameterName)) return false;

        AnimatorControllerParameter[] parameters = targetAnimator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].type == AnimatorControllerParameterType.Bool &&
                parameters[i].name == parameterName)
            {
                return true;
            }
        }

        return false;
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
