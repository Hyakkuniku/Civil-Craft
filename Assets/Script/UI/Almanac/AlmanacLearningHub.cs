using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public enum AlmanacLearningContent { Lessons, Materials }

public sealed class AlmanacLearningHub : MonoBehaviour
{
    private static readonly Color Ink = new Color(0.25f, 0.16f, 0.10f, 1f);
    private static readonly Color MutedInk = new Color(0.47f, 0.35f, 0.25f, 1f);
    private static readonly Color Accent = new Color(0.66f, 0.36f, 0.13f, 1f);
    private static readonly Color PaperTint = new Color(0.96f, 0.91f, 0.82f, 0.98f);
    private static readonly Color CardTint = new Color(0.91f, 0.84f, 0.72f, 0.72f);
    private static readonly Color LockedTint = new Color(0.63f, 0.58f, 0.52f, 0.62f);

    private AlmanacManager manager;
    private int targetCategoryIndex;
    private AlmanacLearningContent contentType;
    private AlmanacLessonTab lessonTab;
    private AlmanacMaterialTab materialTab;
    private RectTransform leftPage;
    private RectTransform rightPage;
    private GameObject leftHome;
    private GameObject rightHome;
    private GameObject leftDetail;
    private GameObject rightDetail;
    private GameObject leftSearch;
    private GameObject rightSearch;
    private TMP_Text searchChapterText;
    private TMP_Text searchTitleText;
    private TMP_Text searchBodyText;
    private TMP_Text searchProgressText;
    private TMP_Text searchIndexText;
    private TMP_Text detailTypeText;
    private TMP_Text detailTitleText;
    private TMP_Text detailDescriptionText;
    private TMP_Text detailFactsText;
    private Image detailImage;
    private GameObject detailImageFrame;
    private ScrollRect detailScroll;
    private TMP_Text fontSource;
    private RectTransform indexContent;
    private readonly List<GameObject> activeFlipSheets = new List<GameObject>();
    private bool built;
    private bool showingDetail;
    private bool isTransitioning;
    private LessonData pendingLesson;
    private BridgeMaterialSO pendingMaterial;

    public void Build(
        AlmanacManager owner,
        int categoryIndex,
        AlmanacLearningContent content,
        Transform leftZone,
        Transform rightZone,
        AlmanacLessonTab lessons,
        AlmanacMaterialTab materials)
    {
        if (built || owner == null || leftZone == null || rightZone == null) return;

        built = true;
        manager = owner;
        targetCategoryIndex = categoryIndex;
        contentType = content;
        lessonTab = lessons;
        materialTab = materials;
        fontSource = owner.Panel != null
            ? owner.Panel.GetComponentInChildren<TMP_Text>(true)
            : GetComponentInChildren<TMP_Text>(true);

        List<Transform> legacyLeftPages = SnapshotChildren(leftZone);
        List<Transform> legacyRightPages = SnapshotChildren(rightZone);

        leftPage = CreatePage(content + "Hub_LeftPage", leftZone, true);
        rightPage = CreatePage(content + "Hub_RightPage", rightZone, false);
        leftPage.SetSiblingIndex(0);
        rightPage.SetSiblingIndex(0);
        StoreLegacyPages(legacyLeftPages, legacyRightPages);

        BuildHomeSpread();
        BuildDetailSpread();
        BuildSearchSpread();
        SetDetailVisible(false);

        manager.OnCategoryChanged += HandleCategoryChanged;
    }

    public void ResetToHome()
    {
        if (!built) return;
        StopAllCoroutines();
        isTransitioning = false;
        RefreshIndex();
        ShowHomeImmediate();
    }

    private void OnDestroy()
    {
        if (manager != null) manager.OnCategoryChanged -= HandleCategoryChanged;
    }

    private void HandleCategoryChanged(int categoryIndex)
    {
        if (categoryIndex == targetCategoryIndex)
        {
            if (leftPage != null) leftPage.gameObject.SetActive(true);
            if (rightPage != null) rightPage.gameObject.SetActive(true);
            RefreshIndex();
            if (!showingDetail) ShowHomeImmediate();
            return;
        }

        StopAllCoroutines();
        isTransitioning = false;
        ClearFlipSheets();
        ShowHomeImmediate();
    }

    private RectTransform CreatePage(string objectName, Transform zone, bool isLeft)
    {
        RectTransform page = CreateRect(objectName, zone);
        // Profile pages fill their half-page zones. Use that same geometry instead
        // of inheriting the old 647x621 lesson/material card rectangle.
        page.anchorMin = Vector2.zero;
        page.anchorMax = Vector2.one;
        page.anchoredPosition = Vector2.zero;
        page.sizeDelta = Vector2.zero;
        page.localScale = Vector3.one;
        page.localRotation = Quaternion.identity;
        page.pivot = new Vector2(isLeft ? 1f : 0f, 0.5f);
        return page;
    }

    private static List<Transform> SnapshotChildren(Transform parent)
    {
        List<Transform> children = new List<Transform>();
        foreach (Transform child in parent) children.Add(child);
        return children;
    }

    private void StoreLegacyPages(List<Transform> leftPages, List<Transform> rightPages)
    {
        RectTransform storage = CreateRect("Legacy" + contentType + "Pages_Runtime", transform);
        storage.gameObject.SetActive(false);
        foreach (Transform page in leftPages)
        {
            if (page != null) page.SetParent(storage, false);
        }
        foreach (Transform page in rightPages)
        {
            if (page != null) page.SetParent(storage, false);
        }
    }

    private void BuildHomeSpread()
    {
        leftHome = CreateRect("Introduction", leftPage).gameObject;
        rightHome = CreateRect("LibraryIndex", rightPage).gameObject;
        CenterInPage(leftHome.transform as RectTransform, 70f, 52f, 700f);
        CenterInPage(rightHome.transform as RectTransform, 42f, 62f, 700f);

        RectTransform intro = leftHome.transform as RectTransform;
        bool isLessons = contentType == AlmanacLearningContent.Lessons;
        string archiveLabel = isLessons ? "LEARNING ARCHIVE" : "MATERIAL ARCHIVE";
        string archiveTitle = isLessons ? "Engineering\nLessons" : "Builder's\nMaterials";
        string purposeCopy = isLessons
            ? "A working record of the engineering ideas you discover while designing and testing structures."
            : "A practical catalogue of every construction material you discover, including its strengths and limits.";

        TMP_Text kicker = CreateText("Kicker", intro, "CIVIL CRAFT  /  " + archiveLabel, 15f,
            FontStyles.Bold, Accent, TextAlignmentOptions.MidlineLeft);
        SetTop(kicker.rectTransform, 0f, 28f);

        TMP_Text title = CreateText("Title", intro, archiveTitle, 42f,
            FontStyles.Bold, Ink, TextAlignmentOptions.TopLeft);
        title.enableAutoSizing = true;
        title.fontSizeMin = 32f;
        title.fontSizeMax = 42f;
        title.lineSpacing = -8f;
        SetTop(title.rectTransform, 36f, 110f);

        RectTransform rule = CreateImage("Rule", intro, Accent).rectTransform;
        SetTop(rule, 158f, 3f);

        TMP_Text purpose = CreateText("Purpose", intro, purposeCopy,
            21f, FontStyles.Normal, Ink, TextAlignmentOptions.TopLeft);
        purpose.enableWordWrapping = true;
        purpose.lineSpacing = 8f;
        SetTop(purpose.rectTransform, 182f, 106f);

        if (isLessons)
        {
            CreateGuideRow(intro, 310f, "01", "REVIEW", "Return to important concepts whenever you need them.");
            CreateGuideRow(intro, 380f, "02", "OBSERVE", "Recognize forces and behavior in your structures.");
            CreateGuideRow(intro, 450f, "03", "APPLY", "Turn each principle into a stronger design.");
        }
        else
        {
            CreateGuideRow(intro, 310f, "01", "INSPECT", "Learn what each discovered material can do.");
            CreateGuideRow(intro, 380f, "02", "COMPARE", "Review cost, mass, length, and force limits.");
            CreateGuideRow(intro, 450f, "03", "CHOOSE", "Match the right material to each structural job.");
        }

        TMP_Text hint = CreateText("Hint", intro,
            isLessons
                ? "Choose an unlocked lesson on the right page to begin."
                : "Choose a discovered material on the right page to begin.", 16f,
            FontStyles.Italic, MutedInk, TextAlignmentOptions.BottomLeft);
        hint.rectTransform.anchorMin = Vector2.zero;
        hint.rectTransform.anchorMax = new Vector2(1f, 0f);
        hint.rectTransform.pivot = new Vector2(0.5f, 0f);
        hint.rectTransform.anchoredPosition = Vector2.zero;
        hint.rectTransform.sizeDelta = new Vector2(0f, 34f);

        RectTransform index = rightHome.transform as RectTransform;
        TMP_Text indexKicker = CreateText("Kicker", index, archiveLabel, 15f,
            FontStyles.Bold, Accent, TextAlignmentOptions.MidlineLeft);
        SetTop(indexKicker.rectTransform, 0f, 24f);

        TMP_Text indexTitle = CreateText("Title", index,
            isLessons ? "Choose a lesson to review" : "Choose a material to inspect", 28f,
            FontStyles.Bold, Ink, TextAlignmentOptions.MidlineLeft);
        indexTitle.enableAutoSizing = true;
        indexTitle.fontSizeMin = 22f;
        indexTitle.fontSizeMax = 28f;
        SetTop(indexTitle.rectTransform, 28f, 54f);

        RectTransform indexRule = CreateImage("Rule", index, Accent).rectTransform;
        SetTop(indexRule, 88f, 2f);
        BuildIndexScroll(index, 106f);
    }

    private void BuildIndexScroll(RectTransform parent, float top)
    {
        RectTransform scrollRoot = CreateRect("IndexScroll", parent);
        scrollRoot.anchorMin = Vector2.zero;
        scrollRoot.anchorMax = Vector2.one;
        scrollRoot.offsetMin = Vector2.zero;
        scrollRoot.offsetMax = new Vector2(0f, -top);

        ScrollRect scroll = scrollRoot.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 28f;

        RectTransform viewport = CreateRect("Viewport", scrollRoot);
        Stretch(viewport, 0f, 0f, 0f, 0f);
        viewport.gameObject.AddComponent<RectMask2D>();

        RectTransform content = CreateRect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;

        VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(2, 8, 0, 12);
        layout.spacing = 9f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = viewport;
        scroll.content = content;

        indexContent = content;
        RefreshIndex();
    }

    private void RefreshIndex()
    {
        if (indexContent == null) return;

        for (int i = indexContent.childCount - 1; i >= 0; i--)
        {
            GameObject child = indexContent.GetChild(i).gameObject;
            child.SetActive(false);
            Destroy(child);
        }

        if (contentType == AlmanacLearningContent.Lessons)
        {
            List<LessonData> lessons = GetLessonDatabase();
            CreateSectionLabel(indexContent, "LESSONS", lessons.Count);
            for (int i = 0; i < lessons.Count; i++)
            {
                LessonData lesson = lessons[i];
                if (lesson == null) continue;
                bool unlocked = LessonSaveManager.IsUnlocked(lesson);
                LessonData captured = lesson;
                CreateIndexButton(indexContent, (i + 1).ToString("00"), lesson.Title,
                    lesson.Image, unlocked, () => OpenLesson(captured));
            }
            return;
        }

        List<BridgeMaterialSO> materials = GetMaterialDatabase();
        materials.Sort((a, b) => string.Compare(a.GetDisplayName(), b.GetDisplayName(),
            System.StringComparison.OrdinalIgnoreCase));
        CreateSectionLabel(indexContent, "MATERIALS", materials.Count);
        for (int i = 0; i < materials.Count; i++)
        {
            BridgeMaterialSO material = materials[i];
            if (material == null) continue;
            bool unlocked = MaterialDiscoverySaveManager.IsDiscovered(material);
            BridgeMaterialSO captured = material;
            CreateIndexButton(indexContent, "M" + (i + 1).ToString("00"), material.GetDisplayName(),
                material.materialIcon, unlocked, () => OpenMaterial(captured));
        }
    }

    private List<LessonData> GetLessonDatabase()
    {
        if (lessonTab != null) return lessonTab.GetAllLessons();

        List<LessonData> lessons = new List<LessonData>();
        HashSet<string> seenIds = new HashSet<string>();
        foreach (LessonData lesson in Resources.FindObjectsOfTypeAll<LessonData>())
        {
            if (lesson == null || string.IsNullOrWhiteSpace(lesson.Id) || !seenIds.Add(lesson.Id))
                continue;
            lessons.Add(lesson);
        }
        return lessons;
    }

    private List<BridgeMaterialSO> GetMaterialDatabase()
    {
        return materialTab != null
            ? materialTab.GetAllMaterials()
            : new List<BridgeMaterialSO>();
    }

    private void UpdateSearchPreview(int pageIndex, int pageCount, bool returning)
    {
        int displayPage = Mathf.Clamp(pageIndex + 1, 1, Mathf.Max(1, pageCount));
        searchProgressText.text = displayPage.ToString("00") + "  /  " + Mathf.Max(1, pageCount).ToString("00");

        if (contentType == AlmanacLearningContent.Lessons)
        {
            List<LessonData> lessons = GetLessonDatabase();
            List<LessonData> candidates = new List<LessonData>();
            foreach (LessonData lesson in lessons)
            {
                if (lesson != null && lesson != pendingLesson) candidates.Add(lesson);
            }

            LessonData preview = candidates.Count > 0 ? candidates[pageIndex % candidates.Count] : null;
            bool unlocked = preview != null && LessonSaveManager.IsUnlocked(preview);
            searchChapterText.text = returning ? "RETURNING  /  LESSON ARCHIVE" : "CHAPTER  " + displayPage.ToString("00");
            searchTitleText.text = preview == null
                ? "Engineering field notes"
                : unlocked ? preview.Title : "Sealed lesson notes";
            searchBodyText.text = preview == null
                ? "Turning through the lesson archive and restoring the main register."
                : unlocked
                    ? Shorten(preview.Description, 430)
                    : "This lesson has not been discovered yet. Its notes will be added after the related engineering activity is completed.";
            searchIndexText.text = BuildLessonRegister(lessons, preview);
            return;
        }

        List<BridgeMaterialSO> materials = GetMaterialDatabase();
        materials.Sort((a, b) => string.Compare(a.GetDisplayName(), b.GetDisplayName(),
            System.StringComparison.OrdinalIgnoreCase));
        List<BridgeMaterialSO> materialCandidates = new List<BridgeMaterialSO>();
        foreach (BridgeMaterialSO material in materials)
        {
            if (material != null && material != pendingMaterial) materialCandidates.Add(material);
        }

        BridgeMaterialSO materialPreview = materialCandidates.Count > 0
            ? materialCandidates[pageIndex % materialCandidates.Count]
            : null;
        bool discovered = materialPreview != null && MaterialDiscoverySaveManager.IsDiscovered(materialPreview);
        searchChapterText.text = returning ? "RETURNING  /  MATERIAL ARCHIVE" : "SPECIMEN  " + displayPage.ToString("00");
        searchTitleText.text = materialPreview == null
            ? "Builder's field catalogue"
            : discovered ? materialPreview.GetDisplayName() : "Undiscovered material";
        searchBodyText.text = materialPreview == null
            ? "Turning through the material archive and restoring the main register."
            : discovered
                ? Shorten(string.IsNullOrWhiteSpace(materialPreview.introductionDescription)
                    ? "A recorded construction material with properties ready for comparison."
                    : materialPreview.introductionDescription, 430)
                : "This material record remains sealed until the material is discovered in the world.";
        searchIndexText.text = BuildMaterialRegister(materials, materialPreview);
    }

    private static string BuildLessonRegister(List<LessonData> lessons, LessonData selected)
    {
        if (lessons.Count == 0) return "No lesson records are currently available.";

        StringBuilder register = new StringBuilder();
        int selectedIndex = Mathf.Max(0, lessons.IndexOf(selected));
        int lineCount = Mathf.Min(6, lessons.Count);
        for (int offset = 0; offset < lineCount; offset++)
        {
            int index = (selectedIndex + offset) % lessons.Count;
            LessonData lesson = lessons[index];
            bool unlocked = lesson != null && LessonSaveManager.IsUnlocked(lesson);
            register.Append(index == selectedIndex ? ">  " : "   ");
            register.Append("PAGE ").Append((index + 1).ToString("00")).Append("     ");
            register.Append(unlocked ? lesson.Title : "UNDISCOVERED LESSON");
            if (offset < lineCount - 1) register.Append('\n');
        }
        return register.ToString();
    }

    private static string BuildMaterialRegister(List<BridgeMaterialSO> materials, BridgeMaterialSO selected)
    {
        if (materials.Count == 0) return "No material records are currently available.";

        StringBuilder register = new StringBuilder();
        int selectedIndex = Mathf.Max(0, materials.IndexOf(selected));
        int lineCount = Mathf.Min(6, materials.Count);
        for (int offset = 0; offset < lineCount; offset++)
        {
            int index = (selectedIndex + offset) % materials.Count;
            BridgeMaterialSO material = materials[index];
            bool discovered = material != null && MaterialDiscoverySaveManager.IsDiscovered(material);
            register.Append(index == selectedIndex ? ">  " : "   ");
            register.Append("M").Append((index + 1).ToString("00")).Append("          ");
            register.Append(discovered ? material.GetDisplayName() : "UNDISCOVERED MATERIAL");
            if (offset < lineCount - 1) register.Append('\n');
        }
        return register.ToString();
    }

    private static string Shorten(string value, int maxCharacters)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Archive notes are available for this entry.";
        string trimmed = value.Trim();
        if (trimmed.Length <= maxCharacters) return trimmed;
        return trimmed.Substring(0, maxCharacters).TrimEnd() + "...";
    }

    private void CreateSectionLabel(RectTransform parent, string label, int count)
    {
        TMP_Text section = CreateText(label + "_Section", parent,
            label + "   " + count, 15f, FontStyles.Bold, MutedInk, TextAlignmentOptions.BottomLeft);
        LayoutElement element = section.gameObject.AddComponent<LayoutElement>();
        element.preferredHeight = 32f;
    }

    private void CreateIndexButton(
        RectTransform parent,
        string number,
        string title,
        Sprite icon,
        bool unlocked,
        UnityEngine.Events.UnityAction clicked)
    {
        RectTransform card = CreateRect(title + "_Entry", parent);
        LayoutElement layout = card.gameObject.AddComponent<LayoutElement>();
        layout.preferredHeight = 70f;

        Image background = card.gameObject.AddComponent<Image>();
        background.color = unlocked ? CardTint : LockedTint;
        Button button = card.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        button.interactable = unlocked;
        if (unlocked) button.onClick.AddListener(clicked);

        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 0.94f, 0.80f, 1f);
        colors.pressedColor = new Color(0.82f, 0.67f, 0.46f, 1f);
        colors.disabledColor = Color.white;
        colors.fadeDuration = 0.12f;
        button.colors = colors;

        RectTransform stripe = CreateImage("Accent", card, unlocked ? Accent : MutedInk).rectTransform;
        stripe.anchorMin = Vector2.zero;
        stripe.anchorMax = new Vector2(0f, 1f);
        stripe.pivot = new Vector2(0f, 0.5f);
        stripe.anchoredPosition = Vector2.zero;
        stripe.sizeDelta = new Vector2(5f, 0f);

        float textLeft = icon != null ? 78f : 18f;
        if (icon != null)
        {
            Image thumbnail = CreateImage("Thumbnail", card, unlocked ? Color.white : new Color(0.55f, 0.55f, 0.55f, 1f));
            thumbnail.sprite = icon;
            thumbnail.preserveAspect = true;
            thumbnail.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            thumbnail.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            thumbnail.rectTransform.pivot = new Vector2(0f, 0.5f);
            thumbnail.rectTransform.anchoredPosition = new Vector2(17f, 0f);
            thumbnail.rectTransform.sizeDelta = new Vector2(50f, 50f);
        }

        TMP_Text numberText = CreateText("Number", card, number, 13f, FontStyles.Bold,
            unlocked ? Accent : MutedInk, TextAlignmentOptions.BottomLeft);
        numberText.rectTransform.anchorMin = Vector2.zero;
        numberText.rectTransform.anchorMax = Vector2.one;
        numberText.rectTransform.offsetMin = new Vector2(textLeft, 34f);
        numberText.rectTransform.offsetMax = new Vector2(-84f, -7f);

        TMP_Text titleText = CreateText("Title", card, title, 19f, FontStyles.Normal,
            unlocked ? Ink : MutedInk, TextAlignmentOptions.TopLeft);
        titleText.enableAutoSizing = true;
        titleText.fontSizeMin = 15f;
        titleText.fontSizeMax = 19f;
        titleText.rectTransform.anchorMin = Vector2.zero;
        titleText.rectTransform.anchorMax = Vector2.one;
        titleText.rectTransform.offsetMin = new Vector2(textLeft, 7f);
        titleText.rectTransform.offsetMax = new Vector2(-82f, -31f);

        TMP_Text state = CreateText("State", card, unlocked ? "OPEN" : "LOCKED", 12f,
            FontStyles.Bold, unlocked ? Accent : MutedInk, TextAlignmentOptions.MidlineRight);
        state.rectTransform.anchorMin = new Vector2(1f, 0f);
        state.rectTransform.anchorMax = Vector2.one;
        state.rectTransform.pivot = new Vector2(1f, 0.5f);
        state.rectTransform.anchoredPosition = new Vector2(-14f, 0f);
        state.rectTransform.sizeDelta = new Vector2(68f, 36f);
    }

    private void BuildDetailSpread()
    {
        leftDetail = CreateRect("DetailLeft", leftPage).gameObject;
        rightDetail = CreateRect("DetailRight", rightPage).gameObject;
        CenterInPage(leftDetail.transform as RectTransform, 70f, 52f, 700f);
        CenterInPage(rightDetail.transform as RectTransform, 42f, 62f, 700f);

        RectTransform left = leftDetail.transform as RectTransform;
        Button back = CreateTextButton("BackToIndex", left, "<  RETURN TO INDEX", 15f, BackToIndex);
        RectTransform backRect = back.transform as RectTransform;
        backRect.anchorMin = new Vector2(0f, 1f);
        backRect.anchorMax = new Vector2(0f, 1f);
        backRect.pivot = new Vector2(0f, 1f);
        backRect.anchoredPosition = Vector2.zero;
        backRect.sizeDelta = new Vector2(190f, 38f);

        detailTypeText = CreateText("Type", left, "LESSON", 15f, FontStyles.Bold,
            Accent, TextAlignmentOptions.MidlineLeft);
        SetTop(detailTypeText.rectTransform, 54f, 24f);

        detailTitleText = CreateText("Title", left, string.Empty, 37f, FontStyles.Bold,
            Ink, TextAlignmentOptions.TopLeft);
        detailTitleText.enableAutoSizing = true;
        detailTitleText.fontSizeMin = 25f;
        detailTitleText.fontSizeMax = 37f;
        SetTop(detailTitleText.rectTransform, 82f, 92f);

        Image imageFrame = CreateImage("ImageFrame", left, CardTint);
        detailImageFrame = imageFrame.gameObject;
        SetTop(imageFrame.rectTransform, 185f, 225f);
        detailImage = CreateImage("Image", imageFrame.rectTransform, Color.white);
        detailImage.preserveAspect = true;
        Stretch(detailImage.rectTransform, 14f, 14f, 14f, 14f);

        detailFactsText = CreateText("Facts", left, string.Empty, 16f, FontStyles.Normal,
            Ink, TextAlignmentOptions.TopLeft);
        detailFactsText.enableWordWrapping = true;
        detailFactsText.lineSpacing = 7f;
        SetTop(detailFactsText.rectTransform, 425f, 120f);

        RectTransform right = rightDetail.transform as RectTransform;
        TMP_Text notesKicker = CreateText("Kicker", right, "FIELD NOTES", 15f,
            FontStyles.Bold, Accent, TextAlignmentOptions.MidlineLeft);
        SetTop(notesKicker.rectTransform, 0f, 26f);

        TMP_Text notesTitle = CreateText("Title", right, "What to remember", 29f,
            FontStyles.Bold, Ink, TextAlignmentOptions.MidlineLeft);
        SetTop(notesTitle.rectTransform, 31f, 52f);

        RectTransform rule = CreateImage("Rule", right, Accent).rectTransform;
        SetTop(rule, 91f, 2f);
        CreateDetailScroll(right, 116f);
    }

    private void BuildSearchSpread()
    {
        leftSearch = CreateRect("SearchLeft", leftPage).gameObject;
        rightSearch = CreateRect("SearchRight", rightPage).gameObject;
        CenterInPage(leftSearch.transform as RectTransform, 70f, 52f, 700f);
        CenterInPage(rightSearch.transform as RectTransform, 42f, 62f, 700f);

        RectTransform left = leftSearch.transform as RectTransform;
        Image chapterBanner = CreateImage("ChapterBanner", left, CardTint);
        SetTop(chapterBanner.rectTransform, 0f, 64f);
        RectTransform marker = CreateImage("Marker", chapterBanner.rectTransform, Accent).rectTransform;
        marker.anchorMin = Vector2.zero;
        marker.anchorMax = new Vector2(0f, 1f);
        marker.pivot = new Vector2(0f, 0.5f);
        marker.anchoredPosition = Vector2.zero;
        marker.sizeDelta = new Vector2(54f, 0f);

        searchChapterText = CreateText("Chapter", chapterBanner.rectTransform, string.Empty, 16f,
            FontStyles.Bold, Ink, TextAlignmentOptions.MidlineLeft);
        Stretch(searchChapterText.rectTransform, 72f, 18f, 8f, 8f);

        searchTitleText = CreateText("Title", left, string.Empty, 38f,
            FontStyles.Bold, Ink, TextAlignmentOptions.TopLeft);
        searchTitleText.enableAutoSizing = true;
        searchTitleText.fontSizeMin = 27f;
        searchTitleText.fontSizeMax = 38f;
        SetTop(searchTitleText.rectTransform, 94f, 132f);

        RectTransform leftRule = CreateImage("Rule", left, Accent).rectTransform;
        SetTop(leftRule, 238f, 3f);

        searchBodyText = CreateText("Body", left, string.Empty, 21f,
            FontStyles.Normal, Ink, TextAlignmentOptions.TopLeft);
        searchBodyText.enableWordWrapping = true;
        searchBodyText.lineSpacing = 9f;
        SetTop(searchBodyText.rectTransform, 266f, 300f);

        TMP_Text leafNote = CreateText("LeafNote", left, "TURNING THROUGH ARCHIVE PAGES", 14f,
            FontStyles.Italic, MutedInk, TextAlignmentOptions.BottomLeft);
        leafNote.rectTransform.anchorMin = Vector2.zero;
        leafNote.rectTransform.anchorMax = new Vector2(0.7f, 0f);
        leafNote.rectTransform.pivot = new Vector2(0f, 0f);
        leafNote.rectTransform.anchoredPosition = Vector2.zero;
        leafNote.rectTransform.sizeDelta = new Vector2(0f, 32f);

        RectTransform right = rightSearch.transform as RectTransform;
        Image progressBanner = CreateImage("ProgressBanner", right, CardTint);
        SetTop(progressBanner.rectTransform, 0f, 64f);
        TMP_Text searchLabel = CreateText("Label", progressBanner.rectTransform, "SEARCHING", 17f,
            FontStyles.Bold, Ink, TextAlignmentOptions.MidlineLeft);
        Stretch(searchLabel.rectTransform, 22f, 170f, 8f, 8f);
        searchProgressText = CreateText("Progress", progressBanner.rectTransform, string.Empty, 15f,
            FontStyles.Bold, Accent, TextAlignmentOptions.MidlineRight);
        Stretch(searchProgressText.rectTransform, 250f, 22f, 8f, 8f);

        TMP_Text indexHeading = CreateText("Heading", right,
            contentType == AlmanacLearningContent.Lessons ? "Lesson register" : "Material register",
            30f, FontStyles.Bold, Ink, TextAlignmentOptions.MidlineLeft);
        SetTop(indexHeading.rectTransform, 88f, 56f);

        RectTransform rightRule = CreateImage("Rule", right, Accent).rectTransform;
        SetTop(rightRule, 154f, 2f);

        searchIndexText = CreateText("Index", right, string.Empty, 19f,
            FontStyles.Normal, Ink, TextAlignmentOptions.TopLeft);
        searchIndexText.enableWordWrapping = true;
        searchIndexText.lineSpacing = 13f;
        SetTop(searchIndexText.rectTransform, 184f, 430f);

        leftSearch.SetActive(false);
        rightSearch.SetActive(false);
    }

    private void CreateDetailScroll(RectTransform parent, float top)
    {
        RectTransform scrollRoot = CreateRect("DetailScroll", parent);
        scrollRoot.anchorMin = Vector2.zero;
        scrollRoot.anchorMax = Vector2.one;
        scrollRoot.offsetMin = Vector2.zero;
        scrollRoot.offsetMax = new Vector2(0f, -top);

        detailScroll = scrollRoot.gameObject.AddComponent<ScrollRect>();
        detailScroll.horizontal = false;
        detailScroll.vertical = true;
        detailScroll.movementType = ScrollRect.MovementType.Clamped;
        detailScroll.scrollSensitivity = 28f;

        RectTransform viewport = CreateRect("Viewport", scrollRoot);
        Stretch(viewport, 0f, 0f, 0f, 0f);
        viewport.gameObject.AddComponent<RectMask2D>();

        detailDescriptionText = CreateText("Description", viewport, string.Empty, 20f,
            FontStyles.Normal, Ink, TextAlignmentOptions.TopLeft);
        detailDescriptionText.enableWordWrapping = true;
        detailDescriptionText.lineSpacing = 10f;
        detailDescriptionText.rectTransform.anchorMin = new Vector2(0f, 1f);
        detailDescriptionText.rectTransform.anchorMax = Vector2.one;
        detailDescriptionText.rectTransform.pivot = new Vector2(0.5f, 1f);
        detailDescriptionText.rectTransform.anchoredPosition = Vector2.zero;
        detailDescriptionText.rectTransform.sizeDelta = Vector2.zero;

        ContentSizeFitter fitter = detailDescriptionText.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        detailScroll.viewport = viewport;
        detailScroll.content = detailDescriptionText.rectTransform;
    }

    private void OpenLesson(LessonData lesson)
    {
        if (lesson == null || !LessonSaveManager.IsUnlocked(lesson) || isTransitioning) return;
        pendingLesson = lesson;
        pendingMaterial = null;
        PopulateDetail("ENGINEERING LESSON", lesson.Title, lesson.Image, lesson.Description,
            "Review the principle, then look for it in your next structure.");
        StartCoroutine(TransitionToDetail());
    }

    private void OpenMaterial(BridgeMaterialSO material)
    {
        if (material == null || !MaterialDiscoverySaveManager.IsDiscovered(material) || isTransitioning) return;
        pendingMaterial = material;
        pendingLesson = null;

        string description = string.IsNullOrWhiteSpace(material.introductionDescription)
            ? "Study this material's properties and consider where its strengths best fit your design."
            : material.introductionDescription;
        string facts =
            "COST / METER    P" + material.costPerMeter.ToString("0") + "\n" +
            "MASS / METER    " + material.GetPlacedMassPerMeter().ToString("0.##") + " kg\n" +
            "MAX LENGTH      " + material.maxLength.ToString("0.##") + " m\n" +
            "TENSION LIMIT   " + material.maxTension.ToString("N0") + " N\n" +
            "COMPRESSION     " + material.maxCompression.ToString("N0") + " N";
        PopulateDetail("BUILDING MATERIAL", material.GetDisplayName(), material.materialIcon,
            description, facts);
        StartCoroutine(TransitionToDetail());
    }

    private void PopulateDetail(string type, string title, Sprite image, string description, string facts)
    {
        detailTypeText.text = type;
        detailTitleText.text = title;
        detailDescriptionText.text = description;
        detailFactsText.text = facts;
        detailImage.sprite = image;
        detailImageFrame.SetActive(image != null);
        detailFactsText.rectTransform.anchoredPosition = new Vector2(0f, image != null ? -425f : -195f);
        Canvas.ForceUpdateCanvases();
        detailScroll.StopMovement();
        detailScroll.verticalNormalizedPosition = 1f;
    }

    private IEnumerator TransitionToDetail()
    {
        isTransitioning = true;
        manager.virtualHasPrev = false;
        manager.virtualHasNext = false;
        manager.EnableVirtualPagination(HandleVirtualPage);
        manager.ForceUpdatePaginationUI();

        yield return PlayPageSearch(true, 5, "SEARCHING THE ALMANAC");
        SetDetailVisible(true);
        showingDetail = true;
        isTransitioning = false;
    }

    private void BackToIndex()
    {
        if (!showingDetail || isTransitioning) return;
        StartCoroutine(TransitionHome());
    }

    private IEnumerator TransitionHome()
    {
        isTransitioning = true;
        yield return PlayPageSearch(false, 3, "RETURNING TO THE INDEX");
        ShowHomeImmediate();
        isTransitioning = false;
    }

    private IEnumerator PlayPageSearch(bool forward, int pageCount, string status)
    {
        ShowSearchImmediate();
        UpdateSearchPreview(0, pageCount, !forward);

        for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            RectTransform parent = forward ? rightPage : leftPage;
            RectTransform sheet = CreateRect("TurningPage_" + pageIndex, parent);
            sheet.anchorMin = Vector2.zero;
            sheet.anchorMax = Vector2.one;
            sheet.offsetMin = new Vector2(forward ? 3f : -3f, 4f);
            sheet.offsetMax = new Vector2(forward ? -3f : 3f, -4f);
            sheet.pivot = new Vector2(forward ? 0f : 1f, 0.5f);
            sheet.SetAsLastSibling();
            activeFlipSheets.Add(sheet.gameObject);

            Canvas canvas = sheet.gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 80 + pageIndex;
            CanvasGroup group = sheet.gameObject.AddComponent<CanvasGroup>();
            Image paper = sheet.gameObject.AddComponent<Image>();
            float variation = pageIndex * 0.012f;
            paper.color = new Color(PaperTint.r - variation, PaperTint.g - variation,
                PaperTint.b - variation, PaperTint.a);
            paper.raycastTarget = true;

            Shadow shadow = sheet.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0.18f, 0.10f, 0.05f, 0.28f);
            shadow.effectDistance = new Vector2(forward ? -10f : 10f, -2f);

            TMP_Text searching = CreateText("Searching", sheet, status + "   " + (pageIndex + 1) + " / " + pageCount,
                14f, FontStyles.Bold, MutedInk, TextAlignmentOptions.Bottom);
            Stretch(searching.rectTransform, 30f, 30f, 30f, 24f);

            float duration = 0.13f;
            float elapsed = 0f;
            bool revealedNextPage = false;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = Mathf.SmoothStep(0f, 1f, t);
                float angle = Mathf.Lerp(0f, forward ? -180f : 180f, eased);
                sheet.localRotation = Quaternion.Euler(0f, angle, 0f);
                sheet.localScale = new Vector3(1f, 1f + Mathf.Sin(t * Mathf.PI) * 0.018f, 1f);
                if (!revealedNextPage && t >= 0.48f)
                {
                    revealedNextPage = true;
                    UpdateSearchPreview(pageIndex + 1, pageCount, !forward);
                }
                if (t > 0.72f) group.alpha = 1f - ((t - 0.72f) / 0.28f);
                yield return null;
            }

            activeFlipSheets.Remove(sheet.gameObject);
            Destroy(sheet.gameObject);
            yield return new WaitForSecondsRealtime(0.09f);
        }

        yield return new WaitForSecondsRealtime(0.08f);
    }

    private void HandleVirtualPage(bool forward)
    {
        // The detailed spread uses its own explicit Return to Index navigation.
    }

    private void ShowHomeImmediate()
    {
        SetDetailVisible(false);
        showingDetail = false;
        if (manager != null) manager.DisableVirtualPagination(HandleVirtualPage);
        ClearFlipSheets();
    }

    private void ShowSearchImmediate()
    {
        if (leftHome != null) leftHome.SetActive(false);
        if (rightHome != null) rightHome.SetActive(false);
        if (leftDetail != null) leftDetail.SetActive(false);
        if (rightDetail != null) rightDetail.SetActive(false);
        if (leftSearch != null) leftSearch.SetActive(true);
        if (rightSearch != null) rightSearch.SetActive(true);
    }

    private void SetDetailVisible(bool visible)
    {
        if (leftHome != null) leftHome.SetActive(!visible);
        if (rightHome != null) rightHome.SetActive(!visible);
        if (leftDetail != null) leftDetail.SetActive(visible);
        if (rightDetail != null) rightDetail.SetActive(visible);
        if (leftSearch != null) leftSearch.SetActive(false);
        if (rightSearch != null) rightSearch.SetActive(false);
    }

    private void ClearFlipSheets()
    {
        foreach (GameObject sheet in activeFlipSheets)
        {
            if (sheet != null) Destroy(sheet);
        }
        activeFlipSheets.Clear();
    }

    private void CreateGuideRow(RectTransform parent, float top, string number, string heading, string body)
    {
        TMP_Text numberText = CreateText("Guide_" + number, parent, number, 16f,
            FontStyles.Bold, Accent, TextAlignmentOptions.TopLeft);
        SetTop(numberText.rectTransform, top, 54f);
        numberText.rectTransform.anchorMax = new Vector2(0.12f, 1f);

        TMP_Text text = CreateText("GuideText_" + number, parent,
            "<b>" + heading + "</b>\n" + body, 16f, FontStyles.Normal, Ink,
            TextAlignmentOptions.TopLeft);
        text.enableWordWrapping = true;
        text.lineSpacing = 4f;
        SetTop(text.rectTransform, top, 60f);
        text.rectTransform.anchorMin = new Vector2(0.14f, 1f);
    }

    private Button CreateTextButton(string objectName, Transform parent, string label,
        float fontSize, UnityEngine.Events.UnityAction clicked)
    {
        RectTransform rect = CreateRect(objectName, parent);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = CardTint;
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(clicked);
        TMP_Text text = CreateText("Label", rect, label, fontSize, FontStyles.Bold,
            Ink, TextAlignmentOptions.Midline);
        Stretch(text.rectTransform, 8f, 8f, 3f, 3f);
        return button;
    }

    private RectTransform CreateRect(string objectName, Transform parent)
    {
        GameObject child = new GameObject(objectName, typeof(RectTransform));
        child.layer = gameObject.layer;
        RectTransform rect = child.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private TMP_Text CreateText(string objectName, Transform parent, string value, float size,
        FontStyles style, Color color, TextAlignmentOptions alignment)
    {
        RectTransform rect = CreateRect(objectName, parent);
        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        if (fontSource != null)
        {
            text.font = fontSource.font;
            text.fontSharedMaterial = fontSource.fontSharedMaterial;
        }
        text.text = value;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.alignment = alignment;
        text.raycastTarget = false;
        return text;
    }

    private Image CreateImage(string objectName, Transform parent, Color color)
    {
        RectTransform rect = CreateRect(objectName, parent);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static void Stretch(RectTransform rect, float left, float right, float bottom, float top)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    private static void CenterInPage(RectTransform rect, float left, float right, float height)
    {
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(1f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2((left - right) * 0.5f, 0f);
        rect.sizeDelta = new Vector2(-(left + right), height);
    }

    private static void SetTop(RectTransform rect, float top, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -top);
        rect.sizeDelta = new Vector2(0f, height);
    }
}
