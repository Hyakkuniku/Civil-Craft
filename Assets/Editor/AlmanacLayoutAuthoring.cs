using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Opt-in authored layout for both book scenes and the shared prefab. Never modifies player data.</summary>
public static class AlmanacLayoutAuthoring
{
    private const string Request = "Temp/almanac-size-bookmarks-v3.request";
    private const string PreviewRequest = "Temp/almanac-bookmark-placement-preview-v2.request";
    private const string Report = "Temp/AlmanacLayoutValidation.txt";
    private const string Prefab = "Assets/Prefabs/BuildingMode/MANAGERS AND CANVASES.prefab";
    private static readonly string[] Scenes = { "Assets/Scenes/CanyonCrossing.unity", "Assets/Scenes/BHAN HOUSE.unity" };
    private static double nextCheck;
    private const float FrameTop = .925f, BookmarkBaseline = 4f, SelectedLift = 32f;

    [InitializeOnLoadMethod]
    private static void Watch() { EditorApplication.update -= Check; EditorApplication.update += Check; }
    private static void Check()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 2;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (File.Exists(PreviewRequest))
        {
            File.Delete(PreviewRequest);
            try { PreviewOnly(); }
            catch (Exception error) { File.WriteAllText("Temp/AlmanacBookmarkPreviewValidation.txt", "FAIL: " + error); Debug.LogException(error); }
        }
        if (!File.Exists(Request)) return;
        if (Scenes.Any(path => SceneManager.GetSceneByPath(path).isDirty))
        { File.WriteAllText(Report, "WAIT: Stop Play Mode and save these unsaved scenes first: " + string.Join(", ", Scenes.Where(path => SceneManager.GetSceneByPath(path).isDirty)) + ". No edits have been overwritten."); return; }
        File.Delete(Request);
        try { Author(); }
        catch (Exception error) { File.WriteAllText(Report, "FAIL: " + error); Debug.LogException(error); }
    }

    [MenuItem("Tools/Civil Craft/Enlarge And Align Almanac")]
    public static void Author()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || Scenes.Any(path => SceneManager.GetSceneByPath(path).isDirty))
            throw new Exception("Stop Play Mode and save scene edits first.");
        var report = new StringBuilder();
        var prefab = PrefabUtility.LoadPrefabContents(Prefab);
        try
        {
            foreach (var book in prefab.GetComponentsInChildren<AlmanacManager>(true)) Apply(book);
            PrefabUtility.SaveAsPrefabAsset(prefab, Prefab);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        foreach (string path in Scenes)
        {
            Scene scene = SceneManager.GetSceneByPath(path); bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                var book = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<AlmanacManager>(true)).Single();
                var buttons = book.almanacCanvas.GetComponentsInChildren<Button>(true).ToDictionary(button => button, button => JsonUtility.ToJson(button.onClick));
                var profile = book.categories.First(category => category.tabType == AlmanacTabType.General);
                var stats = profile.rightPageZone.GetComponentInChildren<AlmanacPlayerStats>(true);
                string statsBefore = EditorJsonUtility.ToJson(stats);
                Apply(book);
                if (statsBefore != EditorJsonUtility.ToJson(stats) || buttons.Any(pair => pair.Value != JsonUtility.ToJson(pair.Key.onClick)))
                    throw new Exception("Profile data or existing button callbacks changed.");
                Validate(book, report);
                AlmanacBookAuthoring.CaptureAuthoredPreviews(book, scene.name);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new Exception("Could not save " + path);
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }
        AssetDatabase.SaveAssets();
        report.AppendLine("PASS: shared prefab and both scenes saved. All chapters use the larger authored frame; bookmarks share book-relative anchors/baselines and render behind every chapter; selected lift is 32 px. 16:9/16:10/wide-phone bounds and full icon visibility checked. Existing profile data, unlock rules, portrait textures and callbacks preserved. No player saves changed. Live navigation still requires testing.");
        File.WriteAllText(Report, report.ToString());
        Debug.Log("[Almanac layout] Larger book and aligned bookmarks validated in both scenes.");
    }

    internal static void Apply(AlmanacManager book)
    {
        var profile = book.categories.FirstOrDefault(category => category.tabType == AlmanacTabType.General);
        var frame = profile?.leftPageZone?.parent as RectTransform;
        if (frame == null) return;
        var container = frame.parent as RectTransform;
        if (container == null) throw new Exception("Book layout container is missing.");
        Place(container, .02f, .025f, .98f, .975f);
        foreach (var category in book.categories)
        {
            var chapter = category.leftPageZone?.parent as RectTransform;
            if (chapter == null || category.rightPageZone == null || category.rightPageZone.parent != chapter || chapter.parent != container)
                throw new Exception("Chapter halves must share the book's authored layout container: " + category.categoryName);
            // Also normalize the old fixed-size prefab. It must not fail before scene overrides are authored.
            Place(chapter, .015f, .025f, .985f, FrameTop);
            Record(chapter);
        }
        var rounded = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Elements/UI/LevelResultRounded.png");
        book.selectedTabUpOffset = SelectedLift; book.tabTransitionSpeed = 14;
        for (int i = 0; i < book.categories.Count; i++)
        {
            var category = book.categories[i]; Button button = category.tabButton; if (button == null) continue;
            var tab = button.transform as RectTransform;
            if (tab.parent != container) throw new Exception("Bookmark has a different layout parent: " + button.name);
            LayoutGroup layout = container.GetComponent<LayoutGroup>(); if (layout != null) { layout.enabled = false; Record(layout); }
            float center = .075f + i * .092f;
            // No world coordinates: the closed Canvas has zero scale, and tabs must also resize at 16:10.
            tab.anchorMin = new Vector2(Mathf.Lerp(frame.anchorMin.x, frame.anchorMax.x, center - .039f), frame.anchorMax.y);
            tab.anchorMax = new Vector2(Mathf.Lerp(frame.anchorMin.x, frame.anchorMax.x, center + .039f), frame.anchorMax.y);
            tab.pivot = Vector2.one * .5f; tab.anchoredPosition = new Vector2(0, BookmarkBaseline); tab.sizeDelta = new Vector2(0, 100);
            tab.localScale = Vector3.one; tab.localRotation = Quaternion.identity;
            Image background = tab.GetComponent<Image>();
            if (background != null && rounded != null)
            {
                background.sprite = rounded; background.type = Image.Type.Sliced;
                background.color = category.tabType == AlmanacTabType.Contracts ? new Color32(231, 158, 35, 255)
                    : category.tabType == AlmanacTabType.Lessons ? new Color32(211, 183, 139, 255)
                    : category.tabType == AlmanacTabType.Materials ? new Color32(188, 194, 153, 255) : new Color32(248, 233, 204, 255);
                category.activeSprite = category.inactiveSprite = rounded;
                Record(background);
            }
            foreach (var image in tab.GetComponentsInChildren<Image>(true))
                if (image.transform != tab && (category.tabAlertIcon == null || !image.transform.IsChildOf(category.tabAlertIcon.transform)))
                { Place(image.rectTransform, .28f, .67f, .72f, .98f); image.preserveAspect = true; Record(image); Record(image.rectTransform); }
            if (category.tabAlertIcon != null)
            {
                var alert = category.tabAlertIcon.transform as RectTransform;
                Place(alert, .80f, .78f, .97f, .98f); Record(alert);
            }
            Record(tab);
        }
        // Moving a tab from before a chapter TO that chapter's old index shifts
        // the chapter left, putting the tab in front of it. Insert in reverse
        // at the start instead, so all bookmarks stay behind ALL chapter covers.
        foreach (var category in book.categories.AsEnumerable().Reverse())
            if (category.tabButton != null) { category.tabButton.transform.SetAsFirstSibling(); Record((RectTransform)category.tabButton.transform); }
        PlaceControl(book.prevButton, container, new Vector2(.01f, .4775f), new Vector2(84, 84));
        PlaceControl(book.nextButton, container, new Vector2(.99f, .4775f), new Vector2(84, 84));
        foreach (var button in book.almanacCanvas.GetComponentsInChildren<Button>(true))
            for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                if (button.onClick.GetPersistentMethodName(i) == "CloseAlmanac" && button.onClick.GetPersistentTarget(i) == book)
                    PlaceControl(button, container, new Vector2(.96f, .95f), new Vector2(160, 84));
        Record(container); Record(book);
    }

    private static void PlaceControl(Button button, RectTransform container, Vector2 anchor, Vector2 size)
    {
        if (button == null) return;
        var rect = button.transform as RectTransform;
        var wrapper = rect.parent as RectTransform;
        if (wrapper != container && wrapper != container.parent &&
            (wrapper.parent == container || wrapper.parent == container.parent))
        {
            // The scene's close button is inside its authored image wrapper;
            // position that wrapper without reparenting or changing the click binding.
            PlaceControlRect(wrapper, container, anchor, size);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one * .5f; rect.offsetMin = rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one; Record(rect);
            return;
        }
        PlaceControlRect(rect, container, anchor, size);
    }
    private static void PlaceControlRect(RectTransform rect, RectTransform container, Vector2 anchor, Vector2 size)
    {
        if (rect.parent == container.parent)
            anchor = new Vector2(Mathf.Lerp(container.anchorMin.x, container.anchorMax.x, anchor.x), Mathf.Lerp(container.anchorMin.y, container.anchorMax.y, anchor.y));
        else if (rect.parent != container) throw new Exception("Navigation control has an unexpected layout parent: " + rect.name);
        rect.anchorMin = rect.anchorMax = anchor; rect.pivot = Vector2.one * .5f;
        rect.anchoredPosition = Vector2.zero; rect.sizeDelta = size; rect.localScale = Vector3.one;
        rect.SetAsLastSibling(); Record(rect);
    }
    private static void Place(RectTransform rect, float x0, float y0, float x1, float y1)
    {
        rect.anchorMin = new Vector2(x0, y0); rect.anchorMax = new Vector2(x1, y1); rect.pivot = Vector2.one * .5f;
        rect.offsetMin = rect.offsetMax = Vector2.zero; rect.localScale = Vector3.one; rect.localRotation = Quaternion.identity;
    }
    private static void Record(Component component)
    {
        EditorUtility.SetDirty(component);
        if (PrefabUtility.IsPartOfPrefabInstance(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
    }

    // Tests clones of the current layout even while source scenes have unsaved edits.
    // It never applies those edits to the source or writes a source scene/prefab.
    private static void PreviewOnly()
    {
        var report = new StringBuilder();
        foreach (string path in Scenes)
        {
            Scene scene = SceneManager.GetSceneByPath(path); bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var source = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<AlmanacManager>(true)).Single();
                Transform sourcePanel = source.categories[0].leftPageZone.parent.parent.parent;
                var root = new GameObject("TemporaryBookmarkCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler)); SceneManager.MoveGameObjectToScene(root, preview);
                root.GetComponent<RectTransform>().sizeDelta = new Vector2(1920, 1080); root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                var sourceScaler = source.almanacCanvas.GetComponentInParent<CanvasScaler>();
                var scaler = root.GetComponent<CanvasScaler>(); scaler.referenceResolution = sourceScaler != null ? sourceScaler.referenceResolution : new Vector2(1920, 1080);
                scaler.matchWidthOrHeight = sourceScaler != null ? sourceScaler.matchWidthOrHeight : .5f;
                var panel = Object.Instantiate(sourcePanel.gameObject, root.transform, false); panel.SetActive(true); Place((RectTransform)panel.transform, 0, 0, 1, 1);
                var ownerObject = new GameObject("TemporaryBookmarkOwner"); SceneManager.MoveGameObjectToScene(ownerObject, preview);
                var book = ownerObject.AddComponent<AlmanacManager>(); book.almanacCanvas = panel;
                foreach (var category in source.categories)
                    book.categories.Add(new AlmanacCategory {
                        categoryName = category.categoryName, tabType = category.tabType, visibilityMode = category.visibilityMode,
                        leftPageZone = panel.transform.Find(AnimationUtility.CalculateTransformPath(category.leftPageZone, sourcePanel)),
                        rightPageZone = panel.transform.Find(AnimationUtility.CalculateTransformPath(category.rightPageZone, sourcePanel)),
                        tabButton = panel.transform.Find(AnimationUtility.CalculateTransformPath(category.tabButton.transform, sourcePanel)).GetComponent<Button>(),
                        tabAlertIcon = category.tabAlertIcon != null ? panel.transform.Find(AnimationUtility.CalculateTransformPath(category.tabAlertIcon.transform, sourcePanel)).gameObject : null,
                        activeSprite = category.activeSprite, inactiveSprite = category.inactiveSprite
                    });
                book.prevButton = source.prevButton != null ? panel.transform.Find(AnimationUtility.CalculateTransformPath(source.prevButton.transform, sourcePanel)).GetComponent<Button>() : null;
                book.nextButton = source.nextButton != null ? panel.transform.Find(AnimationUtility.CalculateTransformPath(source.nextButton.transform, sourcePanel)).GetComponent<Button>() : null;
                foreach (var button in panel.GetComponentsInChildren<Button>(true))
                    for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                        if (button.onClick.GetPersistentMethodName(i) == "CloseAlmanac" && button.onClick.GetPersistentTarget(i) == source)
                            UnityEventTools.RegisterPersistentListener(button.onClick, i, book.CloseAlmanac);
                Apply(book); Validate(book, report); AlmanacBookAuthoring.CaptureAuthoredPreviews(book, scene.name + "_BookmarkPlacement");
                ValidateProfileOpening(book, report);
                report.AppendLine("PASS: " + scene.name + " cloned layout validated. Source edits, callbacks and unlock rules unchanged.");
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); if (opened) EditorSceneManager.CloseScene(scene, true); }
        }
        File.WriteAllText("Temp/AlmanacBookmarkPreviewValidation.txt", "PASS: isolated bookmark placement/stacking/lift checks; no source scene, prefab or player save changed.\n" + report);
    }

    private static void ValidateProfileOpening(AlmanacManager book, StringBuilder report)
    {
        // Exercise the same first-category selection used after the HUD button's
        // opening animation. Do not run OpenAlmanac itself: that would touch the
        // live HUD/input coordinator and mark a player's book as read.
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var motion = book.gameObject.AddComponent<AlmanacBookMotion>();
        typeof(AlmanacManager).GetField("bookMotion", flags).SetValue(book, motion);
        var motionData = new SerializedObject(motion);
        var frames = motionData.FindProperty("chapterFrames");
        frames.arraySize = book.categories.Count;
        for (int i = 0; i < book.categories.Count; i++)
            frames.GetArrayElementAtIndex(i).objectReferenceValue = book.categories[i].leftPageZone.parent.Find("AuthoredBookBinding").gameObject;
        motionData.ApplyModifiedPropertiesWithoutUndo();
        motion.Bind(book);
        typeof(AlmanacManager).GetMethod("InitializeBook", flags).Invoke(book, null);
        book.categories[0].tabButton.gameObject.SetActive(true);
        if (book.categories[0].tabType != AlmanacTabType.General)
            throw new Exception("The opening test requires the authored Player category first.");
        var targets = (Dictionary<RectTransform, float>)typeof(AlmanacManager).GetField("targetTabYPositions", flags).GetValue(book);
        var selectFirst = typeof(AlmanacManager).GetMethod("SelectFirstVisibleCategory", flags);
        for (int pass = 0; pass < 2; pass++)
        {
            if (pass == 1)
            {
                // Emulate reopening from another chapter without marking any
                // contract, lesson, or material as read in a real player save.
                typeof(AlmanacManager).GetField("currentCategoryIndex", flags).SetValue(book, 1);
                targets[(RectTransform)book.categories[1].tabButton.transform] = BookmarkBaseline + SelectedLift;
            }
            selectFirst.Invoke(book, null);
            for (int i = 0; i < book.categories.Count; i++)
            {
                var category = book.categories[i];
                var tab = (RectTransform)category.tabButton.transform;
                if (Mathf.Abs(targets[tab] - (BookmarkBaseline + (i == 0 ? SelectedLift : 0))) > .001f)
                    throw new Exception("Opening the Player tab left an incorrect bookmark lift: " + category.categoryName);
                var binding = category.leftPageZone.parent.Find("AuthoredBookBinding");
                if (binding.gameObject.activeSelf != (i == 0))
                    throw new Exception("Opening the Player tab displayed the wrong book cover.");
                foreach (var chapter in book.categories)
                    if (tab.GetSiblingIndex() >= chapter.leftPageZone.parent.GetSiblingIndex())
                        throw new Exception("Opening the Player tab moved a bookmark in front of the page.");
            }
        }
        report.AppendLine("PASS: real initialization and HUD-opening category selection select Player, keep every bookmark behind its cover, target only Player's 32 px lift, and reset a previous chapter on reopen. No live HUD/input or save calls used.");
    }

    private static void Validate(AlmanacManager source, StringBuilder report)
    {
        Scene preview = EditorSceneManager.NewPreviewScene();
        try
        {
            var root = new GameObject("TemporaryAlmanacLayoutCanvas", typeof(RectTransform), typeof(Canvas)); SceneManager.MoveGameObjectToScene(root, preview);
            var canvasRect = root.GetComponent<RectTransform>(); root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            Transform sourceContainer = source.categories[0].leftPageZone.parent.parent;
            Transform sourcePanel = sourceContainer.parent;
            var clone = Object.Instantiate(sourcePanel.gameObject, root.transform, false); clone.SetActive(true);
            Place((RectTransform)clone.transform, 0, 0, 1, 1);
            foreach (Vector2 screen in new[] { new Vector2(1920, 1080), new Vector2(1728, 1080), new Vector2(2340, 1080) })
            {
                CanvasScaler scaler = source.almanacCanvas.GetComponentInParent<CanvasScaler>();
                Vector2 reference = scaler != null ? scaler.referenceResolution : new Vector2(1920, 1080);
                float match = scaler != null ? scaler.matchWidthOrHeight : .5f;
                float scale = Mathf.Pow(screen.x / reference.x, 1 - match) * Mathf.Pow(screen.y / reference.y, match);
                // Match the actual screen-space CanvasScaler, not a fixed 1080-high
                // preview that hides clipping on a wide landscape Game view.
                canvasRect.sizeDelta = screen / scale; Canvas.ForceUpdateCanvases();
                Rect previous = default;
                foreach (var category in source.categories)
                {
                    var chapter = clone.transform.Find(AnimationUtility.CalculateTransformPath(category.leftPageZone.parent, sourcePanel)) as RectTransform;
                    Rect cover = Bounds(chapter, canvasRect);
                    if (cover.width < canvasRect.rect.width * .92f || cover.height < canvasRect.rect.height * .85f) throw new Exception("Book did not enlarge at " + screen);
                    AssertInside(cover, canvasRect.rect, "chapter");
                    var tab = clone.transform.Find(AnimationUtility.CalculateTransformPath(category.tabButton.transform, sourcePanel)) as RectTransform;
                    Rect tabBounds = Bounds(tab, canvasRect); AssertInside(tabBounds, canvasRect.rect, "bookmark");
                    if (Mathf.Abs(tab.anchoredPosition.y - BookmarkBaseline) > .001f || Mathf.Abs(tab.anchorMin.y - FrameTop) > .001f)
                        throw new Exception("Bookmark baselines differ.");
                    if (previous.width > 0 && previous.xMax > tabBounds.xMin) throw new Exception("Bookmarks overlap at " + screen);
                    foreach (var candidate in source.categories)
                    {
                        var otherChapter = clone.transform.Find(AnimationUtility.CalculateTransformPath(candidate.leftPageZone.parent, sourcePanel));
                        if (tab.GetSiblingIndex() >= otherChapter.GetSiblingIndex()) throw new Exception("Bookmark renders above a chapter cover: " + category.categoryName);
                    }
                    previous = tabBounds;
                    foreach (var icon in tab.GetComponentsInChildren<Image>(true))
                        if (icon.transform != tab && (category.tabAlertIcon == null || !icon.transform.IsChildOf(clone.transform.Find(AnimationUtility.CalculateTransformPath(category.tabAlertIcon.transform, sourcePanel)))))
                        {
                            Rect iconBounds = Bounds(icon.rectTransform, canvasRect);
                            // The paper cover extends ~1.8% above the frame. Icons must sit above it, not behind it.
                            if (iconBounds.yMin < cover.yMax + cover.height * .018f - 1) throw new Exception("Bookmark icon is cut by the cover: " + category.categoryName);
                        }
                    tab.anchoredPosition += Vector2.up * source.selectedTabUpOffset;
                    Rect selected = Bounds(tab, canvasRect);
                    AssertInside(selected, canvasRect.rect, "selected bookmark");
                    if (Mathf.Abs(selected.yMin - tabBounds.yMin - SelectedLift) > .05f || canvasRect.rect.yMax - selected.yMax < 7)
                        throw new Exception("Selected bookmark has the wrong lift or too little screen-edge clearance.");
                    tab.anchoredPosition -= Vector2.up * source.selectedTabUpOffset;
                }
                var controls = new List<Button> { source.prevButton, source.nextButton };
                controls.AddRange(source.almanacCanvas.GetComponentsInChildren<Button>(true).Where(button =>
                    Enumerable.Range(0, button.onClick.GetPersistentEventCount()).Any(i =>
                        button.onClick.GetPersistentMethodName(i) == "CloseAlmanac" && button.onClick.GetPersistentTarget(i) == source)));
                foreach (var button in controls)
                {
                    if (button == null) continue;
                    if (!button.transform.IsChildOf(sourcePanel)) throw new Exception("Book control is outside the previewed panel: " + button.name);
                    var rect = clone.transform.Find(AnimationUtility.CalculateTransformPath(button.transform, sourcePanel)) as RectTransform;
                    AssertInside(Bounds(rect, canvasRect), canvasRect.rect, "navigation");
                }
                report.AppendLine("PASS: " + source.gameObject.scene.name + " at " + screen + ": larger frame, aligned/non-overlapping bookmarks behind all covers, full icons, 32 px lift and navigation bounds (actual CanvasScaler dimensions).");
            }
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
    }
    private static Rect Bounds(RectTransform rect, RectTransform space)
    {
        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
        Vector2 min = space.InverseTransformPoint(corners[0]), max = min;
        foreach (var corner in corners) { Vector2 local = space.InverseTransformPoint(corner); min = Vector2.Min(min, local); max = Vector2.Max(max, local); }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }
    private static void AssertInside(Rect rect, Rect available, string item)
    {
        if (rect.xMin < available.xMin - 1 || rect.xMax > available.xMax + 1 || rect.yMin < available.yMin - 1 || rect.yMax > available.yMax + 1)
            throw new Exception(item + " extends off-screen: " + rect + " vs " + available);
    }
}
