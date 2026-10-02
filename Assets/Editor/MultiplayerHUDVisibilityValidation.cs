using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public static class MultiplayerHUDVisibilityValidation
{
    private static double nextCheck;
    [InitializeOnLoadMethod]
    private static void Watch() { EditorApplication.update -= CheckRequest; EditorApplication.update += CheckRequest; }
    private static void CheckRequest()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 2;
        string request = File.Exists("Temp/challenge-lobby-framing-preparation-validation.request")
            ? "Temp/challenge-lobby-framing-preparation-validation.request" : "Temp/multiplayer-hud-visibility-validation.request";
        if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || !Resources.FindObjectsOfTypeAll<MultiplayerHUDVisibility>()
                .Any(ui => ui.gameObject.scene.name == "CanyonCrossing")) return;
        File.Delete(request);
        HostWorldBridgeSyncValidation.Validate();
    }

    public static void Run(Transform fixture, StringBuilder report)
    {
        string scene = File.ReadAllText("Assets/Scenes/CanyonCrossing.unity");
        var blocks = Regex.Matches(scene, @"(?ms)^--- !u!\d+ &(\d+)(?: stripped)?\r?\n.*?(?=^--- !u!|\z)")
            .Cast<Match>().ToDictionary(m => m.Groups[1].Value, m => m.Value);
        string controller = blocks["900000000000018000"];
        Check(controller.Contains("m_GameObject: {fileID: 591501577}") && controller.Contains("emoteButton: {fileID: 397172063}") &&
            controller.Contains("chatButton: {fileID: 1148905306}"), "HUD controller/buttons are not persistently authored.");
        string[] groupIds = Regex.Matches(controller.Split(new[] { "testHiddenGroups:" }, StringSplitOptions.None)[1], @"fileID: (\d+)")
            .Cast<Match>().Select(m => m.Groups[1].Value).ToArray();
        string[] maskedObjects = groupIds.Select(id => Regex.Match(blocks[id], @"m_GameObject: \{fileID: (\d+)").Groups[1].Value).ToArray();
        string children = Regex.Match(blocks["6234276506990522558"], @"(?s)m_Children:(.*?)m_Father:").Groups[1].Value;
        foreach (Match child in Regex.Matches(children, @"fileID: (\d+)"))
        {
            string rect = child.Groups[1].Value;
            string go = Regex.Match(blocks[rect], @"m_GameObject: \{fileID: (\d+)").Groups[1].Value;
            Check(rect == "900000000000013001" ? !maskedObjects.Contains(go) : maskedObjects.Contains(go),
                "Build UI mask is missing a branch or hides the introduction: " + go);
        }
        Check(!maskedObjects.Contains("2852104685900835928"), "Build Canvas itself must remain visible for the introduction.");

        var root = new GameObject("HUD Visibility Test (Temporary)"); root.SetActive(false); root.transform.SetParent(fixture, false);
        try
        {
            var controllerObject = new GameObject("Persistent Visibility Service"); controllerObject.transform.SetParent(root.transform);
            var ui = controllerObject.AddComponent<MultiplayerHUDVisibility>();
            var emote = new GameObject("Emote"); emote.transform.SetParent(root.transform);
            var chat = new GameObject("Chat"); chat.transform.SetParent(root.transform);
            var controls = new GameObject("Authored Controls", typeof(RectTransform), typeof(CanvasGroup)); controls.transform.SetParent(root.transform);
            var intro = new GameObject("Introduction", typeof(RectTransform), typeof(CanvasGroup)); intro.transform.SetParent(root.transform);
            CanvasGroup group = controls.GetComponent<CanvasGroup>(); group.alpha = .73f; group.interactable = false; group.blocksRaycasts = true;
            CanvasGroup introGroup = intro.GetComponent<CanvasGroup>();
            Set(ui, "emoteButton", emote); Set(ui, "chatButton", chat); Set(ui, "testHiddenGroups", new[] { group, group });
            Apply(ui, false, false);
            Check(!emote.activeSelf && !chat.activeSelf && group.alpha == .73f, "Offline buttons are shown or solo UI changes.");
            Apply(ui, true, false); Check(emote.activeSelf && chat.activeSelf, "Starting multiplayer does not restore utility buttons.");
            chat.SetActive(false); Apply(ui, true, false); Check(!chat.activeSelf, "Visibility undoes modal hiding.");
            Apply(ui, true, true);
            Check(group.alpha == 0f && !group.interactable && !group.blocksRaycasts && controls.activeSelf &&
                intro.activeSelf && introGroup.alpha == 1f, "Test masking hides the introduction or allows hidden controls to receive clicks.");
            group.alpha = 1f; group.blocksRaycasts = true; Apply(ui, true, true);
            Check(group.alpha == 0f && !group.blocksRaycasts, "A controller refresh escapes the test mask.");
            Apply(ui, true, false);
            Check(group.alpha == .73f && !group.interactable && group.blocksRaycasts, "Cleanup does not restore exact authored group flags.");
            Apply(ui, true, true);
            typeof(MultiplayerHUDVisibility).GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(ui, null);
            Check(group.alpha == .73f && !group.interactable && group.blocksRaycasts, "Disabling the service leaves a stale test mask.");
            Apply(ui, false, false); emote.SetActive(true); chat.SetActive(true); Apply(ui, false, false);
            Check(!emote.activeSelf && !chat.activeSelf, "Old modal states resurrect utility buttons offline.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        report.AppendLine("PASS: Authored HUD visibility hides Chat/Emote offline, restores on session start, respects modal hiding, masks every Build Canvas branch except the introduction during multiplayer test phases, blocks hidden input, and restores exact alpha/input flags on completion/disconnect/disable. Solo controls are unchanged.");
    }
    private static void Apply(MultiplayerHUDVisibility ui, bool online, bool testing) =>
        typeof(MultiplayerHUDVisibility).GetMethod("ApplyVisibility", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(ui, new object[] { online, testing });
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
    private static void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
}
