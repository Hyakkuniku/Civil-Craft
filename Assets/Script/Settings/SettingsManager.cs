using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;
using TMPro;

public class SettingsManager : MonoBehaviour
{
    [Header("Audio Settings")]
    public AudioMixer mainAudioMixer;
    public Slider masterVolumeSlider;
    public Toggle sfxToggle; // Common on mobile to just mute/unmute SFX

    [Header("Graphics & Performance (Mobile)")]
    public TMP_Dropdown qualityDropdown;
    [Tooltip("Toggle ON for 60fps (Smooth), OFF for 30fps (Battery Saver)")]
    public Toggle highFpsToggle; 

    [Header("Gameplay")]
    public Toggle hapticsToggle;

    private void Start()
    {
        LoadSettings();

        // Hook up UI listeners automatically
        if (masterVolumeSlider != null) 
            masterVolumeSlider.onValueChanged.AddListener(SetVolume);
            
        if (sfxToggle != null) 
            sfxToggle.onValueChanged.AddListener(SetSFXMute);
            
        if (qualityDropdown != null) 
            qualityDropdown.onValueChanged.AddListener(SetQuality);
            
        if (highFpsToggle != null) 
            highFpsToggle.onValueChanged.AddListener(SetHighFPS);
            
        if (hapticsToggle != null) 
            hapticsToggle.onValueChanged.AddListener(SetHaptics);
    }

    // ────────────────────────────────────────────────
    // GRAPHICS & PERFORMANCE LOGIC
    // ────────────────────────────────────────────────

    public void SetQuality(int qualityIndex)
    {
        QualitySettings.SetQualityLevel(qualityIndex);
        PlayerPrefs.SetInt("QualityLevel", qualityIndex);
        PlayerPrefs.Save();
    }

    public void SetHighFPS(bool isHighFps)
    {
        // 60 FPS feels great but drains mobile batteries fast. 30 FPS is standard for battery saving.
        Application.targetFrameRate = isHighFps ? 60 : 30;
        PlayerPrefs.SetInt("HighFPS", isHighFps ? 1 : 0);
        PlayerPrefs.Save();
    }

    // ────────────────────────────────────────────────
    // AUDIO LOGIC
    // ────────────────────────────────────────────────

    public void SetVolume(float sliderValue)
    {
        // Ensure sliderValue never hits absolute 0 to avoid Log10 math errors
        float clampedValue = Mathf.Clamp(sliderValue, 0.0001f, 1f);
        float decibels = Mathf.Log10(clampedValue) * 20f;
        
        if (mainAudioMixer != null)
            mainAudioMixer.SetFloat("MasterVolume", decibels);

        PlayerPrefs.SetFloat("MasterVolume", sliderValue);
        PlayerPrefs.Save();
    }

    public void SetSFXMute(bool isMuted)
    {
        // Example: If muted, drop the SFX volume to -80dB, otherwise set to 0dB
        float decibels = isMuted ? -80f : 0f;
        
        if (mainAudioMixer != null)
            mainAudioMixer.SetFloat("SFXVolume", decibels);

        PlayerPrefs.SetInt("MuteSFX", isMuted ? 1 : 0);
        PlayerPrefs.Save();
    }

    // ────────────────────────────────────────────────
    // GAMEPLAY LOGIC
    // ────────────────────────────────────────────────

    public void SetHaptics(bool useHaptics)
    {
        PlayerPrefs.SetInt("UseHaptics", useHaptics ? 1 : 0);
        PlayerPrefs.Save();

        // Optional: Play a tiny vibration so the user feels it when they toggle it ON
        if (useHaptics)
        {
            TriggerVibration();
        }
    }

    // Call this anywhere in your other scripts when a bridge breaks!
    public static void TriggerVibration()
    {
        if (PlayerPrefs.GetInt("UseHaptics", 1) == 1)
        {
            Handheld.Vibrate();
        }
    }

    // ────────────────────────────────────────────────
    // SAVE / LOAD
    // ────────────────────────────────────────────────

    private void LoadSettings()
    {
        // 1. Load Volume
        if (masterVolumeSlider != null)
        {
            float savedVol = PlayerPrefs.GetFloat("MasterVolume", 1f); // Default to 1 (Max)
            masterVolumeSlider.value = savedVol;
            SetVolume(savedVol); 
        }

        // 2. Load SFX Mute
        if (sfxToggle != null)
        {
            bool isMuted = PlayerPrefs.GetInt("MuteSFX", 0) == 1; // Default to 0 (Not Muted)
            sfxToggle.isOn = isMuted;
            SetSFXMute(isMuted);
        }

        // 3. Load Quality
        if (qualityDropdown != null)
        {
            // Default to 1 (Medium) or whatever index makes sense for your project
            int savedQuality = PlayerPrefs.GetInt("QualityLevel", 1); 
            qualityDropdown.value = savedQuality;
            SetQuality(savedQuality);
        }

        // 4. Load FPS Cap
        if (highFpsToggle != null)
        {
            bool isHighFps = PlayerPrefs.GetInt("HighFPS", 1) == 1; // Default to 60fps
            highFpsToggle.isOn = isHighFps;
            SetHighFPS(isHighFps);
        }
        else
        {
            // Fallback if there is no UI element, still apply the saved state
            Application.targetFrameRate = PlayerPrefs.GetInt("HighFPS", 1) == 1 ? 60 : 30;
        }

        // 5. Load Haptics
        if (hapticsToggle != null)
        {
            bool useHaptics = PlayerPrefs.GetInt("UseHaptics", 1) == 1; // Default to ON
            hapticsToggle.isOn = useHaptics;
            // No need to apply anything right now, other scripts will just check the PlayerPref
        }
    }
}