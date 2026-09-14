using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(NPCPhaseCall))]
public sealed class NPCPhaseCallDrawer : PropertyDrawer
{
    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        => EditorGUIUtility.singleLineHeight * 5 + EditorGUIUtility.standardVerticalSpacing * 4;

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);
        float height = EditorGUIUtility.singleLineHeight;
        float step = height + EditorGUIUtility.standardVerticalSpacing;
        Rect row = new Rect(position.x, position.y, position.width, height);
        EditorGUI.LabelField(row, label, EditorStyles.boldLabel);
        row.y += step;
        SerializedProperty npc = property.FindPropertyRelative("npc");
        SerializedProperty id = property.FindPropertyRelative("phaseId");
        EditorGUI.PropertyField(row, npc, new GUIContent("NPC"));
        row.y += step;
        var names = new List<string> { "None (no phase action)" };
        var values = new List<string> { "" };
        int selected = 0;
        bool duplicates = false;
        if (npc.objectReferenceValue != null && !npc.hasMultipleDifferentValues)
        {
            var data = new SerializedObject(npc.objectReferenceValue);
            data.Update();
            var phases = data.FindProperty("phases");
            var counts = new Dictionary<string, int>();
            for (int i = 0; i < phases.arraySize; i++)
            {
                string value = phases.GetArrayElementAtIndex(i).FindPropertyRelative("phaseId").stringValue;
                counts[value] = counts.TryGetValue(value, out int count) ? count + 1 : 1;
            }
            for (int i = 0; i < phases.arraySize; i++)
            {
                string value = phases.GetArrayElementAtIndex(i).FindPropertyRelative("phaseId").stringValue;
                if (string.IsNullOrWhiteSpace(value) || counts[value] != 1) { duplicates = true; continue; }
                values.Add(value); names.Add($"Index {i} — {value}");
                if (value == id.stringValue) selected = values.Count - 1;
            }
        }
        Rect clearRect = new Rect(row.xMax - 48, row.y, 48, row.height);
        row.width -= 54;
        if (GUI.Button(clearRect, "Clear")) { id.stringValue = ""; selected = 0; }
        EditorGUI.showMixedValue = id.hasMultipleDifferentValues;
        EditorGUI.BeginChangeCheck();
        int choice = EditorGUI.Popup(row, "Move to Phase", selected, names.ToArray());
        if (EditorGUI.EndChangeCheck()) id.stringValue = values[choice];
        EditorGUI.showMixedValue = false;
        row.width += 54;
        row.y += step;
        row.height = height * 2;
        bool invalid = selected == 0 && !string.IsNullOrEmpty(id.stringValue);
        EditorGUI.HelpBox(row, invalid ? "Saved phase ID is missing or duplicated. Choose a valid phase." :
            duplicates ? "Empty/duplicate IDs are excluded. Assign unique phase IDs on the NPC." :
            "Optional. Runs after the event above. Do not also configure the same move in that event.",
            invalid || duplicates ? MessageType.Warning : MessageType.Info);
        EditorGUI.EndProperty();
    }
}
