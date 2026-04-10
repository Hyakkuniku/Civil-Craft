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

    // --- THE FIX: Live-update the pointer so you can tweak it in the Inspector! ---
    private void Update()
    {
        if (IsTutorialActive && currentSequence != null && currentStepIndex >= 0 && currentStepIndex < currentSequence.tutorialSteps.Length)
        {
            var step = currentSequence.tutorialSteps[currentStepIndex];
            
            // Constantly feed the latest Inspector values to the pointer while the step is active!
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

        // Hide it initially if it's not supposed to be used
        if (!step.usePointer || step.pointerTarget == null)
        {
            if (bouncingArrow != null) bouncingArrow.Hide();
        }

        step.OnStepStart?.Invoke();
    }

    public void SetNextButtonActive(bool isActive)
    {
        if (nextButton != null) nextButton.SetActive(isActive);
    }

    private void CompleteTutorial()
    {
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
    public bool showNextButton = true;
    public bool canSkip = false;

    [Header("Pointer Settings")]
    public bool usePointer = false;
    public RectTransform pointerTarget;
    public Vector2 pointerOffset = new Vector2(0, 80);
    public float pointerRotation = 180f;

    [Header("Events")]
    public UnityEvent OnStepStart = new UnityEvent();
}