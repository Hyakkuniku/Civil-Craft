using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneController : MonoBehaviour
{
    [Header("Quit Confirmation UI")]
    [Tooltip("Assign your Quit Confirmation Panel here.")]
    public GameObject quitConfirmationPanel;

    private const string SCENE_MAIN_MENU      = "Main Menu";
    private const string SCENE_MODE_SELECTION = "Mode Selection";
    private const string SCENE_Tutorial = "Tutorial";
    private const string SCENE_STORY_FALLBACK = "CanyonCrossing";

    private void Start()
    {
        // Ensure the confirmation panel is hidden when the scene loads
        if (quitConfirmationPanel != null)
        {
            quitConfirmationPanel.SetActive(false);
        }
    }

    public void LoadMainMenu()
    {
        LoadScene(SCENE_MAIN_MENU);
    }

    public void LoadModeSelection()
    {
        LoadScene(SCENE_MODE_SELECTION);
    }

    public void LoadTutorial()
    {
        LoadScene(SCENE_Tutorial);
    }

    /// <summary>
    /// Resumes Story Mode in the last gameplay scene saved for the player.
    /// New, missing, or invalid saves safely begin in CanyonCrossing.
    /// </summary>
    public void LoadLastSavedScene()
    {
        string sceneToLoad = SCENE_STORY_FALLBACK;

        if (PlayerDataManager.Instance != null &&
            PlayerDataManager.Instance.CurrentData != null)
        {
            string savedScene = PlayerDataManager.Instance.CurrentData.lastSavedScene;
            if (IsValidSavedGameplayScene(savedScene))
            {
                sceneToLoad = savedScene;
            }
            else if (!string.IsNullOrWhiteSpace(savedScene))
            {
                Debug.LogWarning(
                    $"Saved scene '{savedScene}' cannot be resumed. Loading '{SCENE_STORY_FALLBACK}' instead.");
            }
        }

        LoadScene(sceneToLoad);
    }

    // --- NEW: Helper method to load levels by their ID dynamically ---
    public void LoadLevel(int levelID)
    {
        // Assuming your scenes are named "Level1", "Level2", etc.
        LoadScene("Level" + levelID); 
    }

    // --- THE FIX: Made this PUBLIC so the MapUIManager can access it! ---
    public void LoadScene(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogError("Scene name is empty!");
            return;
        }

        if (SceneExists(sceneName) == false)
        {
            Debug.LogError($"Scene '{sceneName}' was not found. Did you add it to Build Settings?");
            return;
        }

        Debug.Log($"Starting background load for scene: {sceneName}");
        LoadingScreenManager.LoadScene(sceneName);
    }

    private bool SceneExists(string sceneName)
    {
        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (name == sceneName)
                return true;
        }
        return false;
    }

    private bool IsValidSavedGameplayScene(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName)) return false;

        // Never route the Story Play button back into a front-end screen.
        if (string.Equals(sceneName, SCENE_MAIN_MENU, System.StringComparison.OrdinalIgnoreCase) ||
            string.Equals(sceneName, SCENE_MODE_SELECTION, System.StringComparison.OrdinalIgnoreCase) ||
            string.Equals(sceneName, "Level Selection", System.StringComparison.OrdinalIgnoreCase) ||
            string.Equals(sceneName, "Multiplayer", System.StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return SceneExists(sceneName);
    }

    // ==========================================
    // QUIT CONFIRMATION LOGIC
    // ==========================================

    // 1. Link this to your MAIN Quit button on your main menu
    public void RequestQuit()
    {
        if (quitConfirmationPanel != null)
        {
            quitConfirmationPanel.SetActive(true);
        }
        else
        {
            // Fallback: If no panel is assigned, just quit immediately
            ConfirmQuit();
        }
    }

    // 2. Link this to the "No" / "Cancel" button on your popup panel
    public void CancelQuit()
    {
        if (quitConfirmationPanel != null)
        {
            quitConfirmationPanel.SetActive(false);
        }
    }

    // 3. Link this to the "Yes" / "Confirm" button on your popup panel
    public void ConfirmQuit()
    {
        #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
        #else
            Application.Quit();
        #endif

        Debug.Log("Quit game requested");
    }
}
