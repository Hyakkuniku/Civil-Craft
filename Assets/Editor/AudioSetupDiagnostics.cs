using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// Read-only report: never changes scenes, volume settings, or playback.
[InitializeOnLoad]
public static class AudioSetupDiagnostics
{
    private static double nextCheck;
    private static string previousReport;

    static AudioSetupDiagnostics() { EditorApplication.update += Check; }

    private static void Check()
    {
        if (EditorApplication.isCompiling || EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 3;
        var report = new StringBuilder();
        report.AppendLine($"Playing={EditorApplication.isPlaying}; Listener pause={AudioListener.pause}; volume={AudioListener.volume}");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/AudioManager.prefab");
        report.AppendLine($"Prefab loaded={prefab != null}; valid={prefab != null && PrefabUtility.IsPartOfPrefabAsset(prefab)}");
        if (prefab != null) Describe(prefab.GetComponent<AudioManager>(), report, "Prefab");
        foreach (var manager in Resources.FindObjectsOfTypeAll<AudioManager>())
        {
            if (!manager.gameObject.scene.IsValid()) continue;
            Describe(manager, report, manager.gameObject.scene.name);
            foreach (var source in manager.GetComponentsInChildren<AudioSource>(true))
                report.AppendLine($"Source {source.name}: active={source.gameObject.activeInHierarchy}, enabled={source.enabled}, playing={source.isPlaying}, mute={source.mute}, volume={source.volume}, clip={source.clip?.name}");
        }
        foreach (var listener in Object.FindObjectsOfType<AudioListener>(true))
            report.AppendLine($"Listener {listener.name}: active={listener.gameObject.activeInHierarchy}, enabled={listener.enabled}");
        var mixer = AssetDatabase.LoadAssetAtPath<UnityEngine.Audio.AudioMixer>("Assets/MainAudioMixer.mixer");
        if (mixer != null)
            foreach (string key in new[] { "MasterVolume", "MusicVolume", "SFXVolume" })
                if (mixer.GetFloat(key, out float value)) report.AppendLine($"Mixer {key}={value}");
        string text = report.ToString();
        if (text == previousReport) return;
        previousReport = text;
        Directory.CreateDirectory("Temp");
        File.WriteAllText("Temp/AudioSetupDiagnostics.txt", text);
    }

    private static void Describe(AudioManager manager, StringBuilder report, string label)
    {
        if (manager == null) { report.AppendLine(label + ": missing AudioManager component"); return; }
        var data = new SerializedObject(manager);
        report.AppendLine($"{label}: active={manager.gameObject.activeInHierarchy}, enabled={manager.enabled}, singleton={AudioManager.Instance == manager}, music={data.FindProperty("musicTracks").arraySize}, sfx={data.FindProperty("soundEffects").arraySize}, startup={data.FindProperty("playMusicOnStart").boolValue}");
    }
}
