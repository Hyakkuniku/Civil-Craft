using UnityEngine;
using UnityEngine.Audio;

/// <summary>Shared clip and mixer wiring for players, NPCs, and multiplayer proxies.</summary>
[CreateAssetMenu(menuName = "Civil Craft/Audio/Player Footsteps")]
public sealed class PlayerFootstepAudioSettings : ScriptableObject
{
    public AudioClip walkingClip;
    public AudioClip runningClip;
    public AudioMixerGroup output;
    [Range(0f, 1f)] public float walkingVolume = 0.4f;
    [Range(0f, 1f)] public float runningVolume = 0.45f;
    [Min(0.01f)] public float fadeSeconds = 0.08f;
    [Min(0.01f)] public float minimumMovementSpeed = 0.15f;
    [Min(0.1f)] public float remoteMinDistance = 3f;
    [Min(0.1f)] public float remoteMaxDistance = 18f;
    [Min(1f)] public float teleportDistance = 8f;
}
