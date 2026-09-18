#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class TutorialAnchorHighlighterSetup
{
    private const string SessionKey = "CivilCraft.TutorialAnchorHighlighterSetup.V1";
    private const string SequenceName = "Sequence_Build1";
    private const string OldPhrase = "red anchor";
    private const string UpdatedMessage =
        "Click the <b><#E09500>highlighted anchor</color></b> and drag across the gap to build the road.";

    static TutorialAnchorHighlighterSetup()
    {
        EditorApplication.delayCall += TryAutoSetup;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorSceneManager.sceneOpened += OnSceneOpened;
    }

    [MenuItem("Tools/Civil Craft/Setup Tutorial Anchor Highlight")]
    public static void SetupFromMenu()
    {
        SetupActiveScene(true);
    }

    private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
    {
        if (scene.name == "CanyonCrossing")
            EditorApplication.delayCall += TryAutoSetup;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode)
            EditorApplication.delayCall += TryAutoSetup;
    }

    private static void TryAutoSetup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode ||
            SessionState.GetBool(SessionKey, false)) return;
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded || scene.name != "CanyonCrossing") return;

        SessionState.SetBool(SessionKey, true);
        SetupActiveScene(!scene.isDirty);
    }

    private static void SetupActiveScene(bool saveIfSafe)
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded || scene.name != "CanyonCrossing")
        {
            Debug.LogWarning("[TutorialAnchorHighlightSetup] Open CanyonCrossing first.");
            return;
        }

        TutorialSequence sequence = FindSequence(scene, SequenceName);
        if (sequence == null)
        {
            Debug.LogError($"[TutorialAnchorHighlightSetup] Could not find {SequenceName}.");
            return;
        }

        int stepIndex = FindAnchorStep(sequence);
        if (stepIndex < 0)
        {
            Debug.LogError("[TutorialAnchorHighlightSetup] Could not find the red-anchor step.", sequence);
            return;
        }

        SerializedObject serializedSequence = new SerializedObject(sequence);
        SerializedProperty step = serializedSequence.FindProperty("tutorialSteps")
            .GetArrayElementAtIndex(stepIndex);
        SerializedProperty calls = step.FindPropertyRelative("OnStepStart")
            .FindPropertyRelative("m_PersistentCalls")
            .FindPropertyRelative("m_Calls");

        Transform anchor = null;
        for (int i = calls.arraySize - 1; i >= 0; i--)
        {
            SerializedProperty call = calls.GetArrayElementAtIndex(i);
            Object target = call.FindPropertyRelative("m_Target").objectReferenceValue;
            string method = call.FindPropertyRelative("m_MethodName").stringValue;
            if (!(target is Tutorial3DIndicator) || method != "ShowAtPosition") continue;

            anchor = call.FindPropertyRelative("m_Arguments")
                .FindPropertyRelative("m_ObjectArgument").objectReferenceValue as Transform;
            calls.DeleteArrayElementAtIndex(i);
        }

        if (anchor == null)
        {
            for (int i = 0; i < calls.arraySize; i++)
            {
                SerializedProperty call = calls.GetArrayElementAtIndex(i);
                if (call.FindPropertyRelative("m_MethodName").stringValue != "HighlightAnchor") continue;
                anchor = call.FindPropertyRelative("m_Arguments")
                    .FindPropertyRelative("m_ObjectArgument").objectReferenceValue as Transform;
                if (anchor != null) break;
            }
        }

        if (anchor == null || anchor.GetComponentInParent<Point>() == null)
        {
            Debug.LogError(
                "[TutorialAnchorHighlightSetup] The old indicator did not contain a valid Point target. " +
                "No event was replaced.", sequence);
            return;
        }

        TutorialAnchorHighlighter highlighter =
            sequence.GetComponent<TutorialAnchorHighlighter>();
        if (highlighter == null)
            highlighter = sequence.gameObject.AddComponent<TutorialAnchorHighlighter>();

        bool found = false;
        for (int i = 0; i < calls.arraySize; i++)
        {
            SerializedProperty call = calls.GetArrayElementAtIndex(i);
            if (call.FindPropertyRelative("m_Target").objectReferenceValue != highlighter ||
                call.FindPropertyRelative("m_MethodName").stringValue != "HighlightAnchor") continue;
            ConfigureCall(call, highlighter, anchor);
            found = true;
            break;
        }

        if (!found)
        {
            calls.InsertArrayElementAtIndex(calls.arraySize);
            ConfigureCall(calls.GetArrayElementAtIndex(calls.arraySize - 1), highlighter, anchor);
        }

        step.FindPropertyRelative("message").stringValue = UpdatedMessage;
        serializedSequence.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(highlighter);
        EditorUtility.SetDirty(sequence);
        EditorSceneManager.MarkSceneDirty(scene);
        if (saveIfSafe) EditorSceneManager.SaveScene(scene);

        Debug.Log(
            $"[TutorialAnchorHighlightSetup] {SequenceName} step {stepIndex + 1} now highlights '{anchor.name}'.",
            highlighter);
    }

    private static void ConfigureCall(
        SerializedProperty call,
        TutorialAnchorHighlighter highlighter,
        Transform anchor)
    {
        call.FindPropertyRelative("m_Target").objectReferenceValue = highlighter;
        call.FindPropertyRelative("m_TargetAssemblyTypeName").stringValue =
            "TutorialAnchorHighlighter, Assembly-CSharp";
        call.FindPropertyRelative("m_MethodName").stringValue = "HighlightAnchor";
        call.FindPropertyRelative("m_Mode").enumValueIndex = 2;
        SerializedProperty arguments = call.FindPropertyRelative("m_Arguments");
        arguments.FindPropertyRelative("m_ObjectArgument").objectReferenceValue = anchor;
        arguments.FindPropertyRelative("m_ObjectArgumentAssemblyTypeName").stringValue =
            "UnityEngine.Transform, UnityEngine";
        arguments.FindPropertyRelative("m_IntArgument").intValue = 0;
        arguments.FindPropertyRelative("m_FloatArgument").floatValue = 0f;
        arguments.FindPropertyRelative("m_StringArgument").stringValue = string.Empty;
        arguments.FindPropertyRelative("m_BoolArgument").boolValue = false;
        call.FindPropertyRelative("m_CallState").enumValueIndex = 2;
    }

    private static int FindAnchorStep(TutorialSequence sequence)
    {
        if (sequence.tutorialSteps == null) return -1;
        for (int i = 0; i < sequence.tutorialSteps.Length; i++)
        {
            TutorialStep step = sequence.tutorialSteps[i];
            if (step != null && !string.IsNullOrEmpty(step.message) &&
                step.message.IndexOf(OldPhrase, System.StringComparison.OrdinalIgnoreCase) >= 0)
                return i;
        }
        return -1;
    }

    private static TutorialSequence FindSequence(Scene scene, string objectName)
    {
        foreach (TutorialSequence sequence in Resources.FindObjectsOfTypeAll<TutorialSequence>())
            if (sequence != null && sequence.gameObject.scene == scene && sequence.name == objectName)
                return sequence;
        return null;
    }
}
#endif
