using Fusion;
using UnityEditor;
using UnityEngine;

/// <summary>One-time Unity-authored prefab creation for the Fusion avatar proxy.</summary>
[InitializeOnLoad]
public static class FusionAvatarPrefabAuthoring
{
    private const string PrefabPath = "Assets/Resources/FusionMultiplayerAvatar.prefab";

    static FusionAvatarPrefabAuthoring()
    {
        // The Editor can author the NetworkObject prefab after script import,
        // even when its menu command cannot be reached in a headless session.
        EditorApplication.delayCall += CreatePrefabIfNeeded;
    }

    private static void CreatePrefabIfNeeded()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null) CreatePrefab();
    }

    [MenuItem("Civil Craft/Multiplayer/Create Fusion Avatar Prefab")]
    public static void CreatePrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
        {
            Debug.Log("[Fusion] Avatar prefab already exists; leaving it untouched.");
            return;
        }

        GameObject root = new GameObject("Fusion Multiplayer Avatar");
        try
        {
            root.AddComponent<NetworkObject>();
            root.AddComponent<FusionMultiplayerAvatar>();
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            if (prefab == null)
                throw new System.InvalidOperationException("Unity did not save the Fusion avatar prefab.");
            AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Fusion] Authored {PrefabPath}.");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }
}
