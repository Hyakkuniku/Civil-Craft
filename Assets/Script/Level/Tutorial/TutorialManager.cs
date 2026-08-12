using UnityEngine;
using UnityEngine.Events;
using TMPro;
using System.Collections; 

public enum TutorialPosition
{
    Center,
    Left
}

[System.Serializable]
public class TutorialStep
{
    [TextArea(3, 6)]
    public string message = "Step description here...";
    public TutorialPosition screenPosition = TutorialPosition.Center;
    
    public bool showNextButton = true;
    public bool canSkip = false;

    [Header("Pointer Settings")]
    public bool usePointer = false;
    public RectTransform pointerTarget;
    public Vector2 pointerOffset = new Vector2(0, 80);
    public float pointerRotation = 180f;

    public bool advanceOnClick = false;

    [Header("Events")]
    public UnityEvent OnStepStart = new UnityEvent();
}

public class TutorialManager : MonoBehaviour
{
    public static TutorialManager Instance { get; private set; }

    [Header("Center Tutorial UI")]
    [SerializeField] private GameObject centerPanel;
    [SerializeField] private TextMeshProUGUI centerText; 

    [Header("Left Tutorial UI")]
    [SerializeField] private GameObject leftPanel;
    [SerializeField] private TextMeshProUGUI leftText; 

    [Header("Global Tutorial UI")]
    [SerializeField] private GameObject nextButton;     
    [SerializeField] private GameObject skipButton;
    [SerializeField] private TutorialPointer bouncingArrow;

    [Header("Transition Settings")]
    [Tooltip("How fast the panel fades and pops when appearing in the same spot.")]
    public float transitionDuration = 0.25f;
    [Tooltip("How fast the panel slides when moving from Center to Left (or vice versa).")]
    public float slideTransitionDuration = 0.6f;

    [Header("Left Panel Attention Animation")]
    [Tooltip("How fast the left text breathes/pulses to grab attention.")]
    public float pulseSpeed = 4f;
    [Tooltip("How much the text grows during the pulse (0.05 = 5% larger).")]
    public float pulseAmount = 0.05f;

    public bool IsTutorialActive { get; private set; } = false;

    private TutorialSequence currentSequence;
    private int currentStepIndex = -1;
    
    private UnityEngine.UI.Button trackedButton = null;
    private UnityAction trackedButtonAction = null;
    
    private Coroutine currentAnimationCoroutine;
    private Coroutine leftTextIdleCoroutine; 
    
    private TutorialPosition? lastScreenPosition = null;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        trackedButtonAction = new UnityAction(OnTrackedButtonClicked);
    }

    private void Start()
    {
        if (!IsTutorialActive) 
        {
            if (centerPanel != null) centerPanel.SetActive(false);
            if (leftPanel != null) leftPanel.SetActive(false);
            if (bouncingArrow != null) bouncingArrow.Hide();
        }

        if (nextButton != null)
        {
            var btn = nextButton.GetComponent<UnityEngine.UI.Button>();
            if (btn != null) btn.onClick.AddListener(ShowNextStep);
        }

        if (skipButton != null)
        {
            var btn = skipButton.GetComponent<UnityEngine.UI.Button>();
            if (btn != null) btn.onClick.AddListener(SkipTutorial);
        }
    }

    private void Update()
    {
        if (BuildTutorialDirector.Instance != null && BuildTutorialDirector.Instance.isTracingStep)
        {
            return;
        }

        if (IsTutorialActive && currentSequence != null && currentStepIndex >= 0 && currentStepIndex < currentSequence.tutorialSteps.Length)
        {
            var step = currentSequence.tutorialSteps[currentStepIndex];
            
            if (step.usePointer && step.pointerTarget != null && bouncingArrow != null)
            {
                bouncingArrow.PointAt(step.pointerTarget, step.pointerOffset);
                bouncingArrow.transform.localEulerAngles = new Vector3(0, 0, step.pointerRotation);
            }
        }
    }

    public void PlayTutorial(TutorialSequence sequence)
    {
        if (sequence == null || sequence.tutorialSteps.Length == 0) return;

        currentSequence = sequence;
        currentStepIndex = -1;
        IsTutorialActive = true;
        lastScreenPosition = null; 

        if (sequence.tutorialWaypoints != null && sequence.tutorialWaypoints.Count > 0 && PathGuider.Instance != null)
        {
            PathGuider.Instance.SetNewWaypoints(sequence.tutorialWaypoints);
        }

        ShowNextStep();
    }

    public void ShowNextStep()
    {
        if (currentSequence == null || !IsTutorialActive) return;

        ClearTrackedButton();

        currentStepIndex++;

        if (currentStepIndex >= currentSequence.tutorialSteps.Length)
        {
            CompleteTutorial();
            return;
        }

        var step = currentSequence.tutorialSteps[currentStepIndex];
        ShowTutorialStep(step);
    }

    private void ShowTutorialStep(TutorialStep step)
    {
        // Stop any ongoing idle text pulse before we transition and reset the scale
        if (leftTextIdleCoroutine != null)
        {
            StopCoroutine(leftTextIdleCoroutine);
            leftTextIdleCoroutine = null;
            if (leftText != null) leftText.transform.localScale = Vector3.one;
        }

        GameObject activePanel = step.screenPosition == TutorialPosition.Center ? centerPanel : leftPanel;
        TextMeshProUGUI activeText = step.screenPosition == TutorialPosition.Center ? centerText : leftText;
        
        GameObject oldPanel = null;
        if (lastScreenPosition != null && lastScreenPosition != step.screenPosition)
        {
            oldPanel = lastScreenPosition == TutorialPosition.Center ? centerPanel : leftPanel;
        }

        if (centerPanel != null) centerPanel.SetActive(false);
        if (leftPanel != null) leftPanel.SetActive(false);

        if (activePanel != null)
        {
            activePanel.SetActive(true);
            if (activeText != null) activeText.text = step.message ?? "";
            
            float waitDelay = 0f;

            if (oldPanel != null)
            {
                AnimatePanelSlide(activePanel, oldPanel.transform.position);
                waitDelay = slideTransitionDuration;
            }
            else
            {
                AnimatePanelIn(activePanel);
                waitDelay = transitionDuration;
            }

            // --- THE FIX: Only animate the Text component, and wait for the proper duration! ---
            if (step.screenPosition == TutorialPosition.Left && activeText != null)
            {
                leftTextIdleCoroutine = StartCoroutine(IdleTextPulseRoutine(activeText, waitDelay));
            }
        }

        if (nextButton != null) nextButton.SetActive(step.showNextButton);
        if (skipButton != null) skipButton.SetActive(step.canSkip);

        if (!step.usePointer || step.pointerTarget == null)
        {
            if (bouncingArrow != null) bouncingArrow.Hide();
        }
        else
        {
            if (step.advanceOnClick)
            {
                UnityEngine.UI.Button targetBtn = step.pointerTarget.GetComponent<UnityEngine.UI.Button>();
                if (targetBtn != null)
                {
                    trackedButton = targetBtn;
                    trackedButton.onClick.AddListener(trackedButtonAction);
                    if (nextButton != null) nextButton.SetActive(false);
                }
            }
        }

        lastScreenPosition = step.screenPosition;
        step.OnStepStart?.Invoke();
    }

    // ==========================================
    // ANIMATION LOGIC
    // ==========================================

    private void AnimatePanelIn(GameObject panel)
    {
        if (currentAnimationCoroutine != null) StopCoroutine(currentAnimationCoroutine);
        currentAnimationCoroutine = StartCoroutine(FadeAndPopRoutine(panel));
    }

    private void AnimatePanelSlide(GameObject panel, Vector3 oldWorldPosition)
    {
        if (currentAnimationCoroutine != null) StopCoroutine(currentAnimationCoroutine);
        currentAnimationCoroutine = StartCoroutine(SlideRoutine(panel, oldWorldPosition));
    }

    private IEnumerator FadeAndPopRoutine(GameObject panel)
    {
        CanvasGroup group = panel.GetComponent<CanvasGroup>();
        if (group == null) group = panel.AddComponent<CanvasGroup>();

        float elapsed = 0f;
        group.alpha = 0f;
        panel.transform.localScale = new Vector3(0.9f, 0.9f, 1f);

        while (elapsed < transitionDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float smoothT = Mathf.SmoothStep(0f, 1f, elapsed / transitionDuration);

            group.alpha = smoothT;
            panel.transform.localScale = Vector3.Lerp(new Vector3(0.9f, 0.9f, 1f), Vector3.one, smoothT);
            yield return null;
        }

        group.alpha = 1f;
        panel.transform.localScale = Vector3.one;
    }

    private IEnumerator SlideRoutine(GameObject panel, Vector3 startWorldPos)
    {
        CanvasGroup group = panel.GetComponent<CanvasGroup>();
        if (group == null) group = panel.AddComponent<CanvasGroup>();

        RectTransform rt = panel.GetComponent<RectTransform>();
        Vector3 targetWorldPos = rt.position;
        
        float elapsed = 0f;
        group.alpha = 0f;
        panel.transform.localScale = new Vector3(0.9f, 0.9f, 1f);

        while (elapsed < slideTransitionDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float smoothT = Mathf.SmoothStep(0f, 1f, elapsed / slideTransitionDuration); 

            group.alpha = smoothT;
            rt.position = Vector3.Lerp(startWorldPos, targetWorldPos, smoothT);
            panel.transform.localScale = Vector3.Lerp(new Vector3(0.9f, 0.9f, 1f), Vector3.one, smoothT);

            yield return null;
        }

        group.alpha = 1f;
        rt.position = targetWorldPos;
        panel.transform.localScale = Vector3.one;
    }

    // --- THE FIX: Idle Pulse Coroutine specifically for the Text ---
    private IEnumerator IdleTextPulseRoutine(TextMeshProUGUI textToAnimate, float delay)
    {
        // Wait for whichever transition is happening to finish
        yield return new WaitForSecondsRealtime(delay);

        Transform textTransform = textToAnimate.transform;

        while (true)
        {
            // Calculate a soft pulsing scale for the text using a sine wave
            float pulseScale = 1f + (Mathf.Sin(Time.unscaledTime * pulseSpeed) * pulseAmount);
            textTransform.localScale = new Vector3(pulseScale, pulseScale, 1f);
            
            yield return null;
        }
    }

    // ==========================================

    private void OnTrackedButtonClicked()
    {
        ClearTrackedButton();
        ShowNextStep();
    }

    private void ClearTrackedButton()
    {
        if (trackedButton != null)
        {
            trackedButton.onClick.RemoveListener(trackedButtonAction);
            trackedButton = null;
        }
    }

    public void SetNextButtonActive(bool isActive)
    {
        if (nextButton != null) nextButton.SetActive(isActive);
    }

    private void CompleteTutorial()
    {
        ClearTrackedButton(); 
        
        IsTutorialActive = false;
        lastScreenPosition = null;

        if (currentAnimationCoroutine != null)
        {
            StopCoroutine(currentAnimationCoroutine);
            currentAnimationCoroutine = null;
        }

        if (leftTextIdleCoroutine != null)
        {
            StopCoroutine(leftTextIdleCoroutine);
            leftTextIdleCoroutine = null;
            if (leftText != null) leftText.transform.localScale = Vector3.one;
        }

        if (centerPanel != null) centerPanel.SetActive(false);
        if (leftPanel != null) leftPanel.SetActive(false);

        if (bouncingArrow != null) bouncingArrow.Hide();
        
        if (BuildTutorialDirector.Instance != null)
        {
            BuildTutorialDirector.Instance.EndTutorial();
        }

        if (currentSequence != null && PlayerDataManager.Instance != null && !string.IsNullOrEmpty(currentSequence.lessonName))
        {
            PlayerDataManager.Instance.CompleteLesson(currentSequence.lessonName);
        }

        currentSequence = null; 
    }

    public void SkipTutorial()
    {
        CompleteTutorial();
    }
}