using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public sealed class BuildLocationLayoutHelper : EditorWindow
{
    private BuildLocation location;
    private Point leftAnchor, rightAnchor;
    private Transform movingBank;
    private BridgeMaterialSO road;
    private float segmentLength = 10f;
    private int segments = 5;
    private bool preview;
    private Vector3 leftPosition, rightPosition, bankDelta;
    private string report = "Assign the location, two anchors, and the right-hand ravine bank.";

    [MenuItem("Tools/Civil Craft/Build Location Builder")]
    public static void Open() => GetWindow<BuildLocationLayoutHelper>("Build Location Builder");

    private void OnEnable() { SceneView.duringSceneGui += DrawPreview; }
    private void OnDisable() { SceneView.duringSceneGui -= DrawPreview; }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox("Exact road spans along world X. The left bank stays fixed; only the explicitly assigned right bank and the two anchors move. Existing bridges/tutorial ghosts are not rebuilt.", MessageType.Info);
        EditorGUI.BeginChangeCheck();
        location = (BuildLocation)EditorGUILayout.ObjectField("Build Location", location, typeof(BuildLocation), true);
        leftAnchor = (Point)EditorGUILayout.ObjectField("Left Anchor", leftAnchor, typeof(Point), true);
        rightAnchor = (Point)EditorGUILayout.ObjectField("Right Anchor", rightAnchor, typeof(Point), true);
        movingBank = (Transform)EditorGUILayout.ObjectField("Right Bank Root", movingBank, typeof(Transform), true);
        road = (BridgeMaterialSO)EditorGUILayout.ObjectField("Road Material (optional)", road, typeof(BridgeMaterialSO), false);
        using (new EditorGUI.DisabledScope(road != null))
            segmentLength = EditorGUILayout.FloatField("Road Segment Length", road != null ? road.maxLength : segmentLength);
        segments = EditorGUILayout.IntField("Number of Road Pieces", segments);
        if (EditorGUI.EndChangeCheck()) { preview = false; SceneView.RepaintAll(); }
        EditorGUILayout.LabelField("Exact Anchor Span", $"{segments} × {Length:0.###} = {segments * Length:0.###} units");
        using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
        {
            if (GUILayout.Button("Use Location's First Anchor Pair") && location != null)
            {
                leftAnchor = location.startingAnchors.Count > 0 ? location.startingAnchors[0] : null;
                rightAnchor = location.endingAnchors.Count > 0 ? location.endingAnchors[0] : null;
                preview = false;
            }
            if (GUILayout.Button("Add Missing Edge Snap Components"))
            {
                foreach (var anchor in new[] { leftAnchor, rightAnchor })
                    if (anchor != null && anchor.gameObject.scene.IsValid() && anchor.GetComponent<AnchorEdgeSnap>() == null)
                        Undo.AddComponent<AnchorEdgeSnap>(anchor.gameObject);
                preview = false;
            }
            if (GUILayout.Button("Preview Exact Gap")) Measure();
            using (new EditorGUI.DisabledScope(!preview))
                if (GUILayout.Button("Apply Bank + Anchor Layout (Undo supported)")) Apply();
        }
        EditorGUILayout.HelpBox(report, MessageType.Info);
        EditorGUILayout.HelpBox("After applying, do not individually snap these anchors again. Reuse this helper to change the span. Re-bake navigation after moving terrain; update any existing bridge ghosts and cinematics separately.", MessageType.Warning);
    }

    private float Length => road != null ? road.maxLength : segmentLength;

    private bool Validate(out string reason)
    {
        reason = "";
        if (location == null || leftAnchor == null || rightAnchor == null || movingBank == null)
            reason = "Assign all four scene objects first.";
        else if (leftAnchor == rightAnchor || !location.gameObject.scene.IsValid() ||
            leftAnchor.gameObject.scene != location.gameObject.scene || rightAnchor.gameObject.scene != location.gameObject.scene ||
            movingBank.gameObject.scene != location.gameObject.scene)
            reason = "Use two different anchors and a bank from the same loaded scene.";
        else if (segments < 1 || !float.IsFinite(Length) || Length <= 0 || !float.IsFinite(Length * segments))
            reason = "Road length and piece count must be positive, finite values.";
        else if (road != null && !road.isRoad) reason = "Choose a road material.";
        else if (leftAnchor.transform.position.x >= rightAnchor.transform.position.x ||
            Mathf.Abs(leftAnchor.transform.position.z - rightAnchor.transform.position.z) > 0.02f)
            reason = "Place left/right anchors in increasing world X, with the same world Z (build plane).";
        else if (movingBank == location.transform || location.transform.IsChildOf(movingBank) ||
            movingBank == leftAnchor.transform || leftAnchor.transform.IsChildOf(movingBank) ||
            movingBank == rightAnchor.transform || movingBank.IsChildOf(rightAnchor.transform))
            reason = "Choose only the right terrain bank, not the whole level, build location, or an anchor.";
        else if (movingBank.GetComponentInChildren<Collider>() == null)
            reason = "The selected right bank has no active collider to align.";
        else if (location.bakedBars.Count > 0 || leftAnchor.Runtime || rightAnchor.Runtime)
            reason = "Use permanent anchors on an unbuilt location. Existing baked bridges need a separate migration.";
        return reason.Length == 0;
    }

    private bool Measure()
    {
        preview = false;
        if (!Validate(out report)) return false;
        var leftSnap = leftAnchor.GetComponent<AnchorEdgeSnap>();
        var rightSnap = rightAnchor.GetComponent<AnchorEdgeSnap>();
        if (leftSnap == null || rightSnap == null) { report = "Add the missing edge snap components first."; return false; }
        Physics.SyncTransforms();
        if (!leftSnap.TryPreviewEdge(out leftPosition, out Collider leftSurface, out report) ||
            !rightSnap.TryPreviewEdge(out Vector3 measuredRight, out Collider rightSurface, out report)) return false;
        if (rightSurface == null || !rightSurface.transform.IsChildOf(movingBank) ||
            (leftSurface != null && leftSurface.transform.IsChildOf(movingBank)))
        {
            report = "Right Bank Root must contain the detected right-edge collider, but not the left-edge collider. Select the correct terrain root.";
            return false;
        }
        if (measuredRight.x <= leftPosition.x) { report = "Detected edges are reversed. Check the anchor pair."; return false; }
        rightPosition = leftPosition + Vector3.right * (Length * segments);
        bankDelta = rightPosition - measuredRight;
        preview = true;
        report = $"Current edge span: {measuredRight.x-leftPosition.x:0.###}. Exact span: {Length*segments:0.###}.\nRight bank movement: {bankDelta.ToString("F3")} world units.\nBoth anchors will share Y and Z. Automatic edge snapping will be disabled on this pair.";
        SceneView.RepaintAll();
        return true;
    }

    private void Apply()
    {
        if (!Measure()) return;
        if (!EditorUtility.DisplayDialog("Apply exact bridge gap?", report + "\nMove the assigned right bank and anchors?", "Apply", "Cancel")) return;
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Align Exact Build Location Gap");
        Undo.RecordObjects(new Object[] { movingBank, leftAnchor.transform, rightAnchor.transform, leftAnchor, rightAnchor }, "Align Bridge Gap");
        movingBank.position += bankDelta;
        leftAnchor.transform.position = leftPosition;
        rightAnchor.transform.position = rightPosition;
        foreach (var anchor in new[] { leftAnchor, rightAnchor })
        {
            anchor.Runtime = false;
            anchor.isAnchor = anchor.originalIsAnchor = true;
            var snapData = new SerializedObject(anchor.GetComponent<AnchorEdgeSnap>());
            snapData.FindProperty("autoSnapWhenMoved").boolValue = false;
            snapData.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(anchor.GetComponent<AnchorEdgeSnap>());
            PrefabUtility.RecordPrefabInstancePropertyModifications(anchor);
            PrefabUtility.RecordPrefabInstancePropertyModifications(anchor.transform);
        }
        PrefabUtility.RecordPrefabInstancePropertyModifications(movingBank);
        EditorSceneManager.MarkSceneDirty(location.gameObject.scene);
        Physics.SyncTransforms();
        Undo.CollapseUndoOperations(group);
        report = $"Applied: exact {Length * segments:0.###}-unit anchor span ({segments} road pieces). Scene is unsaved; Ctrl+Z undoes the layout.";
        preview = false;
        SceneView.RepaintAll();
    }

    private void DrawPreview(SceneView view)
    {
        if (!preview || leftAnchor == null || rightAnchor == null) return;
        Handles.color = Color.green;
        Handles.DrawAAPolyLine(4f, leftPosition, rightPosition);
        Handles.Label((leftPosition + rightPosition) * 0.5f, $"{segments} × {Length:0.###} = {segments * Length:0.###} units");
        for (int i = 0; i <= Mathf.Min(segments, 200); i++)
        {
            Vector3 point = leftPosition + Vector3.right * (Length * i);
            Handles.DrawWireDisc(point, Vector3.forward, HandleUtility.GetHandleSize(point) * 0.045f);
        }
    }
}
