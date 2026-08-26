using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;
using TMPro;

public class SettingsManager : MonoBehaviour
{
    private const string AudioStartupMigrationKey = "AudioSettingsStartupFixV1";

    [Header("UI Panel Visibility")]
    public GameObject settingsPanel;

    [Header("Audio Mixers & Mute")]
    public AudioMixer mainAudioMixer;
    [Tooltip("Check this to mute everything")]
    public Toggle globalMuteToggle; 

    [Header("Audio Sliders")]
    public Slider masterVolumeSlider;
    public Slider musicVolumeSlider;
    public Slider sfxVolumeSlider;
    public Slider ambientVolumeSlider;
    public Slider uiVolumeSlider;

    [Header("Audio Percentage Texts")]
    public TMP_Text masterVolumeText;
    public TMP_Text musicVolumeText;
    public TMP_Text sfxVolumeText;
    public TMP_Text ambientVolumeText;
    public TMP_Text uiVolumeText;

    [Header("Graphics & Performance (Mobile)")]
    public TMP_Dropdown qualityDropdown;
    [Tooltip("Toggle real-time directional shadows on/off to save battery")]
    public Toggle shadowsToggle; 

    [Header("Gameplay")]
    public Toggle hapticsToggle;

    private void Start()
    {
        LoadSettings();

        // Hook up UI listeners automatically
        if (globalMuteToggle != null)
            globalMuteToggle.onValueChanged.AddListener(SetMute);

        if (masterVolumeSlider != null) 
            masterVolumeSlider.onValueChanged.AddListener((val) => ApplyVolume("MasterVolume", "PrefMaster", val, masterVolumeText));
            
        if (musicVolumeSlider != null) 
            musicVolumeSlider.onValueChanged.AddListener((val) => ApplyVolume("MusicVolume", "PrefMusic", val, musicVolumeText));
            
        if (sfxVolumeSlider != null) 
            sfxVolumeSlider.onValueChanged.AddListener((val) => ApplyVolume("SFXVolume", "PrefSFX", val, sfxVolumeText));
            
        if (ambientVolumeSlider != null) 
            ambientVolumeSlider.onValueChanged.AddListener((val) => ApplyVolume("AmbientVolume", "PrefAmbient", val, ambientVolumeText));
            
        if (uiVolumeSlider != null) 
            uiVolumeSlider.onValueChanged.AddListener((val) => ApplyVolume("UIVolume", "PrefUI", val, uiVolumeText));

        if (qualityDropdown != null) 
            qualityDropdown.onValueChanged.AddListener(SetQuality);
            
        if (shadowsToggle != null) 
            shadowsToggle.onValueChanged.AddListener(SetShadows); 
            
        if (hapticsToggle != null) 
            hapticsToggle.onValueChanged.AddListener(SetHaptics);
    }

    // ────────────────────────────────────────────────
    // UI VISIBILITY LOGIC
    // ────────────────────────────────────────────────

    public void OpenSettings()
    {
        if (settingsPanel == null) return;
        if (UIPanelCoordinator.Instance != null) UIPanelCoordinator.Instance.OpenPanel(settingsPanel);
        else settingsPanel.SetActive(true);
    }

    public void CloseSettings()
    {
        if (settingsPanel == null) return;
        if (UIPanelCoordinator.Instance != null) UIPanelCoordinator.Instance.ClosePanel(settingsPanel);
        else settingsPanel.SetActive(false);
    }

    public void ToggleSettings()
    {
        if (settingsPanel == null) return;
        if (settingsPanel.activeSelf) CloseSettings();
        else OpenSettings();
    }

    // ────────────────────────────────────────────────
    // AUDIO LOGIC
    // ────────────────────────────────────────────────

    public void SetMute(bool isMuted)
    {
        PlayerPrefs.SetInt("GlobalMute", isMuted ? 1 : 0);
        PlayerPrefs.Save();

        ApplyMuteState(isMuted);
    }

    private void ApplyMuteState(bool isMuted)
    {
        if (mainAudioMixer == null)
            return;

        if (isMuted)
        {
            mainAudioMixer.SetFloat("MasterVolume", -80f);
            return;
        }

        // Do not use an Inspector slider's current value here. In CanyonCrossing
        // and BHAN HOUSE that value is serialized as zero until settings load,
        // which previously overwrote PrefMaster and muted the game on startup.
        float savedMaster = PlayerPrefs.GetFloat("PrefMaster", 1f);
        SetMixerVolume("MasterVolume", savedMaster);
    }

    private void ApplyVolume(
        string mixerParam,
        string prefKey,
        float sliderValue,
        TMP_Text percentText,
        bool savePreference = true)
    {
        if (percentText != null)
        {
            percentText.text = Mathf.RoundToInt(sliderValue * 100f).ToString() + "%";
        }

        if (savePreference)
        {
            PlayerPrefs.SetFloat(prefKey, sliderValue);
            PlayerPrefs.Save();
        }

        if (mixerParam == "MasterVolume" && globalMuteToggle != null && globalMuteToggle.isOn)
        {
            return; 
        }

        SetMixerVolume(mixerParam, sliderValue);
    }

    private void SetMixerVolume(string mixerParam, float linearVolume)
    {
        if (mainAudioMixer == null)
            return;

        float clampedValue = Mathf.Clamp(linearVolume, 0.0001f, 1f);
        float decibels = Mathf.Log10(clampedValue) * 20f;
        mainAudioMixer.SetFloat(mixerParam, decibels);
    }

    // ────────────────────────────────────────────────
    // GRAPHICS & GAMEPLAY LOGIC
    // ────────────────────────────────────────────────

    public void SetQuality(int qualityIndex)
    {
        QualitySettings.SetQualityLevel(qualityIndex);
        PlayerPrefs.SetInt("QualityLevel", qualityIndex);
        PlayerPrefs.Save();
    }

    // --- THE FIX: URP Compatible Shadow Toggle ---
    public void SetShadows(bool enableShadows)
    {
        // 1. Apply to the legacy pipeline just in case
        QualitySettings.shadows = enableShadows ? ShadowQuality.All : ShadowQuality.Disable;

        // 2. The URP Fix: Target the actual Directional Light (The Sun) in the scene
        if (RenderSettings.sun != null)
        {
            RenderSettings.sun.shadows = enableShadows ? LightShadows.Soft : LightShadows.None;
        }
        else
        {
            // Fallback: If RenderSettings.sun isn't assigned, find all Directional Lights manually
            Light[] allLights = FindObjectsOfType<Light>();
            foreach (Light light in allLights)
            {
                if (light.type == LightType.Directional)
                {
                    light.shadows = enableShadows ? LightShadows.Soft : LightShadows.None;
                }
            }
        }

        PlayerPrefs.SetInt("EnableShadows", enableShadows ? 1 : 0);
        PlayerPrefs.Save();
    }

    public void SetHaptics(bool useHaptics)
    {
        PlayerPrefs.SetInt("UseHaptics", useHaptics ? 1 : 0);
        PlayerPrefs.Save();
        if (useHaptics) TriggerVibration();
    }

    public static void TriggerVibration()
    {
        if (PlayerPrefs.GetInt("UseHaptics", 1) == 1) Handheld.Vibrate();
    }

    // ────────────────────────────────────────────────
    // SAVE / LOAD
    // ────────────────────────────────────────────────

    private void LoadSettings()
    {
        RepairMasterVolumeCorruptedByOldStartupOrder();

        // Load every mixer value before applying mute. This works even in scenes
        // such as Main Menu where the optional slider references are not assigned.
        LoadSlider(masterVolumeSlider, masterVolumeText, "MasterVolume", "PrefMaster", 1f);
        LoadSlider(musicVolumeSlider, musicVolumeText, "MusicVolume", "PrefMusic", 1f);
        LoadSlider(sfxVolumeSlider, sfxVolumeText, "SFXVolume", "PrefSFX", 1f);
        LoadSlider(ambientVolumeSlider, ambientVolumeText, "AmbientVolume", "PrefAmbient", 1f);
        LoadSlider(uiVolumeSlider, uiVolumeText, "UIVolume", "PrefUI", 1f);

        bool isMuted = PlayerPrefs.GetInt("GlobalMute", 0) == 1;
        if (globalMuteToggle != null)
            globalMuteToggle.SetIsOnWithoutNotify(isMuted);
        ApplyMuteState(isMuted);

        // 2. Load Graphics & Gameplay
        if (qualityDropdown != null)
        {
            int savedQuality = PlayerPrefs.GetInt("QualityLevel", 1); 
            qualityDropdown.value = savedQuality;
            SetQuality(savedQuality);
        }

        if (shadowsToggle != null)
        {
            bool shadowsEnabled = PlayerPrefs.GetInt("EnableShadows", 1) == 1; // Default to ON
            shadowsToggle.isOn = shadowsEnabled;
            SetShadows(shadowsEnabled);
        }

        if (hapticsToggle != null)
        {
            hapticsToggle.isOn = PlayerPrefs.GetInt("UseHaptics", 1) == 1; 
        }
    }

    private void LoadSlider(Slider slider, TMP_Text text, string mixerParam, string prefKey, float defaultVal)
    {
        float savedVol = PlayerPrefs.GetFloat(prefKey, defaultVal);
        if (slider != null)
            slider.SetValueWithoutNotify(savedVol);

        ApplyVolume(mixerParam, prefKey, savedVol, text, false);
    }

    private static void RepairMasterVolumeCorruptedByOldStartupOrder()
    {
        if (PlayerPrefs.GetInt(AudioStartupMigrationKey, 0) == 1)
            return;

        // The old startup order copied the scene slider's serialized zero into
        // PrefMaster whenever the game was not globally muted. Repair that value
        // once, while preserving an intentional Global Mute setting.
        if (PlayerPrefs.GetInt("GlobalMute", 0) == 0 &&
            PlayerPrefs.GetFloat("PrefMaster", 1f) <= 0.0001f)
        {
            PlayerPrefs.SetFloat("PrefMaster", 1f);
        }

        PlayerPrefs.SetInt(AudioStartupMigrationKey, 1);
        PlayerPrefs.Save();
    }
}
