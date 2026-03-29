using UnityEngine;
using UnityEngine.UI; 
using TMPro; 
using PlayFab;
using PlayFab.ClientModels;
using UnityEngine.SceneManagement; 
using System.Text.RegularExpressions; 

public class PlayFabAuthManager : MonoBehaviour
{
    public static PlayFabAuthManager Instance { get; private set; }

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
        else Destroy(gameObject);

        if (!string.IsNullOrEmpty(playFabTitleID))
        {
            PlayFabSettings.staticSettings.TitleId = playFabTitleID;
        }

        if (authCanvas != null) authCanvas.SetActive(false);
    }

    private void Start()
    {
        UpdatePlayerNameDisplay();
    }

    private void Update()
    {
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
            playerNameDisplay.text = "Playing as: " + savedName;
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
            if (PlayFabClientAPI.IsClientLoggedIn())
            {
                LoadGameScene(); 
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
        SceneManager.LoadScene(sceneToLoad);
    }

    // ────────────────────────────────────────────────
    // UI NAVIGATION & TOGGLES
    // ────────────────────────────────────────────────

    public void OpenAuthCanvas()
    {
        authCanvas.SetActive(true);
        ShowLoginPanel(); 
    }

    public void CloseAuthCanvas()
    {
        authCanvas.SetActive(false);
    }

    public void OnPlayAsGuestClicked()
    {
        isGuest = true;
        loggedInPlayerName = "Guest";
        
        PlayerPrefs.SetInt("LoginChoice", 1); 
        PlayerPrefs.SetString("SavedPlayerName", "Guest"); 
        PlayerPrefs.Save();

        UpdatePlayerNameDisplay(); 

        SetFeedbackMessage("Starting as Guest...", successColor);
        Invoke(nameof(LoadGameScene), 1.0f); 
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

        if (isEmailLogin)
        {
            var request = new LoginWithEmailAddressRequest
            {
                Email = inputId,
                Password = loginPassword.text,
                InfoRequestParameters = infoParams
            };
            PlayFabClientAPI.LoginWithEmailAddress(request, OnLoginSuccess, OnLoginError);
        }
        else
        {
            var request = new LoginWithPlayFabRequest
            {
                Username = inputId,
                Password = loginPassword.text,
                InfoRequestParameters = infoParams
            };
            PlayFabClientAPI.LoginWithPlayFab(request, OnLoginSuccess, OnLoginError);
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

    private void OnLoginSuccess(LoginResult result)
    {
        string inputId = loginUsername.text.Trim();
        bool isEmailLogin = inputId.Contains("@");

        if (!isEmailLogin && result.InfoResultPayload != null && result.InfoResultPayload.AccountInfo != null)
        {
            if (result.InfoResultPayload.AccountInfo.Username != inputId)
            {
                SetFeedbackMessage("Login Failed: Username is case-sensitive. Please check your capitalization.", errorColor);
                PlayFabClientAPI.ForgetAllCredentials(); 
                return;
            }
        }

        // We removed the email verification check entirely! Just log them straight in.
        
        if (result.InfoResultPayload != null && result.InfoResultPayload.PlayerProfile != null)
        {
            loggedInPlayerName = result.InfoResultPayload.PlayerProfile.DisplayName;
        }

        string welcomeName = string.IsNullOrEmpty(loggedInPlayerName) ? "Player" : loggedInPlayerName;
        SetFeedbackMessage("Login Successful! Welcome, " + welcomeName + "!", successColor);
        
        PlayerPrefs.SetInt("LoginChoice", 2);
        PlayerPrefs.SetString("SavedPlayerName", welcomeName);
        PlayerPrefs.Save();

        UpdatePlayerNameDisplay(); 
        
        Debug.Log("Logged in! Name: " + welcomeName);
        Invoke(nameof(LoadGameScene), 1.5f); 
    }

    private void OnLoginError(PlayFabError error)
    {
        SetFeedbackMessage("Login Failed: " + error.ErrorMessage, errorColor);
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