using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Fusion;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Isolated checks for the scene-authored host-world session panel.</summary>
public static class WorldMultiplayerPanelValidation
{
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
        const string request = "Temp/world-session-ui-validation.request";
        if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || !Resources.FindObjectsOfTypeAll<WorldMultiplayerPanelUI>()
                .Any(ui => ui.gameObject.scene.name == "CanyonCrossing")) return;
        File.Delete(request);
        HostWorldBridgeSyncValidation.Validate();
    }

    public static void Run(Transform fixture, StringBuilder report)
    {
        string scene = File.ReadAllText("Assets/Scenes/CanyonCrossing.unity");
        Func<string, string> block = id => Regex.Match(scene, @"(?ms)^--- !u!\d+ &" + id + @"\r?\n.*?(?=^--- !u!|\z)").Value;
        string service = block("900000000000015000");
        foreach (string field in new[] { "panel", "openButton", "turnOnButton", "turnOnLabel", "statusText", "codeCard", "codeText",
            "hostRow", "hostName", "guestRow", "guestName", "waitingLabel", "kickButton", "kickConfirmation", "kickGuestName", "confirmKickButton",
            "turnOffButton", "turnOffLabel", "turnOffConfirmation", "confirmTurnOffButton",
            "leaveButton", "leaveLabel", "leaveConfirmation", "confirmLeaveButton" })
        {
            string id = Regex.Match(service, @"(?m)^  " + field + @": \{fileID: (\d+)\}").Groups[1].Value;
            Check(id.Length > 0 && block(id).Length > 0, "Missing authored world session reference: " + field);
            // The outer panel may be visible while authoring. Awake hides it before play;
            // keep the guest row and destructive confirmations hidden in the saved scene.
            if (field == "guestRow" || field == "kickConfirmation" || field == "turnOffConfirmation" || field == "leaveConfirmation")
                Check(block(id).Contains("m_IsActive: 0"), "World session panel/guest/confirmation appears before opening/joining.");
            if (field == "hostName" || field == "guestName" || field == "kickGuestName")
                Check(block(id).Contains("m_isRichText: 0"), "Session IGNs can inject TMP markup.");
        }
        Check(block("885150612").Contains("m_Target: {fileID: 900000000000015000}") && block("885150612").Contains("m_MethodName: Open"),
            "The user's existing MultiplayerButton is not wired to the authored panel.");
        string subtree = string.Join("\n", Regex.Matches(scene, @"(?ms)^--- !u!\d+ &900000000000015\d+\r?\n.*?(?=^--- !u!|\z)").Cast<Match>().Select(m => m.Value));
        foreach (string method in new[] { "Open", "Close", "TurnOnMultiplayer", "RequestKick", "ConfirmKick", "CancelKick",
            "RequestTurnOff", "ConfirmTurnOff", "CancelTurnOff", "RequestLeave", "ConfirmLeave", "CancelLeave" })
            Check(scene.Contains("m_MethodName: " + method) && typeof(WorldMultiplayerPanelUI).GetMethod(method) != null,
                "Missing world session button method " + method);
        Check(subtree.Contains("m_Color: {r: 0.69, g: 0.22, b: 0.16, a: 1}") &&
            subtree.Contains("m_Name: Session Wood Frame") && subtree.Contains("m_Name: Session Cream Panel"),
            "World session theme/red kick action do not match the authored design.");
        var policy = typeof(FusionConnectionManager).GetMethod("CanKickPlayer", BindingFlags.Static | BindingFlags.NonPublic);
        int checks = 0;
        foreach (bool server in new[] { false, true })
            foreach (bool connected in new[] { false, true })
                foreach (PlayerRef target in new[] { PlayerRef.None, PlayerRef.FromIndex(0), PlayerRef.FromIndex(1), PlayerRef.FromIndex(2) })
                {
                    bool actual = (bool)policy.Invoke(null, new object[] { server, PlayerRef.FromIndex(0), target, connected });
                    Check(actual == (server && connected && target != PlayerRef.None && target != PlayerRef.FromIndex(0)),
                        "Kick policy permits a guest, host/self, disconnected or invalid target."); checks++;
                }
        var obj = new GameObject("World Session Controls (Temporary)"); obj.SetActive(false); obj.transform.SetParent(fixture, false);
        try
        {
            var controller = obj.AddComponent<WorldMultiplayerPanelUI>();
            var panel = new GameObject("Session Panel (Temporary)"); panel.transform.SetParent(obj.transform, false); panel.SetActive(false);
            var confirm = new GameObject("Kick Confirmation (Temporary)"); confirm.transform.SetParent(panel.transform, false); confirm.SetActive(false);
            typeof(WorldMultiplayerPanelUI).GetField("panel", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, panel);
            typeof(WorldMultiplayerPanelUI).GetField("kickConfirmation", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, confirm);
            controller.Open(); Check(!panel.activeSelf, "A foreign scene can open world hosting controls.");
            controller.ConfirmKick(); Check(!confirm.activeSelf, "Unopened kick confirmation altered session state.");
            confirm.SetActive(true); controller.CancelKick(); Check(!confirm.activeSelf, "Cancel did not close kick confirmation.");
            controller.Close(); Check(!panel.activeSelf, "Close did not dismiss the session panel.");
            var connection = obj.AddComponent<FusionConnectionManager>();
            Check(!connection.TryKickGuest(PlayerRef.FromIndex(1), null), "An offline manager can kick a player.");
            bool rejected = false;
            try { connection.StopHostingWorldAsync(); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "An offline/guest manager can shut down a host world.");
            rejected = false;
            try { connection.LeaveHostWorldAsync(); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "An offline manager can start a guest world exit.");
            var pendingRunnerObject = new GameObject("Uninitialized Runner (Temporary)");
            pendingRunnerObject.SetActive(false); pendingRunnerObject.transform.SetParent(obj.transform, false);
            var pendingRunner = pendingRunnerObject.AddComponent<NetworkRunner>();
            pendingRunnerObject.AddComponent<NetworkSceneManagerDefault>();
            FieldInfo runnerField = typeof(FusionConnectionManager).GetField("runner", BindingFlags.Instance | BindingFlags.NonPublic);
            runnerField.SetValue(connection, pendingRunner);
            try
            {
                rejected = false;
                try { connection.LeaveHostWorldAsync(); } catch (InvalidOperationException) { rejected = true; }
                Check(rejected, "Uninitialized runner exit queried a missing scene manager Runner.");
            }
            finally { runnerField.SetValue(connection, null); }
            var leavePolicy = typeof(FusionConnectionManager).GetMethod("CanLeaveHostWorld", BindingFlags.Static | BindingFlags.NonPublic);
            Check(leavePolicy != null, "Guest exit policy is missing.");
            foreach (bool visiting in new[] { false, true })
                foreach (bool world in new[] { false, true })
                    foreach (bool loading in new[] { false, true })
                        foreach (bool closing in new[] { false, true })
                            Check((bool)leavePolicy.Invoke(null, new object[] { visiting, world, loading, closing }) ==
                                (visiting && world && !loading && !closing), "Guest exit policy allows an invalid role/transition.");
            connection.StopSession();
            Check(connection.HostJoinCode == null && connection.Runner == null && !connection.IsSessionStopping,
                "Offline shutdown retains a stale code, runner or stopping state.");
        }
        finally { Object.DestroyImmediate(obj); }
        report.AppendLine($"PASS: World MultiplayerButton and all session/kick/turn-off controls are scene-authored, correctly wired and themed; guest/confirmation rows are hidden initially. {checks} server/self/target kick-policy cases pass; offline/unopened/cancel/foreign-scene controls do not disconnect or change saves.");
        ValidateSessionRefresh(fixture, report);
    }

    private static void ValidateSessionRefresh(Transform fixture, StringBuilder report)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var source = Resources.FindObjectsOfTypeAll<WorldMultiplayerPanelUI>().First(ui => ui.gameObject.scene.name == "CanyonCrossing");
        var obj = new GameObject("Session Refresh Regression (Temporary)");
        obj.SetActive(false); obj.transform.SetParent(fixture, false);
        try
        {
            var controller = obj.AddComponent<WorldMultiplayerPanelUI>();
            var panel = Object.Instantiate((GameObject)typeof(WorldMultiplayerPanelUI).GetField("panel", flags).GetValue(source), obj.transform, false);
            foreach (FieldInfo field in typeof(WorldMultiplayerPanelUI).GetFields(flags))
            {
                if (!field.IsDefined(typeof(SerializeField), false) || field.Name == "openButton") continue;
                Object original = (Object)field.GetValue(source);
                if (field.Name == "panel") { field.SetValue(controller, panel); continue; }
                Transform originalTransform = original is GameObject originalObject ? originalObject.transform : ((Component)original).transform;
                string path = AnimationUtility.CalculateTransformPath(originalTransform,
                    ((GameObject)typeof(WorldMultiplayerPanelUI).GetField("panel", flags).GetValue(source)).transform);
                Transform copy = panel.transform.Find(path);
                Object target = original is GameObject ? (Object)copy.gameObject : copy.GetComponent(field.FieldType);
                field.SetValue(controller, target);
            }
            Func<string, Object> get = name => (Object)typeof(WorldMultiplayerPanelUI).GetField(name, flags).GetValue(controller);
            panel.SetActive(true);
            typeof(WorldMultiplayerPanelUI).GetMethod("Awake", flags).Invoke(controller, null);
            Check(!panel.activeSelf && !((GameObject)get("kickConfirmation")).activeSelf && !((GameObject)get("turnOffConfirmation")).activeSelf && !((GameObject)get("leaveConfirmation")).activeSelf,
                "Panel/confirmations aren't hidden on startup, including when the outer panel is visible for authoring.");
            var apply = typeof(WorldMultiplayerPanelUI).GetMethod("ApplySessionView", flags);
            Action<bool, bool, bool, bool, PlayerRef, string> render = (host, guest, loading, closing, player, name) =>
                apply.Invoke(controller, new object[] { host, guest, loading, closing, player, null, host ? "ABC234" : null, name });
            render(true, false, false, false, PlayerRef.None, "");
            Check(!((Button)get("leaveButton")).gameObject.activeSelf, "Host can see the guest leave action.");
            Check(!((GameObject)get("guestRow")).activeSelf && ((GameObject)get("waitingLabel")).activeSelf,
                "Host roster doesn't show waiting before a guest joins.");
            render(true, false, false, false, PlayerRef.FromIndex(1), "Guest (connecting...)");
            Check(((GameObject)get("guestRow")).activeSelf && !((GameObject)get("waitingLabel")).activeSelf &&
                ((TMP_Text)get("statusText")).text.Contains("2 / 2") && !((Button)get("kickButton")).interactable,
                "Connected guest isn't shown before avatar initialization or Kick permits an unbound avatar.");
            render(true, false, false, false, PlayerRef.FromIndex(1), "Guest Engineer");
            Check(((TMP_Text)get("guestName")).text == "Guest Engineer" && !((TMP_Text)get("guestName")).richText,
                "Replicated IGN isn't refreshed safely while the panel is open.");
            render(true, false, false, false, PlayerRef.None, "");
            Check(!((GameObject)get("guestRow")).activeSelf && ((TMP_Text)get("statusText")).text.Contains("1 / 2"),
                "Guest disconnect leaves a stale joined row/count.");
            render(false, false, false, true, PlayerRef.None, "");
            Check(!((Button)get("turnOnButton")).gameObject.activeSelf && !((Button)get("turnOffButton")).interactable &&
                ((TMP_Text)get("codeText")).text == "NOT ACTIVE" && !((GameObject)get("guestRow")).activeSelf,
                "Closing retains a join code/guest or permits restart during shutdown.");
            render(false, false, false, false, PlayerRef.None, "");
            Check(((Button)get("turnOnButton")).gameObject.activeSelf == MultiplayerProgressionGate.IsUnlocked && !((Button)get("turnOffButton")).gameObject.activeSelf,
                "Turn On no longer follows the player's progression gate after shutdown.");
            Check(!((Button)get("leaveButton")).gameObject.activeSelf, "Offline players see a leave action.");
            render(false, true, false, false, PlayerRef.None, "");
            Check(!((Button)get("turnOnButton")).gameObject.activeSelf && !((Button)get("turnOffButton")).gameObject.activeSelf,
                "Guest can manage hosting from the world panel.");
            Check(((Button)get("leaveButton")).gameObject.activeSelf && ((Button)get("leaveButton")).interactable &&
                ((Button)get("confirmLeaveButton")).interactable, "Connected guest cannot leave the session.");
            render(false, true, true, false, PlayerRef.None, "");
            Check(!((Button)get("leaveButton")).interactable && !((Button)get("confirmLeaveButton")).interactable,
                "Guest can leave during a network scene transition.");
            render(false, true, false, true, PlayerRef.None, "");
            Check(!((Button)get("leaveButton")).interactable, "Repeated guest exits are enabled during shutdown.");
            typeof(WorldMultiplayerPanelUI).GetField("leaving", flags).SetValue(controller, true);
            render(false, false, false, true, PlayerRef.None, "");
            Check(((Button)get("leaveButton")).gameObject.activeSelf && !((Button)get("leaveButton")).interactable &&
                ((TMP_Text)get("leaveLabel")).text == "LEAVING..." && !((Button)get("turnOffButton")).gameObject.activeSelf &&
                !((GameObject)get("codeCard")).activeSelf, "Guest exit exposes host controls or permits repeat presses.");
            typeof(WorldMultiplayerPanelUI).GetField("leaving", flags).SetValue(controller, false);
            controller.RequestLeave(); controller.ConfirmLeave();
            Check(!((GameObject)get("leaveConfirmation")).activeSelf, "An unopened guest exit disconnected the session.");
            ((GameObject)get("leaveConfirmation")).SetActive(true); controller.CancelLeave();
            Check(!((GameObject)get("leaveConfirmation")).activeSelf, "Stay doesn't cancel the guest exit.");
            controller.RequestTurnOff(); controller.ConfirmTurnOff();
            Check(!((GameObject)get("turnOffConfirmation")).activeSelf, "Unopened confirmation changes session state.");
            ((GameObject)get("turnOffConfirmation")).SetActive(true); controller.CancelTurnOff();
            Check(!((GameObject)get("turnOffConfirmation")).activeSelf, "Keep On doesn't close turn-off confirmation.");
            // This used to throw every frame in the project's Input-System-only configuration,
            // preventing the roster refresh below it. Never call the legacy API in that configuration.
            typeof(WorldMultiplayerPanelUI).GetMethod("EscapePressed", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            report.AppendLine("PASS: Input-System Escape check runs without the legacy Input exception. Authored roster refreshes waiting -> connected/initializing -> replicated IGN -> guest left; closing clears code/list, disables restart until shutdown and restores Turn On. Guest/unopened controls cannot turn off hosting; Keep On cancels safely.");
            report.AppendLine("PASS: Leave Multiplayer is guest-only, disabled during scene loading/shutdown, and shows Leaving without exposing hosting controls. Stay/unopened/offline calls are safe; all 16 guest exit role/transition policy cases pass. No live room was disconnected.");
        }
        finally { Object.DestroyImmediate(obj); }
    }

    public static void FormatOnlinePreview(GameObject panel)
    {
        foreach (Transform child in panel.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == "Turn On World Multiplayer" || child.name == "Session Waiting Guest" || child.name == "WorldKickConfirmation" || child.name == "WorldTurnOffConfirmation" || child.name == "Leave World Multiplayer" || child.name == "WorldLeaveConfirmation") child.gameObject.SetActive(false);
            if (child.name == "Session Guest Row" || child.name == "Turn Off World Multiplayer") child.gameObject.SetActive(true);
        }
        foreach (TMP_Text text in panel.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text.name == "World Session Code") text.text = "ABC234";
            if (text.name == "World Session Status") text.text = "<color=#376F42><b>MULTIPLAYER ON</b></color>  •  2 / 2 PLAYERS";
            if (text.name == "Session Host Name") text.text = new string('W', 32);
            if (text.name == "Session Guest Name") text.text = new string('W', 32);
        }
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
