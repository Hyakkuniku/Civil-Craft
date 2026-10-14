using System.Text.RegularExpressions;

internal static partial class Program
{
    private static void TestSourceShipping()
    {
        string service = File.ReadAllText(Path.Combine(Root, "Assets/Script/Client/GameReleaseService.cs"));
        string models = File.ReadAllText(Path.Combine(Root, "Assets/Script/Client/GameReleaseModels.cs"));
        string controller = File.ReadAllText(Path.Combine(Root, "Assets/Script/UI/MainMenuUpdatesController.cs"));
        string production = service + models + controller;
        foreach (string forbidden in new[] { "PlayFabClientAPI", "PlayFabServerAPI", "PlayFabAdminAPI", "CloudSaveManager", "PlayerDataManager", "GameWalletService", "GameWalletPolicy", "ClientSessionTicket", "SessionTicket", "PlayerPrefs" })
            Check(!production.Contains(forbidden, StringComparison.Ordinal), "Release reader is independent from account/wallet/save authority: " + forbidden);
        Check(!Regex.IsMatch(production, @"SetRequestHeader\s*\(\s*""(?:Authorization|Cookie|X-Authorization|X-SecretKey)""", RegexOptions.IgnoreCase), "Public release fetch never attaches account headers or secrets.");
        Check(service.Contains("Application.persistentDataPath") && service.Contains("public-release-cache.json"), "Release cache is a separate public-data file outside player saves and wallet journals.");
        Check(!service.Contains("WalletJournals") && !service.Contains("SaveEncryption"), "Public release cache is never inserted into private wallet recovery storage.");
        Check(!service.Contains("Application.OpenURL"), "Fetching release data never opens a browser automatically.");
        Check(production.Contains("getLongVersionCode") && production.Contains("versionCode") && production.Contains("SDK_INT"), "Android installed-code reader uses actual package version with old-device fallback.");
        Check(production.Contains("UnityEditor.PlayerSettings.Android.bundleVersionCode") || production.Contains("PlayerSettings.Android.bundleVersionCode"), "Editor installed version uses project Bundle Version Code.");
        Check(controller.Contains("richText = false"), "Remote TextMeshPro content is displayed without interpreting rich markup.");
        Check(controller.Contains("UIPanelCoordinator") && controller.Contains("OpenPanel") && controller.Contains("ClosePanel"), "Updates modal uses existing menu state coordinator.");
        Check(controller.Contains("escapeKey"), "Escape and Android Back input close the panel.");
        Check(controller.Contains("Application.OpenURL(GameReleasePolicy.DownloadUrl)"), "User Download opens only the fixed website download page, never a remote API-supplied URL.");
        foreach (string forbidden in new[] { "Process.Start", "File.WriteAllBytes", ".apk", "INSTALL_PACKAGES", "REQUEST_INSTALL_PACKAGES", "setDataAndType", "android.intent.action.VIEW" })
            Check(!production.Contains(forbidden, StringComparison.Ordinal), "Release feature does not implement direct APK download/installation: " + forbidden);

        string meta = File.ReadAllText(Path.Combine(Root, "Assets/Script/UI/MainMenuUpdatesController.cs.meta")).Replace("\r\n", "\n");
        Match guid = Regex.Match(meta, @"^guid: ([0-9a-f]{32})$", RegexOptions.Multiline);
        Check(guid.Success, "Updates controller has valid Unity metadata.");
        string scene = File.ReadAllText(Path.Combine(Root, "Assets/Scenes/Main Menu.unity"));
        string scriptReference = "guid: " + guid.Groups[1].Value;
        Check(Regex.Matches(scene, Regex.Escape(scriptReference)).Count == 1, "Saved main menu contains exactly one Updates controller, no runtime duplicate UI.");
        foreach (string name in new[] { "UpdatesButton", "UpdatesPanel", "NEW UPDATE" })
            Check(scene.Contains(name, StringComparison.Ordinal), "Saved main menu ships authored Updates UI: " + name);
        string controllerDocument = DocumentContaining(scene, scriptReference);
        foreach (string field in new[] { "updatesButton", "closeButton", "retryButton", "downloadButton", "releasesTabButton", "messagesTabButton", "panel", "badge", "releaseTitleText", "releaseDateText", "notesText", "installedVersionText", "availableVersionText", "statusText", "notesScroll" })
            Check(ReadReference(controllerDocument, field) != "0", "Saved Updates controller has a non-null authored reference: " + field);
        string managerId = ReadReference(controllerDocument, "m_GameObject");
        string manager = DocumentById(scene, managerId);
        Check(manager.Contains("m_Name: MainMenuUpdatesManager") && manager.Contains("m_IsActive: 1"), "Updates controller lives on an always-active dedicated main-menu manager.");
        foreach (string type in new[] { "GameReleaseService", "MainMenuUpdatesLayout" })
        {
            string metadata = File.ReadAllText(Path.Combine(Root, type == "GameReleaseService" ? "Assets/Script/Client/" : "Assets/Script/UI/", type + ".cs.meta")).Replace("\r\n", "\n");
            string scriptGuid = Regex.Match(metadata, @"^guid: ([0-9a-f]{32})$", RegexOptions.Multiline).Groups[1].Value;
            Check(Regex.Matches(scene, Regex.Escape("guid: " + scriptGuid)).Count == 1, "Exactly one authored Updates companion component ships: " + type);
            Check(ReadReference(DocumentContaining(scene, "guid: " + scriptGuid), "m_GameObject") == managerId, "Updates companion is wired on the same manager: " + type);
        }
        foreach (string hidden in new[] { "panel", "badge" })
            Check(DocumentById(scene, ReadReference(controllerDocument, hidden)).Contains("m_IsActive: 0"), "Updates modal/badge begins hidden before any fetch: " + hidden);
        string entryComponent = DocumentById(scene, ReadReference(controllerDocument, "updatesButton"));
        string entry = DocumentById(scene, ReadReference(entryComponent, "m_GameObject"));
        Check(entry.Contains("m_IsActive: 1"), "Updates entry is available before login without runtime activation.");
    }

    private static string DocumentContaining(string source, string content)
    {
        int index = source.IndexOf(content, StringComparison.Ordinal);
        Check(index >= 0, "Expected saved Unity document contains: " + content);
        int start = source.LastIndexOf("--- !u!", index, StringComparison.Ordinal);
        int end = source.IndexOf("--- !u!", index, StringComparison.Ordinal);
        Check(start >= 0, "Saved Unity component has a document header.");
        return source.Substring(start, (end < 0 ? source.Length : end) - start);
    }
    private static string DocumentById(string source, string id)
    {
        Match header = Regex.Match(source, @"(?m)^--- !u!\d+ &" + Regex.Escape(id) + @"\r?$", RegexOptions.CultureInvariant);
        Check(header.Success, "Saved Unity fileID target exists: " + id);
        int end = source.IndexOf("--- !u!", header.Index + header.Length, StringComparison.Ordinal);
        return source.Substring(header.Index, (end < 0 ? source.Length : end) - header.Index);
    }
    private static string ReadReference(string document, string field)
    {
        Match match = Regex.Match(document, @"(?m)^  " + Regex.Escape(field) + @": \{fileID: (-?\d+)\}\r?$", RegexOptions.CultureInvariant);
        Check(match.Success, "Expected serialized Unity reference exists: " + field);
        return match.Groups[1].Value;
    }
}
