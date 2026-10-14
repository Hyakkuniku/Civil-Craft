using System;
using UnityEngine;
using UnityEngine.Audio;

[Serializable]
public sealed class VehicleSimulationSound
{
    [Tooltip("A seamless engine or rolling loop for this vehicle. Empty means silent.")]
    public AudioClip clip;
    [Tooltip("Optional override. Defaults to the active AudioManager's SFX output.")]
    public AudioMixerGroup output;
    [Range(0f, 1f)] public float volume = 0.65f;
    [Range(0.1f, 3f)] public float pitch = 1f;
}

/// <summary>One owned loop, usable by a simulated vehicle or its mesh-only spectator.</summary>
public sealed class VehicleSimulationLoop : IDisposable
{
    private const string ChannelName = "Vehicle Simulation Audio (Runtime)";
    private readonly Transform owner;
    private AudioSource source;
    private bool suspended;

    public VehicleSimulationLoop(Transform owner) { this.owner = owner; }

    public void Tick(VehicleSimulationSound settings, bool driving, bool paused)
    {
        if (!driving || owner == null || !owner.gameObject.activeInHierarchy || settings == null || settings.clip == null)
        { Stop(); return; }
        if (paused)
        {
            if (source != null && source.isPlaying) { source.Pause(); suspended = true; }
            return;
        }
        if (source == null)
        {
            // Host test clones can inherit this child from an already used vehicle.
            // Reuse its channel instead of adding another source on each test.
            Transform channel = owner.Find(ChannelName);
            if (channel == null)
            {
                var node = new GameObject(ChannelName);
                node.transform.SetParent(owner, false);
                channel = node.transform;
            }
            source = channel.GetComponent<AudioSource>();
            if (source == null) source = channel.gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.Stop();
            suspended = false;
        }
        if (source.clip != settings.clip) { source.Stop(); source.clip = settings.clip; suspended = false; }
        source.loop = true;
        source.playOnAwake = false;
        source.ignoreListenerPause = false;
        source.dopplerLevel = 0f;
        source.volume = Mathf.Clamp01(settings.volume);
        source.pitch = Mathf.Clamp(settings.pitch, 0.1f, 3f);
        // The simulation camera may be far from the live load. Keep both local
        // and spectator loops at the authored volume regardless of its position.
        source.spatialBlend = 0f;
        source.panStereo = 0f;
        source.spatialize = false;
        source.outputAudioMixerGroup = settings.output != null ? settings.output :
            AudioManager.Instance != null ? AudioManager.Instance.SfxOutput : null;
        if (suspended) { source.UnPause(); suspended = false; }
        if (!source.isPlaying) source.Play();
    }

    public void Suspend()
    {
        if (source != null && source.isPlaying) { source.Pause(); suspended = true; }
    }

    public void Stop()
    {
        if (source != null) source.Stop();
        suspended = false;
    }

    public void Dispose() { Stop(); source = null; }
}
