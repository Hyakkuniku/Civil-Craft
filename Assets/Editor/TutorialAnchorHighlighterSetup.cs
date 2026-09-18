#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class TutorialAnchorHighlighterSetup
{
    private const string SessionKey = "CivilCraft.TutorialAnchorHighlighterSetup.V2";
    private const string SequenceName = "Sequence_Build1";
    private const string UpdatedMessage =
        "Click the <b><#E09500>highlighted anchor</color></b> and drag across the gap to build the road.";
    private const string ExplanationMessage =
        "These highlighted points are <b><#E09500>anchors</color></b>. Every build location has anchors—look for them first, because your bridge must connect between them.";

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

        TutorialAnchorHighlighter highlighter =
            sequence.GetComponent<TutorialAnchorHighlighter>();
        if (highlighter == null)
            highlighter = sequence.gameObject.AddComponent<TutorialAnchorHighlighter>();

        SerializedObject serializedSequence = new SerializedObject(sequence);
        SerializedProperty steps = serializedSequence.FindProperty("tutorialSteps");
        SerializedProperty constructionStep = steps.GetArrayElementAtIndex(stepIndex);
        SerializedProperty calls = GetCalls(constructionStep);
        var anchors = new List<Transform>();

        // Preserve manually authored tutorial highlights and migrate the old pointer event.
        for (int i = calls.arraySize - 1; i >= 0; i--)
        {
            SerializedProperty call = calls.GetArrayElementAtIndex(i);
            Object target = call.FindPropertyRelative("m_Target").objectReferenceValue;
            string method = call.FindPropertyRelative("m_MethodName").stringValue;
            Transform argument = GetTransformArgument(call);
            if (method == "HighlightAnchor" && target is TutorialAnchorHighlighter)
            {
                AddAnchor(anchors, argument);
                calls.DeleteArrayElementAtIndex(i);
            }
            else if (method == "ShowAtPosition" && target is Tutorial3DIndicator)
            {
                AddAnchor(anchors, argument);
                calls.DeleteArrayElementAtIndex(i);
            }
        }

        ExpandToBuildLocationAnchors(anchors);
        if (anchors.Count == 0)
        {
            Debug.LogError(
                "[TutorialAnchorHighlightSetup] The construction step has no valid Point targets.", sequence);
            return;
        }

        foreach (Transform anchor in anchors)
            AppendHighlightCall(calls, highlighter, anchor);
        constructionStep.FindPropertyRelative("message").stringValue = UpdatedMessage;
        constructionStep.FindPropertyRelative("showNextButton").boolValue = false;
        serializedSequence.ApplyModifiedPropertiesWithoutUndo();

        serializedSequence.Update();
        steps = serializedSequence.FindProperty("tutorialSteps");
        bool explanationAlreadyExists = stepIndex > 0 &&
            steps.GetArrayElementAtIndex(stepIndex - 1).FindPropertyRelative("message")
                .stringValue == ExplanationMessage;
        int explanationIndex = explanationAlreadyExists ? stepIndex - 1 : stepIndex;
        if (!explanationAlreadyExists)
            steps.InsertArrayElementAtIndex(explanationIndex);

        ConfigureExplanationStep(
            steps.GetArrayElementAtIndex(explanationIndex), highlighter, anchors);
        serializedSequence.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(highlighter);
        EditorUtility.SetDirty(sequence);
        EditorSceneManager.MarkSceneDirty(scene);
        if (saveIfSafe) EditorSceneManager.SaveScene(scene);

        Debug.Log(
            $"[TutorialAnchorHighlightSetup] Added the anchor lesson before the road step and wired {anchors.Count} anchor highlight(s).",
            highlighter);
    }

    private static void ConfigureExplanationStep(
        SerializedProperty step,
        TutorialAnchorHighlighter highlighter,
        List<Transform> anchors)
    {
        step.FindPropertyRelative("message").stringValue = ExplanationMessage;
        step.FindPropertyRelative("screenPosition").enumValueIndex = (int)TutorialPosition.Left;
        step.FindPropertyRelative("showNextButton").boolValue = true;
        step.FindPropertyRelative("canSkip").boolValue = false;
        step.FindPropertyRelative("lockLook").boolValue = false;
        step.FindPropertyRelative("lockJump").boolValue = false;
        step.FindPropertyRelative("lockRun").boolValue = false;
        step.FindPropertyRelative("stepWaypoints").arraySize = 0;
        step.FindPropertyRelative("worldHighlightObject").objectReferenceValue = null;
        step.FindPropertyRelative("usePointer").boolValue = false;
        step.FindPropertyRelative("pointerTarget").objectReferenceValue = null;
        step.FindPropertyRelative("pointerOffset").vector2Value = new Vector2(0f, 80f);
        step.FindPropertyRelative("pointerRotation").floatValue = 180f;
        step.FindPropertyRelative("advanceOnClick").boolValue = false;
        step.FindPropertyRelative("requiredAction").enumValueIndex = (int)TutorialStepAction.None;

        SerializedProperty calls = GetCalls(step);
        calls.arraySize = 0;
        foreach (Transform anchor in anchors)
            AppendHighlightCall(calls, highlighter, anchor);
    }

    private static SerializedProperty GetCalls(SerializedProperty step)
    {
        return step.FindPropertyRelative("OnStepStart")
            .FindPropertyRelative("m_PersistentCalls")
            .FindPropertyRelative("m_Calls");
    }

    private static Transform GetTransformArgument(SerializedProperty call)
    {
        return call.FindPropertyRelative("m_Arguments")
            .FindPropertyRelative("m_ObjectArgument").objectReferenceValue as Transform;
    }

    private static void AddAnchor(List<Transform> anchors, Transform candidate)
    {
        if (candidate == null || candidate.GetComponentInParent<Point>() == null ||
            anchors.Contains(candidate)) return;
        anchors.Add(candidate);
    }

    private static void ExpandToBuildLocationAnchors(List<Transform> anchors)
    {
        if (anchors.Count == 0) return;
        Point firstPoint = anchors[0].GetComponentInParent<Point>();
        BuildLocation location = firstPoint != null ? firstPoint.GetComponentInParent<BuildLocation>() : null;
        if (location == null)
        {
            foreach (BuildLocation candidate in Resources.FindObjectsOfTypeAll<BuildLocation>())
            {
                if (candidate == null || candidate.gameObject.scene != firstPoint.gameObject.scene) continue;
                if (candidate.startingAnchors.Contains(firstPoint) || candidate.endingAnchors.Contains(firstPoint))
                {
                    location = candidate;
                    break;
                }
            }
        }

        if (location == null) return;
        foreach (Point point in location.startingAnchors)
            if (point != null) AddAnchor(anchors, point.transform);
        foreach (Point point in location.endingAnchors)
            if (point != null) AddAnchor(anchors, point.transform);
    }

    private static void AppendHighlightCall(
        SerializedProperty calls,
        TutorialAnchorHighlighter highlighter,
        Transform anchor)
    {
        int newIndex = calls.arraySize;
        calls.arraySize++;
        ConfigureCall(calls.GetArrayElementAtIndex(newIndex), highlighter, anchor);
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
                step.message.IndexOf("anchor", System.StringComparison.OrdinalIgnoreCase) >= 0 &&
                step.message.IndexOf("drag across the gap", System.StringComparison.OrdinalIgnoreCase) >= 0)
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
