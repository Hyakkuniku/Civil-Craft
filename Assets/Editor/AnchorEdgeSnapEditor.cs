using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(AnchorEdgeSnap))]
public sealed class AnchorEdgeSnapEditor : Editor
{
    [MenuItem("Tools/Civil Craft/Add Edge Snap To Permanent Anchors")]
    private static void AddToPermanentSceneAnchors()
    {
        int added = 0;
        foreach (Point point in Resources.FindObjectsOfTypeAll<Point>())
        {
            if (point == null || !point.gameObject.scene.IsValid() ||
                !point.isAnchor || point.Runtime ||
                point.GetComponent<AnchorEdgeSnap>() != null)
                continue;

            Undo.AddComponent<AnchorEdgeSnap>(point.gameObject);
            EditorUtility.SetDirty(point.gameObject);
            added++;
        }

        Debug.Log($"[AnchorEdgeSnap] Added edge snapping to {added} permanent scene anchor(s).");
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space();

        AnchorEdgeSnap snapper = (AnchorEdgeSnap)target;
        using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
        {
            if (GUILayout.Button("Snap To Nearest Ravine Edge"))
            {
                Undo.RecordObject(snapper.transform, "Snap Anchor To Ravine Edge");
                bool snapped = snapper.TrySnapToNearestEdge(out string report);
                if (snapped)
                {
                    EditorUtility.SetDirty(snapper.transform);
                    Debug.Log($"[AnchorEdgeSnap] {report}", snapper);
                }
                else Debug.LogWarning($"[AnchorEdgeSnap] {report}", snapper);
            }
        }

        EditorGUILayout.HelpBox(
            "Automatic snapping only runs for permanent Points with Is Anchor enabled and Runtime disabled. " +
            "Player-created nodes are unaffected.",
            MessageType.Info);
    }
}
