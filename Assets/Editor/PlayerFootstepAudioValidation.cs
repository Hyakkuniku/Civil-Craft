#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Read-only wiring check; never changes player scenes or starts audio.</summary>
[InitializeOnLoad]
public static class PlayerFootstepAudioValidation
{
    private const string SessionKey = "CivilCraft.PlayerFootsteps.Validated.v1";

    static PlayerFootstepAudioValidation() { EditorApplication.delayCall += TryValidate; }

    private static void TryValidate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SessionState.GetBool(SessionKey, false)) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        { EditorApplication.delayCall += TryValidate; return; }
        Validate();
    }

    [MenuItem("Tools/Civil Craft/Validate Player Footstep Audio")]
    public static void Validate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        Scene staging = EditorSceneManager.NewPreviewScene();
        try
        {
            PlayerFootstepAudioSettings settings = Resources.Load<PlayerFootstepAudioSettings>("PlayerFootstepAudioSettings");
            Require(settings != null, "Shared Resources settings missing.");
            Require(settings.walkingClip != null && settings.runningClip != null, "Walk/run clip reference missing.");
            Require(AssetDatabase.GetAssetPath(settings.walkingClip) == "Assets/Elements/SFX/Player/walking2.mp3", "Wrong walk clip.");
            Require(AssetDatabase.GetAssetPath(settings.runningClip) == "Assets/Elements/SFX/Player/run.mp3", "Wrong run clip.");
            Require(settings.output != null && settings.output.name == "SFX", "Footsteps must route through the SFX mixer.");
            foreach (AudioClip clip in new[] { settings.walkingClip, settings.runningClip })
            {
                Require(clip.length > 0f, "Empty audio clip.");
                AudioImporter importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip)) as AudioImporter;
                Require(importer != null && importer.forceToMono && importer.defaultSampleSettings.preloadAudioData,
                    "Clips must be mono and preloaded for mobile/spatial playback.");
            }
            ValidateSources(staging, settings, false);
            ValidateSources(staging, settings, true);
            SessionState.SetBool(SessionKey, true);
            Debug.Log($"[Player footsteps] Wiring verified: walk={settings.walkingClip.length:0.00}s, " +
                $"run={settings.runningClip.length:0.00}s; local 2D / proxy 3D; SFX mixer; two reusable non-autoplay loops.");
        }
        catch (Exception exception) { Debug.LogException(exception); }
        finally { EditorSceneManager.ClosePreviewScene(staging); }
    }

    private static void ValidateSources(Scene staging, PlayerFootstepAudioSettings settings, bool remote)
    {
        GameObject player = new GameObject(remote ? "Proxy Footstep Check" : "Local Footstep Check");
        SceneManager.MoveGameObjectToScene(player, staging);
        PlayerMovementAudio audio = PlayerMovementAudio.Attach(player, remote);
        Require(audio != null, "Movement audio failed to attach.");
        Require(PlayerMovementAudio.Attach(player, remote) == audio, "Duplicate movement audio component.");
        AudioSource[] sources = player.GetComponentsInChildren<AudioSource>(true);
        Require(sources.Length == 2, "Must reuse exactly two loop sources, even after rebinding.");
        Require(sources[0].clip == settings.walkingClip && sources[1].clip == settings.runningClip, "Clip/source mapping incorrect.");
        foreach (AudioSource source in sources)
        {
            Require(source.loop && !source.playOnAwake && !source.isPlaying && source.volume == 0f, "A stationary player must stay silent.");
            Require(source.outputAudioMixerGroup == settings.output, "Source bypasses SFX volume settings.");
            Require(source.spatialBlend == (remote ? 1f : 0f) && source.dopplerLevel == 0f, "Spatial/local routing incorrect.");
            Require(!source.ignoreListenerPause, "Footsteps must respect pause.");
        }
        audio.SetLocalMovement(5f, true, true, true);
        audio.StopImmediately();
        Require(audio.CurrentGait == PlayerFootstepGait.Silent, "Cleanup did not reset locomotion.");
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("[Player footsteps] " + message); }
}
#endif
