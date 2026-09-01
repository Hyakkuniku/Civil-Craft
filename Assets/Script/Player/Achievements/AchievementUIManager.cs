using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AchievementUIManager : MonoBehaviour
{
    public enum AchievementFilter
    {
        All,
        Complete,
        Incomplete
    }

    [Header("Panel Visibility")]
    [Tooltip("Drag the main Achievement Panel here so it can be toggled on/off.")]
    public GameObject achievementPanel;

    [Header("UI Setup")]
    [Tooltip("The Content object inside your Scroll View (must have a Vertical Layout Group)")]
    public Transform contentParent; 
    [Tooltip("The Row Prefab with the AchievementRowUI script attached")]
    public GameObject achievementRowPrefab;

    [Header("Database")]
    [Tooltip("Drag all your Achievement ScriptableObjects here!")]
    public List<AchievementSO> allAchievements = new List<AchievementSO>();

    [Header("Filters")]
    [SerializeField] private Toggle allToggle;
    [SerializeField] private Toggle completeToggle;
    [SerializeField] private Toggle incompleteToggle;
    [SerializeField] private TMP_Text unlockedCountText;

    private PlayerDataManager boundDataManager;
    private bool panelWasOpen;
    private bool filterListenersRegistered;
    private bool refreshWhenDataIsReady;
    private bool achievementCheckQueued;
    private AchievementFilter activeFilter = AchievementFilter.All;

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) return;

        string[] guids = UnityEditor.AssetDatabase.FindAssets("t:AchievementSO");
        List<AchievementSO> discovered = new List<AchievementSO>();
        foreach (string guid in guids)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            AchievementSO achievement = UnityEditor.AssetDatabase.LoadAssetAtPath<AchievementSO>(path);
            if (achievement != null) discovered.Add(achievement);
        }
        discovered.Sort((left, right) => string.Compare(
            left.achievementID,
            right.achievementID,
            System.StringComparison.OrdinalIgnoreCase));

        bool changed = allAchievements == null || allAchievements.Count != discovered.Count;
        if (!changed)
        {
            for (int i = 0; i < discovered.Count; i++)
            {
                if (allAchievements[i] == discovered[i]) continue;
                changed = true;
                break;
            }
        }

        if (!changed) return;
        allAchievements = discovered;
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif

    private void Awake()
    {
        ResolveFilterReferences();
        ApplyReferencePanelLayout();
        RegisterFilterListeners();

        // Keep the panel closed by default when the game starts
        if (achievementPanel != null) 
        {
            achievementPanel.SetActive(false);
        }
    }

    private void Start()
    {
        BindToPlayerData();
        panelWasOpen = achievementPanel != null && achievementPanel.activeSelf;
    }

    private void Update()
    {
        BindToPlayerData();

        // Some existing scene buttons activate AchievementsPanel directly
        // instead of calling OpenPanel. Detect that path and refresh once.
        bool panelIsOpen = achievementPanel != null && achievementPanel.activeSelf;
        if (panelIsOpen && (!panelWasOpen || refreshWhenDataIsReady))
        {
            if (!panelWasOpen && UIPanelCoordinator.Instance != null &&
                !UIPanelCoordinator.Instance.IsOpen(achievementPanel))
            {
                // The panel was activated by an existing Inspector event.
                // Register that already-open panel so other UI is hidden and
                // can be restored correctly when Achievements closes.
                UIPanelCoordinator.Instance.OpenPanel(achievementPanel, false);
            }

            ResolveFilterReferences();
            RegisterFilterListeners();
            RefreshUI();
        }

        if (!panelIsOpen && panelWasOpen && UIPanelCoordinator.Instance != null &&
            UIPanelCoordinator.Instance.IsOpen(achievementPanel))
        {
            // Supports existing Close buttons that still call SetActive(false).
            UIPanelCoordinator.Instance.ClosePanel(achievementPanel);
        }
        panelWasOpen = panelIsOpen;
    }

    private void OnDestroy()
    {
        if (boundDataManager != null)
            boundDataManager.OnAchievementUnlocked -= HandleAchievementUnlocked;

        UnregisterFilterListeners();
    }

    // --- NEW: Methods to hook up to your UI Buttons ---
    public void OpenPanel()
    {
        if (achievementPanel != null) 
        {
            if (UIPanelCoordinator.Instance != null)
                UIPanelCoordinator.Instance.OpenPanel(achievementPanel);
            else
                achievementPanel.SetActive(true);

            ResolveFilterReferences();
            ApplyReferencePanelLayout();
            RegisterFilterListeners();
            SyncFilterToggleState();
            RefreshUI(); // Automatically populate the list every time it is opened!
            panelWasOpen = true;
        }
    }

    public void ClosePanel()
    {
        if (achievementPanel != null) 
        {
            if (UIPanelCoordinator.Instance != null)
                UIPanelCoordinator.Instance.ClosePanel(achievementPanel);
            else
                achievementPanel.SetActive(false);

            panelWasOpen = false;
        }
    }

    public void RefreshUI()
    {
        if (contentParent == null || achievementRowPrefab == null)
        {
            Debug.LogError("AchievementUIManager is missing its Content or Row Prefab reference.", this);
            return;
        }

        // 1. Clear old rows to prevent duplicates
        foreach(Transform child in contentParent)
        {
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }

        if (PlayerDataManager.Instance == null || PlayerDataManager.Instance.CurrentData == null)
        {
            refreshWhenDataIsReady = true;
            return;
        }

        refreshWhenDataIsReady = false;

        PlayerData data = PlayerDataManager.Instance.CurrentData;

        // 2. Separate them into two lists for sorting/filtering.
        List<AchievementSO> completedList = new List<AchievementSO>();
        List<AchievementSO> inProgressList = new List<AchievementSO>();

        foreach(AchievementSO ach in allAchievements)
        {
            if (ach == null) continue;
            if (data.unlockedAchievements.Contains(ach.achievementID))
            {
                completedList.Add(ach);
            }
            else
            {
                inProgressList.Add(ach);
            }
        }

        if (unlockedCountText != null)
            unlockedCountText.text = $"{completedList.Count}/{completedList.Count + inProgressList.Count} Unlocked";

        // All keeps unfinished goals first. The other tabs show only their state.
        if (activeFilter != AchievementFilter.Complete)
        {
            foreach(AchievementSO ach in inProgressList)
                CreateRow(ach, false);
        }

        if (activeFilter != AchievementFilter.Incomplete)
        {
            foreach(AchievementSO ach in completedList)
                CreateRow(ach, true);
        }


        Canvas.ForceUpdateCanvases();
        ScrollRect scrollRect = contentParent.GetComponentInParent<ScrollRect>();
        LayoutRowsManually(scrollRect);
        if (scrollRect != null)
            scrollRect.verticalNormalizedPosition = 1f;
    }

    public void ShowAll() => SetFilter(AchievementFilter.All);
    public void ShowComplete() => SetFilter(AchievementFilter.Complete);
    public void ShowIncomplete() => SetFilter(AchievementFilter.Incomplete);

    private void SetFilter(AchievementFilter filter)
    {
        activeFilter = filter;
        SyncFilterToggleState();

        if (achievementPanel != null && achievementPanel.activeInHierarchy)
            RefreshUI();
    }

    private void RegisterFilterListeners()
    {
        if (filterListenersRegistered) return;

        if (allToggle != null) allToggle.onValueChanged.AddListener(HandleAllToggle);
        if (completeToggle != null) completeToggle.onValueChanged.AddListener(HandleCompleteToggle);
        if (incompleteToggle != null) incompleteToggle.onValueChanged.AddListener(HandleIncompleteToggle);

        filterListenersRegistered = allToggle != null || completeToggle != null || incompleteToggle != null;
    }

    private void UnregisterFilterListeners()
    {
        if (allToggle != null) allToggle.onValueChanged.RemoveListener(HandleAllToggle);
        if (completeToggle != null) completeToggle.onValueChanged.RemoveListener(HandleCompleteToggle);
        if (incompleteToggle != null) incompleteToggle.onValueChanged.RemoveListener(HandleIncompleteToggle);
        filterListenersRegistered = false;
    }

    private void ResolveFilterReferences()
    {
        if (achievementPanel == null) return;

        Transform filterBar = FindDescendant(achievementPanel.transform, "AchievementFilterBar");
        if (filterBar == null) return;

        Toggle resolvedAll = FindDescendant(filterBar, "AllFilter")?.GetComponent<Toggle>();
        Toggle resolvedComplete = FindDescendant(filterBar, "CompleteFilter")?.GetComponent<Toggle>();
        Toggle resolvedIncomplete = FindDescendant(filterBar, "IncompleteFilter")?.GetComponent<Toggle>();
        TMP_Text resolvedCount = FindDescendant(filterBar, "UnlockedCount")?.GetComponent<TMP_Text>();

        bool referencesChanged = resolvedAll != null && resolvedAll != allToggle ||
                                 resolvedComplete != null && resolvedComplete != completeToggle ||
                                 resolvedIncomplete != null && resolvedIncomplete != incompleteToggle;
        if (referencesChanged && filterListenersRegistered)
            UnregisterFilterListeners();

        if (resolvedAll != null) allToggle = resolvedAll;
        if (resolvedComplete != null) completeToggle = resolvedComplete;
        if (resolvedIncomplete != null) incompleteToggle = resolvedIncomplete;
        if (resolvedCount != null) unlockedCountText = resolvedCount;

        ToggleGroup group = filterBar.GetComponent<ToggleGroup>();
        if (group == null) group = filterBar.gameObject.AddComponent<ToggleGroup>();
        group.allowSwitchOff = false;

        PrepareToggle(allToggle, group);
        PrepareToggle(completeToggle, group);
        PrepareToggle(incompleteToggle, group);
        SyncFilterToggleState();
    }

    private static void PrepareToggle(Toggle toggle, ToggleGroup group)
    {
        if (toggle == null) return;

        toggle.enabled = true;
        toggle.interactable = true;
        toggle.group = group;

        Image hitArea = toggle.GetComponent<Image>();
        if (hitArea != null)
            hitArea.raycastTarget = true;

        Graphic checkmark = toggle.graphic;
        if (checkmark != null)
            checkmark.raycastTarget = false;

        TMP_Text label = toggle.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
            label.raycastTarget = false;
    }

    private void SyncFilterToggleState()
    {
        if (allToggle != null)
            allToggle.SetIsOnWithoutNotify(activeFilter == AchievementFilter.All);
        if (completeToggle != null)
            completeToggle.SetIsOnWithoutNotify(activeFilter == AchievementFilter.Complete);
        if (incompleteToggle != null)
            incompleteToggle.SetIsOnWithoutNotify(activeFilter == AchievementFilter.Incomplete);
    }

    private void HandleAllToggle(bool selected)
    {
        if (selected) SetFilter(AchievementFilter.All);
    }

    private void HandleCompleteToggle(bool selected)
    {
        if (selected) SetFilter(AchievementFilter.Complete);
    }

    private void HandleIncompleteToggle(bool selected)
    {
        if (selected) SetFilter(AchievementFilter.Incomplete);
    }

    private void BindToPlayerData()
    {
        PlayerDataManager current = PlayerDataManager.Instance;
        if (current == null || current == boundDataManager) return;

        if (boundDataManager != null)
            boundDataManager.OnAchievementUnlocked -= HandleAchievementUnlocked;

        boundDataManager = current;
        boundDataManager.OnAchievementUnlocked += HandleAchievementUnlocked;
        MergeAchievements(boundDataManager.allGameAchievements);
        MergeAchievements(Resources.FindObjectsOfTypeAll<AchievementSO>());
        boundDataManager.RegisterAchievements(allAchievements);

        // PopupNotification binds during scene startup. Delay the first check
        // so its listener cannot miss an achievement that was already earned.
        if (!achievementCheckQueued)
        {
            achievementCheckQueued = true;
            StartCoroutine(CheckRegisteredAchievementsNextFrame(boundDataManager));
        }
    }

    private void MergeAchievements(IEnumerable<AchievementSO> achievements)
    {
        if (achievements == null) return;
        if (allAchievements == null) allAchievements = new List<AchievementSO>();

        foreach (AchievementSO achievement in achievements)
        {
            if (achievement == null || string.IsNullOrWhiteSpace(achievement.achievementID))
                continue;

            int index = allAchievements.FindIndex(item => item != null &&
                item.achievementID == achievement.achievementID);
            if (index >= 0) allAchievements[index] = achievement;
            else allAchievements.Add(achievement);
        }
    }

    private System.Collections.IEnumerator CheckRegisteredAchievementsNextFrame(PlayerDataManager manager)
    {
        yield return null;
        yield return null;

        if (manager != null)
            manager.CheckAllAchievements();
        achievementCheckQueued = false;
    }

    private void HandleAchievementUnlocked(AchievementSO achievement)
    {
        if (achievementPanel != null && achievementPanel.activeInHierarchy)
            RefreshUI();
    }

    private void CreateRow(AchievementSO achievement, bool isCompleted)
    {
        GameObject rowObj = Instantiate(achievementRowPrefab, contentParent);
        rowObj.SetActive(true);

        if (rowObj.transform is RectTransform rowRect)
        {
            rowRect.anchorMin = new Vector2(0f, 1f);
            rowRect.anchorMax = new Vector2(1f, 1f);
            rowRect.pivot = new Vector2(0.5f, 1f);
            rowRect.anchoredPosition = Vector2.zero;
            rowRect.sizeDelta = new Vector2(0f, 138f);
        }

        AchievementRowUI rowUI = rowObj.GetComponent<AchievementRowUI>();

        if (rowUI != null)
        {
            int currentProg = PlayerDataManager.Instance.GetAchievementProgress(achievement);
            int targetProgress = PlayerDataManager.Instance.GetAchievementTarget(achievement);
            rowUI.Setup(achievement, currentProg, isCompleted, targetProgress);
        }
        else
        {
            Debug.LogError("Achievement row prefab is missing AchievementRowUI.", rowObj);
        }
    }

    public void ApplyReferencePanelLayout()
    {
        if (achievementPanel == null) return;

        Transform scrollTransform = FindDescendant(achievementPanel.transform, "Scroll View");
        if (scrollTransform == null) return;

        RectTransform scrollRectTransform = scrollTransform as RectTransform;
        if (scrollRectTransform != null)
        {
            scrollRectTransform.anchorMin = Vector2.zero;
            scrollRectTransform.anchorMax = Vector2.one;
            scrollRectTransform.offsetMin = new Vector2(50f, 35f);
            scrollRectTransform.offsetMax = new Vector2(-50f, -165f);
        }

        Image listBackground = scrollTransform.GetComponent<Image>();
        if (listBackground != null)
        {
            listBackground.color = new Color(0.94f, 0.90f, 0.77f, 0.98f);
            listBackground.raycastTarget = true;
        }

        Outline listOutline = GetOrAdd<Outline>(scrollTransform.gameObject);
        listOutline.effectColor = new Color(0.31f, 0.21f, 0.14f, 0.72f);
        listOutline.effectDistance = new Vector2(2f, -2f);

        ScrollRect scroll = scrollTransform.GetComponent<ScrollRect>();
        if (scroll == null) return;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 34f;

        RectTransform viewport = scroll.viewport;
        if (viewport == null)
        {
            Transform viewportTransform = FindDescendant(scrollTransform, "Viewport");
            viewport = viewportTransform as RectTransform;
            scroll.viewport = viewport;
        }

        if (viewport != null)
        {
            // These scenes originally used a legacy stencil Mask. The styling
            // pass later added RectMask2D as well; two mask systems on the same
            // viewport can cull every generated row while leaving the scrollbar
            // visible. Keep only RectMask2D active.
            Mask legacyMask = viewport.GetComponent<Mask>();
            if (legacyMask != null)
                legacyMask.enabled = false;

            RectMask2D rectMask = GetOrAdd<RectMask2D>(viewport.gameObject);
            rectMask.enabled = true;
            rectMask.padding = Vector4.zero;
            rectMask.softness = Vector2Int.zero;
            Image viewportImage = viewport.GetComponent<Image>();
            if (viewportImage != null)
            {
                viewportImage.color = new Color(1f, 1f, 1f, 0.02f);
                viewportImage.raycastTarget = true;
            }
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(10f, 10f);
            viewport.offsetMax = new Vector2(-34f, -10f);
        }

        RectTransform contentRect = contentParent as RectTransform;
        if (contentRect != null)
        {
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            // Preserve the fitter-controlled height while guaranteeing that
            // the content stretches to the viewport width.
            contentRect.sizeDelta = new Vector2(0f, contentRect.sizeDelta.y);
            contentRect.localScale = Vector3.one;
            scroll.content = contentRect;

            VerticalLayoutGroup layout = GetOrAdd<VerticalLayoutGroup>(contentRect.gameObject);
            layout.padding = new RectOffset(10, 10, 10, 10);
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.enabled = false;

            ContentSizeFitter fitter = GetOrAdd<ContentSizeFitter>(contentRect.gameObject);
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.enabled = false;
        }

        Scrollbar scrollbar = GetOrCreateScrollbar(scrollTransform);
        scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        scroll.verticalScrollbarSpacing = 8f;

        Transform filterBar = FindDescendant(achievementPanel.transform, "AchievementFilterBar");
        if (filterBar != null)
            StyleFilterBar(filterBar);
    }

    private void StyleFilterBar(Transform filterBar)
    {
        RectTransform rect = filterBar as RectTransform;
        if (rect != null)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -72f);
            rect.sizeDelta = new Vector2(-100f, 76f);
        }

        Image background = filterBar.GetComponent<Image>();
        if (background != null)
        {
            background.color = new Color(0.73f, 0.67f, 0.55f, 0.97f);
            // The individual Toggle root Images are the click targets. Keeping
            // the decorative bar out of raycasts prevents it stealing taps.
            background.raycastTarget = false;
        }

        StyleFilterToggle(allToggle, 24f);
        StyleFilterToggle(completeToggle, 24f);
        StyleFilterToggle(incompleteToggle, 24f);

        if (unlockedCountText != null)
        {
            unlockedCountText.fontSize = 25f;
            unlockedCountText.fontStyle = FontStyles.Bold;
            unlockedCountText.color = new Color(0.20f, 0.13f, 0.09f, 1f);
            unlockedCountText.alignment = TextAlignmentOptions.MidlineRight;
        }
    }

    private static void StyleFilterToggle(Toggle toggle, float fontSize)
    {
        if (toggle == null) return;

        Image rootImage = toggle.GetComponent<Image>();
        if (rootImage != null)
        {
            rootImage.color = new Color(1f, 1f, 1f, 0f);
            rootImage.raycastTarget = true;
        }

        toggle.enabled = true;
        toggle.interactable = true;

        Transform boxTransform = toggle.transform.Find("Box");
        GameObject boxObject;
        if (boxTransform == null)
        {
            boxObject = new GameObject("Box", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline));
            boxObject.layer = 5;
            boxObject.transform.SetParent(toggle.transform, false);
            boxTransform = boxObject.transform;
        }
        else
        {
            boxObject = boxTransform.gameObject;
        }

        RectTransform boxRect = boxTransform as RectTransform;
        boxRect.anchorMin = boxRect.anchorMax = new Vector2(0f, 0.5f);
        boxRect.pivot = new Vector2(0f, 0.5f);
        boxRect.anchoredPosition = new Vector2(10f, 0f);
        boxRect.sizeDelta = new Vector2(34f, 34f);

        Image boxImage = GetOrAdd<Image>(boxObject);
        boxImage.color = new Color(0.91f, 0.86f, 0.72f, 1f);
        boxImage.raycastTarget = false;
        Outline boxOutline = GetOrAdd<Outline>(boxObject);
        boxOutline.effectColor = new Color(0.35f, 0.23f, 0.14f, 0.85f);
        boxOutline.effectDistance = new Vector2(2f, -2f);

        Graphic checkGraphic = toggle.graphic;
        if (checkGraphic != null)
        {
            checkGraphic.transform.SetParent(boxTransform, false);
            RectTransform checkRect = checkGraphic.rectTransform;
            checkRect.anchorMin = new Vector2(0.5f, 0.5f);
            checkRect.anchorMax = new Vector2(0.5f, 0.5f);
            checkRect.pivot = new Vector2(0.5f, 0.5f);
            checkRect.anchoredPosition = Vector2.zero;
            checkRect.sizeDelta = new Vector2(22f, 22f);
            checkGraphic.color = new Color(0.84f, 0.53f, 0.12f, 1f);
            checkGraphic.raycastTarget = false;
        }

        TMP_Text label = toggle.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
        {
            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(55f, 0f);
            labelRect.offsetMax = new Vector2(-4f, 0f);
            label.fontSize = fontSize;
            label.fontStyle = FontStyles.Bold;
            label.color = new Color(0.20f, 0.13f, 0.09f, 1f);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.raycastTarget = false;
        }

        toggle.targetGraphic = rootImage != null ? rootImage : boxImage;
        toggle.transition = Selectable.Transition.ColorTint;
        ColorBlock colors = toggle.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 0.90f, 0.65f, 1f);
        colors.selectedColor = new Color(0.96f, 0.68f, 0.25f, 1f);
        colors.pressedColor = new Color(0.84f, 0.54f, 0.16f, 1f);
        toggle.colors = colors;
    }

    private static Scrollbar GetOrCreateScrollbar(Transform scrollTransform)
    {
        Transform existing = scrollTransform.Find("AchievementScrollbar");
        GameObject scrollbarObject;
        if (existing == null)
        {
            scrollbarObject = new GameObject(
                "AchievementScrollbar", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image), typeof(Scrollbar));
            scrollbarObject.layer = 5;
            scrollbarObject.transform.SetParent(scrollTransform, false);
        }
        else
        {
            scrollbarObject = existing.gameObject;
        }

        RectTransform scrollbarRect = scrollbarObject.GetComponent<RectTransform>();
        scrollbarRect.anchorMin = new Vector2(1f, 0f);
        scrollbarRect.anchorMax = new Vector2(1f, 1f);
        scrollbarRect.pivot = new Vector2(1f, 0.5f);
        scrollbarRect.anchoredPosition = new Vector2(-10f, 0f);
        scrollbarRect.sizeDelta = new Vector2(14f, -20f);
        scrollbarObject.transform.SetAsLastSibling();

        Image track = scrollbarObject.GetComponent<Image>();
        track.color = new Color(0.31f, 0.21f, 0.14f, 0.28f);

        Transform handleTransform = scrollbarObject.transform.Find("Handle");
        GameObject handleObject;
        if (handleTransform == null)
        {
            handleObject = new GameObject("Handle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            handleObject.layer = 5;
            handleObject.transform.SetParent(scrollbarObject.transform, false);
            handleTransform = handleObject.transform;
        }
        else
        {
            handleObject = handleTransform.gameObject;
        }

        RectTransform handleRect = handleTransform as RectTransform;
        handleRect.anchorMin = Vector2.zero;
        handleRect.anchorMax = Vector2.one;
        handleRect.offsetMin = new Vector2(2f, 2f);
        handleRect.offsetMax = new Vector2(-2f, -2f);

        Image handle = handleObject.GetComponent<Image>();
        handle.color = new Color(0.38f, 0.24f, 0.13f, 0.92f);

        Scrollbar scrollbar = scrollbarObject.GetComponent<Scrollbar>();
        scrollbar.handleRect = handleRect;
        scrollbar.targetGraphic = handle;
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scrollbar.numberOfSteps = 0;
        return scrollbar;
    }

    private void LayoutRowsManually(ScrollRect scrollRect)
    {
        RectTransform contentRect = contentParent as RectTransform;
        if (contentRect == null) return;

        const float horizontalPadding = 10f;
        const float topPadding = 10f;
        const float bottomPadding = 10f;
        const float rowHeight = 138f;
        const float spacing = 12f;

        List<RectTransform> rows = new List<RectTransform>();
        for (int i = 0; i < contentRect.childCount; i++)
        {
            RectTransform row = contentRect.GetChild(i) as RectTransform;
            if (row != null && row.gameObject.activeSelf)
                rows.Add(row);
        }

        float contentHeight = topPadding + bottomPadding;
        if (rows.Count > 0)
            contentHeight += rows.Count * rowHeight + (rows.Count - 1) * spacing;

        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = new Vector2(0f, contentHeight);

        for (int i = 0; i < rows.Count; i++)
        {
            RectTransform row = rows[i];
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(1f, 1f);
            row.pivot = new Vector2(0.5f, 1f);
            row.anchoredPosition = new Vector2(
                0f,
                -(topPadding + i * (rowHeight + spacing)));
            row.sizeDelta = new Vector2(-horizontalPadding * 2f, rowHeight);
            row.localScale = Vector3.one;

            AchievementRowUI rowUI = row.GetComponent<AchievementRowUI>();
            if (rowUI != null)
                rowUI.ApplyReferenceLayout();
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
        if (scrollRect != null && scrollRect.viewport != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(scrollRect.viewport);
    }

    private static Transform FindDescendant(Transform root, string objectName)
    {
        if (root == null) return null;
        if (root.name == objectName) return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform result = FindDescendant(root.GetChild(i), objectName);
            if (result != null) return result;
        }

        return null;
    }

    private static T GetOrAdd<T>(GameObject owner) where T : Component
    {
        T component = owner.GetComponent<T>();
        return component != null ? component : owner.AddComponent<T>();
    }
}
