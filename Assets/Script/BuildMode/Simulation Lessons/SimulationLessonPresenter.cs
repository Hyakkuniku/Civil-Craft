using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class SimulationLessonPresenter : MonoBehaviour
{
    private const string GenericLessonResourcePath =
        "Simulation Lessons/Generic Bridge Lesson";

    [SerializeField, HideInInspector] private int hierarchyAuthoringVersion;

    private enum MessagePriority
    {
        Information = 0,
        Warning = 1,
        Outcome = 2
    }

    private struct PendingMessage
    {
        public string text;
        public MessagePriority priority;

        public PendingMessage(string text, MessagePriority priority)
        {
            this.text = text;
            this.priority = priority;
        }
    }

    [Header("Scene-authored UI")]
    [SerializeField] private RectTransform lessonPanel;
    [SerializeField] private CanvasGroup lessonCanvasGroup;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI messageText;

    [Header("Simulation lesson controls")]
    [SerializeField] private Button lessonMenuButton;
    [SerializeField] private Button lessonCloseButton;

    private readonly List<PendingMessage> pendingMessages = new List<PendingMessage>();
    private BridgePhysicsManager physicsManager;
    private SimulationLessonDefinition definition;
    private BuildLocation lessonLocation;
    private LiveLoadVehicle activeVehicle;
    private Coroutine presentationRoutine;
    private Vector2 visiblePanelPosition;
    private MessagePriority currentPriority;
    private string currentMessage;
    private float lastVehicleProgress;
    private float lastVehicleMovementTime;
    private bool lessonActive;
    private bool liveLoadMessageQueued;
    private bool vehicleEntryMessageQueued;
    private bool midpointMessageQueued;
    private bool highStressMessageQueued;
    private bool stuckMessageQueued;
    private bool terminalFailure;
    private bool successConfirmed;
    private float outcomePresentedAt = float.NegativeInfinity;
    private bool lessonPanelOpen;
    private bool slowMotionApplied;
    private float timeScaleBeforeLesson = 1f;
    private float appliedLessonTimeScale = 1f;

    /// <summary>
    /// Returns the time the active result flow must still wait before replacing
    /// the lesson with its completion/failure UI. A dismissed lesson returns
    /// zero so an optional hidden panel never slows normal progression.
    /// </summary>
    public static float GetRemainingOutcomeReadTime(BuildLocation location)
    {
        if (location == null) return 0f;

        SimulationLessonPresenter presenter = BuildUIController.Instance != null
            ? BuildUIController.Instance.GetComponent<SimulationLessonPresenter>()
            : FindObjectOfType<SimulationLessonPresenter>();
        if (presenter == null || !presenter.lessonPanelOpen || !presenter.lessonActive ||
            presenter.lessonLocation != location || presenter.definition == null ||
            float.IsNegativeInfinity(presenter.outcomePresentedAt)) return 0f;

        float requiredTime = presenter.definition.fadeDuration +
            presenter.definition.minimumMessageDuration;
        return Mathf.Max(0f, requiredTime - (Time.unscaledTime - presenter.outcomePresentedAt));
    }

    /// <summary>
    /// Result screens own the foreground once their outcome-reading delay has
    /// elapsed. Clear the optional lesson UI and restore any lesson slow motion
    /// before the completion or failure overlay is shown.
    /// </summary>
    public static void HideForResultOverlay()
    {
        SimulationLessonPresenter presenter = BuildUIController.Instance != null
            ? BuildUIController.Instance.GetComponent<SimulationLessonPresenter>()
            : FindObjectOfType<SimulationLessonPresenter>();

        if (presenter != null) presenter.ClearLesson();
    }

    public void Configure(
        RectTransform panel,
        CanvasGroup canvasGroup,
        TextMeshProUGUI title,
        TextMeshProUGUI message,
        int authoringVersion = 0)
    {
        lessonPanel = panel;
        lessonCanvasGroup = canvasGroup;
        titleText = title;
        messageText = message;
        hierarchyAuthoringVersion = authoringVersion;
    }

    public void ConfigureControls(
        Button menuButton,
        Button closeButton,
        int authoringVersion = 0)
    {
        lessonMenuButton = menuButton;
        lessonCloseButton = closeButton;
        hierarchyAuthoringVersion = authoringVersion;
    }

    private void Awake()
    {
        EnsurePanelIsUnderBuildCanvas();
        if (lessonMenuButton != null)
        {
            lessonMenuButton.onClick.AddListener(OpenLessonPanel);
            lessonMenuButton.gameObject.SetActive(false);
        }
        if (lessonCloseButton != null) lessonCloseButton.onClick.AddListener(CloseLessonPanel);

        if (lessonPanel != null)
        {
            visiblePanelPosition = lessonPanel.anchoredPosition;
            lessonPanel.gameObject.SetActive(false);
        }
    }

    private void EnsurePanelIsUnderBuildCanvas()
    {
        if (lessonPanel == null || lessonPanel.GetComponentInParent<Canvas>(true) != null) return;

        BuildUIController controller = GetComponent<BuildUIController>();
        Canvas canvas = null;
        if (controller != null && controller.actionLogText != null)
            canvas = controller.actionLogText.GetComponentInParent<Canvas>(true);
        if (canvas == null && controller != null && controller.simulationControlsPanel != null)
            canvas = controller.simulationControlsPanel.GetComponentInParent<Canvas>(true);
        if (canvas == null && controller != null && controller.playSimulationButtonObject != null)
            canvas = controller.playSimulationButtonObject.GetComponentInParent<Canvas>(true);
        if (canvas == null) return;

        // This is a repair fallback for older authored scenes. It never creates
        // UI; the panel remains the serialized scene object authored in advance.
        lessonPanel.SetParent(canvas.transform, false);
        lessonPanel.SetAsLastSibling();
    }

    private void OnEnable()
    {
        BindPhysicsManager();
        LevelFailedManager.SimulationFailed += HandleSimulationFailed;
        LevelCompleteManager.SimulationSucceeded += HandleSimulationSucceeded;
    }

    private void Start()
    {
        BindPhysicsManager();
    }

    private void OnDisable()
    {
        UnbindPhysicsManager();
        LevelFailedManager.SimulationFailed -= HandleSimulationFailed;
        LevelCompleteManager.SimulationSucceeded -= HandleSimulationSucceeded;
        ClearLesson();
    }

    private void OnDestroy()
    {
        RestoreSimulationSpeed();
        if (lessonMenuButton != null) lessonMenuButton.onClick.RemoveListener(OpenLessonPanel);
        if (lessonCloseButton != null) lessonCloseButton.onClick.RemoveListener(CloseLessonPanel);
        UnbindPhysicsManager();
        LevelFailedManager.SimulationFailed -= HandleSimulationFailed;
        LevelCompleteManager.SimulationSucceeded -= HandleSimulationSucceeded;
    }

    private void Update()
    {
        if (physicsManager == null)
        {
            BindPhysicsManager();
            return;
        }

        if (!lessonActive) return;

        BuildLocation currentLocation = GameManager.Instance != null
            ? GameManager.Instance.ActiveBuildLocation
            : null;
        if (currentLocation != lessonLocation ||
            GameManager.Instance == null ||
            !GameManager.Instance.IsInBuildMode())
        {
            ClearLesson();
            return;
        }

        if (!physicsManager.isSimulating || terminalFailure || successConfirmed) return;

        ObserveStress();
        ObserveVehicle();
    }

    private void BindPhysicsManager()
    {
        BridgePhysicsManager candidate = BuildUIController.Instance != null
            ? BuildUIController.Instance.physicsManager
            : null;
        if (candidate == null) candidate = FindObjectOfType<BridgePhysicsManager>();
        if (candidate == physicsManager) return;

        UnbindPhysicsManager();
        physicsManager = candidate;
        if (physicsManager == null) return;

        physicsManager.OnSettlePhaseStarted += HandleSettleStarted;
        physicsManager.OnSimulationStarted += HandleSimulationStarted;
        physicsManager.OnSimulationStopped += HandleSimulationStopped;
        physicsManager.OnFirstMemberBroken += HandleFirstMemberBroken;
    }

    private void UnbindPhysicsManager()
    {
        if (physicsManager == null) return;
        physicsManager.OnSettlePhaseStarted -= HandleSettleStarted;
        physicsManager.OnSimulationStarted -= HandleSimulationStarted;
        physicsManager.OnSimulationStopped -= HandleSimulationStopped;
        physicsManager.OnFirstMemberBroken -= HandleFirstMemberBroken;
        physicsManager = null;
    }

    private void HandleSettleStarted()
    {
        BeginLessonForActiveLocation();
    }

    private void HandleSimulationStarted()
    {
        if (!lessonActive) BeginLessonForActiveLocation();
        if (!lessonActive || definition == null || liveLoadMessageQueued) return;

        liveLoadMessageQueued = true;
        QueueMessage(definition.liveLoadStarted, MessagePriority.Information);
        activeVehicle = FindVehicleForCurrentContract();
        if (activeVehicle != null)
        {
            lastVehicleProgress = activeVehicle.NormalizedRouteProgress;
            lastVehicleMovementTime = Time.time;
        }
    }

    private void HandleSimulationStopped()
    {
        // Stopping, resetting, retrying, and returning to editing all use this
        // same signal. The panel is cleared rather than left over the editor.
        ClearLesson();
    }

    private void BeginLessonForActiveLocation()
    {
        BuildLocation location = GameManager.Instance != null
            ? GameManager.Instance.ActiveBuildLocation
            : null;
        if (location == null)
        {
            ClearLesson();
            return;
        }

        SimulationLessonDefinition resolvedDefinition = ResolveDefinition(location);
        if (resolvedDefinition == null)
        {
            ClearLesson();
            return;
        }

        ClearLesson();
        lessonLocation = location;
        definition = resolvedDefinition;
        lessonActive = true;
        activeVehicle = FindVehicleForCurrentContract();
        lastVehicleProgress = activeVehicle != null ? activeVehicle.NormalizedRouteProgress : 0f;
        lastVehicleMovementTime = Time.time;

        if (titleText != null) titleText.text = definition.panelTitle;
        currentMessage = definition.simulationStarted;
        currentPriority = MessagePriority.Information;
        if (messageText != null) messageText.text = currentMessage;

        // A deliberately authored lesson is part of the location's experience,
        // so it opens by default. The generic fallback remains optional and is
        // available from the lesson button for the duration of the simulation.
        if (location.simulationLesson != null)
            OpenLessonPanel();
        else
            RefreshControlVisibility();
    }

    private static SimulationLessonDefinition ResolveDefinition(BuildLocation location)
    {
        if (location != null && location.simulationLesson != null)
            return location.simulationLesson;
        return Resources.Load<SimulationLessonDefinition>(GenericLessonResourcePath);
    }

    public void OpenLessonPanel()
    {
        if (!lessonActive || definition == null || lessonPanel == null) return;

        lessonPanelOpen = true;
        if (lessonMenuButton != null) lessonMenuButton.gameObject.SetActive(false);
        if (titleText != null) titleText.text = definition.panelTitle;
        if (messageText != null && !string.IsNullOrWhiteSpace(currentMessage))
            messageText.text = currentMessage;

        if (presentationRoutine != null) StopCoroutine(presentationRoutine);
        lessonPanel.gameObject.SetActive(true);
        lessonCanvasGroup.interactable = true;
        lessonCanvasGroup.blocksRaycasts = true;
        ApplyLessonSlowMotion();
        presentationRoutine = StartCoroutine(AnimatePanelOpen());
    }

    public void CloseLessonPanel()
    {
        if (!lessonPanelOpen) return;
        lessonPanelOpen = false;
        RestoreSimulationSpeed();
        if (presentationRoutine != null) StopCoroutine(presentationRoutine);
        presentationRoutine = StartCoroutine(AnimatePanelClosed());
    }

    private void RefreshControlVisibility()
    {
        bool simulationActive = lessonActive && physicsManager != null && physicsManager.IsSimulationActive;
        if (lessonMenuButton != null)
            lessonMenuButton.gameObject.SetActive(simulationActive && !lessonPanelOpen);
    }

    private void ObserveStress()
    {
        if (highStressMessageQueued || physicsManager == null || definition == null) return;
        if (physicsManager.GetMaxBridgeStress() < definition.highStressThreshold) return;

        highStressMessageQueued = true;
        QueueMessage(definition.highStressDetected, MessagePriority.Warning);
    }

    private void ObserveVehicle()
    {
        if (definition == null) return;
        if (activeVehicle == null) activeVehicle = FindVehicleForCurrentContract();
        if (activeVehicle == null) return;

        float progress = activeVehicle.NormalizedRouteProgress;
        if (!vehicleEntryMessageQueued && progress >= definition.vehicleEnteredProgress)
        {
            vehicleEntryMessageQueued = true;
            QueueMessage(definition.vehicleEnteredBridge, MessagePriority.Information);
        }

        if (!midpointMessageQueued && progress >= definition.vehicleMidpointProgress)
        {
            midpointMessageQueued = true;
            QueueMessage(definition.vehicleReachedMidpoint, MessagePriority.Information);
        }

        if (progress > lastVehicleProgress + 0.003f || activeVehicle.CurrentSpeed > 0.12f)
        {
            lastVehicleProgress = Mathf.Max(lastVehicleProgress, progress);
            lastVehicleMovementTime = Time.time;
        }

        bool isBetweenEndpoints = progress >= definition.vehicleEnteredProgress && progress < 0.95f;
        if (!stuckMessageQueued && activeVehicle.IsDriving && isBetweenEndpoints &&
            Time.time - lastVehicleMovementTime >= definition.vehicleStuckDelay)
        {
            stuckMessageQueued = true;
            QueueMessage(definition.vehicleStuck, MessagePriority.Warning, true);
        }
    }

    private void HandleFirstMemberBroken(BarStressHandler brokenMember)
    {
        if (!lessonActive || definition == null || successConfirmed) return;
        terminalFailure = true;
        pendingMessages.Clear();
        PresentImmediately(definition.firstMemberBreak, MessagePriority.Outcome);
    }

    private void HandleSimulationFailed(string reason)
    {
        if (!lessonActive || definition == null || successConfirmed) return;

        terminalFailure = true;
        pendingMessages.Clear();
        bool vehicleFailure = !string.IsNullOrWhiteSpace(reason) &&
            (reason.IndexOf("vehicle", StringComparison.OrdinalIgnoreCase) >= 0 ||
             reason.IndexOf("ravine", StringComparison.OrdinalIgnoreCase) >= 0);
        string message;
        if (vehicleFailure) message = definition.vehicleFailed;
        else if (physicsManager != null && physicsManager.HadBrokenPartsThisRun)
            message = definition.firstMemberBreak;
        else message = definition.stressLimitExceeded;
        PresentImmediately(message, MessagePriority.Outcome);
    }

    private void HandleSimulationSucceeded(ContractSO contract, BuildLocation location)
    {
        if (!lessonActive || definition == null || terminalFailure) return;
        if (location != null && location != lessonLocation) return;
        if (GameManager.Instance != null && contract != null && GameManager.Instance.CurrentContract != contract) return;

        successConfirmed = true;
        pendingMessages.Clear();
        PresentImmediately(definition.bridgeSucceeded, MessagePriority.Outcome);
    }

    private LiveLoadVehicle FindVehicleForCurrentContract()
    {
        ContractSO contract = GameManager.Instance != null ? GameManager.Instance.CurrentContract : null;
        return LiveLoadVehicle.FindActiveForContract(contract);
    }

    private void QueueMessage(string text, MessagePriority priority, bool interruptEqualPriority = false)
    {
        if (!lessonActive || string.IsNullOrWhiteSpace(text)) return;
        if (terminalFailure && priority != MessagePriority.Outcome) return;

        // Keep the lesson current even while the optional panel is closed. On
        // reopening, the player sees what is happening now rather than a stale
        // message that was visible when they dismissed it.
        if (!lessonPanelOpen)
        {
            pendingMessages.Clear();
            currentMessage = text;
            currentPriority = priority;
            if (messageText != null) messageText.text = text;
            return;
        }

        if (lessonPanel == null || !lessonPanel.gameObject.activeSelf ||
            priority > currentPriority || (interruptEqualPriority && priority == currentPriority))
        {
            PresentImmediately(text, priority);
            return;
        }

        if (text == currentMessage || pendingMessages.Exists(item => item.text == text)) return;
        pendingMessages.Add(new PendingMessage(text, priority));
    }

    private void PresentImmediately(string text, MessagePriority priority)
    {
        if (string.IsNullOrWhiteSpace(text) || lessonPanel == null ||
            lessonCanvasGroup == null || messageText == null) return;

        if (priority == MessagePriority.Outcome)
            outcomePresentedAt = Time.unscaledTime;

        if (presentationRoutine != null) StopCoroutine(presentationRoutine);
        currentMessage = text;
        currentPriority = priority;
        messageText.text = text;
        if (!lessonPanelOpen) return;
        lessonPanel.gameObject.SetActive(true);
        presentationRoutine = StartCoroutine(PresentMessageRoutine());
    }

    private IEnumerator AnimatePanelOpen()
    {
        float duration = definition != null ? definition.fadeDuration : 0.2f;
        float slideDistance = definition != null ? definition.slideDistance : 24f;
        Vector2 hiddenPosition = visiblePanelPosition + Vector2.left * slideDistance;
        lessonCanvasGroup.alpha = 0f;
        lessonPanel.anchoredPosition = hiddenPosition;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = duration <= 0f ? 1f : Mathf.Clamp01(elapsed / duration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            lessonCanvasGroup.alpha = eased;
            lessonPanel.anchoredPosition = Vector2.LerpUnclamped(hiddenPosition, visiblePanelPosition, eased);
            yield return null;
        }

        lessonCanvasGroup.alpha = 1f;
        lessonPanel.anchoredPosition = visiblePanelPosition;
        presentationRoutine = StartCoroutine(PresentMessageRoutine(false));
    }

    private IEnumerator AnimatePanelClosed()
    {
        float duration = definition != null ? definition.fadeDuration : 0.2f;
        float slideDistance = definition != null ? definition.slideDistance : 24f;
        Vector2 hiddenPosition = visiblePanelPosition + Vector2.left * slideDistance;
        Vector2 startPosition = lessonPanel.anchoredPosition;
        float startAlpha = lessonCanvasGroup.alpha;
        lessonCanvasGroup.interactable = false;
        lessonCanvasGroup.blocksRaycasts = false;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = duration <= 0f ? 1f : Mathf.Clamp01(elapsed / duration);
            float eased = t * t;
            lessonCanvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, eased);
            lessonPanel.anchoredPosition = Vector2.LerpUnclamped(startPosition, hiddenPosition, eased);
            yield return null;
        }

        lessonCanvasGroup.alpha = 0f;
        lessonPanel.anchoredPosition = visiblePanelPosition;
        lessonPanel.gameObject.SetActive(false);
        presentationRoutine = null;
        RefreshControlVisibility();
    }

    private IEnumerator PresentMessageRoutine(bool animateEntrance = true)
    {
        float fadeDuration = definition != null ? definition.fadeDuration : 0.2f;
        float slideDistance = definition != null ? definition.slideDistance : 24f;
        float displayDuration = definition != null ? definition.minimumMessageDuration : 3.5f;
        Vector2 hiddenPosition = visiblePanelPosition + Vector2.left * slideDistance;

        lessonCanvasGroup.alpha = animateEntrance ? 0f : 1f;
        lessonPanel.anchoredPosition = animateEntrance ? hiddenPosition : visiblePanelPosition;
        float elapsed = 0f;
        while (animateEntrance && elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = fadeDuration <= 0f ? 1f : Mathf.Clamp01(elapsed / fadeDuration);
            lessonCanvasGroup.alpha = t;
            lessonPanel.anchoredPosition = Vector2.Lerp(hiddenPosition, visiblePanelPosition, t);
            yield return null;
        }

        lessonCanvasGroup.alpha = 1f;
        lessonPanel.anchoredPosition = visiblePanelPosition;
        elapsed = 0f;
        while (elapsed < displayDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        if (pendingMessages.Count > 0)
        {
            int nextIndex = 0;
            for (int i = 1; i < pendingMessages.Count; i++)
                if (pendingMessages[i].priority > pendingMessages[nextIndex].priority) nextIndex = i;

            PendingMessage next = pendingMessages[nextIndex];
            pendingMessages.RemoveAt(nextIndex);
            currentMessage = next.text;
            currentPriority = next.priority;
            messageText.text = next.text;
            presentationRoutine = StartCoroutine(PresentMessageRoutine());
            yield break;
        }

        // The panel remains visible until the player presses its close button.
        lessonCanvasGroup.alpha = 1f;
        lessonPanel.anchoredPosition = visiblePanelPosition;
        presentationRoutine = null;
    }

    private void ClearLesson()
    {
        RestoreSimulationSpeed();
        if (presentationRoutine != null) StopCoroutine(presentationRoutine);
        presentationRoutine = null;
        pendingMessages.Clear();
        currentMessage = null;
        definition = null;
        lessonLocation = null;
        activeVehicle = null;
        lessonActive = false;
        lessonPanelOpen = false;
        liveLoadMessageQueued = false;
        vehicleEntryMessageQueued = false;
        midpointMessageQueued = false;
        highStressMessageQueued = false;
        stuckMessageQueued = false;
        terminalFailure = false;
        successConfirmed = false;
        outcomePresentedAt = float.NegativeInfinity;

        if (lessonCanvasGroup != null) lessonCanvasGroup.alpha = 0f;
        if (lessonCanvasGroup != null)
        {
            lessonCanvasGroup.interactable = false;
            lessonCanvasGroup.blocksRaycasts = false;
        }
        if (lessonMenuButton != null) lessonMenuButton.gameObject.SetActive(false);
        if (lessonPanel != null)
        {
            lessonPanel.anchoredPosition = visiblePanelPosition;
            lessonPanel.gameObject.SetActive(false);
        }
    }

    private void ApplyLessonSlowMotion()
    {
        if (slowMotionApplied || definition == null || !definition.enableSlowMotion ||
            physicsManager == null || !physicsManager.IsSimulationActive || Time.timeScale <= 0f)
        {
            return;
        }

        timeScaleBeforeLesson = Time.timeScale;
        appliedLessonTimeScale = timeScaleBeforeLesson *
            Mathf.Clamp(definition.simulationTimeScale, 0.1f, 1f);

        // Deliberately leave Time.fixedDeltaTime untouched. PhysX continues to
        // solve the exact same fixed-size steps and deterministic stress samples;
        // those steps are simply presented less frequently in wall-clock time.
        Time.timeScale = appliedLessonTimeScale;
        slowMotionApplied = true;
    }

    private void RestoreSimulationSpeed()
    {
        if (!slowMotionApplied) return;

        // Do not overwrite a pause, debug speed, or another system's later
        // change. Restore only when our own scale is still the active value.
        if (Mathf.Approximately(Time.timeScale, appliedLessonTimeScale))
            Time.timeScale = timeScaleBeforeLesson;

        slowMotionApplied = false;
        timeScaleBeforeLesson = 1f;
        appliedLessonTimeScale = 1f;
    }
}
