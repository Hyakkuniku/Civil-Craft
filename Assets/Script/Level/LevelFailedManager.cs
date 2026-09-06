using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic; 

public class LevelFailedManager : MonoBehaviour
{
    public static LevelFailedManager Instance { get; private set; }

    [Header("UI References")]
    [Tooltip("Drag the Level Failed Panel here.")]
    public GameObject levelFailedPanel;
    
    [Tooltip("Drag the Text element that will display the level/contract name here.")]
    public TextMeshProUGUI levelNameText;

    [Tooltip("Drag the Text element that will display the gold penalty here.")]
    public TextMeshProUGUI penaltyText; 

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

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        if (levelFailedPanel != null) levelFailedPanel.SetActive(false);
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
        if (vehicle == null) return false;

        ContractSO currentContract = GameManager.Instance != null
            ? GameManager.Instance.CurrentContract
            : null;

        return currentContract == null ||
               vehicle.assignedContract == null ||
               vehicle.assignedContract == currentContract;
    }

    private static LiveLoadVehicle FindVehicleForCurrentContract()
    {
        LiveLoadVehicle fallback = null;
        ContractSO currentContract = GameManager.Instance != null
            ? GameManager.Instance.CurrentContract
            : null;

        foreach (LiveLoadVehicle vehicle in FindObjectsOfType<LiveLoadVehicle>(true))
        {
            if (vehicle == null || !vehicle.gameObject.scene.IsValid()) continue;

            if (currentContract != null && vehicle.assignedContract == currentContract)
                return vehicle;

            if (fallback == null &&
                vehicle.gameObject.activeInHierarchy &&
                (currentContract == null || vehicle.assignedContract == null))
            {
                fallback = vehicle;
            }
        }

        return fallback;
    }

    private void InitiateFailure(string reason)
    {
        if (isFailed) return;
        isFailed = true; 

        if (failDelayCoroutine != null) StopCoroutine(failDelayCoroutine);
        failDelayCoroutine = StartCoroutine(FailDelayRoutine(reason));
    }

    private IEnumerator FailDelayRoutine(string reason)
    {
        yield return new WaitForSeconds(delayBeforeFailScreen);
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
            isCompletedContractRedesign = PlayerDataManager.Instance != null &&
                PlayerDataManager.Instance.HasContractCompletionRecord(contract.ContractID);
        }

        bool shouldApplyPenalty = !isTutorial && !isCompletedContractRedesign;
        if (shouldApplyPenalty)
        {
            currentFailCount++;
        }

        tutorialLocationToRestart = isTutorial && !hideRetryButtonThisFail && GameManager.Instance != null
            ? GameManager.Instance.ActiveBuildLocation
            : null;

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

        if (levelFailedPanel != null) levelFailedPanel.SetActive(true);
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX("Level_Fail");

        // --- NEW: Turn off the Retry button if the contract is locked! ---
        if (retryButton != null) retryButton.SetActive(!hideRetryButtonThisFail);

        if (levelNameText != null)
        {
            if (!string.IsNullOrEmpty(failureReason))
            {
                levelNameText.text = failureReason;
            }
            else if (GameManager.Instance != null && GameManager.Instance.CurrentContract != null)
            {
                levelNameText.text = GameManager.Instance.CurrentContract.name + " Failed";
            }
            else
            {
                levelNameText.text = "Bridge Test Failed";
            }
        }

        if (penaltyText != null)
        {
            if (isTutorial)
            {
                penaltyText.text = ""; 
            }
            else if (isCompletedContractRedesign)
            {
                penaltyText.text = "<color=green>No penalty:</color> this contract was already completed.";
            }
            else
            {
                int totalLost = currentFailCount * goldPenaltyPerFail;
                penaltyText.text = $"<color=red>Penalty: -{goldPenaltyPerFail} Gold</color>\n\nTotal Lost This Job: -{totalLost} Gold";
            }
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
        ResetFailCount(); 
        hideRetryButtonThisFail = false; 
        tutorialLocationToRestart = null;
        SceneManager.LoadScene("Level Selection"); 
    }

    private void HandleSimulationStopped()
    {
        if (failDelayCoroutine != null) StopCoroutine(failDelayCoroutine); 
        
        isFailed = false;
        activeVehicle = null;
        hasVehicleStartHeight = false;
        vehicleWasPresentForSimulation = false;
        tutorialLocationToRestart = null;
        RestoreHiddenUI(); 
        if (levelFailedPanel != null) levelFailedPanel.SetActive(false);
    }
}
