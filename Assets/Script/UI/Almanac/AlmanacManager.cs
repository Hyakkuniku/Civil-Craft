using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public enum AlmanacTabType { General, Contracts, Lessons, Materials }

public enum TabVisibility { Normal, AlwaysHidden }

[System.Serializable]
public class AlmanacCategory
{
    public string categoryName;
    public AlmanacTabType tabType = AlmanacTabType.General;
    
    [Header("Visibility Control")]
    [Tooltip("Set to Always Hidden if you want to disable this tab while developing it!")]
    public TabVisibility visibilityMode = TabVisibility.Normal; 
    
    public Button tabButton;
    
    [Header("Tab Visuals")]
    public Sprite inactiveSprite;
    public Sprite activeSprite;

    [Header("Alert Integration")]
    public GameObject tabAlertIcon;

    [Header("Page Containers")]
    public Transform leftPageZone;
    public Transform rightPageZone;

    [HideInInspector] public List<GameObject> leftPages = new List<GameObject>();
    [HideInInspector] public List<GameObject> rightPages = new List<GameObject>();
}

public class AlmanacManager : MonoBehaviour
{
    public static AlmanacManager Instance { get; private set; }
    public GameObject Panel => almanacCanvas;

    [Header("HUD Integration")]
    public GameObject hudOpenButton; 
    public GameObject newAlertIcon; 

    [Header("Opening Animation")]
    public GameObject animationPanel; 
    public Image animationImage; 
    public Sprite[] bookOpenFrames; 
    [Min(0.01f)]
    public float frameRate = 0.03f; 
    private bool isAnimating = false;

    [Header("Tutorial Integration")]
    public TutorialSequence onFirstOpenTutorial;
    [SerializeField] private TutorialSequence onContractsTabUnlockedTutorial;
    [SerializeField] private TutorialSequence onLessonsTabUnlockedTutorial;
    [SerializeField] private TutorialSequence onMaterialsTabUnlockedTutorial;

    [Header("UI Management")]
    public List<GameObject> uiElementsToHide = new List<GameObject>();
    private List<GameObject> temporarilyHiddenPanels = new List<GameObject>();

    [Header("UI Panels")]
    public GameObject almanacCanvas;

    [Header("Tabs & Categories")]
    public List<AlmanacCategory> categories = new List<AlmanacCategory>();
    public float selectedTabUpOffset = 15f; 
    public float tabTransitionSpeed = 10f;
    [Min(0.1f)] public float tabSwitchDuration = 0.28f;
    [Min(0f)] public float tabSwitchSlide = 26f;

    [Header("Pagination & Animation")]
    public Button prevButton;
    public Button nextButton;
    public float flipDuration = 0.25f; 

    [HideInInspector] public bool useVirtualPagination = false;
    [HideInInspector] public bool virtualHasNext = false;
    [HideInInspector] public bool virtualHasPrev = false;
    private System.Action<bool> OnVirtualPageTurn;
    
    public System.Action<int> OnCategoryChanged; 

    private int currentCategoryIndex = 0;
    private int currentSpreadIndex = 0; 
    private bool isFlipping = false;
    private bool isSwitchingCategory;
    private InputManager menuInputManager;
    private bool restoreMovementAfterClose;
    private bool restoreLookAfterClose;
    private bool hasCapturedMenuInput;

    private Dictionary<RectTransform, float> originalTabYPositions = new Dictionary<RectTransform, float>();
    private Dictionary<RectTransform, float> targetTabYPositions = new Dictionary<RectTransform, float>();
    private readonly Dictionary<GameObject, bool> archiveInteractionButtonStates =
        new Dictionary<GameObject, bool>();
    private readonly List<AlmanacLearningHub> learningHubs = new List<AlmanacLearningHub>();

    private sealed class TabPageVisual
    {
        public RectTransform rect;
        public CanvasGroup group;
        public Vector2 position;
        public Vector3 scale;
        public float alpha;
        public float direction;
    }

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else
        {
            Destroy(gameObject);
            return;
        }

        // Scene UI must start closed regardless of saved-player state or script
        // execution order. Doing this in Awake avoids an active Almanac being visible
        // for a frame before Start, especially after clearing data and reloading.
        if (almanacCanvas != null) almanacCanvas.SetActive(false);
        if (animationPanel != null) animationPanel.SetActive(false);
        if (newAlertIcon != null) newAlertIcon.SetActive(false);

        InitializeBook();
        EnsureTabUnlockTutorialDrafts();
    }

    private void Start()
    {
        if (almanacCanvas != null) almanacCanvas.SetActive(false);
        if (newAlertIcon != null) newAlertIcon.SetActive(false); 
        
        if (animationPanel != null) animationPanel.SetActive(false); 

        foreach (var cat in categories)
        {
            if (cat.tabAlertIcon != null) cat.tabAlertIcon.SetActive(false);
            
            if (cat.tabButton != null && cat.inactiveSprite != null)
            {
                Image tabImage = cat.tabButton.GetComponent<Image>();
                if (tabImage != null) tabImage.sprite = cat.inactiveSprite;
            }
        }

        if (PlayerDataManager.Instance != null)
        {
            bool playerHasAlmanac = PlayerDataManager.Instance.CurrentData.hasAlmanac;
            if (hudOpenButton != null) hudOpenButton.SetActive(playerHasAlmanac);

            PlayerDataManager.Instance.OnAlmanacUnlocked += ShowHudButton;
            PlayerDataManager.Instance.OnAlmanacAlertsChanged += HandleAlmanacAlertsChanged;
        }

        EvaluateTabUnlocks();
        RefreshPersistentAlerts();
    }

    private void OnDestroy()
    {
        if (PlayerDataManager.Instance != null)
        {
            PlayerDataManager.Instance.OnAlmanacUnlocked -= ShowHudButton;
            PlayerDataManager.Instance.OnAlmanacAlertsChanged -= HandleAlmanacAlertsChanged;
        }

        RestoreMenuInput();
        RestoreArchiveInteractionButtonContainers();
    }

    private void ShowHudButton()
    {
        if (hudOpenButton != null) hudOpenButton.SetActive(true);
        RefreshPersistentAlerts();
    }

    private void HandleAlmanacAlertsChanged()
    {
        RefreshPersistentAlerts();
    }

    private void Update()
    {
        foreach (var kvp in targetTabYPositions)
        {
            RectTransform tabRect = kvp.Key;
            float targetY = kvp.Value;
            
            Vector2 currentPos = tabRect.anchoredPosition;
            currentPos.y = Mathf.Lerp(currentPos.y, targetY, Time.deltaTime * tabTransitionSpeed);
            tabRect.anchoredPosition = currentPos;
        }

    }

    // ==========================================
    // UNLOCK & VISIBILITY LOGIC
    // ==========================================

    public void EvaluateTabUnlocks()
    {
        if (PlayerDataManager.Instance == null) return;
        PlayerData data = PlayerDataManager.Instance.CurrentData;
        bool changed = false;

        if (!data.hasUnlockedContractsTab && PlayerDataManager.Instance.HasAnyCompletedContract())
        {
            data.hasUnlockedContractsTab = true;
            changed = true;
        }

        if (!data.hasUnlockedLessonsTab && data.unlockedLessonIds != null && data.unlockedLessonIds.Count > 0)
        {
            data.hasUnlockedLessonsTab = true;
            changed = true;
        }

        if (!data.hasUnlockedMaterialsTab && data.discoveredMaterialIds != null &&
            data.discoveredMaterialIds.Count > 0)
        {
            data.hasUnlockedMaterialsTab = true;
            changed = true;
        }

        if (changed) PlayerDataManager.Instance.SaveGame();
        RefreshTabVisibility(data);
    }

    private void RefreshTabVisibility(PlayerData data)
    {
        foreach (var cat in categories)
        {
            if (cat.tabButton != null)
            {
                bool shouldBeVisible = true;
                
                if (cat.tabType == AlmanacTabType.Contracts) shouldBeVisible = data.hasUnlockedContractsTab;
                if (cat.tabType == AlmanacTabType.Lessons) shouldBeVisible = data.hasUnlockedLessonsTab;
                if (cat.tabType == AlmanacTabType.Materials)
                    shouldBeVisible = data.discoveredMaterialIds != null &&
                                      data.discoveredMaterialIds.Count > 0;
                if (!IsSupportedCategory(cat)) shouldBeVisible = false;

                if (cat.visibilityMode == TabVisibility.AlwaysHidden)
                {
                    shouldBeVisible = false;
                }

                cat.tabButton.gameObject.SetActive(shouldBeVisible);
            }
        }
    }

    private void TriggerTabAlertByType(AlmanacTabType targetType)
    {
        if (PlayerDataManager.Instance == null) return;

        if (targetType == AlmanacTabType.Contracts)
            PlayerDataManager.Instance.MarkContractsAlmanacUnread();
        else if (targetType == AlmanacTabType.Lessons)
        {
            // Lesson unlock code owns creation of unread state. Do not manufacture
            // a false alert through a generic UI call.
            RefreshPersistentAlerts();
        }
        else if (targetType == AlmanacTabType.Materials)
        {
            // Discovery owns unread state, matching the Lessons archive.
            RefreshPersistentAlerts();
        }
    }

    public void RefreshPersistentAlerts()
    {
        PlayerData data = PlayerDataManager.Instance != null ? PlayerDataManager.Instance.CurrentData : null;
        bool hasAlmanac = data != null && data.hasAlmanac;
        bool bookIsOpen = almanacCanvas != null && almanacCanvas.activeSelf;
        bool hasGenuineUnreadContent = data != null &&
            (data.hasUnreadAlmanacUnlockAlert || data.hasUnreadContractsAlert ||
             data.hasUnreadLessonsAlert || data.hasUnreadMaterialsAlert);

        if (data != null)
            RefreshTabVisibility(data);

        if (hudOpenButton != null) hudOpenButton.SetActive(hasAlmanac);
        if (newAlertIcon != null)
            newAlertIcon.SetActive(hasAlmanac && hasGenuineUnreadContent && !bookIsOpen);

        foreach (AlmanacCategory category in categories)
        {
            if (category.tabAlertIcon == null) continue;

            bool unread = false;
            if (data != null && category.tabType == AlmanacTabType.Contracts)
                unread = data.hasUnreadContractsAlert;
            else if (data != null && category.tabType == AlmanacTabType.Lessons)
                unread = data.hasUnreadLessonsAlert;
            else if (data != null && category.tabType == AlmanacTabType.Materials)
                unread = data.hasUnreadMaterialsAlert;

            category.tabAlertIcon.SetActive(hasAlmanac && unread && IsSupportedCategory(category) &&
                category.visibilityMode != TabVisibility.AlwaysHidden);
        }
    }

    // ==========================================

    private void InitializeBook()
    {
        InitializeLearningHubs();

        for (int i = 0; i < categories.Count; i++)
        {
            int index = i; 
            AlmanacCategory cat = categories[i];

            if (!IsSupportedCategory(cat))
            {
                if (cat.tabButton != null) cat.tabButton.gameObject.SetActive(false);
                if (cat.tabAlertIcon != null) cat.tabAlertIcon.SetActive(false);
                continue;
            }

            if (cat.tabButton != null)
            {
                RectTransform rect = cat.tabButton.GetComponent<RectTransform>();
                originalTabYPositions[rect] = rect.anchoredPosition.y;
                targetTabYPositions[rect] = rect.anchoredPosition.y;
                cat.tabButton.onClick.AddListener(() => RequestCategory(index));
            }

            if (cat.leftPageZone != null)
            {
                foreach (Transform child in cat.leftPageZone)
                {
                    cat.leftPages.Add(child.gameObject);
                    child.gameObject.SetActive(false);
                    
                    RectTransform pageRect = child.GetComponent<RectTransform>();
                    if (pageRect != null) pageRect.pivot = new Vector2(1f, 0.5f);
                }
            }

            if (cat.rightPageZone != null)
            {
                foreach (Transform child in cat.rightPageZone)
                {
                    cat.rightPages.Add(child.gameObject);
                    child.gameObject.SetActive(false);
                    
                    RectTransform pageRect = child.GetComponent<RectTransform>();
                    if (pageRect != null) pageRect.pivot = new Vector2(0f, 0.5f);
                }
            }
        }

        if (prevButton != null) prevButton.onClick.AddListener(() => TurnPage(false));
        if (nextButton != null) nextButton.onClick.AddListener(() => TurnPage(true));
    }

    private void InitializeLearningHubs()
    {
        if (almanacCanvas == null || categories == null) return;

        AlmanacLessonTab lessonTab = almanacCanvas.GetComponentInChildren<AlmanacLessonTab>(true);
        AlmanacMaterialTab materialTab = almanacCanvas.GetComponentInChildren<AlmanacMaterialTab>(true);
        if (lessonTab == null) lessonTab = FindObjectOfType<AlmanacLessonTab>(true);
        if (materialTab == null) materialTab = FindObjectOfType<AlmanacMaterialTab>(true);

        CreateLearningHub(AlmanacTabType.Lessons, AlmanacLearningContent.Lessons,
            lessonTab, materialTab);
        CreateLearningHub(AlmanacTabType.Materials, AlmanacLearningContent.Materials,
            lessonTab, materialTab);
    }

    private void CreateLearningHub(
        AlmanacTabType tabType,
        AlmanacLearningContent content,
        AlmanacLessonTab lessonTab,
        AlmanacMaterialTab materialTab)
    {
        AlmanacCategory category = categories.Find(candidate =>
            candidate != null && candidate.tabType == tabType);
        if (category == null || category.leftPageZone == null || category.rightPageZone == null)
        {
            Debug.LogWarning($"[Almanac] {tabType} page zones are missing; its learning spread could not be created.", this);
            return;
        }

        // Some legacy tabs (notably Lessons) serialized their right page zone as
        // inactive because the old reader enabled it dynamically. The new spread
        // owns both halves, so both page zones must be available to the manager.
        category.leftPageZone.gameObject.SetActive(true);
        category.rightPageZone.gameObject.SetActive(true);

        AlmanacLearningHub hub = almanacCanvas.AddComponent<AlmanacLearningHub>();
        hub.Build(this, categories.IndexOf(category), content, category.leftPageZone,
            category.rightPageZone, lessonTab, materialTab);
        learningHubs.Add(hub);
    }

    public void TriggerAlert()
    {
        // Alerts are derived from saved unread content. A generic UI call must not
        // manufacture an unread state when nothing new was added.
        RefreshPersistentAlerts();
    }

    public void TriggerTabAlert(string targetCategoryName)
    {
        foreach (AlmanacCategory category in categories)
        {
            if (category.categoryName == targetCategoryName && IsSupportedCategory(category))
            {
                TriggerTabAlertByType(category.tabType);
                break;
            }
        }
    }

    public void OpenAlmanac()
    {
        if (isAnimating) return; 

        CaptureArchiveInteractionButtonContainers();
        EvaluateTabUnlocks(); 
        StartCoroutine(OpenAlmanacRoutine());
    }

    private IEnumerator OpenAlmanacRoutine()
    {
        isAnimating = true;
        CaptureAndDisableMenuInput();

        if (PlayerDataManager.Instance != null) PlayerDataManager.Instance.MarkAlmanacOpened();
        RefreshPersistentAlerts();

        bool coordinatedAnimation = UIPanelCoordinator.Instance != null && animationPanel != null;
        if (coordinatedAnimation)
            UIPanelCoordinator.Instance.OpenPanel(animationPanel, false);

        // CanyonCrossing's animation panel lives under MainCanvas, which is also in
        // uiElementsToHide. Hide its siblings instead of disabling its parent canvas.
        HideUiElementsPreservingAnimation();
        yield return PlayBookAnimation(true);

        if (UIPanelCoordinator.Instance != null)
        {
            // Restore before taking the coordinator snapshot so it can accurately
            // restore the HUD after the Almanac closes.
            RestoreTemporarilyHiddenPanels();
            if (coordinatedAnimation)
                UIPanelCoordinator.Instance.ClosePanel(animationPanel);
            UIPanelCoordinator.Instance.OpenPanel(almanacCanvas, false);
        }

        if (almanacCanvas != null) almanacCanvas.SetActive(true);

        foreach (AlmanacLearningHub hub in learningHubs)
        {
            if (hub != null) hub.ResetToHome();
        }
        SelectFirstVisibleCategory();

        bool startedFirstOpenTutorial = false;
        if (onFirstOpenTutorial != null)
        {
            if (TutorialManager.Instance != null)
            {
                TutorialManager.Instance.PlayPriorityTutorial(onFirstOpenTutorial);
                startedFirstOpenTutorial = TutorialManager.Instance.IsPlayingSequence(onFirstOpenTutorial);
            }
            else
                onFirstOpenTutorial.TryStartTutorial();
        }

        // The first-open walkthrough owns this visit. Newly unlocked tab guides
        // begin on the next open so their pointers never compete for the book UI.
        if (!startedFirstOpenTutorial)
            TryStartPendingTabUnlockTutorial();

        isAnimating = false;
    }
    
    private void SelectFirstVisibleCategory()
    {
        for (int i = 0; i < categories.Count; i++)
        {
            if (categories[i].tabButton != null && categories[i].tabButton.gameObject.activeInHierarchy)
            {
                SelectCategory(i);
                return;
            }
        }
        SelectCategory(0);
    }

    public void CloseAlmanac()
    {
        if (isAnimating || isSwitchingCategory) return;

        // The first Almanac walkthrough is a required part of the Bhan house
        // quest. Closing on its first two pages would leave the house tutorial
        // suspended with no reliable completion path. The final walkthrough
        // step explicitly asks the player to close the book, so closing is
        // permitted once that step is reached.
        if (IsRequiredFirstOpenTutorialBlockingClose() || IsTabUnlockTutorialBlockingClose())
        {
            Debug.Log("[Almanac] Finish the active Almanac introduction before closing the book.");
            return;
        }

        StartCoroutine(CloseAlmanacRoutine(null));
    }

    private bool IsRequiredFirstOpenTutorialBlockingClose()
    {
        if (onFirstOpenTutorial == null || onFirstOpenTutorial.tutorialSteps == null ||
            onFirstOpenTutorial.tutorialSteps.Length == 0 || TutorialManager.Instance == null ||
            !TutorialManager.Instance.IsPlayingSequence(onFirstOpenTutorial))
        {
            return false;
        }

        int finalStepIndex = onFirstOpenTutorial.tutorialSteps.Length - 1;
        return TutorialManager.Instance.CurrentStepIndex < finalStepIndex;
    }

    private void TryStartPendingTabUnlockTutorial()
    {
        if (almanacCanvas == null || !almanacCanvas.activeInHierarchy ||
            PlayerDataManager.Instance == null || TutorialManager.Instance == null)
        {
            return;
        }

        PlayerData data = PlayerDataManager.Instance.CurrentData;
        if (data == null) return;

        if (TryStartTabUnlockTutorial(
                onContractsTabUnlockedTutorial,
                IsTabUnlocked(data, AlmanacTabType.Contracts)))
            return;

        if (TryStartTabUnlockTutorial(
                onLessonsTabUnlockedTutorial,
                IsTabUnlocked(data, AlmanacTabType.Lessons)))
            return;

        TryStartTabUnlockTutorial(
            onMaterialsTabUnlockedTutorial,
            IsTabUnlocked(data, AlmanacTabType.Materials));
    }

    private void TryStartSelectedTabUnlockTutorial(AlmanacTabType selectedType)
    {
        if (PlayerDataManager.Instance == null || TutorialManager.Instance == null ||
            !IsTabUnlocked(PlayerDataManager.Instance.CurrentData, selectedType))
        {
            return;
        }

        TutorialSequence sequence = GetTabUnlockTutorial(selectedType);
        if (sequence == null || sequence.tutorialSteps == null ||
            sequence.tutorialSteps.Length == 0 ||
            TutorialManager.Instance.IsPlayingSequence(sequence) ||
            !sequence.CanStartAsPriorityTutorial())
        {
            return;
        }

        StartCoroutine(StartSelectedTabUnlockTutorialRoutine(sequence));
    }

    private IEnumerator StartSelectedTabUnlockTutorialRoutine(TutorialSequence sequence)
    {
        TutorialManager.Instance.PlayPriorityTutorial(sequence);
        if (!TutorialManager.Instance.IsPlayingSequence(sequence)) yield break;

        // The player already performed the highlighted-tab click that normally
        // advances step zero, so go directly to that tab's explanation.
        yield return null;
        if (TutorialManager.Instance != null &&
            TutorialManager.Instance.IsPlayingSequence(sequence) &&
            TutorialManager.Instance.CurrentStepIndex == 0)
        {
            TutorialManager.Instance.ShowNextStep();
        }
    }

    private TutorialSequence GetTabUnlockTutorial(AlmanacTabType tabType)
    {
        if (tabType == AlmanacTabType.Contracts) return onContractsTabUnlockedTutorial;
        if (tabType == AlmanacTabType.Lessons) return onLessonsTabUnlockedTutorial;
        if (tabType == AlmanacTabType.Materials) return onMaterialsTabUnlockedTutorial;
        return null;
    }

    private static bool IsTabUnlocked(PlayerData data, AlmanacTabType tabType)
    {
        if (data == null) return false;
        if (tabType == AlmanacTabType.Contracts) return data.hasUnlockedContractsTab;
        if (tabType == AlmanacTabType.Lessons) return data.hasUnlockedLessonsTab;
        if (tabType == AlmanacTabType.Materials) return data.hasUnlockedMaterialsTab;
        return false;
    }

    private bool TryStartTabUnlockTutorial(TutorialSequence sequence, bool isUnlocked)
    {
        if (!isUnlocked || sequence == null || sequence.tutorialSteps == null ||
            sequence.tutorialSteps.Length == 0 || TutorialManager.Instance == null)
        {
            return false;
        }

        if (TutorialManager.Instance.IsPlayingSequence(sequence)) return true;
        if (!sequence.CanStartAsPriorityTutorial()) return false;

        if (TutorialManager.Instance.IsTutorialActive)
        {
            // Queue only behind another tab-unlock walkthrough. An unrelated
            // tutorial may outlive the open book and would leave pointers hidden.
            if (!IsAnyTabUnlockTutorialPlaying()) return false;
            TutorialManager.Instance.QueueTutorial(sequence);
        }
        else
        {
            TutorialManager.Instance.PlayPriorityTutorial(sequence);
        }

        return true;
    }

    private bool IsAnyTabUnlockTutorialPlaying()
    {
        if (TutorialManager.Instance == null) return false;
        return TutorialManager.Instance.IsPlayingSequence(onContractsTabUnlockedTutorial) ||
               TutorialManager.Instance.IsPlayingSequence(onLessonsTabUnlockedTutorial) ||
               TutorialManager.Instance.IsPlayingSequence(onMaterialsTabUnlockedTutorial);
    }

    private bool IsTabUnlockTutorialBlockingClose()
    {
        return IsAnyTabUnlockTutorialPlaying();
    }

    private void EnsureTabUnlockTutorialDrafts()
    {
        onContractsTabUnlockedTutorial = EnsureTabUnlockTutorialDraft(
            onContractsTabUnlockedTutorial,
            AlmanacTabType.Contracts,
            "Sequence_AlmanacContractsUnlocked",
            "A new <b><#E09500>CONTRACTS TAB</color></b> is available. Select it to review your completed jobs.",
            "Completed contracts are recorded here with their client, rewards, and saved bridge photo.");

        onLessonsTabUnlockedTutorial = EnsureTabUnlockTutorialDraft(
            onLessonsTabUnlockedTutorial,
            AlmanacTabType.Lessons,
            "Sequence_AlmanacLessonsUnlocked",
            "A new <b><#E09500>LESSONS TAB</color></b> is available. Select it to review what you have learned.",
            "New engineering lessons are added here as you complete activities and discover new concepts.");

        onMaterialsTabUnlockedTutorial = EnsureTabUnlockTutorialDraft(
            onMaterialsTabUnlockedTutorial,
            AlmanacTabType.Materials,
            "Sequence_AlmanacMaterialsUnlocked",
            "A new <b><#E09500>MATERIALS TAB</color></b> is available. Select it to inspect your discovered materials.",
            "The material archive records useful properties and grows whenever you discover a new construction material.");
    }

    private TutorialSequence EnsureTabUnlockTutorialDraft(
        TutorialSequence sequence,
        AlmanacTabType tabType,
        string lessonName,
        string unlockMessage,
        string contentsMessage)
    {
        if (sequence == null)
        {
            GameObject sequenceObject = new GameObject(lessonName);
            sequenceObject.transform.SetParent(transform, false);
            sequence = sequenceObject.AddComponent<TutorialSequence>();
        }

        if (sequence.tutorialSteps != null && sequence.tutorialSteps.Length > 0)
            return sequence;

        AlmanacCategory category = categories.Find(candidate =>
            candidate != null && candidate.tabType == tabType);
        RectTransform tabTarget = category != null && category.tabButton != null
            ? category.tabButton.transform as RectTransform
            : null;

        sequence.lessonName = lessonName;
        sequence.requiredPreviousLesson = string.Empty;
        sequence.playOnStart = false;
        sequence.nextSequence = null;
        sequence.autoStartNextSequence = false;
        sequence.tutorialSteps = new[]
        {
            CreateTabUnlockTutorialStep(
                unlockMessage,
                tabTarget,
                tabTarget != null,
                tabTarget == null),
            CreateTabUnlockTutorialStep(contentsMessage, null, false, true)
        };

        return sequence;
    }

    private static TutorialStep CreateTabUnlockTutorialStep(
        string message,
        RectTransform pointerTarget,
        bool advanceOnClick,
        bool showNextButton)
    {
        return new TutorialStep
        {
            message = message,
            screenPosition = TutorialPosition.Center,
            showNextButton = showNextButton,
            canSkip = true,
            lockLook = false,
            lockJump = false,
            stepWaypoints = new List<GuiderWaypoint>(),
            worldHighlightObject = null,
            usePointer = pointerTarget != null,
            pointerTarget = pointerTarget,
            pointerOffset = new Vector2(0f, 80f),
            pointerRotation = 180f,
            advanceOnClick = advanceOnClick,
            requiredAction = TutorialStepAction.None
        };
    }

    /// <summary>
    /// Closes the book and invokes the callback only after the closing animation,
    /// HUD restoration, and input restoration have finished.
    /// </summary>
    public void CloseAlmanacThen(System.Action afterClosed)
    {
        if (isAnimating) return;
        if (IsRequiredFirstOpenTutorialBlockingClose() || IsTabUnlockTutorialBlockingClose())
        {
            Debug.Log("[Almanac] Finish the active Almanac introduction before opening another archive panel.");
            return;
        }
        StartCoroutine(CloseAlmanacRoutine(afterClosed));
    }

    private IEnumerator CloseAlmanacRoutine(System.Action afterClosed)
    {
        isAnimating = true;

        if (almanacCanvas != null) almanacCanvas.SetActive(false);

        if (UIPanelCoordinator.Instance != null)
        {
            // Swap directly from the Almanac frame to an animation frame. Both
            // operations happen before rendering, preventing a one-frame HUD flash.
            UIPanelCoordinator.Instance.ClosePanel(almanacCanvas);
            if (animationPanel != null)
                UIPanelCoordinator.Instance.OpenPanel(animationPanel, false);
            HideUiElementsPreservingAnimation();
        }

        yield return PlayBookAnimation(false);
        RestoreTemporarilyHiddenPanels();
        if (UIPanelCoordinator.Instance != null && animationPanel != null)
            UIPanelCoordinator.Instance.ClosePanel(animationPanel);
        RestoreMenuInput();
        RestoreArchiveInteractionButtonContainers();

        isAnimating = false;
        afterClosed?.Invoke();
    }

    private void CaptureAndDisableMenuInput()
    {
        menuInputManager = FindObjectOfType<InputManager>();
        if (menuInputManager == null) return;

        restoreMovementAfterClose = menuInputManager.IsPlayerInputEnabled;
        restoreLookAfterClose = menuInputManager.IsLookInputEnabled;
        hasCapturedMenuInput = true;
        menuInputManager.SetPlayerInputEnable(false);
        menuInputManager.SetLookEnabled(false);
    }

    private void RestoreMenuInput()
    {
        if (!hasCapturedMenuInput) return;
        if (menuInputManager != null)
        {
            menuInputManager.SetPlayerInputEnable(restoreMovementAfterClose);
            menuInputManager.SetLookEnabled(restoreLookAfterClose);
        }

        hasCapturedMenuInput = false;
        menuInputManager = null;
    }

    private IEnumerator PlayBookAnimation(bool opening)
    {
        if (animationPanel == null || animationImage == null ||
            bookOpenFrames == null || bookOpenFrames.Length == 0)
        {
            yield break;
        }

        int firstIndex = opening ? 0 : bookOpenFrames.Length - 1;
        animationImage.sprite = bookOpenFrames[firstIndex];
        animationPanel.SetActive(true);

        float delay = Mathf.Max(0.01f, frameRate);
        if (opening)
        {
            for (int i = 0; i < bookOpenFrames.Length; i++)
            {
                if (bookOpenFrames[i] != null) animationImage.sprite = bookOpenFrames[i];
                yield return new WaitForSecondsRealtime(delay);
            }
        }
        else
        {
            for (int i = bookOpenFrames.Length - 1; i >= 0; i--)
            {
                if (bookOpenFrames[i] != null) animationImage.sprite = bookOpenFrames[i];
                yield return new WaitForSecondsRealtime(delay);
            }
        }

        animationPanel.SetActive(false);
    }

    private void HideUiElementsPreservingAnimation()
    {
        temporarilyHiddenPanels.Clear();

        foreach (GameObject ui in uiElementsToHide)
        {
            if (ui == null || !ui.activeSelf) continue;

            bool containsAnimation = animationPanel != null &&
                                     (ui == animationPanel ||
                                      animationPanel.transform.IsChildOf(ui.transform));
            if (!containsAnimation)
            {
                TrackAndHide(ui);
                continue;
            }

            // Keep the containing Canvas active. Only its animation branch remains.
            foreach (Transform child in ui.transform)
            {
                bool isAnimationBranch = child == animationPanel.transform ||
                                         animationPanel.transform.IsChildOf(child);
                if (!isAnimationBranch) TrackAndHide(child.gameObject);
            }
        }
    }

    private void TrackAndHide(GameObject target)
    {
        if (target == null || !target.activeSelf || temporarilyHiddenPanels.Contains(target)) return;
        temporarilyHiddenPanels.Add(target);
        target.SetActive(false);
    }

    private void RestoreTemporarilyHiddenPanels()
    {
        foreach (GameObject ui in temporarilyHiddenPanels)
        {
            if (ui != null) ui.SetActive(true);
        }
        temporarilyHiddenPanels.Clear();
    }

    private void RequestCategory(int index)
    {
        if (index < 0 || index >= categories.Count || index == currentCategoryIndex ||
            isSwitchingCategory || isFlipping || isAnimating ||
            !IsSupportedCategory(categories[index]))
            return;

        StartCoroutine(SwitchCategoryRoutine(index));
    }

    private IEnumerator SwitchCategoryRoutine(int index)
    {
        isSwitchingCategory = true;

        AlmanacCategory outgoingCategory = categories[currentCategoryIndex];
        List<TabPageVisual> outgoing = CaptureSpreadVisuals(outgoingCategory, currentSpreadIndex);
        float outgoingDuration = Mathf.Max(0.04f, tabSwitchDuration * 0.42f);
        float incomingDuration = Mathf.Max(0.06f, tabSwitchDuration * 0.58f);

        yield return AnimateSpreadVisuals(outgoing, 0f, 1f, outgoingDuration);
        ApplyCategorySelection(index);
        ResetSpreadVisuals(outgoing);

        AlmanacCategory incomingCategory = categories[currentCategoryIndex];
        List<TabPageVisual> incoming = CaptureSpreadVisuals(incomingCategory, currentSpreadIndex);
        SetSpreadVisualProgress(incoming, 1f);
        yield return AnimateSpreadVisuals(incoming, 1f, 0f, incomingDuration);
        ResetSpreadVisuals(incoming);

        isSwitchingCategory = false;
        TryStartSelectedTabUnlockTutorial(categories[index].tabType);
    }

    private List<TabPageVisual> CaptureSpreadVisuals(AlmanacCategory category, int spreadIndex)
    {
        List<TabPageVisual> visuals = new List<TabPageVisual>();
        if (category == null) return visuals;

        CapturePageVisual(GetClampedPage(category.leftPages, spreadIndex), 1f, visuals);
        CapturePageVisual(GetClampedPage(category.rightPages, spreadIndex), -1f, visuals);
        return visuals;
    }

    private static void CapturePageVisual(
        GameObject page,
        float direction,
        ICollection<TabPageVisual> visuals)
    {
        if (page == null) return;
        RectTransform rect = page.transform as RectTransform;
        if (rect == null) return;

        CanvasGroup group = page.GetComponent<CanvasGroup>();
        if (group == null) group = page.AddComponent<CanvasGroup>();
        visuals.Add(new TabPageVisual
        {
            rect = rect,
            group = group,
            position = rect.anchoredPosition,
            scale = rect.localScale,
            alpha = group.alpha,
            direction = direction
        });
    }

    private IEnumerator AnimateSpreadVisuals(
        List<TabPageVisual> visuals,
        float from,
        float to,
        float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float normalized = Mathf.Clamp01(elapsed / duration);
            float eased = Mathf.SmoothStep(0f, 1f, normalized);
            SetSpreadVisualProgress(visuals, Mathf.Lerp(from, to, eased));
            yield return null;
        }
        SetSpreadVisualProgress(visuals, to);
    }

    private void SetSpreadVisualProgress(List<TabPageVisual> visuals, float progress)
    {
        foreach (TabPageVisual visual in visuals)
        {
            if (visual == null || visual.rect == null || visual.group == null) continue;
            visual.group.alpha = Mathf.Lerp(visual.alpha, 0f, progress);
            visual.rect.anchoredPosition = visual.position +
                new Vector2(visual.direction * tabSwitchSlide * progress, 0f);
            visual.rect.localScale = Vector3.Scale(visual.scale,
                new Vector3(Mathf.Lerp(1f, 0.975f, progress),
                    Mathf.Lerp(1f, 0.99f, progress), 1f));
        }
    }

    private static void ResetSpreadVisuals(List<TabPageVisual> visuals)
    {
        foreach (TabPageVisual visual in visuals)
        {
            if (visual == null || visual.rect == null || visual.group == null) continue;
            visual.group.alpha = visual.alpha;
            visual.rect.anchoredPosition = visual.position;
            visual.rect.localScale = visual.scale;
        }
    }

    public void SelectCategory(int index)
    {
        if (isSwitchingCategory) return;
        ApplyCategorySelection(index);
    }

    private void ApplyCategorySelection(int index)
    {
        if (index < 0 || index >= categories.Count || isFlipping || !IsSupportedCategory(categories[index])) return;

        useVirtualPagination = false;
        OnVirtualPageTurn = null;

        ToggleSpread(currentSpreadIndex, false);

        currentCategoryIndex = index;
        currentSpreadIndex = 0; 
        AlmanacTabType selectedType = categories[currentCategoryIndex].tabType;

        if (PlayerDataManager.Instance != null)
        {
            if (selectedType == AlmanacTabType.Contracts)
                PlayerDataManager.Instance.MarkContractsAlmanacRead();
            else if (selectedType == AlmanacTabType.Lessons)
                PlayerDataManager.Instance.MarkLessonsAlmanacRead();
            else if (selectedType == AlmanacTabType.Materials)
                PlayerDataManager.Instance.MarkMaterialsAlmanacRead();

        }

        if (selectedType == AlmanacTabType.Lessons ||
            selectedType == AlmanacTabType.Materials)
        {
            HideArchiveInteractionButtonContainers();
        }

        for (int i = 0; i < categories.Count; i++)
        {
            if (categories[i].tabButton == null) continue;
            
            RectTransform tabRect = categories[i].tabButton.GetComponent<RectTransform>();
            Image tabImage = categories[i].tabButton.GetComponent<Image>();

            if (i == currentCategoryIndex)
            {
                targetTabYPositions[tabRect] = originalTabYPositions[tabRect] + selectedTabUpOffset;
                if (categories[i].tabAlertIcon != null) categories[i].tabAlertIcon.SetActive(false);
                
                if (tabImage != null && categories[i].activeSprite != null) 
                {
                    tabImage.sprite = categories[i].activeSprite;
                }
            }
            else
            {
                targetTabYPositions[tabRect] = originalTabYPositions[tabRect]; 
                
                if (tabImage != null && categories[i].inactiveSprite != null) 
                {
                    tabImage.sprite = categories[i].inactiveSprite;
                }
            }
        }

        ToggleSpread(currentSpreadIndex, true);
        
        OnCategoryChanged?.Invoke(currentCategoryIndex);
        UpdatePaginationButtons();
    }

    private void CaptureArchiveInteractionButtonContainers()
    {
        archiveInteractionButtonStates.Clear();
        foreach (Transform candidate in FindObjectsOfType<Transform>(true))
        {
            if (candidate == null || !candidate.gameObject.scene.IsValid() ||
                candidate.name != "InteractionButtonContainer")
                continue;

            archiveInteractionButtonStates[candidate.gameObject] = candidate.gameObject.activeSelf;
        }
    }

    private void HideArchiveInteractionButtonContainers()
    {
        foreach (GameObject container in archiveInteractionButtonStates.Keys)
        {
            if (container != null) container.SetActive(false);
        }
    }

    private void RestoreArchiveInteractionButtonContainers()
    {
        foreach (KeyValuePair<GameObject, bool> state in archiveInteractionButtonStates)
        {
            if (state.Key != null) state.Key.SetActive(state.Value);
        }
        archiveInteractionButtonStates.Clear();
    }

    private static bool IsSupportedCategory(AlmanacCategory category)
    {
        if (category == null) return false;
        return category.tabType == AlmanacTabType.General ||
               category.tabType == AlmanacTabType.Contracts ||
               category.tabType == AlmanacTabType.Lessons ||
               category.tabType == AlmanacTabType.Materials;
    }

    private void TurnPage(bool goingForward)
    {
        if (isSwitchingCategory) return;

        if (useVirtualPagination)
        {
            OnVirtualPageTurn?.Invoke(goingForward);
            return;
        }

        if (isFlipping) return;

        AlmanacCategory currentCat = categories[currentCategoryIndex];
        int maxSpreads = Mathf.Max(currentCat.leftPages.Count, currentCat.rightPages.Count);

        if (goingForward && currentSpreadIndex < maxSpreads - 1)
        {
            StartCoroutine(FlipPageRoutine(true));
        }
        else if (!goingForward && currentSpreadIndex > 0)
        {
            StartCoroutine(FlipPageRoutine(false));
        }
    }

    private IEnumerator FlipPageRoutine(bool goingForward)
    {
        isFlipping = true;
        AlmanacCategory currentCat = categories[currentCategoryIndex];
        int nextSpread = currentSpreadIndex + (goingForward ? 1 : -1);

        bool leftChanges = GetClampedIndex(currentCat.leftPages, currentSpreadIndex) != GetClampedIndex(currentCat.leftPages, nextSpread);
        bool rightChanges = GetClampedIndex(currentCat.rightPages, currentSpreadIndex) != GetClampedIndex(currentCat.rightPages, nextSpread);

        GameObject oldLeft = GetClampedPage(currentCat.leftPages, currentSpreadIndex);
        GameObject oldRight = GetClampedPage(currentCat.rightPages, currentSpreadIndex);
        
        GameObject newLeft = GetClampedPage(currentCat.leftPages, nextSpread);
        GameObject newRight = GetClampedPage(currentCat.rightPages, nextSpread);

        GameObject liftingPage = null;
        if (goingForward && rightChanges) liftingPage = oldRight; 
        else if (!goingForward && leftChanges) liftingPage = oldLeft; 

        if (liftingPage != null)
        {
            float elapsed = 0f;
            float targetAngle = goingForward ? 90f : -90f;

            while (elapsed < flipDuration)
            {
                elapsed += Time.deltaTime;
                liftingPage.transform.localRotation = Quaternion.Euler(0, Mathf.Lerp(0, targetAngle, elapsed / flipDuration), 0);
                yield return null;
            }
            liftingPage.transform.localRotation = Quaternion.Euler(0, targetAngle, 0);
        }
        else yield return new WaitForSeconds(flipDuration); 

        if (leftChanges && oldLeft != null) oldLeft.SetActive(false);
        if (rightChanges && oldRight != null) oldRight.SetActive(false);

        currentSpreadIndex = nextSpread;

        if (newLeft != null) 
        { 
            newLeft.SetActive(true); 
            if (!leftChanges) newLeft.transform.localRotation = Quaternion.identity; 
        }
        if (newRight != null) 
        { 
            newRight.SetActive(true); 
            if (!rightChanges) newRight.transform.localRotation = Quaternion.identity; 
        }

        GameObject landingPage = null;
        if (goingForward && leftChanges) landingPage = newLeft;
        else if (!goingForward && rightChanges) landingPage = newRight;

        if (landingPage != null)
        {
            float elapsed = 0f;
            float startAngle = goingForward ? -90f : 90f;
            
            landingPage.transform.localRotation = Quaternion.Euler(0, startAngle, 0);

            while (elapsed < flipDuration)
            {
                elapsed += Time.deltaTime;
                landingPage.transform.localRotation = Quaternion.Euler(0, Mathf.Lerp(startAngle, 0, elapsed / flipDuration), 0);
                yield return null;
            }
            landingPage.transform.localRotation = Quaternion.identity;
        }
        else yield return new WaitForSeconds(flipDuration);

        UpdatePaginationButtons();
        isFlipping = false;
    }

    private void ToggleSpread(int spreadIndex, bool state)
    {
        AlmanacCategory currentCat = categories[currentCategoryIndex];

        GameObject leftPage = GetClampedPage(currentCat.leftPages, spreadIndex);
        GameObject rightPage = GetClampedPage(currentCat.rightPages, spreadIndex);

        if (leftPage != null) 
        {
            leftPage.SetActive(state);
            if (state) leftPage.transform.localRotation = Quaternion.identity;
        }
        if (rightPage != null) 
        {
            rightPage.SetActive(state);
            if (state) rightPage.transform.localRotation = Quaternion.identity;
        }
    }

    private int GetClampedIndex(List<GameObject> pageList, int requestedIndex)
    {
        if (pageList == null || pageList.Count == 0) return -1;
        return Mathf.Clamp(requestedIndex, 0, pageList.Count - 1);
    }

    private GameObject GetClampedPage(List<GameObject> pageList, int requestedIndex)
    {
        int safeIndex = GetClampedIndex(pageList, requestedIndex);
        if (safeIndex != -1) return pageList[safeIndex];
        return null; 
    }

    public void EnableVirtualPagination(System.Action<bool> callback)
    {
        useVirtualPagination = true;
        OnVirtualPageTurn = callback;
    }

    public void DisableVirtualPagination(System.Action<bool> callback)
    {
        if (OnVirtualPageTurn == callback)
        {
            useVirtualPagination = false;
            OnVirtualPageTurn = null;
            ForceUpdatePaginationUI();
        }
    }

    public void ForceUpdatePaginationUI()
    {
        UpdatePaginationButtons();
    }

    private void UpdatePaginationButtons()
    {
        if (useVirtualPagination)
        {
            SetPaginationButtonState(prevButton, virtualHasPrev);
            SetPaginationButtonState(nextButton, virtualHasNext);
            return;
        }

        if (categories == null || categories.Count == 0 ||
            currentCategoryIndex < 0 || currentCategoryIndex >= categories.Count)
        {
            SetPaginationButtonState(prevButton, false);
            SetPaginationButtonState(nextButton, false);
            return;
        }

        AlmanacCategory currentCat = categories[currentCategoryIndex];
        int maxSpreads = Mathf.Max(currentCat.leftPages.Count, currentCat.rightPages.Count);

        SetPaginationButtonState(prevButton, currentSpreadIndex > 0);
        SetPaginationButtonState(nextButton, currentSpreadIndex < maxSpreads - 1);
    }

    private static void SetPaginationButtonState(Button button, bool available)
    {
        if (button == null) return;
        button.interactable = available;
        if (button.gameObject.activeSelf != available)
            button.gameObject.SetActive(available);
    }
}
