using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Book-relative bookmarks; never changes unlocks, selected lift or page callbacks.</summary>
public static class AlmanacBookmarkLayout
{
    public static void Reflow(IList<AlmanacCategory> categories)
    {
        if (categories == null) return;
        RectTransform first = null;
        foreach (AlmanacCategory category in categories)
            if (category != null && category.tabButton != null)
            { first = category.tabButton.transform as RectTransform; break; }
        if (first == null) return;
        float width = first.anchorMax.x - first.anchorMin.x;
        if (width <= .001f) return; // Leave unrelated fixed-anchor/custom books alone.
        float left = first.anchorMin.x;
        float step = width * (0.092f / 0.078f);
        int visible = 0;
        foreach (AlmanacCategory category in categories)
        {
            if (category == null || category.tabButton == null) continue;
            RectTransform rect = category.tabButton.transform as RectTransform;
            if (rect == null || rect.parent != first.parent || !rect.gameObject.activeSelf ||
                category.visibilityMode == TabVisibility.AlwaysHidden) continue;
            float x = left + visible++ * step;
            rect.anchorMin = new Vector2(x, rect.anchorMin.y);
            rect.anchorMax = new Vector2(x + width, rect.anchorMax.y);
            rect.anchoredPosition = new Vector2(0f, rect.anchoredPosition.y);
        }
        // Reverse insertion preserves category order behind the paper chapters.
        for (int i = categories.Count - 1; i >= 0; i--)
        {
            Button button = categories[i] != null ? categories[i].tabButton : null;
            if (button != null && button.transform.parent == first.parent) button.transform.SetAsFirstSibling();
        }
        // Covers are a separate bottom layer; bookmarks sit above the cover but
        // below the pages, not behind the entire closed-book silhouette.
        Transform covers = first.parent.Find("Almanac Cover Layer");
        if (covers != null) covers.SetAsFirstSibling();
    }

    public static void ConfigureQuizLabel(TMP_Text label)
    {
        if (label == null) return;
        RectTransform rect = label.rectTransform;
        // Match the exposed icon strip on the existing bookmarks, not their
        // covered lower portion. The entire parent Button remains the hit target.
        rect.anchorMin = new Vector2(.08f, .67f);
        rect.anchorMax = new Vector2(.92f, .98f);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        label.fontSize = 25f;
        label.enableAutoSizing = true;
        label.fontSizeMin = 21f;
        label.fontSizeMax = 25f;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
    }
}
