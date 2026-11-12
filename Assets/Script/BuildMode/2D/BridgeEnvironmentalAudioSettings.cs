using UnityEngine;

[CreateAssetMenu(menuName = "Civil Craft/Audio/Wind and Earthquake")]
public sealed class BridgeEnvironmentalAudioSettings : ScriptableObject
{
    public AudioClip windLoop;
    public AudioClip earthquakeLoop;
    [Range(0f, 1f)] public float windVolume = .8f;
    [Range(0f, 1f)] public float earthquakeVolume = .85f;
}
