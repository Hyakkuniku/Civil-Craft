using UnityEngine;

/// <summary>
/// Two reusable loops, driven by actual locomotion. Remote players reuse the
/// existing replicated pose/animation state; no per-footstep network traffic.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]
public sealed class PlayerMovementAudio : MonoBehaviour
{
    private const string SettingsResource = "PlayerFootstepAudioSettings";
    private const float RemoteStateTimeout = 0.25f;
    private PlayerFootstepAudioSettings settings;
    private AudioSource walkSource;
    private AudioSource runSource;
    private bool remote;
    private bool wantsMovement;
    private bool sprinting;
    private bool grounded;
    private float planarSpeed;
    private float lastStateTime = float.NegativeInfinity;
    private Vector3 previousPosition;
    private bool hasPreviousPosition;
    private bool applicationPaused;
    private bool applicationFocused = true;

    public PlayerFootstepGait CurrentGait { get; private set; }

    public static PlayerMovementAudio Attach(GameObject owner, bool remotePlayer)
    {
        if (owner == null) return null;
        PlayerFootstepAudioSettings configuration = Resources.Load<PlayerFootstepAudioSettings>(SettingsResource);
        if (configuration == null)
        {
            Debug.LogWarning("[Player footsteps] Missing Resources/PlayerFootstepAudioSettings asset.", owner);
            return null;
        }
        PlayerMovementAudio audio = owner.GetComponent<PlayerMovementAudio>();
        if (audio == null) audio = owner.AddComponent<PlayerMovementAudio>();
        audio.Configure(configuration, remotePlayer);
        return audio;
    }

    private void Configure(PlayerFootstepAudioSettings configuration, bool remotePlayer)
    {
        settings = configuration;
        remote = remotePlayer;
        if (walkSource == null) walkSource = CreateSource("Footsteps - Walk", settings.walkingClip);
        if (runSource == null) runSource = CreateSource("Footsteps - Run", settings.runningClip);
        ConfigureSpatialAudio(walkSource);
        ConfigureSpatialAudio(runSource);
        StopImmediately();
    }

    private AudioSource CreateSource(string objectName, AudioClip clip)
    {
        GameObject channel = new GameObject(objectName);
        channel.transform.SetParent(transform, false);
        AudioSource source = channel.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = true;
        source.clip = clip;
        source.outputAudioMixerGroup = settings.output;
        source.volume = 0f;
        source.pitch = 1f;
        source.dopplerLevel = 0f;
        source.ignoreListenerPause = false;
        source.priority = 160;
        return source;
    }

    private void ConfigureSpatialAudio(AudioSource source)
    {
        source.spatialBlend = remote ? 1f : 0f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = Mathf.Max(0.1f, settings.remoteMinDistance);
        source.maxDistance = Mathf.Max(source.minDistance + 0.1f, settings.remoteMaxDistance);
    }

    /// <summary>Called after CharacterController.Move, not from joystick intent alone.</summary>
    public void SetLocalMovement(float actualPlanarSpeed, bool isSprinting, bool isGrounded, bool hasMovementInput)
    {
        if (remote) return;
        planarSpeed = actualPlanarSpeed;
        sprinting = isSprinting;
        grounded = isGrounded;
        wantsMovement = hasMovementInput;
        lastStateTime = Time.unscaledTime;
    }

    /// <summary>Proxy travel is measured from the interpolated, visible root position.</summary>
    public void SetRemoteMovement(float moveAmount, bool isSprinting, bool isGrounded)
    {
        if (!remote) return;
        wantsMovement = moveAmount > 0.1f;
        sprinting = isSprinting;
        grounded = isGrounded;
        lastStateTime = Time.unscaledTime;
    }

    private void LateUpdate()
    {
        if (settings == null) return;
        bool teleported = false;
        if (remote)
        {
            Vector3 position = transform.position;
            Vector3 delta = hasPreviousPosition ? position - previousPosition : Vector3.zero;
            teleported = delta.sqrMagnitude > settings.teleportDistance * settings.teleportDistance;
            delta.y = 0f;
            planarSpeed = !teleported && Time.unscaledDeltaTime > 0.0001f
                ? delta.magnitude / Time.unscaledDeltaTime : 0f;
            previousPosition = position;
            hasPreviousPosition = true;
        }
        bool paused = applicationPaused || !applicationFocused || AudioListener.pause || Time.timeScale <= 0f;
        float timeout = remote ? RemoteStateTimeout : Mathf.Max(0.12f, Time.fixedDeltaTime * 3f);
        bool allowed = !paused && !teleported && wantsMovement && Time.unscaledTime - lastStateTime <= timeout;
        CurrentGait = PlayerFootstepPlayback.Resolve(planarSpeed, sprinting, grounded, allowed, settings.minimumMovementSpeed);
        if (paused)
        {
            StopSources();
            return;
        }
        // Walk/run transitions crossfade rather than restarting a pooled one-shot every frame.
        FadeSource(walkSource, CurrentGait == PlayerFootstepGait.Walk ? settings.walkingVolume : 0f, settings.walkingVolume);
        FadeSource(runSource, CurrentGait == PlayerFootstepGait.Run ? settings.runningVolume : 0f, settings.runningVolume);
    }

    private void FadeSource(AudioSource source, float targetVolume, float fullVolume)
    {
        if (source == null || source.clip == null) return;
        if (targetVolume > 0f && !source.isPlaying)
        {
            // Imported clips are preloaded. Never allocate or reload them while moving.
            source.volume = 0f;
            source.Play();
        }
        source.volume = Mathf.MoveTowards(source.volume, targetVolume,
            Mathf.Max(0.001f, fullVolume) * Time.unscaledDeltaTime / Mathf.Max(0.01f, settings.fadeSeconds));
        if (targetVolume <= 0f && source.volume <= 0f && source.isPlaying) source.Stop();
    }

    public void StopImmediately()
    {
        CurrentGait = PlayerFootstepGait.Silent;
        wantsMovement = grounded = sprinting = false;
        planarSpeed = 0f;
        lastStateTime = float.NegativeInfinity;
        hasPreviousPosition = false;
        StopSources();
    }

    private void StopSources()
    {
        if (walkSource != null) { walkSource.Stop(); walkSource.volume = 0f; }
        if (runSource != null) { runSource.Stop(); runSource.volume = 0f; }
    }

    private void OnDisable() { StopImmediately(); }
    private void OnApplicationPause(bool paused)
    {
        applicationPaused = paused;
        if (paused) StopImmediately();
    }
    private void OnApplicationFocus(bool focused)
    {
        applicationFocused = focused;
        if (!focused) StopImmediately();
    }
}
