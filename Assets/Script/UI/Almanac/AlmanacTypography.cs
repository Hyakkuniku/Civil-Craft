using TMPro;
using UnityEngine;

/// <summary>Book-only typography. Other menus and the TMP default font stay unchanged.</summary>
public static class AlmanacTypography
{
    public const string FontResource = "Fonts/Internet Friends SDF";
    private static TMP_FontAsset bookFont;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => bookFont = null;

    public static void ApplyFont(TMP_Text text)
    {
        if (text == null) return;
        // Retry after an editor import rather than caching a missing asset forever.
        if (bookFont == null) bookFont = Resources.Load<TMP_FontAsset>(FontResource);
        if (bookFont == null) return;
        text.font = bookFont;
        // A material from the previous typeface samples the wrong glyph atlas.
        text.fontSharedMaterial = bookFont.material;
    }

    public static void ApplyExistingBook(AlmanacManager book)
    {
        if (book == null || book.categories == null) return;
        foreach (AlmanacCategory category in book.categories)
        {
            if (category == null) continue;
            ApplyPage(category.leftPageZone, category.tabType == AlmanacTabType.General);
            ApplyPage(category.rightPageZone, category.tabType == AlmanacTabType.General);
        }
    }

    private static void ApplyPage(Transform page, bool isProfile)
    {
        if (page == null) return;
        foreach (TMP_Text text in page.GetComponentsInChildren<TMP_Text>(true))
        {
            ApplyFont(text);
            if (text.name == "DividerStar")
            {
                // A decorative glyph in the thin title divider is not body copy.
                text.fontSize = 14f;
                text.enableAutoSizing = true;
                text.fontSizeMin = 12f;
                text.fontSizeMax = 14f;
                continue;
            }
            if (text.name == "RunningHeader" || text.name == "Folio")
            {
                // Printed margin labels have less height than the main page cards.
                text.fontSize = 22f;
                text.enableAutoSizing = true;
                text.fontSizeMin = 18f;
                text.fontSizeMax = 22f;
                continue;
            }
            if (!isProfile) continue; // Readers configure their own scrolling and size hierarchy.
            float target = 26f, minimum = 22f;
            string name = text.gameObject.name;
            if (name.EndsWith("Title", System.StringComparison.Ordinal))
            { target = 42f; minimum = 34f; }
            else if (name == "EngineerName")
            { target = 36f; minimum = 28f; }
            else if (name.EndsWith("Value", System.StringComparison.Ordinal))
            { target = 32f; minimum = 26f; }
            else if (name == "AchievementSummary" || name == "Summary")
            { target = 28f; minimum = 24f; }
            float originalMaximum = text.enableAutoSizing ? text.fontSizeMax : text.fontSize;
            target = Mathf.Max(target, originalMaximum);
            text.fontSize = target;
            text.enableAutoSizing = true;
            text.fontSizeMax = target;
            text.fontSizeMin = Mathf.Min(target, Mathf.Max(minimum, text.fontSizeMin));
            text.enableWordWrapping = true;
        }
    }
}
