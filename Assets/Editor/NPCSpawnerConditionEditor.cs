using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(NPCSpawnerCondition)), CanEditMultipleObjects]
public sealed class NPCSpawnerConditionEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.HelpBox("Controls an existing scene NPC; does not create prefab copies. Hidden NPCs still recheck rules. Manual Spawn/Despawn overrides rules until Use Conditions is called.", MessageType.Info);
        Draw("npcRoot");
        Draw("waitForManualSpawn");
        Draw("persistManualOverride");
        if (serializedObject.FindProperty("persistManualOverride").boolValue)
        {
            Draw("visibilitySaveId");
            if (string.IsNullOrWhiteSpace(serializedObject.FindProperty("visibilitySaveId").stringValue))
                EditorGUILayout.HelpBox("Set a unique Visibility Save ID to persist Spawn/Despawn events.", MessageType.Warning);
        }
        Draw("useMultipleConditions");
        if (serializedObject.FindProperty("useMultipleConditions").boolValue)
        {
            Draw("matchMode"); Draw("conditions"); Draw("hideWhenMatched");
        }
        else { Draw("requiredLessonName"); Draw("appearAfterLesson"); }
        Draw("updateAutomatically");
        Draw("useDespawnConditions");
        if (serializedObject.FindProperty("useDespawnConditions").boolValue)
        { Draw("despawnMatchMode"); Draw("despawnConditions"); }
        Draw("onShown"); Draw("onHidden");
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Play Mode Controls", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(!Application.isPlaying || targets.Length != 1))
        {
            var gate = (NPCSpawnerCondition)target;
            EditorGUILayout.LabelField(gate.HasManualOverride ? "Manual override active" : "Using conditions");
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Spawn NPC")) gate.SpawnNPC();
            if (GUILayout.Button("Despawn NPC")) gate.DespawnNPC();
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("Use Conditions")) gate.UseConditions();
            if (GUILayout.Button("Evaluate Now")) gate.EvaluateNow();
        }
    }
    private void Draw(string name) => EditorGUILayout.PropertyField(serializedObject.FindProperty(name), true);
}

[CustomPropertyDrawer(typeof(NPCSpawnerCondition.Condition))]
public sealed class NPCSpawnRuleDrawer : PropertyDrawer
{
    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        bool phase = property.FindPropertyRelative("kind").enumValueIndex == (int)NPCSpawnerCondition.ConditionKind.NPCAtPhase;
        return (EditorGUIUtility.singleLineHeight + 3f) * (phase ? 4 : 3);
    }
    public override void OnGUI(Rect rect, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(rect, label, property);
        rect.height = EditorGUIUtility.singleLineHeight;
        DrawLine(ref rect, property.FindPropertyRelative("kind"), label);
        var kind = (NPCSpawnerCondition.ConditionKind)property.FindPropertyRelative("kind").enumValueIndex;
        if (kind == NPCSpawnerCondition.ConditionKind.ContractCompleted)
            DrawLine(ref rect, property.FindPropertyRelative("contract"), new GUIContent("Contract"));
        else
            DrawLine(ref rect, property.FindPropertyRelative("id"), new GUIContent(kind == NPCSpawnerCondition.ConditionKind.NPCAtPhase ? "NPC Save ID" : kind == NPCSpawnerCondition.ConditionKind.FeatureUnlocked ? "Feature ID" : "Lesson Name"));
        if (kind == NPCSpawnerCondition.ConditionKind.NPCAtPhase)
            DrawLine(ref rect, property.FindPropertyRelative("phaseId"), new GUIContent("Phase ID (on arrival)"));
        DrawLine(ref rect, property.FindPropertyRelative("mustBeTrue"), new GUIContent("Must Be True", "Uncheck to require this condition to be false."));
        EditorGUI.EndProperty();
    }
    private static void DrawLine(ref Rect rect, SerializedProperty property, GUIContent label)
    { EditorGUI.PropertyField(rect, property, label); rect.y += EditorGUIUtility.singleLineHeight + 3f; }
}
