using System;
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(NPCOptionalDialogueStep))]
public sealed class NPCOptionalDialogueStepDrawer : PropertyDrawer
{
    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float height = EditorGUIUtility.singleLineHeight;
        if (!property.isExpanded) return height;
        height += FieldHeight(property, "stepType");
        int stepType = property.FindPropertyRelative("stepType").intValue;
        if (stepType == (int)NPCOptionalDialogueStep.StepType.Dialogue)
            height += FieldHeight(property, "dialogue");
        else if (stepType == (int)NPCOptionalDialogueStep.StepType.MaterialUnlock)
            height += FieldHeight(property, "material") + FieldHeight(property, "materialButtonLabel");
        else if (stepType == (int)NPCOptionalDialogueStep.StepType.Quiz)
            height += FieldHeight(property, "quiz") + FieldHeight(property, "quizStepId");
        return height;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);
        Rect row = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        SerializedProperty type = property.FindPropertyRelative("stepType");
        int enumIndex = type.enumValueIndex;
        string typeLabel = type.hasMultipleDifferentValues ? "Mixed steps" :
            enumIndex >= 0 && enumIndex < type.enumDisplayNames.Length ? type.enumDisplayNames[enumIndex] : "Unknown step";
        property.isExpanded = EditorGUI.Foldout(row, property.isExpanded, new GUIContent(label.text + " — " + typeLabel), true);
        if (property.isExpanded)
        {
            EditorGUI.indentLevel++;
            EditorGUI.BeginChangeCheck();
            DrawField(ref row, type);
            bool authoredType = EditorGUI.EndChangeCheck();
            int stepType = type.intValue;
            if (stepType == (int)NPCOptionalDialogueStep.StepType.Dialogue)
                DrawField(ref row, property.FindPropertyRelative("dialogue"));
            else if (stepType == (int)NPCOptionalDialogueStep.StepType.MaterialUnlock)
            {
                DrawField(ref row, property.FindPropertyRelative("material"));
                DrawField(ref row, property.FindPropertyRelative("materialButtonLabel"));
            }
            else if (stepType == (int)NPCOptionalDialogueStep.StepType.Quiz)
            {
                EditorGUI.BeginChangeCheck();
                DrawField(ref row, property.FindPropertyRelative("quiz"));
                bool authoredAsset = EditorGUI.EndChangeCheck();
                SerializedProperty id = property.FindPropertyRelative("quizStepId");
                // Only an authoring edit fills a missing identity. Merely opening
                // an existing scene's Inspector never rewrites its saved content.
                if ((authoredType || authoredAsset) && !id.hasMultipleDifferentValues &&
                    string.IsNullOrWhiteSpace(id.stringValue))
                    id.stringValue = "quiz-" + Guid.NewGuid().ToString("N");
                DrawField(ref row, id);
            }
            EditorGUI.indentLevel--;
        }
        EditorGUI.EndProperty();
    }

    private static float FieldHeight(SerializedProperty property, string childName)
    {
        return EditorGUIUtility.standardVerticalSpacing +
               EditorGUI.GetPropertyHeight(property.FindPropertyRelative(childName), true);
    }

    private static void DrawField(ref Rect row, SerializedProperty property)
    {
        row.y += row.height + EditorGUIUtility.standardVerticalSpacing;
        row.height = EditorGUI.GetPropertyHeight(property, true);
        EditorGUI.PropertyField(row, property, true);
    }
}
