using UnityEngine;
using UnityEngine.SceneManagement;

public class PauseManager : MonoBehaviour
{
    public static PauseManager Instance { get; private set; }

    [Header("UI References")]
    [Tooltip("Drag your main Pause Panel here.")]
    public GameObject pausePanel;
    
    [Tooltip("Drag your Settings Panel here (Optional, to close it when unpausing).")]
    public GameObject settingsPanel; 

    [Header("Elements to Hide")]
    [Tooltip("Drag any game objects (like HUD elements) here that should disappear when paused.")]
    public GameObject[] objectsToHide; // --- NEW: Array of objects to hide ---

    [Header("Scene Management")]
    [Tooltip("The exact name of your Mode Selection scene.")]
    public string modeSelectionSceneName = "ModeSelection";

    [HideInInspector] public bool isPaused = false;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        // Ensure the pause panel is hidden when the game starts
        if (pausePanel != null) pausePanel.SetActive(false);
    }

    private void Update()
    {
        // Toggle pause with the Escape key
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            // If the player is building, the GameManager uses Escape to exit build mode. 
            // We don't want to pause the game at the same time!
            if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameManager.GameState.Building)
            {
                return; 
            }

            TogglePause();
        }
    }

    public void TogglePause()
    {
        // If the settings panel is open, pressing Escape should just close settings, not unpause the whole game yet.
        if (isPaused && settingsPanel != null && settingsPanel.activeSelf)
        {
            settingsPanel.SetActive(false);
            return;
        }

        if (isPaused)
        {
            ResumeGame();
        }
        else
        {
            PauseGame();
        }
    }

    public void PauseGame()
    {
        isPaused = true;
        Time.timeScale = 0f; // Freezes physics and animations
        
        if (pausePanel != null) pausePanel.SetActive(true);

        // --- NEW: Hide the objects in the array ---
        if (objectsToHide != null)
        {
            foreach (GameObject obj in objectsToHide)
            {
                if (obj != null) obj.SetActive(false);
            }
        }

        // Disable player movement and camera look
        InputManager inputObj = FindObjectOfType<InputManager>();
        if (inputObj != null) 
        {
            inputObj.SetPlayerInputEnable(false);
            inputObj.SetLookEnabled(false);
        }
    }

    public void ResumeGame()
    {
        isPaused = false;
        Time.timeScale = 1f; // Unfreezes the game
        
        if (pausePanel != null) pausePanel.SetActive(false);
        if (settingsPanel != null) settingsPanel.SetActive(false);

        // --- NEW: Re-enable the objects in the array ---
        if (objectsToHide != null)
        {
            foreach (GameObject obj in objectsToHide)
            {
                if (obj != null) obj.SetActive(true);
            }
        }

        // Re-enable player movement and camera look
        InputManager inputObj = FindObjectOfType<InputManager>();
        if (inputObj != null) 
        {
            inputObj.SetPlayerInputEnable(true);
            inputObj.SetLookEnabled(true);
        }
    }

    public void ReturnToModeSelection()
    {
        // CRITICAL: Always reset time scale before loading a new scene, or the next scene will be frozen!
        Time.timeScale = 1f; 
        
        // Ensure the game isn't trying to carry over a paused state
        isPaused = false; 

        SceneManager.LoadScene(modeSelectionSceneName); 
    }
}