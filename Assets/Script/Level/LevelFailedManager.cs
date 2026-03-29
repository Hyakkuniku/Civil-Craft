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
    private bool isFailed = false;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        // Ensure the panel is hidden when the game starts
        if (levelFailedPanel != null) levelFailedPanel.SetActive(false);
    }

    private void Start()
    {
        physicsManager = FindObjectOfType<BridgePhysicsManager>();
        
        // Listen to the physics manager to know when to auto-close the panel
        if (physicsManager != null)
        {
            physicsManager.OnSimulationStopped += HandleSimulationStopped;
        }
    }

    private void OnDestroy()
    {
        // Always clean up event listeners!
        if (physicsManager != null)
        {
            physicsManager.OnSimulationStopped -= HandleSimulationStopped;
        }
    }

    private void Update()
    {
        if (isFailed) return;

        // Only check for failure while the bridge is actually being simulated
        if (physicsManager != null && physicsManager.isSimulating)
        {
            // Find the vehicle in the scene
            if (activeVehicle == null) activeVehicle = FindObjectOfType<LiveLoadVehicle>();

            // Check if the vehicle has fallen past the death line
            if (activeVehicle != null && activeVehicle.gameObject.activeInHierarchy)
            {
                if (activeVehicle.transform.position.y < deathThreshold)
                {
                    TriggerLevelFailed();
                }
            }
        }
    }

    public void TriggerLevelFailed()
    {
        isFailed = true;

        // Show the UI Panel
        if (levelFailedPanel != null) levelFailedPanel.SetActive(true);

        // Display the current Contract/Level Name
        if (levelNameText != null)
        {
            if (GameManager.Instance != null && GameManager.Instance.CurrentContract != null)
            {
                levelNameText.text = GameManager.Instance.CurrentContract.name;
            }
            else
            {
                levelNameText.text = "Bridge Test Failed";
            }
        }
    }

    // --- BUTTON ACTIONS ---

    public void RetryLevel()
    {
        // Calling StopPhysicsAndReset instantly puts the player back into CAD/Build mode 
        // with their bridge intact so they can fix their mistakes!
        if (physicsManager != null)
        {
            physicsManager.StopPhysicsAndReset();
        }
        
        BarCreator barCreator = FindObjectOfType<BarCreator>();
        if (barCreator != null) barCreator.isSimulating = false;
    }

    public void ExitLevel()
    {
        // Ensures time is unfrozen, then returns to the map
        Time.timeScale = 1f;
        SceneManager.LoadScene("Level Selection"); // <-- IMPORTANT: Make sure this matches your Map scene name!
    }

    // Automatically hides the panel if the simulation is stopped (e.g. via keyboard shortcut)
    private void HandleSimulationStopped()
    {
        isFailed = false;
        if (levelFailedPanel != null) levelFailedPanel.SetActive(false);
    }
}