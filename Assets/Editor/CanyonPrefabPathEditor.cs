using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[CustomEditor(typeof(CanyonPrefabPath))]
public sealed class CanyonPrefabPathEditor : Editor
{
    private const int Limit = 500;
    private bool pending;
    private int selectedPoint = -1;
    private int dragGroup = -1;
    private CanyonPrefabPath Path => (CanyonPrefabPath)target;

    [MenuItem("Tools/Civil Craft/Create Canyon Path")]
    public static void Create()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || PrefabStageUtility.GetCurrentPrefabStage() != null) return;
        GameObject go = new GameObject("Canyon Prefab Path");
        Undo.RegisterCreatedObjectUndo(go, "Create Canyon Path");
        CanyonPrefabPath path = Undo.AddComponent<CanyonPrefabPath>(go);
        if (Selection.activeObject is GameObject asset && EditorUtility.IsPersistent(asset) && PrefabUtility.IsPartOfPrefabAsset(asset)) path.prefab = asset;
        if (SceneView.lastActiveSceneView != null) go.transform.position = SceneView.lastActiveSceneView.pivot;
        Selection.activeGameObject = go;
    }

    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox("Assign a canyon prefab, then move the numbered points in Scene view. Shift + left-click adds a point on the horizontal plane through the path origin. Esc cancels a pending rebuild. Generated pieces remain ordinary prefab instances.", MessageType.Info);
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            serializedObject.Update();
            EditorGUI.BeginChangeCheck();
            DrawPropertiesExcluding(serializedObject, "m_Script", "generated");
            bool changed = EditorGUI.EndChangeCheck();
            serializedObject.ApplyModifiedProperties();
            if (changed && Path.liveUpdate) Rebuild();
            EditorGUILayout.HelpBox("Automatic spacing measures the prefab along the path direction after Yaw Offset. Try 90 degrees if the cliff faces the wrong way. Height Offset can sink the base; disable Seat Bounds to preserve the prefab pivot height.", MessageType.None);
            if (GUILayout.Button("Rebuild Path")) Rebuild();
            if (GUILayout.Button("Add Point At End"))
            {
                Undo.RecordObject(Path, "Add Path Point");
                int n = Path.points.Count;
                Path.points.Add(n > 1 ? Path.points[n - 1] + (Path.points[n - 1] - Path.points[n - 2]) : n == 1 ? Path.points[0] + Vector3.forward * 20 : Vector3.zero);
                Changed();
            }
            using (new EditorGUI.DisabledScope(selectedPoint < 0 || selectedPoint >= Path.points.Count || Path.points.Count <= 2))
                if (GUILayout.Button("Remove Selected Point"))
                {
                    Undo.RecordObject(Path, "Remove Path Point");
                    Path.points.RemoveAt(selectedPoint);
                    selectedPoint = -1;
                    Changed();
                }
            EditorGUILayout.LabelField("Generated pieces", Path.generated.Count.ToString());
            if (GUILayout.Button("Select Generated Pieces"))
                Selection.objects = Path.generated.FindAll(go => go != null).ToArray();
        }
    }

    private void Changed()
    {
        EditorUtility.SetDirty(Path);
        if (Path.liveUpdate) Rebuild();
        SceneView.RepaintAll();
        Repaint();
    }

    private void OnSceneGUI()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        CanyonPrefabPath path = Path;
        Event e = Event.current;
        List<Vector3> curve = Sample(path);
        Handles.color = new Color(1, 0.65f, 0.1f);
        if (curve.Count > 1) Handles.DrawAAPolyLine(3, curve.ToArray());
        if (e.shift && !e.alt)
        {
            int id = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(id);
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                if (new Plane(Vector3.up, path.transform.position).Raycast(ray, out float d))
                {
                    Undo.RecordObject(path, "Add Path Point");
                    path.points.Add(path.transform.InverseTransformPoint(ray.GetPoint(d)));
                    selectedPoint = path.points.Count - 1;
                    Changed();
                    e.Use();
                }
            }
        }
        for (int i = 0; i < path.points.Count; i++)
        {
            Vector3 world = path.transform.TransformPoint(path.points[i]);
            Handles.Label(world + Vector3.up, "Point " + (i + 1));
            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.PositionHandle(world, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                if (dragGroup < 0) dragGroup = Undo.GetCurrentGroup();
                Undo.RecordObject(path, "Move Path Point");
                path.points[i] = path.transform.InverseTransformPoint(moved);
                selectedPoint = i;
                pending = true;
                EditorUtility.SetDirty(path);
                Repaint();
            }
        }
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { pending = false; dragGroup = -1; }
        if (e.rawType == EventType.MouseUp && pending)
        {
            if (path.liveUpdate) Rebuild();
            if (dragGroup >= 0) Undo.CollapseUndoOperations(dragGroup);
            pending = false;
            dragGroup = -1;
        }
    }

    // Fixed subdivisions create an arc-length lookup, so placements follow distance rather than control-point spacing.
    public static List<Vector3> Sample(CanyonPrefabPath path)
    {
        List<Vector3> result = new List<Vector3>();
        int count = path.points.Count;
        if (count < 2) return result;
        int segments = path.closed ? count : count - 1;
        for (int i = 0; i < segments; i++)
        {
            Vector3 a = Point(path, i - 1), b = Point(path, i), c = Point(path, i + 1), d = Point(path, i + 2);
            int subdivisions = path.smooth ? 32 : 1;
            for (int j = 0; j < subdivisions; j++)
            {
                float t = (float)j / subdivisions;
                Vector3 local = path.smooth ? 0.5f * ((2 * b) + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t * t + (-a + 3 * b - 3 * c + d) * t * t * t) : b;
                result.Add(path.transform.TransformPoint(local));
            }
        }
        result.Add(path.transform.TransformPoint(path.closed ? path.points[0] : path.points[count - 1]));
        return result;
    }

    private static Vector3 Point(CanyonPrefabPath path, int index)
    {
        int count = path.points.Count;
        return path.points[path.closed ? (index + count) % count : Mathf.Clamp(index, 0, count - 1)];
    }

    private void Rebuild()
    {
        CanyonPrefabPath path = Path;
        if (path.prefab == null || !EditorUtility.IsPersistent(path.prefab) || !PrefabUtility.IsPartOfPrefabAsset(path.prefab))
        { Debug.LogWarning("Canyon Path: assign a prefab asset from the Project window.", path); return; }
        if (PrefabStageUtility.GetCurrentPrefabStage() != null || !path.gameObject.scene.IsValid()) return;
        Vector3 scale = path.transform.lossyScale;
        if ((scale - Vector3.one).sqrMagnitude > 0.0001f)
        { Debug.LogWarning("Canyon Path: keep the path and its parents at scale 1. Use Uniform Scale for the pieces.", path); return; }
        List<Vector3> curve = Sample(path);
        if (curve.Count < 2) return;
        Quaternion localRotation = Quaternion.Euler(0, path.yawOffset, 0) * path.prefab.transform.localRotation;
        Vector3 localScale = path.prefab.transform.localScale * Mathf.Max(0.01f, path.uniformScale);
        Bounds bounds = PrefabBounds(path.prefab, localRotation, localScale);
        float step = path.automaticSpacing ? Mathf.Max(0.1f, bounds.size.z * (1 - Mathf.Clamp(path.overlap, 0, 0.75f))) : Mathf.Max(0.1f, path.spacing);
        float length = 0;
        for (int i = 1; i < curve.Count; i++) length += Vector3.Distance(curve[i - 1], curve[i]);
        if (length < 0.01f) return;
        int count = Mathf.Max(1, Mathf.CeilToInt(length / step));
        if (count > Limit) { Debug.LogWarning("Canyon Path: more than 500 pieces requested. Increase spacing or prefab scale.", path); return; }
        Undo.RecordObject(path, "Rebuild Canyon Path");
        // Only delete objects recorded by this tool and still parented to this path.
        foreach (GameObject old in path.generated)
            if (old != null && old.transform.parent == path.transform) Undo.DestroyObjectImmediate(old);
        path.generated.Clear();
        float interval = length / count;
        int segment = 1;
        float traversed = 0;
        for (int i = 0; i < count; i++)
        {
            float distance = (i + 0.5f) * interval;
            while (segment < curve.Count - 1 && traversed + Vector3.Distance(curve[segment - 1], curve[segment]) < distance)
            { traversed += Vector3.Distance(curve[segment - 1], curve[segment]); segment++; }
            Vector3 delta = curve[segment] - curve[segment - 1];
            Vector3 position = Vector3.Lerp(curve[segment - 1], curve[segment], delta.magnitude > 0 ? (distance - traversed) / delta.magnitude : 0);
            Vector3 direction = Vector3.ProjectOnPlane(delta, Vector3.up).normalized;
            if (direction.sqrMagnitude < 0.001f) direction = Vector3.forward;
            Quaternion heading = Quaternion.LookRotation(direction, Vector3.up);
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(path.prefab, path.gameObject.scene);
            Undo.RegisterCreatedObjectUndo(instance, "Place Canyon Piece");
            Undo.SetTransformParent(instance.transform, path.transform, "Parent Canyon Piece");
            Undo.RecordObject(instance.transform, "Position Canyon Piece");
            instance.transform.localScale = localScale;
            instance.transform.rotation = heading * localRotation;
            // Center even off-center prefab pivots along the path; optionally rest bounds on its height.
            instance.transform.position = position - heading * new Vector3(bounds.center.x, path.seatBounds ? bounds.min.y : 0, bounds.center.z) + Vector3.up * path.heightOffset;
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
            path.generated.Add(instance);
        }
        EditorUtility.SetDirty(path);
        EditorSceneManager.MarkSceneDirty(path.gameObject.scene);
    }

    private static Bounds PrefabBounds(GameObject prefab, Quaternion rotation, Vector3 scale)
    {
        bool found = false;
        Bounds bounds = new Bounds(Vector3.zero, Vector3.one);
        Matrix4x4 root = Matrix4x4.TRS(Vector3.zero, rotation, scale);
        foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
        {
            Matrix4x4 matrix = root * prefab.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
            Bounds local = renderer.localBounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = matrix.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                if (!found) { bounds = new Bounds(corner, Vector3.zero); found = true; }
                else bounds.Encapsulate(corner);
            }
        }
        return bounds;
    }
}
