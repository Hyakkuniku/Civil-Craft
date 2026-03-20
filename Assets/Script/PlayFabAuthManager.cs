using UnityEngine;
using UnityEngine.UI; 
using TMPro; 
using PlayFab;
using PlayFab.ClientModels;
using UnityEngine.SceneManagement; 

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
    
    // NEW: The text element on your main menu that shows the name
    [Tooltip("Text on the main menu to show 'Playing as: Name'")]
    public TextMeshProUGUI playerNameDisplay; 

    [Header("Login Inputs")]
    public TMP_InputField loginUsername;
    public TMP_InputField loginPassword;

    [Header("Register Inputs")]
    public TMP_InputField registerUsername;
    public TMP_InputField registerEmail;
    public TMP_InputField registerPassword;

    [Header("Forgot Password Inputs")]
    public TMP_InputField resetEmailInput; 
    
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

        if (string.IsNullOrEmpty(PlayFabSettings.staticSettings.TitleId))
        {
            PlayFabSettings.staticSettings.TitleId = playFabTitleID;
        }

        if (authCanvas != null) authCanvas.SetActive(false);
    }

    private void Start()
    {
        // NEW: Check the player's name the second the game starts!
        UpdatePlayerNameDisplay();
    }

    // ────────────────────────────────────────────────
    // NEW: NAME DISPLAY LOGIC
    // ────────────────────────────────────────────────

    private void UpdatePlayerNameDisplay()
    {
        if (playerNameDisplay == null) return;

        int choice = PlayerPrefs.GetInt("LoginChoice", 0);

        if (choice == 0)
        {
            // Brand new player, no name to show! Hide the text.
            playerNameDisplay.gameObject.SetActive(false);
        }
        else
        {
            // They are a Guest or a Logged-in user. Show the text!
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
            // Brand new player! Open AuthCanvas directly to the Login Panel
            OpenAuthCanvas();
        }
        else if (choice == 1)
        {
            // They chose Guest previously. Skip menus and load the game!
            isGuest = true;
            LoadGameScene();
        }
        else if (choice == 2)
        {
            // They have an account. Are they still securely logged in?
            if (PlayFabClientAPI.IsClientLoggedIn())
            {
                LoadGameScene(); 
            }
            else
            {
                // Session expired. Show them the Login screen.
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
        
        // Save their choice permanently, AND save their name!
        PlayerPrefs.SetInt("LoginChoice", 1); 
        PlayerPrefs.SetString("SavedPlayerName", "Guest"); 
        PlayerPrefs.Save();

        UpdatePlayerNameDisplay(); // Instantly update the UI text

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
            if (registerPasswordToggleImage != null && hidePasswordSprite != null)
                registerPasswordToggleImage.sprite = hidePasswordSprite;
        }
        else
        {
            registerPassword.contentType = TMP_InputField.ContentType.Password;
            if (registerPasswordToggleImage != null && showPasswordSprite != null)
                registerPasswordToggleImage.sprite = showPasswordSprite;
        }

        registerPassword.ForceLabelUpdate();
    }

    // ────────────────────────────────────────────────
    // PLAYFAB API CALLS
    // ────────────────────────────────────────────────

    public void OnLoginButtonClicked()
    {
        if (string.IsNullOrEmpty(loginUsername.text) || string.IsNullOrEmpty(loginPassword.text))
        {
            SetFeedbackMessage("Please enter Username and Password.", errorColor);
            return;
        }

        SetFeedbackMessage("Logging in...", processColor);

        var request = new LoginWithPlayFabRequest
        {
            Username = loginUsername.text,
            Password = loginPassword.text,
            
            InfoRequestParameters = new GetPlayerCombinedInfoRequestParams
            {
                GetPlayerProfile = true,
                ProfileConstraints = new PlayerProfileViewConstraints
                {
                    ShowContactEmailAddresses = true,
                    ShowDisplayName = true
                }
            }
        };

        PlayFabClientAPI.LoginWithPlayFab(request, OnLoginSuccess, OnLoginError);
    }

    public void OnRegisterButtonClicked()
    {
        if (string.IsNullOrEmpty(registerUsername.text) || string.IsNullOrEmpty(registerEmail.text) || string.IsNullOrEmpty(registerPassword.text))
        {
            SetFeedbackMessage("Please fill out all fields.", errorColor);
            return;
        }

        if (registerPassword.text.Length < 6)
        {
            SetFeedbackMessage("Password must be at least 6 characters.", errorColor);
            return;
        }

        SetFeedbackMessage("Registering account...", processColor);

        var request = new RegisterPlayFabUserRequest
        {
            Username = registerUsername.text,
            Email = registerEmail.text,
            Password = registerPassword.text,
            RequireBothUsernameAndEmail = true 
        };

        PlayFabClientAPI.RegisterPlayFabUser(request, OnRegisterSuccess, OnRegisterError);
    }

    public void OnForgotPasswordButtonClicked()
    {
        if (string.IsNullOrEmpty(resetEmailInput.text))
        {
            SetFeedbackMessage("Please enter your email address.", errorColor);
            return;
        }

        SetFeedbackMessage("Sending recovery email...", processColor);

        var request = new SendAccountRecoveryEmailRequest
        {
            Email = resetEmailInput.text,
            TitleId = playFabTitleID
        };

        PlayFabClientAPI.SendAccountRecoveryEmail(request, OnPasswordResetSuccess, OnPasswordResetError);
    }

    public void SubmitNewDisplayName(string newName)
    {
        var request = new UpdateUserTitleDisplayNameRequest { DisplayName = newName };
        PlayFabClientAPI.UpdateUserTitleDisplayName(request, 
            result => {
                loggedInPlayerName = result.DisplayName;
                PlayerPrefs.SetString("SavedPlayerName", result.DisplayName); // Update locally too!
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
        bool isEmailVerified = false;
        
        if (result.InfoResultPayload != null && result.InfoResultPayload.PlayerProfile != null)
        {
            loggedInPlayerName = result.InfoResultPayload.PlayerProfile.DisplayName;

            if (result.InfoResultPayload.PlayerProfile.ContactEmailAddresses != null)
            {
                foreach (var emailInfo in result.InfoResultPayload.PlayerProfile.ContactEmailAddresses)
                {
                    if (emailInfo.VerificationStatus == EmailVerificationStatus.Confirmed)
                    {
                        isEmailVerified = true;
                        break;
                    }
                }
            }
        }

        if (isEmailVerified)
        {
            string welcomeName = string.IsNullOrEmpty(loggedInPlayerName) ? "Player" : loggedInPlayerName;
            SetFeedbackMessage("Login Successful! Welcome, " + welcomeName + "!", successColor);
            
            // Save their choice AND their name permanently!
            PlayerPrefs.SetInt("LoginChoice", 2);
            PlayerPrefs.SetString("SavedPlayerName", welcomeName);
            PlayerPrefs.Save();

            UpdatePlayerNameDisplay(); // Instantly update the UI text
            
            Debug.Log("Logged in! Name: " + welcomeName);
            Invoke(nameof(LoadGameScene), 1.5f); 
        }
        else
        {
            SetFeedbackMessage("Please check your email and click the verification link before logging in.", errorColor);
            PlayFabClientAPI.ForgetAllCredentials(); 
        }
    }

    private void OnLoginError(PlayFabError error)
    {
        SetFeedbackMessage("Login Failed: " + error.ErrorMessage, errorColor);
    }

    private void OnRegisterSuccess(RegisterPlayFabUserResult result)
    {
        SetFeedbackMessage("Registration Successful! Please check your email for the verification link.", successColor);
        
        var displayNameRequest = new UpdateUserTitleDisplayNameRequest { DisplayName = registerUsername.text };
        PlayFabClientAPI.UpdateUserTitleDisplayName(displayNameRequest, 
            nameResult => Debug.Log("Name set to: " + nameResult.DisplayName), 
            nameError => Debug.LogWarning("Failed to set display name: " + nameError.ErrorMessage));

        registerPassword.text = "";
        loginUsername.text = registerUsername.text; 
        
        var emailRequest = new AddOrUpdateContactEmailRequest { EmailAddress = registerEmail.text };
        PlayFabClientAPI.AddOrUpdateContactEmail(emailRequest, 
            success => Debug.Log("Email sent!"), 
            failure => Debug.LogWarning("Email failed: " + failure.ErrorMessage));

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
    }
}