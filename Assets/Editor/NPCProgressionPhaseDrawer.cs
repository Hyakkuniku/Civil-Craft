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
            EditorGUI.indentLevel--;
        }
        EditorGUI.EndProperty();
    }
}
