using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public sealed class BuildLocationLayoutHelper : EditorWindow
{
    private BuildLocation location;
    private Point leftAnchor, rightAnchor;
    private Transform leftBank;
    private Transform movingBank;
    private Transform deckHeightReference;
    private float deckHeightOffset;
    private BridgeMaterialSO road;
    private float segmentLength = 10f;
    private int segments = 5;
    private float leftLandInset = 0.4f;
    private float rightLandInset = 0.1f;
    private float leftBankExtraDrop = 0.05f;
    private float rightBankExtraDrop = 0.05f;
    private bool preview;
    private Vector3 leftPosition, rightPosition, leftBankDelta, rightBankDelta, routeDirection;
    private string report = "Assign the location, two anchors, and both ravine banks.";

    [MenuItem("Tools/Civil Craft/Build Location Builder")]
    public static void Open() => GetWindow<BuildLocationLayoutHelper>("Build Location Builder");

    private void OnEnable() { SceneView.duringSceneGui += DrawPreview; }
    private void OnDisable() { SceneView.duringSceneGui -= DrawPreview; }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox("Creates a straight, vehicle-ready road route. Edge detection is restricted to the assigned bank roots, and the route follows the actual anchor-to-anchor direction. Both banks move beneath one repeatable deck-height reference.", MessageType.Info);
        EditorGUI.BeginChangeCheck();
        location = (BuildLocation)EditorGUILayout.ObjectField("Build Location", location, typeof(BuildLocation), true);
        leftAnchor = (Point)EditorGUILayout.ObjectField("Left Anchor", leftAnchor, typeof(Point), true);
        rightAnchor = (Point)EditorGUILayout.ObjectField("Right Anchor", rightAnchor, typeof(Point), true);
        leftBank = (Transform)EditorGUILayout.ObjectField("Left Bank Root", leftBank, typeof(Transform), true);
        movingBank = (Transform)EditorGUILayout.ObjectField("Right Bank Root", movingBank, typeof(Transform), true);
        deckHeightReference = (Transform)EditorGUILayout.ObjectField("Deck Height Reference", deckHeightReference, typeof(Transform), true);
        deckHeightOffset = EditorGUILayout.FloatField("Deck Height Offset", deckHeightOffset);
        road = (BridgeMaterialSO)EditorGUILayout.ObjectField("Road Material (optional)", road, typeof(BridgeMaterialSO), false);
        using (new EditorGUI.DisabledScope(road != null))
            segmentLength = EditorGUILayout.FloatField("Road Segment Length", road != null ? road.maxLength : segmentLength);
        segments = EditorGUILayout.IntField("Number of Road Pieces", segments);
        leftLandInset = EditorGUILayout.FloatField("Left Anchor Overlap", leftLandInset);
        rightLandInset = EditorGUILayout.FloatField("Right Anchor Overlap", rightLandInset);
        leftBankExtraDrop = EditorGUILayout.FloatField("Left Bank Extra Drop", leftBankExtraDrop);
        rightBankExtraDrop = EditorGUILayout.FloatField("Right Bank Extra Drop", rightBankExtraDrop);
        if (EditorGUI.EndChangeCheck()) { preview = false; SceneView.RepaintAll(); }
        EditorGUILayout.LabelField("Exact Road Route", $"{segments} × {Length:0.###} = {segments * Length:0.###} units");
        using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
        {
            if (GUILayout.Button("Use Location's First Anchor Pair") && location != null)
            {
                leftAnchor = location.startingAnchors.Count > 0 ? location.startingAnchors[0] : null;
                rightAnchor = location.endingAnchors.Count > 0 ? location.endingAnchors[0] : null;
                if (deckHeightReference == null && leftAnchor != null)
                    deckHeightReference = leftAnchor.transform;
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
                if (GUILayout.Button("Apply Vehicle-Safe Alignment (Undo supported)")) Apply();
        }
        EditorGUILayout.HelpBox(report, MessageType.Info);
        EditorGUILayout.HelpBox("For repeatable results, assign a Deck Height Reference that is not a child of either moving bank. Leaving it empty uses the current left-anchor height for backward compatibility.", MessageType.None);
        EditorGUILayout.HelpBox("After applying, do not individually snap these anchors again. Reuse this helper to change the span. Re-bake navigation after moving terrain; update any existing bridge ghosts and cinematics separately.", MessageType.Warning);
    }

    private float Length => road != null ? road.maxLength : segmentLength;

    private bool Validate(out string reason)
    {
        reason = "";
        if (location == null || leftAnchor == null || rightAnchor == null || leftBank == null || movingBank == null)
            reason = "Assign the build location, both anchors, and both bank roots.";
        else if (leftAnchor == rightAnchor || !location.gameObject.scene.IsValid() ||
            leftAnchor.gameObject.scene != location.gameObject.scene || rightAnchor.gameObject.scene != location.gameObject.scene ||
            leftBank.gameObject.scene != location.gameObject.scene || movingBank.gameObject.scene != location.gameObject.scene ||
            leftBank == movingBank)
            reason = "Use two different anchors and two different banks from the same loaded scene.";
        else if (segments < 1 || !float.IsFinite(Length) || Length <= 0 || !float.IsFinite(Length * segments) ||
                 !float.IsFinite(leftLandInset) || leftLandInset < 0f || leftLandInset > 2f ||
                 !float.IsFinite(rightLandInset) || rightLandInset < 0f || rightLandInset > 2f ||
                 !float.IsFinite(leftBankExtraDrop) || leftBankExtraDrop < 0f || leftBankExtraDrop > 2f ||
                 !float.IsFinite(rightBankExtraDrop) || rightBankExtraDrop < 0f || rightBankExtraDrop > 2f)
            reason = "Road length and piece count must be positive and finite. Bank overlaps and extra drops must be from 0 to 2 units.";
        else if (road != null && !road.isRoad) reason = "Choose a road material.";
        else if ((new Vector3(
                     rightAnchor.transform.position.x - leftAnchor.transform.position.x,
                     0f,
                     rightAnchor.transform.position.z - leftAnchor.transform.position.z)).sqrMagnitude < 0.0001f)
            reason = "The two anchors need different horizontal positions.";
        else if (!IsSafeBankRoot(leftBank) || !IsSafeBankRoot(movingBank) ||
            leftBank.IsChildOf(movingBank) || movingBank.IsChildOf(leftBank))
            reason = "Choose the two separate terrain-bank roots, not the whole level, build location, an anchor, or nested roots.";
        else if (leftBank.GetComponentInChildren<Collider>() == null || movingBank.GetComponentInChildren<Collider>() == null)
            reason = "Each selected bank must contain an active collider to align.";
        else if (deckHeightReference != null &&
                 (!deckHeightReference.gameObject.scene.IsValid() ||
                  deckHeightReference.gameObject.scene != location.gameObject.scene ||
                  Contains(leftBank, deckHeightReference) || Contains(movingBank, deckHeightReference)))
            reason = "The Deck Height Reference must be from the same scene and cannot be inside either moving bank.";
        else if (!float.IsFinite(deckHeightOffset))
            reason = "The Deck Height Offset must be finite.";
        else if (location.bakedBars.Count > 0 || leftAnchor.Runtime || rightAnchor.Runtime)
            reason = "Use permanent anchors on an unbuilt location. Existing baked bridges need a separate migration.";
        return reason.Length == 0;
    }

    private bool IsSafeBankRoot(Transform bank)
    {
        return bank != null &&
               bank != location.transform && !location.transform.IsChildOf(bank) &&
               bank != leftAnchor.transform && !leftAnchor.transform.IsChildOf(bank) && !bank.IsChildOf(leftAnchor.transform) &&
               bank != rightAnchor.transform && !rightAnchor.transform.IsChildOf(bank) && !bank.IsChildOf(rightAnchor.transform);
    }

    private static bool Contains(Transform root, Transform candidate)
    {
        return root != null && candidate != null && (candidate == root || candidate.IsChildOf(root));
    }

    private bool Measure()
    {
        preview = false;
        if (!Validate(out report)) return false;
        var leftSnap = leftAnchor.GetComponent<AnchorEdgeSnap>();
        var rightSnap = rightAnchor.GetComponent<AnchorEdgeSnap>();
        if (leftSnap == null || rightSnap == null) { report = "Add the missing edge snap components first."; return false; }
        Physics.SyncTransforms();
        routeDirection = rightAnchor.transform.position - leftAnchor.transform.position;
        routeDirection.y = 0f;
        routeDirection.Normalize();
        if (!leftSnap.TryPreviewEdge(leftLandInset, leftBank, routeDirection,
                out Vector3 measuredLeft, out Collider leftSurface, out report) ||
            !rightSnap.TryPreviewEdge(rightLandInset, movingBank, routeDirection,
                out Vector3 measuredRight, out Collider rightSurface, out report)) return false;
        if (leftSurface == null || rightSurface == null ||
            !Contains(leftBank, leftSurface.transform) || !Contains(movingBank, rightSurface.transform) ||
            Contains(leftBank, rightSurface.transform) || Contains(movingBank, leftSurface.transform))
        {
            report = "Each Bank Root must contain only its matching detected edge collider. Select the correct left and right terrain roots.";
            return false;
        }
        if (Vector3.Dot(measuredRight - measuredLeft, routeDirection) <= 0f)
        {
            report = "Detected edges are reversed relative to the selected left-to-right anchor direction. Check the anchor pair.";
            return false;
        }

        float exactRouteLength = Length * segments;
        Transform heightReference = deckHeightReference != null ? deckHeightReference : leftAnchor.transform;
        float commonAnchorY = heightReference.position.y + deckHeightOffset;
        leftPosition = new Vector3(measuredLeft.x, commonAnchorY, measuredLeft.z);
        rightPosition = leftPosition + routeDirection * exactRouteLength;

        // The anchors own the level road plane. Move each bank relative to that
        // plane and leave a small clearance so the road is not buried in either lip.
        // Using measured-to-target deltas also makes repeated Apply operations stable.
        leftBankDelta = leftPosition - measuredLeft + Vector3.down * leftBankExtraDrop;
        rightBankDelta = rightPosition - measuredRight + Vector3.down * rightBankExtraDrop;
        preview = true;
        report = $"Detected banks: '{leftSurface.name}' / '{rightSurface.name}'. Route direction: {routeDirection.ToString("F3")}.\nCurrent measured route: {Vector3.Distance(measuredLeft, measuredRight):0.###}. Exact level route: {exactRouteLength:0.###}.\nLeft bank movement: {leftBankDelta.ToString("F3")}; extra drop: {leftBankExtraDrop:0.###}.\nRight bank movement: {rightBankDelta.ToString("F3")}; extra drop: {rightBankExtraDrop:0.###}.\nLeft/right bank overlap: {leftLandInset:0.###} / {rightLandInset:0.###}. Deck height: {commonAnchorY:0.###} from '{heightReference.name}' plus {deckHeightOffset:0.###}.";
        SceneView.RepaintAll();
        return true;
    }

    private void Apply()
    {
        if (!Measure()) return;
        if (!EditorUtility.DisplayDialog("Apply exact bridge gap?", report + "\nMove the assigned left/right banks and anchors?", "Apply", "Cancel")) return;
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Align Exact Build Location Gap");
        AnchorEdgeSnap leftSnap = leftAnchor.GetComponent<AnchorEdgeSnap>();
        AnchorEdgeSnap rightSnap = rightAnchor.GetComponent<AnchorEdgeSnap>();
        Undo.RecordObjects(new Object[]
        {
            leftBank, movingBank, leftAnchor.transform, rightAnchor.transform,
            leftAnchor, rightAnchor, leftSnap, rightSnap
        }, "Align Bridge Gap");
        leftBank.position += leftBankDelta;
        movingBank.position += rightBankDelta;
        leftAnchor.transform.position = leftPosition;
        rightAnchor.transform.position = rightPosition;
        foreach (var anchor in new[] { leftAnchor, rightAnchor })
        {
            anchor.Runtime = false;
            anchor.isAnchor = anchor.originalIsAnchor = true;
            var snapData = new SerializedObject(anchor.GetComponent<AnchorEdgeSnap>());
            snapData.FindProperty("autoSnapWhenMoved").boolValue = false;
            snapData.FindProperty("edgeInsetOntoLand").floatValue =
                anchor == leftAnchor ? leftLandInset : rightLandInset;
            snapData.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(anchor.GetComponent<AnchorEdgeSnap>());
            PrefabUtility.RecordPrefabInstancePropertyModifications(anchor);
            PrefabUtility.RecordPrefabInstancePropertyModifications(anchor.transform);
        }

        BridgeAbutmentAligner aligner = location.GetComponent<BridgeAbutmentAligner>();
        if (aligner == null)
            aligner = Undo.AddComponent<BridgeAbutmentAligner>(location.gameObject);
        else
            Undo.RecordObject(aligner, "Configure Smooth Bridge Approaches");

        var alignerData = new SerializedObject(aligner);
        alignerData.FindProperty("buildLocation").objectReferenceValue = location;
        alignerData.FindProperty("referenceAnchor").objectReferenceValue = leftAnchor;
        alignerData.FindProperty("alignAnchorsToTutorialGhosts").boolValue = false;
        alignerData.ApplyModifiedProperties();

        // Endpoint approach colliders are no longer part of the layout. Remove
        // editor-saved copies so no invisible blocker remains at either end.
        aligner.RemoveGeneratedApproaches();
        PrefabUtility.RecordPrefabInstancePropertyModifications(aligner);
        PrefabUtility.RecordPrefabInstancePropertyModifications(leftBank);
        PrefabUtility.RecordPrefabInstancePropertyModifications(movingBank);
        EditorSceneManager.MarkSceneDirty(location.gameObject.scene);
        Physics.SyncTransforms();
        Undo.CollapseUndoOperations(group);
        report = $"Applied: exact {Length * segments:0.###}-unit level route ({segments} road pieces), left/right bank overlap {leftLandInset:0.###}/{rightLandInset:0.###}, left/right bank extra drop {leftBankExtraDrop:0.###}/{rightBankExtraDrop:0.###}, with no generated endpoint colliders. Scene is unsaved; Ctrl+Z undoes the layout.";
        preview = false;
        SceneView.RepaintAll();
    }

    private void DrawPreview(SceneView view)
    {
        if (!preview || leftAnchor == null || rightAnchor == null) return;
        Handles.color = Color.green;
        Handles.DrawAAPolyLine(4f, leftPosition, rightPosition);
        Handles.Label((leftPosition + rightPosition) * 0.5f, $"{segments} × {Length:0.###} = {segments * Length:0.###} units • L/R overlap {leftLandInset:0.###}/{rightLandInset:0.###}");
        for (int i = 0; i <= Mathf.Min(segments, 200); i++)
        {
            Vector3 point = Vector3.Lerp(leftPosition, rightPosition, (float)i / segments);
            Handles.DrawWireDisc(point, Vector3.up, HandleUtility.GetHandleSize(point) * 0.045f);
        }
    }
}
