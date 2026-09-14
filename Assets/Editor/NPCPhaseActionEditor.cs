using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(NPCPhaseAction))]
public sealed class NPCPhaseActionEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        SerializedProperty npc = serializedObject.FindProperty("npc");
        SerializedProperty selectedId = serializedObject.FindProperty("phaseId");
        EditorGUILayout.PropertyField(npc);
        var manager = npc.objectReferenceValue as NPCProgressionManager;
        if (manager == null)
            EditorGUILayout.HelpBox("Assign an NPC to choose its phase.", MessageType.Info);
        else
        {
            var data = new SerializedObject(manager);
            data.Update();
            SerializedProperty phases = data.FindProperty("phases");
            var ids = new List<string> { selectedId.stringValue };
            var labels = new List<string> { string.IsNullOrEmpty(selectedId.stringValue) ? "Choose a phase..." : "Missing / invalid ID: " + selectedId.stringValue };
            var counts = new Dictionary<string, int>();
            for (int i = 0; i < phases.arraySize; i++)
            {
                string id = phases.GetArrayElementAtIndex(i).FindPropertyRelative("phaseId").stringValue;
                counts[id] = counts.TryGetValue(id, out int count) ? count + 1 : 1;
            }
            int selected = 0;
            for (int i = 0; i < phases.arraySize; i++)
            {
                string id = phases.GetArrayElementAtIndex(i).FindPropertyRelative("phaseId").stringValue;
                if (string.IsNullOrWhiteSpace(id) || counts[id] != 1) continue;
                ids.Add(id);
                labels.Add($"Index {i} — {id}");
                if (id == selectedId.stringValue) selected = ids.Count - 1;
            }
            EditorGUI.BeginChangeCheck();
            int choice = EditorGUILayout.Popup("Target Phase", selected, labels.ToArray());
            if (EditorGUI.EndChangeCheck() && choice > 0) selectedId.stringValue = ids[choice];
            if (selected == 0 && !string.IsNullOrEmpty(selectedId.stringValue))
                EditorGUILayout.HelpBox("The stored ID is missing or duplicated. Select a unique phase; it will not be changed automatically.", MessageType.Warning);
            if (ids.Count - 1 < phases.arraySize)
                EditorGUILayout.HelpBox("Phases with empty or duplicate IDs are excluded. Give them unique Phase IDs in the NPC Inspector.", MessageType.Warning);
            EditorGUILayout.LabelField("Stored Phase ID", selectedId.stringValue);
        }
        EditorGUILayout.HelpBox("Connect your event to NPCPhaseAction → MoveToSelectedPhase(). Reordering phases is safe; renaming their IDs requires updating this selection.", MessageType.Info);
        serializedObject.ApplyModifiedProperties();
    }
}
