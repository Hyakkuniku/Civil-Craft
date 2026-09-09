using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.AI.Navigation;

public static class RavineNavMeshLinksEditor
{
    [MenuItem("Tools/Civil Craft/Repair Canyon Ravine Links")]
    private static void Repair()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var scene = SceneManager.GetActiveScene();
        if (scene.name != "CanyonCrossing") return;
        int repaired = 0, unresolved = 0;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name != "NavMeshLinks") continue;
            foreach (NavMeshLink link in root.GetComponentsInChildren<NavMeshLink>())
            {
                if (!link.isActiveAndEnabled) continue;
                if (!RavineNavMeshLinks.TryResolve(link, 1.5f, out Vector3 start, out Vector3 end))
                {
                    unresolved++;
                    Debug.LogWarning($"{link.name}: endpoint is outside the nearby baked bank. Select this warning to inspect it; bake the world surface before retrying.", link);
                    continue;
                }
                Undo.RecordObject(link, "Snap ravine link to baked banks");
                RavineNavMeshLinks.Apply(link, start, end);
                EditorUtility.SetDirty(link);
                PrefabUtility.RecordPrefabInstancePropertyModifications(link);
                repaired++;
            }
        }
        if (repaired > 0) EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"Ravine links: {repaired} snapped to baked banks, {unresolved} need inspection. Save the scene after reviewing.");
    }
}
