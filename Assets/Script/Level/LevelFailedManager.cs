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

    [Header("Gameplay Elements to Hide")]
    [Tooltip("UI elements to hide when this panel is open (e.g., Crosshair, HUD)")]
    public List<GameObject> uiElementsToHide = new List<GameObject>(); 
    private List<GameObject> temporarilyHiddenPanels = new List<GameObject>(); 

    [Header("Failure Settings")]
    [Tooltip("The Y-axis height at which the vehicle is considered fallen/destroyed.")]
    public float deathThreshold = -15f;
    
    [Tooltip("How long to wait before showing the fail screen (lets the player watch the destruction).")]
    public float delayBeforeFailScreen = 2.0f; 

    [Header("Penalty Tracking")]
    [Tooltip("How much gold is deducted from the final reward EVERY time the bridge collapses?")]
    public int goldPenaltyPerFail = 25;
    [HideInInspector] public int currentFailCount = 0;

    private LiveLoadVehicle activeVehicle;
    private BridgePhysicsManager physicsManager;
    private Coroutine failDelayCoroutine;
    
    [HideInInspector] public bool isFailed = false;

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
            physicsManager.OnSimulationStopped += HandleSimulationStopped;
        }
    }

    private void OnDestroy()
    {
        if (physicsManager != null)
        {
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

            if (physicsManager.peakStressThisRun >= stressThreshold)
            {
                InitiateFailure(stressFailReason);
                return; 
            }

            if (activeVehicle == null) activeVehicle = FindObjectOfType<LiveLoadVehicle>();

            if (activeVehicle != null && activeVehicle.gameObject.activeInHierarchy)
            {
                if (activeVehicle.transform.position.y < deathThreshold)
                {
                    InitiateFailure("Vehicle Destroyed!");
                }
            }
        }
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

    public void TriggerLevelFailed(string failureReason = "")
    {
        InitiateFailure(failureReason);
    }

    private void ShowFailScreen(string failureReason)
    {
        bool isTutorial = false;
        
        if (GameManager.Instance != null && GameManager.Instance.CurrentContract != null)
        {
            isTutorial = GameManager.Instance.CurrentContract.isTutorialContract;
        }

        if (!isTutorial)
        {
            currentFailCount++;
        }

        if (activeVehicle == null) activeVehicle = FindObjectOfType<LiveLoadVehicle>();
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
        if (failDelayCoroutine != null) StopCoroutine(failDelayCoroutine); 
        
        if (physicsManager != null)
        {
            physicsManager.StopPhysicsAndReset();
        }
        
        BarCreator barCreator = FindObjectOfType<BarCreator>();
        if (barCreator != null) barCreator.isSimulating = false;

        // --- NEW: Reset the Time Attack Timer if they Restart ---
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
    }

    public void ExitLevel()
    {
        Time.timeScale = 1f;
        ResetFailCount(); 
        SceneManager.LoadScene("Level Selection"); 
    }

    private void HandleSimulationStopped()
    {
        if (failDelayCoroutine != null) StopCoroutine(failDelayCoroutine); 
        
        isFailed = false;
        RestoreHiddenUI(); 
        if (levelFailedPanel != null) levelFailedPanel.SetActive(false);
    }
}