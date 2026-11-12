using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Fills silent game controls, including UI instantiated after scene changes.</summary>
[DefaultExecutionOrder(10000)]
public sealed class SceneInteractionAudio : MonoBehaviour
{
    private Selectable[] controls = new Selectable[128];
    public static bool IsSupportedScene => SupportsScene(SceneManager.GetActiveScene().name);

    public static bool SupportsScene(string sceneName)
    {
        switch (sceneName)
        {
            case "Main Menu":
            case "Mode Selection":
            case "CanyonCrossing":
            case "BHAN HOUSE":
            case "Multiplayer":
                return true;
            default:
                return false;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Subscribe()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallForDirectPlay() => Install(SceneManager.GetActiveScene());

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Install(scene);
    private static void Install(Scene scene)
    {
        if (!SupportsScene(scene.name) || FindObjectOfType<SceneInteractionAudio>(true) != null) return;
        var owner = new GameObject("Scene Interaction Audio (Runtime)");
        DontDestroyOnLoad(owner);
        owner.AddComponent<SceneInteractionAudio>();
    }

    private void LateUpdate()
    {
        if (!IsSupportedScene) return;
        // Unity maintains this registry. Avoid hierarchy scans and allocations
        // every frame, while still catching newly opened/generated controls.
        if (controls.Length < Selectable.allSelectableCount)
            controls = new Selectable[Mathf.NextPowerOfTwo(Selectable.allSelectableCount)];
        int count = Selectable.AllSelectablesNoAlloc(controls);
        for (int i = 0; i < count; i++)
        {
            Selectable control = controls[i];
            controls[i] = null;
            string sceneName = control.gameObject.scene.name;
            if (!SupportsScene(sceneName) && sceneName != "DontDestroyOnLoad") continue;
            UIActionAudio.EnsureFeedback(control, AlmanacManager.Instance);
        }
    }
}
