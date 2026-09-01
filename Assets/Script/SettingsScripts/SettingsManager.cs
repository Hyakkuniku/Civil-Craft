using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;
using TMPro;

public class SettingsManager : MonoBehaviour
{
    private const string AudioStartupMigrationKey = "AudioSettingsStartupFixV1";
    private static readonly int[] FrameRateCaps = { 30, 60, 90, 120 };

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
    public TMP_Dropdown frameRateDropdown;

    [Header("Gameplay")]
    public Toggle hapticsToggle;
    [Range(0.5f, 2f)] public float defaultCameraSensitivity = 1f;
    public Slider cameraSensitivitySlider;
    public TMP_Text cameraSensitivityText;
    public Toggle invertLookYToggle;

    [Header("Account")]
    public PlayFabAuthManager authManager;
    public TMP_Text accountStatusText;
    public TMP_Text accountActionButtonText;
    public Button accountActionButton;
    public Button logoutButton;

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

        if (frameRateDropdown != null)
            frameRateDropdown.onValueChanged.AddListener(SetFrameRateOption);

        if (hapticsToggle != null) 
            hapticsToggle.onValueChanged.AddListener(SetHaptics);

        if (cameraSensitivitySlider != null)
            cameraSensitivitySlider.onValueChanged.AddListener(SetCameraSensitivity);

        if (invertLookYToggle != null)
            invertLookYToggle.onValueChanged.AddListener(SetInvertLookY);

        if (accountActionButton != null)
            accountActionButton.onClick.AddListener(OpenAccountLogin);

        if (logoutButton != null)
            logoutButton.onClick.AddListener(LogoutAccount);

        RefreshAccountUI();
    }

    // ────────────────────────────────────────────────
    // UI VISIBILITY LOGIC
    // ────────────────────────────────────────────────

    public void OpenSettings()
    {
        if (settingsPanel == null) return;
        LoadSettings();
        RefreshAccountUI();
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
        int clampedIndex = Mathf.Clamp(qualityIndex, 0, Mathf.Max(0, QualitySettings.names.Length - 1));
        QualitySettings.SetQualityLevel(clampedIndex);
        PlayerPrefs.SetInt("QualityLevel", clampedIndex);
        PlayerPrefs.Save();
    }

    public void SetFrameRateOption(int optionIndex)
    {
        int clampedIndex = Mathf.Clamp(optionIndex, 0, FrameRateCaps.Length - 1);
        int cap = FrameRateCaps[clampedIndex];
        PlayerPrefs.SetInt("FrameRateOption", clampedIndex);
        PlayerPrefs.SetInt("FrameRateCap", cap);
        PlayerPrefs.Save();
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = cap;
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

    public void SetCameraSensitivity(float multiplier)
    {
        float clamped = Mathf.Clamp(multiplier, 0.5f, 2f);
        PlayerPrefs.SetFloat("CameraSensitivity", clamped);
        PlayerPrefs.Save();
        if (cameraSensitivityText != null)
            cameraSensitivityText.text = $"{clamped:0.0}x";

        foreach (PlayerLook playerLook in FindObjectsOfType<PlayerLook>(true))
            playerLook.ApplySavedCameraSettings();
    }

    public void SetInvertLookY(bool invert)
    {
        PlayerPrefs.SetInt("InvertLookY", invert ? 1 : 0);
        PlayerPrefs.Save();
        foreach (PlayerLook playerLook in FindObjectsOfType<PlayerLook>(true))
            playerLook.ApplySavedCameraSettings();
    }

    public void ApplyAndClose()
    {
        PlayerPrefs.Save();
        CloseSettings();
    }

    public void RestoreCozyDefaults()
    {
        PlayerPrefs.SetInt("GlobalMute", 0);
        PlayerPrefs.SetFloat("PrefMaster", 1f);
        PlayerPrefs.SetFloat("PrefMusic", 0.85f);
        PlayerPrefs.SetFloat("PrefSFX", 0.9f);
        PlayerPrefs.SetFloat("PrefAmbient", 0.85f);
        PlayerPrefs.SetFloat("PrefUI", 0.9f);
        PlayerPrefs.SetInt("QualityLevel", Mathf.Clamp(2, 0, Mathf.Max(0, QualitySettings.names.Length - 1)));
        PlayerPrefs.SetInt("EnableShadows", 1);
        PlayerPrefs.SetInt("FrameRateOption", 1);
        PlayerPrefs.SetInt("FrameRateCap", 60);
        PlayerPrefs.SetInt("UseHaptics", 1);
        PlayerPrefs.SetFloat("CameraSensitivity", defaultCameraSensitivity);
        PlayerPrefs.SetInt("InvertLookY", 0);
        PlayerPrefs.Save();
        LoadSettings();
    }

    public void OpenAccountLogin()
    {
        if (authManager == null) authManager = FindObjectOfType<PlayFabAuthManager>(true);
        if (authManager == null)
        {
            Debug.LogError("SettingsManager: PlayFabAuthManager is not available.", this);
            return;
        }

        CloseSettings();
        authManager.OpenAuthCanvasForMainMenu();
    }

    public void LogoutAccount()
    {
        if (authManager == null) authManager = FindObjectOfType<PlayFabAuthManager>(true);
        if (authManager != null) authManager.LogoutToSignedOutState();
        RefreshAccountUI();
    }

    public void RefreshAccountUI()
    {
        if (authManager == null) authManager = FindObjectOfType<PlayFabAuthManager>(true);

        int loginChoice = PlayerPrefs.GetInt("LoginChoice", 0);
        string savedName = PlayerPrefs.GetString("SavedPlayerName", "Player");
        bool accountControlsAvailable = authManager != null;
        bool signedIn = accountControlsAvailable
            ? authManager.IsPlayerLoggedIn
            : loginChoice == 2;
        bool guest = loginChoice == 1;

        if (accountStatusText != null)
        {
            if (signedIn)
                accountStatusText.text = $"Signed in as <b>{savedName}</b>\nOnline account and cloud-ready progression.";
            else if (guest)
                accountStatusText.text = "Playing as <b>Guest</b>\nProgress is stored locally on this device.";
            else
                accountStatusText.text = "Not signed in\nSign in to use your PlayFab account.";
        }

        if (accountActionButtonText != null)
            accountActionButtonText.text = accountControlsAvailable
                ? (signedIn ? "SWITCH ACCOUNT" : "SIGN IN")
                : "MANAGE IN MAIN MENU";
        if (accountActionButton != null)
            accountActionButton.interactable = accountControlsAvailable;
        if (logoutButton != null)
            logoutButton.interactable = accountControlsAvailable && (signedIn || guest);
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
            int savedQuality = Mathf.Clamp(
                PlayerPrefs.GetInt("QualityLevel", Mathf.Clamp(2, 0, Mathf.Max(0, QualitySettings.names.Length - 1))),
                0,
                Mathf.Max(0, QualitySettings.names.Length - 1));
            qualityDropdown.SetValueWithoutNotify(savedQuality);
            qualityDropdown.RefreshShownValue();
            SetQuality(savedQuality);
        }

        if (shadowsToggle != null)
        {
            bool shadowsEnabled = PlayerPrefs.GetInt("EnableShadows", 1) == 1; // Default to ON
            shadowsToggle.SetIsOnWithoutNotify(shadowsEnabled);
            SetShadows(shadowsEnabled);
        }

        int frameRateOption = Mathf.Clamp(PlayerPrefs.GetInt("FrameRateOption", 1), 0, FrameRateCaps.Length - 1);
        if (frameRateDropdown != null)
        {
            frameRateDropdown.SetValueWithoutNotify(frameRateOption);
            frameRateDropdown.RefreshShownValue();
        }
        SetFrameRateOption(frameRateOption);

        if (hapticsToggle != null)
        {
            hapticsToggle.SetIsOnWithoutNotify(PlayerPrefs.GetInt("UseHaptics", 1) == 1);
        }

        float cameraSensitivity = PlayerPrefs.GetFloat("CameraSensitivity", defaultCameraSensitivity);
        if (cameraSensitivitySlider != null)
            cameraSensitivitySlider.SetValueWithoutNotify(cameraSensitivity);
        SetCameraSensitivity(cameraSensitivity);

        bool invertY = PlayerPrefs.GetInt("InvertLookY", 0) == 1;
        if (invertLookYToggle != null)
            invertLookYToggle.SetIsOnWithoutNotify(invertY);
        SetInvertLookY(invertY);

        RefreshAccountUI();
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
