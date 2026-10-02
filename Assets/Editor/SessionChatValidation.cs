using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using TMPro;

public static class SessionChatValidation
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
        string request = File.Exists("Temp/session-chat-highlight-validation.request")
            ? "Temp/session-chat-highlight-validation.request" : "Temp/session-chat-validation.request";
        if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || !Resources.FindObjectsOfTypeAll<SessionChatUI>()
                .Any(ui => ui.gameObject.scene.name == "CanyonCrossing")) return;
        File.Delete(request);
        HostWorldBridgeSyncValidation.Validate();
    }

    public static void Run(Transform fixture, StringBuilder report)
    {
        string scene = File.ReadAllText("Assets/Scenes/CanyonCrossing.unity");
        Func<string, string> block = id => Regex.Match(scene, @"(?ms)^--- !u!\d+ &" + id + @"\r?\n.*?(?=^--- !u!|\z)").Value;
        string service = block("900000000000017000");
        foreach (string field in new[] { "panel", "openButton", "messageInput", "messages", "messageScroll", "sendButton", "status", "unreadBadge" })
        {
            string id = Regex.Match(service, @"(?m)^  " + field + @": \{fileID: (\d+)\}").Groups[1].Value;
            Check(id.Length > 0 && block(id).Length > 0, "Missing authored chat reference: " + field);
            if (field == "messageInput")
            {
                Check(block(id).Contains("m_CharacterLimit: 280") && block(id).Contains("m_RichText: 0"), "Chat input limit/markup settings incorrect.");
                foreach (string reference in new[] { "m_TextViewport", "m_TextComponent", "m_Placeholder" })
                {
                    string target = Regex.Match(block(id), @"(?m)^  " + reference + @": \{fileID: (\d+)\}").Groups[1].Value;
                    Check(target.Length > 0 && block(target).Length > 0, "Unbound chat input caret/text/placeholder.");
                }
                Check(block(id).Contains("m_MethodName: SubmitInput"), "Enter isn't wired to chat submission.");
            }
            if (field == "messages") Check(block(id).Contains("m_isRichText: 1"), "Authored sender highlight is disabled.");
        }
        Check(block("1148905308").Contains("m_Target: {fileID: 900000000000017000}") &&
            block("1148905308").Contains("m_MethodName: Open"), "The user's Chat button isn't persistently wired.");
        var history = new SessionChatHistory();
        Check(!history.Add(1, "Engineer", "  \n\t ") && history.Count == 0, "Empty chat is accepted.");
        Check(history.Add(1, "Alice", "hello") && !history.Add(1, "Alice", "duplicate") && !history.Add(0, "Alice", "late"),
            "Duplicate or late broadcast is displayed more than once.");
        Check(history.Add(2, "Bob", "<color=red>hi</color>") && history.Text.Contains("<noparse><</noparse>color=red>"), "Chat text isn't escaped before trusted sender highlighting.");
        for (int i = 3; i <= 80; i++) history.Add(i, "Engineer", "Message " + i);
        Check(history.Count == SessionChatHistory.HistoryLimit && history.Text.Contains("Message 80") && !history.Text.Contains("hello"), "Chat history is unbounded or newest message is lost.");
        history.Clear(); Check(history.Count == 0 && history.Add(1, "New session", "Hello again"), "New room retains old history/sequence.");
        Check(SessionChatHistory.Normalize(new string('A', 400)).Length == 280 &&
            SessionChatHistory.Normalize("hello\nworld\t!\0") == "hello world !", "Length/control-character bounds aren't enforced.");
        Check(SessionChatHistory.Normalize(new string('A', 279) + "😀").Length == 279 && SessionChatHistory.Normalize("Hello 😀").Contains("😀"),
            "Message limit splits an emoji or valid Unicode isn't preserved.");
        string network = File.ReadAllText("Assets/Script/FusionMultiplayerAvatar.Chat.cs");
        Check(network.Contains("RpcSources.InputAuthority, RpcTargets.StateAuthority") && network.Contains("RpcSources.StateAuthority, RpcTargets.All") &&
            network.Contains("player != Object") && network.Contains("NormalizeMapPlayerName(MapPlayerName)") && network.Contains("nextServerChatTime"),
            "Chat bypasses ownership, authoritative names, host sequencing or server flood control.");
        var temp = new GameObject("Chat Offline Controls (Temporary)"); temp.SetActive(false); temp.transform.SetParent(fixture, false);
        try
        {
            var ui = temp.AddComponent<SessionChatUI>(); ui.Open(); ui.Send(); ui.SubmitInput("hello"); ui.Close();
            Check(!SessionChatUI.IsOpen, "Offline chat controls capture gameplay or send traffic.");
        }
        finally { UnityEngine.Object.DestroyImmediate(temp); }
        report.AppendLine("PASS: Chat names render bold gold/brown without colouring message bodies; adversarial name/body markup (including closing noparse tags) displays literally. Chat button, Send/Enter/Close, bounded escaped-text history, TMP viewport/text/placeholder and unread badge are authored and wired. Empty/oversize/control-character/Unicode/duplicate/late/history-reset cases pass; host-owned broadcast and avatar-owned requests enforce names/rate/authority. Offline controls cannot send or capture gameplay. Live host/guest RPC delivery still needs a two-player play test.");
    }

    public static void FormatPreview(GameObject panel)
    {
        var texts = panel.GetComponentsInChildren<TMP_Text>(true);
        TMP_Text messages = texts.First(t => t.name == "Chat Messages");
        messages.richText = true;
        const string hostileName = "<color=red>Alice</color>";
        const string hostileBody = "</noparse><size=999><color=red>hello</color> <b>text</b>";
        messages.text = SessionChatHistory.FormatLine(hostileName, hostileBody);
        messages.ForceMeshUpdate(true, true);
        Check(messages.GetParsedText() == hostileName + ":\n" + hostileBody, "Typed name/body markup is interpreted instead of displayed literally.");
        Color32 accent = new Color32(152, 96, 28, 255);
        Check(messages.textInfo.characterInfo[0].color.Equals(accent) &&
            (messages.textInfo.characterInfo[0].style & FontStyles.Bold) != 0,
            "Sender name isn't bold gold/brown.");
        int bodyIndex = hostileName.Length + 2;
        Check(!messages.textInfo.characterInfo[bodyIndex].color.Equals(accent) &&
            (messages.textInfo.characterInfo[bodyIndex].style & FontStyles.Bold) == 0 &&
            messages.textInfo.characterInfo[bodyIndex].pointSize < 100f,
            "Sender highlight or typed markup leaks into message body.");
        messages.text = SessionChatHistory.FormatLine("hyakkimaru", "Ready to build the bridge?") + "\n\n" +
            SessionChatHistory.FormatLine("javen", "Yes! Let's try the factory bridge.") + "\n\n" +
            SessionChatHistory.FormatLine("hyakkimaru", "Watch the live load weight before submitting.");
        var input = panel.GetComponentInChildren<TMP_InputField>(true);
        input.SetTextWithoutNotify("Sounds good!"); input.ForceLabelUpdate();
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
