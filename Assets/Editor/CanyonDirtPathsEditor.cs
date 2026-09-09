using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(CanyonDirtPaths))]
public sealed class CanyonDirtPathsEditor : Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox("Visual dirt only: original materials, cliff mesh and NavMesh stay unchanged. Move the street endpoints in Scene view. Width is relative to canyon size. Disable this component to remove the effect.", MessageType.Info);
        DrawDefaultInspector();
    }

    private void OnSceneGUI()
    {
        var paths = (CanyonDirtPaths)target;
        if (paths.canyon == null || paths.streets == null) return;
        Bounds b = paths.SurfaceBounds;
        if (b.size.x < .001f || b.size.z < .001f) return;
        Handles.color = new Color(.85f,.60f,.23f);
        for (int i = 0; i < Mathf.Min(paths.streets.Length,16); i++)
        {
            var street = paths.streets[i];
            Vector3 a = World(street.from,b), z = World(street.to,b);
            Handles.DrawAAPolyLine(3,a,z);
            EditorGUI.BeginChangeCheck();
            a = Handles.PositionHandle(a,Quaternion.identity);
            z = Handles.PositionHandle(z,Quaternion.identity);
            if (!EditorGUI.EndChangeCheck()) continue;
            Undo.RecordObject(paths,"Move Dirt Street");
            paths.streets[i] = new CanyonDirtPaths.Street(Normalized(a,b),Normalized(z,b));
            EditorUtility.SetDirty(paths);
            paths.Refresh();
        }
    }
    private static Vector3 World(Vector2 v, Bounds b) => new Vector3(b.min.x+v.x*b.size.x,b.max.y+.1f,b.min.z+v.y*b.size.z);
    private static Vector2 Normalized(Vector3 v, Bounds b) => new Vector2((v.x-b.min.x)/b.size.x,(v.z-b.min.z)/b.size.z);
}
