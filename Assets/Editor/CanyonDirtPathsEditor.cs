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
    private static readonly string[] SurfaceNames =
        { "Dirt", "Road", "Stone", "Custom" };
    private static readonly string[] ShaderNames =
    {
        "Civil Craft/Canyon Dirt Surface",
        "Civil Craft/Canyon Road Surface",
        "Civil Craft/Canyon Stone Path"
    };

    public override void OnInspectorGUI()
    {
        var paths = (CanyonDirtPaths)target;
        serializedObject.Update();
        SerializedProperty shaderProperty = serializedObject.FindProperty("surfaceShader");
        var currentShader = shaderProperty.objectReferenceValue as Shader;
        int surfaceType = SurfaceType(currentShader);
        EditorGUI.BeginChangeCheck();
        int selectedType = EditorGUILayout.Popup("Default Surface Type", surfaceType, SurfaceNames);
        bool typeChanged = EditorGUI.EndChangeCheck();
        if (typeChanged)
        {
            if (selectedType == ShaderNames.Length)
                shaderProperty.objectReferenceValue = null;
            else
            {
                Shader selectedShader = Shader.Find(ShaderNames[selectedType]);
                if (selectedShader != null)
                    shaderProperty.objectReferenceValue = selectedShader;
                else
                    EditorGUILayout.HelpBox("This surface shader is missing from the project.", MessageType.Error);
            }
        }
        EditorGUILayout.PropertyField(shaderProperty, new GUIContent("Default Surface Shader", "Streets without their own shader use this one."));
        var displayShader = shaderProperty.objectReferenceValue as Shader;
        SerializedProperty streetsProperty = serializedObject.FindProperty("streets");
        EditorGUILayout.HelpBox(
            "Expand Streets and set Surface Shader on any street that should differ from the default. Dirt, road and stone can appear on the same canyon. The mesh, colliders and NavMesh stay unchanged.",
            MessageType.Info);
        DrawPropertiesExcluding(serializedObject, "m_Script", "surfaceShader", "streets", "dirtTint", "roadTint",
            "sidewalkWidth", "sidewalkTint", "curbTint", "stoneTint", "groutTint",
            "stoneVariation", "stonesAcross", "stoneAspect", "jointWidth");
        EditorGUILayout.PropertyField(streetsProperty, true);
        bool usesRoad = UsesSurface(streetsProperty, displayShader, 1);
        bool usesStone = UsesSurface(streetsProperty, displayShader, 2);
        bool usesDirt = UsesSurface(streetsProperty, displayShader, 0);
        if (usesRoad)
        {
            EditorGUILayout.LabelField("Road Appearance", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("roadTint"), new GUIContent("Road Tint"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("sidewalkWidth"), new GUIContent("Sidewalk Width"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("sidewalkTint"), new GUIContent("Sidewalk Color"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("curbTint"), new GUIContent("Curb Color"));
        }
        if (usesStone)
        {
            EditorGUILayout.LabelField("Stone Appearance", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("stoneTint"), new GUIContent("Stone Color"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("groutTint"), new GUIContent("Grout Color"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("stoneVariation"), new GUIContent("Block Color Variation"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("stonesAcross"), new GUIContent("Blocks Across Path"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("stoneAspect"), new GUIContent("Block Length / Width"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("jointWidth"), new GUIContent("Joint Width"));
        }
        if (usesDirt)
        {
            EditorGUILayout.LabelField("Dirt Appearance", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("dirtTint"), new GUIContent("Dirt Color"));
        }
        if (serializedObject.ApplyModifiedProperties())
        {
            paths.Refresh();
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
        }
    }

    private static int SurfaceType(Shader shader)
    {
        if (shader == null) return ShaderNames.Length;
        for (int i = 0; i < ShaderNames.Length; i++)
            if (shader.name == ShaderNames[i]) return i;
        return ShaderNames.Length;
    }

    private static bool UsesSurface(SerializedProperty streets, Shader defaultShader, int type)
    {
        for (int i = 0; i < Mathf.Min(streets.arraySize, 16); i++)
        {
            var shaderProperty = streets.GetArrayElementAtIndex(i).FindPropertyRelative("surfaceShader");
            var shader = shaderProperty.objectReferenceValue as Shader;
            if (SurfaceType(shader != null ? shader : defaultShader) == type) return true;
        }
        return false;
    }

    private void OnSceneGUI()
    {
        var paths = (CanyonDirtPaths)target;
        if (paths.canyon == null || paths.streets == null) return;
        Bounds b = paths.SurfaceBounds;
        if (b.size.x < .001f || b.size.z < .001f) return;
        for (int i = 0; i < Mathf.Min(paths.streets.Length,16); i++)
        {
            var street = paths.streets[i];
            Shader shader = street.surfaceShader != null ? street.surfaceShader : paths.surfaceShader;
            bool isRoad = SurfaceType(shader) == 1;
            bool isStone = SurfaceType(shader) == 2;
            Handles.color = isRoad ? new Color(.55f, .62f, .67f) :
                isStone ? new Color(.86f, .76f, .63f) : new Color(.85f,.60f,.23f);
            Vector3 a = World(street.from,b), z = World(street.to,b);
            Handles.DrawAAPolyLine(3,a,z);
            EditorGUI.BeginChangeCheck();
            a = Handles.PositionHandle(a,Quaternion.identity);
            z = Handles.PositionHandle(z,Quaternion.identity);
            if (!EditorGUI.EndChangeCheck()) continue;
            Undo.RecordObject(paths, isRoad ? "Move Road Route" :
                isStone ? "Move Stone Path" : "Move Dirt Street");
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
