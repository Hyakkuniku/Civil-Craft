using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic; 
using System;
using UnityEngine.UI;

public class LevelFailedManager : MonoBehaviour
{
    public static LevelFailedManager Instance { get; private set; }
    public static event Action<string> SimulationFailed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSimulationFailureEvent()
    {
        SimulationFailed = null;
    }

    [Header("UI References")]
    [Tooltip("Drag the Level Failed Panel here.")]
    public GameObject levelFailedPanel;
    
    [Tooltip("Drag the Text element that will display the level/contract name here.")]
    public TextMeshProUGUI levelNameText;

    [Tooltip("Drag the Text element that will display the gold penalty here.")]
    public TextMeshProUGUI penaltyText; 

    [Header("Failure Diagnosis UI")]
    [Tooltip("Detailed explanation of what caused the failed test. Created automatically when omitted.")]
    public TextMeshProUGUI failureDetailText;

    [Tooltip("Actionable bridge-design advice for the detected failure. Created automatically when omitted.")]
    public TextMeshProUGUI failureRecommendationText;

    // --- NEW: Reference to the Retry Button ---
    [Tooltip("Drag the Retry Button here so we can disable it for locked contracts.")]
    public GameObject retryButton; 

    [Header("Gameplay Elements to Hide")]
    [Tooltip("UI elements to hide when this panel is open (e.g., Crosshair, HUD)")]
    public List<GameObject> uiElementsToHide = new List<GameObject>(); 
    private List<GameObject> temporarilyHiddenPanels = new List<GameObject>(); 

    [Header("Failure Settings")]
    [Tooltip("The Y-axis height at which the vehicle is considered fallen/destroyed.")]
    public float deathThreshold = -15f;

    [Tooltip("The vehicle also fails after falling this far below its own start point. This keeps fall detection reliable in scenes that use different world heights.")]
    [Min(1f)] public float maximumVehicleFallDistance = 6f;
    
    [Tooltip("How long to wait before showing the fail screen (lets the player watch the destruction).")]
    public float delayBeforeFailScreen = 2.0f; 

    [Header("Penalty Tracking")]
    [Tooltip("How much gold is deducted from the final reward EVERY time the bridge collapses?")]
    public int goldPenaltyPerFail = 25;
    [HideInInspector] public int currentFailCount = 0;

    private LiveLoadVehicle activeVehicle;
    private BridgePhysicsManager physicsManager;
    private Coroutine failDelayCoroutine;
    private BuildLocation tutorialLocationToRestart;
    private float activeVehicleStartY;
    private bool hasVehicleStartHeight;
    private bool vehicleWasPresentForSimulation;
    
    [HideInInspector] public bool isFailed = false;

    // --- NEW: Flag to track if we should hide the retry button this specific time ---
    private bool hideRetryButtonThisFail = false;
    private bool showExitButtonThisFail;
    private TextMeshProUGUI contractNameText;
    private GameObject exitButton;
    private RectTransform failureExitButtonRect;
    private RectTransform failureRetryButtonRect;

    private readonly struct FailurePresentation
    {
        public readonly string Summary;
        public readonly string Detail;
        public readonly string Recommendation;

        public FailurePresentation(string summary, string detail, string recommendation)
        {
            Summary = summary;
            Detail = detail;
            Recommendation = recommendation;
        }
    }

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        if (levelFailedPanel != null)
        {
            // Build while inactive so the entrance animation starts only when a
            // real failure opens the panel, never during scene initialization.
            levelFailedPanel.SetActive(false);
            BuildFailureLayout();
        }
    }

    private void Start()
    {
        physicsManager = FindObjectOfType<BridgePhysicsManager>();
        
        if (physicsManager != null)
        {
            physicsManager.OnSimulationStarted += HandleSimulationStarted;
            physicsManager.OnSimulationStopped += HandleSimulationStopped;
        }
    }

    private void OnDestroy()
    {
        if (physicsManager != null)
        {
            physicsManager.OnSimulationStarted -= HandleSimulationStarted;
            physicsManager.OnSimulationStopped -= HandleSimulationStopped;
        }
    }

    private void Update()
    {
        if (isFailed) return;

        if (physicsManager != null && physicsManager.isSimulating)
        {
            float stressThreshold = 1.0f; 
            string stressFailReason = "Bridge Collapsed!";

            if (GameManager.Instance != null && GameManager.Instance.CurrentContract != null)
            {
                ContractSO contract = GameManager.Instance.CurrentContract;
                if (contract.enforceMaxStress)
                {
                    stressThreshold = contract.maxAllowedStress / 100f; 
                    stressFailReason = $"Challenge Failed: Stress exceeded {contract.maxAllowedStress}%!";
                }
            }

            if (!BridgePhysicsManager.DebugInvincibleBridge &&
                physicsManager.peakStressThisRun >= stressThreshold)
            {
                InitiateFailure(stressFailReason);
                return; 
            }

            if (GameManager.Instance != null && GameManager.Instance.IsCargoTestActive) return;
            if (!IsVehicleForCurrentContract(activeVehicle))
                CaptureActiveVehicle(FindVehicleForCurrentContract());

            if (activeVehicle != null)
            {
                if (!activeVehicle.gameObject.activeInHierarchy)
                {
                    InitiateFailure("Vehicle Destroyed!");
                    return;
                }

                float relativeDeathThreshold = hasVehicleStartHeight
                    ? activeVehicleStartY - Mathf.Max(1f, maximumVehicleFallDistance)
                    : float.NegativeInfinity;
                float effectiveDeathThreshold = Mathf.Max(deathThreshold, relativeDeathThreshold);

                if (activeVehicle.transform.position.y < effectiveDeathThreshold)
                {
                    InitiateFailure("Vehicle Fell Into the Ravine!");
                    return;
                }
            }
            else if (vehicleWasPresentForSimulation)
            {
                // A destroyed GameObject compares equal to null in Unity. Once a
                // test vehicle was registered, losing it is itself a failed test.
                InitiateFailure("Vehicle Destroyed!");
            }
        }
    }

    private void HandleSimulationStarted()
    {
        activeVehicle = null;
        hasVehicleStartHeight = false;
        vehicleWasPresentForSimulation = false;
        if (GameManager.Instance != null && GameManager.Instance.IsCargoTestActive) return;
        CaptureActiveVehicle(FindVehicleForCurrentContract());
    }

    private void CaptureActiveVehicle(LiveLoadVehicle vehicle)
    {
        activeVehicle = vehicle;
        if (activeVehicle == null) return;

        vehicleWasPresentForSimulation = true;
        activeVehicleStartY = activeVehicle.startPoint != null
            ? activeVehicle.startPoint.position.y
            : activeVehicle.transform.position.y;
        hasVehicleStartHeight = true;
    }

    private static bool IsVehicleForCurrentContract(LiveLoadVehicle vehicle)
    {
        if (vehicle == null || !vehicle.gameObject.activeInHierarchy) return false;

        ContractSO currentContract = GameManager.Instance != null
            ? GameManager.Instance.CurrentContract
            : null;

        return ContractsMatch(vehicle.assignedContract, currentContract);
    }

    private static LiveLoadVehicle FindVehicleForCurrentContract()
    {
        ContractSO currentContract = GameManager.Instance != null
            ? GameManager.Instance.CurrentContract
            : null;
        if (currentContract == null) return null;

        foreach (LiveLoadVehicle vehicle in FindObjectsOfType<LiveLoadVehicle>(true))
        {
            if (vehicle == null || !vehicle.gameObject.scene.IsValid() ||
                !vehicle.gameObject.activeInHierarchy)
                continue;

            if (ContractsMatch(vehicle.assignedContract, currentContract))
                return vehicle;
        }

        return null;
    }

    private static bool ContractsMatch(ContractSO left, ContractSO right)
    {
        if (left == null || right == null) return false;
        if (left == right) return true;
        return !string.IsNullOrWhiteSpace(left.ContractID) &&
               string.Equals(left.ContractID, right.ContractID, System.StringComparison.Ordinal);
    }

    private void InitiateFailure(string reason)
    {
        if (GameManager.Instance != null && GameManager.Instance.IsCargoTestActive)
        {
            GameManager.Instance.CancelCargoTest();
            return;
        }
        if (isFailed) return;
        isFailed = true; 
        SimulationFailed?.Invoke(reason);

        if (failDelayCoroutine != null) StopCoroutine(failDelayCoroutine);
        failDelayCoroutine = StartCoroutine(FailDelayRoutine(reason));
    }

    private IEnumerator FailDelayRoutine(string reason)
    {
        yield return new WaitForSeconds(delayBeforeFailScreen);

        BuildLocation failedLocation = GameManager.Instance != null
            ? GameManager.Instance.ActiveBuildLocation
            : null;
        // Preserve the existing destruction-view delay, then wait only for any
        // unread portion of the lesson outcome before covering it with the fail UI.
        while (SimulationLessonPresenter.GetRemainingOutcomeReadTime(failedLocation) > 0f)
            yield return null;

        ShowFailScreen(reason);
    }

    // --- THE FIX: Added the 'hideRetry' parameter to match BuildLocation! ---
    public void TriggerLevelFailed(string failureReason = "", bool hideRetry = false)
    {
        hideRetryButtonThisFail = hideRetry;
        InitiateFailure(failureReason);
    }

    private void ShowFailScreen(string failureReason)
    {
        bool isTutorial = false;
        bool isCompletedContractRedesign = false;
        
        if (GameManager.Instance != null && GameManager.Instance.CurrentContract != null)
        {
            ContractSO contract = GameManager.Instance.CurrentContract;
            isTutorial = contract.IsTutorialForCurrentPlayer();
            BuildLocation activeLocation = GameManager.Instance.ActiveBuildLocation;
            bool isTutorialReplay = activeLocation != null && activeLocation.IsTutorialReplayActive;
            bool tutorialAlreadyPassed = LevelCompleteManager.Instance != null &&
                LevelCompleteManager.Instance.HasPassedTutorialTestThisSession(contract);
            isCompletedContractRedesign = PlayerDataManager.Instance != null &&
                PlayerDataManager.Instance.HasContractCompletionRecord(contract.ContractID);

            tutorialLocationToRestart = !hideRetryButtonThisFail &&
                ((isTutorial && !tutorialAlreadyPassed) || isTutorialReplay)
                ? activeLocation
                : null;
        }

        bool shouldApplyPenalty = !isTutorial && !isCompletedContractRedesign;
        if (shouldApplyPenalty)
        {
            currentFailCount++;
        }

        // An unfinished contract must be retried rather than abandoned from the
        // failure popup. Completed redesigns may be exited safely because their
        // previously saved bridge remains intact. A permanently locked contract
        // must also retain an exit path because retry is intentionally disabled.
        showExitButtonThisFail = isCompletedContractRedesign || hideRetryButtonThisFail;

        if (!IsVehicleForCurrentContract(activeVehicle))
            activeVehicle = FindVehicleForCurrentContract();
        if (activeVehicle != null) activeVehicle.EmergencyStop();

        temporarilyHiddenPanels.Clear();
        foreach (GameObject ui in uiElementsToHide)
        {
            if (ui != null && ui.activeSelf)
            {
                temporarilyHiddenPanels.Add(ui);
                ui.SetActive(false);
            }
        }

        FailurePresentation presentation = BuildFailurePresentation(failureReason);
        if (levelNameText != null) levelNameText.text = presentation.Summary;
        if (failureDetailText != null) failureDetailText.text = presentation.Detail;
        if (failureRecommendationText != null)
            failureRecommendationText.text = presentation.Recommendation;
        if (contractNameText != null)
        {
            ContractSO contract = GameManager.Instance != null ? GameManager.Instance.CurrentContract : null;
            string contractLabel = contract != null ? contract.name : string.Empty;
            contractNameText.text = !string.IsNullOrWhiteSpace(contractLabel)
                ? $"CONTRACT TEST  •  {contractLabel.ToUpperInvariant()}"
                : "STRUCTURAL TEST REPORT";
        }

        if (penaltyText != null)
        {
            if (isTutorial)
            {
                penaltyText.text = "<b>PRACTICE TEST</b>  •  No Gold penalty was applied.";
            }
            else if (isCompletedContractRedesign)
            {
                penaltyText.text = "<b>SAVED CONTRACT</b>  •  No penalty was applied. Your saved bridge remains unchanged.";
            }
            else
            {
                int totalLost = currentFailCount * goldPenaltyPerFail;
                penaltyText.text = $"<b>PENALTY</b>  •  -{goldPenaltyPerFail} Gold     <color=#8C3B2D>Total lost this job: -{totalLost} Gold</color>";
            }
        }

        UpdateFailureButtonLayout();
        SimulationLessonPresenter.HideForResultOverlay();
        if (levelFailedPanel != null) levelFailedPanel.SetActive(true);
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX("Level_Fail");
    }

    private FailurePresentation BuildFailurePresentation(string failureReason)
    {
        string reason = failureReason ?? string.Empty;
        string normalized = reason.ToLowerInvariant();

        if (normalized.Contains("fell") || normalized.Contains("ravine"))
        {
            return new FailurePresentation(
                "VEHICLE LEFT THE BRIDGE",
                "The live-load vehicle fell below the test route because the roadway or its supporting structure could not maintain a continuous crossing.",
                "Check road continuity, reinforce the failed span, and confirm that the load path reaches both anchors.");
        }

        if (normalized.Contains("vehicle") && normalized.Contains("destroy"))
        {
            return new FailurePresentation(
                "LIVE-LOAD VEHICLE LOST",
                "The contract vehicle was destroyed or removed before it completed the required crossing.",
                "Inspect road joints and vehicle clearance, then reinforce the area where the vehicle was lost.");
        }

        if (normalized.Contains("stress") || normalized.Contains("capacity"))
        {
            float peakStress = physicsManager != null
                ? physicsManager.GetPeakDisplayedBridgeStress() * 100f
                : 0f;
            ContractSO contract = GameManager.Instance != null ? GameManager.Instance.CurrentContract : null;
            float allowedStress = contract != null && contract.enforceMaxStress
                ? contract.maxAllowedStress
                : 100f;
            string measured = peakStress > 0.01f
                ? $" Peak bridge stress reached {peakStress:0.#}% against an allowed {allowedStress:0.#}%."
                : $" The allowed stress limit was {allowedStress:0.#}%.";

            return new FailurePresentation(
                "STRESS LIMIT EXCEEDED",
                "One or more structural members exceeded the contract's safe capacity." + measured,
                "Strengthen orange or red members, reduce long unsupported spans, and improve the load path with complete triangles.");
        }

        if (normalized.Contains("collapse") || normalized.Contains("member") || normalized.Contains("structur"))
        {
            float peakStress = physicsManager != null
                ? physicsManager.GetPeakDisplayedBridgeStress() * 100f
                : 0f;
            string measured = peakStress > 0.01f
                ? $" The test recorded a peak bridge stress of {peakStress:0.#}%."
                : string.Empty;
            return new FailurePresentation(
                "STRUCTURAL FAILURE",
                "A bridge member failed and the structure lost a continuous load path." + measured,
                "Support the failed region, reconnect separated members, and use triangular bracing to distribute the load.");
        }

        if (normalized.Contains("time") || normalized.Contains("expired"))
        {
            return new FailurePresentation(
                "CONTRACT TIME EXPIRED",
                "The contract deadline expired before the bridge completed a successful live-load test.",
                "Return to the contract list and continue with another available build location.");
        }

        string summary = string.IsNullOrWhiteSpace(reason)
            ? "BRIDGE TEST FAILED"
            : reason.Trim().TrimEnd('!', '.', ' ').ToUpperInvariant();
        return new FailurePresentation(
            summary,
            "The simulation ended because the bridge did not satisfy the contract's required test.",
            "Review stressed members, road continuity, supports, and the required live-load route before testing again.");
    }

    private void BuildFailureLayout()
    {
        if (levelFailedPanel == null) return;

        TMP_FontAsset font = levelNameText != null ? levelNameText.font : TMP_Settings.defaultFontAsset;
        foreach (Transform child in levelFailedPanel.transform) child.gameObject.SetActive(false);

        RectTransform root = levelFailedPanel.GetComponent<RectTransform>();
        if (root == null) return;
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = root.offsetMax = Vector2.zero;
        root.localScale = Vector3.one;

        Image background = root.GetComponent<Image>();
        if (background == null) background = root.gameObject.AddComponent<Image>();
        background.sprite = null;
        background.color = new Color(0.08f, 0.055f, 0.035f, 0.78f);
        background.raycastTarget = true;

        RectTransform safe = CompletionReceiptLayout.Box(root, "Failure Safe Area", 0f, 0f, 1f, 1f);
        safe.gameObject.AddComponent<CompletionSafeArea>();
        RectTransform frame = CompletionReceiptLayout.Panel(
            safe, "Wood Frame", .12f, .085f, .88f, .915f, new Color32(90, 55, 31, 255));
        RectTransform paper = CompletionReceiptLayout.Panel(
            frame, "Cream Panel", .006f, .008f, .994f, .992f, new Color32(248, 233, 204, 255));

        RectTransform header = CompletionReceiptLayout.Panel(
            paper, "Failure Header", .025f, .81f, .975f, .975f, new Color32(18, 48, 111, 255));
        TextMeshProUGUI title = CompletionReceiptLayout.Label(
            header, "Title", "BRIDGE TEST FAILED", .04f, .24f, .96f, .90f, 50f, font, TextAlignmentOptions.Center);
        title.color = Color.white;
        contractNameText = CompletionReceiptLayout.Label(
            header, "Contract Name", "STRUCTURAL TEST REPORT", .04f, .04f, .96f, .31f, 22f, font, TextAlignmentOptions.Center);
        contractNameText.color = new Color32(238, 202, 129, 255);

        RectTransform reasonCard = CompletionReceiptLayout.Panel(
            paper, "Failure Summary", .035f, .65f, .965f, .79f, new Color32(239, 220, 184, 255));
        RectTransform reasonBadge = CompletionReceiptLayout.Panel(
            reasonCard, "Failure Badge", .025f, .16f, .13f, .84f, new Color32(218, 111, 39, 255));
        TextMeshProUGUI badgeText = CompletionReceiptLayout.Label(
            reasonBadge, "Mark", "!", 0f, 0f, 1f, 1f, 46f, font, TextAlignmentOptions.Center);
        badgeText.color = Color.white;
        levelNameText = CompletionReceiptLayout.Label(
            reasonCard, "Failure Reason", "BRIDGE TEST FAILED", .16f, .10f, .965f, .90f, 38f, font, TextAlignmentOptions.MidlineLeft);
        levelNameText.fontStyle = FontStyles.Bold;

        RectTransform diagnosis = CompletionReceiptLayout.Panel(
            paper, "Failure Diagnosis", .035f, .35f, .965f, .63f, new Color32(243, 226, 195, 255));
        TextMeshProUGUI happenedHeading = CompletionReceiptLayout.Label(
            diagnosis, "What Happened Heading", "WHAT HAPPENED", .035f, .76f, .37f, .96f, 23f, font);
        happenedHeading.fontStyle = FontStyles.Bold;
        failureDetailText = CompletionReceiptLayout.Label(
            diagnosis, "Failure Detail", "", .035f, .43f, .965f, .78f, 27f, font, TextAlignmentOptions.TopLeft);
        failureDetailText.fontSizeMin = 20f;
        CompletionReceiptLayout.Divider(diagnosis, "Diagnosis Divider", .035f, .39f, .965f);
        TextMeshProUGUI checkHeading = CompletionReceiptLayout.Label(
            diagnosis, "What To Check Heading", "WHAT TO CHECK", .035f, .19f, .27f, .37f, 23f, font);
        checkHeading.fontStyle = FontStyles.Bold;
        failureRecommendationText = CompletionReceiptLayout.Label(
            diagnosis, "Recommendation", "", .29f, .05f, .965f, .38f, 25f, font, TextAlignmentOptions.MidlineLeft);
        failureRecommendationText.fontSizeMin = 19f;

        RectTransform statusCard = CompletionReceiptLayout.Panel(
            paper, "Failure Status", .035f, .225f, .965f, .33f, new Color32(229, 232, 204, 255));
        penaltyText = CompletionReceiptLayout.Label(
            statusCard, "Penalty Status", "", .035f, .08f, .965f, .92f, 26f, font, TextAlignmentOptions.Center);

        Button exit = CompletionReceiptLayout.Button(
            paper, "EXIT", .08f, .045f, .47f, .19f, new Color32(239, 214, 170, 255), font, ExitLevel);
        Button retry = CompletionReceiptLayout.Button(
            paper, "TRY AGAIN", .53f, .045f, .92f, .19f, new Color32(228, 157, 44, 255), font, RetryLevel);
        exitButton = exit.gameObject;
        failureExitButtonRect = exit.transform as RectTransform;
        failureRetryButtonRect = retry.transform as RectTransform;
        retryButton = retry.gameObject;

        safe.gameObject.AddComponent<FailureEntranceMotion>().Configure(frame, reasonCard, diagnosis);
    }

    private void UpdateFailureButtonLayout()
    {
        bool showRetry = !hideRetryButtonThisFail;
        if (exitButton != null) exitButton.SetActive(showExitButtonThisFail);
        if (retryButton != null) retryButton.SetActive(showRetry);

        if (failureExitButtonRect != null && showExitButtonThisFail)
        {
            failureExitButtonRect.anchorMin = showRetry
                ? new Vector2(.08f, .045f)
                : new Vector2(.30f, .045f);
            failureExitButtonRect.anchorMax = showRetry
                ? new Vector2(.47f, .19f)
                : new Vector2(.70f, .19f);
            failureExitButtonRect.offsetMin = failureExitButtonRect.offsetMax = Vector2.zero;
        }

        if (failureRetryButtonRect != null && showRetry)
        {
            failureRetryButtonRect.anchorMin = showExitButtonThisFail
                ? new Vector2(.53f, .045f)
                : new Vector2(.30f, .045f);
            failureRetryButtonRect.anchorMax = showExitButtonThisFail
                ? new Vector2(.92f, .19f)
                : new Vector2(.70f, .19f);
            failureRetryButtonRect.offsetMin = failureRetryButtonRect.offsetMax = Vector2.zero;
        }
    }

    public void ResetFailCount()
    {
        currentFailCount = 0;
    }

    private void RestoreHiddenUI()
    {
        foreach (GameObject ui in temporarilyHiddenPanels)
        {
            if (ui != null) ui.SetActive(true);
        }
        temporarilyHiddenPanels.Clear();
    }

    public void RetryLevel()
    {
        BuildLocation restartLocation = tutorialLocationToRestart;
        tutorialLocationToRestart = null;

        if (failDelayCoroutine != null) StopCoroutine(failDelayCoroutine); 
        
        if (physicsManager != null)
        {
            physicsManager.StopPhysicsAndReset();
        }
        
        BarCreator barCreator = FindObjectOfType<BarCreator>();
        if (barCreator != null) barCreator.isSimulating = false;

        BuildLocation[] allLocs = Resources.FindObjectsOfTypeAll<BuildLocation>();
        foreach (var loc in allLocs)
        {
            if (loc.gameObject.scene.name != null && loc.gameObject.activeInHierarchy)
            {
                loc.ResetTimeAttack(); 
            }
        }

        RestoreHiddenUI(); 

        if (levelFailedPanel != null) levelFailedPanel.SetActive(false);
        isFailed = false;
        hideRetryButtonThisFail = false; 
        showExitButtonThisFail = false;

        if (restartLocation != null)
            StartCoroutine(RestartTutorialAfterRetry(restartLocation));
    }

    private IEnumerator RestartTutorialAfterRetry(BuildLocation restartLocation)
    {
        // Let the physics reset, restored HUD, and UI layouts finish first.
        yield return null;
        Canvas.ForceUpdateCanvases();

        if (restartLocation != null && restartLocation.gameObject.activeInHierarchy)
        {
            if (!restartLocation.RestartBuildTutorialAfterFailure())
                Debug.LogWarning("Tutorial contract retry could not restart its build tutorial. Check the Build Location tutorial reference.");
        }
    }

    public void ExitLevel()
    {
        Time.timeScale = 1f;
        if (failDelayCoroutine != null) StopCoroutine(failDelayCoroutine);

        if (physicsManager != null) physicsManager.StopPhysicsAndReset();
        BarCreator barCreator = FindObjectOfType<BarCreator>();
        if (barCreator != null) barCreator.isSimulating = false;

        if (BuildTutorialDirector.Instance != null && BuildTutorialDirector.Instance.isTutorialRunning)
            BuildTutorialDirector.Instance.EndTutorial();

        RestoreHiddenUI();
        if (levelFailedPanel != null) levelFailedPanel.SetActive(false);
        isFailed = false;
        hideRetryButtonThisFail = false;
        showExitButtonThisFail = false;
        tutorialLocationToRestart = null;

        // Use the normal build-mode exit path so the player returns to the world,
        // the previous saved redesign is restored, and cameras/UI transition in
        // the same way as the regular Exit Build Mode control.
        if (GameManager.Instance != null) GameManager.Instance.ExitBuildMode();
    }

    private void HandleSimulationStopped()
    {
        if (failDelayCoroutine != null) StopCoroutine(failDelayCoroutine); 
        
        isFailed = false;
        activeVehicle = null;
        hasVehicleStartHeight = false;
        vehicleWasPresentForSimulation = false;
        tutorialLocationToRestart = null;
        showExitButtonThisFail = false;
        RestoreHiddenUI(); 
        if (levelFailedPanel != null) levelFailedPanel.SetActive(false);
    }
}

internal sealed class FailureEntranceMotion : MonoBehaviour
{
    private RectTransform frame;
    private RectTransform summary;
    private RectTransform diagnosis;
    private CanvasGroup rootGroup;
    private CanvasGroup summaryGroup;
    private CanvasGroup diagnosisGroup;
    private Vector2 summaryPosition;
    private Vector2 diagnosisPosition;
    private Coroutine animationRoutine;

    internal void Configure(RectTransform panel, RectTransform summaryCard, RectTransform diagnosisCard)
    {
        frame = panel;
        summary = summaryCard;
        diagnosis = diagnosisCard;
        summaryPosition = summary.anchoredPosition;
        diagnosisPosition = diagnosis.anchoredPosition;
        rootGroup = panel.gameObject.AddComponent<CanvasGroup>();
        summaryGroup = summary.gameObject.AddComponent<CanvasGroup>();
        diagnosisGroup = diagnosis.gameObject.AddComponent<CanvasGroup>();
    }

    private void OnEnable()
    {
        if (frame != null) animationRoutine = StartCoroutine(Reveal());
    }

    private IEnumerator Reveal()
    {
        rootGroup.interactable = false;
        rootGroup.alpha = 0f;
        summaryGroup.alpha = diagnosisGroup.alpha = 0f;
        frame.localScale = Vector3.one * .82f;
        summary.anchoredPosition = summaryPosition + Vector2.up * 45f;
        diagnosis.anchoredPosition = diagnosisPosition + Vector2.up * 35f;

        float elapsed = 0f;
        while (elapsed < .85f)
        {
            elapsed += Time.unscaledDeltaTime;
            rootGroup.alpha = Ease(elapsed / .28f);
            frame.localScale = Vector3.one * Mathf.LerpUnclamped(.82f, 1f, Pop(elapsed / .55f));
            float summaryT = Ease((elapsed - .12f) / .32f);
            float diagnosisT = Ease((elapsed - .28f) / .38f);
            summaryGroup.alpha = summaryT;
            diagnosisGroup.alpha = diagnosisT;
            summary.anchoredPosition = summaryPosition + Vector2.up * (45f * (1f - summaryT));
            diagnosis.anchoredPosition = diagnosisPosition + Vector2.up * (35f * (1f - diagnosisT));
            yield return null;
        }

        Restore();
        animationRoutine = null;
    }

    private static float Ease(float value)
    {
        float t = Mathf.Clamp01(value);
        return 1f - Mathf.Pow(1f - t, 3f);
    }

    private static float Pop(float value)
    {
        float t = Mathf.Clamp01(value) - 1f;
        return 1f + 2.2f * t * t * t + 1.2f * t * t;
    }

    private void OnDisable()
    {
        if (animationRoutine != null) StopCoroutine(animationRoutine);
        animationRoutine = null;
        Restore();
    }

    private void Restore()
    {
        if (frame == null) return;
        frame.localScale = Vector3.one;
        summary.anchoredPosition = summaryPosition;
        diagnosis.anchoredPosition = diagnosisPosition;
        rootGroup.alpha = summaryGroup.alpha = diagnosisGroup.alpha = 1f;
        rootGroup.interactable = true;
    }
}
