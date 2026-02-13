using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneController : MonoBehaviour
{
    // Optional: you can use build index or scene names
    // Using names is usually clearer and less error-prone

    private const string SCENE_MAIN_MENU      = "Main Menu";
    private const string SCENE_MODE_SELECTION = "Mode Selection";
    private const string SCENE_Tutorial = "Tutorial";
    // Add more later, e.g.:
    // private const string SCENE_GAME           = "Game";
    // private const string SCENE_TUTORIAL       = "Tutorial";

    // ────────────────────────────────────────────────
    // Call these from buttons (via OnClick in Inspector)
    // ────────────────────────────────────────────────

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


    // Example – add when you have a game scene
    // public void LoadGame()
    // {
    //     LoadScene(SCENE_GAME);
    // }

    // ────────────────────────────────────────────────
    // Core loading method (you can expand later)
    // ────────────────────────────────────────────────

    private void LoadScene(string sceneName)
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

        Debug.Log($"Loading scene: {sceneName}");
        SceneManager.LoadScene(sceneName);
    }

    // Optional helper – prevents silent failures
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

    // Bonus: Quit game (very useful for main menu)
    public void QuitGame()
    {
        #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
        #else
            Application.Quit();
        #endif

        Debug.Log("Quit game requested");
    }
}