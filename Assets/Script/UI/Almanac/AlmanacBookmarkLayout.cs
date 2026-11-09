using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Positions bookmarks between the book cover and its paper.</summary>
public static class AlmanacBookmarkLayout
{
    public const float SelectedLift = 8f;
    public const float Baseline = 22f;
    public const float IconSize = 56f;
    public const float AlertSize = 30f;

    public static void TuckBehindPage(AlmanacCategory category)
    {
        if (category?.tabButton == null || !(category.leftPageZone?.parent is RectTransform chapter)) return;
        var tab = (RectTransform)category.tabButton.transform;
        if (tab.parent != chapter.parent || tab.anchorMax.x - tab.anchorMin.x <= .001f) return;

        // The bottom follows the page as the book resizes. The exposed strip
        // above the chapter stays the same height across screen aspect ratios.
        tab.anchorMin = new Vector2(tab.anchorMin.x, Mathf.Lerp(chapter.anchorMin.y, chapter.anchorMax.y, .90f));
        tab.anchorMax = new Vector2(tab.anchorMax.x, chapter.anchorMax.y);
        tab.pivot = new Vector2(tab.pivot.x, .5f);
        tab.sizeDelta = new Vector2(tab.sizeDelta.x, Baseline * 2f);
        tab.anchoredPosition = new Vector2(0f, Baseline);

        Transform alert = category.tabAlertIcon != null ? category.tabAlertIcon.transform : null;
        foreach (Image image in tab.GetComponentsInChildren<Image>(true))
        {
            if (image.transform == tab || (alert != null && image.transform.IsChildOf(alert))) continue;
            SetTopDecoration(image.rectTransform, .5f, IconSize, IconSize, 34f);
            image.preserveAspect = true;
        }
        if (alert is RectTransform badge) SetTopDecoration(badge, .86f, AlertSize, AlertSize, 24f);
    }

    private static void SetTopDecoration(RectTransform rect, float x, float width, float height, float inset)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(x, 1f);
        rect.pivot = new Vector2(.5f, .5f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(0f, -inset);
    }

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

}
