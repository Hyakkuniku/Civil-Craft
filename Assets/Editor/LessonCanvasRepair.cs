#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class LessonCanvasRepair
{
    static LessonCanvasRepair()
    {
        EditorApplication.delayCall += RepairLoadedScenes;
        EditorSceneManager.sceneOpened -= HandleSceneOpened;
        EditorSceneManager.sceneOpened += HandleSceneOpened;
    }

    private static void HandleSceneOpened(Scene scene, OpenSceneMode mode)
    {
        RepairScene(scene);
    }

    private static void RepairLoadedScenes()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        for (int i = 0; i < SceneManager.sceneCount; i++)
            RepairScene(SceneManager.GetSceneAt(i));
    }

    private static void RepairScene(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Transform lessonCanvas = FindChild(root.transform, "LessonCanvas");
            if (lessonCanvas == null || !IsCollapsed(lessonCanvas.localScale))
                continue;

            Undo.RecordObject(lessonCanvas, "Repair Lesson Canvas Scale");
            lessonCanvas.localScale = Vector3.one;
            EditorUtility.SetDirty(lessonCanvas);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"Repaired the zero-scale LessonCanvas in {scene.name}.", lessonCanvas);
            return;
        }
    }

    private static bool IsCollapsed(Vector3 scale)
    {
        return Mathf.Abs(scale.x) <= 0.0001f ||
               Mathf.Abs(scale.y) <= 0.0001f ||
               Mathf.Abs(scale.z) <= 0.0001f;
    }

    private static Transform FindChild(Transform current, string targetName)
    {
        if (current.name == targetName)
            return current;

        for (int i = 0; i < current.childCount; i++)
        {
            Transform result = FindChild(current.GetChild(i), targetName);
            if (result != null)
                return result;
        }

        return null;
    }
}
#endif
