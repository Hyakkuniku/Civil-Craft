using System;
using System.IO;
using System.Reflection;
using System.Text;
using PlayFab;
using PlayFab.ClientModels;
using PlayFab.Internal;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Opt-in plain-name regressions in a disposable preview scene. Never saves or authenticates.</summary>
public static class NameRegistrationPolicyValidation
{
    private const string Request = "Temp/NameRegistrationPolicyValidation.request";
    private const string Report = "Temp/NameRegistrationPolicyValidation.txt";
    private const string Menu = "Tools/Civil Craft/Validate Registration Name Policy";
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static double nextCheck;

    private static readonly string[] AcceptedNames =
    {
        "Engineer", "Alice Smith", "  Alice  ", "BF40BF", "Dev_Hyakkimaru", "player.dev",
        ".dev", "dev_name", "#12", "#abZ", "#GHI", "R&D_42", "Renée", "工程师", "#１２３"
    };

    private static readonly string[] FormattedNames =
    {
        "<#BF40BF>Alice", "<color=#bf40bf>Alice</color>", "<b>Alice</b>", "Alice<", "Alice>",
        "#BF40BF", "Alice#bf40bf", "#BF40BF00 Alice", "#B4B", "#abcd", "#123", "#abcZ", "#12#ABC"
    };

    private static readonly string[] ReservedNames =
    {
        ".dev_", ".dev_hyakkimaru", ".DEV_Hyakk", "player.dEv_name", "  .dEv_hyakk  ", "x.dev_y"
    };

    private static bool Busy => EditorApplication.isPlayingOrWillChangePlaymode ||
        EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer;

    [InitializeOnLoadMethod]
    private static void Watch()
    {
        EditorApplication.update -= CheckRequest;
        EditorApplication.update += CheckRequest;
    }

    private static void CheckRequest()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(Request) || Busy) return;
        File.Delete(Request);
        WriteReport();
    }

    [MenuItem(Menu)]
    private static void RunFromMenu() => WriteReport();

    [MenuItem(Menu, true)]
    private static bool CanRunFromMenu() => !Busy;

    private static void WriteReport()
    {
        Directory.CreateDirectory("Temp");
        try
        {
            File.WriteAllText(Report, Run());
            Debug.Log("[Registration name policy] PASS. Report: " + Report);
        }
        catch (Exception error)
        {
            File.WriteAllText(Report, "FAIL: " + error);
            Debug.LogException(error);
        }
    }

    /// <summary>Calls only pure validation, UI presentation, and rejected confirmation/authentication paths.</summary>
    public static string Run()
    {
        Check(!Busy, "Wait until Play Mode, compilation, imports and player builds have stopped.");
        var report = new StringBuilder();
        ValidatePolicy(report);
        var identity = new IdentitySnapshot();
        Scene preview = EditorSceneManager.NewPreviewScene();
        GameObject fixture = null;
        ITransportPlugin previousTransport = null;
        var blockedTransport = new BlockingTransport();
        int sdkCalls = 0;
        PlayFabHttp.ApiProcessingEvent<ApiProcessingEventArgs> observer = args =>
        {
            if (args.EventType == ApiProcessingEventType.Pre) sdkCalls++;
        };
        bool transportInstalled = false;
        bool observerInstalled = false;
        try
        {
            // Inactive parent means Awake/Start/OnEnable on production controllers never run.
            fixture = new GameObject("Name Policy Validation (Temporary)", typeof(RectTransform), typeof(Canvas));
            fixture.SetActive(false);
            SceneManager.MoveGameObjectToScene(fixture, preview);
            fixture.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            ValidateBhan(fixture.transform, report);

            // A future missing guard still cannot issue network traffic during this regression check.
            // The original transport is restored synchronously before returning to the editor.
            previousTransport = PluginManager.GetPlugin<ITransportPlugin>(PluginContract.PlayFab_Transport);
            PluginManager.SetPlugin(blockedTransport, PluginContract.PlayFab_Transport);
            transportInstalled = true;
            PlayFabHttp.ApiProcessingEventHandler += observer;
            observerInstalled = true;
            ValidateAuthentication(fixture.transform, report);
            Check(blockedTransport.Attempts == 0 && sdkCalls == 0,
                "A rejected name escaped the guard and reached the PlayFab SDK.");
            identity.AssertUnchanged();
            report.AppendLine("PASS: No PlayFab SDK calls, network attempts, player identity changes, PlayerPrefs changes or save changes.");
            report.AppendLine("NOTE: No valid ConfirmNameYes, valid account registration, valid display-name update or live scene load/save was executed.");
            report.AppendLine("Completed UTC: " + DateTime.UtcNow.ToString("O"));
            return report.ToString();
        }
        finally
        {
            if (observerInstalled) PlayFabHttp.ApiProcessingEventHandler -= observer;
            if (transportInstalled) PluginManager.SetPlugin(previousTransport, PluginContract.PlayFab_Transport);
            if (fixture != null) Object.DestroyImmediate(fixture);
            if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    private static void ValidatePolicy(StringBuilder report)
    {
        foreach (string name in AcceptedNames)
        {
            Check(PlayerNamePolicy.TryValidate(name, out string error), "An ordinary name was rejected: " + name);
            Check(string.IsNullOrEmpty(error), "An accepted name retained an error: " + name);
        }
        foreach (string name in FormattedNames) ExpectRejected(name, PlayerNamePolicy.FormattingError);
        foreach (string name in ReservedNames) ExpectRejected(name, PlayerNamePolicy.ReservedNameError);
        foreach (string blank in new[] { null, string.Empty, " \t\r\n" }) ExpectRejected(blank, "Please enter a name.");
        report.AppendLine("PASS: Plain/Unicode names and bare hex-like text are accepted; markup, # plus three or more ASCII hex digits, reserved .dev_ in any case, and blank policy input are rejected.");
    }

    private static void ExpectRejected(string name, string expectedError)
    {
        Check(!PlayerNamePolicy.TryValidate(name, out string error), "A prohibited name was accepted: " + name);
        Check(error == expectedError, "A prohibited name returned an unexpected error: " + name);
    }

    private static void ValidateBhan(Transform parent, StringBuilder report)
    {
        GameObject controller = Rect("Bhan Registration Fixture", parent);
        var ui = controller.AddComponent<NameRegistrationUI>();
        ui.enabled = false;
        ui.nameInputPanel = Rect("Input Panel", controller.transform);
        GameObject authoredFrame = Rect("Registration Wood Frame", ui.nameInputPanel.transform);
        TMP_Text hint = Label("Registration Name Hint", authoredFrame.transform,
            "Your name appears in your Almanac and multiplayer.");
        hint.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            "Assets/TextMesh Pro/Resources/Fonts & Materials/Bekind Sans SDF.asset");
        Check(hint.font != null, "The authored Bekind font is unavailable for validation-error layout checks.");
        hint.fontSize = hint.fontSizeMax = 21;
        hint.fontSizeMin = 18;
        hint.enableAutoSizing = true;
        hint.enableWordWrapping = true;
        hint.margin = Vector4.zero;
        hint.rectTransform.sizeDelta = new Vector2(690, 37);
        Color normalColor = new Color32(147, 113, 80, 255);
        hint.color = normalColor;
        hint.richText = true;
        string normalText = hint.text;
        ui.nameInputField = Input("Name", ui.nameInputPanel.transform);
        ui.confirmationPanel = Rect("Confirmation Panel", controller.transform);
        ui.confirmationText = Label("Confirmation Message", ui.confirmationPanel.transform, "UNCHANGED");
        ui.confirmationNameText = Label("Confirmed Engineer Name", ui.confirmationPanel.transform, "UNCHANGED");
        ui.confirmationPanel.SetActive(false);
        ui.onNameConfirmed = new UnityEvent();
        int confirmed = 0;
        ui.onNameConfirmed.AddListener(() => confirmed++);

        ui.ConfirmNameYes(); // Empty/unarmed pending name; never a valid save path.
        CheckRejectedBhan(ui, hint, "Please enter a name.", confirmed);
        foreach (string name in FormattedNames)
        {
            ui.nameInputField.text = name;
            ui.SubmitName();
            CheckRejectedBhan(ui, hint, PlayerNamePolicy.FormattingError, confirmed);
        }
        foreach (string name in ReservedNames)
        {
            ui.nameInputField.text = name;
            ui.SubmitName();
            CheckRejectedBhan(ui, hint, PlayerNamePolicy.ReservedNameError, confirmed);
        }
        report.AppendLine("PASS: Actual Bhan invalid Submit keeps entry visible, hides confirmation, clears pending state, reports the error in the existing hint and fires no follow-up event.");

        ui.nameInputField.text = "  Alice  ";
        ui.SubmitName();
        CheckConfirmation(ui, "Alice", confirmed);
        Check(hint.text == normalText && hint.color.Equals(normalColor) && hint.richText,
            "Valid Submit did not restore the authored hint text/color/rich-text setting.");
        ui.ConfirmNameNo();
        Check(ui.nameInputPanel.activeSelf && !ui.confirmationPanel.activeSelf &&
            !Get<bool>(ui, "nameAwaitingConfirmation") && confirmed == 0,
            "Edit Name did not cancel its pending confirmation.");

        ui.nameInputField.text = "BF40BF";
        ui.SubmitName();
        CheckConfirmation(ui, "BF40BF", confirmed);
        ui.nameInputField.text = "Alice#ABC";
        ui.SubmitName();
        CheckRejectedBhan(ui, hint, PlayerNamePolicy.FormattingError, confirmed);
        ui.ConfirmNameYes(); // Repeated submit must have erased the previously valid pending name.
        CheckRejectedBhan(ui, hint, "Please enter a name.", confirmed);

        ui.nameInputField.text = " \t ";
        ui.SubmitName();
        CheckConfirmation(ui, "Engineer", confirmed);
        ui.ConfirmNameNo();
        report.AppendLine("PASS: Valid/trimmed names and blank-to-Engineer fallback open confirmation; Edit Name disarms it; a subsequent invalid Submit clears a previously valid pending name.");

        foreach (string invalid in new[] { "<#BF40BF>Alice", "#BF40BF", ".DEV_Hyakk" })
        {
            Set(ui, "pendingName", invalid);
            Set(ui, "nameAwaitingConfirmation", true);
            ui.nameInputPanel.SetActive(false);
            ui.confirmationPanel.SetActive(true);
            ui.ConfirmNameYes();
            PlayerNamePolicy.TryValidate(invalid, out string error);
            CheckRejectedBhan(ui, hint, error, confirmed);
        }
        report.AppendLine("PASS: Directly corrupted pending confirmation is revalidated and rejected without events or saving.");

        // ShowNamePrompt searches globally for InputManager. Exercise it only when
        // no loaded scene's input could be frozen by that existing production behavior.
        if (Object.FindObjectOfType<InputManager>() == null)
        {
            ui.ShowNamePrompt();
            Check(ui.nameInputPanel.activeSelf && !ui.confirmationPanel.activeSelf &&
                ui.nameInputField.text == string.Empty && Get<string>(ui, "pendingName") == string.Empty &&
                !Get<bool>(ui, "nameAwaitingConfirmation") && hint.text == normalText &&
                hint.color.Equals(normalColor) && hint.richText && confirmed == 0,
                "ShowNamePrompt did not restore the normal hint and clear pending confirmation.");
            report.AppendLine("PASS: ShowNamePrompt restores the authored hint and clears pending entry/confirmation state.");
        }
        else
            report.AppendLine("NOTE: ShowNamePrompt invocation skipped because a loaded scene has an active InputManager; its input state was left untouched. Hint restoration was exercised by valid Submit.");
    }

    private static void CheckRejectedBhan(NameRegistrationUI ui, TMP_Text hint, string error, int confirmed)
    {
        Check(ui.nameInputPanel.activeSelf && !ui.confirmationPanel.activeSelf,
            "An invalid name closed entry or opened confirmation.");
        Check(Get<string>(ui, "pendingName") == string.Empty && !Get<bool>(ui, "nameAwaitingConfirmation"),
            "An invalid name retained pending confirmation state.");
        Check(hint.text == error && !hint.richText, "The authored name hint did not display the plain validation error.");
        hint.ForceMeshUpdate(true, true);
        Check(!hint.isTextOverflowing, "The validation error overflows the authored name-hint area: " + error);
        Check(confirmed == 0, "A rejected name fired the Bhan follow-up event.");
    }

    private static void CheckConfirmation(NameRegistrationUI ui, string name, int confirmed)
    {
        Check(!ui.nameInputPanel.activeSelf && ui.confirmationPanel.activeSelf &&
            Get<bool>(ui, "nameAwaitingConfirmation") && Get<string>(ui, "pendingName") == name,
            "A valid name did not become the trimmed pending confirmation.");
        Check(ui.confirmationNameText.text == name && !ui.confirmationNameText.richText && confirmed == 0,
            "Confirmation did not display the literal name without firing its follow-up event.");
    }

    private static void ValidateAuthentication(Transform parent, StringBuilder report)
    {
        GameObject controller = Rect("Auth Registration Fixture", parent);
        var auth = controller.AddComponent<PlayFabAuthManager>();
        auth.enabled = false;
        auth.registerUsername = Input("Username", controller.transform);
        auth.registerEmail = Input("Email", controller.transform);
        auth.registerPassword = Input("Password", controller.transform);
        auth.registerConfirmPassword = Input("Confirm Password", controller.transform);
        auth.loginUsername = Input("Login Username", controller.transform);
        auth.feedbackText = Label("Feedback", controller.transform, "UNCHANGED");
        auth.registerEmail.text = "name-validation@example.invalid";
        auth.registerPassword.text = "Validation123";
        auth.registerConfirmPassword.text = "Validation123";
        auth.loginUsername.text = "LOGIN UNCHANGED";
        auth.loggedInPlayerName = "IDENTITY UNCHANGED";
        auth.isGuest = true;
        int authEvents = 0;
        auth.MainMenuAuthenticationSucceeded += () => authEvents++;
        auth.AutomaticLoginCompleted += _ => authEvents++;

        foreach (string name in FormattedNames)
            CheckRejectedAuth(auth, name, PlayerNamePolicy.FormattingError, ref authEvents);
        foreach (string name in ReservedNames)
            CheckRejectedAuth(auth, name, PlayerNamePolicy.ReservedNameError, ref authEvents);

        foreach (string blank in new[] { null, string.Empty, " \t\r\n" })
        {
            auth.feedbackText.text = "UNCHANGED";
            auth.SubmitNewDisplayName(blank);
            CheckAuthFeedback(auth, "Please enter a name.", authEvents);
        }

        MethodInfo success = typeof(PlayFabAuthManager).GetMethod("OnRegisterSuccess", PrivateInstance,
            null, new[] { typeof(RegisterPlayFabUserResult), typeof(string) }, null);
        Check(success != null, "The guarded registration callback no longer accepts the captured registered name.");
        foreach (string name in new[] { "#BF40BF", "<b>Alice</b>", ".DEV_Hyakk", null })
        {
            auth.feedbackText.text = "UNCHANGED";
            success.Invoke(auth, new object[] { new RegisterPlayFabUserResult(), name });
            PlayerNamePolicy.TryValidate(name, out string error);
            CheckAuthFeedback(auth, error, authEvents);
        }
        Check(auth.loginUsername.text == "LOGIN UNCHANGED" &&
            auth.registerPassword.text == "Validation123" && auth.registerConfirmPassword.text == "Validation123",
            "A rejected registration callback changed login or password fields.");
        report.AppendLine("PASS: Actual PlayFab account-registration, display-name update and rejected callback paths report the policy error before any SDK request or authentication event; valid email/password fields do not bypass rejection.");
    }

    private static void CheckRejectedAuth(PlayFabAuthManager auth, string name, string error, ref int events)
    {
        auth.registerUsername.text = name;
        auth.feedbackText.text = "UNCHANGED";
        auth.OnRegisterButtonClicked();
        CheckAuthFeedback(auth, error, events);
        auth.feedbackText.text = "UNCHANGED";
        auth.SubmitNewDisplayName(name);
        CheckAuthFeedback(auth, error, events);
    }

    private static void CheckAuthFeedback(PlayFabAuthManager auth, string error, int events)
    {
        Check(auth.feedbackText.text == error && auth.feedbackText.color.Equals(auth.errorColor),
            "An invalid auth name did not show the expected validation feedback.");
        Check(auth.loggedInPlayerName == "IDENTITY UNCHANGED" && auth.isGuest && events == 0,
            "An invalid auth name changed player identity or fired an authentication event.");
    }

    private static GameObject Rect(string name, Transform parent)
    {
        var obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        return obj;
    }

    private static TextMeshProUGUI Label(string name, Transform parent, string value)
    {
        var label = Rect(name, parent).AddComponent<TextMeshProUGUI>();
        label.text = value;
        return label;
    }

    private static TMP_InputField Input(string name, Transform parent)
    {
        var obj = Rect(name, parent);
        var input = obj.AddComponent<TMP_InputField>();
        input.textViewport = (RectTransform)Rect("Viewport", obj.transform).transform;
        input.textComponent = Label("Text", input.textViewport, string.Empty);
        input.textComponent.richText = false;
        input.richText = false;
        return input;
    }

    private static FieldInfo Field(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(name, PrivateInstance);
        Check(field != null, "Regression field was not found: " + name);
        return field;
    }

    private static T Get<T>(object target, string name) => (T)Field(target, name).GetValue(target);
    private static void Set(object target, string name, object value) => Field(target, name).SetValue(target, value);

    private sealed class IdentitySnapshot
    {
        private static readonly string[] PreferenceKeys =
        {
            "SavedPlayerName", "LoginChoice", "RememberedPlayFabId", "DeviceLinkedPlayFabId", "LegacyProgressPrefsOwner"
        };
        private readonly PlayFabAuthManager auth = PlayFabAuthManager.Instance;
        private readonly PlayerDataManager player = PlayerDataManager.Instance;
        private readonly string authName;
        private readonly bool guest;
        private readonly object playerData;
        private readonly string playerJson;
        private readonly string savePath;
        private readonly string[] saveStamps;
        private readonly bool[] preferencesPresent = new bool[PreferenceKeys.Length];
        private readonly string[] preferenceStrings = new string[PreferenceKeys.Length];
        private readonly int[] preferenceInts = new int[PreferenceKeys.Length];
        private readonly FieldInfo[] sdkFields = typeof(PlayFabAuthenticationContext).GetFields(BindingFlags.Instance | BindingFlags.Public);
        private readonly object[] sdkValues;

        public IdentitySnapshot()
        {
            authName = auth != null ? auth.loggedInPlayerName : null;
            guest = auth != null && auth.isGuest;
            playerData = player != null ? player.CurrentData : null;
            playerJson = player != null ? player.GetCurrentDataJson() : null;
            savePath = player != null ? player.CurrentSavePath : null;
            saveStamps = new[] { Stamp(savePath), Stamp(Suffixed(savePath, ".bak")), Stamp(Suffixed(savePath, ".tmp")) };
            sdkValues = new object[sdkFields.Length];
            for (int index = 0; index < sdkFields.Length; index++)
                sdkValues[index] = sdkFields[index].GetValue(PlayFabSettings.staticPlayer);
            for (int index = 0; index < PreferenceKeys.Length; index++)
            {
                preferencesPresent[index] = PlayerPrefs.HasKey(PreferenceKeys[index]);
                preferenceStrings[index] = PlayerPrefs.GetString(PreferenceKeys[index], string.Empty);
                preferenceInts[index] = PlayerPrefs.GetInt(PreferenceKeys[index], 0);
            }
        }

        public void AssertUnchanged()
        {
            Check(PlayFabAuthManager.Instance == auth && PlayerDataManager.Instance == player,
                "The detached fixture changed a live player/authentication singleton.");
            Check(auth == null || auth.loggedInPlayerName == authName && auth.isGuest == guest,
                "Validation changed the loaded player's authentication identity.");
            Check(player == null || ReferenceEquals(player.CurrentData, playerData) &&
                player.GetCurrentDataJson() == playerJson && player.CurrentSavePath == savePath,
                "Validation changed the loaded player's save data or active save path.");
            Check(Stamp(savePath) == saveStamps[0] && Stamp(Suffixed(savePath, ".bak")) == saveStamps[1] &&
                Stamp(Suffixed(savePath, ".tmp")) == saveStamps[2], "Validation wrote to the loaded player's save files.");
            for (int index = 0; index < sdkFields.Length; index++)
                Check(Equals(sdkFields[index].GetValue(PlayFabSettings.staticPlayer), sdkValues[index]),
                    "Validation changed the PlayFab authentication context.");
            for (int index = 0; index < PreferenceKeys.Length; index++)
                Check(PlayerPrefs.HasKey(PreferenceKeys[index]) == preferencesPresent[index] &&
                    PlayerPrefs.GetString(PreferenceKeys[index], string.Empty) == preferenceStrings[index] &&
                    PlayerPrefs.GetInt(PreferenceKeys[index], 0) == preferenceInts[index],
                    "Validation changed a player preference: " + PreferenceKeys[index]);
        }

        private static string Suffixed(string path, string suffix) => string.IsNullOrEmpty(path) ? null : path + suffix;
        private static string Stamp(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            var file = new FileInfo(path);
            return file.Length + ":" + file.LastWriteTimeUtc.Ticks;
        }
    }

    private sealed class BlockingTransport : ITransportPlugin
    {
        public int Attempts { get; private set; }
        public bool IsInitialized => true;
        public void Initialize() => Block();
        public void Update() { }
        public void OnDestroy() { }
        public int GetPendingMessages() => 0;
        public void SimpleGetCall(string url, Action<byte[]> success, Action<string> error) => Block();
        public void SimplePutCall(string url, byte[] payload, Action<byte[]> success, Action<string> error) => Block();
        public void SimplePostCall(string url, byte[] payload, Action<byte[]> success, Action<string> error) => Block();
        public void MakeApiCall(object request) => Block();
        private void Block()
        {
            Attempts++;
            throw new InvalidOperationException("The isolated name-policy validation blocked an unexpected PlayFab request.");
        }
    }

    private static void Check(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
    }
}
