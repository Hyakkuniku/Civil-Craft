#if UNITY_EDITOR
using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[InitializeOnLoad]
public static class ShopSceneSetup
{
    private const string SessionKey = "CivilCraft.ShopSceneSetup.V3";
    private const string CardPrefabPath = "Assets/Prefabs/UI/ShopItemCard.prefab";
    private const string FontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/Bekind Sans SDF.asset";
    private const string CoinPlusSpritePath = "Assets/Elements/UI/bm_ui/coin_plus (1).png";

    private static readonly Color PanelColor = Hex("F8EDCF", 0.99f);
    private static readonly Color CardColor = Hex("FFF8E8", 1f);
    private static readonly Color Brown = Hex("4C321F", 1f);
    private static readonly Color MutedBrown = Hex("9A7653", 1f);
    private static readonly Color Gold = Hex("E9A12A", 1f);
    private static readonly Color TabColor = Hex("D8C3A2", 1f);

    static ShopSceneSetup()
    {
        EditorApplication.delayCall += TryAutoSetup;
        EditorSceneManager.sceneOpened += HandleSceneOpened;
        EditorApplication.playModeStateChanged += HandlePlayModeChanged;
    }

    [MenuItem("Tools/Civil Craft/Setup Shop In Current Gameplay Scene")]
    public static void SetupFromMenu()
    {
        SetupActiveScene(true);
    }

    public static void SetupCurrentScene(bool saveScene = true)
    {
        SetupActiveScene(saveScene);
    }

    public static void SetupCanyonCrossingAsset()
    {
        Scene scene = EditorSceneManager.OpenScene(
            "Assets/Scenes/CanyonCrossing.unity",
            OpenSceneMode.Single);
        SetupActiveScene(true);
    }

    public static void SetupBhanHouseAsset()
    {
        Scene scene = EditorSceneManager.OpenScene(
            "Assets/Scenes/BHAN HOUSE.unity",
            OpenSceneMode.Single);
        SetupActiveScene(true);
    }

    private static void HandleSceneOpened(Scene scene, OpenSceneMode mode)
    {
        if (IsSupportedGameplayScene(scene))
            EditorApplication.delayCall += TryAutoSetup;
    }

    private static void HandlePlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode)
            EditorApplication.delayCall += TryAutoSetup;
    }

    private static void TryAutoSetup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        Scene scene = SceneManager.GetActiveScene();
        if (!IsSupportedGameplayScene(scene)) return;

        bool alreadyRan = SessionState.GetBool(SessionKey, false);
        ShopManager existingManager = FindSceneComponent<ShopManager>(scene);
        bool isComplete = existingManager != null &&
                          FindSceneComponent<ShopButtonTrigger>(scene) != null &&
                          FindRecursive(existingManager.transform, "SecondaryCurrencyPill") != null &&
                          FindRecursive(existingManager.transform, "PurchaseConfirmationPanel") != null &&
                          FindRecursive(existingManager.transform, "PurchaseFeedbackPanel") != null;
        if (alreadyRan && isComplete) return;

        SessionState.SetBool(SessionKey, true);
        bool sceneWasDirty = scene.isDirty;
        SetupActiveScene(!sceneWasDirty);
    }

    private static void SetupActiveScene(bool saveScene)
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogWarning("[ShopSetup] Open CanyonCrossing or BHAN HOUSE before running the setup.");
            return;
        }

        Canvas mainCanvas = FindMainCanvas(scene);
        if (mainCanvas == null)
        {
            Debug.LogError("[ShopSetup] Could not find MainCanvas.");
            return;
        }

        ShopManager manager = FindSceneComponent<ShopManager>(scene);
        GameObject shopSystem;
        GameObject shopPanel;

        if (manager == null)
        {
            shopSystem = new GameObject("ShopSystem", typeof(RectTransform), typeof(ShopManager));
            shopSystem.transform.SetParent(mainCanvas.transform, false);
            SetStretch(shopSystem.GetComponent<RectTransform>());
            manager = shopSystem.GetComponent<ShopManager>();
            shopPanel = CreateShopPanel(shopSystem.transform, manager);
        }
        else
        {
            shopSystem = manager.gameObject;
            SerializedObject managerData = new SerializedObject(manager);
            SerializedProperty panelProperty = managerData.FindProperty("shopPanel");
            shopPanel = panelProperty != null ? panelProperty.objectReferenceValue as GameObject : null;
            if (shopPanel == null)
                shopPanel = CreateShopPanel(shopSystem.transform, manager);
        }

        if (shopPanel != null)
            UpgradeShopPanel(shopPanel, manager);

        RemoveInvalidShopButtonTriggers(scene);
        Button shopButton = FindShopButton(scene);
        if (shopButton == null)
            shopButton = CreateShopAccessButton(mainCanvas.transform);
        WireShopButton(shopButton, manager);

        shopSystem.SetActive(true);
        if (shopPanel != null) shopPanel.SetActive(false);
        SetLayerRecursively(shopSystem, LayerMask.NameToLayer("UI"));

        EditorUtility.SetDirty(manager);
        EditorSceneManager.MarkSceneDirty(scene);
        if (saveScene) EditorSceneManager.SaveScene(scene);

        Selection.activeGameObject = shopSystem;
        Debug.Log("[ShopSetup] Shop panel, item-card prefab, tabs, scroll grid, and Shop button are ready.", shopSystem);
    }

    private static bool IsSupportedGameplayScene(Scene scene)
    {
        return scene.IsValid() && scene.isLoaded &&
               (scene.name == "CanyonCrossing" || scene.name == "BHAN HOUSE");
    }

    private static GameObject CreateShopPanel(Transform parent, ShopManager manager)
    {
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        ShopItemUI cardPrefab = GetOrCreateCardPrefab(font);

        GameObject panel = CreateImage(parent, "ShopPanel", PanelColor);
        SetStretch(panel.GetComponent<RectTransform>());
        panel.GetComponent<Image>().raycastTarget = true;

        GameObject border = CreateImage(panel.transform, "Border", Color.clear);
        SetStretch(border.GetComponent<RectTransform>(), 22f);
        Outline borderOutline = border.AddComponent<Outline>();
        borderOutline.effectColor = Brown;
        borderOutline.effectDistance = new Vector2(3f, -3f);

        TMP_Text title = CreateText(panel.transform, "Title", "SHOP", 62f, FontStyles.Bold, font);
        SetAnchored(title.rectTransform, new Vector2(0.35f, 0.88f), new Vector2(0.65f, 0.98f));

        Button backButton = CreateButton(panel.transform, "BackButton", "‹", 54f, font, CardColor);
        SetAnchored(backButton.GetComponent<RectTransform>(), new Vector2(0.025f, 0.885f), new Vector2(0.09f, 0.97f));
        UnityEventTools.AddPersistentListener(backButton.onClick, manager.CloseShop);

        Button closeButton = CreateButton(panel.transform, "CloseButton", "×", 54f, font, CardColor);
        SetAnchored(closeButton.GetComponent<RectTransform>(), new Vector2(0.91f, 0.885f), new Vector2(0.975f, 0.97f));
        UnityEventTools.AddPersistentListener(closeButton.onClick, manager.CloseShop);

        GameObject currencyPill = CreateImage(panel.transform, "CurrencyPill", CardColor);
        SetAnchored(currencyPill.GetComponent<RectTransform>(), new Vector2(0.72f, 0.89f), new Vector2(0.895f, 0.965f));
        Outline currencyOutline = currencyPill.AddComponent<Outline>();
        currencyOutline.effectColor = MutedBrown;
        currencyOutline.effectDistance = new Vector2(2f, -2f);
        TMP_Text currency = CreateText(currencyPill.transform, "CurrencyText", "₱0", 34f, FontStyles.Bold, font);
        SetStretch(currency.rectTransform, 10f);
        currency.alignment = TextAlignmentOptions.Center;

        GameObject tabs = new GameObject("CategoryTabs", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        tabs.transform.SetParent(panel.transform, false);
        SetAnchored(tabs.GetComponent<RectTransform>(), new Vector2(0.04f, 0.765f), new Vector2(0.96f, 0.855f));
        HorizontalLayoutGroup tabsLayout = tabs.GetComponent<HorizontalLayoutGroup>();
        tabsLayout.spacing = 12f;
        tabsLayout.childAlignment = TextAnchor.MiddleCenter;
        tabsLayout.childControlWidth = true;
        tabsLayout.childControlHeight = true;
        tabsLayout.childForceExpandWidth = true;
        tabsLayout.childForceExpandHeight = true;

        Button[] tabButtons = new Button[6];
        GameObject[] selectedVisuals = new GameObject[6];
        string[] names = { "Builder", "Materials", "Tools", "Decorations", "Vehicles", "Bundles" };
        for (int i = 0; i < names.Length; i++)
        {
            tabButtons[i] = CreateButton(tabs.transform, names[i] + "Tab", names[i].ToUpperInvariant(), 27f, font, TabColor);
            LayoutElement layout = tabButtons[i].gameObject.AddComponent<LayoutElement>();
            layout.flexibleWidth = 1f;
            layout.minHeight = 70f;

            GameObject selected = CreateImage(tabButtons[i].transform, "Selected", Gold);
            RectTransform selectedRect = selected.GetComponent<RectTransform>();
            selectedRect.anchorMin = new Vector2(0.08f, 0f);
            selectedRect.anchorMax = new Vector2(0.92f, 0f);
            selectedRect.pivot = new Vector2(0.5f, 0f);
            selectedRect.anchoredPosition = new Vector2(0f, 4f);
            selectedRect.sizeDelta = new Vector2(0f, 6f);
            selected.GetComponent<Image>().raycastTarget = false;
            selectedVisuals[i] = selected;
        }

        GameObject scrollObject = CreateImage(panel.transform, "ItemScrollView", Hex("E9DCC1", 0.55f));
        SetAnchored(scrollObject.GetComponent<RectTransform>(), new Vector2(0.04f, 0.055f), new Vector2(0.96f, 0.74f));
        ScrollRect scrollRect = scrollObject.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Elastic;
        scrollRect.scrollSensitivity = 45f;

        GameObject viewport = CreateImage(scrollObject.transform, "Viewport", Color.clear);
        SetStretch(viewport.GetComponent<RectTransform>(), 8f);
        viewport.AddComponent<RectMask2D>();
        viewport.GetComponent<Image>().raycastTarget = true;

        GameObject content = new GameObject(
            "Content",
            typeof(RectTransform),
            typeof(GridLayoutGroup),
            typeof(ContentSizeFitter));
        content.transform.SetParent(viewport.transform, false);
        RectTransform contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = Vector2.zero;

        GridLayoutGroup grid = content.GetComponent<GridLayoutGroup>();
        grid.padding = new RectOffset(20, 20, 20, 28);
        grid.spacing = new Vector2(18f, 20f);
        grid.cellSize = new Vector2(330f, 380f);
        grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;
        grid.childAlignment = TextAnchor.UpperCenter;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 4;

        ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.viewport = viewport.GetComponent<RectTransform>();
        scrollRect.content = contentRect;

        SerializedObject managerData = new SerializedObject(manager);
        managerData.FindProperty("shopPanel").objectReferenceValue = panel;
        managerData.FindProperty("currencyText").objectReferenceValue = currency;
        managerData.FindProperty("itemGridContent").objectReferenceValue = content.transform;
        managerData.FindProperty("itemCardPrefab").objectReferenceValue = cardPrefab;

        SerializedProperty tabArray = managerData.FindProperty("categoryTabs");
        tabArray.arraySize = 6;
        for (int i = 0; i < 6; i++)
        {
            SerializedProperty tab = tabArray.GetArrayElementAtIndex(i);
            tab.FindPropertyRelative("category").enumValueIndex = i;
            tab.FindPropertyRelative("button").objectReferenceValue = tabButtons[i];
            tab.FindPropertyRelative("selectedVisual").objectReferenceValue = selectedVisuals[i];
        }
        managerData.ApplyModifiedPropertiesWithoutUndo();

        panel.SetActive(false);
        return panel;
    }

    private static void UpgradeShopPanel(GameObject panel, ShopManager manager)
    {
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);

        Transform primaryPill = FindRecursive(panel.transform, "CurrencyPill");
        if (primaryPill is RectTransform primaryRect)
            SetAnchored(primaryRect, new Vector2(0.62f, 0.89f), new Vector2(0.755f, 0.965f));

        Transform close = FindRecursive(panel.transform, "CloseButton");
        if (close is RectTransform closeRect)
            SetAnchored(closeRect, new Vector2(0.935f, 0.885f), new Vector2(0.985f, 0.97f));

        Transform existingAdd = FindRecursive(panel.transform, "AddCurrencyButton");
        if (existingAdd == null)
        {
            GameObject addObject = CreateImage(panel.transform, "AddCurrencyButton", Color.white);
            Button addCurrencyButton = addObject.AddComponent<Button>();
            addCurrencyButton.targetGraphic = addObject.GetComponent<Image>();
            Image addImage = addObject.GetComponent<Image>();
            addImage.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(CoinPlusSpritePath);
            addImage.preserveAspect = true;
            ColorBlock colors = addCurrencyButton.colors;
            colors.highlightedColor = Hex("FFF2C9", 1f);
            colors.pressedColor = Hex("D88B20", 1f);
            addCurrencyButton.colors = colors;
            SetAnchored(addObject.GetComponent<RectTransform>(), new Vector2(0.77f, 0.89f), new Vector2(0.82f, 0.965f));
            UnityEventTools.AddPersistentListener(addCurrencyButton.onClick, manager.HandleAddCurrencyClicked);
        }

        GameObject secondaryPill;
        Transform existingSecondary = FindRecursive(panel.transform, "SecondaryCurrencyPill");
        if (existingSecondary == null)
        {
            secondaryPill = CreateImage(panel.transform, "SecondaryCurrencyPill", CardColor);
            SetAnchored(
                secondaryPill.GetComponent<RectTransform>(),
                new Vector2(0.835f, 0.89f),
                new Vector2(0.925f, 0.965f));
            Outline outline = secondaryPill.AddComponent<Outline>();
            outline.effectColor = MutedBrown;
            outline.effectDistance = new Vector2(2f, -2f);
            TMP_Text placeholder = CreateText(
                secondaryPill.transform,
                "SecondaryCurrencyText",
                "0",
                34f,
                FontStyles.Bold,
                font);
            SetStretch(placeholder.rectTransform, 8f);
        }
        else
        {
            secondaryPill = existingSecondary.gameObject;
        }

        Transform secondaryTextTransform = FindRecursive(secondaryPill.transform, "SecondaryCurrencyText");
        TMP_Text secondaryText = secondaryTextTransform != null
            ? secondaryTextTransform.GetComponent<TMP_Text>()
            : null;

        GameObject confirmationPanel;
        Transform existingConfirmation = FindRecursive(panel.transform, "PurchaseConfirmationPanel");
        if (existingConfirmation == null)
        {
            confirmationPanel = CreateImage(panel.transform, "PurchaseConfirmationPanel", new Color(0f, 0f, 0f, 0.62f));
            SetStretch(confirmationPanel.GetComponent<RectTransform>());
            confirmationPanel.GetComponent<Image>().raycastTarget = true;

            GameObject dialog = CreateImage(confirmationPanel.transform, "Dialog", CardColor);
            SetAnchored(dialog.GetComponent<RectTransform>(), new Vector2(0.27f, 0.22f), new Vector2(0.73f, 0.78f));
            Outline dialogOutline = dialog.AddComponent<Outline>();
            dialogOutline.effectColor = Brown;
            dialogOutline.effectDistance = new Vector2(3f, -3f);

            TMP_Text confirmationTitle = CreateText(
                dialog.transform,
                "ConfirmationTitle",
                "CONFIRM PURCHASE",
                38f,
                FontStyles.Bold,
                font);
            SetAnchored(confirmationTitle.rectTransform, new Vector2(0.08f, 0.81f), new Vector2(0.92f, 0.96f));

            GameObject confirmationIconObject = CreateImage(dialog.transform, "ConfirmationIcon", Color.white);
            SetAnchored(
                confirmationIconObject.GetComponent<RectTransform>(),
                new Vector2(0.08f, 0.32f),
                new Vector2(0.39f, 0.79f));
            Image confirmationIcon = confirmationIconObject.GetComponent<Image>();
            confirmationIcon.preserveAspect = true;
            confirmationIcon.raycastTarget = false;

            TMP_Text confirmationDescription = CreateText(
                dialog.transform,
                "ConfirmationDescription",
                "Purchase this item?",
                25f,
                FontStyles.Normal,
                font);
            SetAnchored(
                confirmationDescription.rectTransform,
                new Vector2(0.43f, 0.44f),
                new Vector2(0.92f, 0.77f));
            confirmationDescription.enableWordWrapping = true;

            TMP_Text confirmationPrice = CreateText(
                dialog.transform,
                "ConfirmationPrice",
                "₱0",
                38f,
                FontStyles.Bold,
                font);
            confirmationPrice.color = Gold;
            SetAnchored(confirmationPrice.rectTransform, new Vector2(0.43f, 0.28f), new Vector2(0.92f, 0.44f));

            Button cancel = CreateButton(dialog.transform, "CancelButton", "CANCEL", 27f, font, TabColor);
            SetAnchored(cancel.GetComponent<RectTransform>(), new Vector2(0.08f, 0.07f), new Vector2(0.46f, 0.23f));
            UnityEventTools.AddPersistentListener(cancel.onClick, manager.CancelPendingPurchase);

            Button confirm = CreateButton(dialog.transform, "ConfirmButton", "BUY", 27f, font, Gold);
            SetAnchored(confirm.GetComponent<RectTransform>(), new Vector2(0.54f, 0.07f), new Vector2(0.92f, 0.23f));
            UnityEventTools.AddPersistentListener(confirm.onClick, manager.ConfirmPendingPurchase);
        }
        else
        {
            confirmationPanel = existingConfirmation.gameObject;
        }

        TMP_Text titleText = GetChildComponent<TMP_Text>(confirmationPanel.transform, "ConfirmationTitle");
        TMP_Text descriptionText = GetChildComponent<TMP_Text>(confirmationPanel.transform, "ConfirmationDescription");
        TMP_Text priceText = GetChildComponent<TMP_Text>(confirmationPanel.transform, "ConfirmationPrice");
        Image iconImage = GetChildComponent<Image>(confirmationPanel.transform, "ConfirmationIcon");

        GameObject feedbackPanel;
        Transform existingFeedback = FindRecursive(panel.transform, "PurchaseFeedbackPanel");
        if (existingFeedback == null)
        {
            feedbackPanel = CreateImage(panel.transform, "PurchaseFeedbackPanel", new Color(0f, 0f, 0f, 0.62f));
            SetStretch(feedbackPanel.GetComponent<RectTransform>());
            feedbackPanel.GetComponent<Image>().raycastTarget = true;

            GameObject feedbackDialog = CreateImage(feedbackPanel.transform, "Dialog", CardColor);
            SetAnchored(feedbackDialog.GetComponent<RectTransform>(), new Vector2(0.31f, 0.31f), new Vector2(0.69f, 0.69f));
            Outline feedbackOutline = feedbackDialog.AddComponent<Outline>();
            feedbackOutline.effectColor = Brown;
            feedbackOutline.effectDistance = new Vector2(3f, -3f);

            TMP_Text feedbackTitle = CreateText(
                feedbackDialog.transform,
                "FeedbackTitle",
                "PURCHASE UNAVAILABLE",
                38f,
                FontStyles.Bold,
                font);
            feedbackTitle.color = Brown;
            SetAnchored(feedbackTitle.rectTransform, new Vector2(0.08f, 0.69f), new Vector2(0.92f, 0.91f));

            TMP_Text feedbackMessage = CreateText(
                feedbackDialog.transform,
                "FeedbackMessage",
                "You do not have enough money for this item.",
                27f,
                FontStyles.Normal,
                font);
            feedbackMessage.enableWordWrapping = true;
            SetAnchored(feedbackMessage.rectTransform, new Vector2(0.09f, 0.28f), new Vector2(0.91f, 0.68f));

            Button okButton = CreateButton(feedbackDialog.transform, "OkayButton", "OK", 28f, font, Gold);
            SetAnchored(okButton.GetComponent<RectTransform>(), new Vector2(0.30f, 0.07f), new Vector2(0.70f, 0.25f));
            UnityEventTools.AddPersistentListener(okButton.onClick, manager.HidePurchaseFeedback);
        }
        else
        {
            feedbackPanel = existingFeedback.gameObject;
        }

        TMP_Text feedbackTitleText = GetChildComponent<TMP_Text>(feedbackPanel.transform, "FeedbackTitle");
        TMP_Text feedbackMessageText = GetChildComponent<TMP_Text>(feedbackPanel.transform, "FeedbackMessage");

        SerializedObject managerData = new SerializedObject(manager);
        managerData.FindProperty("secondaryCurrencyText").objectReferenceValue = secondaryText;
        managerData.FindProperty("purchaseConfirmationPanel").objectReferenceValue = confirmationPanel;
        managerData.FindProperty("confirmationTitleText").objectReferenceValue = titleText;
        managerData.FindProperty("confirmationDescriptionText").objectReferenceValue = descriptionText;
        managerData.FindProperty("confirmationPriceText").objectReferenceValue = priceText;
        managerData.FindProperty("confirmationIconImage").objectReferenceValue = iconImage;
        managerData.FindProperty("purchaseFeedbackPanel").objectReferenceValue = feedbackPanel;
        managerData.FindProperty("purchaseFeedbackTitleText").objectReferenceValue = feedbackTitleText;
        managerData.FindProperty("purchaseFeedbackMessageText").objectReferenceValue = feedbackMessageText;
        managerData.ApplyModifiedPropertiesWithoutUndo();

        confirmationPanel.transform.SetAsLastSibling();
        confirmationPanel.SetActive(false);
        feedbackPanel.transform.SetAsLastSibling();
        feedbackPanel.SetActive(false);
    }

    private static ShopItemUI GetOrCreateCardPrefab(TMP_FontAsset font)
    {
        GameObject existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CardPrefabPath);
        if (existingPrefab != null)
        {
            ShopItemUI existing = existingPrefab.GetComponent<ShopItemUI>();
            if (existing != null) return existing;
        }

        EnsureAssetFolder("Assets/Prefabs/UI");

        GameObject root = CreateImage(null, "ShopItemCard", CardColor);
        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.sizeDelta = new Vector2(330f, 380f);
        Outline outline = root.AddComponent<Outline>();
        outline.effectColor = Hex("C9AE7F", 1f);
        outline.effectDistance = new Vector2(2f, -2f);
        ShopItemUI card = root.AddComponent<ShopItemUI>();

        TMP_Text title = CreateText(root.transform, "Title", "ITEM NAME", 29f, FontStyles.Bold, font);
        SetAnchored(title.rectTransform, new Vector2(0.06f, 0.84f), new Vector2(0.94f, 0.97f));

        GameObject iconObject = CreateImage(root.transform, "Icon", Color.clear);
        SetAnchored(iconObject.GetComponent<RectTransform>(), new Vector2(0.16f, 0.37f), new Vector2(0.84f, 0.83f));
        Image icon = iconObject.GetComponent<Image>();
        icon.color = Color.white;
        icon.preserveAspect = true;
        icon.raycastTarget = false;

        TMP_Text description = CreateText(root.transform, "Description", "Item description", 23f, FontStyles.Normal, font);
        SetAnchored(description.rectTransform, new Vector2(0.07f, 0.18f), new Vector2(0.93f, 0.38f));
        description.enableWordWrapping = true;

        Button buy = CreateButton(root.transform, "BuyButton", "₱0", 29f, font, Gold);
        SetAnchored(buy.GetComponent<RectTransform>(), new Vector2(0.16f, 0.035f), new Vector2(0.84f, 0.17f));
        TMP_Text price = buy.GetComponentInChildren<TMP_Text>(true);

        GameObject owned = CreateImage(root.transform, "OwnedBadge", Brown);
        SetAnchored(owned.GetComponent<RectTransform>(), new Vector2(0.69f, 0.86f), new Vector2(0.95f, 0.95f));
        TMP_Text ownedText = CreateText(owned.transform, "Text", "OWNED", 18f, FontStyles.Bold, font);
        SetStretch(ownedText.rectTransform, 3f);
        ownedText.color = Color.white;
        owned.SetActive(false);

        SerializedObject cardData = new SerializedObject(card);
        cardData.FindProperty("titleText").objectReferenceValue = title;
        cardData.FindProperty("descriptionText").objectReferenceValue = description;
        cardData.FindProperty("iconImage").objectReferenceValue = icon;
        cardData.FindProperty("buyButton").objectReferenceValue = buy;
        cardData.FindProperty("priceText").objectReferenceValue = price;
        cardData.FindProperty("ownedBadge").objectReferenceValue = owned;
        cardData.ApplyModifiedPropertiesWithoutUndo();

        SetLayerRecursively(root, LayerMask.NameToLayer("UI"));
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, CardPrefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        return prefab.GetComponent<ShopItemUI>();
    }

    private static Button FindShopButton(Scene scene)
    {
        foreach (Button button in FindSceneComponents<Button>(scene))
        {
            if (LooksLikeShopButton(button)) return button;
        }

        return null;
    }

    private static void RemoveInvalidShopButtonTriggers(Scene scene)
    {
        foreach (ShopButtonTrigger trigger in FindSceneComponents<ShopButtonTrigger>(scene))
        {
            Button button = trigger.GetComponent<Button>();
            if (button != null && LooksLikeShopButton(button)) continue;
            UnityEngine.Object.DestroyImmediate(trigger);
        }
    }

    private static bool LooksLikeShopButton(Button button)
    {
        if (button == null) return false;

        string objectName = Normalize(button.name);
        if (objectName.Contains("shop") || IsStoreToken(objectName)) return true;

        foreach (TMP_Text label in button.GetComponentsInChildren<TMP_Text>(true))
        {
            string text = Normalize(label.text);
            if (text.Contains("shop") || IsStoreToken(text)) return true;
        }

        Image image = button.targetGraphic as Image;
        if (image != null && image.sprite != null)
        {
            string spritePath = Normalize(AssetDatabase.GetAssetPath(image.sprite));
            if (spritePath.Contains("shop") || IsStoreToken(spritePath)) return true;
        }

        return false;
    }

    private static bool IsStoreToken(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Contains("restore")) return false;
        return value == "store" || value.StartsWith("storebutton") ||
               value.StartsWith("storebtn") || value.EndsWith("storebutton") ||
               value.EndsWith("storebtn");
    }

    private static Button CreateShopAccessButton(Transform mainCanvas)
    {
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        Transform accessButtons = FindRecursive(mainCanvas, "AccessButtons");
        Transform parent = accessButtons != null ? accessButtons : mainCanvas;
        Button button = CreateButton(parent, "Shop_btn", "SHOP", 24f, font, Gold);
        RectTransform rect = button.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(150f, 78f);

        if (parent.GetComponent<HorizontalOrVerticalLayoutGroup>() == null)
        {
            RectTransform parentRect = parent as RectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            float maxRight = 0f;
            for (int i = 0; i < parent.childCount; i++)
            {
                RectTransform sibling = parent.GetChild(i) as RectTransform;
                if (sibling == null || sibling == rect) continue;
                maxRight = Mathf.Max(maxRight, sibling.anchoredPosition.x + sibling.rect.width);
            }
            rect.anchoredPosition = new Vector2(maxRight + 14f, 0f);
        }
        else
        {
            LayoutElement layout = button.gameObject.AddComponent<LayoutElement>();
            layout.preferredWidth = 150f;
            layout.preferredHeight = 78f;
        }

        return button;
    }

    private static void WireShopButton(Button button, ShopManager manager)
    {
        ShopButtonTrigger trigger = button.GetComponent<ShopButtonTrigger>();
        if (trigger == null) trigger = button.gameObject.AddComponent<ShopButtonTrigger>();

        SerializedObject triggerData = new SerializedObject(trigger);
        triggerData.FindProperty("shopManager").objectReferenceValue = manager;
        triggerData.FindProperty("button").objectReferenceValue = button;
        triggerData.FindProperty("wireButtonAutomatically").boolValue = true;
        triggerData.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(button.gameObject);
    }

    private static Canvas FindMainCanvas(Scene scene)
    {
        foreach (Canvas canvas in FindSceneComponents<Canvas>(scene))
        {
            if (canvas.name == "MainCanvas") return canvas;
        }
        return null;
    }

    private static T FindSceneComponent<T>(Scene scene) where T : Component
    {
        T[] components = FindSceneComponents<T>(scene);
        return components.Length > 0 ? components[0] : null;
    }

    private static T[] FindSceneComponents<T>(Scene scene) where T : Component
    {
        return Resources.FindObjectsOfTypeAll<T>()
            .Where(component => component != null && component.gameObject.scene == scene)
            .ToArray();
    }

    private static Transform FindRecursive(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindRecursive(root.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }

    private static T GetChildComponent<T>(Transform root, string objectName) where T : Component
    {
        Transform child = FindRecursive(root, objectName);
        return child != null ? child.GetComponent<T>() : null;
    }

    private static GameObject CreateImage(Transform parent, string name, Color color)
    {
        GameObject target = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        if (parent != null) target.transform.SetParent(parent, false);
        Image image = target.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = color.a > 0f;
        return target;
    }

    private static TMP_Text CreateText(
        Transform parent,
        string name,
        string value,
        float size,
        FontStyles style,
        TMP_FontAsset font)
    {
        GameObject target = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        target.transform.SetParent(parent, false);
        TextMeshProUGUI text = target.GetComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = size;
        text.fontStyle = style;
        text.font = font;
        text.color = Brown;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        text.enableWordWrapping = false;
        return text;
    }

    private static Button CreateButton(
        Transform parent,
        string name,
        string label,
        float fontSize,
        TMP_FontAsset font,
        Color background)
    {
        GameObject target = CreateImage(parent, name, background);
        Button button = target.AddComponent<Button>();
        button.targetGraphic = target.GetComponent<Image>();
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = Hex("FFF2C9", 1f);
        colors.pressedColor = Hex("D88B20", 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = Hex("B7AA94", 0.6f);
        button.colors = colors;
        Outline outline = target.AddComponent<Outline>();
        outline.effectColor = MutedBrown;
        outline.effectDistance = new Vector2(2f, -2f);

        TMP_Text text = CreateText(target.transform, "Text", label, fontSize, FontStyles.Bold, font);
        SetStretch(text.rectTransform, 5f);
        return button;
    }

    private static void SetStretch(RectTransform rect, float inset = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    private static void SetAnchored(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        if (root == null || layer < 0) return;
        root.layer = layer;
        for (int i = 0; i < root.transform.childCount; i++)
            SetLayerRecursively(root.transform.GetChild(i).gameObject, layer);
    }

    private static void EnsureAssetFolder(string folderPath)
    {
        string[] parts = folderPath.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    private static string Normalize(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace(" ", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
    }

    private static Color Hex(string hex, float alpha)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out Color color);
        color.a = alpha;
        return color;
    }
}
#endif
