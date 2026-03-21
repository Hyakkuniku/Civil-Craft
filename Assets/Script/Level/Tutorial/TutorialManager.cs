using UnityEngine;
using UnityEngine.Events;
using TMPro;

// NEW: A simple list of screen positions we can pick from!
public enum TutorialPosition
{
    TopCenter,
    BottomLeft,
    BottomRight,
    Center
}

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
    [SerializeField] private GameObject nextButton;     
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
        
        if (tutorialText != null) 
        {
            // 1. Update the words
            tutorialText.text = step.message ?? "";

            // 2. NEW: Automatically move the text box to the chosen location!
            RectTransform rt = tutorialText.GetComponent<RectTransform>();
            
            switch (step.screenPosition)
            {
                case TutorialPosition.TopCenter:
                    rt.anchorMin = new Vector2(0.5f, 1f);
                    rt.anchorMax = new Vector2(0.5f, 1f);
                    rt.pivot = new Vector2(0.5f, 1f);
                    rt.anchoredPosition = new Vector2(0, -100); // 100 pixels down from the top
                    break;

                case TutorialPosition.BottomLeft:
                    rt.anchorMin = new Vector2(0f, 0f);
                    rt.anchorMax = new Vector2(0f, 0f);
                    rt.pivot = new Vector2(0f, 0f);
                    rt.anchoredPosition = new Vector2(100, 150); // Up and right from bottom-left
                    break;

                case TutorialPosition.BottomRight:
                    rt.anchorMin = new Vector2(1f, 0f);
                    rt.anchorMax = new Vector2(1f, 0f);
                    rt.pivot = new Vector2(1f, 0f);
                    rt.anchoredPosition = new Vector2(-100, 150); // Up and left from bottom-right
                    break;

                case TutorialPosition.Center:
                    rt.anchorMin = new Vector2(0.5f, 0.5f);
                    rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = Vector2.zero; // Dead center
                    break;
            }
        }

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

    public void MarkCurrentStepComplete()
    {
        if (currentStepIndex >= 0 && currentStepIndex < tutorialSteps.Length)
        {
            tutorialSteps[currentStepIndex].isComplete = true;
        }
    }
}

// ────────────────────────────────────────────────
// Tutorial Step Data
// ────────────────────────────────────────────────
[System.Serializable]
public class TutorialStep
{
    [TextArea(3, 6)]
    public string message = "Step description here...";

    // NEW: Adds a dropdown in the Unity Inspector!
    [Tooltip("Where should this text appear on the screen?")]
    public TutorialPosition screenPosition = TutorialPosition.TopCenter;

    public bool showNextButton = true;
    public bool canSkip = false;

    [HideInInspector]
    public bool isComplete;

    public UnityEvent OnStepStart = new UnityEvent();
}