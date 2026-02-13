using UnityEngine;
using UnityEngine.Events;
using TMPro;

public class TutorialManager : MonoBehaviour
{
    [Header("Tutorial Steps")]
    public TutorialStep[] tutorialSteps;

    [Header("Player & UI References")]
    [SerializeField] private Transform player;
    [SerializeField] private InputManager inputManager;
    [SerializeField] private PlayerInteract playerInteract;

    [Header("Tutorial UI")]
    [SerializeField] private GameObject tutorialPanel;
    [SerializeField] private TextMeshProUGUI tutorialText;
    [SerializeField] private GameObject nextButton;     // better as Button type, see note below
    [SerializeField] private GameObject skipButton;

    private int currentStepIndex = -1;
    public static TutorialManager Instance { get; private set; }

    public bool IsTutorialActive { get; private set; } = true;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    private void Start()
    {
        // Optional: hook up buttons here instead of doing it in prefab
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

        ShowNextStep();
    }

    public void ShowNextStep()
    {
        currentStepIndex++;

        if (currentStepIndex >= tutorialSteps.Length)
        {
            CompleteTutorial();
            return;
        }

        var step = tutorialSteps[currentStepIndex];
        ShowTutorialStep(step);
    }

    private void ShowTutorialStep(TutorialStep step)
    {
        IsTutorialActive = true;
        if (tutorialPanel != null) tutorialPanel.SetActive(true);
        if (tutorialText != null) tutorialText.text = step.message ?? "";

        if (nextButton != null)   nextButton.SetActive(step.showNextButton);
        if (skipButton != null)   skipButton.SetActive(step.canSkip);

        step.OnStepStart?.Invoke();
    }

    private void CompleteTutorial()
    {
        IsTutorialActive = false;
        if (tutorialPanel != null) tutorialPanel.SetActive(false);
        Debug.Log("Tutorial Complete!");
    }

    public void SkipTutorial()
    {
        CompleteTutorial();
    }

    // Optional helper — call this from other scripts if you want to force-advance
    public void MarkCurrentStepComplete()
    {
        if (currentStepIndex >= 0 && currentStepIndex < tutorialSteps.Length)
        {
            tutorialSteps[currentStepIndex].isComplete = true;
        }
    }
}

// ────────────────────────────────────────────────
// Moved outside + removed wrong inheritance
// ────────────────────────────────────────────────
[System.Serializable]
public class TutorialStep
{
    [TextArea(3, 6)]
    public string message = "Step description here...";

    public bool showNextButton = true;
    public bool canSkip = false;

    [HideInInspector]
    public bool isComplete;

    public UnityEvent OnStepStart = new UnityEvent();
}