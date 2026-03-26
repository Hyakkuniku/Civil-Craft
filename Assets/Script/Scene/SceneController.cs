using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneController : MonoBehaviour
{
    private const string SCENE_MAIN_MENU      = "Main Menu";
    private const string SCENE_MODE_SELECTION = "Mode Selection";
    private const string SCENE_Tutorial = "Tutorial";

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
        
        StartCoroutine(LoadSceneAsyncCoroutine(sceneName));
    }

    private IEnumerator LoadSceneAsyncCoroutine(string sceneName)
    {
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName);

        while (!asyncLoad.isDone)
        {
            yield return null; 
        }
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