using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public enum AlmanacLearningContent { Lessons, Materials }

public sealed class AlmanacLearningHub : MonoBehaviour
{
    private static readonly Color Ink = new Color(0.25f, 0.16f, 0.10f, 1f);
    private static readonly Color MutedInk = new Color(0.47f, 0.35f, 0.25f, 1f);
    private static readonly Color Accent = new Color(0.66f, 0.36f, 0.13f, 1f);
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
    private TMP_Text detailTypeText;
    private TMP_Text detailTitleText;
    private TMP_Text detailDescriptionText;
    private TMP_Text detailFactsText;
    private Image detailImage;
    private GameObject detailImageFrame;
    private ScrollRect detailScroll;
    private TMP_Text fontSource;
    private RectTransform indexContent;
    private bool built;
    private bool showingDetail;
    private bool isTransitioning;

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
        PopulateDetail("ENGINEERING LESSON", lesson.Title, lesson.Image, lesson.Description,
            "Review the principle, then look for it in your next structure.");
        StartCoroutine(TransitionToDetail());
    }

    private void OpenMaterial(BridgeMaterialSO material)
    {
        if (material == null || !MaterialDiscoverySaveManager.IsDiscovered(material) || isTransitioning) return;
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

        yield return AnimateSpreadSwap(leftHome, rightHome, leftDetail, rightDetail);
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
        yield return AnimateSpreadSwap(leftDetail, rightDetail, leftHome, rightHome);
        ShowHomeImmediate();
        isTransitioning = false;
    }

    private IEnumerator AnimateSpreadSwap(
        GameObject outgoingLeft,
        GameObject outgoingRight,
        GameObject incomingLeft,
        GameObject incomingRight)
    {
        GameObject[] outgoing = { outgoingLeft, outgoingRight };
        GameObject[] incoming = { incomingLeft, incomingRight };

        foreach (GameObject page in incoming)
        {
            if (page == null) continue;
            page.SetActive(true);
            CanvasGroup group = GetOrAddCanvasGroup(page);
            group.alpha = 0f;
            page.transform.localScale = new Vector3(0.985f, 0.992f, 1f);
        }

        const float duration = 0.24f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);

            foreach (GameObject page in outgoing)
            {
                if (page == null) continue;
                CanvasGroup group = GetOrAddCanvasGroup(page);
                group.alpha = 1f - eased;
                page.transform.localScale = new Vector3(
                    Mathf.Lerp(1f, 0.985f, eased),
                    Mathf.Lerp(1f, 0.992f, eased), 1f);
            }

            foreach (GameObject page in incoming)
            {
                if (page == null) continue;
                CanvasGroup group = GetOrAddCanvasGroup(page);
                group.alpha = eased;
                page.transform.localScale = new Vector3(
                    Mathf.Lerp(0.985f, 1f, eased),
                    Mathf.Lerp(0.992f, 1f, eased), 1f);
            }

            yield return null;
        }

        foreach (GameObject page in outgoing)
        {
            ResetTransitionVisual(page);
            if (page != null) page.SetActive(false);
        }
        foreach (GameObject page in incoming) ResetTransitionVisual(page);
    }

    private static CanvasGroup GetOrAddCanvasGroup(GameObject page)
    {
        CanvasGroup group = page.GetComponent<CanvasGroup>();
        return group != null ? group : page.AddComponent<CanvasGroup>();
    }

    private static void ResetTransitionVisual(GameObject page)
    {
        if (page == null) return;
        CanvasGroup group = page.GetComponent<CanvasGroup>();
        if (group != null) group.alpha = 1f;
        page.transform.localScale = Vector3.one;
    }

    private void HandleVirtualPage(bool forward)
    {
        // The detailed spread uses its own explicit Return to Index navigation.
    }

    private void ShowHomeImmediate()
    {
        ResetTransitionVisual(leftHome);
        ResetTransitionVisual(rightHome);
        ResetTransitionVisual(leftDetail);
        ResetTransitionVisual(rightDetail);
        SetDetailVisible(false);
        showingDetail = false;
        if (manager != null) manager.DisableVirtualPagination(HandleVirtualPage);
    }

    private void SetDetailVisible(bool visible)
    {
        if (leftHome != null) leftHome.SetActive(!visible);
        if (rightHome != null) rightHome.SetActive(!visible);
        if (leftDetail != null) leftDetail.SetActive(visible);
        if (rightDetail != null) rightDetail.SetActive(visible);
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
