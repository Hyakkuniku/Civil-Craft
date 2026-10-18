using TMPro;
using UnityEngine;

int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    checks++;
}
RectTransform Find(Transform node, string name)
{
    if (node.name == name) return node as RectTransform;
    foreach (Transform child in node)
    {
        RectTransform found = Find(child, name);
        if (found != null) return found;
    }
    return null;
}
bool Separated(RectTransform a, RectTransform b) =>
    a.anchorMax.x <= b.anchorMin.x || b.anchorMax.x <= a.anchorMin.x ||
    a.anchorMax.y <= b.anchorMin.y || b.anchorMax.y <= a.anchorMin.y;
string Snapshot(Transform node) => node.name + ":" + (node is RectTransform rect
    ? rect.anchorMin + ";" + rect.anchorMax + ";" + rect.offsetMin + ";" + rect.offsetMax : "") +
    (node.text != null ? ":" + node.text.text + ":" + node.text.fontSizeMin + ":" + node.text.fontSizeMax +
        ":" + node.text.enableWordWrapping + ":" + node.text.alignment : "") +
    string.Join("/", node.children.Select(Snapshot));

AlmanacProfileLayout.Apply(null);
var unrelated = new RectTransform("NotAProfile");
var unrelatedValue = unrelated.Add("GoldValue", "Do not restyle shared HUD");
string unrelatedBefore = Snapshot(unrelated);
AlmanacProfileLayout.Apply(unrelated);
Check(unrelatedBefore == Snapshot(unrelated), "Unrelated menus must stay unchanged.");
AlmanacProfileLayout.Apply(new Transform("EmptyPage"));
var emptyStats = new RectTransform("ProfileStatsDesign");
AlmanacProfileLayout.Apply(emptyStats);
Check(emptyStats.children.Count == 0, "Missing authored cards must not generate replacement objects.");

var page = new RectTransform("ProfileBookPage");
var stats = page.Add("ProfileStatsDesign");
var overview = stats.Add("OverviewCard");
overview.Add("RankLabel", "rank");
overview.Add("RankValue", "Journeyman");
var coins = overview.Add("GoldCard");
coins.Add("CoinIcon"); coins.Add("GoldLabel", "coins"); coins.Add("GoldValue", "5,850");
var diamonds = overview.Add("SecondaryCurrencyCard");
diamonds.Add("DiamondIcon"); diamonds.Add("SecondaryCurrencyLabel", "dias");
diamonds.Add("SecondaryCurrencyValue", "Unavailable");
overview.Add("ExperienceIcon"); overview.Add("ExpLabel", "experience");
overview.Add("ExpValue", "357 / 600");
overview.Add("ExpProgressBackground").Add("ExpProgressFill");
overview.Add("ExpRemaining", "243 EXP to next rank");
overview.Add("BridgesLabel", "bridges built"); overview.Add("BridgesValue", "1");
overview.Add("ContractsLabel", "contracts"); overview.Add("ContractsValue", "1");
var portrait = page.Add("ProfilePageDesign");
portrait.Add("PortraitArea");
portrait.Add("IdentityCard").Add("EngineerName", "Engineer: engineer");
var guardedSibling = page.Add("NotProfileCurrency");
guardedSibling.Add("GoldValue", "Other category");
string guardedBefore = Snapshot(guardedSibling);

AlmanacProfileLayout.Apply(page);
Check(Find(page, "SecondaryCurrencyLabel").text.text == "DIAMONDS", "Use full currency name, not the old ambiguous abbreviation.");
Check(Find(page, "GoldLabel").text.text == "COINS", "Coins label remains explicit.");
Check(Find(page, "SecondaryCurrencyValue").text.text == "Unavailable", "Unavailable must remain truthful and unabridged.");
Check(guardedBefore == Snapshot(guardedSibling), "Named text outside the profile must remain unchanged.");
foreach (string name in new[] { "GoldValue", "SecondaryCurrencyValue" })
{
    RectTransform value = Find(page, name);
    Check(value.anchorMin.x == .06f && value.anchorMax.x == .94f, name + " must receive the full value row.");
    Check(!value.text.enableWordWrapping && value.text.enableAutoSizing, name + " must stay on one adaptive-size line.");
    Check(value.text.fontSizeMin == 26 && value.text.fontSizeMax == 32, name + " has bounded legible currency type.");
    Check(!value.text.raycastTarget, name + " must not block book input.");
    Check(value.text.fontApplications >= 1, name + " must keep the book's typeface.");
    RectTransform card = value.parent as RectTransform;
    Check(card.anchorMin.y == .48f && card.anchorMax.y == .77f, name + " card reserves enough vertical room for two rows.");
    string label = name == "GoldValue" ? "GoldLabel" : "SecondaryCurrencyLabel";
    Check(Separated(value, Find(card, label)), name + " and label must not overlap.");
    Check(Separated(value, Find(card, name == "GoldValue" ? "CoinIcon" : "DiamondIcon")), name + " and icon must not overlap.");
    foreach (string amount in new[] { "0", "5,850", "1,000,000", "9,999,999", "2,147,483,647", "Unavailable" })
    {
        value.text.text = amount;
        AlmanacProfileLayout.Apply(page);
        Check(value.text.text == amount, "Layout must not round, truncate data, zero, or abbreviate balance values.");
        Check(!value.text.enableWordWrapping, "New wallet values must never restore wrapping.");
    }
}
foreach (var pair in new[] {
    ("GoldCard", "SecondaryCurrencyCard"), ("RankLabel", "RankValue"),
    ("RankValue", "GoldCard"), ("GoldCard", "ExpLabel"), ("SecondaryCurrencyCard", "ExpValue"),
    ("ExpLabel", "ExpValue"), ("ExpLabel", "ExpProgressBackground"),
    ("ExpProgressBackground", "ExpRemaining"), ("ExpRemaining", "BridgesLabel"),
    ("BridgesLabel", "BridgesValue"), ("ContractsLabel", "ContractsValue") })
    Check(Separated(Find(page, pair.Item1), Find(page, pair.Item2)), pair.Item1 + " overlaps " + pair.Item2);

foreach (string name in new[] { "EngineerName", "RankValue" })
{
    RectTransform node = Find(page, name);
    string longName = new string('W', 200);
    node.text.text = longName;
    AlmanacProfileLayout.Apply(page);
    Check(node.text.text == longName, "Long identity must retain its original source text.");
    Check(!node.text.enableWordWrapping && node.text.overflowMode == TextOverflowModes.Ellipsis,
        "An excessive identity must not cross card boundaries.");
    Check(node.text.fontSizeMin >= 24, "Identity must not silently shrink to unreadable text.");
}
string beforeRepeat = Snapshot(page);
AlmanacProfileLayout.Apply(page);
Check(beforeRepeat == Snapshot(page), "Repeat layout application must be idempotent.");
Find(page, "SecondaryCurrencyValue").text.enableWordWrapping = true;
Find(page, "SecondaryCurrencyValue").text.fontSizeMin = 10;
AlmanacProfileLayout.Apply(page);
Check(!Find(page, "SecondaryCurrencyValue").text.enableWordWrapping &&
    Find(page, "SecondaryCurrencyValue").text.fontSizeMin == 26,
    "Reapplying after generic typography must restore the guarded value layout.");

Console.WriteLine($"PASS: {checks} checks on the production profile-layout helper. Named hierarchy and text are isolated doubles; native TMP rendering is verified separately. No scenes, saves, wallets or accounts touched.");
