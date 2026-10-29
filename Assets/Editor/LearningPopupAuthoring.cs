#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Explicitly authors the existing lesson readers and the shared material reader.</summary>
[InitializeOnLoad]
public static class LearningPopupAuthoring
{
    public const string RequestPath = "Temp/learning-popup-authoring.request";
    public const string ReportPath = "Logs/learning-popup-authoring.txt";
    private const string RewardPrefab = "Assets/Prefabs/UI/CollectRewardSystem.prefab";
    private static readonly string[] Scenes = { "Assets/Scenes/CanyonCrossing.unity", "Assets/Scenes/BHAN HOUSE.unity", "Assets/Scenes/Multiplayer/Multiplayer.unity" };
    // Match NameRegistrationUIAuthoring's existing authored frame and action theme.
    private static readonly Color Wood = new Color32(90, 55, 31, 255), Ink = new Color32(77, 53, 36, 255);
    private static readonly Color Cream = new Color32(248, 233, 204, 255), Paper = new Color32(255, 248, 231, 255);
    private static readonly Color Gold = new Color32(231, 158, 35, 255), Sand = new Color32(239, 219, 177, 255);
    private static readonly Color DividerInk = new Color32(177, 131, 76, 255);
    private static TMP_FontAsset font;
    private static Sprite rounded, buttonSprite;
    private static bool running;
    private static double nextPoll;
    private static bool Busy => running || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
        EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer || PrefabStageUtility.GetCurrentPrefabStage() != null;
    static LearningPopupAuthoring() { EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (Busy || EditorApplication.timeSinceStartup < nextPoll || !File.Exists(RequestPath)) return;
        nextPoll = EditorApplication.timeSinceStartup + 1;
        string command = File.ReadAllText(RequestPath).Trim(); File.Delete(RequestPath);
        if (command == "apply") Author();
        else if (command == "validate") Validate();
        else if (command == "preview") CapturePreviews();
        else Report("FAILED\nUse apply, validate or preview for learning popup authoring.");
    }
    private sealed class Target
    {
        public Scene scene;
        public bool opened, presentBefore, initiallyDirty, saved, applied;
        public string diskHash;
        public LessonUIManager[] managers;
    }

    [MenuItem("Tools/Civil Craft/Author Learning Popup Layouts")]
    public static void Author() { Run(false); }
    [MenuItem("Tools/Civil Craft/Validate Authored Learning Popup Layouts")]
    public static void Validate() { Run(true); }
    [MenuItem("Tools/Civil Craft/Capture Learning Popup Previews")]
    public static void CapturePreviews() { if (!Busy) LearningPopupPresentationValidation.Run(); }

    private static void Run(bool verify)
    {
        if (Busy) return;
        running = true;
        Scene active = SceneManager.GetActiveScene(); Object[] selected = Selection.objects;
        SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup();
        Scene[] originalScenes = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).ToArray();
        var unrelated = originalScenes.Where(s => s.isLoaded && !Scenes.Contains(s.path)).ToDictionary(s => s,
            s => new { s.isDirty, roots = s.GetRootGameObjects(), hash = File.Exists(s.path) ? Hash(s.path) : null });
        var targets = new List<Target>(); var report = new StringBuilder("AUTHORED STYLE: Name Registration Reference (v3)\n"); int undo = -1; bool success = false;
        PlayerDataManager saves = PlayerDataManager.Instance; PlayerData player = saves != null ? saves.CurrentData : null;
        string playerBefore = player != null ? JsonUtility.ToJson(player) : null;
        try
        {
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/Bekind Sans SDF.asset");
            rounded = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Elements/UI/LevelResultRounded.png");
            buttonSprite = rounded;
            Require(font != null && rounded != null && buttonSprite != null, "Persistent gameplay font or rounded button artwork is missing.");
            Require(!EditorUtility.IsDirty(AssetDatabase.LoadAssetAtPath<GameObject>(RewardPrefab)), "Save the existing reward prefab edits before authoring.");
            foreach (string path in Scenes) Require(File.Exists(path), "Missing scene: " + path);
            string backup = "Logs/LearningPopupBackups/" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            if (!verify)
            {
                Directory.CreateDirectory(backup);
                File.Copy(RewardPrefab, backup + "/CollectRewardSystem.prefab");
                foreach (string path in Scenes) File.Copy(path, backup + "/" + Path.GetFileName(path));
                report.AppendLine("BACKUP: " + backup);
                Undo.IncrementCurrentGroup(); undo = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Author material and lesson readers");
            }
            // Finish the shared asset before loading unopened scene instances.
            // Existing scene overrides remain intact during prefab propagation.
            AuthorPrefab(verify);
            report.AppendLine("PASS: shared CollectRewardSystem material reader is authored; cosmetic reward controls and callbacks preserved.");
            foreach (string path in Scenes)
            {
                Require(File.Exists(path), "Missing scene: " + path);
                Scene scene = SceneManager.GetSceneByPath(path); bool opened = !scene.IsValid() || !scene.isLoaded;
                if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                var target = new Target { scene = scene, opened = opened, presentBefore = setup.Any(value => value.path == path),
                    initiallyDirty = scene.isDirty, diskHash = Hash(path), managers = All<LessonUIManager>(scene).ToArray() };
                targets.Add(target);
                Require(target.managers.Length == 1 && target.managers[0].Panel != null, "Expected one existing lesson reader in " + path);
                Require(!target.managers[0].transform.IsChildOf(target.managers[0].Panel.transform), "Lesson manager must remain outside its hidden panel.");
                RequiredReferences(target.managers[0]);
            }
            foreach (Target target in targets)
            {
                Transform[] scopes = target.managers.Select(m => m.Panel.transform).ToArray();
                var protectedComponents = All<Component>(target.scene).Where(c => c != null && !scopes.Any(root => c.transform == root || c.transform.IsChildOf(root)))
                    .ToDictionary(c => c, EditorJsonUtility.ToJson);
                var callbacks = All<Button>(target.scene).ToDictionary(button => button, button => JsonUtility.ToJson(button.onClick));
                foreach (LessonUIManager manager in target.managers)
                {
                    if (!verify)
                    {
                        Undo.RegisterFullObjectHierarchyUndo(manager.Panel, "Style authored lesson reader");
                        References refs = RequiredReferences(manager);
                        BuildReader(manager.Panel, refs.title, refs.image, refs.body, refs.scroll, refs.close, "CLOSE");
                        manager.Panel.SetActive(false);
                        EditorUtility.SetDirty(manager.Panel.GetComponent<LearningPopupLayout>());
                        target.applied = true;
                    }
                    ValidateReader(manager.Panel.GetComponent<LearningPopupLayout>(), "CLOSE");
                }
                Require(protectedComponents.All(pair => pair.Key != null && pair.Value == EditorJsonUtility.ToJson(pair.Key)), "An unrelated component or lesson manager changed in " + target.scene.name);
                Require(callbacks.All(pair => pair.Key != null && pair.Value == JsonUtility.ToJson(pair.Key.onClick)), "A persistent button callback changed in " + target.scene.name);
                if (!verify)
                {
                    EditorSceneManager.MarkSceneDirty(target.scene);
                }
            }
            Require(saves == PlayerDataManager.Instance && (player == null || ReferenceEquals(player, saves.CurrentData) && JsonUtility.ToJson(player) == playerBefore), "Authoring changed player data.");
            // All readers must pass before any scene is saved. Existing dirty
            // scenes stay dirty so user edits remain under the user's control.
            foreach (Target target in targets)
            {
                if (!verify && !target.initiallyDirty)
                {
                    Require(target.diskHash == Hash(target.scene.path), "Scene changed externally before save: " + target.scene.path);
                    Require(EditorSceneManager.SaveScene(target.scene), "Could not save " + target.scene.path);
                    target.saved = true;
                }
                report.AppendLine((verify ? "VERIFIED: " : target.saved ? "SAVED: " : "WAIT SAVE: ") + target.scene.name +
                    "; real lesson panel/buttons restyled; manager references, callbacks, gameplay and existing edits preserved.");
            }
            if (undo >= 0) Undo.CollapseUndoOperations(undo);
            success = true; report.Insert(0, targets.Any(t => !verify && t.initiallyDirty) ? "WAIT SAVE\n" : "PASS\n");
        }
        catch (Exception error)
        {
            report.Insert(0, "FAILED\n"); report.AppendLine(error.ToString());
            if (undo >= 0) Undo.RevertAllDownToGroup(undo);
        }
        finally
        {
            foreach (Target target in targets.AsEnumerable().Reverse())
                if (target.opened && target.scene.IsValid() && target.scene.isLoaded)
                {
                    if (!target.scene.isDirty) EditorSceneManager.CloseScene(target.scene, !target.presentBefore);
                    else report.AppendLine("LEFT OPEN: " + target.scene.name + "; unsaved changes retained.");
                }
            if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
            Selection.objects = selected;
            try
            {
                Require(originalScenes.All(scene => scene.IsValid()), "An original scene entry was removed.");
                Require(unrelated.All(pair => pair.Key.isDirty == pair.Value.isDirty && pair.Key.GetRootGameObjects().SequenceEqual(pair.Value.roots) &&
                    (pair.Value.hash == null || Hash(pair.Key.path) == pair.Value.hash)), "An unrelated scene changed.");
                report.AppendLine("PASS: original active scene/selection, unrelated scenes and player data preserved. No learning, discovery, reward or close callback invoked.");
            }
            catch (Exception error) { report.Insert(0, "FAILED SAFETY\n"); report.AppendLine(error.ToString()); success = false; }
            Report(report.ToString()); running = false;
            if (success) Debug.Log("[Learning UI] " + report); else Debug.LogWarning("[Learning UI] See " + ReportPath);
        }
    }

    private static void AuthorPrefab(bool verify)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(RewardPrefab);
        try
        {
            ItemUnlockUI manager = root.GetComponent<ItemUnlockUI>(); Require(manager != null, "Existing reward manager is missing.");
            RectTransform rootRect = root.transform as RectTransform; Require(rootRect != null, "Reward root is not a UI RectTransform.");
            var protectedComponents = root.GetComponentsInChildren<Component>(true).Where(c => c != null && c != manager && c != rootRect &&
                (manager.materialPopup == null || !c.transform.IsChildOf(manager.materialPopup.transform) && c.transform != manager.materialPopup.transform))
                .ToDictionary(c => c, EditorJsonUtility.ToJson);
            string managerBefore = WithoutMaterialPopup(EditorJsonUtility.ToJson(manager));
            Vector2 originalSize = rootRect.sizeDelta; Vector2 originalMin = rootRect.anchorMin, originalMax = rootRect.anchorMax;
            Vector2 originalPosition = rootRect.anchoredPosition, originalPivot = rootRect.pivot; Vector3 scale = rootRect.localScale, position = rootRect.localPosition;
            if (!verify)
            {
                try
                {
                    rootRect.sizeDelta = new Vector2(1920, 1080);
                    LearningPopupLayout old = manager.materialPopup;
                    RectTransform popup = old != null ? (RectTransform)old.transform : Rect(root.transform, "Material Introduction Popup");
                    manager.materialPopup = BuildReader(popup.gameObject, old != null ? old.titleText : null, old != null ? old.contentImage : null,
                        old != null ? old.descriptionText : null, old != null ? old.descriptionScroll : null, old != null ? old.dismissButton : null, "GOT IT");
                    BridgeMaterialSO example = AssetDatabase.LoadAssetAtPath<BridgeMaterialSO>("Assets/BridgeBuilder/Data/Resources/ConcreteBeam.asset");
                    manager.materialPopup.SetContent("Material Introduction", example != null ? example.PopupImage : null,
                        example != null ? example.PopupDescription : "Discover a material to learn its strengths, uses, and limits.");
                    popup.gameObject.SetActive(false); EditorUtility.SetDirty(manager);
                }
                finally { rootRect.sizeDelta = originalSize; }
            }
            ValidateReader(manager.materialPopup, "GOT IT");
            Require(!manager.materialPopup.gameObject.activeSelf, "The authored material reader must start hidden.");
            Require(managerBefore == WithoutMaterialPopup(EditorJsonUtility.ToJson(manager)) && protectedComponents.All(pair => pair.Key != null && pair.Value == EditorJsonUtility.ToJson(pair.Key)),
                "Existing cosmetic reward controls, manager fields or callbacks changed.");
            Require(rootRect.sizeDelta == originalSize && rootRect.anchorMin == originalMin && rootRect.anchorMax == originalMax && rootRect.anchoredPosition == originalPosition &&
                rootRect.pivot == originalPivot && rootRect.localScale == scale && rootRect.localPosition == position, "The reward prefab root pose changed.");
            if (!verify)
            {
                PrefabUtility.SaveAsPrefabAsset(root, RewardPrefab);
                AssetDatabase.ImportAsset(RewardPrefab, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            }
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private sealed class References { public TMP_Text title, body; public Image image; public ScrollRect scroll; public Button close; }
    private static References RequiredReferences(LessonUIManager manager)
    {
        var data = new SerializedObject(manager);
        var refs = new References { title = data.FindProperty("lessonTitleText").objectReferenceValue as TMP_Text,
            body = data.FindProperty("lessonDescriptionText").objectReferenceValue as TMP_Text, image = data.FindProperty("lessonImage").objectReferenceValue as Image,
            scroll = data.FindProperty("descriptionScrollRect").objectReferenceValue as ScrollRect, close = data.FindProperty("closeButton").objectReferenceValue as Button };
        Require(refs.title != null && refs.body != null && refs.image != null && refs.scroll != null && refs.close != null && refs.scroll.content != null && refs.scroll.viewport != null,
            "An existing lesson reader reference is missing. No control was substituted."); return refs;
    }
    private static LearningPopupLayout BuildReader(GameObject root, TMP_Text title, Image image, TMP_Text body, ScrollRect scroll, Button close, string label)
    {
        CozyModalLayout.Stretch(root.transform as RectTransform, 0, 0, 0, 0);
        Image overlay = Ensure<Image>(root); overlay.sprite = null; overlay.material = null;
        overlay.color = new Color(.08f, .055f, .035f, .58f); overlay.raycastTarget = true;
        LearningPopupLayout layout = Ensure<LearningPopupLayout>(root);
        layout.card = Panel(root.transform, "Learning Card", Wood);
        layout.cardSurface = Panel(layout.card, "Cream Surface", Cream); layout.cardSurface.SetAsFirstSibling();
        layout.titleTab = Rect(layout.card, "Title Tab");
        Image oldTab = layout.titleTab.GetComponent<Image>(); if (oldTab != null) { oldTab.enabled = false; oldTab.raycastTarget = false; }
        if (title == null) title = Text(layout.titleTab, "Title", "Material Introduction", false); else title.transform.SetParent(layout.titleTab, false);
        StyleText(title, false); title.alignment = TextAlignmentOptions.Center; layout.titleText = title;
        layout.contextText = Text(layout.card, "Learning Context", label == "GOT IT" ? "MATERIAL INTRODUCTION" : "ENGINEERING LESSON", false);
        layout.contextText.characterSpacing = 1f; layout.contextText.alignment = TextAlignmentOptions.Center;
        layout.headerDivider = Rect(layout.card, "Learning Header Divider");
        Image divider = Ensure<Image>(layout.headerDivider.gameObject);
        divider.sprite = null; divider.type = Image.Type.Simple; divider.color = DividerInk; divider.raycastTarget = false;
        layout.imageFrame = Panel(layout.card, "Picture Frame", Sand);
        layout.imageSurface = Panel(layout.imageFrame, "Picture Surface", Paper); layout.imageSurface.SetAsFirstSibling();
        if (image == null) image = Ensure<Image>(Rect(layout.imageFrame, "Picture").gameObject); else image.transform.SetParent(layout.imageFrame, false);
        image.material = null; image.color = Color.white; image.type = Image.Type.Simple; image.preserveAspect = true; image.raycastTarget = false;
        image.transform.SetAsLastSibling(); layout.contentImage = image;
        layout.descriptionFrame = Panel(layout.card, "Description Frame", Sand);
        layout.descriptionSurface = Panel(layout.descriptionFrame, "Description Surface", Paper); layout.descriptionSurface.SetAsFirstSibling();
        if (scroll == null)
        {
            scroll = Ensure<ScrollRect>(Rect(layout.descriptionFrame, "Description Scroll").gameObject);
            scroll.viewport = Rect(scroll.transform, "Viewport"); scroll.content = Rect(scroll.viewport, "Content");
        }
        else scroll.transform.SetParent(layout.descriptionFrame, false);
        Image hit = Ensure<Image>(scroll.gameObject); hit.color = Color.clear; hit.sprite = null; hit.material = null; hit.raycastTarget = true; hit.canvasRenderer.cullTransparentMesh = false;
        Image viewport = Ensure<Image>(scroll.viewport.gameObject); viewport.color = Color.clear; viewport.sprite = null; viewport.material = null; viewport.raycastTarget = true;
        Mask oldMask = scroll.viewport.GetComponent<Mask>(); if (oldMask != null) oldMask.enabled = false;
        Ensure<RectMask2D>(scroll.viewport.gameObject);
        if (body == null) body = Text(scroll.content, "Description", "Learn how to use this material.", false); else body.transform.SetParent(scroll.content, false);
        StyleText(body, false); body.alignment = TextAlignmentOptions.TopLeft; body.overflowMode = TextOverflowModes.Overflow;
        foreach (Behaviour item in scroll.content.GetComponentsInChildren<Behaviour>(true)) if (item is LayoutGroup || item is ContentSizeFitter) item.enabled = false;
        scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.inertia = true;
        scroll.decelerationRate = .135f; scroll.scrollSensitivity = 34f; scroll.verticalScrollbar = ScrollbarFor(scroll);
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent; layout.descriptionScroll = scroll; layout.descriptionText = body;
        if (close == null) close = Ensure<Button>(Rect(layout.card, "Close").gameObject); else close.transform.SetParent(layout.card, false);
        Image border = Ensure<Image>(close.gameObject); border.sprite = buttonSprite; border.type = Image.Type.Sliced; border.color = Wood;
        border.material = null; border.enabled = true; border.raycastTarget = true;
        RectTransform faceRect = Rect(close.transform, "Cream Face"); Image face = Ensure<Image>(faceRect.gameObject);
        face.sprite = buttonSprite; face.type = Image.Type.Sliced; face.color = Gold; face.enabled = true; face.raycastTarget = false;
        close.targetGraphic = face;
        CozyModalLayout.Stretch(faceRect, 4, 4, 4, 4);
        TMP_Text text = close.transform.Find("Label")?.GetComponent<TMP_Text>() ?? close.GetComponentInChildren<TMP_Text>(true);
        if (text == null) text = Text(close.transform, "Label", label, true);
        text.name = "Label"; text.transform.SetParent(close.transform, false); StyleText(text, false); text.text = label;
        text.fontSize = text.fontSizeMax = 30f; text.fontSizeMin = 23f; text.enableAutoSizing = true;
        text.enabled = true; text.gameObject.SetActive(true); text.alignment = TextAlignmentOptions.Center; text.enableWordWrapping = false;
        foreach (Graphic graphic in close.GetComponentsInChildren<Graphic>(true))
            if (graphic != border && graphic != face && graphic != text) { graphic.enabled = false; graphic.raycastTarget = false; }
        faceRect.SetAsFirstSibling(); text.transform.SetAsLastSibling();
        close.navigation = new Navigation { mode = Navigation.Mode.None }; close.transition = Selectable.Transition.ColorTint;
        ColorBlock colors = close.colors; colors.normalColor = Color.white; colors.highlightedColor = new Color(1, .95f, .82f);
        colors.pressedColor = new Color(.85f, .76f, .61f); colors.selectedColor = colors.highlightedColor; colors.disabledColor = new Color(1, 1, 1, .45f); colors.fadeDuration = .08f; close.colors = colors;
        layout.dismissButton = close; close.transform.SetAsLastSibling();
        for (int index = 0; index < root.transform.childCount; index++)
            if (root.transform.GetChild(index) != layout.card) root.transform.GetChild(index).gameObject.SetActive(false);
        layout.authoredVersion = 3; layout.ApplyLayout();
        foreach (Component component in root.GetComponentsInChildren<Component>(true))
        {
            if (component == null) continue;
            EditorUtility.SetDirty(component); EditorUtility.SetDirty(component.gameObject);
            if (PrefabUtility.IsPartOfPrefabInstance(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            if (PrefabUtility.IsPartOfPrefabInstance(component.gameObject)) PrefabUtility.RecordPrefabInstancePropertyModifications(component.gameObject);
        }
        return layout;
    }
    private static Scrollbar ScrollbarFor(ScrollRect scroll)
    {
        Scrollbar bar = scroll.verticalScrollbar;
        if (bar == null) bar = Ensure<Scrollbar>(Rect(scroll.transform, "Reading Scrollbar").gameObject);
        RectTransform rail = (RectTransform)bar.transform; rail.anchorMin = new Vector2(1, 0); rail.anchorMax = Vector2.one;
        rail.pivot = new Vector2(1, .5f); rail.anchoredPosition = Vector2.zero; rail.sizeDelta = new Vector2(7, 0);
        Image track = Ensure<Image>(rail.gameObject); track.color = Color.clear; track.raycastTarget = true;
        RectTransform handle = bar.handleRect != null ? bar.handleRect : Panel(rail, "Handle", new Color32(146, 109, 70, 255));
        CozyModalLayout.Stretch(handle, 0, 0, 0, 0);
        Image thumb = Ensure<Image>(handle.gameObject); thumb.sprite = rounded; thumb.type = Image.Type.Sliced; thumb.color = new Color32(146, 109, 70, 255); thumb.raycastTarget = false;
        bar.direction = Scrollbar.Direction.BottomToTop; bar.handleRect = handle; bar.targetGraphic = thumb; bar.navigation = new Navigation { mode = Navigation.Mode.None }; return bar;
    }
    private static void ValidateReader(LearningPopupLayout layout, string label)
    {
        Require(layout != null && layout.authoredVersion == 3 && layout.card != null && layout.cardSurface != null && layout.titleTab != null && layout.titleText != null &&
            layout.contextText != null && layout.headerDivider != null &&
            layout.contentImage != null && layout.imageFrame != null && layout.descriptionFrame != null && layout.descriptionText != null && layout.descriptionScroll != null && layout.dismissButton != null,
            "The real authored learning reader is incomplete.");
        TMP_Text caption = layout.dismissButton.GetComponentInChildren<TMP_Text>(true);
        Require(caption != null && caption.text == label && caption.font == font && caption.fontStyle == FontStyles.Normal &&
            layout.titleText.font == font && layout.titleText.fontStyle == FontStyles.Normal && layout.titleText.alignment == TextAlignmentOptions.Center &&
            layout.descriptionText.font == font && layout.contentImage.preserveAspect && layout.contextText.font == font &&
            layout.contextText.text == (label == "GOT IT" ? "MATERIAL INTRODUCTION" : "ENGINEERING LESSON") && layout.contextText.characterSpacing == 1f,
            "The authored reader label/font/aspect differs.");
        Require(layout.card.GetComponent<Image>().color == Wood && layout.cardSurface.GetComponent<Image>().color == Cream &&
            layout.imageSurface.GetComponent<Image>().color == Paper && layout.descriptionSurface.GetComponent<Image>().color == Paper &&
            layout.dismissButton.transform.Find("Cream Face").GetComponent<Image>().color == Gold && layout.dismissButton.GetComponent<Image>().color == Wood &&
            layout.dismissButton.GetComponent<Image>().sprite == rounded && layout.dismissButton.targetGraphic == layout.dismissButton.transform.Find("Cream Face").GetComponent<Image>() &&
            layout.headerDivider.GetComponent<Image>().color == DividerInk && layout.headerDivider.GetComponent<Image>().sprite == null,
            "The authored Name Registration reference styling is missing.");
        Require(layout.titleTab.GetComponent<Image>() == null || !layout.titleTab.GetComponent<Image>().enabled, "The old title plaque remains visible.");
    }
    private static string WithoutMaterialPopup(string json) => Regex.Replace(json, "\"materialPopup\"\\s*:\\s*\\{[^}]*\\}", "\"materialPopup\":{}");
    private static TMP_Text Text(Transform parent, string name, string value, bool bold) { TMP_Text text = Ensure<TextMeshProUGUI>(Rect(parent, name).gameObject); text.text = value; StyleText(text, bold); return text; }
    private static void StyleText(TMP_Text text, bool bold) { text.font = font; text.fontSharedMaterial = font.material; text.color = Ink; text.fontStyle = FontStyles.Normal; text.characterSpacing = 0f; text.richText = true; text.raycastTarget = false; text.enableAutoSizing = false; text.enableWordWrapping = true; text.margin = Vector4.zero; }
    private static RectTransform Panel(Transform parent, string name, Color color) { RectTransform rect = Rect(parent, name); Image image = Ensure<Image>(rect.gameObject); image.sprite = rounded; image.type = Image.Type.Sliced; image.color = color; image.material = null; image.raycastTarget = false; return rect; }
    private static RectTransform Rect(Transform parent, string name) { RectTransform rect = parent.Find(name) as RectTransform; if (rect != null) return rect; var item = new GameObject(name, typeof(RectTransform)); item.layer = 5; rect = (RectTransform)item.transform; rect.SetParent(parent, false); return rect; }
    private static T Ensure<T>(GameObject item) where T : Component => item.GetComponent<T>() ?? item.AddComponent<T>();
    private static IEnumerable<T> All<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true));
    private static string Hash(string path) { using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path))); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Report(string value) { Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, value); }
}
#endif
