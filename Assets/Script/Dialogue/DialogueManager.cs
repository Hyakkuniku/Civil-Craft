using System; 
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class DialogueManager : MonoBehaviour
{
    private static Sprite skipRoundedSprite;

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

    [Header("Dialogue Skip Button")]
    [Tooltip("Optional authored button. When empty, a mobile-friendly SKIP button is created on the dialogue Canvas.")]
    [SerializeField] private Button skipDialogueButton;
    [SerializeField] private Vector2 skipButtonSize = new Vector2(190f, 76f);
    [Tooltip("Distance from the device safe area's top-right corner, in Canvas units.")]
    [SerializeField] private Vector2 skipButtonMargin = new Vector2(36f, 28f);
    [Tooltip("How long the player must continuously hold SKIP before the dialogue closes.")]
    [Min(0.25f)] [SerializeField] private float holdToSkipDuration = 0.9f;

    [Header("NPC Animation")]
    [Tooltip("Bool parameter used by the speaking NPC's Animator while this dialogue is open.")]
    [SerializeField] private string speakerTalkingBoolParameter = "isTalking";
    [Tooltip("Walking is stopped before the talking animation begins when this bool exists on the NPC Animator.")]
    [SerializeField] private string speakerWalkingBoolParameter = "isWalking";
    [Tooltip("Running is stopped before the talking animation begins when this bool exists on the NPC Animator.")]
    [SerializeField] private string speakerRunningBoolParameter = "isRunning";

    [Header("Conversation Facing")]
    [Tooltip("Turn the player horizontally toward the character speaking when dialogue begins.")]
    [SerializeField] private bool turnPlayerTowardSpeaker = true;
    [Tooltip("Turn the speaking NPC horizontally toward the player when dialogue begins.")]
    [SerializeField] private bool turnSpeakerTowardPlayer = true;
    [Tooltip("Seconds used for both characters' automatic turn. Set to 0 for an immediate snap.")]
    [Min(0f)] [SerializeField] private float playerTurnDuration = 0.2f;

    [Header("UI Management")]
    public List<GameObject> elementsToHide = new List<GameObject>();

    private Queue<string> sentences;
    [SerializeField] private InputManager inputManager;
    private PlayerInteract playerInteract;
    private PlayerUI playerUI;
    private Action onDialogueEndCallback; 
    private Animator activeSpeakerAnimator;
    private Transform playerTransform;
    private Coroutine playerFacingCoroutine;
    private Coroutine speakerFacingCoroutine;

    private bool isTyping = false;
    private Coroutine typingCoroutine;
    private Coroutine hideDialogueCoroutine;
    private RectTransform skipButtonRect;
    private Canvas skipButtonCanvas;
    private bool isDialogueActive;
    private Rect lastSafeArea;
    private Vector2Int lastScreenSize;
    private readonly Dictionary<GameObject, bool> elementVisibilityBeforeDialogue =
        new Dictionary<GameObject, bool>();
    
    private WaitForSeconds cachedTypingWait;

    void Awake()
    {
        playerInteract = FindObjectOfType<PlayerInteract>();
        playerUI = FindObjectOfType<PlayerUI>();
        ResolveDialogueBox();
        ApplyLandscapeMobileLayout();
        EnsureSkipDialogueButton();

        // The panel used to remain active below the screen. Tall/wide aspect
        // ratios could expose its top edge, so keep it completely inactive.
        if (dialogueBox != null)
            dialogueBox.SetActive(false);
        SetSkipButtonVisible(false);
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
        ApplyReadableDialogueStyle();
        if (dialogueBox == null) return;

        RectTransform dialogueRect = dialogueBox.transform as RectTransform;
        if (dialogueRect == null) return;

        float scale = Mathf.Clamp(landscapeMobileScale, 0.65f, 1f);
        dialogueRect.localScale = new Vector3(scale, scale, 1f);

        Vector2 position = dialogueRect.anchoredPosition;
        position.x = 0f;
        dialogueRect.anchoredPosition = position;
    }

    /// <summary>
    /// Adapts the proven Left Tutorial text treatment to the much larger
    /// parchment dialogue panel. Dialogue boxes use authored negative TMP
    /// margins to define their writing area, so those margins stay untouched.
    /// </summary>
    private void ApplyReadableDialogueStyle()
    {
        if (dialogueText == null) return;

        dialogueText.fontSize = 48f;
        dialogueText.enableAutoSizing = true;
        dialogueText.fontSizeMin = 34f;
        dialogueText.fontSizeMax = 48f;
        dialogueText.fontStyle = FontStyles.Bold;
        dialogueText.alignment = TextAlignmentOptions.MidlineLeft;
        dialogueText.enableWordWrapping = true;
        dialogueText.overflowMode = TextOverflowModes.Ellipsis;
        dialogueText.characterSpacing = 0.25f;
        dialogueText.lineSpacing = 6f;
        dialogueText.paragraphSpacing = 4f;
    }

    void Start()
    {
        sentences = new Queue<string>();
        cachedTypingWait = new WaitForSeconds(typingSpeed); 
    }

    private void LateUpdate()
    {
        if (skipDialogueButton == null || !skipDialogueButton.gameObject.activeInHierarchy)
            return;

        Rect safeArea = Screen.safeArea;
        Vector2Int screenSize = new Vector2Int(Screen.width, Screen.height);
        if (safeArea != lastSafeArea || screenSize != lastScreenSize)
            PositionSkipButtonForSafeArea();
    }

    public void StartDialogue(
        Dialogue dialogue,
        Action onEnd = null,
        Animator speakerAnimator = null,
        Transform speakerTransform = null)
    {
        if (dialogue == null) return;
        bool wasAlreadyActive = isDialogueActive;

        SetActiveSpeaker(speakerAnimator);
        TurnParticipantsTowardEachOther(
            ResolveSpeakerTransform(speakerAnimator, speakerTransform));

        ResolveDialogueBox();
        ApplyReadableDialogueStyle();
        EnsureSkipDialogueButton();
        if (hideDialogueCoroutine != null)
        {
            StopCoroutine(hideDialogueCoroutine);
            hideDialogueCoroutine = null;
        }

        if (dialogueBox != null)
            dialogueBox.SetActive(true);

        isDialogueActive = true;
        SetSkipButtonVisible(true);

        onDialogueEndCallback = onEnd; 
        inputManager?.SetPlayerInputEnable(false);
        inputManager?.SetLookEnabled(false);

        if (playerInteract != null) playerInteract.enabled = false;
        if (playerUI != null) playerUI.UpdateButtons(new List<Interactable>());

        HideDialogueElements(!wasAlreadyActive);

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
        if (!isDialogueActive) return;
        isDialogueActive = false;
        SetSkipButtonVisible(false);
        StopActiveSpeakerTalking();

        inputManager?.SetPlayerInputEnable(true);
        inputManager?.SetLookEnabled(true);
        if (playerInteract != null) playerInteract.enabled = true;

        if (animator != null)
            animator.SetBool("isOpen", false);

        if (hideDialogueCoroutine != null)
            StopCoroutine(hideDialogueCoroutine);
        hideDialogueCoroutine = StartCoroutine(HideDialogueBoxAfterClose());

        RestoreDialogueElements();

        // Clear the completed callback before invoking it. A callback is allowed
        // to start the next dialogue immediately; clearing afterward would erase
        // that new dialogue's completion callback and break ordered chains.
        Action completedCallback = onDialogueEndCallback;
        onDialogueEndCallback = null;
        completedCallback?.Invoke();
    }

    /// <summary>Ends the complete current dialogue and invokes its completion flow once.</summary>
    public void SkipDialogue()
    {
        if (!isDialogueActive) return;

        if (typingCoroutine != null)
        {
            StopCoroutine(typingCoroutine);
            typingCoroutine = null;
        }

        isTyping = false;
        sentences?.Clear();
        if (dialogueText != null)
            dialogueText.maxVisibleCharacters = 99999;

        EndDialogue();
    }

    private void OnDisable()
    {
        isDialogueActive = false;
        SetSkipButtonVisible(false);
        RestoreDialogueElements();
        if (playerFacingCoroutine != null)
        {
            StopCoroutine(playerFacingCoroutine);
            playerFacingCoroutine = null;
        }

        if (speakerFacingCoroutine != null)
        {
            StopCoroutine(speakerFacingCoroutine);
            speakerFacingCoroutine = null;
        }

        StopActiveSpeakerTalking();
    }

    private void HideDialogueElements(bool captureCurrentState)
    {
        if (captureCurrentState)
            elementVisibilityBeforeDialogue.Clear();

        foreach (GameObject obj in elementsToHide)
        {
            if (obj == null) continue;
            if (!elementVisibilityBeforeDialogue.ContainsKey(obj))
                elementVisibilityBeforeDialogue.Add(obj, obj.activeSelf);
            obj.SetActive(false);
        }
    }

    private void RestoreDialogueElements()
    {
        foreach (KeyValuePair<GameObject, bool> state in elementVisibilityBeforeDialogue)
            if (state.Key != null) state.Key.SetActive(state.Value);
        elementVisibilityBeforeDialogue.Clear();
    }

    private void TurnParticipantsTowardEachOther(Transform speakerTransform)
    {
        if (speakerTransform == null) return;

        ResolvePlayerTransform();
        if (playerTransform == null || playerTransform == speakerTransform) return;

        Vector3 playerDirection = speakerTransform.position - playerTransform.position;
        playerDirection.y = 0f;
        if (playerDirection.sqrMagnitude < 0.0001f) return;

        if (turnPlayerTowardSpeaker)
        {
            if (playerFacingCoroutine != null)
                StopCoroutine(playerFacingCoroutine);
            playerFacingCoroutine = StartCoroutine(RotateParticipantToward(
                playerTransform,
                Quaternion.LookRotation(playerDirection.normalized),
                true));
        }

        if (turnSpeakerTowardPlayer)
        {
            Vector3 speakerDirection = -playerDirection;
            if (speakerFacingCoroutine != null)
                StopCoroutine(speakerFacingCoroutine);
            speakerFacingCoroutine = StartCoroutine(RotateParticipantToward(
                speakerTransform,
                Quaternion.LookRotation(speakerDirection.normalized),
                false));
        }
    }

    private IEnumerator RotateParticipantToward(
        Transform participant,
        Quaternion targetRotation,
        bool isPlayer)
    {
        if (participant == null) yield break;

        Quaternion startRotation = participant.rotation;
        float duration = Mathf.Max(0f, playerTurnDuration);

        if (duration <= 0f)
        {
            participant.rotation = targetRotation;
            ClearFacingCoroutine(isPlayer);
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration && participant != null)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            float easedProgress = progress * progress * (3f - (2f * progress));
            participant.rotation = Quaternion.Slerp(
                startRotation,
                targetRotation,
                easedProgress);
            yield return null;
        }

        if (participant != null)
            participant.rotation = targetRotation;
        ClearFacingCoroutine(isPlayer);
    }

    private void ClearFacingCoroutine(bool isPlayer)
    {
        if (isPlayer) playerFacingCoroutine = null;
        else speakerFacingCoroutine = null;
    }

    private void ResolvePlayerTransform()
    {
        if (playerTransform != null) return;

        if (playerInteract == null)
            playerInteract = FindObjectOfType<PlayerInteract>();
        if (playerInteract != null)
        {
            playerTransform = playerInteract.transform;
            return;
        }

        if (inputManager == null)
            inputManager = FindObjectOfType<InputManager>();
        if (inputManager != null)
            playerTransform = inputManager.transform;
    }

    private static Transform ResolveSpeakerTransform(
        Animator speakerAnimator,
        Transform explicitSpeakerTransform)
    {
        if (explicitSpeakerTransform != null) return explicitSpeakerTransform;
        if (speakerAnimator == null) return null;

        // Animators commonly live on a nested model object. Prefer the gameplay
        // interactable root so model offsets do not skew the facing direction.
        Interactable interactable = speakerAnimator.GetComponentInParent<Interactable>();
        if (interactable != null) return interactable.transform;

        NPCProgressionManager progression =
            speakerAnimator.GetComponentInParent<NPCProgressionManager>();
        return progression != null ? progression.transform : speakerAnimator.transform;
    }

    private void SetActiveSpeaker(Animator speakerAnimator)
    {
        StopActiveSpeakerTalking();
        activeSpeakerAnimator = speakerAnimator;

        if (activeSpeakerAnimator == null) return;

        if (HasBoolParameter(activeSpeakerAnimator, speakerWalkingBoolParameter))
            activeSpeakerAnimator.SetBool(speakerWalkingBoolParameter, false);
        if (HasBoolParameter(activeSpeakerAnimator, speakerRunningBoolParameter))
            activeSpeakerAnimator.SetBool(speakerRunningBoolParameter, false);

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

    private void EnsureSkipDialogueButton()
    {
        ResolveDialogueBox();
        if (skipDialogueButton == null)
        {
            Canvas dialogueCanvas = dialogueBox != null
                ? dialogueBox.GetComponentInParent<Canvas>()
                : null;
            skipButtonCanvas = dialogueCanvas != null ? dialogueCanvas.rootCanvas : null;
            if (skipButtonCanvas == null) return;

            GameObject buttonObject = new GameObject(
                "Dialogue Skip Button",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button),
                typeof(Shadow));
            buttonObject.layer = 5;
            buttonObject.transform.SetParent(skipButtonCanvas.transform, false);

            skipButtonRect = buttonObject.GetComponent<RectTransform>();
            skipButtonRect.anchorMin = Vector2.one;
            skipButtonRect.anchorMax = Vector2.one;
            skipButtonRect.pivot = Vector2.one;
            skipButtonRect.sizeDelta = skipButtonSize;

            Image background = buttonObject.GetComponent<Image>();
            background.color = new Color32(145, 84, 48, 255);
            background.sprite = GetSkipRoundedSprite();
            background.type = Image.Type.Sliced;

            Shadow shadow = buttonObject.GetComponent<Shadow>();
            shadow.effectColor = new Color32(103, 58, 35, 180);
            shadow.effectDistance = new Vector2(0f, -5f);

            skipDialogueButton = buttonObject.GetComponent<Button>();
            skipDialogueButton.targetGraphic = background;
            ColorBlock colors = skipDialogueButton.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color32(255, 245, 220, 255);
            colors.pressedColor = new Color32(229, 202, 151, 255);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.45f);
            colors.fadeDuration = 0.08f;
            skipDialogueButton.colors = colors;

            Image face = CreateSkipButtonLayer(
                buttonObject.transform,
                "Cream Face",
                new Vector2(5f, 8f),
                new Vector2(-5f, -5f),
                new Color32(239, 216, 167, 255));
            face.raycastTarget = false;

            Image holdFill = CreateSkipButtonLayer(
                buttonObject.transform,
                "Hold Fill",
                new Vector2(5f, 8f),
                new Vector2(-5f, -5f),
                new Color32(207, 145, 74, 255));
            holdFill.type = Image.Type.Filled;
            holdFill.fillMethod = Image.FillMethod.Horizontal;
            holdFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            holdFill.fillAmount = 0f;
            holdFill.enabled = false;
            holdFill.raycastTarget = false;

            GameObject labelObject = new GameObject(
                "Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            labelObject.layer = 5;
            labelObject.transform.SetParent(buttonObject.transform, false);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
            label.text = "SKIP";
            label.font = nameText != null && nameText.font != null
                ? nameText.font
                : dialogueText != null ? dialogueText.font : null;
            label.fontSize = 32f;
            label.enableAutoSizing = true;
            label.fontSizeMin = 22f;
            label.fontSizeMax = 32f;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color32(132, 74, 43, 255);
            label.raycastTarget = false;

            DialogueHoldToSkip hold = buttonObject.AddComponent<DialogueHoldToSkip>();
            hold.Configure(this, skipDialogueButton, holdFill, holdToSkipDuration);
        }

        if (skipButtonCanvas == null)
        {
            Canvas canvas = skipDialogueButton.GetComponentInParent<Canvas>();
            skipButtonCanvas = canvas != null ? canvas.rootCanvas : null;
        }
        if (skipButtonRect == null)
            skipButtonRect = skipDialogueButton.transform as RectTransform;

        DialogueHoldToSkip holdController = skipDialogueButton.GetComponent<DialogueHoldToSkip>();
        if (holdController == null)
        {
            Image holdFill = CreateSkipButtonLayer(
                skipDialogueButton.transform,
                "Hold Fill",
                new Vector2(5f, 8f),
                new Vector2(-5f, -5f),
                new Color32(207, 145, 74, 255));
            holdFill.type = Image.Type.Filled;
            holdFill.fillMethod = Image.FillMethod.Horizontal;
            holdFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            holdFill.fillAmount = 0f;
            holdFill.enabled = false;
            holdFill.raycastTarget = false;
            holdFill.transform.SetAsFirstSibling();

            holdController = skipDialogueButton.gameObject.AddComponent<DialogueHoldToSkip>();
            holdController.Configure(this, skipDialogueButton, holdFill, holdToSkipDuration);
        }

        PositionSkipButtonForSafeArea();
        skipDialogueButton.gameObject.SetActive(false);
    }

    private void SetSkipButtonVisible(bool visible)
    {
        if (skipDialogueButton == null && visible)
            EnsureSkipDialogueButton();
        if (skipDialogueButton == null) return;

        skipDialogueButton.interactable = visible;
        skipDialogueButton.gameObject.SetActive(visible);
        if (visible)
        {
            skipDialogueButton.transform.SetAsLastSibling();
            PositionSkipButtonForSafeArea();
        }
    }

    private void PositionSkipButtonForSafeArea()
    {
        if (skipButtonRect == null || skipButtonCanvas == null) return;

        Rect safeArea = Screen.safeArea;
        float scaleFactor = Mathf.Max(0.01f, skipButtonCanvas.scaleFactor);
        float rightInset = Mathf.Max(0f, Screen.width - safeArea.xMax) / scaleFactor;
        float topInset = Mathf.Max(0f, Screen.height - safeArea.yMax) / scaleFactor;

        skipButtonRect.anchorMin = Vector2.one;
        skipButtonRect.anchorMax = Vector2.one;
        skipButtonRect.pivot = Vector2.one;
        skipButtonRect.sizeDelta = skipButtonSize;
        skipButtonRect.anchoredPosition = new Vector2(
            -(rightInset + skipButtonMargin.x),
            -(topInset + skipButtonMargin.y));

        lastSafeArea = safeArea;
        lastScreenSize = new Vector2Int(Screen.width, Screen.height);
    }

    internal static Image CreateSkipButtonLayer(
        Transform parent,
        string objectName,
        Vector2 offsetMin,
        Vector2 offsetMax,
        Color color)
    {
        GameObject layerObject = new GameObject(
            objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        layerObject.layer = 5;
        layerObject.transform.SetParent(parent, false);

        RectTransform rect = layerObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;

        Image image = layerObject.GetComponent<Image>();
        image.sprite = GetSkipRoundedSprite();
        image.type = Image.Type.Sliced;
        image.color = color;
        return image;
    }

    internal static Sprite GetSkipRoundedSprite()
    {
        if (skipRoundedSprite != null) return skipRoundedSprite;

        // Match the button's wide aspect ratio so a Filled Image preserves the
        // small corner radius instead of stretching it into a pill shape.
        const int width = 128;
        const int height = 52;
        const float radius = 8f;
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = "Dialogue Skip Rounded Rectangle",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };

        Color32[] pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float dx = Mathf.Max(Mathf.Abs(x + 0.5f - width * 0.5f) -
                                     (width * 0.5f - radius), 0f);
                float dy = Mathf.Max(Mathf.Abs(y + 0.5f - height * 0.5f) -
                                     (height * 0.5f - radius), 0f);
                byte alpha = (byte)Mathf.RoundToInt(
                    Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f) * 255f);
                pixels[y * width + x] = new Color32(255, 255, 255, alpha);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        skipRoundedSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, width, height),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect,
            new Vector4(9f, 9f, 9f, 9f));
        skipRoundedSprite.name = "Dialogue Skip Rounded Rectangle";
        skipRoundedSprite.hideFlags = HideFlags.HideAndDontSave;
        return skipRoundedSprite;
    }
}

/// <summary>Requires an uninterrupted mobile press and visualizes its progress.</summary>
public sealed class DialogueHoldToSkip : MonoBehaviour,
    IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    private DialogueManager dialogueManager;
    private Button button;
    private Image fillImage;
    private float holdDuration = 0.9f;
    private float heldTime;
    private bool holding;
    private bool completed;

    public void Configure(
        DialogueManager manager,
        Button targetButton,
        Image progressFill,
        float duration)
    {
        dialogueManager = manager;
        button = targetButton;
        fillImage = progressFill;
        holdDuration = Mathf.Max(0.25f, duration);
        ResetHold();
    }

    private void Update()
    {
        if (!holding || completed || button == null || !button.IsInteractable()) return;

        heldTime += Time.unscaledDeltaTime;
        float progress = Mathf.Clamp01(heldTime / holdDuration);
        if (fillImage != null)
        {
            fillImage.enabled = true;
            fillImage.fillAmount = progress;
        }
        if (progress < 1f) return;

        completed = true;
        holding = false;
        dialogueManager?.SkipDialogue();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left ||
            button == null || !button.IsInteractable()) return;

        heldTime = 0f;
        completed = false;
        holding = true;
        if (fillImage != null)
        {
            fillImage.fillAmount = 0f;
            fillImage.enabled = true;
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!completed) ResetHold();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!completed) ResetHold();
    }

    private void OnDisable()
    {
        ResetHold();
    }

    private void ResetHold()
    {
        holding = false;
        completed = false;
        heldTime = 0f;
        if (fillImage != null)
        {
            fillImage.fillAmount = 0f;
            fillImage.enabled = false;
        }
    }
}
