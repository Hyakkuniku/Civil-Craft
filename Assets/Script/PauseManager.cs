using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class PauseManager : MonoBehaviour
{
    public static PauseManager Instance { get; private set; }

    [Header("UI References")]
    [Tooltip("Drag your main Pause Panel here.")]
    public GameObject pausePanel;
    
    [Tooltip("Drag your Settings Panel here (Optional, to close it when unpausing).")]
    public GameObject settingsPanel; 

    [Tooltip("The existing HUD Pause button. It is hidden for tutorial contracts.")]
    public GameObject pauseButton;

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
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        // Ensure the pause panel is hidden when the game starts
        if (pausePanel != null) pausePanel.SetActive(false);
    }

    private void Start()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnEnterBuildMode.AddListener(RefreshPauseAvailability);
            GameManager.Instance.OnExitBuildMode.AddListener(RefreshPauseAvailability);
        }

        RefreshPauseAvailability();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnEnterBuildMode.RemoveListener(RefreshPauseAvailability);
            GameManager.Instance.OnExitBuildMode.RemoveListener(RefreshPauseAvailability);
        }
    }

    private void Update()
    {
        // Toggle pause with the Escape key
        bool pausePressed = (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) ||
                            (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame);

        if (pausePressed)
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
            if (UIPanelCoordinator.Instance != null)
                UIPanelCoordinator.Instance.ClosePanel(settingsPanel);
            else
                settingsPanel.SetActive(false);
            return;
        }

        if (!isPaused && IsPauseBlockedByTutorialContract()) return;

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
        if (IsPauseBlockedByTutorialContract())
        {
            RefreshPauseAvailability();
            return;
        }

        isPaused = true;
        Time.timeScale = 0f; // Freezes physics and animations
        
        if (UIPanelCoordinator.Instance != null)
            UIPanelCoordinator.Instance.OpenPanel(pausePanel);
        else if (pausePanel != null)
            pausePanel.SetActive(true);

        if (UIPanelCoordinator.Instance == null && objectsToHide != null)
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
        
        if (UIPanelCoordinator.Instance != null)
        {
            if (settingsPanel != null && settingsPanel.activeSelf)
                UIPanelCoordinator.Instance.ClosePanel(settingsPanel);
            UIPanelCoordinator.Instance.ClosePanel(pausePanel);
        }
        else
        {
            if (pausePanel != null) pausePanel.SetActive(false);
            if (settingsPanel != null) settingsPanel.SetActive(false);
        }

        // --- NEW: Re-enable the objects in the array ---
        if (UIPanelCoordinator.Instance == null && objectsToHide != null)
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

    public void RefreshPauseAvailability()
    {
        if (pauseButton == null)
            pauseButton = FindPauseButton();

        bool pauseAllowed = !IsPauseBlockedByTutorialContract();
        if (pauseButton != null) pauseButton.SetActive(pauseAllowed);

        if (!pauseAllowed && isPaused) ResumeGame();
    }

    private static bool IsPauseBlockedByTutorialContract()
    {
        return GameManager.Instance != null &&
               GameManager.Instance.CurrentState == GameManager.GameState.Building &&
               GameManager.Instance.CurrentContract != null &&
               GameManager.Instance.CurrentContract.isTutorialContract;
    }

    private GameObject FindPauseButton()
    {
        foreach (Button button in FindObjectsOfType<Button>(true))
        {
            for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
            {
                if (button.onClick.GetPersistentTarget(i) == this &&
                    button.onClick.GetPersistentMethodName(i) == nameof(TogglePause))
                {
                    return button.gameObject;
                }
            }
        }

        return null;
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
