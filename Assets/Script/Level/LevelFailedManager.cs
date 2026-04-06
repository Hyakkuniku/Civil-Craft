using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;
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

    [Header("Penalty Tracking")]
    [Tooltip("How much gold is deducted from the final reward EVERY time the bridge collapses?")]
    public int goldPenaltyPerFail = 25;
    [HideInInspector] public int currentFailCount = 0;

    private LiveLoadVehicle activeVehicle;
    private BridgePhysicsManager physicsManager;
    
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
                TriggerLevelFailed(stressFailReason);
                return; 
            }

            if (activeVehicle == null) activeVehicle = FindObjectOfType<LiveLoadVehicle>();

            if (activeVehicle != null && activeVehicle.gameObject.activeInHierarchy)
            {
                if (activeVehicle.transform.position.y < deathThreshold)
                {
                    TriggerLevelFailed("Vehicle Destroyed!");
                }
            }
        }
    }

    public void TriggerLevelFailed(string failureReason = "")
    {
        bool isTutorial = false;
        
        // Check if we are currently playing a tutorial contract
        if (GameManager.Instance != null && GameManager.Instance.CurrentContract != null)
        {
            isTutorial = GameManager.Instance.CurrentContract.isTutorialContract;
        }

        if (!isFailed) 
        {
            // --- THE FIX: Only add to the fail count if it is NOT a tutorial ---
            if (!isTutorial)
            {
                currentFailCount++;
            }
        }

        isFailed = true;

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

        // --- THE FIX: Hide the penalty text entirely if it's a tutorial ---
        if (penaltyText != null)
        {
            if (isTutorial)
            {
                penaltyText.text = ""; // Clears the text so they don't see any penalty info
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
        if (physicsManager != null)
        {
            physicsManager.StopPhysicsAndReset();
        }
        
        BarCreator barCreator = FindObjectOfType<BarCreator>();
        if (barCreator != null) barCreator.isSimulating = false;

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
        isFailed = false;
        RestoreHiddenUI(); 
        if (levelFailedPanel != null) levelFailedPanel.SetActive(false);
    }
}