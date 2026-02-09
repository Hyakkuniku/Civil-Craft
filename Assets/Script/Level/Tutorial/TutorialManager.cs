// Replace your TutorialManager with this enhanced version
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TutorialManager : MonoBehaviour
{
    public static TutorialManager Instance { get; private set; }

    [Header("UI References")]
    public GameObject tutorialCanvas;
    public TextMeshProUGUI tutorialTitle;
    public TextMeshProUGUI tutorialText;
    public Button nextButton;
    public Button skipButton;
    public Image highlightImage;
    public GameObject touchArrowPrefab;

    [Header("Steps")]
    public TutorialStep[] tutorialSteps;

    int currentStep = 0;
    bool tutorialActive = false;
    Coroutine currentCoroutine;

    [System.Serializable]
    public class TutorialStep
    {
        public string title;
        [TextArea(3,6)] public string description;
        public TutorialStepType type;
        public string targetTag;
        public float highlightDuration = 2f;
        public bool requireMovement;
        public Vector3 movementTarget;
        public float movementRadius = 2f;
    }

    public enum TutorialStepType { InfoOnly, HighlightObject, WaitForMovement, WaitForInteraction, WaitForBuildMode }

    void Awake() { Instance = this; }
    
    void Start()
    {
        tutorialCanvas.SetActive(false);
        nextButton.onClick.AddListener(() => { currentStep++; ShowStep(); });
        skipButton.onClick.AddListener(CompleteTutorial);
    }

    public void StartTutorial()
    {
        if (tutorialActive || PlayerPrefs.GetInt("TutorialCompleted", 0) == 1) return;
        tutorialActive = true;
        currentStep = 0;
        tutorialCanvas.SetActive(true);
        ShowStep();
    }

    void ShowStep()
    {
        if (currentStep >= tutorialSteps.Length) { CompleteTutorial(); return; }
        
        var step = tutorialSteps[currentStep];
        tutorialTitle.text = step.title;
        tutorialText.text = step.description;

        // Stop previous effects
        if (currentCoroutine != null) StopCoroutine(currentCoroutine);

        switch (step.type)
        {
            case TutorialStepType.HighlightObject:
                currentCoroutine = StartCoroutine(HighlightObject(step));
                break;
            case TutorialStepType.WaitForMovement:
                currentCoroutine = StartCoroutine(WaitForMovement(step));
                break;
        }
    }

    IEnumerator HighlightObject(TutorialStep step)
    {
        GameObject target = GameObject.FindWithTag(step.targetTag);
        if (target == null) yield break;

        highlightImage.gameObject.SetActive(true);
        Canvas canvas = tutorialCanvas.GetComponent<Canvas>();

        while (true)
        {
            Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(Camera.main, target.transform.position);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvas.transform as RectTransform, screenPos, canvas.worldCamera, out Vector2 canvasPos);
            highlightImage.rectTransform.anchoredPosition = canvasPos;
            yield return null;
        }
    }

    IEnumerator WaitForMovement(TutorialStep step)
    {
        var player = FindObjectOfType<PlayerMotor>().transform;
        while (Vector3.Distance(player.position, step.movementTarget) > step.movementRadius)
            yield return new WaitForSeconds(0.1f);
        currentStep++;
        ShowStep();
    }

    void CompleteTutorial()
    {
        tutorialActive = false;
        tutorialCanvas.SetActive(false);
        PlayerPrefs.SetInt("TutorialCompleted", 1);
    }

    void Update()
{
    // Press T to start tutorial (testing only)
    if (Input.GetKeyDown(KeyCode.T))
        StartTutorial();
}
}