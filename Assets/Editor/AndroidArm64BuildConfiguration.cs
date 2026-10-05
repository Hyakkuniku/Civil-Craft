using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

/// <summary>Explicit Android runtime configuration; never builds or installs an app.</summary>
public static class AndroidArm64BuildConfiguration
{
    private const string RequestPath = "Temp/android-arm64-il2cpp.request";
    private const string ReportPath = "Temp/AndroidArm64BuildConfiguration.txt";

    [InitializeOnLoadMethod]
    private static void CheckQueuedConfiguration()
    {
        if (!File.Exists(RequestPath)) return;
        EditorApplication.update -= ApplyQueuedConfiguration;
        EditorApplication.update += ApplyQueuedConfiguration;
    }

    private static void ApplyQueuedConfiguration()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer) return;
        EditorApplication.update -= ApplyQueuedConfiguration;
        if (!File.Exists(RequestPath)) return;
        File.Delete(RequestPath);
        try { Configure(); }
        catch (Exception error)
        {
            File.WriteAllText(ReportPath, "FAIL: " + error);
            Debug.LogException(error);
        }
    }

    [MenuItem("Tools/Civil Craft/Configure Android IL2CPP ARM64")]
    public static void Configure()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
            EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer)
            throw new InvalidOperationException("Stop Play Mode and wait for compilation/building before configuring Android.");

        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        AssetDatabase.SaveAssets();

        if (PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android) != ScriptingImplementation.IL2CPP ||
            PlayerSettings.Android.targetArchitectures != AndroidArchitecture.ARM64)
            throw new InvalidOperationException("Android did not retain IL2CPP and ARM64-only settings.");

        File.WriteAllText(ReportPath,
            "PASS: Android scripting backend = IL2CPP.\n" +
            "PASS: Android target architectures = ARM64 only (ARMv7 disabled).\n" +
            "No graphics settings, scenes, player saves, package identity, builds or device installs were changed.\n" +
            "Next: Build And Run a new APK, then retest the guest submitting first.\n");
        Debug.Log("[Android build configuration] IL2CPP + ARM64-only settings saved. Rebuild the APK before retesting; the installed app is unchanged.");
    }
}
