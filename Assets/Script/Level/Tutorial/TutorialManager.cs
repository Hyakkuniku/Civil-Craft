using UnityEngine;
using UnityEngine.Events;
using TMPro;
using System.Collections; 
using System.Collections.Generic; // Required for List

public enum TutorialPosition
{
    Center = 0,
    Left = 1,
    // Value 2 is intentionally reserved so existing serialized enum values are never renumbered.
    LowCenter = 3
}

public enum TutorialStepAction
{
    None,
    SelectTool,
    SelectBridge,
    CopySelection,
    PositionPastePreview,
    PasteSelection
}

[System.Serializable]
public class TutorialStep
{
    [TextArea(3, 6)]
    public string message = "Step description here...";
    public TutorialPosition screenPosition = TutorialPosition.Center;
    
    public bool showNextButton = true;
    public bool canSkip = false;

    // --- NEW: Step-Specific Wasp Waypoints! ---
    [Header("Wasp Guide Settings")]
    [Tooltip("Waypoints for the wasp to guide the player during THIS specific step.")]
    public List<GuiderWaypoint> stepWaypoints;

    [Header("3D World Highlight")]
    [Tooltip("Drag your 3D Ghost Box here. It will turn on for this step!")]
    public GameObject worldHighlightObject; 

    [Header("Pointer Settings")]
    public bool usePointer = false;
    public RectTransform pointerTarget;
    public Vector2 pointerOffset = new Vector2(0, 80);
    public float pointerRotation = 180f;

    public bool advanceOnClick = false;

    [Header("Required Player Action")]
    [Tooltip("Optional semantic action used by build tutorials that react to selection and clipboard state.")]
    public TutorialStepAction requiredAction = TutorialStepAction.None;

    [Header("Events")]
    public UnityEvent OnStepStart = new UnityEvent();
}

public class TutorialManager : MonoBehaviour
{
    private sealed class SuspendedTutorialState
    {
        public TutorialSequence sequence;
        public int stepIndex;
    }

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
    public float transitionDuration = 0.25f;
    public float slideTransitionDuration = 0.6f;
    [Tooltip("Incoming offset used only when ReturnToStep forces the tutorial backwards.")]
    public Vector2 reverseStepOffset = new Vector2(-180f, 0f);

    [Header("Low Center Position")]
    [Tooltip("Offset from the authored Center panel position while a step uses LowCenter.")]
    [SerializeField] private Vector2 lowCenterOffset = new Vector2(0f, -520f);

    [Header("Left Panel Attention Animation")]
    public float pulseSpeed = 4f;
    public float pulseAmount = 0.05f;

    public bool IsTutorialActive { get; private set; } = false;
    public TutorialPointer SharedPointer => bouncingArrow;

    private TutorialSequence currentSequence;
    private int currentStepIndex = -1;
    private bool isAdvancingStep;
    private int lastAdvanceFrame = -1;
    private readonly List<TutorialSequence> queuedSequences = new List<TutorialSequence>();
    private readonly Stack<SuspendedTutorialState> suspendedSequences = new Stack<SuspendedTutorialState>();
    private Coroutine queuedSequenceCoroutine;

    public int CurrentStepIndex => currentStepIndex;
    public string CurrentLessonName => currentSequence != null ? currentSequence.lessonName : string.Empty;
    /// <summary>The sequence currently being shown, or null when no tutorial is active.</summary>
    public TutorialSequence ActiveSequence => IsTutorialActive ? currentSequence : null;
    public TutorialStepAction CurrentStepAction
    {
        get
        {
            if (currentSequence == null || currentSequence.tutorialSteps == null ||
                currentStepIndex < 0 || currentStepIndex >= currentSequence.tutorialSteps.Length)
            {
                return TutorialStepAction.None;
            }

            return currentSequence.tutorialSteps[currentStepIndex].requiredAction;
        }
    }

    public bool IsPlayingLesson(string lessonName)
    {
        return IsTutorialActive && currentSequence != null &&
               string.Equals(currentSequence.lessonName, lessonName, System.StringComparison.Ordinal);
    }

    public bool IsPlayingSequence(TutorialSequence sequence)
    {
        return IsTutorialActive && sequence != null && currentSequence == sequence;
    }
    
    private UnityEngine.UI.Button trackedButton = null;
    private UnityAction trackedButtonAction = null;
    
    private Coroutine currentAnimationCoroutine;
    private Coroutine leftTextIdleCoroutine; 

    private const int ActiveTutorialSortingOrder = 31000;
    private Canvas tutorialRootCanvas;
    private bool hasSavedTutorialCanvasState;
    private bool tutorialCanvasWasEnabled;
    private bool tutorialCanvasOverrodeSorting;
    private int tutorialCanvasSortingOrder;
    private RectTransform centerPanelRect;
    private Vector2 centerPanelDefaultAnchoredPosition;
    private bool hasCachedCenterPanelPosition;
    private RectTransform leftPanelRect;
    private Vector2 leftPanelDefaultAnchoredPosition;
    private bool hasCachedLeftPanelPosition;
    
    private TutorialPosition? lastScreenPosition = null;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else
        {
            Destroy(gameObject);
            return;
        }

        // Scene-authored tutorial UI may be left active for easier editing. Runtime
        // ownership starts here, so nothing is visible until PlayTutorial selects a
        // real step and target.
        if (centerPanel != null) centerPanel.SetActive(false);
        if (leftPanel != null) leftPanel.SetActive(false);
        if (bouncingArrow != null) bouncingArrow.Hide();

        CacheTutorialPanelPositions();
        tutorialRootCanvas = FindTutorialRootCanvas();
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
        if (sequence == null || sequence.tutorialSteps == null || sequence.tutorialSteps.Length == 0) return;
        if (IsTutorialActive)
        {
            QueueTutorial(sequence);
            return;
        }

        ClearTrackedButton();
        isAdvancingStep = false;

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
            BuildTutorialDirector.Instance.BeginSequence();

        currentSequence = sequence;
        currentStepIndex = -1;
        lastAdvanceFrame = -1;
        IsTutorialActive = true;
        lastScreenPosition = null; 

        PrepareTutorialCanvas();
        ShowNextStep();
    }

    /// <summary>
    /// Temporarily suspends the active sequence, shows a modal-specific tutorial,
    /// then resumes the suspended sequence at the exact step it was displaying.
    /// Only one tutorial owns the shared UI at a time.
    /// </summary>
    public void PlayPriorityTutorial(TutorialSequence sequence)
    {
        if (sequence == null || sequence.tutorialSteps == null || sequence.tutorialSteps.Length == 0)
            return;
        if (IsPlayingSequence(sequence) || !sequence.CanStartAsPriorityTutorial())
            return;

        queuedSequences.Remove(sequence);

        if (IsTutorialActive && currentSequence != null)
        {
            suspendedSequences.Push(new SuspendedTutorialState
            {
                sequence = currentSequence,
                stepIndex = currentStepIndex
            });
            SuspendCurrentTutorial();
        }

        PlayTutorial(sequence);
    }

    public void ShowNextStep()
    {
        if (currentSequence == null || !IsTutorialActive || isAdvancingStep) return;
        if (BuildTutorialDirector.Instance != null && BuildTutorialDirector.Instance.IsAwaitingInvalidBarUndo) return;
        if (lastAdvanceFrame == Time.frameCount) return;

        isAdvancingStep = true;
        lastAdvanceFrame = Time.frameCount;

        try
        {
            ClearTrackedButton();

            currentStepIndex++;

            if (currentStepIndex >= currentSequence.tutorialSteps.Length)
            {
                CompleteTutorial();
                return;
            }

            if (BuildTutorialDirector.Instance != null)
                BuildTutorialDirector.Instance.BeginStep(currentStepIndex);

            TutorialStep step = currentSequence.tutorialSteps[currentStepIndex];
            ShowTutorialStep(step, false);
        }
        finally
        {
            isAdvancingStep = false;
        }
    }

    /// <summary>
    /// Returns an active sequence to an earlier step and invokes that step's setup
    /// events again. Used when Undo invalidates a tracing step that already advanced.
    /// </summary>
    public bool ReturnToStep(int stepIndex)
    {
        if (!IsTutorialActive || currentSequence == null || currentSequence.tutorialSteps == null ||
            stepIndex < 0 || stepIndex >= currentSequence.tutorialSteps.Length ||
            stepIndex >= currentStepIndex || isAdvancingStep)
        {
            return false;
        }

        isAdvancingStep = true;
        try
        {
            ClearTrackedButton();
            currentStepIndex = stepIndex;
            lastAdvanceFrame = -1;
            lastScreenPosition = null;

            if (BuildTutorialDirector.Instance != null)
                BuildTutorialDirector.Instance.BeginStep(currentStepIndex);

            ShowTutorialStep(currentSequence.tutorialSteps[currentStepIndex], true);
            return true;
        }
        finally
        {
            isAdvancingStep = false;
        }
    }

    private void ShowTutorialStep(TutorialStep step, bool playReverseAnimation)
    {
        // A rapidly advanced step can interrupt a slide before it reaches its target.
        // Stop it before reading or restoring positions so the next transition starts
        // from a stable, authored layout instead of inheriting an in-between position.
        if (currentAnimationCoroutine != null)
        {
            StopCoroutine(currentAnimationCoroutine);
            currentAnimationCoroutine = null;
        }

        // A new step owns its own world indicator. Its OnStepStart event can show
        // the required indicator again after this cleanup.
        Tutorial3DIndicator.HideAll();

        if (leftTextIdleCoroutine != null)
        {
            StopCoroutine(leftTextIdleCoroutine);
            leftTextIdleCoroutine = null;
            if (leftText != null) leftText.transform.localScale = Vector3.one;
        }
        
        foreach (var s in currentSequence.tutorialSteps)
        {
            if (s.worldHighlightObject != null) s.worldHighlightObject.SetActive(false);
        }

        GameObject activePanel = GetPanelForPosition(step.screenPosition);
        TextMeshProUGUI activeText = GetTextForPosition(step.screenPosition);
        
        GameObject oldPanel = null;
        Vector3 oldPanelWorldPosition = Vector3.zero;
        bool hasOldPanelPosition = false;
        if (lastScreenPosition != null && lastScreenPosition != step.screenPosition)
        {
            oldPanel = GetPanelForPosition(lastScreenPosition.Value);
            if (oldPanel != null)
            {
                oldPanelWorldPosition = oldPanel.transform.position;
                hasOldPanelPosition = true;
            }
        }

        ApplyPanelPosition(step.screenPosition);

        if (centerPanel != null) centerPanel.SetActive(false);
        if (leftPanel != null) leftPanel.SetActive(false);

        if (activePanel != null)
        {
            activePanel.SetActive(true);
            if (activeText != null) activeText.text = step.message ?? "";
            
            float waitDelay = 0f;

            if (playReverseAnimation)
            {
                AnimatePanelReverse(activePanel);
                waitDelay = slideTransitionDuration;
            }
            else if (hasOldPanelPosition)
            {
                AnimatePanelSlide(activePanel, oldPanelWorldPosition);
                waitDelay = slideTransitionDuration;
            }
            else
            {
                AnimatePanelIn(activePanel);
                waitDelay = transitionDuration;
            }

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

        if (step.worldHighlightObject != null)
        {
            step.worldHighlightObject.SetActive(true);
        }

        // --- NEW: Trigger the step-specific Wasp path! ---
        if (PathGuider.Instance != null)
        {
            if (step.stepWaypoints != null && step.stepWaypoints.Count > 0)
            {
                PathGuider.Instance.SetNewWaypoints(step.stepWaypoints);
            }
            else
            {
                // Clear the wasp if this step doesn't have waypoints
                PathGuider.Instance.SetNewWaypoints(new List<GuiderWaypoint>()); 
            }
        }

        lastScreenPosition = step.screenPosition;
        step.OnStepStart?.Invoke();
    }

    private GameObject GetPanelForPosition(TutorialPosition position)
    {
        switch (position)
        {
            case TutorialPosition.Center:
            case TutorialPosition.LowCenter:
                return centerPanel;
            case TutorialPosition.Left:
            default:
                return leftPanel;
        }
    }

    private TextMeshProUGUI GetTextForPosition(TutorialPosition position)
    {
        switch (position)
        {
            case TutorialPosition.Center:
            case TutorialPosition.LowCenter:
                return centerText;
            case TutorialPosition.Left:
            default:
                return leftText;
        }
    }

    private void CacheTutorialPanelPositions()
    {
        if (!hasCachedCenterPanelPosition)
        {
            centerPanelRect = centerPanel != null ? centerPanel.GetComponent<RectTransform>() : null;
            if (centerPanelRect != null)
            {
                centerPanelDefaultAnchoredPosition = centerPanelRect.anchoredPosition;
                hasCachedCenterPanelPosition = true;
            }
        }

        if (!hasCachedLeftPanelPosition)
        {
            leftPanelRect = leftPanel != null ? leftPanel.GetComponent<RectTransform>() : null;
            if (leftPanelRect != null)
            {
                leftPanelDefaultAnchoredPosition = leftPanelRect.anchoredPosition;
                hasCachedLeftPanelPosition = true;
            }
        }
    }

    private void ApplyPanelPosition(TutorialPosition position)
    {
        CacheTutorialPanelPositions();

        if (position != TutorialPosition.Center && position != TutorialPosition.LowCenter)
        {
            if (hasCachedLeftPanelPosition && leftPanelRect != null)
                leftPanelRect.anchoredPosition = leftPanelDefaultAnchoredPosition;
            return;
        }

        if (!hasCachedCenterPanelPosition || centerPanelRect == null) return;

        centerPanelRect.anchoredPosition = centerPanelDefaultAnchoredPosition +
            (position == TutorialPosition.LowCenter ? lowCenterOffset : Vector2.zero);
    }

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

    private void AnimatePanelReverse(GameObject panel)
    {
        if (currentAnimationCoroutine != null) StopCoroutine(currentAnimationCoroutine);
        currentAnimationCoroutine = StartCoroutine(ReverseSlideRoutine(panel));
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

    private IEnumerator ReverseSlideRoutine(GameObject panel)
    {
        CanvasGroup group = panel.GetComponent<CanvasGroup>();
        if (group == null) group = panel.AddComponent<CanvasGroup>();

        RectTransform rect = panel.GetComponent<RectTransform>();
        if (rect == null) yield break;

        Vector2 targetPosition = rect.anchoredPosition;
        Vector2 startPosition = targetPosition + reverseStepOffset;
        float duration = Mathf.Max(0.01f, slideTransitionDuration);
        float elapsed = 0f;

        group.alpha = 0f;
        rect.anchoredPosition = startPosition;
        rect.localScale = new Vector3(0.95f, 0.95f, 1f);

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float smoothT = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            group.alpha = smoothT;
            rect.anchoredPosition = Vector2.Lerp(startPosition, targetPosition, smoothT);
            rect.localScale = Vector3.Lerp(new Vector3(0.95f, 0.95f, 1f), Vector3.one, smoothT);
            yield return null;
        }

        group.alpha = 1f;
        rect.anchoredPosition = targetPosition;
        rect.localScale = Vector3.one;
    }

    private IEnumerator IdleTextPulseRoutine(TextMeshProUGUI textToAnimate, float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        Transform textTransform = textToAnimate.transform;

        while (true)
        {
            float pulseScale = 1f + (Mathf.Sin(Time.unscaledTime * pulseSpeed) * pulseAmount);
            textTransform.localScale = new Vector3(pulseScale, pulseScale, 1f);
            yield return null;
        }
    }

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

    /// <summary>
    /// The tutorial's continue button is intentionally a full-screen click target.
    /// Camera gesture code must treat it as pass-through or every touch is
    /// incorrectly classified as UI input while the button is visible.
    /// </summary>
    public bool AllowsCameraInputThrough(GameObject hitObject)
    {
        if (hitObject == null || nextButton == null || !nextButton.activeInHierarchy)
            return false;

        Transform hitTransform = hitObject.transform;
        Transform nextTransform = nextButton.transform;
        return hitTransform == nextTransform || hitTransform.IsChildOf(nextTransform);
    }

    private void CompleteTutorial()
    {
        TutorialSequence completedSequence = currentSequence;
        ClearTrackedButton(); 
        Tutorial3DIndicator.HideAll();
        
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
        if (nextButton != null) nextButton.SetActive(false);
        if (skipButton != null) skipButton.SetActive(false);

        if (bouncingArrow != null) bouncingArrow.Hide();
        RestoreTutorialCanvas();
        
        if (completedSequence != null)
        {
            foreach (var s in completedSequence.tutorialSteps)
            {
                if (s.worldHighlightObject != null) s.worldHighlightObject.SetActive(false);
            }
        }

        // --- NEW: Clear the wasp path when the tutorial finishes! ---
        if (PathGuider.Instance != null)
        {
            PathGuider.Instance.SetNewWaypoints(new List<GuiderWaypoint>());
        }
        
        if (BuildTutorialDirector.Instance != null)
        {
            BuildTutorialDirector.Instance.EndTutorial();
        }

        if (completedSequence != null && PlayerDataManager.Instance != null && !string.IsNullOrEmpty(completedSequence.lessonName))
        {
            PlayerDataManager.Instance.CompleteLesson(completedSequence.lessonName);
        }

        currentSequence = null;
        currentStepIndex = -1;
        lastAdvanceFrame = -1;
        isAdvancingStep = false;

        if (completedSequence != null && completedSequence.autoStartNextSequence && completedSequence.nextSequence != null)
            QueueTutorial(completedSequence.nextSequence);

        if (!TryResumeSuspendedTutorial())
            TryStartQueuedTutorialNextFrame();
    }

    private void SuspendCurrentTutorial()
    {
        ClearTrackedButton();
        Tutorial3DIndicator.HideAll();

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

        if (currentSequence != null && currentSequence.tutorialSteps != null)
        {
            foreach (TutorialStep step in currentSequence.tutorialSteps)
            {
                if (step != null && step.worldHighlightObject != null)
                    step.worldHighlightObject.SetActive(false);
            }
        }

        if (centerPanel != null) centerPanel.SetActive(false);
        if (leftPanel != null) leftPanel.SetActive(false);
        if (nextButton != null) nextButton.SetActive(false);
        if (skipButton != null) skipButton.SetActive(false);
        if (bouncingArrow != null) bouncingArrow.Hide();

        if (PathGuider.Instance != null)
            PathGuider.Instance.SetNewWaypoints(new List<GuiderWaypoint>());
        if (BuildTutorialDirector.Instance != null)
            BuildTutorialDirector.Instance.EndTutorial();

        RestoreTutorialCanvas();
        IsTutorialActive = false;
        currentSequence = null;
        currentStepIndex = -1;
        lastAdvanceFrame = -1;
        isAdvancingStep = false;
        lastScreenPosition = null;
    }

    private bool TryResumeSuspendedTutorial()
    {
        while (suspendedSequences.Count > 0)
        {
            SuspendedTutorialState suspended = suspendedSequences.Pop();
            if (suspended.sequence == null || suspended.sequence.tutorialSteps == null ||
                suspended.sequence.tutorialSteps.Length == 0)
            {
                continue;
            }

            int resumeStep = Mathf.Clamp(
                suspended.stepIndex,
                0,
                suspended.sequence.tutorialSteps.Length - 1);

            currentSequence = suspended.sequence;
            currentStepIndex = resumeStep;
            lastAdvanceFrame = -1;
            isAdvancingStep = false;
            IsTutorialActive = true;
            lastScreenPosition = null;

            PrepareTutorialCanvas();
            if (BuildTutorialDirector.Instance != null)
            {
                BuildTutorialDirector.Instance.BeginSequence();
                BuildTutorialDirector.Instance.BeginStep(currentStepIndex);
            }

            ShowTutorialStep(currentSequence.tutorialSteps[currentStepIndex], false);
            return true;
        }

        return false;
    }

    private Canvas FindTutorialRootCanvas()
    {
        if (centerPanel != null)
        {
            Canvas canvas = centerPanel.GetComponentInParent<Canvas>(true);
            if (canvas != null) return canvas.rootCanvas;
        }

        if (leftPanel != null)
        {
            Canvas canvas = leftPanel.GetComponentInParent<Canvas>(true);
            if (canvas != null) return canvas.rootCanvas;
        }

        if (nextButton != null)
        {
            Canvas canvas = nextButton.GetComponentInParent<Canvas>(true);
            if (canvas != null) return canvas.rootCanvas;
        }

        return null;
    }

    private void PrepareTutorialCanvas()
    {
        if (tutorialRootCanvas == null)
            tutorialRootCanvas = FindTutorialRootCanvas();

        if (tutorialRootCanvas == null) return;

        if (!hasSavedTutorialCanvasState)
        {
            tutorialCanvasWasEnabled = tutorialRootCanvas.enabled;
            tutorialCanvasOverrodeSorting = tutorialRootCanvas.overrideSorting;
            tutorialCanvasSortingOrder = tutorialRootCanvas.sortingOrder;
            hasSavedTutorialCanvasState = true;
        }

        // Modal panels intentionally disable other canvases. A tutorial launched by
        // that modal must temporarily opt back in so its instructions are visible.
        tutorialRootCanvas.enabled = true;
        tutorialRootCanvas.overrideSorting = true;
        tutorialRootCanvas.sortingOrder = ActiveTutorialSortingOrder;
    }

    private void RestoreTutorialCanvas()
    {
        if (!hasSavedTutorialCanvasState || tutorialRootCanvas == null) return;

        tutorialRootCanvas.enabled = tutorialCanvasWasEnabled;
        tutorialRootCanvas.overrideSorting = tutorialCanvasOverrodeSorting;
        tutorialRootCanvas.sortingOrder = tutorialCanvasSortingOrder;
        hasSavedTutorialCanvasState = false;
    }

    public void QueueTutorial(TutorialSequence sequence)
    {
        if (sequence == null || queuedSequences.Contains(sequence)) return;
        queuedSequences.Add(sequence);
        if (!IsTutorialActive) TryStartQueuedTutorialNextFrame();
    }

    /// <summary>
    /// Immediately abandons the current tutorial without recording completion and
    /// starts the supplied sequence from step zero. Used after a tutorial contract
    /// fails, including when its final input step already closed the old sequence.
    /// </summary>
    public void RestartTutorial(TutorialSequence sequence)
    {
        if (sequence == null || sequence.tutorialSteps == null || sequence.tutorialSteps.Length == 0)
            return;

        ClearTrackedButton();

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

        if (queuedSequenceCoroutine != null)
        {
            StopCoroutine(queuedSequenceCoroutine);
            queuedSequenceCoroutine = null;
        }

        queuedSequences.Clear();

        if (currentSequence != null && currentSequence.tutorialSteps != null)
        {
            foreach (TutorialStep step in currentSequence.tutorialSteps)
            {
                if (step != null && step.worldHighlightObject != null)
                    step.worldHighlightObject.SetActive(false);
            }
        }

        if (centerPanel != null) centerPanel.SetActive(false);
        if (leftPanel != null) leftPanel.SetActive(false);
        if (nextButton != null) nextButton.SetActive(false);
        if (skipButton != null) skipButton.SetActive(false);
        if (bouncingArrow != null) bouncingArrow.Hide();

        if (PathGuider.Instance != null)
            PathGuider.Instance.SetNewWaypoints(new List<GuiderWaypoint>());

        if (BuildTutorialDirector.Instance != null)
            BuildTutorialDirector.Instance.EndTutorial();

        IsTutorialActive = false;
        currentSequence = null;
        currentStepIndex = -1;
        lastAdvanceFrame = -1;
        isAdvancingStep = false;
        lastScreenPosition = null;

        PlayTutorial(sequence);
    }

    private void TryStartQueuedTutorialNextFrame()
    {
        if (queuedSequenceCoroutine != null) StopCoroutine(queuedSequenceCoroutine);
        queuedSequenceCoroutine = StartCoroutine(StartQueuedTutorialRoutine());
    }

    private IEnumerator StartQueuedTutorialRoutine()
    {
        yield return null;
        queuedSequenceCoroutine = null;

        if (IsTutorialActive) yield break;

        for (int i = 0; i < queuedSequences.Count; i++)
        {
            TutorialSequence candidate = queuedSequences[i];
            if (candidate == null)
            {
                queuedSequences.RemoveAt(i--);
                continue;
            }

            if (!candidate.CanStartTutorial()) continue;

            queuedSequences.RemoveAt(i);
            PlayTutorial(candidate);
            yield break;
        }
    }

    public void SkipTutorial()
    {
        CompleteTutorial();
    }

    /// <summary>
    /// Developer-menu cleanup after every scene tutorial has been recorded as
    /// complete. Prevents auto-start chains and previously queued sequences from
    /// opening again on the following frame.
    /// </summary>
    public void DebugCloseActiveTutorialAndClearQueue()
    {
        if (queuedSequenceCoroutine != null)
        {
            StopCoroutine(queuedSequenceCoroutine);
            queuedSequenceCoroutine = null;
        }

        queuedSequences.Clear();
        suspendedSequences.Clear();

        if (IsTutorialActive)
            CompleteTutorial();

        // CompleteTutorial can queue the sequence's configured next tutorial.
        if (queuedSequenceCoroutine != null)
        {
            StopCoroutine(queuedSequenceCoroutine);
            queuedSequenceCoroutine = null;
        }

        queuedSequences.Clear();
    }
}
