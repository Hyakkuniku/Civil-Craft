using System.Diagnostics;
using System.Text.RegularExpressions;

string root = Path.GetFullPath(args.Length > 0 ? args[0] : Directory.GetCurrentDirectory());
string prefab = File.ReadAllText(Path.Combine(root, "Assets/Prefabs/UI/AccountSaveChoiceDialog.prefab"));
if (args.Length == 3 && args[1] == "--export-wallet-scene")
{
    string path = args[2];
    if (!WebsiteShopScenePolicy.RequiredScenes.Contains(path)) throw new InvalidOperationException("Not an allowlisted wallet scene.");
    Console.Write(WalletSceneDelta.Extract(ReadCommitted(path), File.ReadAllText(Path.Combine(root, path)), prefab));
    return;
}
int checks = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
    checks++;
}

string acceptance = Path.Combine(root, "Builds", "WalletAcceptance", "CivilCraft-wallet-acceptance-policy-only-does-not-exist.apk");
Check(WalletAcceptanceBuildPath.Validate(root, acceptance) == Path.GetFullPath(acceptance), "new local acceptance path must pass without creating a file");
foreach (string invalid in new[] { "relative.apk", Path.Combine(root, "CivilCraft-wallet-acceptance-outside.apk"),
    "\\THESIS\\CivilCraft-wallet-acceptance-drive-relative.apk", "D:CivilCraft-wallet-acceptance-drive-relative.apk",
    Path.Combine(root, "Builds", "WalletAcceptanceOther", "CivilCraft-wallet-acceptance-prefix-escape.apk"),
    Path.Combine(root, "Builds", "WalletAcceptance", "..", "CivilCraft-wallet-acceptance-traversal.apk"),
    Path.Combine(root, "Builds", "WalletAcceptance", "release.apk"),
    Path.Combine(root, "Builds", "WalletAcceptance", "CivilCraft-wallet-acceptance-not-an-apk.aab") })
{
    bool rejected = false;
    try { WalletAcceptanceBuildPath.Validate(root, invalid); } catch (InvalidOperationException) { rejected = true; }
    Check(rejected, "unsafe or non-acceptance output path must fail: " + invalid);
}
string verifiedCommit = new string('a', 40);
Check(WalletAcceptanceCheckoutPolicy.Verify(0, verifiedCommit + "\n", 0, 0, "") == verifiedCommit,
    "clean committed acceptance checkout must pass");
foreach (var state in new[] {
    (CommitCode: 128, Commit: "", TrackedCode: 0, UntrackedCode: 0, Untracked: ""),
    (CommitCode: 0, Commit: "invalid", TrackedCode: 0, UntrackedCode: 0, Untracked: ""),
    (CommitCode: 0, Commit: verifiedCommit, TrackedCode: 1, UntrackedCode: 0, Untracked: ""),
    (CommitCode: 0, Commit: verifiedCommit, TrackedCode: 128, UntrackedCode: 0, Untracked: ""),
    (CommitCode: 0, Commit: verifiedCommit, TrackedCode: 0, UntrackedCode: 128, Untracked: ""),
    (CommitCode: 0, Commit: verifiedCommit, TrackedCode: 0, UntrackedCode: 0, Untracked: "Assets/Uncommitted.cs\n"),
    (CommitCode: 0, Commit: verifiedCommit, TrackedCode: 0, UntrackedCode: 0, Untracked: "Packages/manifest.json\n"),
    (CommitCode: 0, Commit: verifiedCommit, TrackedCode: 0, UntrackedCode: 0, Untracked: "ProjectSettings/Unreviewed.asset\n") })
{
    bool rejected = false;
    try { WalletAcceptanceCheckoutPolicy.Verify(state.CommitCode, state.Commit, state.TrackedCode, state.UntrackedCode, state.Untracked); }
    catch (InvalidOperationException) { rejected = true; }
    Check(rejected, "uncommitted, dirty, untracked or unverifiable acceptance checkout must fail");
}

foreach (string path in WebsiteShopScenePolicy.RequiredScenes)
{
    string scene = File.ReadAllText(Path.Combine(root, path));
    var errors = WebsiteShopScenePolicy.CollectErrors(scene, prefab);
    Check(errors.Count == 0, path + ": " + string.Join("; ", errors));
    Check(WebsiteShopScenePolicy.CollectErrors(Regex.Replace(scene, @"currencyShopDialog: \{fileID: -?\d+\}", "currencyShopDialog: {fileID: 0}"), prefab).Count > 0,
        path + " missing dialog must fail");
    foreach (string method in new[] { "HandleAddCurrencyClicked", "HandleAddDiamondsClicked", "RefreshWalletBalances" })
        Check(WebsiteShopScenePolicy.CollectErrors(scene.Replace("m_MethodName: " + method, "m_MethodName: MissingCallback"), prefab).Count > 0,
            path + " missing " + method + " must fail");
    var documents = WebsiteShopScenePolicy.ReadDocuments(scene);
    var shop = documents.Single(document => document.Text.Contains("m_Script: {fileID: 11500000, guid: " + WebsiteShopScenePolicy.ShopScriptGuid + ","));
    var diamond = documents.Single(document => document.Text.Contains("m_MethodName: HandleAddDiamondsClicked\n"));
    string wrongTarget = diamond.Text.Replace("m_Target: {fileID: " + shop.Id + "}", "m_Target: {fileID: 0}");
    Check(WebsiteShopScenePolicy.CollectErrors(scene.Replace(diamond.Text, wrongTarget), prefab).Count > 0, path + " wrong account-shop callback target must fail");
    Check(WebsiteShopScenePolicy.CollectErrors(scene.Replace(diamond.Text, diamond.Text.Replace("m_CallState: 2", "m_CallState: 0")), prefab).Count > 0,
        path + " disabled persistent callback must fail");
    Check(WebsiteShopScenePolicy.CollectErrors(scene.Replace(diamond.Text, diamond.Text.Replace("m_Interactable: 1", "m_Interactable: 0")), prefab).Count > 0,
        path + " disabled Diamond control must fail");
    var diamondObject = documents.Single(document => document.Id == WebsiteShopScenePolicy.Reference(diamond, "m_GameObject"));
    Check(WebsiteShopScenePolicy.CollectErrors(scene.Replace(diamondObject.Text, diamondObject.Text.Replace("m_IsActive: 1", "m_IsActive: 0")), prefab).Count > 0,
        path + " inactive Diamond control must fail");
    Check(WebsiteShopScenePolicy.CollectErrors(scene, Regex.Replace(prefab, @"titleText: \{fileID: -?\d+\}", "titleText: {fileID: 0}")).Count > 0,
        path + " broken dialog prefab must fail");
    string committed = ReadCommitted(path);
    if (WebsiteShopScenePolicy.CollectErrors(committed, prefab).Count > 0)
    {
        string isolated = WalletSceneDelta.Extract(committed, scene, prefab);
        Check(WebsiteShopScenePolicy.CollectErrors(isolated, prefab).Count == 0, path + " isolated wallet-only scene must pass");
        AuditWalletOnlyDelta(path, committed, isolated);
        var oldIds = WebsiteShopScenePolicy.ReadDocuments(committed).Select(document => document.Id).ToHashSet();
        var unrelatedObject = documents.First(document => document.Type == "1" && oldIds.Contains(document.Id) && document.Text.Contains("  m_TagString: Untagged\n"));
        string futureUnrelated = scene.Replace(unrelatedObject.Text, unrelatedObject.Text.Replace("  m_TagString: Untagged\n", "  m_TagString: UserUnrelatedTagEdit\n"));
        Check(futureUnrelated != scene, path + " unrelated preservation regression must actually alter the authored input");
        Check(WalletSceneDelta.Extract(committed, futureUnrelated, prefab) == isolated, path + " unrelated existing scene edits must not enter the wallet delta");
        Console.WriteLine("Wallet-only delta: " + path + "; " + WalletSceneDelta.LastAddedDocuments + " added objects, " + WalletSceneDelta.LastChangedDocuments + " allowlisted existing objects");
    }
    Console.WriteLine("PASS: " + path + " saved authored controls and rejection cases");
}
Console.WriteLine("PASS: " + checks + " saved scene shipping checks; no Unity, PlayFab, browser or payment request was made.");

void AuditWalletOnlyDelta(string path, string original, string isolated)
{
    var before = WebsiteShopScenePolicy.ReadDocuments(original).ToDictionary(document => document.Id);
    var after = WebsiteShopScenePolicy.ReadDocuments(isolated).ToDictionary(document => document.Id);
    var additions = after.Keys.Except(before.Keys).ToHashSet();
    Check(before.Keys.All(after.ContainsKey), path + " every original YAML object must remain");
    Check(additions.Count == 20, path + " only 20 reviewed wallet YAML objects may be added");
    var changed = before.Values.Where(document => document.Text != after[document.Id].Text).ToList();
    Check(changed.Count == 5, path + " only five reviewed existing YAML objects may change");
    foreach (var document in changed)
    {
        string replacement = after[document.Id].Text;
        string description;
        if (document.Text.Contains("m_Script: {fileID: 11500000, guid: " + WebsiteShopScenePolicy.ShopScriptGuid + ","))
        {
            Check(Regex.Replace(replacement, @"^  currencyShopDialog:.*\n", "", RegexOptions.Multiline) == document.Text,
                path + " ShopManager must retain every tutorial/catalog/confirmation field");
            description = "ShopManager: dialog field only; tutorial/catalog/confirmation unchanged";
        }
        else if (document.Type == "1660057539" || replacement.Contains("  m_Children:\n"))
        {
            string stripped = replacement;
            foreach (string id in additions) stripped = stripped.Replace("  - {fileID: " + id + "}\n", "");
            Check(stripped == document.Text, path + " scene/currency-row root change must only append new wallet child IDs");
            description = document.Type == "1660057539" ? "SceneRoots: reviewed dialog root only" : "RectTransform: new wallet child reference only";
        }
        else
        {
            string IgnoreAmountLayout(string value) => Regex.Replace(value, @"^  (m_AnchorMax|m_AnchoredPosition|m_SizeDelta):.*\n", "", RegexOptions.Multiline);
            Check(document.Type == "224" && IgnoreAmountLayout(replacement) == IgnoreAmountLayout(document.Text),
                path + " Diamond amount may change only its reviewed three layout fields");
            Check(replacement.Contains("  m_AnchorMax: {x: 0.75, y: 1}\n") && replacement.Contains("  m_AnchoredPosition: {x: -1, y: 0}\n") &&
                replacement.Contains("  m_SizeDelta: {x: -2, y: 0}\n"), path + " Diamond amount layout must match authored plus-button space");
            description = "RectTransform: Diamond amount layout only";
        }
        Console.WriteLine("AUDIT: " + path + " &" + document.Id + " " + description);
    }
    var oldCoin = before.Values.Single(document => document.Text.Contains("m_MethodName: HandleAddCurrencyClicked\n"));
    Check(after[oldCoin.Id].Text == oldCoin.Text, path + " existing Coin button callback must be byte-for-byte retained");
    foreach (string id in additions)
    {
        var added = after[id];
        if (added.Type == "1") Check(new[] { "AddDiamondsButton", "RefreshCurrencyButton", "Plus", "Label" }.Contains(WebsiteShopScenePolicy.Name(added)),
            path + " no unrelated named GameObject may be added");
        if (added.Type == "114") Check(new[] { WebsiteShopScenePolicy.DialogScriptGuid, WebsiteShopScenePolicy.ButtonScriptGuid,
            "f4688fdb7df04437aeb418b961361dc5", "fe87c0e1cc204ed48ad3b37840f39efc" }.Any(guid => added.Text.Contains("m_Script: {fileID: 11500000, guid: " + guid + ",")),
            path + " no unrelated script may be introduced by the wallet scene delta");
    }
}

string ReadCommitted(string path)
{
    var info = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
    info.ArgumentList.Add("show"); info.ArgumentList.Add("HEAD:" + path);
    using var process = Process.Start(info);
    string text = process.StandardOutput.ReadToEnd();
    string error = process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0) throw new InvalidOperationException("Cannot read committed baseline: " + error);
    return text;
}
