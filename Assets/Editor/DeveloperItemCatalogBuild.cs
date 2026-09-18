using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>Include the complete ID catalog even when a build starts outside the shop scene.</summary>
public sealed class DeveloperItemCatalogBuild : IPreprocessBuildWithReport
{
    public int callbackOrder => 100;

    public void OnPreprocessBuild(BuildReport report) => RefreshCatalog();

    [MenuItem("Tools/Civil Craft/Refresh Developer Item Catalog")]
    public static void RefreshCatalog()
    {
        const string path = "Assets/Resources/DeveloperItemCatalog.json";
        string json = JsonUtility.ToJson(DeveloperItemCatalog.CollectEditorCatalog(), true);
        Directory.CreateDirectory("Assets/Resources");
        if (File.Exists(path) && File.ReadAllText(path) == json) return;
        File.WriteAllText(path, json);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
    }
}
