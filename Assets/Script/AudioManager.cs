using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

[Serializable]
public sealed class AudioLibraryEntry
{
    [Tooltip("Unique name used by code, for example: Click, Walk, PlaceBar.")]
    public string id;
    public AudioClip clip;
    [Range(0f, 1f)] public float volume = 1f;
    [Range(0.1f, 3f)] public float pitch = 1f;
    [Tooltip("Random pitch variation applied only to SFX. Zero disables it.")]
    [Range(0f, 0.5f)] public float randomPitchRange;
}

/// <summary>
/// Persistent global audio service with crossfaded music and pooled,
/// overlapping sound effects.
/// </summary>
[DisallowMultipleComponent]
public sealed class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        // Required when Enter Play Mode Options has Domain Reload disabled.
        Instance = null;
    }

    [Header("Lifetime")]
    [SerializeField] private bool persistAcrossScenes = true;

    [Header("Mixer Routing")]
    [Tooltip("Assign MainAudioMixer/Music.")]
    [SerializeField] private AudioMixerGroup musicOutput;
    [Tooltip("Assign MainAudioMixer/SFX.")]
    [SerializeField] private AudioMixerGroup sfxOutput;

    [Header("Audio Library")]
    [SerializeField] private List<AudioLibraryEntry> musicTracks =
        new List<AudioLibraryEntry>();
    [SerializeField] private List<AudioLibraryEntry> soundEffects =
        new List<AudioLibraryEntry>();

    [Header("Startup Music")]
    [SerializeField] private bool playMusicOnStart;
    [SerializeField] private string startingMusicId;
    [Min(0f)] [SerializeField] private float defaultMusicFadeDuration = 0.75f;

    [Header("SFX Pool")]
    [Min(1)] [SerializeField] private int initialSfxVoices = 12;
    [Min(1)] [SerializeField] private int maximumSfxVoices = 32;
    [Tooltip("Useful for UI sounds when AudioListener.pause is enabled.")]
    [SerializeField] private bool sfxIgnoreListenerPause = true;

    private readonly Dictionary<string, AudioLibraryEntry> musicById =
        new Dictionary<string, AudioLibraryEntry>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AudioLibraryEntry> sfxById =
        new Dictionary<string, AudioLibraryEntry>(StringComparer.OrdinalIgnoreCase);
    private readonly List<SfxVoice> sfxVoices = new List<SfxVoice>();

    private AudioSource firstMusicSource;
    private AudioSource secondMusicSource;
    private AudioSource activeMusicSource;
    private Coroutine musicFadeCoroutine;
    private string currentMusicId;

    public string CurrentMusicId => currentMusicId;
    public bool IsMusicPlaying =>
        (firstMusicSource != null && firstMusicSource.isPlaying) ||
        (secondMusicSource != null && secondMusicSource.isPlaying);

    private sealed class SfxVoice
    {
        public AudioSource source;
        public double startedAt;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        if (persistAcrossScenes)
        {
            // DontDestroyOnLoad only works on root GameObjects. Some scenes keep
            // this manager inside a Managers container, so detach it at runtime.
            if (transform.parent != null)
                transform.SetParent(null, true);

            DontDestroyOnLoad(gameObject);
        }

        RebuildLibrary();
        CreateAudioChannels();
    }

    private void OnEnable()
    {
        if (Instance == this)
            SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void OnDisable()
    {
        if (Instance == this)
            SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    private void Start()
    {
        if (Instance != this) return;

        if (playMusicOnStart && !string.IsNullOrWhiteSpace(startingMusicId))
            PlayMusic(startingMusicId);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode loadMode)
    {
        // Awake normally removes a scene-local duplicate before this callback.
        // This second pass also catches managers enabled or instantiated late.
        AudioManager[] managers = Resources.FindObjectsOfTypeAll<AudioManager>();
        foreach (AudioManager manager in managers)
        {
            if (manager == null || manager == this) continue;

            Scene managerScene = manager.gameObject.scene;
            if (!managerScene.IsValid() || !managerScene.isLoaded) continue;

            Destroy(manager.gameObject);
        }
    }

    /// <summary>Plays a 2D sound effect. Multiple calls can overlap.</summary>
    public void PlaySFX(string id)
    {
        PlaySFX(id, 1f);
    }

    public void PlaySFX(string id, float volumeMultiplier)
    {
        if (!TryGetEntry(sfxById, id, "SFX", out AudioLibraryEntry sound))
            return;

        SfxVoice voice = GetAvailableSfxVoice();
        AudioSource source = voice.source;
        source.transform.localPosition = Vector3.zero;
        source.spatialBlend = 0f;
        source.clip = sound.clip;
        source.volume = sound.volume * Mathf.Max(0f, volumeMultiplier);
        source.pitch = GetRandomizedPitch(sound);
        source.loop = false;
        voice.startedAt = AudioSettings.dspTime;
        source.Play();
    }

    /// <summary>Optional world-space variant for knocks, footsteps, and construction.</summary>
    public void PlaySFXAtPosition(string id, Vector3 worldPosition)
    {
        if (!TryGetEntry(sfxById, id, "SFX", out AudioLibraryEntry sound))
            return;

        SfxVoice voice = GetAvailableSfxVoice();
        AudioSource source = voice.source;
        source.transform.position = worldPosition;
        source.spatialBlend = 1f;
        source.clip = sound.clip;
        source.volume = sound.volume;
        source.pitch = GetRandomizedPitch(sound);
        source.loop = false;
        voice.startedAt = AudioSettings.dspTime;
        source.Play();
    }

    /// <summary>Crossfades from the current track to the requested track.</summary>
    public void PlayMusic(string id)
    {
        PlayMusic(id, defaultMusicFadeDuration);
    }

    public void PlayMusic(string id, float fadeDuration)
    {
        if (!TryGetEntry(musicById, id, "music", out AudioLibraryEntry music))
            return;

        if (activeMusicSource != null && activeMusicSource.isPlaying &&
            activeMusicSource.clip == music.clip)
        {
            currentMusicId = id;
            return;
        }

        StopMusicFadeCoroutine();

        AudioSource previous = activeMusicSource != null && activeMusicSource.isPlaying
            ? activeMusicSource
            : null;
        AudioSource next = previous == firstMusicSource
            ? secondMusicSource
            : firstMusicSource;

        next.Stop();
        next.clip = music.clip;
        next.loop = true;
        next.pitch = music.pitch;
        next.volume = fadeDuration > 0f ? 0f : music.volume;
        next.Play();

        activeMusicSource = next;
        currentMusicId = id;

        if (fadeDuration <= 0f)
        {
            if (previous != null)
            {
                previous.Stop();
                previous.clip = null;
            }
            return;
        }

        musicFadeCoroutine = StartCoroutine(
            CrossfadeMusic(previous, next, music.volume, fadeDuration));
    }

    public void StopMusic()
    {
        StopMusic(defaultMusicFadeDuration);
    }

    public void StopMusic(float fadeDuration)
    {
        StopMusicFadeCoroutine();
        currentMusicId = null;

        if (fadeDuration <= 0f)
        {
            StopAndClearMusicSource(firstMusicSource);
            StopAndClearMusicSource(secondMusicSource);
            activeMusicSource = null;
            return;
        }

        musicFadeCoroutine = StartCoroutine(FadeOutAllMusic(fadeDuration));
    }

    public void PauseMusic()
    {
        if (firstMusicSource != null) firstMusicSource.Pause();
        if (secondMusicSource != null) secondMusicSource.Pause();
    }

    public void ResumeMusic()
    {
        if (firstMusicSource != null && firstMusicSource.clip != null) firstMusicSource.UnPause();
        if (secondMusicSource != null && secondMusicSource.clip != null) secondMusicSource.UnPause();
    }

    public void StopAllSFX()
    {
        foreach (SfxVoice voice in sfxVoices)
            if (voice.source != null) voice.source.Stop();
    }

    private void RebuildLibrary()
    {
        musicById.Clear();
        sfxById.Clear();
        AddEntriesToLookup(musicTracks, musicById, "music");
        AddEntriesToLookup(soundEffects, sfxById, "SFX");
    }

    private void AddEntriesToLookup(
        List<AudioLibraryEntry> entries,
        Dictionary<string, AudioLibraryEntry> lookup,
        string libraryName)
    {
        foreach (AudioLibraryEntry entry in entries)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.id) || entry.clip == null)
                continue;

            string normalizedId = entry.id.Trim();
            if (lookup.ContainsKey(normalizedId))
            {
                Debug.LogWarning(
                    $"AudioManager ignored duplicate {libraryName} ID '{normalizedId}'.",
                    this);
                continue;
            }

            lookup.Add(normalizedId, entry);
        }
    }

    private void CreateAudioChannels()
    {
        firstMusicSource = CreateSource("Music Channel A", musicOutput, true, false);
        secondMusicSource = CreateSource("Music Channel B", musicOutput, true, false);

        int initialCount = Mathf.Max(1, initialSfxVoices);
        maximumSfxVoices = Mathf.Max(initialCount, maximumSfxVoices);
        for (int i = 0; i < initialCount; i++)
            AddSfxVoice();
    }

    private AudioSource CreateSource(
        string objectName,
        AudioMixerGroup output,
        bool loop,
        bool ignoreListenerPause)
    {
        GameObject channel = new GameObject(objectName);
        channel.transform.SetParent(transform, false);
        AudioSource source = channel.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = loop;
        source.spatialBlend = 0f;
        source.outputAudioMixerGroup = output;
        source.ignoreListenerPause = ignoreListenerPause;
        return source;
    }

    private SfxVoice AddSfxVoice()
    {
        AudioSource source = CreateSource(
            $"SFX Voice {sfxVoices.Count + 1}",
            sfxOutput,
            false,
            sfxIgnoreListenerPause);
        SfxVoice voice = new SfxVoice { source = source };
        sfxVoices.Add(voice);
        return voice;
    }

    private SfxVoice GetAvailableSfxVoice()
    {
        foreach (SfxVoice voice in sfxVoices)
        {
            if (!voice.source.isPlaying)
                return voice;
        }

        if (sfxVoices.Count < maximumSfxVoices)
            return AddSfxVoice();

        SfxVoice oldest = sfxVoices[0];
        for (int i = 1; i < sfxVoices.Count; i++)
        {
            if (sfxVoices[i].startedAt < oldest.startedAt)
                oldest = sfxVoices[i];
        }

        oldest.source.Stop();
        return oldest;
    }

    private IEnumerator CrossfadeMusic(
        AudioSource previous,
        AudioSource next,
        float targetVolume,
        float duration)
    {
        float previousStartVolume = previous != null ? previous.volume : 0f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            if (previous != null) previous.volume = Mathf.Lerp(previousStartVolume, 0f, progress);
            if (next != null) next.volume = Mathf.Lerp(0f, targetVolume, progress);
            yield return null;
        }

        if (previous != null)
        {
            previous.Stop();
            previous.clip = null;
        }
        if (next != null) next.volume = targetVolume;
        musicFadeCoroutine = null;
    }

    private IEnumerator FadeOutAllMusic(float duration)
    {
        float firstStartVolume = firstMusicSource != null ? firstMusicSource.volume : 0f;
        float secondStartVolume = secondMusicSource != null ? secondMusicSource.volume : 0f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            if (firstMusicSource != null)
                firstMusicSource.volume = Mathf.Lerp(firstStartVolume, 0f, progress);
            if (secondMusicSource != null)
                secondMusicSource.volume = Mathf.Lerp(secondStartVolume, 0f, progress);
            yield return null;
        }

        StopAndClearMusicSource(firstMusicSource);
        StopAndClearMusicSource(secondMusicSource);
        activeMusicSource = null;
        musicFadeCoroutine = null;
    }

    private void StopMusicFadeCoroutine()
    {
        if (musicFadeCoroutine == null)
            return;

        StopCoroutine(musicFadeCoroutine);
        musicFadeCoroutine = null;
    }

    private static void StopAndClearMusicSource(AudioSource source)
    {
        if (source == null) return;
        source.Stop();
        source.clip = null;
        source.volume = 0f;
    }

    private static float GetRandomizedPitch(AudioLibraryEntry sound)
    {
        float variation = UnityEngine.Random.Range(
            -sound.randomPitchRange,
            sound.randomPitchRange);
        return Mathf.Clamp(sound.pitch + variation, 0.1f, 3f);
    }

    private bool TryGetEntry(
        Dictionary<string, AudioLibraryEntry> lookup,
        string id,
        string libraryName,
        out AudioLibraryEntry entry)
    {
        entry = null;
        if (!string.IsNullOrWhiteSpace(id) && lookup.TryGetValue(id.Trim(), out entry))
            return true;

        Debug.LogWarning($"AudioManager could not find {libraryName} ID '{id}'.", this);
        return false;
    }
}
