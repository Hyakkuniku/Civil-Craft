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

        ApplyAuthenticationStyle();
        if (authCanvas != null) authCanvas.SetActive(false);
    }

    private void ApplyAuthenticationStyle()
    {
        if (authCanvas == null || loginPanel == null || registerPanel == null) return;

        Color brown = new Color32(73, 56, 44, 255);
        Color cream = new Color32(255, 250, 242, 255);
        Color white = new Color32(255, 253, 249, 255);
        Color muted = new Color32(116, 96, 80, 255);

        Sprite outlineSprite = null;
        Sprite surfaceSprite = null;
        SceneController sceneController = FindObjectOfType<SceneController>(true);
        if (sceneController != null && sceneController.quitConfirmationPanel != null)
        {
            Image outlineSource = sceneController.quitConfirmationPanel.GetComponent<Image>();
            if (outlineSource != null) outlineSprite = outlineSource.sprite;
            Transform sourceContent = sceneController.quitConfirmationPanel.transform
                .Find("QuitConfirmationContent");
            Image surfaceSource = sourceContent != null ? sourceContent.GetComponent<Image>() : null;
            if (surfaceSource != null) surfaceSprite = surfaceSource.sprite;
        }
        Sprite controlSprite = surfaceSprite;
        foreach (Image candidate in Resources.FindObjectsOfTypeAll<Image>())
        {
            if (candidate.sprite != null &&
                candidate.sprite.name.IndexOf("Rounded5", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                controlSprite = candidate.sprite;
                break;
            }
        }

        Image authBackground = authCanvas.transform.Find("BG")?.GetComponent<Image>();
        if (authBackground != null)
            authBackground.color = new Color(0.08f, 0.055f, 0.04f, 0.76f);

        StyleAuthCard(loginPanel, new Vector2(720f, 680f), outlineSprite, surfaceSprite,
            brown, cream);
        StyleAuthCard(registerPanel, new Vector2(720f, 790f), outlineSprite, surfaceSprite,
            brown, cream);
        if (forgotPasswordPanel != null)
            StyleAuthCard(forgotPasswordPanel, new Vector2(700f, 560f), outlineSprite,
                surfaceSprite, brown, cream);

        TMP_Text loginTitle = FindText(loginPanel, "txt_login");
        StyleTitle(loginTitle, "WELCOME BACK", new Vector2(0f, 245f), brown);
        CreateSubtitle(loginPanel, loginTitle, "Sign in to continue your journey",
            new Vector2(0f, 198f), muted);

        TMP_Text loginUserLabel = FindText(loginPanel, "txt_LogUser");
        TMP_Text loginPassLabel = FindText(loginPanel, "txt_LogPass");
        StyleFieldLabel(loginUserLabel, "USERNAME OR EMAIL", new Vector2(0f, 135f), brown);
        StyleFieldLabel(loginPassLabel, "PASSWORD", new Vector2(0f, 35f), brown);
        StyleInput(loginUsername, loginPanel.transform, new Vector2(0f, 95f),
            new Vector2(520f, 58f), controlSprite, brown, white, "Username or email");
        StyleInput(loginPassword, loginPanel.transform, new Vector2(0f, -5f),
            new Vector2(520f, 58f), controlSprite, brown, white, "Password");

        StyleNamedButton(loginPanel, "btn_login", "SIGN IN", new Vector2(0f, -110f),
            new Vector2(520f, 62f), true, controlSprite, brown, cream);
        StyleNamedButton(loginPanel, "btn_Register", "CREATE ACCOUNT", new Vector2(-132f, -200f),
            new Vector2(245f, 52f), false, controlSprite, brown, cream);
        StyleNamedButton(loginPanel, "btn_ForgotPass", "FORGOT PASSWORD", new Vector2(132f, -200f),
            new Vector2(245f, 52f), false, controlSprite, brown, cream);
        StyleNamedButton(loginPanel, "btn_guest", "CONTINUE AS GUEST", new Vector2(0f, -280f),
            new Vector2(330f, 50f), false, controlSprite, brown, cream);
        StyleCloseButton(loginPanel, "btn_exit", new Vector2(303f, 289f), brown);

        TMP_Text registerTitle = FindText(registerPanel, "txt_Title");
        StyleTitle(registerTitle, "CREATE ACCOUNT", new Vector2(0f, 302f), brown);
        CreateSubtitle(registerPanel, registerTitle, "Save your progress and play on another device",
            new Vector2(0f, 252f), muted);

        StyleFieldLabel(FindText(registerPanel, "txt_Email"), "EMAIL",
            new Vector2(0f, 195f), brown);
        StyleFieldLabel(FindText(registerPanel, "txt_RegUSer"), "USERNAME",
            new Vector2(0f, 91f), brown);
        StyleFieldLabel(FindText(registerPanel, "txt_RegPass"), "PASSWORD",
            new Vector2(0f, -13f), brown);
        StyleFieldLabel(FindText(registerPanel, "txt_ConfRegPass"), "CONFIRM PASSWORD",
            new Vector2(0f, -117f), brown);
        StyleInput(registerEmail, registerPanel.transform, new Vector2(0f, 155f),
            new Vector2(520f, 56f), controlSprite, brown, white, "Email address");
        StyleInput(registerUsername, registerPanel.transform, new Vector2(0f, 51f),
            new Vector2(520f, 56f), controlSprite, brown, white, "Choose a username");
        StyleInput(registerPassword, registerPanel.transform, new Vector2(0f, -53f),
            new Vector2(520f, 56f), controlSprite, brown, white, "Create a password");
        StyleInput(registerConfirmPassword, registerPanel.transform, new Vector2(0f, -157f),
            new Vector2(520f, 56f), controlSprite, brown, white, "Repeat your password");

        StyleNamedButton(registerPanel, "btn_Register", "CREATE ACCOUNT", new Vector2(0f, -250f),
            new Vector2(520f, 62f), true, controlSprite, brown, cream);
        StyleNamedButton(registerPanel, "btn_signup", "BACK TO SIGN IN", new Vector2(0f, -325f),
            new Vector2(260f, 46f), false, controlSprite, brown, cream, true);
        StyleCloseButton(registerPanel, "btn_exit (2)", new Vector2(303f, 337f), brown);

        if (forgotPasswordPanel != null)
        {
            TMP_Text recoverTitle = FindText(forgotPasswordPanel, "txt_title");
            StyleTitle(recoverTitle, "RECOVER ACCOUNT", new Vector2(0f, 190f), brown);
            CreateSubtitle(forgotPasswordPanel, recoverTitle,
                "We'll send a password reset link to your email",
                new Vector2(0f, 140f), muted);
            StyleFieldLabel(FindText(forgotPasswordPanel, "txt_LogUser"), "EMAIL",
                new Vector2(0f, 72f), brown);
            StyleInput(resetEmailInput, forgotPasswordPanel.transform, new Vector2(0f, 29f),
                new Vector2(520f, 58f), controlSprite, brown, white, "Email address");
            StyleNamedButton(forgotPasswordPanel, "btn_recover", "SEND RECOVERY EMAIL",
                new Vector2(0f, -78f), new Vector2(520f, 62f), true,
                controlSprite, brown, cream);
            StyleNamedButton(forgotPasswordPanel, "btn_back", "BACK TO SIGN IN",
                new Vector2(0f, -166f), new Vector2(260f, 46f), false,
                controlSprite, brown, cream, true);
            StyleCloseButton(forgotPasswordPanel, "btn_exit (1)",
                new Vector2(293f, 224f), brown);
        }

        if (feedbackText != null)
        {
            SetRect(feedbackText.rectTransform, new Vector2(0f, -375f), new Vector2(900f, 52f));
            feedbackText.enableAutoSizing = true;
            feedbackText.fontSizeMin = 18f;
            feedbackText.fontSizeMax = 26f;
            feedbackText.alignment = TextAlignmentOptions.Center;
            feedbackText.fontStyle = FontStyles.Bold;
            feedbackText.raycastTarget = false;
        }

        errorColor = new Color32(174, 57, 49, 255);
        successColor = new Color32(56, 126, 75, 255);
        processColor = new Color32(166, 111, 43, 255);
    }

    private static void StyleAuthCard(GameObject panel, Vector2 size, Sprite outlineSprite,
        Sprite surfaceSprite, Color outlineColor, Color surfaceColor)
    {
        RectTransform panelRect = panel.transform as RectTransform;
        SetRect(panelRect, Vector2.zero, size);
        Image outline = panel.GetComponent<Image>();
        if (outline != null)
        {
            if (outlineSprite != null) outline.sprite = outlineSprite;
            outline.type = Image.Type.Sliced;
            outline.color = outlineColor;
        }

        Transform existing = panel.transform.Find("AuthCardInterior");
        GameObject interior = existing != null ? existing.gameObject :
            new GameObject("AuthCardInterior", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        interior.layer = panel.layer;
        interior.transform.SetParent(panel.transform, false);
        interior.transform.SetAsFirstSibling();
        RectTransform interiorRect = interior.transform as RectTransform;
        interiorRect.anchorMin = Vector2.zero;
        interiorRect.anchorMax = Vector2.one;
        interiorRect.offsetMin = new Vector2(8f, 8f);
        interiorRect.offsetMax = new Vector2(-8f, -8f);
        Image surface = interior.GetComponent<Image>();
        if (surfaceSprite != null) surface.sprite = surfaceSprite;
        surface.type = Image.Type.Sliced;
        surface.color = surfaceColor;
        surface.raycastTarget = false;
    }

    private static void StyleTitle(TMP_Text title, string text, Vector2 position, Color color)
    {
        if (title == null) return;
        title.text = text;
        title.color = color;
        title.fontStyle = FontStyles.Bold;
        title.enableAutoSizing = true;
        title.fontSizeMin = 28f;
        title.fontSizeMax = 46f;
        title.alignment = TextAlignmentOptions.Center;
        title.raycastTarget = false;
        SetRect(title.rectTransform, position, new Vector2(620f, 64f));
    }

    private static void CreateSubtitle(GameObject panel, TMP_Text source, string text,
        Vector2 position, Color color)
    {
        if (source == null) return;
        Transform existing = panel.transform.Find("AuthSubtitle");
        TMP_Text subtitle;
        if (existing != null) subtitle = existing.GetComponent<TMP_Text>();
        else
        {
            subtitle = Instantiate(source, panel.transform, false);
            subtitle.gameObject.name = "AuthSubtitle";
        }
        subtitle.text = text;
        subtitle.color = color;
        subtitle.fontStyle = FontStyles.Normal;
        subtitle.enableAutoSizing = true;
        subtitle.fontSizeMin = 18f;
        subtitle.fontSizeMax = 25f;
        subtitle.alignment = TextAlignmentOptions.Center;
        subtitle.raycastTarget = false;
        SetRect(subtitle.rectTransform, position, new Vector2(640f, 42f));
    }

    private static void StyleFieldLabel(TMP_Text label, string text, Vector2 position, Color color)
    {
        if (label == null) return;
        label.text = text;
        label.color = color;
        label.fontStyle = FontStyles.Bold;
        label.enableAutoSizing = true;
        label.fontSizeMin = 16f;
        label.fontSizeMax = 22f;
        label.alignment = TextAlignmentOptions.Left;
        label.raycastTarget = false;
        SetRect(label.rectTransform, position, new Vector2(560f, 30f));
    }

    private static void StyleInput(TMP_InputField input, Transform panel, Vector2 position,
        Vector2 size, Sprite roundedSprite, Color outlineColor, Color surfaceColor,
        string placeholder)
    {
        if (input == null) return;
        input.transform.SetParent(panel, false);
        RectTransform rect = input.transform as RectTransform;
        SetRect(rect, position, size);
        Image background = input.GetComponent<Image>();
        if (background != null)
        {
            if (roundedSprite != null) background.sprite = roundedSprite;
            background.type = Image.Type.Sliced;
            background.color = new Color32(248, 242, 233, 255);
            Outline border = input.GetComponent<Outline>();
            if (border == null) border = input.gameObject.AddComponent<Outline>();
            border.effectColor = new Color(outlineColor.r, outlineColor.g, outlineColor.b, 0.48f);
            border.effectDistance = new Vector2(1.25f, -1.25f);
            border.useGraphicAlpha = true;
        }
        if (input.textComponent != null)
        {
            input.textComponent.color = outlineColor;
            input.textComponent.fontSize = 22f;
            input.textComponent.margin = new Vector4(18f, 0f, 50f, 0f);
        }
        TMP_Text placeholderText = input.placeholder as TMP_Text;
        if (placeholderText != null)
        {
            placeholderText.text = placeholder;
            placeholderText.color = new Color(outlineColor.r, outlineColor.g, outlineColor.b, 0.42f);
            placeholderText.fontSize = 21f;
            placeholderText.fontStyle = FontStyles.Normal;
            placeholderText.margin = new Vector4(18f, 0f, 50f, 0f);
        }
        ColorBlock inputColors = input.colors;
        inputColors.normalColor = Color.white;
        inputColors.highlightedColor = new Color32(255, 252, 247, 255);
        inputColors.selectedColor = new Color32(255, 252, 247, 255);
        inputColors.pressedColor = new Color32(244, 235, 224, 255);
        inputColors.disabledColor = new Color32(215, 208, 199, 180);
        inputColors.colorMultiplier = 1f;
        inputColors.fadeDuration = 0.12f;
        input.colors = inputColors;
        input.customCaretColor = true;
        input.caretColor = outlineColor;
        input.selectionColor = new Color(outlineColor.r, outlineColor.g, outlineColor.b, 0.25f);
    }

    private static void StyleNamedButton(GameObject panel, string objectName, string text,
        Vector2 position, Vector2 size, bool primary, Sprite roundedSprite,
        Color brown, Color cream, bool linkStyle = false)
    {
        Button button = FindButton(panel, objectName);
        if (button == null) return;
        RectTransform rect = button.transform as RectTransform;
        button.transform.SetParent(panel.transform, false);
        SetRect(rect, position, size);
        Image image = button.GetComponent<Image>();
        if (image != null)
        {
            if (roundedSprite != null) image.sprite = roundedSprite;
            image.type = Image.Type.Sliced;
            Color secondaryFill = new Color32(237, 224, 207, 255);
            image.color = linkStyle ? new Color(1f, 1f, 1f, 0f) :
                primary ? brown : secondaryFill;
            Outline border = button.GetComponent<Outline>();
            if (!primary && !linkStyle)
            {
                if (border == null) border = button.gameObject.AddComponent<Outline>();
                border.effectColor = new Color(brown.r, brown.g, brown.b, 0.3f);
                border.effectDistance = new Vector2(1f, -1f);
                border.useGraphicAlpha = true;
                border.enabled = true;
            }
            else if (border != null) border.enabled = false;

            Shadow depth = null;
            foreach (Shadow shadow in button.GetComponents<Shadow>())
            {
                if (!(shadow is Outline)) { depth = shadow; break; }
            }
            if (!linkStyle)
            {
                if (depth == null) depth = button.gameObject.AddComponent<Shadow>();
                depth.effectColor = new Color(0.16f, 0.11f, 0.08f, primary ? 0.24f : 0.12f);
                depth.effectDistance = new Vector2(0f, primary ? -4f : -2f);
                depth.useGraphicAlpha = true;
                depth.enabled = true;
            }
            else if (depth != null) depth.enabled = false;
        }
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = primary
            ? new Color32(255, 244, 232, 255) : new Color32(255, 250, 242, 255);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = primary
            ? new Color32(200, 179, 158, 255) : new Color32(224, 208, 190, 255);
        colors.disabledColor = new Color32(170, 162, 153, 150);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.12f;
        button.colors = colors;
        TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
        {
            label.text = text;
            label.color = primary ? cream : brown;
            label.fontStyle = linkStyle ? FontStyles.Normal : FontStyles.Bold;
            label.enableAutoSizing = true;
            label.fontSizeMin = 15f;
            label.fontSizeMax = primary ? 24f : 21f;
            label.alignment = TextAlignmentOptions.Center;
        }
    }

    private static void StyleCloseButton(GameObject panel, string objectName, Vector2 position,
        Color brown)
    {
        Button button = FindButton(panel, objectName);
        if (button == null) return;
        button.transform.SetParent(panel.transform, false);
        SetRect(button.transform as RectTransform, position, new Vector2(54f, 54f));
        Image image = button.GetComponent<Image>();
        TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
        {
            if (image != null) image.color = new Color(1f, 1f, 1f, 0f);
            label.text = "×";
            label.color = brown;
            label.fontStyle = FontStyles.Bold;
            label.fontSize = 38f;
            label.alignment = TextAlignmentOptions.Center;
        }
        else if (image != null) image.color = brown;
    }

    private static TMP_Text FindText(GameObject root, string objectName)
    {
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
            if (text.gameObject.name == objectName) return text;
        return null;
    }

    private static Button FindButton(GameObject root, string objectName)
    {
        foreach (Button button in root.GetComponentsInChildren<Button>(true))
            if (button.gameObject.name == objectName) return button;
        return null;
    }

    private static void SetRect(RectTransform rect, Vector2 position, Vector2 size)
    {
        if (rect == null) return;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
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
        PositionFeedback(-375f);
        SetFeedbackMessage("", Color.white);
    }

    public void ShowRegisterPanel()
    {
        loginPanel.SetActive(false);
        registerPanel.SetActive(true);
        if (forgotPasswordPanel != null) forgotPasswordPanel.SetActive(false);
        PositionFeedback(-430f);
        SetFeedbackMessage("", Color.white);
    }

    public void ShowForgotPasswordPanel()
    {
        loginPanel.SetActive(false);
        registerPanel.SetActive(false);
        if (forgotPasswordPanel != null) forgotPasswordPanel.SetActive(true);
        PositionFeedback(-320f);
        SetFeedbackMessage("", Color.white);
    }

    private void PositionFeedback(float verticalPosition)
    {
        if (feedbackText == null) return;
        RectTransform rect = feedbackText.rectTransform;
        rect.anchoredPosition = new Vector2(0f, verticalPosition);
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
                        ? "This account has saved progress.\nSwitch accounts? Your guest save stays."
                        : "This account has a device save.\nSwitch accounts? Your guest save stays.";
                    if (!ShowSaveChoice(message, "Switch Account", "Stay Guest",
                            () => StartSelectedLogin(result, generation,
                                online ? CloudSaveManager.StartMode.LoadOnline :
                                    CloudSaveManager.StartMode.LoadLocalAccount),
                            CancelPendingLogin))
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
        // The main-menu confirmation uses the same rounded sprite and typeface
        // as the bridge-redesign dialog. Match that dialog's light card styling.
        Color outlineColor = new Color32(73, 56, 44, 255);
        Color fillColor = new Color32(255, 251, 246, 255);
        Image panelImage = panel.GetComponent<Image>();
        if (panelImage != null) panelImage.color = outlineColor;
        Transform content = panel.transform.Find("QuitConfirmationContent");
        Image contentImage = content != null ? content.GetComponent<Image>() : null;
        if (contentImage != null) contentImage.color = fillColor;
        Button left = null;
        Button right = null;
        TMP_Text body = null;
        foreach (Button button in panel.GetComponentsInChildren<Button>(true))
        {
            if (button.name == "btnConf") left = button;
            if (button.name == "btnCancel") right = button;
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
        body.fontSize = 34f;
        body.fontSizeMin = 18f;
        body.fontSizeMax = 34f;
        body.alignment = TextAlignmentOptions.Center;
        body.enableWordWrapping = true;
        body.color = outlineColor;
        RectTransform bodyRect = body.rectTransform;
        bodyRect.anchorMin = new Vector2(0.08f, 0.38f);
        bodyRect.anchorMax = new Vector2(0.92f, 0.9f);
        bodyRect.offsetMin = Vector2.zero;
        bodyRect.offsetMax = Vector2.zero;
        left.onClick = new Button.ButtonClickedEvent();
        right.onClick = new Button.ButtonClickedEvent();
        SetChoiceButton(left, leftLabel, onLeft, -160f, outlineColor, fillColor);
        SetChoiceButton(right, rightLabel, onRight, 160f, outlineColor, fillColor);
        cancelSaveChoice = CancelPendingLogin;
        panel.SetActive(true);
        if (EventSystem.current == null)
            saveChoiceEventSystem = new GameObject("Account Save Event System",
                typeof(EventSystem), typeof(StandaloneInputModule));
        return true;
    }

    private static void SetChoiceButton(Button button, string label, Action action,
        float horizontalPosition, Color outlineColor, Color fillColor)
    {
        RectTransform buttonRect = button.transform as RectTransform;
        if (buttonRect != null)
        {
            buttonRect.anchoredPosition = new Vector2(horizontalPosition, -130f);
            buttonRect.sizeDelta = new Vector2(250f, 90f);
        }
        Image outline = button.GetComponent<Image>();
        if (outline != null)
        {
            outline.color = outlineColor;
            GameObject inset = new GameObject("Light Button Interior",
                typeof(RectTransform), typeof(Image));
            inset.transform.SetParent(button.transform, false);
            inset.transform.SetAsFirstSibling();
            RectTransform insetRect = inset.GetComponent<RectTransform>();
            insetRect.anchorMin = Vector2.zero;
            insetRect.anchorMax = Vector2.one;
            insetRect.offsetMin = new Vector2(5f, 5f);
            insetRect.offsetMax = new Vector2(-5f, -5f);
            Image insetImage = inset.GetComponent<Image>();
            insetImage.sprite = outline.sprite;
            insetImage.type = Image.Type.Sliced;
            insetImage.color = fillColor;
            insetImage.raycastTarget = false;
        }
        TMP_Text buttonText = button.GetComponentInChildren<TMP_Text>(true);
        if (buttonText != null)
        {
            buttonText.text = label;
            buttonText.enableAutoSizing = true;
            buttonText.fontSize = 27f;
            buttonText.fontSizeMin = 16f;
            buttonText.fontSizeMax = 27f;
            buttonText.alignment = TextAlignmentOptions.Center;
            buttonText.color = outlineColor;
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
