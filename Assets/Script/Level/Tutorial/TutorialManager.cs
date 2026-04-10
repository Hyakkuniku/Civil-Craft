using UnityEngine;
using UnityEngine.Events;
using TMPro;

public enum TutorialPosition
{
    TopCenter,
    BottomLeft,
    BottomRight,
    Center
}

public class TutorialManager : MonoBehaviour
{
    public static TutorialManager Instance { get; private set; }

    [Header("Tutorial UI")]
    [SerializeField] private GameObject tutorialPanel;
    [SerializeField] private TextMeshProUGUI tutorialText; 
    [SerializeField] private GameObject nextButton;     
    [SerializeField] private GameObject skipButton;
    
    [SerializeField] private TutorialPointer bouncingArrow;

    public bool IsTutorialActive { get; private set; } = false;

    private TutorialSequence currentSequence;
    private int currentStepIndex = -1;
    
    // --- NEW: Keeps track of the button we are waiting for the player to click! ---
    private UnityEngine.UI.Button trackedButton = null;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        if (!IsTutorialActive) 
        {
            if (tutorialPanel != null) tutorialPanel.SetActive(false);
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

        if (sequence.tutorialWaypoints != null && sequence.tutorialWaypoints.Count > 0 && PathGuider.Instance != null)
        {
            PathGuider.Instance.SetNewWaypoints(sequence.tutorialWaypoints);
        }

        ShowNextStep();
    }

    public void ShowNextStep()
    {
        if (currentSequence == null || !IsTutorialActive) return;

        // Clean up any button listeners from the previous step!
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
        if (tutorialPanel != null) tutorialPanel.SetActive(true);
        
        if (tutorialText != null) 
        {
            tutorialText.text = step.message ?? "";

            RectTransform rt = tutorialText.GetComponent<RectTransform>();
            
            switch (step.screenPosition)
            {
                case TutorialPosition.TopCenter:
                    rt.anchorMin = new Vector2(0.5f, 1f); rt.anchorMax = new Vector2(0.5f, 1f); rt.pivot = new Vector2(0.5f, 1f);
                    rt.anchoredPosition = new Vector2(0, -100); 
                    break;
                case TutorialPosition.BottomLeft:
                    rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(0f, 0f); rt.pivot = new Vector2(0f, 0f);
                    rt.anchoredPosition = new Vector2(100, 150); 
                    break;
                case TutorialPosition.BottomRight:
                    rt.anchorMin = new Vector2(1f, 0f); rt.anchorMax = new Vector2(1f, 0f); rt.pivot = new Vector2(1f, 0f);
                    rt.anchoredPosition = new Vector2(-100, 150); 
                    break;
                case TutorialPosition.Center:
                    rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = Vector2.zero; 
                    break;
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
            // --- THE FIX: Hook into the target button if we want to advance on click! ---
            if (step.advanceOnClick)
            {
                UnityEngine.UI.Button targetBtn = step.pointerTarget.GetComponent<UnityEngine.UI.Button>();
                if (targetBtn != null)
                {
                    trackedButton = targetBtn;
                    trackedButton.onClick.AddListener(OnTrackedButtonClicked);

                    // Force hide the 'Next' button so the player HAS to click the target UI!
                    if (nextButton != null) nextButton.SetActive(false);
                }
                else
                {
                    Debug.LogWarning($"[Tutorial] Step wants to advance on click, but {step.pointerTarget.name} has no Button component!");
                }
            }
        }

        step.OnStepStart?.Invoke();
    }

    // --- NEW: Fired exactly when the player clicks the UI button we are pointing at ---
    private void OnTrackedButtonClicked()
    {
        ClearTrackedButton();
        ShowNextStep();
    }

    // --- NEW: Safely removes the listener so we don't accidentally double-fire later ---
    private void ClearTrackedButton()
    {
        if (trackedButton != null)
        {
            trackedButton.onClick.RemoveListener(OnTrackedButtonClicked);
            trackedButton = null;
        }
    }

    public void SetNextButtonActive(bool isActive)
    {
        if (nextButton != null) nextButton.SetActive(isActive);
    }

    private void CompleteTutorial()
    {
        ClearTrackedButton(); // Final cleanup just in case
        
        IsTutorialActive = false;
        if (tutorialPanel != null) tutorialPanel.SetActive(false);
        if (bouncingArrow != null) bouncingArrow.Hide();
        
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

[System.Serializable]
public class TutorialStep
{
    [TextArea(3, 6)]
    public string message = "Step description here...";
    public TutorialPosition screenPosition = TutorialPosition.TopCenter;
    
    [Tooltip("Show the standard Next button on the tutorial panel.")]
    public bool showNextButton = true;
    public bool canSkip = false;

    [Header("Pointer Settings")]
    public bool usePointer = false;
    public RectTransform pointerTarget;
    public Vector2 pointerOffset = new Vector2(0, 80);
    public float pointerRotation = 180f;

    // --- NEW: The Inspector toggle to turn this feature on! ---
    [Tooltip("If true, the tutorial will wait for the player to click the pointed target to advance (automatically hides Next button).")]
    public bool advanceOnClick = false;

    [Header("Events")]
    public UnityEvent OnStepStart = new UnityEvent();
}