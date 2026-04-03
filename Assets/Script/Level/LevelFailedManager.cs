using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;

public class LevelFailedManager : MonoBehaviour
{
    public static LevelFailedManager Instance { get; private set; }

    [Header("UI References")]
    [Tooltip("Drag the Level Failed Panel here.")]
    public GameObject levelFailedPanel;
    
    [Tooltip("Drag the Text element that will display the level/contract name here.")]
    public TextMeshProUGUI levelNameText;

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

    public void RetryLevel()
    {
        if (physicsManager != null)
        {
            physicsManager.StopPhysicsAndReset();
        }
        
        BarCreator barCreator = FindObjectOfType<BarCreator>();
        if (barCreator != null) barCreator.isSimulating = false;

        // --- THE CHANGE: Level Reset call REMOVED per your constraints! ---

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
        if (levelFailedPanel != null) levelFailedPanel.SetActive(false);
    }
}