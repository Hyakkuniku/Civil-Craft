using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Detached logout regressions: never authenticates, signs out, or changes PlayerPrefs.</summary>
public static class LogoutConfirmationValidation
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
    private const string Request = "Temp/logout-confirmation-regression-v1.request";
    private const string Report = "Temp/LogoutConfirmationRegressionValidation.txt";
    private static double nextCheck;

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
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Request);
        try
        {
            File.WriteAllText(Report, Run());
            Debug.Log("[Logout confirmation] Isolated cancel/focus/back regressions passed. Authored scenes were not modified.");
        }
        catch (Exception error)
        {
            File.WriteAllText(Report, "FAIL: " + error);
            Debug.LogException(error);
        }
    }

    /// <summary>Read-only validation of an authored scene instance before it is saved.</summary>
    public static string ValidateAuthored(SettingsManager manager)
    {
        Check(manager != null && manager.logoutConfirmation != null && manager.logoutStayButton != null,
            "Logout confirmation or safe Stay reference is missing.");
        AuthoredSaveChoiceDialog dialog = manager.logoutConfirmation;
        Check(!dialog.gameObject.activeSelf, "Authored logout confirmation must start inactive.");
        Check(manager.settingsPanel != null && dialog.transform.IsChildOf(manager.settingsPanel.transform),
            "Logout confirmation must belong to its Settings panel.");
        foreach (string field in new[] { "titleText", "messageText", "leftLabel", "rightLabel", "leftButton", "rightButton" })
        {
            Component reference = Get<Component>(dialog, field);
            Check(reference != null && reference.transform.IsChildOf(dialog.transform),
                "Authored logout dialog reference is missing or belongs to another panel: " + field);
        }
        Button stay = Get<Button>(dialog, "leftButton");
        Button confirm = Get<Button>(dialog, "rightButton");
        Check(stay != confirm && manager.logoutStayButton == stay,
            "The safe default must be the left Stay button, not Log Out.");
        Check(Get<TMP_Text>(dialog, "leftLabel").text == "STAY" &&
            Get<TMP_Text>(dialog, "rightLabel").text == "LOG OUT", "Logout action labels are reversed or incorrect.");
        Check(stay.onClick.GetPersistentEventCount() == 0 && confirm.onClick.GetPersistentEventCount() == 0,
            "Logout dialog contains persistent callbacks that could bypass its pending guard.");
        Check(manager.logoutButton != null, "The existing Log Out button is missing.");
        for (int i = 0; i < manager.logoutButton.onClick.GetPersistentEventCount(); i++)
        {
            string method = manager.logoutButton.onClick.GetPersistentMethodName(i);
            Check(method != nameof(SettingsManager.ConfirmLogout) && method != nameof(PlayFabAuthManager.LogoutToSignedOutState),
                "Existing Log Out button bypasses the confirmation.");
        }
        return "PASS: " + manager.gameObject.scene.name + " has inactive, fully referenced authored Stay/Log Out controls with no direct logout bypass.";
    }

    /// <summary>Runs synchronously in Edit Mode; all fixture objects are removed before returning.</summary>
    public static string Run()
    {
        Check(!EditorApplication.isPlayingOrWillChangePlaymode, "Stop Play Mode before isolated logout checks.");
        Check(!Object.FindObjectsOfType<SettingsManager>(true).Any(manager => manager.IsLogoutConfirmationOpen),
            "Close any existing logout confirmation before isolated checks.");
        var report = new StringBuilder();
        Scene previousScene = SceneManager.GetActiveScene();
        EventSystem previousEvents = EventSystem.current;
        GameObject previousSelection = previousEvents != null ? previousEvents.currentSelectedGameObject : null;
        FieldInfo backFrame = typeof(SettingsManager).GetField("logoutBackHandledFrame", PrivateStatic);
        Check(backFrame != null, "Logout Back guard field was not found.");
        int previousBackFrame = (int)backFrame.GetValue(null);
        float previousTimeScale = Time.timeScale;
        Scene fixtureScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SettingsManager fixtureManager = null;
        EventSystem fixtureEvents = null;
        try
        {
            GameObject root = new GameObject("Logout Regression Fixture (Temporary)");
            root.SetActive(false);
            SceneManager.MoveGameObjectToScene(root, fixtureScene);
            SettingsManager manager = root.AddComponent<SettingsManager>();
            fixtureManager = manager;
            manager.enabled = false; // Start/LoadSettings must never run in this check.
            PauseManager pause = root.AddComponent<PauseManager>();
            pause.enabled = false;
            EventSystem events = root.AddComponent<EventSystem>();
            fixtureEvents = events;
            GameObject settings = Rect("Settings Fixture", root.transform);
            GameObject dialogRoot = Rect("Logout Confirmation Fixture", settings.transform);
            AuthoredSaveChoiceDialog dialog = dialogRoot.AddComponent<AuthoredSaveChoiceDialog>();
            Button stay = Control("Stay", dialogRoot.transform);
            Button confirm = Control("Log Out", dialogRoot.transform);
            TMP_Text title = Label("Title", dialogRoot.transform, "LOG OUT?");
            TMP_Text message = Label("Message", dialogRoot.transform, "Saved progress stays on this device.");
            TMP_Text stayLabel = Label("Label", stay.transform, "STAY");
            TMP_Text confirmLabel = Label("Label", confirm.transform, "LOG OUT");
            dialog.SetAuthoringReferences(title, message, stayLabel, confirmLabel, stay, confirm);
            dialogRoot.SetActive(false);
            manager.settingsPanel = settings;
            manager.logoutConfirmation = dialog;
            manager.logoutStayButton = stay;
            manager.logoutButton = Control("Existing Logout", settings.transform);
            manager.accountStatusText = Label("Account Status Sentinel", settings.transform, "UNCHANGED");
            root.SetActive(true);
            // EventSystem.current accepts only systems registered by OnEnable.
            // In Edit Mode the regular MonoBehaviour lifecycle may not run;
            // register this isolated system explicitly only when it is absent.
            if (!RegisteredEvents().Contains(events)) Invoke(events, "OnEnable");
            EventSystem.current = events;
            Check(EventSystem.current == events, "Fixture EventSystem did not become current; real scene focus was not tested.");
            report.AppendLine(ValidateAuthored(manager));

            Check(!manager.IsLogoutConfirmationOpen, "An inactive dialog is already pending.");
            manager.ConfirmLogout(); // Guard must return before auth, save, or UI refresh.
            Check(!manager.IsLogoutConfirmationOpen && !Get<bool>(manager, "logoutPending") &&
                manager.accountStatusText.text == "UNCHANGED" && manager.authManager == null,
                "Direct ConfirmLogout changed an unarmed confirmation.");
            report.AppendLine("PASS: Direct unarmed ConfirmLogout is a no-op.");

            events.SetSelectedGameObject(manager.logoutButton.gameObject);
            Set(manager, "selectionBeforeLogout", manager.logoutButton.gameObject);
            OpenFixture(manager);
            Check(manager.IsLogoutConfirmationOpen, "Fixture dialog did not become pending and visible.");
            Action right = Get<Action>(dialog, "rightAction");
            Check(right != null && ReferenceEquals(right.Target, manager) && right.Method.Name == nameof(SettingsManager.ConfirmLogout),
                "Log Out does not point to guarded ConfirmLogout.");
            Action left = Get<Action>(dialog, "leftAction");
            Check(left != null && ReferenceEquals(left.Target, manager) && left.Method.Name == nameof(SettingsManager.CancelLogout),
                "Stay does not point to CancelLogout.");
            Check(Get<TMP_Text>(dialog, "leftLabel").text == "STAY", "Safe fixture label changed.");
            // The live request requires authentication; verify its safe selection structurally,
            // without faking a login or touching the current player's authentication context.
            MonoScript settingsSource = AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/Script/SettingsScripts/SettingsManager.cs");
            Check(settingsSource != null && Regex.IsMatch(settingsSource.text,
                @"SetSelectedGameObject\s*\(\s*logoutStayButton\.gameObject\s*\)"),
                "Logout request no longer selects the safe Stay control.");
            events.SetSelectedGameObject(stay.gameObject);
            Check(events.currentSelectedGameObject == stay.gameObject, "The safe Stay control cannot receive focus.");
            stay.onClick.Invoke();
            CheckClean(manager);
            Check(events.currentSelectedGameObject == manager.logoutButton.gameObject, "Cancel did not restore previous focus.");
            manager.ConfirmLogout();
            confirm.onClick.Invoke(); // Cleared listener, not an actual logout.
            CheckClean(manager);
            Check(manager.accountStatusText.text == "UNCHANGED" && manager.authManager == null,
                "Stale confirmation refreshed or changed the account session.");
            report.AppendLine("PASS: Safe Stay selection, guarded action binding, Cancel focus restoration and stale callbacks.");

            int choices = 0;
            dialog.Show("TEST", "Temporary", "STAY", "LOG OUT", () => choices++, () => choices++);
            dialog.Show("TEST", "Temporary", "STAY", "LOG OUT", () => choices++, () => choices++);
            stay.onClick.Invoke();
            Check(choices == 1, "Repeated Show accumulated Stay callbacks.");
            dialog.Hide();
            stay.onClick.Invoke(); confirm.onClick.Invoke();
            Check(choices == 1 && Get<Action>(dialog, "leftAction") == null && Get<Action>(dialog, "rightAction") == null,
                "Hide retained action callbacks.");
            report.AppendLine("PASS: Repeated Show does not duplicate callbacks; Hide removes both callbacks.");

            OpenFixture(manager);
            settings.SetActive(false);
            Check(!manager.IsLogoutConfirmationOpen, "A hidden parent still permits confirmed logout.");
            manager.ConfirmLogout();
            Check(manager.accountStatusText.text == "UNCHANGED" && manager.authManager == null,
                "Hidden confirmation reached the account logout flow.");
            Invoke(manager, "Update"); // Returns through hidden-parent cleanup before reading input.
            CheckClean(manager);
            settings.SetActive(true);
            OpenFixture(manager);
            Invoke(manager, "OnDisable");
            CheckClean(manager);
            report.AppendLine("PASS: Hidden parent and manager disable cancel pending confirmation; hidden Confirm cannot sign out.");

            OpenFixture(manager);
            backFrame.SetValue(null, int.MinValue);
            Check(SettingsManager.TryCancelLogoutConfirmation(), "Back did not cancel the pending dialog.");
            CheckClean(manager);
            Check(SettingsManager.TryCancelLogoutConfirmation(), "The same Back frame was not consumed on its second handler.");
            MonoScript pauseSource = AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/Script/PauseManager.cs");
            Check(pauseSource != null && Regex.IsMatch(pauseSource.text,
                @"public\s+void\s+TogglePause\s*\(\s*\)\s*\{\s*if\s*\(\s*SettingsManager\.TryCancelLogoutConfirmation\s*\(\s*\)\s*\)\s*return\s*;"),
                "Pause toggle no longer consumes logout Back before other actions.");
            pause.isPaused = true;
            pause.settingsPanel = settings;
            pause.TogglePause(); // Same-frame guard returns before any Pause side effects.
            Check(pause.isPaused && settings.activeSelf && Time.timeScale == previousTimeScale,
                "The same Back press also toggled Pause or closed Settings.");
            backFrame.SetValue(null, int.MinValue);
            Check(!SettingsManager.TryCancelLogoutConfirmation(), "Back guard stayed armed after its consumed frame.");
            MethodInfo reset = typeof(SettingsManager).GetMethod("ResetLogoutBackState", PrivateStatic);
            if (reset != null)
            {
                backFrame.SetValue(null, Time.frameCount);
                reset.Invoke(null, null);
                Check((int)backFrame.GetValue(null) == -1, "Logout Back guard did not reset for a new play session.");
            }
            report.AppendLine("PASS: Back cancels once, is consumed by repeated handlers, and does not toggle Pause or dismiss Settings.");
            report.AppendLine("NOTE: No PlayFab login/logout, PlayerPrefs writes, Settings Start/LoadSettings, or real player save was invoked.");
        }
        finally
        {
            // Clear fixture callbacks while its own EventSystem is still selected;
            // never restore focus into a real scene from a temporary button.
            if (fixtureManager != null) fixtureManager.CancelLogout();
            if (fixtureEvents != null && RegisteredEvents().Contains(fixtureEvents)) Invoke(fixtureEvents, "OnDisable");
            backFrame.SetValue(null, previousBackFrame);
            if (Time.timeScale != previousTimeScale) Time.timeScale = previousTimeScale;
            EditorSceneManager.CloseScene(fixtureScene, true);
            if (previousEvents != null && RegisteredEvents().Contains(previousEvents))
            {
                EventSystem.current = previousEvents;
                if (previousEvents.currentSelectedGameObject != previousSelection)
                    previousEvents.SetSelectedGameObject(previousSelection);
            }
            if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
        }
        return report.ToString();
    }

    private static void OpenFixture(SettingsManager manager)
    {
        Check(manager.logoutConfirmation.Show("LOG OUT?", "Temporary isolated dialog", "STAY", "LOG OUT",
            manager.CancelLogout, manager.ConfirmLogout), "Fixture dialog rejected valid references.");
        Set(manager, "logoutPending", true);
    }

    private static void CheckClean(SettingsManager manager)
    {
        Check(!manager.IsLogoutConfirmationOpen && !Get<bool>(manager, "logoutPending") &&
            !manager.logoutConfirmation.gameObject.activeSelf && Get<GameObject>(manager, "selectionBeforeLogout") == null &&
            Get<Action>(manager.logoutConfirmation, "leftAction") == null &&
            Get<Action>(manager.logoutConfirmation, "rightAction") == null, "Cancel did not fully clean confirmation state.");
    }

    private static GameObject Rect(string name, Transform parent)
    {
        var target = new GameObject(name, typeof(RectTransform));
        target.transform.SetParent(parent, false);
        return target;
    }
    private static Button Control(string name, Transform parent) => Rect(name, parent).AddComponent<Button>();
    private static TMP_Text Label(string name, Transform parent, string value)
    {
        TMP_Text label = Rect(name, parent).AddComponent<TextMeshProUGUI>();
        label.text = value;
        return label;
    }
    private static T Get<T>(object target, string name) => (T)Field(target, name).GetValue(target);
    private static void Set(object target, string name, object value) => Field(target, name).SetValue(target, value);
    private static FieldInfo Field(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(name, PrivateInstance);
        Check(field != null, "Regression field not found: " + name);
        return field;
    }
    private static void Invoke(object target, string name)
    {
        MethodInfo method = target.GetType().GetMethod(name, PrivateInstance);
        Check(method != null, "Regression method not found: " + name);
        method.Invoke(target, null);
    }
    private static IList RegisteredEvents()
    {
        FieldInfo field = typeof(EventSystem).GetField("m_EventSystems", PrivateStatic);
        Check(field != null, "UGUI EventSystem registration list was not found.");
        return (IList)field.GetValue(null);
    }
    private static void Check(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
    }
}
