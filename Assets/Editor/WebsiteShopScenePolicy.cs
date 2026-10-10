using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>Read-only checks of saved scene assets; no scene authoring or account access.</summary>
public static class WebsiteShopScenePolicy
{
    public const string ShopScriptGuid = "3b56a5c71a29a37468d89a236c591e4d";
    public const string DialogScriptGuid = "bbb12f70db5e4ba3bcd76ac455b82943";
    public const string DialogPrefabGuid = "2a540a6fab2ee104b9fd20216350a9ca";
    public const string ButtonScriptGuid = "4e29b1a8efbd4b44bb3f3716e73f07ff";
    public static readonly string[] RequiredScenes = {
        "Assets/Scenes/Main Menu.unity", "Assets/Scenes/BHAN HOUSE.unity",
        "Assets/Scenes/CanyonCrossing.unity", "Assets/Scenes/Multiplayer/Multiplayer.unity"
    };

    public sealed class Document
    {
        public string Id;
        public string Type;
        public string Text;
    }

    public static List<Document> ReadDocuments(string text)
    {
        string normalized = text.Replace("\r\n", "\n");
        MatchCollection starts = Regex.Matches(normalized, @"^--- !u!(\d+) &(-?\d+)(?: stripped)?\n", RegexOptions.Multiline);
        var documents = new List<Document>();
        for (int index = 0; index < starts.Count; index++)
        {
            Match start = starts[index];
            int end = index + 1 < starts.Count ? starts[index + 1].Index : normalized.Length;
            documents.Add(new Document {
                Id = start.Groups[2].Value, Type = start.Groups[1].Value,
                Text = normalized.Substring(start.Index, end - start.Index)
            });
        }
        return documents;
    }

    public static string Reference(Document document, string field)
    {
        Match match = Regex.Match(document.Text, @"^  " + Regex.Escape(field) + @": \{fileID: (-?\d+)(?:,|\})", RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value : "0";
    }

    public static string Name(Document document)
    {
        Match match = Regex.Match(document.Text, @"^  m_Name: (.*)$", RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value : "";
    }

    public static List<string> CollectErrors(string sceneText, string prefabText)
    {
        var errors = new List<string>();
        List<Document> documents = ReadDocuments(sceneText);
        if (documents.Count == 0) { errors.Add("Scene is not a saved Unity text asset."); return errors; }
        if (documents.GroupBy(document => document.Id).Any(group => group.Count() != 1))
        { errors.Add("Scene contains duplicate object IDs."); return errors; }
        var byId = documents.ToDictionary(document => document.Id);
        var transforms = documents.Where(document => document.Type == "224" || document.Type == "4")
            .Select(document => new { ObjectId = Reference(document, "m_GameObject"), Transform = document })
            .Where(entry => entry.ObjectId != "0").GroupBy(entry => entry.ObjectId)
            .ToDictionary(group => group.Key, group => group.First().Transform);
        List<Document> shops = documents.Where(document => HasScript(document, ShopScriptGuid)).ToList();
        if (shops.Count == 0) errors.Add("No authored ShopManager is saved in this required scene.");
        foreach (Document shop in shops)
        {
            string prefix = "ShopManager " + shop.Id + ": ";
            if (!byId.TryGetValue(Reference(shop, "currencyShopDialog"), out Document dialog) || !HasScript(dialog, DialogScriptGuid))
                errors.Add(prefix + "currencyShopDialog is missing or is not an authored dialog.");
            else if (!byId.TryGetValue(Reference(dialog, "m_PrefabInstance"), out Document instance) ||
                     !instance.Text.Contains("m_SourcePrefab: {fileID: 100100000, guid: " + DialogPrefabGuid + ","))
                errors.Add(prefix + "dialog does not resolve to the reviewed AccountSaveChoiceDialog prefab.");
            string shopObject = Reference(shop, "m_GameObject");
            CheckButton("AddCurrencyButton", "HandleAddCurrencyClicked");
            CheckButton("AddDiamondsButton", "HandleAddDiamondsClicked");
            CheckButton("RefreshCurrencyButton", "RefreshWalletBalances");

            void CheckButton(string controlName, string method)
            {
                List<Document> controls = documents.Where(document => document.Type == "1" && Name(document) == controlName &&
                    IsUnder(document.Id, shopObject, transforms, byId)).ToList();
                if (controls.Count != 1) { errors.Add(prefix + controlName + " must occur exactly once under this shop."); return; }
                Document control = controls[0];
                List<Document> buttons = documents.Where(document => HasScript(document, ButtonScriptGuid) && Reference(document, "m_GameObject") == control.Id).ToList();
                if (buttons.Count != 1 || !HasActiveCall(buttons[0], shop.Id, method))
                    errors.Add(prefix + controlName + " has no enabled persistent " + method + " callback targeting this shop.");
                if (!control.Text.Contains("  m_IsActive: 1\n")) errors.Add(prefix + controlName + " is saved inactive.");
                if (buttons.Count == 1 && (!buttons[0].Text.Contains("  m_Enabled: 1\n") || !buttons[0].Text.Contains("  m_Interactable: 1\n")))
                    errors.Add(prefix + controlName + " is disabled or not interactable.");
            }
        }
        List<Document> prefab = ReadDocuments(prefabText);
        Document prefabDialog = prefab.SingleOrDefault(document => HasScript(document, DialogScriptGuid));
        if (prefabDialog == null) errors.Add("Reviewed dialog prefab has no unique AuthoredSaveChoiceDialog.");
        else foreach (string field in new[] { "titleText", "messageText", "leftLabel", "rightLabel", "leftButton", "rightButton" })
        {
            string id = Reference(prefabDialog, field);
            if (id == "0" || !prefab.Any(document => document.Id == id)) errors.Add("Dialog prefab " + field + " reference is missing.");
        }
        return errors;
    }

    private static bool HasScript(Document document, string guid) => document.Type == "114" &&
        document.Text.Contains("m_Script: {fileID: 11500000, guid: " + guid + ",");

    private static bool HasActiveCall(Document button, string shopId, string method)
    {
        foreach (string call in Regex.Split(button.Text, @"(?=^      - m_Target: )", RegexOptions.Multiline).Skip(1))
            if (call.StartsWith("      - m_Target: {fileID: " + shopId + "}", StringComparison.Ordinal) &&
                call.Contains("        m_MethodName: " + method + "\n") &&
                Regex.IsMatch(call, @"^        m_CallState: [12]$", RegexOptions.Multiline)) return true;
        return false;
    }

    private static bool IsUnder(string gameObject, string ancestor, Dictionary<string, Document> transforms, Dictionary<string, Document> byId)
    {
        var visited = new HashSet<string>();
        while (gameObject != "0" && visited.Add(gameObject))
        {
            if (gameObject == ancestor) return true;
            if (!transforms.TryGetValue(gameObject, out Document transform) ||
                !byId.TryGetValue(Reference(transform, "m_Father"), out Document parent)) return false;
            gameObject = Reference(parent, "m_GameObject");
        }
        return false;
    }
}
