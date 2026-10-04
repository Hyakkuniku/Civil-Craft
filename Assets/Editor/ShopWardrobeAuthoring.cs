using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Opt-in authored UI migration. Never buys, unlocks, or saves a player look.</summary>
public static class ShopWardrobeAuthoring
{
    private const string Request = "Temp/shop-wardrobe-bookmarks-v8.request";
    private const string Report = "Temp/ShopWardrobeValidation.txt";
    private static readonly string[] Scenes = { "Assets/Scenes/CanyonCrossing.unity", "Assets/Scenes/BHAN HOUSE.unity", "Assets/Scenes/Main Menu.unity" };
    private static readonly Color Paper = new Color32(249, 235, 206, 255);
    private static readonly Color Cream = new Color32(255, 246, 226, 255);
    private static readonly Color Wood = new Color32(87, 55, 31, 255);
    private static readonly Color Gold = new Color32(232, 160, 35, 255);
    private static TMP_FontAsset font;
    private static Sprite rounded;
    private static double nextCheck;

    [InitializeOnLoadMethod]
    private static void Watch() { EditorApplication.update -= Check; EditorApplication.update += Check; }
    private static void Check()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (Scenes.Any(path => SceneManager.GetSceneByPath(path).isDirty))
        { File.WriteAllText(Report, "WAIT: Save scene edits first. No unsaved scene has been overwritten."); return; }
        File.Delete(Request);
        try { Author(); }
        catch (Exception error) { File.WriteAllText(Report, "FAIL: " + error); Debug.LogException(error); }
    }

    [MenuItem("Tools/Civil Craft/Author Shop And Wardrobe UI")]
    public static void Author()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || Scenes.Any(path => SceneManager.GetSceneByPath(path).isDirty))
            throw new Exception("Stop Play Mode and save scene edits first.");
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/Bekind Sans SDF.asset");
        rounded = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Elements/UI/LevelResultRounded.png");
        if (font == null || rounded == null) throw new Exception("Existing UI font/frame asset is missing.");
        ValidateMaterialColors();
        ValidateReveal();
        EditPrefab("Assets/Prefabs/UI/ShopItemCard.prefab", root => StyleShopCard(root.GetComponent<ShopItemUI>()));
        EditPrefab("Assets/Prefabs/UI/CharacterCustomizationPanel.prefab", root => StyleWardrobe(root.GetComponent<CharacterCustomizationController>()));
        EditPrefab("Assets/Prefabs/BuildingMode/MANAGERS AND CANVASES.prefab", StyleHierarchy);
        int wardrobes = 0, shops = 0;
        foreach (string path in Scenes)
        {
            Scene scene = SceneManager.GetSceneByPath(path);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    wardrobes += root.GetComponentsInChildren<CharacterCustomizationController>(true).Length;
                    shops += root.GetComponentsInChildren<ShopManager>(true).Length;
                    StyleHierarchy(root);
                }
                foreach (CharacterCustomizationController wardrobe in All<CharacterCustomizationController>(scene))
                {
                    ValidateWardrobe(wardrobe);
                    RenderWardrobe(wardrobe, scene.name);
                }
                foreach (ShopManager shop in All<ShopManager>(scene)) RenderShop(shop, scene.name);
                foreach (AlmanacManager book in All<AlmanacManager>(scene))
                    AlmanacBookAuthoring.CaptureAuthoredPreviews(book, scene.name);
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }
        AssetDatabase.SaveAssets();
        File.WriteAllText(Report, "PASS: authored " + wardrobes + " wardrobes and " + shops +
            " shops; lowered bookmarks, centered/aspect-correct portraits, capped currency type, authored dynamic Color 1/2/... pickers, and short reveal animations. 16:9/16:10 previews, one/two/max material row visibility, swatch isolation, reset, animation interruption, legacy colors and equipped appearance payload checked. Purchase/story gates and original button callbacks preserved. No player saves, unlocks or purchases changed. Live navigation/Save/Cancel still required.\n");
    }

    private static void EditPrefab(string path, Action<GameObject> edit)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try { edit(root); PrefabUtility.SaveAsPrefabAsset(root, path); }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
    private static IEnumerable<T> All<T>(Scene scene) where T : Component =>
        scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true));
    private static void StyleHierarchy(GameObject root)
    {
        foreach (CharacterCustomizationController controller in root.GetComponentsInChildren<CharacterCustomizationController>(true))
        { StyleWardrobe(controller); RecordOverrides(controller.gameObject); }
        foreach (ShopManager shop in root.GetComponentsInChildren<ShopManager>(true))
        { StyleShop(shop); if (shop.Panel != null) RecordOverrides(shop.Panel); }
        foreach (AlmanacManager book in root.GetComponentsInChildren<AlmanacManager>(true)) StyleBookmarks(book);
    }
    private static T Ensure<T>(GameObject obj) where T : Component
    {
        // Use Unity's null check: an editor prefab may return a native-null wrapper.
        T component = obj.GetComponent<T>();
        if (component == null) component = obj.AddComponent<T>();
        return component;
    }

    private static void RecordOverrides(GameObject uiRoot)
    {
        foreach (Component component in uiRoot.GetComponentsInChildren<Component>(true))
            if (component != null && PrefabUtility.IsPartOfPrefabInstance(component))
                PrefabUtility.RecordPrefabInstancePropertyModifications(component);
    }

    private static void StyleWardrobe(CharacterCustomizationController controller)
    {
        if (controller == null) return;
        var data = new SerializedObject(controller);
        string[] preserve = { "cosmetics", "previewMirror", "sharedPortrait", "profilePortraitAnchor", "transitionLayer", "profileGroups", "almanacPanel" };
        string before = string.Join("|", preserve.Select(name => data.FindProperty(name).propertyPath + ":" + FieldJSON(controller, name)));
        var events = controller.GetComponentsInChildren<Button>(true).ToDictionary(button => button, button => JsonUtility.ToJson(button.onClick));
        Transform root = controller.transform;
        root.GetComponent<Image>().color = Color.clear;
        Image shade = Find(root, "Backdrop").GetComponent<Image>(); shade.sprite = null; shade.color = new Color(0, 0, 0, .45f);
        RectTransform shell = Find(root, "WardrobeBook") as RectTransform;
        Place(shell, .035f, .045f, .965f, .955f); Frame(shell, Paper);
        var title = Find(shell, "Title").GetComponent<TMP_Text>(); Place(title.rectTransform, .06f, .895f, .94f, .985f); TextStyle(title, 44, true);
        RectTransform photo = Find(shell, "PhotoboothFrame") as RectTransform;
        Place(photo, .025f, .145f, .34f, .875f); Frame(photo, Cream);
        Place(Find(photo, "CustomizationPortraitAnchor") as RectTransform, .06f, .14f, .94f, .90f);
        TMP_Text rotate = Find(photo, "RotateHint").GetComponent<TMP_Text>(); Place(rotate.rectTransform, .08f, .025f, .92f, .10f); TextStyle(rotate, 21);
        RectTransform options = Find(shell, "WardrobeOptions") as RectTransform;
        Place(options, .355f, .145f, .975f, .875f); Frame(options, Cream);
        TMP_Text heading = Find(options, "CategoryTitle").GetComponent<TMP_Text>(); Place(heading.rectTransform, .04f, .79f, .96f, .855f); TextStyle(heading, 27, true);
        SerializedProperty tabs = data.FindProperty("categoryTabs");
        for (int i = 0; i < tabs.arraySize; i++)
        {
            Button button = tabs.GetArrayElementAtIndex(i).FindPropertyRelative("button").objectReferenceValue as Button;
            Place((RectTransform)button.transform, .018f + i * .196f, .875f, .202f + i * .196f, .977f);
            StyleButton(button, Paper, 22);
            var selected = tabs.GetArrayElementAtIndex(i).FindPropertyRelative("selectedVisual").objectReferenceValue as GameObject;
            Theme(selected.GetComponent<Image>(), Gold); Place((RectTransform)selected.transform, .02f, .05f, .98f, .95f);
        }
        RectTransform grid = Find(options, "CosmeticGrid") as RectTransform;
        Place(grid, .02f, .025f, .98f, .775f); grid.GetComponent<GridLayoutGroup>().enabled = false;
        var slots = data.FindProperty("optionSlots");
        for (int i = 0; i < slots.arraySize; i++)
        {
            CosmeticOptionButton slot = slots.GetArrayElementAtIndex(i).objectReferenceValue as CosmeticOptionButton;
            float x = (i % 4) * .25f, y = i < 4 ? .51f : 0;
            Place((RectTransform)slot.transform, x + .008f, y + .015f, x + .242f, y + .49f);
            StyleButton(slot.GetComponent<Button>(), Paper, 22);
            Place(Find(slot.transform, "Icon") as RectTransform, .12f, .29f, .88f, .91f);
            TMP_Text label = Find(slot.transform, "Label").GetComponent<TMP_Text>(); Place(label.rectTransform, .055f, .035f, .945f, .245f); TextStyle(label, 22);
            Image highlight = Find(slot.transform, "SelectedBorder").GetComponent<Image>(); Theme(highlight, new Color(Gold.r, Gold.g, Gold.b, .35f));
            Image locked = Find(slot.transform, "Locked").GetComponent<Image>(); Theme(locked, new Color(Wood.r, Wood.g, Wood.b, .18f));
            TMP_Text lockLabel = Find(locked.transform, "LockLabel").GetComponent<TMP_Text>(); Place(lockLabel.rectTransform, .1f, .73f, .9f, .9f); TextStyle(lockLabel, 17, true);
        }
        // Reuse the panel on prefab instances. Reparenting source-prefab children
        // out of it is not permitted by Unity's prefab override system.
        Transform colorLabel = Find(options, "ColorLabel");
        var colors = data.FindProperty("colorSlots");
        RectTransform detail = Rect("SelectedItemPanel", options); Place(detail, .025f, .02f, .975f, .78f);
        Frame(detail, Paper);
        Button back = Button("BackToItems", detail, "‹ ITEMS", Paper, .035f, .88f, .27f, .975f);
        Wire(back, controller.CloseItemEditor);
        TMP_Text name = Label("ItemTitle", detail, "Engineering Hard Hat", 32, .31f, .88f, .96f, .975f, true);
        RectTransform iconRect = Rect("ItemIcon", detail);
        Image icon = iconRect.GetComponent<Image>() ?? iconRect.gameObject.AddComponent<Image>(); Place(icon.rectTransform, .045f, .43f, .30f, .83f); icon.preserveAspect = true; icon.raycastTarget = false;
        Label("PreviewHint", detail, "LIVE PREVIEW\nDrag your builder to rotate", 19, .04f, .31f, .31f, .42f);
        Button equip = Button("AccessoryAction", detail, "UNEQUIP ITEM", Cream, .04f, .18f, .31f, .28f);
        Wire(equip, controller.ToggleSelectedAccessory);
        Label("MaterialHeading", detail, "ITEM COLORS", 23, .35f, .79f, .95f, .86f, true);
        Transform dropdown = detail.Find("MaterialSelector");
        if (dropdown != null) dropdown.gameObject.SetActive(false);
        TMP_Text hint = Label("MaterialHint", detail, "Pick a color for each part. Changes stay in your preview until Save Look.", 19, .35f, .68f, .95f, .78f);
        colorLabel.gameObject.SetActive(false);
        for (int i = 0; i < colors.arraySize; i++)
        {
            Button swatch = colors.GetArrayElementAtIndex(i).FindPropertyRelative("button").objectReferenceValue as Button;
            if (swatch != null) swatch.gameObject.SetActive(false);
        }
        ScrollRect colorScroll = AuthorColorRows(controller, detail);
        AuthorReveal(detail);
        Button reset = Button("ResetItemColors", detail, "RESTORE ITEM COLORS", Cream, .35f, .08f, .95f, .20f);
        Wire(reset, controller.ResetItemColors);
        Label("DefaultColorHint", detail, "D = original material  ·  Save Look keeps your changes", 17, .035f, .012f, .965f, .065f);
        Button cancel = Find(shell, "CancelButton").GetComponent<Button>(); Place((RectTransform)cancel.transform, .355f, .035f, .65f, .12f); StyleButton(cancel, Paper, 30);
        Button save = Find(shell, "SaveLookButton").GetComponent<Button>(); Place((RectTransform)save.transform, .67f, .035f, .975f, .12f); StyleButton(save, Gold, 30);
        data.Update();
        data.FindProperty("itemListPanel").objectReferenceValue = grid.gameObject;
        data.FindProperty("selectedItemPanel").objectReferenceValue = detail.gameObject;
        data.FindProperty("selectedItemTitle").objectReferenceValue = name;
        data.FindProperty("selectedItemIcon").objectReferenceValue = icon;
        data.FindProperty("materialHint").objectReferenceValue = hint;
        data.FindProperty("materialDropdown").objectReferenceValue = null;
        data.FindProperty("materialColorScroll").objectReferenceValue = colorScroll;
        var colorRows = colorScroll.content.GetComponentsInChildren<CosmeticMaterialColorRow>(true);
        SerializedProperty rows = data.FindProperty("materialColorRows"); rows.arraySize = colorRows.Length;
        for (int i = 0; i < rows.arraySize; i++) rows.GetArrayElementAtIndex(i).objectReferenceValue = colorRows[i];
        data.FindProperty("accessoryActionButton").objectReferenceValue = equip;
        data.FindProperty("accessoryActionLabel").objectReferenceValue = equip.GetComponentInChildren<TMP_Text>(true);
        data.ApplyModifiedPropertiesWithoutUndo(); detail.gameObject.SetActive(false); grid.gameObject.SetActive(true);
        if (before != string.Join("|", preserve.Select(field => data.FindProperty(field).propertyPath + ":" + FieldJSON(controller, field))))
            throw new Exception("Wardrobe catalog/portrait/save bindings changed.");
        foreach (var pair in events) if (pair.Key != null && JsonUtility.ToJson(pair.Key.onClick) != pair.Value)
            throw new Exception("An original wardrobe callback changed: " + pair.Key.name);
        EditorUtility.SetDirty(controller);
        CenterPortrait(controller);
    }

    private static ScrollRect AuthorColorRows(CharacterCustomizationController controller, Transform parent)
    {
        var catalog = (List<CosmeticDefinition>)Field(controller, "cosmetics");
        var mirror = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Loading/NewCharacterPreview.prefab").GetComponent<PlayerCosmeticMirror>();
        int rowCount = Mathf.Max(1, mirror.cosmeticBindings.Max(binding => CosmeticBindingUtility.GetMaterialSlots(binding).Count));
        int paletteCount = Mathf.Max(1, catalog.Where(item => item != null && item.availableColors != null).Max(item => item.availableColors.Length));
        RectTransform root = Rect("ColorPickers", parent); Place(root, .34f, .235f, .96f, .665f);
        ScrollRect scroll = Ensure<ScrollRect>(root.gameObject);
        scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 24;
        RectTransform viewport = Rect("Viewport", root); Inset(viewport, 0);
        if (viewport.GetComponent<RectMask2D>() == null) viewport.gameObject.AddComponent<RectMask2D>();
        Image surface = Ensure<Image>(viewport.gameObject); surface.color = new Color(Cream.r, Cream.g, Cream.b, .25f);
        RectTransform content = Rect("Content", viewport); content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one;
        content.pivot = new Vector2(.5f, 1); content.anchoredPosition = Vector2.zero; content.sizeDelta = Vector2.zero;
        VerticalLayoutGroup layout = Ensure<VerticalLayoutGroup>(content.gameObject);
        layout.spacing = 12; layout.padding = new RectOffset(4, 4, 4, 4); layout.childControlHeight = layout.childControlWidth = true;
        layout.childForceExpandHeight = false; layout.childForceExpandWidth = true;
        ContentSizeFitter fitter = Ensure<ContentSizeFitter>(content.gameObject);
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        for (int i = 0; i < rowCount; i++)
        {
            RectTransform rect = Rect("ColorRow_" + (i + 1), content); Frame(rect, Cream);
            LayoutElement element = Ensure<LayoutElement>(rect.gameObject);
            element.minHeight = element.preferredHeight = 96; element.flexibleHeight = 0;
            var row = Ensure<CosmeticMaterialColorRow>(rect.gameObject);
            TMP_Text label = Label("ColorLabel", rect, "COLOR " + (i + 1), 22, .035f, .64f, .94f, .95f, true);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            var rowData = new SerializedObject(row); rowData.FindProperty("label").objectReferenceValue = label;
            SerializedProperty swatches = rowData.FindProperty("swatches"); swatches.arraySize = paletteCount;
            for (int j = 0; j < paletteCount; j++)
            {
                float width = .90f / paletteCount;
                Button swatch = Button("Swatch_" + j, rect, j == 0 ? "D" : "", Cream, .035f + j * width, .12f, .035f + j * width + width * .83f, .57f);
                Image chip = Ensure<Image>(Rect("Chip", swatch.transform).gameObject);
                Inset(chip.rectTransform, 5); Theme(chip, j == 0 ? Color.white : Gold); chip.raycastTarget = false;
                swatch.GetComponentInChildren<TMP_Text>(true).transform.SetAsLastSibling();
                Image selected = Ensure<Image>(Rect("Selected", swatch.transform).gameObject);
                Theme(selected, Gold); Place(selected.rectTransform, .08f, .02f, .92f, .085f); selected.raycastTarget = false; selected.gameObject.SetActive(false);
                var entry = swatches.GetArrayElementAtIndex(j);
                entry.FindPropertyRelative("button").objectReferenceValue = swatch;
                entry.FindPropertyRelative("colorImage").objectReferenceValue = chip;
                entry.FindPropertyRelative("selectedVisual").objectReferenceValue = selected.gameObject;
            }
            rowData.ApplyModifiedPropertiesWithoutUndo(); rect.gameObject.SetActive(false);
        }
        scroll.content = content; scroll.viewport = viewport; return scroll;
    }

    private static void AuthorReveal(RectTransform target)
    {
        var motion = Ensure<AuthoredUIReveal>(target.gameObject);
        var group = Ensure<CanvasGroup>(target.gameObject);
        group.alpha = 1; group.interactable = group.blocksRaycasts = true;
        var data = new SerializedObject(motion); data.FindProperty("visual").objectReferenceValue = target;
        data.FindProperty("group").objectReferenceValue = group; data.FindProperty("duration").floatValue = .18f;
        data.FindProperty("startScale").floatValue = .985f; data.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void CenterPortrait(CharacterCustomizationController controller)
    {
        var portrait = Field(controller, "sharedPortrait") as RectTransform;
        var area = Field(controller, "profilePortraitAnchor") as RectTransform;
        if (portrait == null || area == null || portrait.parent != area.parent) return;
        Place(area, .07f, .055f, .93f, .945f);
        var fit = Ensure<AlmanacPortraitFit>(portrait.gameObject);
        var data = new SerializedObject(fit); data.FindProperty("homeArea").objectReferenceValue = area; data.ApplyModifiedPropertiesWithoutUndo();
        Canvas.ForceUpdateCanvases(); fit.FitNow();
        RecordOverrides(portrait.gameObject); RecordOverrides(area.gameObject);
    }

    private static void StyleBookmarks(AlmanacManager book)
    {
        AlmanacLayoutAuthoring.Apply(book);
    }

    private static TMP_Dropdown BuildDropdown(Transform parent)
    {
        RectTransform root = Rect("MaterialSelector", parent); Frame(root, Cream);
        TMP_Dropdown existing = root.GetComponent<TMP_Dropdown>();
        if (existing != null) return existing;
        TMP_Dropdown dropdown = root.gameObject.AddComponent<TMP_Dropdown>(); dropdown.targetGraphic = root.GetComponent<Image>();
        TMP_Text caption = Label("Label", root, "Shell material", 23, .04f, .08f, .88f, .92f);
        Label("Arrow", root, "v", 23, .90f, .1f, .98f, .9f);
        RectTransform template = Rect("Template", root); Frame(template, Cream); template.anchorMin = new Vector2(0, 0); template.anchorMax = new Vector2(1, 0);
        template.pivot = new Vector2(.5f, 1); template.anchoredPosition = new Vector2(0, -8); template.sizeDelta = new Vector2(0, 220);
        ScrollRect scroll = template.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
        RectTransform viewport = Rect("Viewport", template); Inset(viewport, 6); viewport.gameObject.AddComponent<RectMask2D>();
        RectTransform content = Rect("Content", viewport); content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(.5f, 1); content.sizeDelta = new Vector2(0, 48);
        RectTransform item = Rect("Item", content); Place(item, 0, 0, 1, 1); Theme(item.gameObject.AddComponent<Image>(), Paper);
        Toggle toggle = item.gameObject.AddComponent<Toggle>(); toggle.targetGraphic = item.GetComponent<Image>();
        Image check = Rect("Selected", item).gameObject.AddComponent<Image>(); Theme(check, Gold); Place(check.rectTransform, .015f, .16f, .04f, .84f); check.raycastTarget = false; toggle.graphic = check;
        TMP_Text itemLabel = Label("ItemLabel", item, "Material", 22, .065f, .05f, .97f, .95f); itemLabel.alignment = TextAlignmentOptions.MidlineLeft;
        dropdown.template = template; dropdown.captionText = caption; dropdown.itemText = itemLabel; scroll.viewport = viewport; scroll.content = content;
        template.gameObject.SetActive(false); return dropdown;
    }

    private static void StyleShop(ShopManager shop)
    {
        if (shop.Panel == null) return;
        string gates = FieldJSON(shop, "onOpenTutorial") + FieldJSON(shop, "blockClosingUntilFinalTutorialStep");
        Transform panel = shop.Panel.transform; Frame(panel as RectTransform, Paper, "AuthoredShopFrame");
        foreach (TMP_Text text in panel.GetComponentsInChildren<TMP_Text>(true)) TextStyle(text, text.fontSizeMax > 0 ? text.fontSizeMax : text.fontSize);
        foreach (Button button in panel.GetComponentsInChildren<Button>(true))
            if (button.GetComponentInChildren<TMP_Text>(true) != null)
                StyleButton(button, button.name.Contains("Confirm") ? Gold : Cream, 27);
        TMP_Text title = Find(panel, "Title").GetComponent<TMP_Text>(); Place(title.rectTransform, .20f, .89f, .58f, .975f); TextStyle(title, 48, true);
        foreach (string field in new[] { "currencyText", "secondaryCurrencyText" })
        {
            TMP_Text currency = Field(shop, field) as TMP_Text; if (currency == null) continue;
            TextStyle(currency, 32); currency.fontStyle = FontStyles.Normal; currency.fontSizeMin = 18;
            currency.enableWordWrapping = false; currency.alignment = TextAlignmentOptions.MidlineRight;
        }
        Transform tabs = Find(panel, "CategoryTabs"); Place(tabs as RectTransform, .04f, .775f, .96f, .86f);
        ScrollRect scroll = Find(panel, "ItemScrollView").GetComponent<ScrollRect>(); Place(scroll.transform as RectTransform, .04f, .055f, .96f, .75f);
        AuthorReveal(panel as RectTransform); AuthorReveal(scroll.transform as RectTransform);
        var grid = scroll.content.GetComponent<GridLayoutGroup>(); grid.cellSize = new Vector2(330, 395); grid.spacing = new Vector2(20, 20);
        foreach (ShopItemUI card in panel.GetComponentsInChildren<ShopItemUI>(true)) StyleShopCard(card);
        if (gates != FieldJSON(shop, "onOpenTutorial") + FieldJSON(shop, "blockClosingUntilFinalTutorialStep")) throw new Exception("Story/purchase gates changed.");
    }
    private static void StyleShopCard(ShopItemUI card)
    {
        Transform root = card.transform; Frame(root as RectTransform, Cream);
        TMP_Text title = Find(root, "Title").GetComponent<TMP_Text>(); Place(title.rectTransform, .055f, .78f, .945f, .95f); TextStyle(title, 26, true);
        Place(Find(root, "Icon") as RectTransform, .16f, .40f, .84f, .74f);
        TMP_Text description = Find(root, "Description").GetComponent<TMP_Text>(); Place(description.rectTransform, .07f, .20f, .93f, .36f); TextStyle(description, 20);
        Button buy = Find(root, "BuyButton").GetComponent<Button>(); Place(buy.transform as RectTransform, .12f, .04f, .88f, .17f); StyleButton(buy, Gold, 26);
        Place(Find(root, "OwnedBadge") as RectTransform, .61f, .945f, .94f, .992f);
        TextStyle(Find(root, "OwnedBadge").GetComponentInChildren<TMP_Text>(true), 15, true);
    }

    private static string FieldJSON(object target, string name) => JsonUtility.ToJson(new Box { value = Field(target, name) is Object obj ? obj.GetInstanceID().ToString() : JsonUtility.ToJson(Field(target, name)) });
    [Serializable] private class Box { public string value; }
    private static object Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static Transform Find(Transform root, string name) => root.GetComponentsInChildren<Transform>(true).First(child => child.name == name);
    private static RectTransform Rect(string name, Transform parent)
    { Transform existing = parent.Find(name); if (existing != null) return existing as RectTransform;
      var obj = new GameObject(name, typeof(RectTransform)); obj.layer = LayerMask.NameToLayer("UI"); obj.transform.SetParent(parent, false); return obj.GetComponent<RectTransform>(); }
    private static void Place(RectTransform rect, float x0, float y0, float x1, float y1)
    { rect.anchorMin = new Vector2(x0, y0); rect.anchorMax = new Vector2(x1, y1); rect.pivot = new Vector2(.5f, .5f); rect.offsetMin = rect.offsetMax = Vector2.zero; rect.localScale = Vector3.one; }
    private static void Inset(RectTransform rect, float pixels)
    { Place(rect, 0, 0, 1, 1); rect.offsetMin = Vector2.one * pixels; rect.offsetMax = -Vector2.one * pixels; }
    private static void Theme(Image image, Color color)
    { image.sprite = rounded; image.type = Image.Type.Sliced; image.color = color; }
    private static void Frame(RectTransform rect, Color fill, string paperName = "AuthoredFramePaper")
    {
        Image image = rect.GetComponent<Image>() ?? rect.gameObject.AddComponent<Image>(); Theme(image, Wood);
        Transform old = rect.Find(paperName); RectTransform paper = old != null ? old as RectTransform : Rect(paperName, rect);
        Inset(paper, 5); Image background = paper.GetComponent<Image>() ?? paper.gameObject.AddComponent<Image>(); Theme(background, fill); background.raycastTarget = false; paper.SetAsFirstSibling();
        Outline outline = rect.GetComponent<Outline>(); if (outline != null) outline.enabled = false;
    }
    private static void TextStyle(TMP_Text text, float size, bool bold = false)
    { text.font = font; text.color = Wood; text.fontSize = text.fontSizeMax = size; text.fontSizeMin = Mathf.Min(16, size); text.enableAutoSizing = true; text.enableWordWrapping = true; text.overflowMode = TextOverflowModes.Ellipsis; text.raycastTarget = false; if (bold) text.fontStyle = FontStyles.Bold; }
    private static TMP_Text Label(string name, Transform parent, string value, float size, float x0, float y0, float x1, float y1, bool bold = false)
    { var rect = Rect(name, parent); var text = rect.GetComponent<TMP_Text>() ?? rect.gameObject.AddComponent<TextMeshProUGUI>(); Place(text.rectTransform, x0, y0, x1, y1); text.text = value; text.alignment = TextAlignmentOptions.Center; TextStyle(text, size, bold); return text; }
    private static Button Button(string name, Transform parent, string label, Color fill, float x0, float y0, float x1, float y1)
    { RectTransform rect = Rect(name, parent); Place(rect, x0, y0, x1, y1); Frame(rect, fill); var button = rect.GetComponent<Button>() ?? rect.gameObject.AddComponent<Button>(); button.targetGraphic = rect.GetComponent<Image>(); Label("Label", rect, label, 23, .045f, .08f, .955f, .92f); return button; }
    private static void Wire(Button button, UnityEngine.Events.UnityAction action)
    { for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
          if (button.onClick.GetPersistentTarget(i) == action.Target as Object && button.onClick.GetPersistentMethodName(i) == action.Method.Name) return;
      UnityEventTools.AddPersistentListener(button.onClick, action); }
    private static void StyleButton(Button button, Color fill, float size)
    { Frame(button.transform as RectTransform, fill); foreach (TMP_Text text in button.GetComponentsInChildren<TMP_Text>(true)) TextStyle(text, size); }

    private static void ValidateWardrobe(CharacterCustomizationController wardrobe)
    {
        foreach (string name in new[] { "itemListPanel", "selectedItemPanel", "materialColorScroll", "selectedItemTitle" })
            if (Field(wardrobe, name) == null) throw new Exception("Missing item-editor binding: " + name);
        if (Field(wardrobe, "materialDropdown") != null || wardrobe.GetComponentsInChildren<TMP_Dropdown>(false).Length != 0)
            throw new Exception("Material dropdown is still exposed.");
        foreach (CosmeticOptionButton option in wardrobe.GetComponentsInChildren<CosmeticOptionButton>(true))
        {
            var icon = (RectTransform)Find(option.transform, "Icon"); var label = (RectTransform)Find(option.transform, "Label");
            if (label.anchorMax.y >= icon.anchorMin.y) throw new Exception("Item name overlaps icon.");
        }
        ValidateItemEditor(wardrobe);
    }

    private static void ValidateItemEditor(CharacterCustomizationController source)
    {
        Scene preview = EditorSceneManager.NewPreviewScene();
        Material first = null, second = null; CosmeticDefinition definition = null;
        try
        {
            var clone = Object.Instantiate(source.gameObject); SceneManager.MoveGameObjectToScene(clone, preview);
            var controller = clone.GetComponent<CharacterCustomizationController>();
            var model = new GameObject("TemporaryItem", typeof(MeshRenderer)); SceneManager.MoveGameObjectToScene(model, preview);
            first = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")); second = new Material(first);
            model.GetComponent<Renderer>().sharedMaterials = new[] { first, second };
            var mirrorObject = new GameObject("TemporaryMirror"); SceneManager.MoveGameObjectToScene(mirrorObject, preview);
            model.transform.SetParent(mirrorObject.transform, false);
            var mirror = mirrorObject.AddComponent<PlayerCosmeticMirror>();
            mirror.cosmeticBindings = new List<CosmeticModelBinding> { new CosmeticModelBinding { cosmeticID = "TemporaryEditorItem", category = CosmeticCategory.Shirt, models = new List<GameObject> { model } } };
            definition = ScriptableObject.CreateInstance<CosmeticDefinition>(); definition.EditorConfigure("TemporaryEditorItem", "Temporary Item", CosmeticCategory.Shirt, "", true, new[] { Color.clear, Color.red, Color.blue });
            var loadout = new CosmeticLoadoutData(); var original = loadout.Clone();
            SetField(controller, "previewMirror", mirror); SetField(controller, "previewLoadout", loadout);
            SetField(controller, "cosmetics", new List<CosmeticDefinition> { definition });
            Invoke(controller, "SelectCosmetic", definition);
            var rows = (List<CosmeticMaterialColorRow>)Field(controller, "materialColorRows");
            if (!((GameObject)Field(controller, "selectedItemPanel")).activeSelf || ((GameObject)Field(controller, "itemListPanel")).activeSelf ||
                rows.Count(row => row.gameObject.activeSelf) != 2) throw new Exception("Item editor did not open both color pickers.");
            var swatches = (List<CosmeticColorButtonBinding>)Field(rows[1], "swatches");
            swatches[2].button.onClick.Invoke();
            var slots = CosmeticBindingUtility.GetMaterialSlots(mirror.cosmeticBindings[0]);
            if (loadout.GetMaterialColor(definition.PermanentID, slots[1].Key, CosmeticCategory.Shirt) != Color.blue ||
                loadout.GetMaterialColor(definition.PermanentID, slots[0].Key, CosmeticCategory.Shirt) != Color.clear || original.materialColors.Count != 0)
                throw new Exception("Item editor changed the wrong material or original save draft.");
            controller.ResetItemColors();
            if (slots.Any(slot => loadout.GetMaterialColor(definition.PermanentID, slot.Key, CosmeticCategory.Shirt) != Color.clear)) throw new Exception("Item editor reset failed.");
            model.GetComponent<Renderer>().sharedMaterials = new[] { first };
            Invoke(controller, "OpenItemEditor");
            if (rows.Count(row => row.gameObject.activeSelf) != 1 || rows[0].GetComponentInChildren<TMP_Text>().text != "COLOR 1")
                throw new Exception("Single-material item has extra controls or missing numbered label.");
            var extraMaterials = new List<Material>();
            try
            {
                for (int i = 0; i < rows.Count; i++) extraMaterials.Add(new Material(first));
                model.GetComponent<Renderer>().sharedMaterials = extraMaterials.ToArray();
                Invoke(controller, "OpenItemEditor");
                if (rows.Any(row => !row.gameObject.activeSelf)) throw new Exception("Maximum catalog color count was truncated.");
                ((List<CosmeticColorButtonBinding>)Field(rows.Last(), "swatches"))[1].button.onClick.Invoke();
                var allSlots = CosmeticBindingUtility.GetMaterialSlots(mirror.cosmeticBindings[0]);
                if (loadout.GetMaterialColor(definition.PermanentID, allSlots.Last().Key, CosmeticCategory.Shirt) != Color.red)
                    throw new Exception("Last color picker did not apply independently.");
                controller.ResetItemColors();
            }
            finally { model.GetComponent<Renderer>().sharedMaterials = new[] { first }; foreach (var material in extraMaterials) Object.DestroyImmediate(material); }
            controller.CloseItemEditor();
            if (((GameObject)Field(controller, "selectedItemPanel")).activeSelf || !((GameObject)Field(controller, "itemListPanel")).activeSelf) throw new Exception("Back to items failed.");
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); if (first != null) Object.DestroyImmediate(first); if (second != null) Object.DestroyImmediate(second); if (definition != null) Object.DestroyImmediate(definition); }
    }
    private static void Invoke(object target, string method, params object[] args) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);

    private static void ValidateReveal()
    {
        var obj = new GameObject("TemporaryRevealCheck", typeof(RectTransform), typeof(CanvasGroup));
        Texture2D texture = null; GameObject portraitObject = null;
        try
        {
            AuthorReveal(obj.transform as RectTransform);
            var motion = obj.GetComponent<AuthoredUIReveal>(); var group = obj.GetComponent<CanvasGroup>();
            motion.ApplyProgress(0); if (group.alpha != 0 || obj.transform.localScale.x >= 1) throw new Exception("Reveal did not start.");
            motion.ApplyProgress(1); if (group.alpha != 1 || obj.transform.localScale != Vector3.one) throw new Exception("Reveal did not finish.");
            motion.ApplyProgress(.4f); obj.SetActive(false);
            // Non-ExecuteAlways behaviours do not receive normal Play Mode lifecycle
            // callbacks on edit-only test objects. Sample that callback explicitly.
            Invoke(motion, "OnDisable");
            if (group.alpha != 1 || obj.transform.localScale != Vector3.one) throw new Exception("Interrupted reveal left a hidden/scaled panel.");
            var parent = obj.transform as RectTransform; parent.sizeDelta = new Vector2(640, 320);
            var area = Rect("Area", parent); Place(area, .05f, .05f, .95f, .95f);
            var portrait = Rect("Portrait", parent); portraitObject = portrait.gameObject;
            texture = new Texture2D(32, 32); portrait.gameObject.AddComponent<RawImage>().texture = texture;
            var fit = portrait.gameObject.AddComponent<AlmanacPortraitFit>();
            var data = new SerializedObject(fit); data.FindProperty("homeArea").objectReferenceValue = area; data.ApplyModifiedPropertiesWithoutUndo();
            fit.FitNow();
            if (portrait.anchoredPosition.sqrMagnitude > .01f || Mathf.Abs(portrait.rect.width - portrait.rect.height) > .01f)
                throw new Exception("Portrait was stretched or not centered.");
            var transition = Rect("Transition", parent); portrait.SetParent(transition, false); portrait.anchoredPosition = new Vector2(40, 30);
            fit.FitNow(); if (portrait.anchoredPosition != new Vector2(40, 30)) throw new Exception("Portrait fit fought the wardrobe transition.");
        }
        finally { Object.DestroyImmediate(obj); if (texture != null) Object.DestroyImmediate(texture); }
    }

    private static void ValidateMaterialColors()
    {
        GameObject root = new GameObject("TemporaryCosmeticMaterialCheck");
        Material first = null, second = null;
        try
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            first = new Material(shader); second = new Material(shader); first.color = Color.blue; second.color = Color.green;
            var renderer = root.AddComponent<MeshRenderer>(); renderer.sharedMaterials = new[] { first, second };
            var binding = new CosmeticModelBinding { cosmeticID = "test", category = CosmeticCategory.Shirt, models = new List<GameObject> { root } };
            var loadout = new CosmeticLoadoutData { shirtID = "test" };
            var slots = CosmeticBindingUtility.GetMaterialSlots(binding);
            loadout.SetMaterialColor("test", slots[0].Key, Color.red);
            CosmeticBindingUtility.Apply(new[] { binding }, loadout);
            var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block, 0);
            string property = first.HasProperty("_BaseColor") ? "_BaseColor" : "_Color";
            if (block.GetColor(property) != Color.red) throw new Exception("Selected material did not recolor.");
            renderer.GetPropertyBlock(block, 1); if (!block.isEmpty || second.color != Color.green) throw new Exception("Another material was recolored.");
            loadout.SetMaterialColor("test", slots[0].Key, Color.clear); CosmeticBindingUtility.Apply(new[] { binding }, loadout);
            renderer.GetPropertyBlock(block, 0); if (!block.isEmpty || first.color != Color.blue) throw new Exception("Original material did not restore.");
            loadout.SetMaterialColor("hat", "0.0.0", Color.yellow);
            if (loadout.Clone().GetMaterialColor("hat", "0.0.0", CosmeticCategory.Accessories) != Color.yellow ||
                loadout.GetMaterialColor("vest", "0.0.0", CosmeticCategory.Accessories) != Color.clear)
                throw new Exception("Item colors bleed across accessories or JSON clone.");
            if (loadout.EquippedAppearanceCopy().materialColors.Any(entry => entry.itemID == "hat")) throw new Exception("Unequipped colors sent to multiplayer.");
            loadout.shirtColor = Color.cyan;
            if (loadout.GetMaterialColor("other", "0.0.1", CosmeticCategory.Shirt) != Color.cyan) throw new Exception("Old save color lost.");
            var preview = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Loading/NewCharacterPreview.prefab").GetComponent<PlayerCosmeticMirror>();
            var payload = new CosmeticLoadoutData { hairID = "Hair_Base", shirtID = "Shirt_Base", pantsID = "Pants_Base", shoesID = "Shoes_Base" };
            payload.SetAccessoryEquipped("EngineeringHardHat", true); payload.SetAccessoryEquipped("Accessory_SafetyVest", true);
            foreach (var item in preview.cosmeticBindings)
                foreach (var slot in CosmeticBindingUtility.GetMaterialSlots(item))
                {
                    if (!slot.Material.HasProperty("_BaseColor") && !slot.Material.HasProperty("_Color"))
                        throw new Exception("Material has no supported color property: " + item.cosmeticID + " / " + slot.Label);
                    payload.SetMaterialColor(item.cosmeticID, slot.Key, Color.red);
                }
            // Test the largest current clothing combination, not only defaults.
            foreach (CosmeticCategory category in new[] { CosmeticCategory.Hair, CosmeticCategory.Shirt, CosmeticCategory.Pants, CosmeticCategory.Shoes })
            {
                var largest = preview.cosmeticBindings.Where(item => item.category == category)
                    .OrderByDescending(item => CosmeticBindingUtility.GetMaterialSlots(item).Count).First();
                payload.SetID(category, largest.cosmeticID);
            }
            int bytes = System.Text.Encoding.UTF8.GetByteCount(JsonUtility.ToJson(payload.EquippedAppearanceCopy()));
            if (bytes > 1900) throw new Exception("Equipped appearance exceeds existing Fusion payload: " + bytes);
            File.WriteAllText("Temp/ShopWardrobeMaterialMetrics.txt", "All catalog material slots enumerable and tint-compatible. Largest edited clothing combination + hat/vest equipped JSON: " + bytes + " bytes. Shared material assets untouched.\n");
        }
        finally { Object.DestroyImmediate(root); if (first != null) Object.DestroyImmediate(first); if (second != null) Object.DestroyImmediate(second); }
    }

    private static void RenderWardrobe(CharacterCustomizationController source, string sceneName)
    {
        Render(source.gameObject, "Temp/ShopWardrobe_" + sceneName, clone =>
        {
            clone.SetActive(true); var group = clone.GetComponent<CanvasGroup>(); group.alpha = 1; group.interactable = true;
            var controller = clone.GetComponent<CharacterCustomizationController>();
            var catalog = (List<CosmeticDefinition>)Field(controller, "cosmetics");
            var accessoryItems = catalog.Where(item => item != null && item.category == CosmeticCategory.Accessories).ToList();
            var options = clone.GetComponentsInChildren<CosmeticOptionButton>(true);
            for (int i = 0; i < options.Length; i++) options[i].Bind(i < accessoryItems.Count ? accessoryItems[i] : null, true, i == 1, null);
            var tabs = (List<CosmeticCategoryTabBinding>)Field(controller, "categoryTabs");
            foreach (var tab in tabs) tab.selectedVisual.SetActive(tab.category == CosmeticCategory.Accessories);
        }, clone =>
        {
            var controller = clone.GetComponent<CharacterCustomizationController>();
            SetField(controller, "previewLoadout", new CosmeticLoadoutData());
            SetField(controller, "selectedDefinition", AssetDatabase.LoadAssetAtPath<CosmeticDefinition>("Assets/BridgeBuilder/Data/Cosmetics/EngineeringHardHat.asset"));
            controller.GetType().GetMethod("OpenItemEditor", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(controller, null);
        });
    }
    private static void RenderShop(ShopManager shop, string sceneName)
    {
        if (shop.Panel != null) Render(shop.Panel, "Temp/ShopUI_" + sceneName, clone =>
        {
            clone.SetActive(true);
            foreach (Transform child in clone.transform) if (child.name.Contains("Confirmation") || child.name.Contains("Feedback")) child.gameObject.SetActive(false);
            foreach (var card in clone.GetComponentsInChildren<ShopItemUI>(true)) Object.DestroyImmediate(card.gameObject);
            var content = Find(clone.transform, "ItemScrollView").GetComponent<ScrollRect>().content;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/ShopItemCard.prefab");
            foreach (var item in shop.AllItems.Where(item => item != null && item.category == ShopCategory.Accessory).Take(6))
                Object.Instantiate(prefab, content, false).GetComponent<ShopItemUI>().Bind(item, shop);
            foreach (var tab in (List<ShopCategoryTab>)Field(shop, "categoryTabs"))
            {
                string path = AnimationUtility.CalculateTransformPath(tab.selectedVisual.transform, shop.Panel.transform);
                clone.transform.Find(path).gameObject.SetActive(tab.category == ShopCategory.Accessory);
            }
        }, null);
    }
    private static void SetField(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
    private static void Render(GameObject source, string prefix, Action<GameObject> setup, Action<GameObject> details)
    {
        Scene preview = EditorSceneManager.NewPreviewScene(); RenderTexture previous = RenderTexture.active;
        try
        {
            var root = new GameObject("TemporaryWardrobeCanvas", typeof(RectTransform), typeof(Canvas)); SceneManager.MoveGameObjectToScene(root, preview);
            root.GetComponent<RectTransform>().sizeDelta = new Vector2(1920, 1080); root.transform.localScale = Vector3.one * .01f; root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var clone = Object.Instantiate(source, root.transform, false); Place(clone.transform as RectTransform, 0, 0, 1, 1); setup(clone);
            var cameraObject = new GameObject("TemporaryWardrobeCamera", typeof(Camera)); SceneManager.MoveGameObjectToScene(cameraObject, preview);
            Camera camera = cameraObject.GetComponent<Camera>(); camera.scene = preview; camera.orthographic = true; camera.orthographicSize = 5.4f; camera.transform.position = new Vector3(0, 0, -20); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(139, 111, 88, 255);
            Capture(camera, 1600, 900, prefix + ".png"); root.GetComponent<RectTransform>().sizeDelta = new Vector2(1728, 1080); Capture(camera, 1440, 900, prefix + "_16x10.png");
            root.GetComponent<RectTransform>().sizeDelta = new Vector2(1920, 1080);
            if (details != null) { details(clone); Capture(camera, 1600, 900, prefix + "_Item.png"); }
        }
        finally { RenderTexture.active = previous; EditorSceneManager.ClosePreviewScene(preview); }
    }
    private static void Capture(Camera camera, int width, int height, string path)
    {
        var target = new RenderTexture(width, height, 24); target.Create(); var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        try
        {
            camera.targetTexture = target; Canvas.ForceUpdateCanvases();
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null) camera.Render();
            else UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera, new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply(); File.WriteAllBytes(path, texture.EncodeToPNG());
        }
        finally { camera.targetTexture = null; RenderTexture.active = null; target.Release(); Object.DestroyImmediate(texture); Object.DestroyImmediate(target); }
    }
}
