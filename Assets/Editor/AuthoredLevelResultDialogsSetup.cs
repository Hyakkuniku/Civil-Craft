using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>One-time authoring of the existing result layouts into serialized UI hierarchies.</summary>
public static class AuthoredLevelResultDialogsSetup
{
    private const string RoundedSpritePath = "Assets/Elements/UI/LevelResultRounded.png";
    private const string ManagerPrefabPath = "Assets/Prefabs/BuildingMode/MANAGERS AND CANVASES.prefab";
    private static readonly string[] ScenePaths =
    {
        "Assets/Scenes/BHAN HOUSE.unity",
        "Assets/Scenes/CanyonCrossing.unity",
        "Assets/Scenes/Multiplayer/Multiplayer.unity"
    };

    [MenuItem("Tools/Civil Craft/Author Level Result Dialogs")]
    public static void AuthorAll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        foreach (string path in ScenePaths)
        {
            Scene loaded = SceneManager.GetSceneByPath(path);
            if (loaded.isLoaded && loaded.isDirty)
            {
                Debug.LogWarning("Save edits in " + path + " first. Level-result authoring was not run.");
                return;
            }
        }

        Sprite rounded = EnsureRoundedSprite();
        if (rounded == null)
        {
            Debug.LogError("Could not create the persistent level-result rounded sprite.");
            return;
        }
        CompletionReceiptLayout.SetRoundedSprite(rounded);

        bool complete = AuthorManagerPrefab();
        foreach (string path in ScenePaths) complete &= AuthorScene(path);
        AssetDatabase.SaveAssets();
        if (complete)
            Debug.Log("Level Complete and Level Failed layouts were authored in all build-mode scenes and the manager prefab.");
    }

    private static Sprite EnsureRoundedSprite()
    {
        Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(RoundedSpritePath);
        if (existing != null) return existing;

        const int size = 64;
        const float corner = 18f;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = Mathf.Max(Mathf.Abs(x + .5f - size * .5f) - (size * .5f - corner), 0);
            float dy = Mathf.Max(Mathf.Abs(y + .5f - size * .5f) - (size * .5f - corner), 0);
            byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(corner - Mathf.Sqrt(dx * dx + dy * dy) + .5f) * 255);
            pixels[y * size + x] = new Color32(255, 255, 255, alpha);
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        string fullPath = Path.Combine(Application.dataPath, "Elements/UI/LevelResultRounded.png");
        File.WriteAllBytes(fullPath, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(RoundedSpritePath, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = AssetImporter.GetAtPath(RoundedSpritePath) as TextureImporter;
        if (importer == null) return null;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 100f;
        importer.spriteBorder = new Vector4(19f, 19f, 19f, 19f);
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(RoundedSpritePath);
    }

    private static bool AuthorScene(string path)
    {
        Scene scene = SceneManager.GetSceneByPath(path);
        bool opened = !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        try
        {
            LevelCompleteManager complete = FindInScene<LevelCompleteManager>(scene);
            LevelFailedManager failed = FindInScene<LevelFailedManager>(scene);
            if (complete == null || failed == null || complete.levelCompletePanel == null ||
                failed.levelFailedPanel == null) return false;
            bool changed = AuthorPanels(complete, failed);
            if (!changed) return true;
            EditorSceneManager.MarkSceneDirty(scene);
            return EditorSceneManager.SaveScene(scene);
        }
        finally
        {
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static bool AuthorManagerPrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(ManagerPrefabPath);
        if (root == null) return false;
        try
        {
            LevelCompleteManager complete = root.GetComponentInChildren<LevelCompleteManager>(true);
            LevelFailedManager failed = root.GetComponentInChildren<LevelFailedManager>(true);
            if (complete == null || failed == null || complete.levelCompletePanel == null ||
                failed.levelFailedPanel == null) return false;
            bool changed = AuthorPanels(complete, failed);
            if (!changed) return true;
            return PrefabUtility.SaveAsPrefabAsset(root, ManagerPrefabPath) != null;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static bool AuthorPanels(LevelCompleteManager complete, LevelFailedManager failed)
    {
        bool changed = false;
        if (complete.levelCompletePanel != null &&
            complete.levelCompletePanel.transform.Find("Completion Safe Area") == null)
        {
            complete.AuthorReceiptLayout();
            EditorUtility.SetDirty(complete);
            changed = true;
        }
        if (failed.levelFailedPanel != null &&
            failed.levelFailedPanel.transform.Find("Failure Safe Area") == null)
        {
            failed.AuthorFailureLayout();
            EditorUtility.SetDirty(failed);
            changed = true;
        }
        // Components added while creating an inactive hierarchy are not always persisted
        // by the first scene save. Rebind the entrance groups after the hierarchy exists.
        Transform completeSafe = complete.levelCompletePanel.transform.Find("Completion Safe Area");
        if (completeSafe != null)
        {
            var motion = completeSafe.GetComponent<CompletionEntranceMotion>();
            var frame = completeSafe.Find("Wood Frame") as RectTransform;
            var paper = completeSafe.Find("Wood Frame/Cream Panel");
            var receipt = paper != null ? paper.Find("Material Receipt") as RectTransform : null;
            var photo = paper != null ? paper.Find("Bridge Photo Slot/Bridge Photo Frame") as RectTransform : null;
            if (motion != null && frame != null && receipt != null && photo != null)
            {
                motion.Configure(frame, receipt, photo);
                EditorUtility.SetDirty(motion);
                changed = true;
            }
        }
        Transform failedSafe = failed.levelFailedPanel.transform.Find("Failure Safe Area");
        if (failedSafe != null)
        {
            var motion = failedSafe.GetComponent<FailureEntranceMotion>();
            var frame = failedSafe.Find("Wood Frame") as RectTransform;
            var paper = failedSafe.Find("Wood Frame/Cream Panel");
            var summary = paper != null ? paper.Find("Failure Summary") as RectTransform : null;
            var diagnosis = paper != null ? paper.Find("Failure Diagnosis") as RectTransform : null;
            if (motion != null && frame != null && summary != null && diagnosis != null)
            {
                motion.Configure(frame, summary, diagnosis);
                EditorUtility.SetDirty(motion);
                changed = true;
            }
        }
        return changed;
    }

    private static T FindInScene<T>(Scene scene) where T : Component
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            T found = root.GetComponentInChildren<T>(true);
            if (found != null) return found;
        }
        return null;
    }
}
