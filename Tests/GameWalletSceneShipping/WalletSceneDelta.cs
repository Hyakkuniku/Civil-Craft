using System.Text;
using System.Text.RegularExpressions;
using Doc = WebsiteShopScenePolicy.Document;

/// <summary>Produces a reviewed wallet-only candidate on stdout; never saves, stages or commits a scene.</summary>
static class WalletSceneDelta
{
    public static int LastAddedDocuments { get; private set; }
    public static int LastChangedDocuments { get; private set; }

    public static string Extract(string baseline, string authored, string prefab)
    {
        if (WebsiteShopScenePolicy.CollectErrors(authored, prefab).Count != 0)
            throw new InvalidOperationException("Authored scene has unresolved wallet controls; refusing to export.");
        List<Doc> original = WebsiteShopScenePolicy.ReadDocuments(baseline);
        List<Doc> working = WebsiteShopScenePolicy.ReadDocuments(authored);
        var before = original.ToDictionary(document => document.Id);
        var after = working.ToDictionary(document => document.Id);
        var added = new HashSet<string>();
        var changed = new Dictionary<string, string>();
        Doc shop = working.Single(document => document.Text.Contains("m_Script: {fileID: 11500000, guid: " + WebsiteShopScenePolicy.ShopScriptGuid + ","));
        if (!before.TryGetValue(shop.Id, out Doc oldShop)) throw new InvalidOperationException("Wallet may not replace an existing ShopManager.");
        Doc dialog = after[Ref(shop, "currencyShopDialog")];
        string instanceId = Ref(dialog, "m_PrefabInstance");
        Add(dialog.Id); Add(instanceId);
        foreach (Doc document in working.Where(document => Ref(document, "m_PrefabInstance") == instanceId)) Add(document.Id);
        string dialogLine = "  currencyShopDialog: {fileID: " + dialog.Id + "}\n";
        changed[shop.Id] = Regex.IsMatch(oldShop.Text, @"^  currencyShopDialog:", RegexOptions.Multiline)
            ? Regex.Replace(oldShop.Text, @"^  currencyShopDialog:.*\n", dialogLine, RegexOptions.Multiline)
            : ReplaceExactlyOnce(oldShop.Text, "  purchaseConfirmationPanel:", dialogLine + "  purchaseConfirmationPanel:");

        Doc diamond = Control("AddDiamondsButton");
        Doc refresh = Control("RefreshCurrencyButton");
        Doc diamondTransform = TransformFor(diamond.Id);
        Doc refreshTransform = TransformFor(refresh.Id);
        string pillId = Ref(diamondTransform, "m_Father");
        string rowId = Ref(refreshTransform, "m_Father");
        if (!before.ContainsKey(pillId) || !before.ContainsKey(rowId) || Ref(after[pillId], "m_Father") != rowId)
            throw new InvalidOperationException("Currency controls do not belong to the existing currency row.");
        AppendChild(pillId, diamondTransform.Id);
        AppendChild(rowId, refreshTransform.Id);
        Doc amount = after[Ref(shop, "secondaryCurrencyText")];
        Doc amountTransform = TransformFor(Ref(amount, "m_GameObject"));
        if (Ref(amountTransform, "m_Father") != pillId || !before.TryGetValue(amountTransform.Id, out Doc oldAmount))
            throw new InvalidOperationException("Cannot identify the existing Diamond amount layout.");
        string amountText = oldAmount.Text;
        foreach (string field in new[] { "m_AnchorMax", "m_AnchoredPosition", "m_SizeDelta" })
        {
            string line = Regex.Match(amountTransform.Text, @"^  " + field + @":.*\n", RegexOptions.Multiline).Value;
            if (line.Length == 0) throw new InvalidOperationException("Missing reviewed amount layout field.");
            amountText = Regex.Replace(amountText, @"^  " + field + @":.*\n", _ => line, RegexOptions.Multiline);
        }
        changed[amountTransform.Id] = amountText;
        Doc roots = original.Single(document => document.Type == "1660057539");
        changed[roots.Id] = roots.Text.TrimEnd('\n') + "\n  - {fileID: " + instanceId + "}\n";
        LastAddedDocuments = added.Count;
        LastChangedDocuments = changed.Count;
        var inserted = added.Select(id => after[id]).OrderBy(document => long.Parse(document.Id)).ToList();
        var result = new StringBuilder("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
        int next = 0;
        foreach (Doc document in original)
        {
            while (next < inserted.Count && long.Parse(inserted[next].Id) < long.Parse(document.Id)) result.Append(inserted[next++].Text);
            result.Append(changed.TryGetValue(document.Id, out string replacement) ? replacement : document.Text);
        }
        while (next < inserted.Count) result.Append(inserted[next++].Text);
        string candidate = result.ToString();
        if (WebsiteShopScenePolicy.CollectErrors(candidate, prefab).Count != 0)
            throw new InvalidOperationException("Isolated wallet scene failed policy validation.");
        return candidate;

        void Add(string id)
        {
            if (before.ContainsKey(id)) throw new InvalidOperationException("Authored wallet object already exists in baseline: " + id + ". Export is only for the first scene shipping change.");
            added.Add(id);
        }

        Doc Control(string name)
        {
            Doc gameObject = working.Single(document => document.Type == "1" && WebsiteShopScenePolicy.Name(document) == name);
            AddGraph(gameObject);
            return gameObject;
        }

        void AddGraph(Doc gameObject)
        {
            Add(gameObject.Id);
            foreach (Match component in Regex.Matches(gameObject.Text, @"^  - component: \{fileID: (-?\d+)\}$", RegexOptions.Multiline)) Add(component.Groups[1].Value);
            Doc transform = TransformFor(gameObject.Id);
            string children = Regex.Match(transform.Text, @"(?m)^  m_Children:\n([\s\S]*?)(?=^  m_Father:)").Groups[1].Value;
            foreach (Match child in Regex.Matches(children, @"fileID: (-?\d+)")) AddGraph(after[Ref(after[child.Groups[1].Value], "m_GameObject")]);
        }

        Doc TransformFor(string gameObject) => working.Single(document => document.Type == "224" && Ref(document, "m_GameObject") == gameObject);

        void AppendChild(string parentId, string childId)
        {
            string text = changed.TryGetValue(parentId, out string previous) ? previous : before[parentId].Text;
            if (text.Contains("m_Children: []\n")) text = ReplaceExactlyOnce(text, "m_Children: []\n", "m_Children:\n  - {fileID: " + childId + "}\n");
            else text = ReplaceExactlyOnce(text, "  m_Father:", "  - {fileID: " + childId + "}\n  m_Father:");
            changed[parentId] = text;
        }
    }

    private static string Ref(Doc document, string field) => WebsiteShopScenePolicy.Reference(document, field);

    private static string ReplaceExactlyOnce(string text, string search, string replacement)
    {
        int found = text.IndexOf(search, StringComparison.Ordinal);
        if (found < 0 || text.IndexOf(search, found + search.Length, StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException("Expected one reviewed field: " + search);
        return text.Substring(0, found) + replacement + text.Substring(found + search.Length);
    }
}
