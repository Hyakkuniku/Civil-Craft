using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(NPCProgressionPhase))]
public sealed class NPCProgressionPhaseDrawer : PropertyDrawer
{
    private static GUIContent Header(SerializedProperty property, GUIContent fallback)
    {
        string path = property.propertyPath;
        int start = path.LastIndexOf(".Array.data[", System.StringComparison.Ordinal);
        int end = start >= 0 ? path.IndexOf(']', start) : -1;
        string index = start >= 0 && end > start
            ? path.Substring(start + 12, end - start - 12) : null;
        SerializedProperty id = property.FindPropertyRelative("phaseId");
        string name = id != null && !id.hasMultipleDifferentValues ? id.stringValue : "Phase";
        if (string.IsNullOrWhiteSpace(name)) name = "Unnamed phase";
        return new GUIContent(index != null ? $"Index {index} — {name}" : fallback.text,
            "Zero-based phase index used by MoveToPhase and other phase actions. Updates automatically when reordered.");
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float height = EditorGUIUtility.singleLineHeight;
        if (!property.isExpanded) return height;
        SerializedProperty child = property.Copy();
        SerializedProperty end = property.GetEndProperty();
        if (child.NextVisible(true))
            do
            {
                if (SerializedProperty.EqualContents(child, end)) break;
                height += EditorGUIUtility.standardVerticalSpacing + EditorGUI.GetPropertyHeight(child, true);
            } while (child.NextVisible(false));
        height += QuizAuthoringWarnings(property).Count *
                  (EditorGUIUtility.standardVerticalSpacing + EditorGUIUtility.singleLineHeight * 3f);
        return height;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);
        Rect row = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        property.isExpanded = EditorGUI.Foldout(row, property.isExpanded, Header(property, label), true);
        if (property.isExpanded)
        {
            EditorGUI.indentLevel++;
            SerializedProperty child = property.Copy();
            SerializedProperty end = property.GetEndProperty();
            if (child.NextVisible(true))
                do
                {
                    if (SerializedProperty.EqualContents(child, end)) break;
                    row.y += row.height + EditorGUIUtility.standardVerticalSpacing;
                    row.height = EditorGUI.GetPropertyHeight(child, true);
                    EditorGUI.PropertyField(row, child, true);
                } while (child.NextVisible(false));
            foreach (string warning in QuizAuthoringWarnings(property))
            {
                row.y += row.height + EditorGUIUtility.standardVerticalSpacing;
                row.height = EditorGUIUtility.singleLineHeight * 3f;
                EditorGUI.HelpBox(EditorGUI.IndentedRect(row), warning, MessageType.Warning);
            }
            EditorGUI.indentLevel--;
        }
        EditorGUI.EndProperty();
    }

    private static List<string> QuizAuthoringWarnings(SerializedProperty phase)
    {
        List<string> warnings = new List<string>();
        SerializedProperty sequence = phase.FindPropertyRelative("optionalDialogueSequence");
        if (sequence == null || !sequence.isArray) return warnings;
        bool hasQuiz = false;
        bool hasOtherUsableStep = false;
        string contentWarning = null;
        HashSet<string> stepIds = new HashSet<string>(System.StringComparer.Ordinal);
        for (int i = 0; i < sequence.arraySize; i++)
        {
            SerializedProperty step = sequence.GetArrayElementAtIndex(i);
            int type = step.FindPropertyRelative("stepType").intValue;
            if (type == (int)NPCOptionalDialogueStep.StepType.Quiz)
            {
                hasQuiz = true;
                string id = step.FindPropertyRelative("quizStepId").stringValue;
                QuizData quiz = step.FindPropertyRelative("quiz").objectReferenceValue as QuizData;
                if (string.IsNullOrWhiteSpace(id))
                    contentWarning = contentWarning ?? "A Quiz step needs a stable Quiz Step ID. Edit its Quiz asset field or enter a unique ID.";
                else if (!stepIds.Add(id.Trim()))
                    contentWarning = contentWarning ?? "Quiz Step IDs are duplicated. Give copied Quiz steps different IDs; do not change released IDs.";
                if (quiz == null)
                    contentWarning = contentWarning ?? "Assign Quiz Data to every Quiz step. Invalid quizzes block the sequence rather than being skipped.";
                else if (!quiz.TryValidate(out string error))
                    contentWarning = contentWarning ?? "Quiz Data is invalid: " + error;
            }
            else if (type == (int)NPCOptionalDialogueStep.StepType.Dialogue)
            {
                SerializedProperty sentences = step.FindPropertyRelative("dialogue").FindPropertyRelative("sentences");
                hasOtherUsableStep |= sentences != null && sentences.isArray && sentences.arraySize > 0;
            }
            else if (type == (int)NPCOptionalDialogueStep.StepType.MaterialUnlock)
                hasOtherUsableStep |= step.FindPropertyRelative("material").objectReferenceValue != null;
        }
        if (!hasQuiz) return warnings;
        if (contentWarning != null) warnings.Add(contentWarning);
        SerializedProperty progressionId = phase.serializedObject.FindProperty("progressionSaveId");
        string phaseId = phase.FindPropertyRelative("phaseId").stringValue;
        if (string.IsNullOrWhiteSpace(phaseId) ||
            (progressionId != null && string.IsNullOrWhiteSpace(progressionId.stringValue)))
            warnings.Add("Quiz resume requires a stable NPC Progression Save ID and a unique Phase ID.");
        else
        {
            SerializedProperty phases = phase.serializedObject.FindProperty("phases");
            int matches = 0;
            if (phases != null && phases.isArray)
                for (int i = 0; i < phases.arraySize; i++)
                    if (string.Equals(phases.GetArrayElementAtIndex(i).FindPropertyRelative("phaseId").stringValue?.Trim(),
                        phaseId.Trim(), System.StringComparison.Ordinal)) matches++;
            if (matches > 1) warnings.Add("This Phase ID is duplicated. A quiz cannot safely save progress until phase IDs are unique.");
        }
        if (!hasOtherUsableStep && phase.FindPropertyRelative("contract").objectReferenceValue == null &&
            string.Equals(phase.FindPropertyRelative("unlockFeatureIdAfterDialogue").stringValue?.Trim(),
                "minimap", System.StringComparison.OrdinalIgnoreCase))
            warnings.Add("This quiz-only phase still has the default Minimap reward. Clear Unlock Feature ID After Dialogue if no reward is intended.");
        return warnings;
    }
}
