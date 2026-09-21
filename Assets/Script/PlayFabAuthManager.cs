using UnityEngine;
using UnityEngine.UI; 
using TMPro; 
using PlayFab;
using PlayFab.ClientModels;
using UnityEngine.SceneManagement; 
using UnityEngine.EventSystems;
using System.Text.RegularExpressions; 
using System;

public class PlayFabAuthManager : MonoBehaviour
{
    private const string RememberedPlayFabIdKey = "RememberedPlayFabId";
    private const string DeviceLinkedPlayFabIdKey = "DeviceLinkedPlayFabId";

    public static PlayFabAuthManager Instance { get; private set; }
    public event Action MainMenuAuthenticationSucceeded;
    public event Action<bool> AutomaticLoginCompleted;
    public bool IsPlayerLoggedIn => PlayFabClientAPI.IsClientLoggedIn();
    public bool IsAutomaticLoginInProgress { get; private set; }
    public bool IsGuestSelected => PlayerPrefs.GetInt("LoginChoice", 0) == 1;
    public bool IsCloudSaveReady { get; private set; }

    private bool returnToMainMenuAfterLogin;
    private bool playAfterAutomaticLogin;
    private bool manualLoginInProgress;
    private int authenticationGeneration;
    private GameObject saveChoiceOverlay;
    private GameObject saveChoiceEventSystem;
    private Action cancelSaveChoice;

    [Header("PlayFab Configuration")]
    public string playFabTitleID = ""; 

    [Header("Player Data")]
    public string loggedInPlayerName = ""; 
    public bool isGuest = false; 

    [Header("Scene Management")]
    public string sceneToLoad = "GameScene"; 

    [Header("UI Panels & Text")]
    public GameObject authCanvas;       
    public GameObject loginPanel;
    public GameObject registerPanel;
    public GameObject forgotPasswordPanel;
    
    [Tooltip("Text on the main menu to show 'Playing as: Name'")]
    public TextMeshProUGUI playerNameDisplay; 

    [Header("Login Inputs")]
    [Tooltip("Players can enter either their Username OR their Email here.")]
    public TMP_InputField loginUsername;
    public TMP_InputField loginPassword;

    [Header("Register Inputs")]
    public TMP_InputField registerUsername;
    public TMP_InputField registerEmail;
    public TMP_InputField registerPassword;
    public TMP_InputField registerConfirmPassword; 

    [Header("Forgot Password Inputs")]
    public TMP_InputField resetEmailInput; 
    public Button forgotPasswordSubmitButton;      
    public TextMeshProUGUI forgotPasswordButtonText; 

    private float forgotPasswordCooldown = 0f;     
    
    [Header("Password Visibility Icons")]
    public Image loginPasswordToggleImage;
    public Image registerPasswordToggleImage;
    public Sprite showPasswordSprite; 
    public Sprite hidePasswordSprite; 

    [Header("Feedback UI")]
    public TextMeshProUGUI feedbackText; 
    public Color errorColor = Color.red;
    public Color successColor = Color.green;
    public Color processColor = Color.yellow;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else
        {
            Destroy(gameObject);
            return;
        }

        if (!string.IsNullOrEmpty(playFabTitleID))
        {
            PlayFabSettings.staticSettings.TitleId = playFabTitleID;
        }

        if (authCanvas != null) authCanvas.SetActive(false);
    }

    private void Start()
    {
        CloudSaveManager cloud = PlayerDataManager.Instance != null
            ? PlayerDataManager.Instance.GetComponent<CloudSaveManager>() : null;
        IsCloudSaveReady = PlayFabClientAPI.IsClientLoggedIn() &&
            cloud != null && cloud.IsAccountActive;
        UpdatePlayerNameDisplay();
        TryRestoreLogin();
    }

    private static bool TryGetDeviceId(out string deviceId)
    {
        deviceId = string.Empty;
        if (Application.platform != RuntimePlatform.Android &&
            Application.platform != RuntimePlatform.IPhonePlayer)
            return false;

        deviceId = SystemInfo.deviceUniqueIdentifier;
        return !string.IsNullOrWhiteSpace(deviceId) &&
               deviceId != SystemInfo.unsupportedIdentifier;
    }

    private void TryRestoreLogin()
    {
        if (PlayerPrefs.GetInt("LoginChoice", 0) != 2 ||
            PlayFabClientAPI.IsClientLoggedIn() ||
            !TryGetDeviceId(out string deviceId)) return;

        string expectedPlayFabId = PlayerPrefs.GetString(RememberedPlayFabIdKey, string.Empty);
        if (string.IsNullOrEmpty(expectedPlayFabId)) return;

        IsAutomaticLoginInProgress = true;
        int generation = ++authenticationGeneration;
        var info = new GetPlayerCombinedInfoRequestParams { GetPlayerProfile = true };

        if (Application.platform == RuntimePlatform.Android)
        {
            PlayFabClientAPI.LoginWithAndroidDeviceID(new LoginWithAndroidDeviceIDRequest
            {
                AndroidDeviceId = deviceId,
                CreateAccount = false,
                InfoRequestParameters = info
            }, result => OnAutomaticLoginSuccess(result, expectedPlayFabId, generation),
               error => OnAutomaticLoginError(error, generation));
        }
        else
        {
            PlayFabClientAPI.LoginWithIOSDeviceID(new LoginWithIOSDeviceIDRequest
            {
                DeviceId = deviceId,
                CreateAccount = false,
                InfoRequestParameters = info
            }, result => OnAutomaticLoginSuccess(result, expectedPlayFabId, generation),
               error => OnAutomaticLoginError(error, generation));
        }
    }

    private void OnAutomaticLoginSuccess(LoginResult result, string expectedPlayFabId, int generation)
    {
        if (generation != authenticationGeneration)
        {
            PlayFabClientAPI.ForgetAllCredentials();
            return;
        }

        if (!string.Equals(result.PlayFabId, expectedPlayFabId, StringComparison.Ordinal))
        {
            // A device may have been re-linked outside this installation. Never
            // accept a different account merely because its device login worked.
            PlayFabClientAPI.ForgetAllCredentials();
            PlayerPrefs.DeleteKey(RememberedPlayFabIdKey);
            PlayerPrefs.Save();
            Debug.LogWarning("Automatic login returned a different PlayFab account. Please sign in manually.", this);
            IsAutomaticLoginInProgress = false;
            FinishAutomaticLogin(false);
            return;
        }

        isGuest = false;
        string displayName = result.InfoResultPayload?.PlayerProfile?.DisplayName;
        loggedInPlayerName = string.IsNullOrWhiteSpace(displayName)
            ? PlayerPrefs.GetString("SavedPlayerName", "Player") : displayName;
        PlayerPrefs.SetString("SavedPlayerName", loggedInPlayerName);
        PlayerPrefs.Save();
        UpdatePlayerNameDisplay();
        BeginCloudSaveLogin(result, CloudSaveManager.StartMode.Resume, generation, succeeded =>
        {
            if (generation != authenticationGeneration) return;
            IsAutomaticLoginInProgress = false;
            if (!succeeded) PlayFabClientAPI.ForgetAllCredentials();
            FinishAutomaticLogin(succeeded);
        });
    }

    private void OnAutomaticLoginError(PlayFabError error, int generation)
    {
        if (generation != authenticationGeneration) return;
        IsAutomaticLoginInProgress = false;
        Debug.LogWarning("Automatic login failed; manual sign-in is available. " + error.ErrorMessage, this);
        FinishAutomaticLogin(false);
    }

    private void FinishAutomaticLogin(bool succeeded)
    {
        AutomaticLoginCompleted?.Invoke(succeeded);
        if (!playAfterAutomaticLogin) return;
        playAfterAutomaticLogin = false;
        if (succeeded) LoadGameScene();
        else OpenAuthCanvas();
    }

    private void Update()
    {
        if (saveChoiceOverlay != null && Input.GetKeyDown(KeyCode.Escape))
        {
            cancelSaveChoice?.Invoke();
            return;
        }
        if (forgotPasswordCooldown > 0f)
        {
            forgotPasswordCooldown -= Time.deltaTime;
            
            if (forgotPasswordSubmitButton != null) forgotPasswordSubmitButton.interactable = false;
            if (forgotPasswordButtonText != null) forgotPasswordButtonText.text = $"Wait {Mathf.CeilToInt(forgotPasswordCooldown)}s";
        }
        else if (forgotPasswordCooldown < 0f) 
        {
            forgotPasswordCooldown = 0f;
            
            if (forgotPasswordSubmitButton != null) forgotPasswordSubmitButton.interactable = true;
            if (forgotPasswordButtonText != null) forgotPasswordButtonText.text = "Send Email";
        }
    }

    // ────────────────────────────────────────────────
    // NAME DISPLAY LOGIC
    // ────────────────────────────────────────────────

    private void UpdatePlayerNameDisplay()
    {
        if (playerNameDisplay == null) return;

        int choice = PlayerPrefs.GetInt("LoginChoice", 0);

        if (choice == 0)
        {
            playerNameDisplay.gameObject.SetActive(false);
        }
        else
        {
            playerNameDisplay.gameObject.SetActive(true);
            string savedName = PlayerPrefs.GetString("SavedPlayerName", "Player");
            playerNameDisplay.text = choice == 2 && !PlayFabClientAPI.IsClientLoggedIn()
                ? "Last played as: " + savedName
                : "Playing as: " + savedName;
        }
    }

    // ────────────────────────────────────────────────
    // MAIN PLAY BUTTON LOGIC
    // ────────────────────────────────────────────────

    public void OnMainPlayButtonClicked()
    {
        int choice = PlayerPrefs.GetInt("LoginChoice", 0); 

        if (choice == 0)
        {
            OpenAuthCanvas();
        }
        else if (choice == 1)
        {
            isGuest = true;
            LoadGameScene();
        }
        else if (choice == 2)
        {
            if (IsAutomaticLoginInProgress)
            {
                playAfterAutomaticLogin = true;
                return;
            }
            if (PlayFabClientAPI.IsClientLoggedIn())
            {
                if (IsCloudSaveReady) LoadGameScene();
                else SetFeedbackMessage("Checking online save. Please wait...", processColor);
            }
            else
            {
                OpenAuthCanvas();
            }
        }
    }

    private void LoadGameScene()
    {
        Debug.Log("Loading Scene: " + sceneToLoad);
        LoadingScreenManager.LoadScene(sceneToLoad);
    }

    // ────────────────────────────────────────────────
    // UI NAVIGATION & TOGGLES
    // ────────────────────────────────────────────────

    public void OpenAuthCanvas()
    {
        if (authCanvas == null)
        {
            Debug.LogError("PlayFabAuthManager: AuthCanvas is not assigned.", this);
            return;
        }

        authCanvas.SetActive(true);
        ShowLoginPanel(); 
    }

    public void OpenAuthCanvasForMainMenu()
    {
        returnToMainMenuAfterLogin = true;
        OpenAuthCanvas();
    }

    public void CloseAuthCanvas()
    {
        if (manualLoginInProgress)
        {
            authenticationGeneration++;
            FailPendingLogin("Account switch canceled. Your guest save is unchanged.");
        }
        CloseSaveChoice();
        returnToMainMenuAfterLogin = false;
        if (loginPanel != null) loginPanel.SetActive(true);
        if (registerPanel != null) registerPanel.SetActive(false);
        if (forgotPasswordPanel != null) forgotPasswordPanel.SetActive(false);
        if (authCanvas != null) authCanvas.SetActive(false);
    }

    /// <summary>Clears the current PlayFab/guest session without deleting game progress.</summary>
    public void LogoutToSignedOutState()
    {
        CloseSaveChoice();
        CancelInvoke(nameof(LoadGameScene));
        authenticationGeneration++;
        IsAutomaticLoginInProgress = false;
        playAfterAutomaticLogin = false;
        manualLoginInProgress = false;
        IsCloudSaveReady = false;
        CloudSaveManager cloud = PlayerDataManager.Instance != null
            ? PlayerDataManager.Instance.GetComponent<CloudSaveManager>() : null;
        if (cloud != null) cloud.EndSession();
        if (PlayerDataManager.Instance != null) PlayerDataManager.Instance.UseGuestSave();
        PlayFabClientAPI.ForgetAllCredentials();
        returnToMainMenuAfterLogin = false;
        isGuest = false;
        loggedInPlayerName = string.Empty;

        PlayerPrefs.SetInt("LoginChoice", 0);
        PlayerPrefs.DeleteKey("SavedPlayerName");
        PlayerPrefs.DeleteKey(RememberedPlayFabIdKey);
        PlayerPrefs.Save();

        if (authCanvas != null) authCanvas.SetActive(false);
        UpdatePlayerNameDisplay();
    }

    public void OnPlayAsGuestClicked()
    {
        CloseSaveChoice();
        authenticationGeneration++;
        IsAutomaticLoginInProgress = false;
        playAfterAutomaticLogin = false;
        manualLoginInProgress = false;
        IsCloudSaveReady = false;
        CloudSaveManager cloud = PlayerDataManager.Instance != null
            ? PlayerDataManager.Instance.GetComponent<CloudSaveManager>() : null;
        if (cloud != null) cloud.EndSession();
        if (PlayerDataManager.Instance != null) PlayerDataManager.Instance.UseGuestSave();
        PlayFabClientAPI.ForgetAllCredentials();
        isGuest = true;
        loggedInPlayerName = "Guest";
        
        PlayerPrefs.SetInt("LoginChoice", 1); 
        PlayerPrefs.SetString("SavedPlayerName", "Guest"); 
        PlayerPrefs.DeleteKey(RememberedPlayFabIdKey);
        PlayerPrefs.Save();

        UpdatePlayerNameDisplay(); 

        SetFeedbackMessage("Starting as Guest...", successColor);
        if (returnToMainMenuAfterLogin)
        {
            CompleteMainMenuAuthenticationRequest();
        }
        else
        {
            Invoke(nameof(LoadGameScene), 1.0f);
        }
    }

    public void ShowLoginPanel()
    {
        loginPanel.SetActive(true);
        registerPanel.SetActive(false);
        if (forgotPasswordPanel != null) forgotPasswordPanel.SetActive(false);
        SetFeedbackMessage("", Color.white);
    }

    public void ShowRegisterPanel()
    {
        loginPanel.SetActive(false);
        registerPanel.SetActive(true);
        if (forgotPasswordPanel != null) forgotPasswordPanel.SetActive(false);
        SetFeedbackMessage("", Color.white);
    }

    public void ShowForgotPasswordPanel()
    {
        loginPanel.SetActive(false);
        registerPanel.SetActive(false);
        if (forgotPasswordPanel != null) forgotPasswordPanel.SetActive(true);
        SetFeedbackMessage("Enter your email to reset your password.", Color.white);
    }

    private void SetFeedbackMessage(string message, Color color)
    {
        if (feedbackText != null)
        {
            feedbackText.text = message;
            feedbackText.color = color;
        }
    }

    public void ToggleLoginPasswordVisibility()
    {
        if (loginPassword.contentType == TMP_InputField.ContentType.Password)
        {
            loginPassword.contentType = TMP_InputField.ContentType.Standard;
            if (loginPasswordToggleImage != null && hidePasswordSprite != null)
                loginPasswordToggleImage.sprite = hidePasswordSprite;
        }
        else
        {
            loginPassword.contentType = TMP_InputField.ContentType.Password;
            if (loginPasswordToggleImage != null && showPasswordSprite != null)
                loginPasswordToggleImage.sprite = showPasswordSprite;
        }

        loginPassword.ForceLabelUpdate();
    }

    public void ToggleRegisterPasswordVisibility()
    {
        if (registerPassword.contentType == TMP_InputField.ContentType.Password)
        {
            registerPassword.contentType = TMP_InputField.ContentType.Standard;
            if (registerConfirmPassword != null) registerConfirmPassword.contentType = TMP_InputField.ContentType.Standard; 
            if (registerPasswordToggleImage != null && hidePasswordSprite != null)
                registerPasswordToggleImage.sprite = hidePasswordSprite;
        }
        else
        {
            registerPassword.contentType = TMP_InputField.ContentType.Password;
            if (registerConfirmPassword != null) registerConfirmPassword.contentType = TMP_InputField.ContentType.Password; 
            if (registerPasswordToggleImage != null && showPasswordSprite != null)
                registerPasswordToggleImage.sprite = showPasswordSprite;
        }

        registerPassword.ForceLabelUpdate();
        if (registerConfirmPassword != null) registerConfirmPassword.ForceLabelUpdate();
    }

    // ────────────────────────────────────────────────
    // PLAYFAB API CALLS
    // ────────────────────────────────────────────────

    public void OnLoginButtonClicked()
    {
        if (manualLoginInProgress) return;
        if (IsAutomaticLoginInProgress)
        {
            SetFeedbackMessage("Finishing automatic sign-in. Please wait...", processColor);
            return;
        }

        string inputId = loginUsername.text.Trim(); 

        if (string.IsNullOrEmpty(inputId) || string.IsNullOrEmpty(loginPassword.text))
        {
            SetFeedbackMessage("Please enter Username/Email and Password.", errorColor);
            return;
        }

        SetFeedbackMessage("Logging in...", processColor);

        var infoParams = new GetPlayerCombinedInfoRequestParams
        {
            GetPlayerProfile = true,
            GetUserAccountInfo = true, 
            ProfileConstraints = new PlayerProfileViewConstraints
            {
                ShowContactEmailAddresses = true,
                ShowDisplayName = true
            }
        };

        bool isEmailLogin = inputId.Contains("@");
        manualLoginInProgress = true;
        int generation = ++authenticationGeneration;

        if (isEmailLogin)
        {
            var request = new LoginWithEmailAddressRequest
            {
                Email = inputId,
                Password = loginPassword.text,
                InfoRequestParameters = infoParams
            };
            PlayFabClientAPI.LoginWithEmailAddress(request,
                result => OnLoginSuccess(result, generation),
                error => OnLoginError(error, generation));
        }
        else
        {
            var request = new LoginWithPlayFabRequest
            {
                Username = inputId,
                Password = loginPassword.text,
                InfoRequestParameters = infoParams
            };
            PlayFabClientAPI.LoginWithPlayFab(request,
                result => OnLoginSuccess(result, generation),
                error => OnLoginError(error, generation));
        }
    }

    public void OnRegisterButtonClicked()
    {
        string trimmedUser = registerUsername.text.Trim();
        string trimmedEmail = registerEmail.text.Trim();

        if (string.IsNullOrEmpty(trimmedUser) || string.IsNullOrEmpty(trimmedEmail) || 
            string.IsNullOrEmpty(registerPassword.text) || (registerConfirmPassword != null && string.IsNullOrEmpty(registerConfirmPassword.text)))
        {
            SetFeedbackMessage("Please fill out all fields.", errorColor);
            return;
        }

        if (registerConfirmPassword != null && registerPassword.text != registerConfirmPassword.text)
        {
            SetFeedbackMessage("Passwords do not match.", errorColor);
            return;
        }

        string passwordRegex = @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).{8,}$";
        if (!Regex.IsMatch(registerPassword.text, passwordRegex))
        {
            SetFeedbackMessage("Password must be at least 8 characters, include an uppercase letter, a lowercase letter, and a number.", errorColor);
            return;
        }

        SetFeedbackMessage("Registering account...", processColor);

        var request = new RegisterPlayFabUserRequest
        {
            Username = trimmedUser,
            Email = trimmedEmail,
            Password = registerPassword.text,
            RequireBothUsernameAndEmail = true 
        };

        PlayFabClientAPI.RegisterPlayFabUser(request, OnRegisterSuccess, OnRegisterError);
    }

    public void OnForgotPasswordButtonClicked()
    {
        string resetEmail = resetEmailInput.text.Trim();

        if (string.IsNullOrEmpty(resetEmail))
        {
            SetFeedbackMessage("Please enter your email address.", errorColor);
            return;
        }

        string emailRegex = @"^[^@\s]+@[^@\s]+\.[^@\s]+$";
        if (!Regex.IsMatch(resetEmail, emailRegex))
        {
            SetFeedbackMessage("Please enter a valid email address.", errorColor);
            return;
        }

        SetFeedbackMessage("Sending recovery email...", processColor);

        forgotPasswordCooldown = 60f;

        var request = new SendAccountRecoveryEmailRequest
        {
            Email = resetEmail,
            TitleId = PlayFabSettings.staticSettings.TitleId 
        };

        PlayFabClientAPI.SendAccountRecoveryEmail(request, OnPasswordResetSuccess, OnPasswordResetError);
    }

    public void SubmitNewDisplayName(string newName)
    {
        var request = new UpdateUserTitleDisplayNameRequest { DisplayName = newName.Trim() };
        PlayFabClientAPI.UpdateUserTitleDisplayName(request, 
            result => {
                loggedInPlayerName = result.DisplayName;
                PlayerPrefs.SetString("SavedPlayerName", result.DisplayName); 
                UpdatePlayerNameDisplay();
                Debug.Log("Display Name successfully changed to: " + result.DisplayName);
            }, 
            error => Debug.LogError("Failed to change name: " + error.ErrorMessage));
    }

    // ────────────────────────────────────────────────
    // PLAYFAB SUCCESS/ERROR CALLBACKS
    // ────────────────────────────────────────────────

    private void OnLoginSuccess(LoginResult result, int generation)
    {
        if (generation != authenticationGeneration) return;
        // A successful account switch replaces the PlayFab credentials. Stop
        // the previous account's pending sync before any further API calls.
        CloudSaveManager previousCloud = PlayerDataManager.Instance != null
            ? PlayerDataManager.Instance.GetComponent<CloudSaveManager>() : null;
        if (previousCloud != null) previousCloud.EndSession();
        IsCloudSaveReady = false;
        loginPassword.text = string.Empty;
        string inputId = loginUsername.text.Trim();
        bool isEmailLogin = inputId.Contains("@");

        if (!isEmailLogin && result.InfoResultPayload != null && result.InfoResultPayload.AccountInfo != null)
        {
            if (result.InfoResultPayload.AccountInfo.Username != inputId)
            {
                SetFeedbackMessage("Login Failed: Username is case-sensitive. Please check your capitalization.", errorColor);
                manualLoginInProgress = false;
                PlayFabClientAPI.ForgetAllCredentials(); 
                return;
            }
        }

        // Inspect saves before linking this device: canceling the choice must
        // leave both the guest slot and the remembered account untouched.
        loggedInPlayerName = string.Empty;
        if (result.InfoResultPayload != null && result.InfoResultPayload.PlayerProfile != null)
        {
            loggedInPlayerName = result.InfoResultPayload.PlayerProfile.DisplayName;
        }

        CompleteManualLogin(result, generation);
    }

    private void CompleteManualLogin(LoginResult result, int generation)
    {
        if (generation != authenticationGeneration) return;
        CloudSaveManager cloud = PlayerDataManager.Instance != null
            ? PlayerDataManager.Instance.GetComponent<CloudSaveManager>() : null;
        if (cloud == null)
        {
            FailPendingLogin("Account save is unavailable. Please try again.");
            return;
        }

        SetFeedbackMessage("Checking account save...", processColor);
        cloud.InspectAccount(result, availability =>
        {
            if (generation != authenticationGeneration) return;
            switch (availability)
            {
                case CloudSaveManager.SaveAvailability.Empty:
                    if (!ShowSaveChoice(
                            "This account has no save.\nStart fresh or use your guest save?",
                            "Start Fresh", "Use Guest Save",
                            () => StartSelectedLogin(result, generation,
                                CloudSaveManager.StartMode.StartFresh),
                            () => StartSelectedLogin(result, generation,
                                CloudSaveManager.StartMode.UseGuestSave)))
                        FailPendingLogin("Could not show the save choice. Please try again.");
                    break;

                case CloudSaveManager.SaveAvailability.Online:
                case CloudSaveManager.SaveAvailability.LocalOnly:
                    bool online = availability == CloudSaveManager.SaveAvailability.Online;
                    string message = online
                        ? "This account has saved progress.\nSwitch to it? Your guest save is kept."
                        : "This account has a save on this device.\nSwitch to it? Your guest save is kept.";
                    if (!ShowSaveChoice(message, "Stay Guest", "Switch Account",
                            CancelPendingLogin,
                            () => StartSelectedLogin(result, generation,
                                online ? CloudSaveManager.StartMode.LoadOnline :
                                    CloudSaveManager.StartMode.LoadLocalAccount)))
                        FailPendingLogin("Could not show the account choice. Please try again.");
                    break;

                default:
                    FailPendingLogin("Could not check this account's save. Your guest save is unchanged.");
                    break;
            }
        });
    }

    private void StartSelectedLogin(LoginResult result, int generation,
        CloudSaveManager.StartMode mode)
    {
        if (generation != authenticationGeneration) return;
        CloseSaveChoice();
        SetFeedbackMessage("Opening account save...", processColor);
        BeginCloudSaveLogin(result, mode, generation, succeeded =>
        {
            if (generation != authenticationGeneration) return;
            if (!succeeded)
            {
                FailPendingLogin("Could not open this account's save. Your guest save is unchanged.");
                return;
            }

            SetFeedbackMessage("Finishing sign-in...", processColor);
            LinkDeviceAfterSaveChoice(result, generation,
                deviceLinked => FinishSelectedLogin(result, deviceLinked, generation, mode));
        });
    }

    private void LinkDeviceAfterSaveChoice(LoginResult result, int generation,
        Action<bool> onComplete)
    {
        if (!TryGetDeviceId(out string deviceId))
        {
            onComplete(false);
            return;
        }

        bool forceLink = !string.IsNullOrEmpty(PlayerPrefs.GetString(DeviceLinkedPlayFabIdKey, string.Empty)) &&
            !string.Equals(PlayerPrefs.GetString(DeviceLinkedPlayFabIdKey, string.Empty),
                result.PlayFabId, StringComparison.Ordinal);
        Action<PlayFabError> onError = error =>
        {
            if (generation != authenticationGeneration) return;
            Debug.LogWarning("Device sign-in could not be enabled: " + error.ErrorMessage, this);
            onComplete(false);
        };
        if (Application.platform == RuntimePlatform.Android)
            PlayFabClientAPI.LinkAndroidDeviceID(new LinkAndroidDeviceIDRequest
            {
                AndroidDeviceId = deviceId,
                ForceLink = forceLink
            }, _ => { if (generation == authenticationGeneration) onComplete(true); }, onError);
        else
            PlayFabClientAPI.LinkIOSDeviceID(new LinkIOSDeviceIDRequest
            {
                DeviceId = deviceId,
                ForceLink = forceLink
            }, _ => { if (generation == authenticationGeneration) onComplete(true); }, onError);
    }

    private void FinishSelectedLogin(LoginResult result, bool deviceLinked, int generation,
        CloudSaveManager.StartMode mode)
    {
        if (generation != authenticationGeneration) return;
        manualLoginInProgress = false;
        string welcomeName = string.IsNullOrEmpty(loggedInPlayerName)
            ? "Player" : loggedInPlayerName;
        PlayerPrefs.SetInt("LoginChoice", 2);
        PlayerPrefs.SetString("SavedPlayerName", welcomeName);
        if (deviceLinked)
        {
            PlayerPrefs.SetString(RememberedPlayFabIdKey, result.PlayFabId);
            PlayerPrefs.SetString(DeviceLinkedPlayFabIdKey, result.PlayFabId);
        }
        else PlayerPrefs.DeleteKey(RememberedPlayFabIdKey);
        PlayerPrefs.Save();

        isGuest = false;
        if ((mode == CloudSaveManager.StartMode.StartFresh ||
             mode == CloudSaveManager.StartMode.UseGuestSave) &&
            PlayerDataManager.Instance?.CurrentData != null)
        {
            PlayerDataManager.Instance.CurrentData.playerName = welcomeName;
            PlayerDataManager.Instance.SaveGame();
        }
        UpdatePlayerNameDisplay();
        CloudSaveManager cloud = PlayerDataManager.Instance != null
            ? PlayerDataManager.Instance.GetComponent<CloudSaveManager>() : null;
        SetFeedbackMessage(cloud != null && !string.IsNullOrEmpty(cloud.LastSyncError)
            ? "Signed in. Online sync will retry; progress is saved on this device."
            : "Login successful. Save is ready.", successColor);
        if (returnToMainMenuAfterLogin) CompleteMainMenuAuthenticationRequest();
        else Invoke(nameof(LoadGameScene), 1.5f);
    }

    private void CancelPendingLogin()
    {
        CloseSaveChoice();
        manualLoginInProgress = false;
        IsCloudSaveReady = false;
        PlayFabClientAPI.ForgetAllCredentials();
        if (PlayerDataManager.Instance != null) PlayerDataManager.Instance.UseGuestSave();
        isGuest = true;
        loggedInPlayerName = "Guest";
        PlayerPrefs.SetInt("LoginChoice", 1);
        PlayerPrefs.SetString("SavedPlayerName", "Guest");
        PlayerPrefs.DeleteKey(RememberedPlayFabIdKey);
        PlayerPrefs.Save();
        UpdatePlayerNameDisplay();
        if (returnToMainMenuAfterLogin) CompleteMainMenuAuthenticationRequest();
        else CloseAuthCanvas();
    }

    private void FailPendingLogin(string message)
    {
        CloseSaveChoice();
        manualLoginInProgress = false;
        IsCloudSaveReady = false;
        CloudSaveManager cloud = PlayerDataManager.Instance != null
            ? PlayerDataManager.Instance.GetComponent<CloudSaveManager>() : null;
        if (cloud != null) cloud.EndSession();
        if (PlayerDataManager.Instance != null) PlayerDataManager.Instance.UseGuestSave();
        PlayFabClientAPI.ForgetAllCredentials();
        if (PlayerPrefs.GetInt("LoginChoice", 0) == 2)
        {
            PlayerPrefs.SetInt("LoginChoice", 0);
            PlayerPrefs.DeleteKey(RememberedPlayFabIdKey);
            PlayerPrefs.Save();
        }
        isGuest = IsGuestSelected;
        loggedInPlayerName = isGuest ? "Guest" : string.Empty;
        UpdatePlayerNameDisplay();
        SetFeedbackMessage(message, errorColor);
    }

    private void BeginCloudSaveLogin(LoginResult result, CloudSaveManager.StartMode mode, int generation,
        Action<bool> onComplete)
    {
        IsCloudSaveReady = false;
        CloudSaveManager cloud = PlayerDataManager.Instance != null
            ? PlayerDataManager.Instance.GetComponent<CloudSaveManager>() : null;
        if (cloud == null)
        {
            Debug.LogError("Cloud save manager is missing from PlayerDataManager.", this);
            onComplete(false);
            return;
        }
        cloud.BeginSession(result, mode, ready =>
        {
            if (generation != authenticationGeneration) return;
            IsCloudSaveReady = ready;
            onComplete(ready);
        });
    }

    private bool ShowSaveChoice(string message, string leftLabel, string rightLabel,
        Action onLeft, Action onRight)
    {
        CloseSaveChoice();
        SceneController sceneController = FindObjectOfType<SceneController>(true);
        GameObject template = sceneController != null ? sceneController.quitConfirmationPanel : null;
        if (template == null) return false;

        saveChoiceOverlay = new GameObject("Account Save Confirmation", typeof(RectTransform),
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = saveChoiceOverlay.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 32000;
        CanvasScaler scaler = saveChoiceOverlay.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        CanvasScaler sourceScaler = template.GetComponentInParent<CanvasScaler>(true);
        scaler.referenceResolution = sourceScaler != null
            ? sourceScaler.referenceResolution : new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = sourceScaler != null ? sourceScaler.matchWidthOrHeight : 0.5f;

        GameObject blocker = new GameObject("Dim Background", typeof(RectTransform), typeof(Image));
        blocker.transform.SetParent(saveChoiceOverlay.transform, false);
        RectTransform blockerRect = blocker.GetComponent<RectTransform>();
        blockerRect.anchorMin = Vector2.zero;
        blockerRect.anchorMax = Vector2.one;
        blockerRect.offsetMin = Vector2.zero;
        blockerRect.offsetMax = Vector2.zero;
        blocker.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.7f);

        GameObject panel = Instantiate(template, saveChoiceOverlay.transform, false);
        panel.name = "Account Save Choice Panel";
        Button left = null;
        Button right = null;
        TMP_Text body = null;
        foreach (Button button in panel.GetComponentsInChildren<Button>(true))
        {
            if (button.name == "btnCancel") left = button;
            if (button.name == "btnConf") right = button;
        }
        foreach (TMP_Text label in panel.GetComponentsInChildren<TMP_Text>(true))
            if (label.name == "textConf") { body = label; break; }

        if (left == null || right == null || body == null)
        {
            CloseSaveChoice();
            return false;
        }

        body.text = message;
        body.enableAutoSizing = true;
        body.fontSizeMin = 24f;
        left.onClick = new Button.ButtonClickedEvent();
        right.onClick = new Button.ButtonClickedEvent();
        SetChoiceButton(left, leftLabel, onLeft);
        SetChoiceButton(right, rightLabel, onRight);
        cancelSaveChoice = CancelPendingLogin;
        panel.SetActive(true);
        if (EventSystem.current == null)
            saveChoiceEventSystem = new GameObject("Account Save Event System",
                typeof(EventSystem), typeof(StandaloneInputModule));
        return true;
    }

    private static void SetChoiceButton(Button button, string label, Action action)
    {
        TMP_Text buttonText = button.GetComponentInChildren<TMP_Text>(true);
        if (buttonText != null)
        {
            buttonText.text = label;
            buttonText.enableAutoSizing = true;
            buttonText.fontSizeMin = 18f;
        }
        button.onClick.AddListener(() => action?.Invoke());
    }

    private void CloseSaveChoice()
    {
        cancelSaveChoice = null;
        if (saveChoiceOverlay != null) Destroy(saveChoiceOverlay);
        if (saveChoiceEventSystem != null) Destroy(saveChoiceEventSystem);
        saveChoiceOverlay = null;
        saveChoiceEventSystem = null;
    }

    private void OnDestroy()
    {
        CloseSaveChoice();
        if (Instance == this) Instance = null;
    }

    private void OnLoginError(PlayFabError error, int generation)
    {
        if (generation != authenticationGeneration) return;
        manualLoginInProgress = false;
        loginPassword.text = string.Empty;
        SetFeedbackMessage("Login Failed: " + error.ErrorMessage, errorColor);
    }

    private void CompleteMainMenuAuthenticationRequest()
    {
        CancelInvoke(nameof(LoadGameScene));
        returnToMainMenuAfterLogin = false;
        if (authCanvas != null) authCanvas.SetActive(false);
        MainMenuAuthenticationSucceeded?.Invoke();
    }

    private void OnRegisterSuccess(RegisterPlayFabUserResult result)
    {
        SetFeedbackMessage("Registration Successful!", successColor);
        
        var displayNameRequest = new UpdateUserTitleDisplayNameRequest { DisplayName = registerUsername.text.Trim() };
        PlayFabClientAPI.UpdateUserTitleDisplayName(displayNameRequest, 
            nameResult => Debug.Log("Name set to: " + nameResult.DisplayName), 
            nameError => Debug.LogWarning("Failed to set display name: " + nameError.ErrorMessage));

        registerPassword.text = "";
        if (registerConfirmPassword != null) registerConfirmPassword.text = ""; 
        loginUsername.text = registerUsername.text.Trim(); 
        
        // We removed the AddOrUpdateContactEmailRequest so it no longer attempts to trigger a verification email!

        Invoke(nameof(ShowLoginPanel), 2.5f);
    }

    private void OnRegisterError(PlayFabError error)
    {
        SetFeedbackMessage("Registration Failed: " + error.ErrorMessage, errorColor);
    }

    private void OnPasswordResetSuccess(SendAccountRecoveryEmailResult result)
    {
        SetFeedbackMessage("Recovery email sent! Check your inbox.", successColor);
        resetEmailInput.text = ""; 
        Invoke(nameof(ShowLoginPanel), 2.0f);
    }

    private void OnPasswordResetError(PlayFabError error)
    {
        SetFeedbackMessage("Password Reset Failed: " + error.ErrorMessage, errorColor);
        forgotPasswordCooldown = -1f; 
    }
}
