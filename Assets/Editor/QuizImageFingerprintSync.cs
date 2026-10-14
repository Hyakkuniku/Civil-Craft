using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>Image edits invalidate quiz progress even when the Quiz asset itself was not edited.</summary>
public sealed class QuizImageFingerprintSync : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    [InitializeOnLoadMethod]
    private static void Watch()
    {
        EditorApplication.playModeStateChanged -= HandlePlayMode;
        EditorApplication.playModeStateChanged += HandlePlayMode;
    }

    private static void HandlePlayMode(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.ExitingEditMode) return;
        foreach (QuizData quiz in Resources.FindObjectsOfTypeAll<QuizData>())
            if (EditorUtility.IsPersistent(quiz)) Sync(quiz);
    }

    public void OnPreprocessBuild(BuildReport report)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:QuizData"))
        {
            QuizData quiz = AssetDatabase.LoadAssetAtPath<QuizData>(AssetDatabase.GUIDToAssetPath(guid));
            if (quiz == null) continue;
            Sync(quiz);
        }
    }

    private static void Sync(QuizData quiz)
    {
        if (!quiz.RefreshImageFingerprints()) return;
        EditorUtility.SetDirty(quiz);
        AssetDatabase.SaveAssetIfDirty(quiz);
    }
}
