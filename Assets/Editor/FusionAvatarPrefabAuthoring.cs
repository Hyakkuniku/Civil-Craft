using Fusion;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
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

    [MenuItem("Civil Craft/Multiplayer/Validate Fusion Avatar Capacity")]
    public static void ValidateCapacity()
    {
        NetworkObject prefab = AssetDatabase.LoadAssetAtPath<NetworkObject>(PrefabPath);
        if (prefab == null || !prefab.TryGetComponent(out FusionMultiplayerAvatar avatar) ||
            prefab.NetworkedBehaviours == null ||
            !System.Array.Exists(prefab.NetworkedBehaviours, behaviour => behaviour == avatar))
            throw new BuildFailedException("The Fusion avatar prefab is missing its baked network behaviour.");

        int stateBytes = NetworkObject.GetWordCount(prefab) * sizeof(int);
        int pageBytes = 1 << (int)NetworkProjectConfig.Global.Heap.PageShift;
        if (stateBytes > pageBytes)
            throw new BuildFailedException($"The Fusion avatar needs {stateBytes:N0} bytes, but a heap page holds " +
                $"{pageBytes:N0} bytes. Players can join a room but their avatars cannot spawn.");

        Debug.Log($"[Fusion] Avatar state fits the heap page ({stateBytes:N0} of {pageBytes:N0} bytes).", prefab);
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

public sealed class FusionAvatarCapacityBuildCheck : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report) => FusionAvatarPrefabAuthoring.ValidateCapacity();
}
