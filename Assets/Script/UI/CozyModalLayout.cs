using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Fits existing, scene-authored modal controls to the device safe area.
/// No controls are rebuilt at runtime; layout runs only on opening/resizing.</summary>
[DisallowMultipleComponent]
public sealed class CozyModalLayout : MonoBehaviour
{
    public enum PanelKind { Achievements, Settings, Leaderboards }
    [HideInInspector] public int authoredVersion;
    public PanelKind kind;
    public RectTransform card;
    public RectTransform surface;
    public RectTransform titleTab;
    public RectTransform titleSurface;
    public RectTransform closeRoot;
    public Button closeButton;
    public TMP_FontAsset font;
    public Sprite roundedSprite;
    public Sprite titleSprite;
    public Sprite circleSprite;

    public static readonly Color Ink = new Color32(75, 50, 36, 255);
    public static readonly Color Cream = new Color32(255, 249, 238, 255);
    public static readonly Color Sand = new Color32(239, 226, 204, 255);
    public static readonly Color Gold = new Color32(239, 166, 49, 255);
    public static readonly Color TitleCream = new Color32(255, 231, 176, 255);
    private Rect lastParentRect;
    private Rect lastSafeArea;
    private Vector2Int lastScreen;
    private bool applied;

    private void OnEnable() { applied = false; ApplyLayout(); }
    private void LateUpdate()
    {
        if (card == null || !(card.parent is RectTransform parent)) return;
        if (!applied || parent.rect != lastParentRect || Screen.safeArea != lastSafeArea ||
            lastScreen.x != Screen.width || lastScreen.y != Screen.height) ApplyLayout();
    }

    public void ApplyLayout()
    {
        if (card == null || !(card.parent is RectTransform parent)) return;
        Rect available = parent.rect;
        Canvas canvas = GetComponentInParent<Canvas>();
        if (Application.isPlaying && canvas != null && Screen.width > 0 && Screen.height > 0)
        {
            Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, Screen.safeArea.min, camera, out Vector2 min) &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, Screen.safeArea.max, camera, out Vector2 max))
            {
                min = Vector2.Max(min, available.min);
                max = Vector2.Min(max, available.max);
                if (max.x > min.x && max.y > min.y) available = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            }
        }
        if (available.width < 100f || available.height < 100f) return;

        // Leave room for the original overlapping title and circular close control.
        Vector2 size = new Vector2(Mathf.Min(1440f, available.width - 128f),
            Mathf.Min(860f, available.height - 156f));
        float unit = Mathf.Min(1f, Mathf.Min(size.x / 1440f, size.y / 900f));
        card.anchorMin = card.anchorMax = new Vector2(.5f, .5f);
        card.pivot = new Vector2(.5f, .5f);
        card.sizeDelta = size;
        card.anchoredPosition = available.center - parent.rect.center + new Vector2(0f, -22f * unit);
        Paint(card, Ink);
        Stretch(surface, 6f, 6f, 6f, 6f);
        Paint(surface, Cream);
        Place(titleTab, new Vector2(.5f, 1f), new Vector2(0f, 16f * unit), new Vector2(620f, 142f) * unit);
        Image tabImage = titleTab != null ? titleTab.GetComponent<Image>() : null;
        if (tabImage != null)
        {
            tabImage.enabled = true; tabImage.sprite = titleSprite;
            tabImage.type = Image.Type.Simple; tabImage.color = Color.white;
        }
        if (titleSurface != null) titleSurface.gameObject.SetActive(false);
        TMP_Text heading = titleTab != null ? titleTab.GetComponentInChildren<TMP_Text>(true) : null;
        if (heading != null)
        {
            heading.text = kind == PanelKind.Achievements ? "Achievements" : kind == PanelKind.Settings ? "Settings" : "Leaderboards";
            Text(heading, 40f * unit, true);
            heading.alignment = TextAlignmentOptions.Center;
            Stretch(heading.rectTransform, 60f * unit, 28f * unit, 60f * unit, 46f * unit);
        }
        Place(closeRoot, new Vector2(1f, 1f), Vector2.zero, new Vector2(96f, 96f) * unit);
        if (closeButton != null)
        {
            // Preserve the authored close artwork and its click callback.
            foreach (RectTransform rect in closeRoot.GetComponentsInChildren<RectTransform>(true))
                if (rect != closeRoot) Stretch(rect, 0f, 0f, 0f, 0f);
            Image background = closeRoot.GetComponent<Image>();
            if (background != null) { background.sprite = circleSprite; background.type = Image.Type.Simple; background.color = Ink; }
            RectTransform closeSurface = Find(closeRoot, "Cozy Close Surface");
            Stretch(closeSurface, 4f * unit, 4f * unit, 4f * unit, 4f * unit);
            Image face = closeSurface != null ? closeSurface.GetComponent<Image>() : null;
            if (face != null) { face.sprite = circleSprite; face.type = Image.Type.Simple; face.color = Cream; }
            TMP_Text label = closeButton.GetComponentInChildren<TMP_Text>(true);
            if (label != null) { Text(label, 58f * unit, true); label.alignment = TextAlignmentOptions.Center; }
            closeButton.navigation = new Navigation { mode = Navigation.Mode.None };
        }

        if (kind == PanelKind.Settings) LayoutSettings(unit);
        else if (kind == PanelKind.Leaderboards) LayoutLeaderboard(unit);
        else LayoutAchievements(unit);

        lastParentRect = parent.rect;
        lastSafeArea = Screen.safeArea;
        lastScreen = new Vector2Int(Screen.width, Screen.height);
        applied = true;
    }

    private void LayoutSettings(float u)
    {
        RectTransform root = Find(card, "SettingsContentRoot");
        Stretch(root, 44f * u, 32f * u, 44f * u, 80f * u);
        RectTransform tabs = Find(root, "TabsRow");
        Top(tabs, 0f, 0f, 0f, 72f * u);
        RectTransform footer = Find(root, "Footer");
        Bottom(footer, 0f, 0f, 0f, 72f * u);
        RectTransform page = Find(root, "PageBackground");
        Stretch(page, 0f, 92f * u, 0f, 92f * u);
        Paint(page, Sand);
        foreach (HorizontalLayoutGroup layout in root.GetComponentsInChildren<HorizontalLayoutGroup>(true))
        {
            layout.spacing = 14f * u;
            if (layout.name.EndsWith("Row")) layout.padding = new RectOffset((int)(24f*u), (int)(24f*u), (int)(10f*u), (int)(10f*u));
        }
        foreach (VerticalLayoutGroup layout in page.GetComponentsInChildren<VerticalLayoutGroup>(true))
        {
            layout.spacing = 12f * u;
            layout.padding = new RectOffset((int)(20f*u), (int)(20f*u), (int)(20f*u), (int)(20f*u));
        }
        foreach (LayoutElement element in page.GetComponentsInChildren<LayoutElement>(true))
        {
            string name = element.name;
            if (name.EndsWith("Row")) { element.minHeight = element.preferredHeight = 86f * u; Paint(element.transform as RectTransform, Cream); }
            else if (name == "Label") { element.minWidth = 0f; element.preferredWidth = 400f * u; element.flexibleWidth = 1f; element.preferredHeight = 60f * u; }
            else if (name.EndsWith("Dropdown")) { element.minWidth = 0f; element.preferredWidth = Mathf.Min(390f*u, card.rect.width*.34f); element.preferredHeight = 64f*u; }
            else if (name.EndsWith("Toggle")) { element.preferredWidth = element.preferredHeight = 64f*u; }
            else if (name.EndsWith("SliderGroup")) { element.minWidth = 0f; element.preferredWidth = Mathf.Min(410f*u, card.rect.width*.36f); element.preferredHeight = 64f*u; }
            else if (name.EndsWith("Slider")) { element.minWidth = 0f; element.preferredWidth = 290f*u; element.flexibleWidth = 1f; element.preferredHeight = 56f*u; }
            else if (name == "AccountStatusText") element.preferredHeight = 170f*u;
        }
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
        {
            bool bold = text.name == "Value" || text.GetComponentInParent<Button>() != null;
            Text(text, (bold ? 28f : 29f) * u, bold);
            text.enableWordWrapping = text.name == "AccountStatusText" ||
                (text.name == "Label" && text.GetComponentInParent<TMP_Dropdown>() == null && text.GetComponentInParent<Button>() == null);
        }
        foreach (Button button in root.GetComponentsInChildren<Button>(true))
        {
            Color color = button.name == "ApplySettingsButton" ? Gold : Sand;
            Paint(button.transform as RectTransform, color); Border(button.transform as RectTransform);
            button.navigation = new Navigation { mode = Navigation.Mode.None };
        }
        SettingsTabController controller = root.GetComponent<SettingsTabController>();
        if (controller != null)
        {
            controller.activeTabColor = Gold;
            controller.inactiveTabColor = Sand;
            controller.RefreshTabColors();
        }
        foreach (TMP_Dropdown dropdown in root.GetComponentsInChildren<TMP_Dropdown>(true)) StyleDropdown(dropdown, u);
        LayoutRebuilder.ForceRebuildLayoutImmediate(root);
    }

    private void LayoutAchievements(float u)
    {
        RectTransform filters = Find(card, "AchievementFilterBar");
        Top(filters, 44f*u, 44f*u, 80f*u, 72f*u);
        Paint(filters, new Color32(187, 173, 144, 255));
        string[] names = { "AllFilter", "CompleteFilter", "IncompleteFilter" };
        for (int i = 0; i < names.Length; i++)
        {
            RectTransform filter = Find(filters, names[i]);
            FractionTop(filter, i*.225f, (i+1)*.225f, 4f*u, 64f*u, 10f*u);
            StyleChoice(filter, u);
        }
        RectTransform count = Find(filters, "UnlockedCount");
        FractionTop(count, .70f, 1f, 4f*u, 64f*u, 18f*u);
        TMP_Text countText = count != null ? count.GetComponent<TMP_Text>() : null;
        Text(countText, 26f*u, true);
        if (countText != null) countText.alignment = TextAlignmentOptions.MidlineRight;
        RectTransform scroll = Find(card, "Scroll View");
        Stretch(scroll, 44f*u, 32f*u, 44f*u, 168f*u);
        Paint(scroll, Sand);
        ScrollRect scrollRect = scroll != null ? scroll.GetComponent<ScrollRect>() : null;
        if (scrollRect != null) { scrollRect.decelerationRate = .12f; scrollRect.scrollSensitivity = 40f; }
        AchievementUIManager manager = null;
        foreach (AchievementUIManager candidate in FindObjectsOfType<AchievementUIManager>(true))
            if (candidate.achievementPanel == gameObject) { manager = candidate; break; }
        if (manager != null) manager.RelayoutVisibleRows();
    }

    private void LayoutLeaderboard(float u)
    {
        TMP_Dropdown dropdown = card.GetComponentInChildren<TMP_Dropdown>(true);
        if (dropdown != null)
        {
            FractionTop(dropdown.transform as RectTransform, .14f, .86f, 80f*u, 72f*u, 0f);
            StyleDropdown(dropdown, u);
        }
        RectTransform efficient = Find(card, "Most Efficient Toggle");
        RectTransform strongest = Find(card, "Strongest Toggle");
        FractionTop(efficient, .14f, .48f, 168f*u, 68f*u, 0f);
        FractionTop(strongest, .54f, .86f, 168f*u, 68f*u, 0f);
        StyleChoice(efficient, u); StyleChoice(strongest, u);
        Top(Find(card, "Ranking Description"), 32f*u, 32f*u, 246f*u, 38f*u);
        Label("Ranking Description", 26f*u, false);
        float top = 296f*u, bottom = 130f*u;
        RectTransform frame = Find(card, "Ranking List Frame");
        Stretch(frame, 32f*u, bottom, 32f*u, top);
        Paint(frame, Sand);
        Border(frame);
        RectTransform listSurface = Find(frame, "Ranking List Surface");
        if (listSurface != null) Paint(listSurface, Sand);
        RectTransform header = Find(card, "Column Header");
        Top(header, 42f*u, 42f*u, top+8f*u, 56f*u);
        Paint(header, TitleCream);
        RectTransform viewport = Find(card, "Leaderboard Viewport");
        Stretch(viewport, 44f*u, bottom+10f*u, 70f*u, top+74f*u);
        RectTransform scrollbar = Find(card, "Leaderboard Scrollbar");
        if (scrollbar != null)
        {
            scrollbar.anchorMin = new Vector2(1f, 0f); scrollbar.anchorMax = Vector2.one;
            scrollbar.offsetMin = new Vector2(-58f*u, bottom+14f*u);
            scrollbar.offsetMax = new Vector2(-42f*u, -top-78f*u);
        }
        Stretch(Find(card, "Leaderboard Status"), 60f*u, bottom+30f*u, 60f*u, top+90f*u);
        Label("Leaderboard Status", 28f*u, false);
        Bottom(Find(card, "Personal Best"), 32f*u, 32f*u, 68f*u, 46f*u);
        Label("Personal Best", 28f*u, true);
        Bottom(Find(card, "Top 15 Footer"), 32f*u, 32f*u, 24f*u, 32f*u);
        Label("Top 15 Footer", 24f*u, false);
        StyleColumns(header, u, true);
        foreach (LeaderboardRowUI row in card.GetComponentsInChildren<LeaderboardRowUI>(true))
        {
            RectTransform rect = row.transform as RectTransform;
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, 66f*u);
            Image rowImage = row.GetComponent<Image>();
            if (rowImage != null)
            {
                rowImage.sprite = roundedSprite;
                rowImage.type = Image.Type.Sliced;
                rowImage.pixelsPerUnitMultiplier = 1f;
            }
            LayoutElement element = row.GetComponent<LayoutElement>();
            if (element != null) element.minHeight = element.preferredHeight = 66f*u;
            StyleColumns(rect, u, false);
        }
        ScrollRect scroll = viewport != null ? viewport.GetComponent<ScrollRect>() : null;
        if (scroll != null) { scroll.decelerationRate = .12f; scroll.scrollSensitivity = 40f; }
        if (scroll != null && scroll.content != null) LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
    }

    private void StyleColumns(RectTransform row, float u, bool header)
    {
        if (row == null) return;
        TMP_Text[] labels = row.GetComponentsInChildren<TMP_Text>(true);
        foreach (TMP_Text label in labels)
        {
            string name = label.name.ToLowerInvariant();
            float left = 0f, right = .10f;
            if (name.Contains("builder")) { left = .11f; right = .53f; }
            else if (name.Contains("cost")) { left = .54f; right = .77f; }
            else if (name.Contains("stress")) { left = .78f; right = 1f; }
            RectTransform rect = label.rectTransform;
            rect.anchorMin = new Vector2(left, 0f); rect.anchorMax = new Vector2(right, 1f);
            rect.offsetMin = new Vector2(12f*u, 4f*u); rect.offsetMax = new Vector2(-12f*u, -4f*u);
            Text(label, (header ? 26f : 30f)*u, header);
            label.alignment = name.Contains("builder") ? TextAlignmentOptions.MidlineLeft :
                name.Contains("cost") || name.Contains("stress") ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.Center;
        }
    }

    private void StyleChoice(RectTransform rect, float u)
    {
        if (rect == null) return;
        Toggle toggle = rect.GetComponent<Toggle>();
        if (toggle == null) return;
        toggle.transition = Selectable.Transition.None;
        Image hit = rect.GetComponent<Image>();
        if (hit != null) { hit.color = new Color(1f, 1f, 1f, 0f); hit.raycastTarget = true; }
        RectTransform box = Find(rect, "Checkbox") ?? Find(rect, "Box");
        Place(box, new Vector2(0f, .5f), new Vector2(30f*u, 0f), new Vector2(48f, 48f)*u);
        if (box != null)
        {
            Paint(box, toggle.isOn ? Gold : Cream); Border(box);
            if (toggle.graphic != null) { Stretch(toggle.graphic.rectTransform, 10f*u, 10f*u, 10f*u, 10f*u); toggle.graphic.color = Ink; }
        }
        RectTransform labelRect = Find(rect, "Label");
        Stretch(labelRect, 70f*u, 0f, 8f*u, 0f);
        TMP_Text label = labelRect != null ? labelRect.GetComponent<TMP_Text>() : null;
        Text(label, 28f*u, true);
        if (label != null) label.alignment = TextAlignmentOptions.MidlineLeft;
    }

    private void StyleDropdown(TMP_Dropdown dropdown, float u)
    {
        Paint(dropdown.transform as RectTransform, Cream); Border(dropdown.transform as RectTransform);
        Text(dropdown.captionText, 30f*u, false);
        Text(dropdown.itemText, 28f*u, false);
        if (dropdown.template != null)
        {
            dropdown.template.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 280f*u);
            Toggle item = dropdown.template.GetComponentInChildren<Toggle>(true);
            if (item != null) (item.transform as RectTransform).SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 64f*u);
        }
        dropdown.navigation = new Navigation { mode = Navigation.Mode.None };
    }

    private void Label(string name, float size, bool bold)
    {
        RectTransform rect = Find(card, name);
        Text(rect != null ? rect.GetComponent<TMP_Text>() : null, size, bold);
    }
    private void Text(TMP_Text text, float size, bool bold)
    {
        if (text == null) return;
        if (font != null) { text.font = font; text.fontSharedMaterial = font.material; }
        text.color = Ink;
        text.fontSize = size; text.enableAutoSizing = false;
        text.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        text.enableWordWrapping = false; text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
    }
    private void Paint(RectTransform rect, Color color)
    {
        if (rect == null) return;
        Image image = rect.GetComponent<Image>();
        if (image == null) return;
        image.enabled = true;
        image.material = null;
        image.sprite = roundedSprite; image.type = Image.Type.Sliced;
        image.color = color; image.pixelsPerUnitMultiplier = 1f;
        image.SetAllDirty();
    }
    private static void Border(RectTransform rect)
    {
        if (rect == null) return;
        Outline outline = rect.GetComponent<Outline>();
        if (outline != null) { outline.effectColor = Ink; outline.effectDistance = new Vector2(2f, -2f); }
    }
    public static RectTransform Find(Transform root, string name)
    {
        if (root == null) return null;
        foreach (RectTransform rect in root.GetComponentsInChildren<RectTransform>(true))
            if (rect.name == name) return rect;
        return null;
    }
    public static void Stretch(RectTransform rect, float left, float bottom, float right, float top)
    {
        if (rect == null) return;
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom); rect.offsetMax = new Vector2(-right, -top);
        rect.localScale = Vector3.one;
    }
    private static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
    {
        if (rect == null) return;
        rect.anchorMin = rect.anchorMax = anchor; rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = position; rect.sizeDelta = size;
        rect.localScale = Vector3.one;
    }
    private static void Top(RectTransform rect, float left, float right, float top, float height)
    {
        if (rect == null) return;
        rect.anchorMin = new Vector2(0f, 1f); rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, -top-height); rect.offsetMax = new Vector2(-right, -top);
    }
    private static void Bottom(RectTransform rect, float left, float right, float bottom, float height)
    {
        if (rect == null) return;
        rect.anchorMin = Vector2.zero; rect.anchorMax = new Vector2(1f, 0f);
        rect.offsetMin = new Vector2(left, bottom); rect.offsetMax = new Vector2(-right, bottom+height);
    }
    private static void FractionTop(RectTransform rect, float left, float right, float top, float height, float inset)
    {
        if (rect == null) return;
        rect.anchorMin = new Vector2(left, 1f); rect.anchorMax = new Vector2(right, 1f);
        rect.offsetMin = new Vector2(inset, -top-height); rect.offsetMax = new Vector2(-inset, -top);
    }
}
