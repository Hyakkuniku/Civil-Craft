using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.AI.Navigation;
using Unity.AI.Navigation.Editor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class WorldNavigationBake
{
    [MenuItem("Tools/Civil Craft/Bake Canyon World Navigation")]
    public static void Bake()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { Debug.LogWarning("Exit Play Mode before baking world navigation."); return; }
        var scene = SceneManager.GetActiveScene();
        if (scene.name != "CanyonCrossing")
        { Debug.LogWarning("Open CanyonCrossing before baking."); return; }
        NavMeshSurface surface = null;
        foreach (var root in scene.GetRootGameObjects())
            foreach (var candidate in root.GetComponentsInChildren<NavMeshSurface>(true))
                if (candidate.name == "Runtimie Navigation") surface = candidate;
        if (surface == null) { Debug.LogError("World NavMesh Surface was not found."); return; }
        Undo.RecordObject(surface, "Configure full world navigation bake");
        surface.collectObjects = CollectObjects.All;
        surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
        // Preserve the authored layer filter. Runtime bridges remain a separate surface.
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = surface.gameObject;
        NavMeshAssetManager.instance.StartBakingSurfaces(new Object[] { surface });
        Debug.Log("World Surface bake started. Wait for baking to finish, then save the scene. Do not use the obsolete Navigation Bake window.", surface);
    }
}
