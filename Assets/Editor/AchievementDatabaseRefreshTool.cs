#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class AchievementDatabaseRefreshTool
{
    private const string MenuPath = "Tools/Civil Craft/Refresh Achievement Database";

    [MenuItem(MenuPath)]
    public static void RefreshAchievementDatabase()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog(
                "Refresh Achievement Database",
                "Exit Play Mode before refreshing the achievement database.",
                "OK");
            return;
        }

        List<AchievementSO> achievements = LoadAchievements();
        if (!ValidateAchievements(achievements))
            return;

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        SceneSetup[] originalSceneSetup = EditorSceneManager.GetSceneManagerSetup();
        int updatedPrefabs = 0;
        int updatedScenes = 0;

        try
        {
            updatedPrefabs = RefreshPrefabs(achievements);
            updatedScenes = RefreshScenes(achievements);
            AssetDatabase.SaveAssets();
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            EditorSceneManager.RestoreSceneManagerSetup(originalSceneSetup);
        }

        string message =
            $"Registered {achievements.Count} achievement(s).\n\n" +
            $"Updated prefabs: {updatedPrefabs}\n" +
            $"Updated scenes: {updatedScenes}";

        Debug.Log($"[Achievement Database] {message.Replace(Environment.NewLine, " ")}");
        EditorUtility.DisplayDialog("Achievement Database Refreshed", message, "OK");
    }

    [MenuItem(MenuPath, true)]
    private static bool ValidateRefreshAchievementDatabase()
    {
        return !EditorApplication.isPlayingOrWillChangePlaymode;
    }

    private static List<AchievementSO> LoadAchievements()
    {
        string[] guids = AssetDatabase.FindAssets("t:AchievementSO", new[] { "Assets" });
        List<AchievementSO> achievements = new List<AchievementSO>(guids.Length);

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            AchievementSO achievement = AssetDatabase.LoadAssetAtPath<AchievementSO>(path);
            if (achievement != null)
                achievements.Add(achievement);
        }

        achievements.Sort((left, right) => string.Compare(
            left.achievementID,
            right.achievementID,
            StringComparison.OrdinalIgnoreCase));
        return achievements;
    }

    private static bool ValidateAchievements(IReadOnlyList<AchievementSO> achievements)
    {
        if (achievements.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "Refresh Achievement Database",
                "No AchievementSO assets were found under Assets.",
                "OK");
            return false;
        }

        Dictionary<string, AchievementSO> achievementsById =
            new Dictionary<string, AchievementSO>(StringComparer.OrdinalIgnoreCase);
        List<string> errors = new List<string>();

        foreach (AchievementSO achievement in achievements)
        {
            if (string.IsNullOrWhiteSpace(achievement.achievementID))
            {
                errors.Add($"Missing achievement ID: {AssetDatabase.GetAssetPath(achievement)}");
                continue;
            }

            if (achievementsById.TryGetValue(achievement.achievementID, out AchievementSO duplicate))
            {
                errors.Add(
                    $"Duplicate ID '{achievement.achievementID}':\n" +
                    $"- {AssetDatabase.GetAssetPath(duplicate)}\n" +
                    $"- {AssetDatabase.GetAssetPath(achievement)}");
                continue;
            }

            achievementsById.Add(achievement.achievementID, achievement);
        }

        if (errors.Count == 0)
            return true;

        string details = string.Join("\n\n", errors);
        Debug.LogError($"[Achievement Database] Refresh cancelled.\n{details}");
        EditorUtility.DisplayDialog(
            "Achievement Database Not Refreshed",
            "Fix these achievement ID problems first:\n\n" + details,
            "OK");
        return false;
    }

    private static int RefreshPrefabs(List<AchievementSO> achievements)
    {
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
        int updatedCount = 0;

        for (int i = 0; i < prefabGuids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefabAsset == null ||
                (prefabAsset.GetComponentInChildren<PlayerDataManager>(true) == null &&
                 prefabAsset.GetComponentInChildren<AchievementUIManager>(true) == null))
            {
                continue;
            }

            EditorUtility.DisplayProgressBar(
                "Refreshing Achievement Database",
                $"Checking prefab {i + 1} of {prefabGuids.Length}",
                prefabGuids.Length == 0 ? 0f : (float)i / prefabGuids.Length);

            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (!RefreshManagers(prefabRoot, achievements))
                    continue;

                PrefabUtility.SaveAsPrefabAsset(prefabRoot, path);
                updatedCount++;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        return updatedCount;
    }

    private static int RefreshScenes(List<AchievementSO> achievements)
    {
        string[] sceneGuids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" });
        int updatedCount = 0;

        for (int i = 0; i < sceneGuids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(sceneGuids[i]);
            EditorUtility.DisplayProgressBar(
                "Refreshing Achievement Database",
                $"Checking scene {i + 1} of {sceneGuids.Length}",
                sceneGuids.Length == 0 ? 1f : (float)i / sceneGuids.Length);

            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            // OnValidate may have refreshed a list while the scene was opening.
            // Preserve that valid refresh by also respecting the scene's dirty flag.
            bool changed = scene.isDirty;

            foreach (GameObject root in scene.GetRootGameObjects())
                changed |= RefreshManagers(root, achievements);

            if (!changed)
                continue;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            updatedCount++;
        }

        return updatedCount;
    }

    private static bool RefreshManagers(GameObject root, List<AchievementSO> achievements)
    {
        bool changed = false;

        foreach (PlayerDataManager manager in root.GetComponentsInChildren<PlayerDataManager>(true))
        {
            if (ListsMatch(manager.allGameAchievements, achievements))
            {
                changed |= EditorUtility.IsDirty(manager);
                continue;
            }

            manager.allGameAchievements = new List<AchievementSO>(achievements);
            EditorUtility.SetDirty(manager);
            changed = true;
        }

        foreach (AchievementUIManager manager in root.GetComponentsInChildren<AchievementUIManager>(true))
        {
            if (ListsMatch(manager.allAchievements, achievements))
            {
                changed |= EditorUtility.IsDirty(manager);
                continue;
            }

            manager.allAchievements = new List<AchievementSO>(achievements);
            EditorUtility.SetDirty(manager);
            changed = true;
        }

        return changed;
    }

    private static bool ListsMatch(
        IReadOnlyList<AchievementSO> current,
        IReadOnlyList<AchievementSO> expected)
    {
        if (current == null || current.Count != expected.Count)
            return false;

        for (int i = 0; i < expected.Count; i++)
        {
            if (current[i] != expected[i])
                return false;
        }

        return true;
    }
}
#endif
