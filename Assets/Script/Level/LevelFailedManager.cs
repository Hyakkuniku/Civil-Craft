using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;
using System.Collections.Generic; // --- NEW ---

public class LevelFailedManager : MonoBehaviour
{
    public static LevelFailedManager Instance { get; private set; }

    [Header("UI References")]
    [Tooltip("Drag the Level Failed Panel here.")]
    public GameObject levelFailedPanel;
    
    [Tooltip("Drag the Text element that will display the level/contract name here.")]
    public TextMeshProUGUI levelNameText;

    [Header("Gameplay Elements to Hide")]
    [Tooltip("UI elements to hide when this panel is open (e.g., Crosshair, HUD)")]
    public List<GameObject> uiElementsToHide = new List<GameObject>(); // --- NEW ---
    private List<GameObject> temporarilyHiddenPanels = new List<GameObject>(); // --- NEW ---

    [Header("Failure Settings")]
    [Tooltip("The Y-axis height at which the vehicle is considered fallen/destroyed.")]
    public float deathThreshold = -15f;

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
            if (physicsManager.peakStressThisRun >= 1f)
            {
                TriggerLevelFailed("Bridge Collapsed!");
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
        isFailed = true;

        if (activeVehicle == null) activeVehicle = FindObjectOfType<LiveLoadVehicle>();
        if (activeVehicle != null) activeVehicle.EmergencyStop();

        // --- THE FIX: Hide UI Elements ---
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

        RestoreHiddenUI(); // --- THE FIX: Restore UI ---

        if (levelFailedPanel != null) levelFailedPanel.SetActive(false);
        isFailed = false;
    }

    public void ExitLevel()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene("Level Selection"); 
    }

    private void HandleSimulationStopped()
    {
        isFailed = false;
        RestoreHiddenUI(); // --- THE FIX: Restore UI ---
        if (levelFailedPanel != null) levelFailedPanel.SetActive(false);
    }
}