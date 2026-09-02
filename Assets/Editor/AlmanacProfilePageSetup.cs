#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[InitializeOnLoad]
public static class AlmanacProfilePageSetup
{
    private const string SessionKey = "CivilCraft.AlmanacProfilePage.FinalWiring.v1";
    private const string LeftDesignName = "ProfilePageDesign";
    private const string RightDesignName = "ProfileStatsDesign";

    private static readonly string[] ScenePaths =
    {
        "Assets/Scenes/CanyonCrossing.unity",
        "Assets/Scenes/BHAN HOUSE.unity"
    };

    private static readonly Color Ink = Hex("3D291C");
    private static readonly Color MutedInk = Hex("6C513A");
    private static readonly Color Paper = Hex("F8EACD", 0.96f);
    private static readonly Color Card = Hex("F3DFC0", 0.92f);
    private static readonly Color CardLight = Hex("FFF5DF", 0.94f);
    private static readonly Color Gold = Hex("D89A39");
    private static readonly Color Brown = Hex("80502D");
    private static readonly Color Blue = Hex("338BCB");
    private static readonly Color Green = Hex("6F934F");
    private static readonly Color Line = Hex("B98D5A", 0.72f);

    static AlmanacProfilePageSetup()
    {
        EditorApplication.delayCall += RunAutomaticSetupOnce;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
    }

    [MenuItem("Tools/Civil Craft/Style Almanac Profile Pages")]
    public static void StyleAlmanacProfilePages()
    {
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            "Assets/TextMesh Pro/Resources/Fonts & Materials/Bekind Sans SDF.asset");
        Sprite star = AssetDatabase.LoadAssetAtPath<Sprite>(
            "Assets/Images/UI/Yellow/star.png");

        foreach (string scenePath in ScenePaths)
            StyleScene(scenePath, font, star);

        AssetDatabase.SaveAssets();
        Debug.Log("[AlmanacProfilePageSetup] Engineer Profile pages are styled and wired.");
    }

    private static void RunAutomaticSetupOnce()
    {
        if (SessionState.GetBool(SessionKey, false)) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += RunAutomaticSetupOnce;
            return;
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        FinalizeExistingProfileWiring();
        SessionState.SetBool(SessionKey, true);
    }

    private static void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode)
            EditorApplication.delayCall += RunAutomaticSetupOnce;
    }

    [MenuItem("Tools/Civil Craft/Finalize Existing Almanac Profile Wiring")]
    public static void FinalizeExistingProfileWiring()
    {
        foreach (string scenePath in ScenePaths)
            FinalizeSceneWiring(scenePath);

        AssetDatabase.SaveAssets();
        Debug.Log("[AlmanacProfilePageSetup] Existing Almanac layout preserved; final wiring repaired.");
    }

    private static void FinalizeSceneWiring(string scenePath)
    {
        Scene scene = SceneManager.GetSceneByPath(scenePath);
        bool openedForSetup = !scene.IsValid() || !scene.isLoaded;
        if (openedForSetup)
            scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);

        AlmanacPlayerStats stats = FindStatsPage(scene);
        if (stats == null)
        {
            if (openedForSetup) EditorSceneManager.CloseScene(scene, true);
            return;
        }

        Transform design = FindDescendant(stats.transform, RightDesignName);
        if (design == null) design = stats.transform;

        Transform fillTransform = FindDescendant(design, "ExpProgressFill");
        Image fill = fillTransform != null ? fillTransform.GetComponent<Image>() : null;
        if (fill != null)
        {
            fill.type = Image.Type.Simple;
            fill.raycastTarget = false;
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = Vector2.one;
            fill.rectTransform.offsetMin = Vector2.zero;
            fill.rectTransform.offsetMax = Vector2.zero;
            stats.expProgressFill = fill;
        }

        Transform cardTransform = FindDescendant(design, "AchievementSummaryCard");
        if (cardTransform != null)
        {
            Image cardImage = cardTransform.GetComponent<Image>();
            if (cardImage != null) cardImage.raycastTarget = true;

            Button cardButton = cardTransform.GetComponent<Button>();
            if (cardButton == null)
                cardButton = cardTransform.gameObject.AddComponent<Button>();
            if (cardImage != null) cardButton.targetGraphic = cardImage;
            stats.achievementPanelButton = cardButton;

            Transform obsoleteButton = FindDescendant(cardTransform, "OpenAchievementsButton");
            if (obsoleteButton != null && obsoleteButton != cardTransform)
                obsoleteButton.gameObject.SetActive(false);
        }

        EditorUtility.SetDirty(stats);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        if (openedForSetup) EditorSceneManager.CloseScene(scene, true);
    }

    private static void StyleScene(
        string scenePath,
        TMP_FontAsset font,
        Sprite star)
    {
        Scene scene = SceneManager.GetSceneByPath(scenePath);
        bool openedForSetup = !scene.IsValid() || !scene.isLoaded;
        if (openedForSetup)
            scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);

        AlmanacPlayerStats stats = FindStatsPage(scene);
        Transform modelPage = FindSceneTransform(scene, "Page1_Model");
        if (stats == null || modelPage == null)
        {
            Debug.LogWarning($"[AlmanacProfilePageSetup] Profile pages were not found in {scenePath}.");
            if (openedForSetup) EditorSceneManager.CloseScene(scene, true);
            return;
        }

        ConfigureBookAndProfilePages(stats, modelPage);
        ConfigureTabsAndNavigation(scene);
        BuildLeftPage(modelPage, stats, font);
        BuildRightPage(stats.transform, stats, font, star);

        EditorUtility.SetDirty(stats);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        if (openedForSetup) EditorSceneManager.CloseScene(scene, true);
    }

    private static void BuildLeftPage(
        Transform page,
        AlmanacPlayerStats stats,
        TMP_FontAsset font)
    {
        RemoveGeneratedChild(page, LeftDesignName);

        RawImage sourcePortrait = page.GetComponent<RawImage>();
        Texture portraitTexture = sourcePortrait != null ? sourcePortrait.texture : null;
        Rect sourceUv = sourcePortrait != null ? sourcePortrait.uvRect : new Rect(0f, 0f, 1f, 1f);
        if (sourcePortrait != null)
        {
            sourcePortrait.enabled = false;
            sourcePortrait.raycastTarget = false;
        }

        // The old parent Image is the oversized wooden portrait frame. Keep its
        // transform as a layout container, but do not render or raycast it.
        Image oldPortraitFrame = page.parent != null ? page.parent.GetComponent<Image>() : null;
        if (oldPortraitFrame != null)
        {
            oldPortraitFrame.enabled = false;
            oldPortraitFrame.raycastTarget = false;
        }

        RectTransform design = CreateRect(LeftDesignName, page);
        Stretch(design);

        RectTransform header = CreateRect("ProfileHeader", design);
        SetRect(header, new Vector2(0.10f, 0.84f), new Vector2(0.90f, 0.95f));
        TMP_Text title = CreateText(
            header, "ProfileTitle", "ENGINEER PROFILE", font, 34f,
            FontStyles.Bold, Ink, TextAlignmentOptions.Center);
        Stretch(title.rectTransform);

        Image divider = CreateImage(header, "TitleDivider", Line);
        SetRect(divider.rectTransform, new Vector2(0.10f, 0.02f), new Vector2(0.90f, 0.06f));
        TMP_Text dividerStar = CreateText(
            header, "DividerStar", "*", font, 20f, FontStyles.Bold,
            Gold, TextAlignmentOptions.Center);
        SetRect(dividerStar.rectTransform, new Vector2(0.46f, 0f), new Vector2(0.54f, 0.20f));

        Image portraitCard = CreatePanel(design, "PortraitArea", CardLight, Line, 2f);
        SetRect(portraitCard.rectTransform, new Vector2(0.14f, 0.21f), new Vector2(0.86f, 0.80f));

        RawImage portrait = CreateRawImage(portraitCard.transform, "EngineerPortrait", portraitTexture);
        portrait.uvRect = sourceUv;
        SetRect(portrait.rectTransform, new Vector2(0.05f, 0.04f), new Vector2(0.95f, 0.96f));

        Image nameCard = CreatePanel(design, "IdentityCard", Paper, Line, 2f);
        SetRect(nameCard.rectTransform, new Vector2(0.14f, 0.075f), new Vector2(0.86f, 0.175f));

        TMP_Text helmetBadge = CreateText(
            nameCard.transform, "EngineerBadge", "E", font, 31f,
            FontStyles.Bold, Color.white, TextAlignmentOptions.Center);
        helmetBadge.rectTransform.anchorMin = new Vector2(0f, 0f);
        helmetBadge.rectTransform.anchorMax = new Vector2(0.18f, 1f);
        helmetBadge.rectTransform.offsetMin = Vector2.zero;
        helmetBadge.rectTransform.offsetMax = Vector2.zero;
        Image badgeBackground = CreatePanel(nameCard.transform, "BadgeBackground", Brown, Brown, 0f);
        badgeBackground.rectTransform.SetAsFirstSibling();
        badgeBackground.rectTransform.anchorMin = new Vector2(0f, 0f);
        badgeBackground.rectTransform.anchorMax = new Vector2(0.18f, 1f);
        badgeBackground.rectTransform.offsetMin = Vector2.zero;
        badgeBackground.rectTransform.offsetMax = Vector2.zero;

        TMP_Text playerName = CreateText(
            nameCard.transform, "EngineerName", "Engineer: Guest", font, 29f,
            FontStyles.Bold, Ink, TextAlignmentOptions.Center);
        SetRect(playerName.rectTransform, new Vector2(0.18f, 0f), Vector2.one);
        stats.playerNameText = playerName as TextMeshProUGUI;
    }

    private static void BuildRightPage(
        Transform page,
        AlmanacPlayerStats stats,
        TMP_FontAsset font,
        Sprite star)
    {
        RemoveGeneratedChild(page, RightDesignName);
        for (int index = 0; index < page.childCount; index++)
        {
            Transform child = page.GetChild(index);
            if (child.name != RightDesignName) child.gameObject.SetActive(false);
        }

        RectTransform design = CreateRect(RightDesignName, page);
        Stretch(design);

        TMP_Text overviewBadge = CreateText(
            design, "OverviewBadge", "O", font, 34f, FontStyles.Bold,
            Color.white, TextAlignmentOptions.Center);
        SetRect(overviewBadge.rectTransform, new Vector2(0.055f, 0.86f), new Vector2(0.16f, 0.96f));
        Image overviewBadgeBack = CreatePanel(design, "OverviewBadgeBackground", Brown, Brown, 0f);
        SetRect(overviewBadgeBack.rectTransform, new Vector2(0.065f, 0.865f), new Vector2(0.15f, 0.955f));
        overviewBadgeBack.rectTransform.SetAsFirstSibling();

        TMP_Text overviewTitle = CreateText(
            design, "OverviewTitle", "OVERVIEW", font, 37f,
            FontStyles.Bold, Ink, TextAlignmentOptions.MidlineLeft);
        SetRect(overviewTitle.rectTransform, new Vector2(0.17f, 0.86f), new Vector2(0.57f, 0.96f));
        Image overviewLine = CreateImage(design, "OverviewLine", Line);
        SetRect(overviewLine.rectTransform, new Vector2(0.49f, 0.895f), new Vector2(0.94f, 0.9f));

        Image overviewCard = CreatePanel(design, "OverviewCard", Card, Line, 2f);
        SetRect(overviewCard.rectTransform, new Vector2(0.055f, 0.42f), new Vector2(0.945f, 0.855f));

        CreateLabel(overviewCard.transform, "RankLabel", "RANK", font,
            new Vector2(0.04f, 0.80f), new Vector2(0.50f, 0.97f), 27f);
        TMP_Text rankValue = CreateText(
            overviewCard.transform, "RankValue", "Novice Builder", font, 28f,
            FontStyles.Bold, Ink, TextAlignmentOptions.MidlineRight);
        SetRect(rankValue.rectTransform, new Vector2(0.48f, 0.80f), new Vector2(0.95f, 0.97f));

        Image rankLine = CreateImage(overviewCard.transform, "RankDivider", Line);
        SetRect(rankLine.rectTransform, new Vector2(0.04f, 0.78f), new Vector2(0.96f, 0.785f));

        Image goldCard = CreatePanel(overviewCard.transform, "GoldCard", CardLight, Line, 1f);
        SetRect(goldCard.rectTransform, new Vector2(0.035f, 0.53f), new Vector2(0.49f, 0.75f));
        CreateLabel(goldCard.transform, "GoldLabel", "COINS", font,
            new Vector2(0.05f, 0f), new Vector2(0.60f, 1f), 24f);
        TMP_Text goldValue = CreateText(
            goldCard.transform, "GoldValue", "0", font, 28f,
            FontStyles.Bold, Ink, TextAlignmentOptions.MidlineRight);
        SetRect(goldValue.rectTransform, new Vector2(0.56f, 0f), new Vector2(0.94f, 1f));

        Image secondaryCard = CreatePanel(overviewCard.transform, "SecondaryCurrencyCard", CardLight, Line, 1f);
        SetRect(secondaryCard.rectTransform, new Vector2(0.51f, 0.53f), new Vector2(0.965f, 0.75f));
        CreateLabel(secondaryCard.transform, "SecondaryCurrencyLabel", "GEMS", font,
            new Vector2(0.05f, 0f), new Vector2(0.62f, 1f), 24f);
        TMP_Text secondaryValue = CreateText(
            secondaryCard.transform, "SecondaryCurrencyValue", "0", font, 28f,
            FontStyles.Bold, Ink, TextAlignmentOptions.MidlineRight);
        SetRect(secondaryValue.rectTransform, new Vector2(0.60f, 0f), new Vector2(0.94f, 1f));

        CreateLabel(overviewCard.transform, "ExpLabel", "XP   EXP", font,
            new Vector2(0.04f, 0.35f), new Vector2(0.40f, 0.51f), 25f);
        TMP_Text expValue = CreateText(
            overviewCard.transform, "ExpValue", "0 / 100", font, 25f,
            FontStyles.Bold, Ink, TextAlignmentOptions.MidlineRight);
        SetRect(expValue.rectTransform, new Vector2(0.48f, 0.35f), new Vector2(0.95f, 0.51f));

        Image progressBack = CreatePanel(overviewCard.transform, "ExpProgressBackground",
            Hex("D7C19C"), Line, 1f);
        SetRect(progressBack.rectTransform, new Vector2(0.18f, 0.235f), new Vector2(0.94f, 0.315f));
        Image progressFill = CreateImage(progressBack.transform, "ExpProgressFill", Blue);
        Stretch(progressFill.rectTransform);
        progressFill.type = Image.Type.Filled;
        progressFill.fillMethod = Image.FillMethod.Horizontal;
        progressFill.fillAmount = 0f;

        TMP_Text remaining = CreateText(
            overviewCard.transform, "ExpRemaining", "100 EXP to next rank", font, 19f,
            FontStyles.Normal, MutedInk, TextAlignmentOptions.Center);
        SetRect(remaining.rectTransform, new Vector2(0.18f, 0.13f), new Vector2(0.94f, 0.23f));

        CreateLabel(overviewCard.transform, "BridgesLabel", "BRIDGES BUILT", font,
            new Vector2(0.04f, 0.0f), new Vector2(0.34f, 0.14f), 22f);
        TMP_Text bridgesValue = CreateText(
            overviewCard.transform, "BridgesValue", "0", font, 28f,
            FontStyles.Bold, Ink, TextAlignmentOptions.MidlineRight);
        SetRect(bridgesValue.rectTransform, new Vector2(0.32f, 0.0f), new Vector2(0.46f, 0.14f));

        CreateLabel(overviewCard.transform, "ContractsLabel", "CONTRACTS", font,
            new Vector2(0.52f, 0.0f), new Vector2(0.78f, 0.14f), 22f);
        TMP_Text contractsValue = CreateText(
            overviewCard.transform, "ContractsValue", "0", font, 28f,
            FontStyles.Bold, Ink, TextAlignmentOptions.MidlineRight);
        SetRect(contractsValue.rectTransform, new Vector2(0.78f, 0.0f), new Vector2(0.95f, 0.14f));

        RectTransform achievementSection = CreateRect("AchievementSection", design);
        SetRect(achievementSection, Vector2.zero, new Vector2(1f, 0.39f));

        Image achievementBadge = CreatePanel(achievementSection, "AchievementBadge", Green, Green, 0f);
        SetRect(achievementBadge.rectTransform, new Vector2(0.065f, 0.70f), new Vector2(0.15f, 0.94f));
        TMP_Text trophy = CreateText(
            achievementBadge.transform, "Trophy", "A", font, 28f,
            FontStyles.Bold, Color.white, TextAlignmentOptions.Center);
        Stretch(trophy.rectTransform);

        TMP_Text achievementTitle = CreateText(
            achievementSection, "AchievementTitle", "ACHIEVEMENTS", font, 32f,
            FontStyles.Bold, Ink, TextAlignmentOptions.MidlineLeft);
        SetRect(achievementTitle.rectTransform, new Vector2(0.17f, 0.70f), new Vector2(0.62f, 0.94f));
        Image achievementLine = CreateImage(achievementSection, "AchievementLine", Line);
        SetRect(achievementLine.rectTransform, new Vector2(0.55f, 0.795f), new Vector2(0.94f, 0.81f));

        Image achievementCard = CreatePanel(achievementSection, "AchievementSummaryCard", CardLight, Line, 2f);
        SetRect(achievementCard.rectTransform, new Vector2(0.055f, 0.08f), new Vector2(0.945f, 0.67f));
        TMP_Text summary = CreateText(
            achievementCard.transform, "AchievementSummary",
            "No achievements yet.\nKeep building to earn your first achievement!",
            font, 22f, FontStyles.Normal, Ink, TextAlignmentOptions.Center);
        SetRect(summary.rectTransform, new Vector2(0.27f, 0.36f), new Vector2(0.72f, 0.90f));

        Image latestIcon = CreateSpriteImage(achievementCard.transform, "LatestAchievementIcon", star);
        SetRect(latestIcon.rectTransform, new Vector2(0.04f, 0.12f), new Vector2(0.25f, 0.88f));
        latestIcon.color = new Color(1f, 1f, 1f, 0.35f);

        Button viewAchievements = CreateButton(
            achievementCard.transform, "OpenAchievementsButton", "VIEW ACHIEVEMENTS", font);
        SetRect(viewAchievements.GetComponent<RectTransform>(),
            new Vector2(0.72f, 0.20f), new Vector2(0.96f, 0.80f));

        stats.useProfileCardLayout = true;
        stats.titleText = rankValue as TextMeshProUGUI;
        stats.goldText = goldValue as TextMeshProUGUI;
        stats.expText = expValue as TextMeshProUGUI;
        stats.bridgesBuiltText = bridgesValue as TextMeshProUGUI;
        stats.contractsCompletedText = contractsValue as TextMeshProUGUI;
        stats.secondaryCurrencyText = secondaryValue as TextMeshProUGUI;
        stats.expProgressFill = progressFill;
        stats.expRemainingText = remaining as TextMeshProUGUI;
        stats.achievementsSummaryText = summary as TextMeshProUGUI;
        stats.latestAchievementIcon = latestIcon;
        stats.achievementSectionRoot = achievementSection.gameObject;
        stats.overviewCardRect = overviewCard.rectTransform;
        stats.achievementPanelButton = viewAchievements;
    }

    private static void ConfigureBookAndProfilePages(AlmanacPlayerStats stats, Transform modelPage)
    {
        Transform playerStats = stats.transform.parent != null ? stats.transform.parent.parent : null;
        Transform bookContainer = playerStats != null ? playerStats.parent : null;
        if (bookContainer == null || bookContainer.name != "Container") return;

        RectTransform bookRect = bookContainer as RectTransform;
        if (bookRect != null)
        {
            bookRect.anchorMin = Vector2.zero;
            bookRect.anchorMax = Vector2.one;
            bookRect.offsetMin = new Vector2(48f, 42f);
            bookRect.offsetMax = new Vector2(-48f, -42f);
            bookRect.localScale = Vector3.one;
        }

        RectTransform playerStatsRect = playerStats as RectTransform;
        if (playerStatsRect != null)
        {
            playerStatsRect.anchorMin = new Vector2(0.035f, 0.07f);
            playerStatsRect.anchorMax = new Vector2(0.965f, 0.92f);
            playerStatsRect.offsetMin = Vector2.zero;
            playerStatsRect.offsetMax = Vector2.zero;
            playerStatsRect.localScale = Vector3.one;
        }

        RectTransform leftZone = FindDirectChild(playerStats, "LeftPageZone") as RectTransform;
        RectTransform rightZone = FindDirectChild(playerStats, "RightPageZone") as RectTransform;
        if (leftZone != null) SetRect(leftZone, Vector2.zero, new Vector2(0.5f, 1f));
        if (rightZone != null) SetRect(rightZone, new Vector2(0.5f, 0f), Vector2.one);

        if (leftZone != null)
        {
            foreach (RectTransform rect in leftZone.GetComponentsInChildren<RectTransform>(true))
            {
                if (rect == leftZone) continue;
                if (rect == modelPage || rect.parent == leftZone || IsAncestorOf(rect, modelPage))
                    Stretch(rect);
            }
        }

        Stretch(stats.GetComponent<RectTransform>());
    }

    private static void ConfigureTabsAndNavigation(Scene scene)
    {
        AlmanacManager manager = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            manager = root.GetComponentInChildren<AlmanacManager>(true);
            if (manager != null) break;
        }
        if (manager == null) return;

        manager.selectedTabUpOffset = 12f;
        foreach (AlmanacCategory category in manager.categories)
        {
            if (category == null || category.tabButton == null) continue;
            RectTransform tabRect = category.tabButton.GetComponent<RectTransform>();
            if (tabRect == null) continue;

            tabRect.anchorMin = new Vector2(0.5f, 1f);
            tabRect.anchorMax = new Vector2(0.5f, 1f);
            tabRect.anchoredPosition = new Vector2(tabRect.anchoredPosition.x, -12f);
            tabRect.sizeDelta = new Vector2(160f, 90f);
            tabRect.localScale = Vector3.one;
        }

        ConfigureNavigationButton(manager.prevButton, false);
        ConfigureNavigationButton(manager.nextButton, true);
        EditorUtility.SetDirty(manager);
    }

    private static void ConfigureNavigationButton(Button button, bool rightSide)
    {
        if (button == null) return;
        RectTransform rect = button.GetComponent<RectTransform>();
        if (rect == null) return;

        rect.anchorMin = new Vector2(rightSide ? 1f : 0f, 0.5f);
        rect.anchorMax = rect.anchorMin;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(rightSide ? -72f : 72f, 0f);
        rect.sizeDelta = new Vector2(92f, 92f);
        rect.localScale = Vector3.one;
        rect.SetAsLastSibling();
    }

    private static Transform FindDirectChild(Transform parent, string childName)
    {
        if (parent == null) return null;
        foreach (Transform child in parent)
            if (child.name == childName) return child;
        return null;
    }

    private static bool IsAncestorOf(Transform possibleAncestor, Transform child)
    {
        Transform current = child != null ? child.parent : null;
        while (current != null)
        {
            if (current == possibleAncestor) return true;
            current = current.parent;
        }
        return false;
    }

    private static AlmanacPlayerStats FindStatsPage(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            AlmanacPlayerStats[] candidates = root.GetComponentsInChildren<AlmanacPlayerStats>(true);
            foreach (AlmanacPlayerStats candidate in candidates)
            {
                if (candidate != null && candidate.gameObject.name == "Page1_Stats")
                    return candidate;
            }
        }
        return null;
    }

    private static Transform FindSceneTransform(Scene scene, string objectName)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Transform found = FindDescendant(root.transform, objectName);
            if (found != null) return found;
        }
        return null;
    }

    private static Transform FindDescendant(Transform parent, string objectName)
    {
        if (parent.name == objectName) return parent;
        foreach (Transform child in parent)
        {
            Transform found = FindDescendant(child, objectName);
            if (found != null) return found;
        }
        return null;
    }

    private static void RemoveGeneratedChild(Transform parent, string childName)
    {
        Transform existing = parent.Find(childName);
        if (existing != null) Object.DestroyImmediate(existing.gameObject);
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject created = new GameObject(name, typeof(RectTransform));
        RectTransform rect = created.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private static Image CreateImage(Transform parent, string name, Color color)
    {
        GameObject created = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        created.transform.SetParent(parent, false);
        Image image = created.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static Image CreatePanel(
        Transform parent,
        string name,
        Color color,
        Color outlineColor,
        float outlineSize)
    {
        Image image = CreateImage(parent, name, color);
        if (outlineSize > 0f)
        {
            Outline outline = image.gameObject.AddComponent<Outline>();
            outline.effectColor = outlineColor;
            outline.effectDistance = new Vector2(outlineSize, -outlineSize);
            outline.useGraphicAlpha = false;
        }
        return image;
    }

    private static Image CreateSpriteImage(Transform parent, string name, Sprite sprite)
    {
        Image image = CreateImage(parent, name, Color.white);
        image.sprite = sprite;
        image.preserveAspect = true;
        image.enabled = sprite != null;
        return image;
    }

    private static Button CreateButton(
        Transform parent,
        string name,
        string label,
        TMP_FontAsset font)
    {
        Image background = CreatePanel(parent, name, Gold, Brown, 2f);
        background.raycastTarget = true;

        Button button = background.gameObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = Hex("FFE2A2");
        colors.pressedColor = Hex("C47B25");
        colors.selectedColor = colors.highlightedColor;
        button.colors = colors;
        button.targetGraphic = background;

        TMP_Text buttonLabel = CreateText(
            background.transform, "Label", label, font, 20f,
            FontStyles.Bold, Ink, TextAlignmentOptions.Center);
        Stretch(buttonLabel.rectTransform);
        return button;
    }

    private static RawImage CreateRawImage(Transform parent, string name, Texture texture)
    {
        GameObject created = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        created.transform.SetParent(parent, false);
        RawImage image = created.GetComponent<RawImage>();
        image.texture = texture;
        image.color = Color.white;
        image.raycastTarget = false;
        return image;
    }

    private static TMP_Text CreateText(
        Transform parent,
        string name,
        string value,
        TMP_FontAsset font,
        float size,
        FontStyles style,
        Color color,
        TextAlignmentOptions alignment)
    {
        GameObject created = new GameObject(
            name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        created.transform.SetParent(parent, false);
        TextMeshProUGUI text = created.GetComponent<TextMeshProUGUI>();
        text.text = value;
        text.font = font;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.alignment = alignment;
        text.enableAutoSizing = true;
        text.fontSizeMin = Mathf.Max(12f, size * 0.58f);
        text.fontSizeMax = size;
        text.enableWordWrapping = true;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        return text;
    }

    private static void CreateLabel(
        Transform parent,
        string name,
        string value,
        TMP_FontAsset font,
        Vector2 min,
        Vector2 max,
        float size)
    {
        TMP_Text label = CreateText(
            parent, name, value, font, size, FontStyles.Bold,
            Ink, TextAlignmentOptions.MidlineLeft);
        SetRect(label.rectTransform, min, max);
    }

    private static void SetRect(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
    }

    private static void Stretch(RectTransform rect)
    {
        SetRect(rect, Vector2.zero, Vector2.one);
    }

    private static Color Hex(string value, float alpha = 1f)
    {
        ColorUtility.TryParseHtmlString("#" + value, out Color color);
        color.a = alpha;
        return color;
    }
}
#endif
