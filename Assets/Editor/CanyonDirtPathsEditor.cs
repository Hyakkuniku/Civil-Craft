using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Independent of inspector selection: Ctrl+S must restore every enabled overlay.
[InitializeOnLoad]
internal static class CanyonDirtPathsSaveRefresh
{
    static CanyonDirtPathsSaveRefresh()
    {
        EditorSceneManager.sceneSaved += OnSceneSaved;
    }

    private static void OnSceneSaved(Scene scene)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        // Never create/destroy generated objects inside scene serialization.
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                !scene.IsValid() || !scene.isLoaded) return;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (CanyonDirtPaths paths in root.GetComponentsInChildren<CanyonDirtPaths>(true))
                    if (paths != null && paths.isActiveAndEnabled)
                        paths.RebuildGeneratedSurface();
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
        };
    }
}

[CustomEditor(typeof(CanyonDirtPaths))]
public sealed class CanyonDirtPathsEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var paths = (CanyonDirtPaths)target;
        bool isRoad = paths.surfaceShader != null && paths.surfaceShader.name == "Civil Craft/Canyon Road Surface";
        EditorGUILayout.HelpBox(isRoad
            ? "Visual road only: the canyon mesh, colliders and NavMesh stay unchanged. Move the route endpoints in Scene view. Width is relative to canyon size."
            : "Visual dirt only: original materials, cliff mesh and NavMesh stay unchanged. Move the street endpoints in Scene view. Width is relative to canyon size. Disable this component to remove the effect.", MessageType.Info);
        serializedObject.Update();
        DrawPropertiesExcluding(serializedObject, "m_Script", "dirtTint", "roadTint",
            "sidewalkWidth", "sidewalkTint", "curbTint");
        if (isRoad)
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("roadTint"), new GUIContent("Road Tint"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("sidewalkWidth"), new GUIContent("Sidewalk Width"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("sidewalkTint"), new GUIContent("Sidewalk Color"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("curbTint"), new GUIContent("Curb Color"));
        }
        else
            EditorGUILayout.PropertyField(serializedObject.FindProperty("dirtTint"), new GUIContent("Dirt Tint"));
        serializedObject.ApplyModifiedProperties();
    }

    private void OnSceneGUI()
    {
        var paths = (CanyonDirtPaths)target;
        if (paths.canyon == null || paths.streets == null) return;
        Bounds b = paths.SurfaceBounds;
        if (b.size.x < .001f || b.size.z < .001f) return;
        bool isRoad = paths.surfaceShader != null && paths.surfaceShader.name == "Civil Craft/Canyon Road Surface";
        Handles.color = isRoad ? new Color(.55f, .62f, .67f) : new Color(.85f,.60f,.23f);
        for (int i = 0; i < Mathf.Min(paths.streets.Length,16); i++)
        {
            var street = paths.streets[i];
            Vector3 a = World(street.from,b), z = World(street.to,b);
            Handles.DrawAAPolyLine(3,a,z);
            EditorGUI.BeginChangeCheck();
            a = Handles.PositionHandle(a,Quaternion.identity);
            z = Handles.PositionHandle(z,Quaternion.identity);
            if (!EditorGUI.EndChangeCheck()) continue;
            Undo.RecordObject(paths, isRoad ? "Move Road Route" : "Move Dirt Street");
            street.from = Normalized(a,b);
            street.to = Normalized(z,b);
            paths.streets[i] = street;
            EditorUtility.SetDirty(paths);
            paths.Refresh();
        }
    }
    private static Vector3 World(Vector2 v, Bounds b) => new Vector3(b.min.x+v.x*b.size.x,b.max.y+.1f,b.min.z+v.y*b.size.z);
    private static Vector2 Normalized(Vector3 v, Bounds b) => new Vector2((v.x-b.min.x)/b.size.x,(v.z-b.min.z)/b.size.z);
}
