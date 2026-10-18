using TMPro;
using UnityEngine;

/// <summary>Readability repairs for the existing profile cards; no data or shared menu styling.</summary>
public static class AlmanacProfileLayout
{
    public const float CurrencyMinimumFontSize = 26f;
    public const float IdentityMinimumFontSize = 24f;

    public static void Apply(Transform page)
    {
        if (page == null) return;
        Transform stats = Find(page, "ProfileStatsDesign");
        if (stats != null) ApplyStats(stats);
        Transform profile = Find(page, "ProfilePageDesign");
        if (profile != null)
        {
            // A long account name stays inside its own card instead of wrapping
            // into the photo or the bottom margin. The original value is retained.
            Text(profile, "EngineerName", new Vector2(.20f, .08f), new Vector2(.97f, .92f),
                36f, IdentityMinimumFontSize, TextAlignmentOptions.Center);
        }
    }

    private static void ApplyStats(Transform stats)
    {
        Transform overview = Find(stats, "OverviewCard");
        if (overview == null) return;

        Text(overview, "RankLabel", new Vector2(.04f, .81f), new Vector2(.23f, .97f),
            26f, 22f, TextAlignmentOptions.MidlineLeft);
        Text(overview, "RankValue", new Vector2(.25f, .81f), new Vector2(.95f, .97f),
            32f, IdentityMinimumFontSize, TextAlignmentOptions.MidlineRight);

        // Each currency has a real header and a separate, full-width value row.
        // In particular, "Unavailable" must not become a misleading zero, an
        // abbreviation, or a two-line fragment squeezed beside the label.
        Currency(overview, "GoldCard", "GoldLabel", "GoldValue", "CoinIcon", "COINS", .035f, .49f);
        Currency(overview, "SecondaryCurrencyCard", "SecondaryCurrencyLabel",
            "SecondaryCurrencyValue", "DiamondIcon", "DIAMONDS", .51f, .965f);

        Rect(Find(overview, "ExperienceIcon"), new Vector2(.04f, .315f), new Vector2(.115f, .46f));
        Text(overview, "ExpLabel", new Vector2(.125f, .315f), new Vector2(.49f, .46f),
            26f, 22f, TextAlignmentOptions.MidlineLeft);
        Text(overview, "ExpValue", new Vector2(.51f, .315f), new Vector2(.95f, .46f),
            30f, 24f, TextAlignmentOptions.MidlineRight);
        Rect(Find(overview, "ExpProgressBackground"), new Vector2(.18f, .225f), new Vector2(.94f, .295f));
        Text(overview, "ExpRemaining", new Vector2(.18f, .125f), new Vector2(.94f, .22f),
            24f, 22f, TextAlignmentOptions.Center);

        Text(overview, "BridgesLabel", new Vector2(.04f, .005f), new Vector2(.33f, .12f),
            25f, 22f, TextAlignmentOptions.MidlineLeft);
        Text(overview, "BridgesValue", new Vector2(.35f, .005f), new Vector2(.48f, .12f),
            30f, 24f, TextAlignmentOptions.MidlineRight);
        Text(overview, "ContractsLabel", new Vector2(.52f, .005f), new Vector2(.80f, .12f),
            25f, 22f, TextAlignmentOptions.MidlineLeft);
        Text(overview, "ContractsValue", new Vector2(.82f, .005f), new Vector2(.96f, .12f),
            30f, 24f, TextAlignmentOptions.MidlineRight);
    }

    private static void Currency(Transform overview, string cardName, string labelName,
        string valueName, string iconName, string label, float left, float right)
    {
        Transform card = Find(overview, cardName);
        if (card == null) return;
        Rect(card, new Vector2(left, .48f), new Vector2(right, .77f));
        Rect(Find(card, iconName), new Vector2(.045f, .58f), new Vector2(.17f, .95f));
        TMP_Text heading = Text(card, labelName, new Vector2(.21f, .56f), new Vector2(.94f, .98f),
            26f, 22f, TextAlignmentOptions.MidlineLeft);
        if (heading != null) heading.text = label;
        Text(card, valueName, new Vector2(.06f, .025f), new Vector2(.94f, .53f),
            32f, CurrencyMinimumFontSize, TextAlignmentOptions.MidlineRight);
    }

    private static TMP_Text Text(Transform root, string name, Vector2 min, Vector2 max,
        float maximum, float minimum, TextAlignmentOptions alignment)
    {
        Transform node = Find(root, name);
        TMP_Text text = node != null ? node.GetComponent<TMP_Text>() : null;
        if (text == null) return null;
        Rect(node, min, max);
        AlmanacTypography.ApplyFont(text);
        text.fontSize = maximum;
        text.enableAutoSizing = true;
        text.fontSizeMax = maximum;
        text.fontSizeMin = minimum;
        text.alignment = alignment;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.margin = Vector4.zero;
        text.raycastTarget = false;
        return text;
    }

    private static void Rect(Transform node, Vector2 min, Vector2 max)
    {
        if (!(node is RectTransform rect)) return;
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
    }

    private static Transform Find(Transform root, string name)
    {
        if (root == null) return null;
        if (root.name == name) return root;
        foreach (Transform child in root)
        {
            Transform match = Find(child, name);
            if (match != null) return match;
        }
        return null;
    }
}
