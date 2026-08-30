using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(BridgeAbutmentAligner))]
public sealed class BridgeAbutmentAlignerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space();

        BridgeAbutmentAligner aligner = (BridgeAbutmentAligner)target;
        using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
        {
            if (GUILayout.Button("Sync Anchors To Tutorial Ghosts"))
                SyncAnchorsWithUndo(aligner);

            if (GUILayout.Button("Align Anchors And Approach Surfaces"))
                AlignWithUndo(aligner);

            if (GUILayout.Button("Auto Create / Refresh Smooth Approaches"))
            {
                Undo.RegisterFullObjectHierarchyUndo(aligner.gameObject, "Generate Smooth Bridge Approaches");
                bool generated = aligner.GenerateSmoothApproaches(out string report);
                EditorUtility.SetDirty(aligner.gameObject);
                if (generated) Debug.Log($"[BridgeAbutmentAligner] {report}", aligner);
                else Debug.LogWarning($"[BridgeAbutmentAligner] {report}", aligner);
            }

            if (GUILayout.Button("Remove Generated Smooth Approaches"))
            {
                Undo.RegisterFullObjectHierarchyUndo(aligner.gameObject, "Remove Smooth Bridge Approaches");
                aligner.RemoveGeneratedApproaches();
                EditorUtility.SetDirty(aligner.gameObject);
            }

            if (GUILayout.Button("Validate Alignment"))
            {
                bool valid = aligner.ValidateAlignment(out string report);
                if (valid) Debug.Log($"[BridgeAbutmentAligner] {report}", aligner);
                else Debug.LogWarning($"[BridgeAbutmentAligner] {report}", aligner);
            }
        }

        EditorGUILayout.HelpBox(
            "For the least manual setup, assign Build Location and click Auto Create / Refresh. " +
            "Sync Anchors To Tutorial Ghosts repairs blueprint alignment after an anchor was accidentally moved. " +
            "The smooth approach tool then measures the Environment/Ground surface and creates invisible transition colliders.",
            MessageType.Info);
    }

    private static void SyncAnchorsWithUndo(BridgeAbutmentAligner aligner)
    {
        BuildLocation location = aligner.Location;
        if (location == null)
        {
            Debug.LogWarning("[BridgeAbutmentAligner] No Build Location is assigned.", aligner);
            return;
        }

        List<Object> changedObjects = new List<Object> { aligner };
        foreach (Point anchor in location.startingAnchors)
            if (anchor != null) changedObjects.Add(anchor.transform);
        foreach (Point anchor in location.endingAnchors)
            if (anchor != null && !changedObjects.Contains(anchor.transform))
                changedObjects.Add(anchor.transform);

        Undo.RecordObjects(changedObjects.ToArray(), "Sync Tutorial Anchors To Ghosts");
        bool changed = aligner.AlignAnchorsToTutorialGhostEndpoints(out string report);
        foreach (Object changedObject in changedObjects)
            if (changedObject != null) EditorUtility.SetDirty(changedObject);

        if (changed) Debug.Log($"[BridgeAbutmentAligner] {report}", aligner);
        else Debug.LogWarning($"[BridgeAbutmentAligner] {report}", aligner);
    }

    private static void AlignWithUndo(BridgeAbutmentAligner aligner)
    {
        List<Object> changedObjects = new List<Object> { aligner };
        foreach (BridgeAbutmentAligner.Abutment abutment in aligner.Abutments)
        {
            if (abutment == null) continue;
            if (abutment.anchor != null) changedObjects.Add(abutment.anchor.transform);

            Transform movable = abutment.movableRoot != null
                ? abutment.movableRoot
                : abutment.approachCollider != null
                    ? abutment.approachCollider.transform
                    : null;
            if (movable != null) changedObjects.Add(movable);
        }

        Undo.RecordObjects(changedObjects.ToArray(), "Align Bridge Abutments");
        bool aligned = aligner.AlignAll(out string report);

        foreach (Object changedObject in changedObjects)
            if (changedObject != null) EditorUtility.SetDirty(changedObject);

        if (aligned) Debug.Log($"[BridgeAbutmentAligner] {report}", aligner);
        else Debug.LogWarning($"[BridgeAbutmentAligner] {report}", aligner);
    }
}
