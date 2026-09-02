#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[InitializeOnLoad]
public static class LandscapeCanvasResolutionSetup
{
    private const string SessionKey = "CivilCraft.LandscapeCanvasResolution.1920x1080.V1";
    private static readonly string[] ScenePaths =
    {
        "Assets/Scenes/Main Menu.unity",
        "Assets/Scenes/Mode Selection.unity",
        "Assets/Scenes/CanyonCrossing.unity",
        "Assets/Scenes/BHAN HOUSE.unity"
    };

    static LandscapeCanvasResolutionSetup()
    {
        EditorApplication.delayCall += TryRunOnce;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode)
                EditorApplication.delayCall += TryRunOnce;
        };
    }

    [MenuItem("Tools/Civil Craft/Normalize Scene Canvases To 1920x1080")]
    public static void NormalizeAllScenes()
    {
        foreach (string path in ScenePaths)
            NormalizeScene(path);

        AssetDatabase.SaveAssets();
        Debug.Log("[Canvas Resolution] Screen-space canvases now use 1920 x 1080 with a 0.5 width/height match.");
    }

    private static void TryRunOnce()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
            SessionState.GetBool(SessionKey, false))
            return;

        SessionState.SetBool(SessionKey, true);
        NormalizeAllScenes();
    }

    private static void NormalizeScene(string scenePath)
    {
        Scene scene = SceneManager.GetSceneByPath(scenePath);
        bool openedForSetup = !scene.IsValid() || !scene.isLoaded;
        if (openedForSetup)
            scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);

        bool changed = false;
        CanvasScaler[] scalers = Resources.FindObjectsOfTypeAll<CanvasScaler>()
            .Where(scaler => scaler != null && scaler.gameObject.scene == scene)
            .ToArray();

        foreach (CanvasScaler scaler in scalers)
        {
            Canvas canvas = scaler.GetComponent<Canvas>();
            if (canvas == null || canvas.renderMode == RenderMode.WorldSpace)
                continue;

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            scaler.referencePixelsPerUnit = 100f;
            EditorUtility.SetDirty(scaler);
            changed = true;
        }

        if (changed)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        if (openedForSetup)
            EditorSceneManager.CloseScene(scene, true);
    }
}
#endif
