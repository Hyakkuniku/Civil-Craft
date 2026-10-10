#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>Refuses to ship saved scenes without the authored currency controls.</summary>
public sealed class WebsiteShopBuildValidation : IPreprocessBuildWithReport
{
    public int callbackOrder => 10;

    [MenuItem("Tools/Civil Craft/Validate Saved Website Shop Controls")]
    public static void ValidateSavedScenes()
    {
        List<string> errors = CollectErrors();
        if (errors.Count > 0) throw new BuildFailedException("Website shop scene validation failed:\n" + string.Join("\n", errors));
        Debug.Log("[Website Shop] All four saved release scenes contain the reviewed dialog, Coins/Diamonds buttons and balance refresh. No account request or payment was made.");
    }

    public void OnPreprocessBuild(BuildReport report) => ValidateSavedScenes();

    private static List<string> CollectErrors()
    {
        var errors = new List<string>();
        const string prefabPath = "Assets/Prefabs/UI/AccountSaveChoiceDialog.prefab";
        if (!File.Exists(prefabPath)) { errors.Add("Reviewed dialog prefab is missing."); return errors; }
        string prefab = File.ReadAllText(prefabPath);
        var enabled = new HashSet<string>(EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path));
        foreach (string path in WebsiteShopScenePolicy.RequiredScenes)
        {
            if (!enabled.Contains(path)) errors.Add(path + ": required shop scene is not enabled in Build Settings.");
            if (!File.Exists(path)) { errors.Add(path + ": saved scene is missing."); continue; }
            errors.AddRange(WebsiteShopScenePolicy.CollectErrors(File.ReadAllText(path), prefab).Select(error => path + ": " + error));
        }
        return errors;
    }
}
#endif
