using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class CivilCraftPrefabBrush : EditorWindow
{
    [SerializeField] private List<GameObject> prefabs = new List<GameObject>();
    [SerializeField] private Transform parent;
    [SerializeField] private float radius = 0f, spacing = 4f, planeHeight, offset;
    [SerializeField] private Vector2 scaleRange = Vector2.one;
    [SerializeField] private bool randomYaw = true, alignToSurface, usePlane = true, seatOnSurface = true;
    private bool painting;
    private int stroke = -1, control;
    private Vector3 lastPosition;
    private Vector2 scroll;
    private readonly System.Random random = new System.Random();

    [MenuItem("Tools/Civil Craft/Prefab Brush")]
    public static void Open() => GetWindow<CivilCraftPrefabBrush>("Prefab Brush");

    private void OnEnable() => SceneView.duringSceneGui += DuringSceneGUI;
    private void OnDisable()
    {
        EndStroke();
        SceneView.duringSceneGui -= DuringSceneGUI;
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.HelpBox("Add prefab assets below. Enable painting, then left-click or drag in Scene view. Alt/right mouse navigate. Esc stops painting. Ctrl+Z undoes a stroke.", MessageType.Info);
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            bool next = GUILayout.Toggle(painting, painting ? "Painting ON — click to stop" : "Enable Painting", "Button", GUILayout.Height(32));
            if (next != painting) { EndStroke(); painting = next; SceneView.RepaintAll(); }
        }
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Prefab palette", EditorStyles.boldLabel);
        for (int i = 0; i < prefabs.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            GameObject candidate = (GameObject)EditorGUILayout.ObjectField(prefabs[i], typeof(GameObject), false);
            prefabs[i] = IsPrefab(candidate) ? candidate : null;
            if (GUILayout.Button("Remove", GUILayout.Width(65))) { prefabs.RemoveAt(i); i--; }
            EditorGUILayout.EndHorizontal();
        }
        if (GUILayout.Button("Add Prefab Slot")) prefabs.Add(null);
        if (GUILayout.Button("Add Selected Project Prefabs"))
            foreach (Object selected in Selection.objects)
                if (selected is GameObject go && IsPrefab(go) && !prefabs.Contains(go)) prefabs.Add(go);
        if (GUILayout.Button("Load Canyon Rock Prefabs"))
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/MAPS/Canyon Crossing/Environemnt" }))
            {
                GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (IsPrefab(go) && !prefabs.Contains(go)) prefabs.Add(go);
            }

        EditorGUILayout.Space();
        parent = (Transform)EditorGUILayout.ObjectField("Output Parent", parent, typeof(Transform), true);
        if (parent != null && EditorUtility.IsPersistent(parent)) parent = null;
        EditorGUILayout.HelpBox("Leave parent empty to create a Painted Prefabs group. Collider painting ignores objects inside the output group, so rocks do not stack on earlier brush placements.", MessageType.None);
        spacing = Mathf.Max(0.1f, EditorGUILayout.FloatField("Stroke Spacing", spacing));
        radius = Mathf.Max(0f, EditorGUILayout.FloatField("Scatter Radius", radius));
        scaleRange = EditorGUILayout.Vector2Field("Uniform Scale Min / Max", scaleRange);
        scaleRange.x = Mathf.Max(0.01f, scaleRange.x);
        scaleRange.y = Mathf.Max(scaleRange.x, scaleRange.y);
        randomYaw = EditorGUILayout.Toggle("Random Y Rotation", randomYaw);
        alignToSurface = EditorGUILayout.Toggle("Align To Surface", alignToSurface);
        seatOnSurface = EditorGUILayout.Toggle("Seat Bounds On Surface", seatOnSurface);
        offset = EditorGUILayout.FloatField("Surface Offset", offset);
        usePlane = EditorGUILayout.Toggle("Paint On Flat Plane", usePlane);
        if (usePlane) planeHeight = EditorGUILayout.FloatField("Plane Height (World Y)", planeHeight);
        else EditorGUILayout.HelpBox("Surfaces need a 3D collider. Use the flat plane for empty areas or canyon meshes without colliders.", MessageType.Info);
        EditorGUILayout.EndScrollView();
    }

    private static bool IsPrefab(GameObject go) => go != null && EditorUtility.IsPersistent(go) && PrefabUtility.IsPartOfPrefabAsset(go);

    private bool Surface(Ray ray, out Vector3 position, out Vector3 normal)
    {
        position = default;
        normal = Vector3.up;
        if (usePlane)
        {
            if (!new Plane(Vector3.up, new Vector3(0, planeHeight, 0)).Raycast(ray, out float distance)) return false;
            position = ray.GetPoint(distance);
            return true;
        }
        float nearest = float.PositiveInfinity;
        foreach (RaycastHit hit in Physics.RaycastAll(ray, Mathf.Infinity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.distance >= nearest || (parent != null && hit.transform.IsChildOf(parent)) ||
                SceneVisibilityManager.instance.IsHidden(hit.transform.gameObject)) continue;
            nearest = hit.distance;
            position = hit.point;
            normal = hit.normal;
        }
        return !float.IsPositiveInfinity(nearest);
    }

    private void DuringSceneGUI(SceneView view)
    {
        if (!painting || EditorApplication.isPlayingOrWillChangePlaymode || PrefabStageUtility.GetCurrentPrefabStage() != null)
        { EndStroke(); return; }
        Event e = Event.current;
        control = GUIUtility.GetControlID(FocusType.Passive);
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
        { EndStroke(); painting = false; e.Use(); Repaint(); return; }
        if (e.rawType == EventType.MouseUp && e.button == 0) EndStroke();
        if (e.alt || e.button == 1 || e.button == 2) return;
        if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(control);
        if (!Surface(HandleUtility.GUIPointToWorldRay(e.mousePosition), out Vector3 position, out Vector3 normal)) return;
        if (e.type == EventType.Repaint)
        {
            Handles.color = new Color(1f, 0.7f, 0.15f, 1f);
            Handles.DrawWireDisc(position, normal, Mathf.Max(radius, HandleUtility.GetHandleSize(position) * 0.15f));
            Handles.DrawLine(position, position + normal * HandleUtility.GetHandleSize(position) * 0.5f);
        }
        if (e.type == EventType.MouseMove) view.Repaint();
        bool down = e.type == EventType.MouseDown && e.button == 0;
        bool drag = e.type == EventType.MouseDrag && e.button == 0 && stroke >= 0;
        if (!down && !drag) return;
        if (!prefabs.Exists(IsPrefab)) { ShowNotification(new GUIContent("Add at least one prefab first.")); return; }
        if (down)
        {
            Undo.IncrementCurrentGroup();
            stroke = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Paint Prefabs");
            GUIUtility.hotControl = control;
        }
        if (down || Vector3.Distance(lastPosition, position) >= spacing)
        {
            Place(position, normal);
            lastPosition = position;
        }
        e.Use();
        view.Repaint();
    }

    private void Place(Vector3 position, Vector3 normal)
    {
        if (radius > 0f)
        {
            float angle = (float)random.NextDouble() * Mathf.PI * 2f;
            float distance = Mathf.Sqrt((float)random.NextDouble()) * radius;
            Vector3 tangent = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 sample = position + (tangent * Mathf.Cos(angle) + Vector3.Cross(normal, tangent) * Mathf.Sin(angle)) * distance;
            if (usePlane) position = sample;
            else if (!Surface(new Ray(sample + normal * (radius + 10f), -normal), out position, out normal)) return;
        }
        List<GameObject> valid = prefabs.FindAll(IsPrefab);
        if (parent == null)
        {
            GameObject group = new GameObject("Painted Prefabs");
            Undo.RegisterCreatedObjectUndo(group, "Create Brush Group");
            parent = group.transform;
        }
        Scene scene = parent.gameObject.scene;
        if (!scene.IsValid() || !scene.isLoaded) return;
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(valid[random.Next(valid.Count)], scene);
        Undo.RegisterCreatedObjectUndo(instance, "Paint Prefab");
        Undo.SetTransformParent(instance.transform, parent, "Parent Painted Prefab");
        Undo.RecordObject(instance.transform, "Position Painted Prefab");
        Quaternion rotation = alignToSurface ? Quaternion.FromToRotation(Vector3.up, normal) : Quaternion.identity;
        instance.transform.rotation = rotation * Quaternion.Euler(0, randomYaw ? (float)random.NextDouble() * 360f : 0, 0) * instance.transform.rotation;
        instance.transform.localScale *= Mathf.Lerp(scaleRange.x, scaleRange.y, (float)random.NextDouble());
        instance.transform.position = position;
        if (seatOnSurface)
        {
            float lowest = float.PositiveInfinity;
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>())
            {
                Bounds bounds = renderer.localBounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    lowest = Mathf.Min(lowest, Vector3.Dot(renderer.transform.TransformPoint(corner) - position, normal));
                }
            }
            if (!float.IsPositiveInfinity(lowest)) instance.transform.position -= normal * lowest;
        }
        instance.transform.position += normal * offset;
        PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
        EditorSceneManager.MarkSceneDirty(scene);
    }

    private void EndStroke()
    {
        if (stroke < 0) return;
        Undo.CollapseUndoOperations(stroke);
        stroke = -1;
        if (GUIUtility.hotControl == control) GUIUtility.hotControl = 0;
    }
}
