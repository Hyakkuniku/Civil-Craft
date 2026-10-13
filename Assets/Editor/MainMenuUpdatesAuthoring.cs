#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Opt-in additive authored UI. Never runs authentication, network, game input or player saves.</summary>
public static class MainMenuUpdatesAuthoring
{
    private const string ScenePath = "Assets/Scenes/Main Menu.unity";
    private const string Request = "Temp/main-menu-updates-authoring.request";
    private const string Report = "Temp/main-menu-updates-authoring-report.txt";
    private const string ValidationRequest = "Temp/main-menu-updates-validation.request";
    private const string ValidationReport = "Temp/main-menu-updates-validation-report.txt";
    private const string ManagerName = "MainMenuUpdatesManager";
    private const string EntryName = "MainMenuUpdatesButton";
    private const string PanelName = "MainMenuUpdatesPanel";
    private static readonly Color Ink = new Color32(75, 50, 36, 255);
    private static readonly Color Cream = new Color32(255, 249, 238, 255);
    private static readonly Color Sand = new Color32(239, 226, 204, 255);
    private static readonly Color Gold = new Color32(239, 166, 49, 255);
    private static Sprite rounded;
    private static TMP_FontAsset font;
    private static double nextCheck;
    private static bool recordingUndo;

    [InitializeOnLoadMethod]
    private static void Watch()
    {
        EditorApplication.update -= CheckRequest;
        EditorApplication.update += CheckRequest;
    }

    private static bool Busy => EditorApplication.isCompiling || EditorApplication.isUpdating ||
        EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer ||
        PrefabStageUtility.GetCurrentPrefabStage() != null;

    private static void CheckRequest()
    {
        if (EditorApplication.timeSinceStartup < nextCheck || Busy) return;
        nextCheck = EditorApplication.timeSinceStartup + 2;
        if (File.Exists(Request))
        {
            File.Delete(Request);
            try { Author(); }
            catch (Exception error) { File.WriteAllText(Report, "FAIL: " + error); Debug.LogException(error); }
        }
        if (File.Exists(ValidationRequest))
        {
            File.Delete(ValidationRequest);
            try { Validate(); }
            catch (Exception error) { File.WriteAllText(ValidationReport, "FAIL: " + error); Debug.LogException(error); }
        }
    }

    [MenuItem("Tools/Civil Craft/Author Main Menu Updates")]
    public static void Author()
    {
        Check(!Busy, "Stay in Edit Mode, finish imports and leave Prefab Mode before authoring Updates.");
        Check(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null, "Main Menu scene is missing.");
        rounded = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Elements/UI/LevelResultRounded.png");
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/Bekind Sans SDF.asset");
        Check(rounded != null && font != null, "The existing rounded sprite or Bekind font is missing.");
        Scene previous = SceneManager.GetActiveScene();
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        bool alreadyDirty = scene.isDirty;
        bool succeeded = false;
        bool saved = false;
        SceneAuthoringSnapshot before = null;
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Author Main Menu Updates");
        var report = new StringBuilder();
        try
        {
            MainMenuUIController original = Components<MainMenuUIController>(scene).Single();
            string originalConfiguration = JsonUtility.ToJson(original);
            var originalData = new SerializedObject(original);
            RectTransform dropdown = originalData.FindProperty("dropdownPanel").objectReferenceValue as RectTransform;
            Check(dropdown != null, "Existing dropdown reference is missing.");
            Canvas menuCanvas = dropdown.GetComponentInParent<Canvas>(true).rootCanvas;
            Check(menuCanvas != null && menuCanvas.name == "MainMenuCanvas", "Expected the existing MainMenuCanvas.");
            var originalEvents = Components<Button>(scene).Where(button => button.name != EntryName &&
                button.GetComponentInParent<Canvas>(true)?.name != PanelName).ToDictionary(button => button, button => JsonUtility.ToJson(button.onClick));
            before = new SceneAuthoringSnapshot(scene, menuCanvas.gameObject,
                scene.GetRootGameObjects().SingleOrDefault(root => root.name == ManagerName), original, originalEvents);
            Undo.RegisterFullObjectHierarchyUndo(menuCanvas.gameObject, "Add Updates Controls");
            GameObject manager = scene.GetRootGameObjects().SingleOrDefault(root => root.name == ManagerName);
            if (manager == null)
            {
                manager = new GameObject(ManagerName);
                SceneManager.MoveGameObjectToScene(manager, scene);
                Undo.RegisterCreatedObjectUndo(manager, "Create Updates Manager");
            }
            else
            {
                Check(manager.GetComponent<MainMenuUpdatesController>() != null, "An unrelated Updates manager exists and was preserved.");
                Undo.RegisterFullObjectHierarchyUndo(manager, "Wire Updates Manager");
            }
            recordingUndo = true;
            manager.SetActive(true);
            MainMenuUpdatesController controller = Ensure<MainMenuUpdatesController>(manager);
            MainMenuUpdatesLayout layout = Ensure<MainMenuUpdatesLayout>(manager);
            Build(menuCanvas.transform, controller, layout);
            recordingUndo = false;
            Canvas.ForceUpdateCanvases();
            layout.ApplyLayout();
            Check(JsonUtility.ToJson(original) == originalConfiguration, "Existing Main Menu/authentication configuration changed.");
            foreach (var pair in originalEvents)
                Check(pair.Key != null && JsonUtility.ToJson(pair.Key.onClick) == pair.Value, "An existing button callback changed.");
            ValidateReferences(controller, layout, menuCanvas);
            Preview(controller, report);
            Undo.FlushUndoRecordObjects();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!alreadyDirty)
            {
                Check(EditorSceneManager.SaveScene(scene), "Could not save Main Menu Updates.");
                saved = true;
                ValidateSavedSource();
                report.Insert(0, "PASS: Main Menu Updates saved. Existing menu configuration and button callbacks were preserved.\n");
            }
            else report.Insert(0, "PENDING SAVE: Updates authored in the already-dirty Main Menu scene. Save together with your existing edits; no automatic save or discard occurred.\n");
            report.AppendLine("NOTE: Disposable preview only; no network requests, authentication, live callbacks, input injection, preferences or player saves ran.");
            succeeded = true;
            File.WriteAllText(Report, report.ToString());
            Debug.Log("[Updates UI] " + (alreadyDirty ? "Authored; save Main Menu with your existing edits." : "Saved; phone/tablet, scroll and click-priority checks passed."));
        }
        catch
        {
            recordingUndo = false;
            if (!succeeded && !saved)
            {
                Undo.RevertAllDownToGroup(undoGroup);
                if (before != null)
                {
                    // Undo restores objects but can leave an otherwise clean
                    // scene dirty. Only clear our dirty mark after verifying the
                    // exact pre-author roots, touched hierarchy and callbacks.
                    before.Verify(scene);
                    if (!alreadyDirty) ClearVerifiedAuthoringDirtiness(scene);
                }
            }
            throw;
        }
        finally
        {
            recordingUndo = false;
            if (opened && scene.IsValid() && scene.isLoaded && !scene.isDirty) EditorSceneManager.CloseScene(scene, true);
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
        }
    }

    private static void ClearVerifiedAuthoringDirtiness(Scene scene)
    {
        // Unity 2022 exposes this editor operation internally. Unlike reopening
        // a loaded scene, it preserves object references, selection and context.
        var clear = typeof(EditorSceneManager).GetMethod("ClearSceneDirtiness",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic,
            null, new[] { typeof(Scene) }, null);
        Check(clear != null, "Clean-state restoration is unavailable; the verified rollback was left loaded, never discarded.");
        clear.Invoke(null, new object[] { scene });
        Check(!scene.isDirty, "Could not restore the verified original clean flag; scene left loaded.");
    }

    private sealed class SceneAuthoringSnapshot
    {
        private readonly int[] roots;
        private readonly List<KeyValuePair<Object, string>> objects = new List<KeyValuePair<Object, string>>();
        private readonly MainMenuUIController menu;
        private readonly string menuConfiguration;
        private readonly Dictionary<Button, string> callbacks;

        public SceneAuthoringSnapshot(Scene scene, GameObject canvas, GameObject manager,
            MainMenuUIController menu, Dictionary<Button, string> callbacks)
        {
            roots = scene.GetRootGameObjects().Select(root => root.GetInstanceID()).ToArray();
            this.menu = menu; menuConfiguration = JsonUtility.ToJson(menu); this.callbacks = callbacks;
            Capture(canvas);
            if (manager != null) Capture(manager);
        }

        private void Capture(GameObject root)
        {
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                objects.Add(new KeyValuePair<Object, string>(transform.gameObject, EditorJsonUtility.ToJson(transform.gameObject)));
                foreach (Component component in transform.GetComponents<Component>())
                    if (component != null) objects.Add(new KeyValuePair<Object, string>(component, EditorJsonUtility.ToJson(component)));
            }
        }

        public void Verify(Scene scene)
        {
            Check(scene.IsValid() && scene.isLoaded && roots.SequenceEqual(scene.GetRootGameObjects().Select(root => root.GetInstanceID())),
                "Rollback root verification failed; scene left loaded and dirty, never saved or discarded.");
            Check(menu != null && JsonUtility.ToJson(menu) == menuConfiguration,
                "Rollback Main Menu configuration differs; scene left loaded and dirty.");
            foreach (var callback in callbacks)
                Check(callback.Key != null && JsonUtility.ToJson(callback.Key.onClick) == callback.Value,
                    "Rollback callback differs; scene left loaded and dirty.");
            foreach (var item in objects)
                Check(item.Key != null && EditorJsonUtility.ToJson(item.Key) == item.Value,
                    "Rollback object differs: " + (item.Key != null ? item.Key.name : "missing") + ". Scene left loaded and dirty.");
        }
    }

    [MenuItem("Tools/Civil Craft/Validate Main Menu Updates")]
    public static void Validate()
    {
        Check(!Busy, "Stay in Edit Mode and leave Prefab Mode before validation.");
        Scene previous = SceneManager.GetActiveScene();
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        var report = new StringBuilder();
        try
        {
            ValidateSavedSource();
            MainMenuUpdatesController controller = Components<MainMenuUpdatesController>(scene).Single();
            MainMenuUpdatesLayout layout = controller.GetComponent<MainMenuUpdatesLayout>();
            ValidateReferences(controller, layout, controller.updatesButton.GetComponentInParent<Canvas>(true).rootCanvas);
            Preview(controller, report);
            report.AppendLine("RESULT: PASS. No source-scene saves, network requests, preferences, input injection or player-data operations occurred.");
            File.WriteAllText(ValidationReport, report.ToString());
        }
        finally
        {
            if (opened && scene.IsValid() && scene.isLoaded && !scene.isDirty) EditorSceneManager.CloseScene(scene, true);
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
        }
    }

    private static IEnumerable<T> Components<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true));

    private static void Build(Transform parent, MainMenuUpdatesController controller, MainMenuUpdatesLayout layout)
    {
        Transform oldEntry = parent.Find(EntryName), oldPanel = parent.Find(PanelName);
        if (oldEntry != null) Check(oldEntry.GetComponent<Button>() != null && oldEntry.Find("NewUpdateBadge") != null, "An unrelated Updates button exists.");
        if (oldPanel != null) Check(oldPanel.Find("UpdatesCard") != null, "An unrelated Updates panel exists.");
        Button entry = Button(parent, EntryName, "UPDATES", Sand);
        entry.transform.SetAsLastSibling();
        Canvas entryCanvas = Ensure<Canvas>(entry.gameObject);
        Ensure<GraphicRaycaster>(entry.gameObject);
        SetCanvasOrder(entryCanvas, 10);
        Image badge = Surface(entry.transform, "NewUpdateBadge", Gold, false);
        TMP_Text badgeLabel = Label(badge.transform, "Badge Label", "NEW UPDATE", 22);
        Stretch(badgeLabel.rectTransform, 6, 2, 6, 2);
        badge.gameObject.SetActive(false);
        GameObject panel = Child(parent, PanelName);
        panel.SetActive(false);
        Stretch(panel.transform as RectTransform, 0, 0, 0, 0);
        panel.transform.SetAsLastSibling();
        Canvas panelCanvas = Ensure<Canvas>(panel);
        Ensure<GraphicRaycaster>(panel);
        Image dim = Ensure<Image>(panel);
        dim.sprite = null; dim.type = Image.Type.Simple; dim.color = new Color(.08f, .055f, .035f, .7f); dim.raycastTarget = true;
        CanvasGroup canvasGroup = Ensure<CanvasGroup>(panel);
        canvasGroup.alpha = 1; canvasGroup.interactable = canvasGroup.blocksRaycasts = true;
        Image card = Surface(panel.transform, "UpdatesCard", Ink, false);
        Image face = Surface(card.transform, "UpdatesSurface", Cream, false);
        TMP_Text heading = Label(face.transform, "UpdatesHeading", "GAME UPDATES", 38);
        TMP_Text title = Label(face.transform, "ReleaseTitle", "Release notes", 34);
        TMP_Text date = Label(face.transform, "ReleaseDate", "", 25);
        TMP_Text installed = Label(face.transform, "InstalledVersion", "Installed: checking...", 25);
        TMP_Text available = Label(face.transform, "AvailableVersion", "Available: checking...", 25);
        TMP_Text status = Label(face.transform, "UpdateStatus", "Checking for release notes...", 25);
        Button close = Button(face.transform, "UpdatesClose", "CLOSE", Sand);
        Button retry = Button(face.transform, "UpdatesRetry", "RETRY", Sand);
        Button download = Button(face.transform, "UpdatesDownload", "DOWNLOAD", Gold);
        download.interactable = false;
        Image viewport = Surface(face.transform, "NotesViewport", Sand, true);
        Ensure<RectMask2D>(viewport.gameObject);
        RectTransform content = Child(viewport.transform, "NotesContent").transform as RectTransform;
        content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(.5f, 1);
        content.anchoredPosition = content.sizeDelta = Vector2.zero;
        VerticalLayoutGroup group = Ensure<VerticalLayoutGroup>(content.gameObject);
        group.childControlWidth = group.childControlHeight = true;
        group.childForceExpandWidth = true; group.childForceExpandHeight = false;
        group.childAlignment = TextAnchor.UpperLeft;
        ContentSizeFitter fitter = Ensure<ContentSizeFitter>(content.gameObject);
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained; fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        TMP_Text notes = Label(content, "ReleaseNotes", "No release notes have been loaded yet.", 30);
        notes.alignment = TextAlignmentOptions.TopLeft; notes.enableWordWrapping = true;
        notes.richText = false; notes.overflowMode = TextOverflowModes.Overflow;
        Image scrollbarImage = Surface(face.transform, "NotesScrollbar", Sand, true);
        RectTransform sliding = Child(scrollbarImage.transform, "Sliding Area").transform as RectTransform;
        Stretch(sliding, 0, 0, 0, 0);
        Image scrollHandle = Surface(sliding, "Handle", Ink, true);
        // Scrollbar drives its handle anchors, not the default 100x100 offsets.
        // Start with zero offsets so the handle remains inside its narrow rail.
        Stretch(scrollHandle.rectTransform, 0, 0, 0, 0);
        Scrollbar scrollbar = Ensure<Scrollbar>(scrollbarImage.gameObject);
        scrollbar.handleRect = scrollHandle.rectTransform; scrollbar.targetGraphic = scrollHandle;
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };
        ScrollRect scroll = Ensure<ScrollRect>(viewport.gameObject);
        scroll.viewport = viewport.rectTransform; scroll.content = content;
        scroll.horizontal = false; scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.inertia = true; scroll.decelerationRate = .12f; scroll.scrollSensitivity = 42f;
        scroll.verticalScrollbar = scrollbar; scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        SetCanvasOrder(panelCanvas, 32000);
        controller.updatesButton = entry; controller.panel = panel; controller.badge = badge.gameObject;
        controller.closeButton = close; controller.retryButton = retry; controller.downloadButton = download;
        controller.releaseTitleText = title; controller.releaseDateText = date; controller.notesText = notes;
        controller.installedVersionText = installed; controller.availableVersionText = available; controller.statusText = status;
        controller.notesScroll = scroll;
        WireLayout(layout, entry.gameObject, panel);
        layout.authoredVersion = 1;
        EditorUtility.SetDirty(controller); EditorUtility.SetDirty(layout);
    }

    private static void WireLayout(MainMenuUpdatesLayout layout, GameObject entry, GameObject panel)
    {
        layout.entryButton = entry.transform as RectTransform;
        layout.badgeRect = entry.transform.Find("NewUpdateBadge") as RectTransform;
        layout.panelRect = panel.transform as RectTransform;
        layout.card = Find(panel.transform, "UpdatesCard"); layout.surface = Find(panel.transform, "UpdatesSurface");
        layout.heading = Find(panel.transform, "UpdatesHeading"); layout.releaseTitle = Find(panel.transform, "ReleaseTitle");
        layout.releaseDate = Find(panel.transform, "ReleaseDate"); layout.notesViewport = Find(panel.transform, "NotesViewport");
        layout.notesContent = Find(panel.transform, "NotesContent"); layout.scrollbar = Find(panel.transform, "NotesScrollbar");
        layout.installedVersion = Find(panel.transform, "InstalledVersion"); layout.availableVersion = Find(panel.transform, "AvailableVersion");
        layout.status = Find(panel.transform, "UpdateStatus"); layout.retry = Find(panel.transform, "UpdatesRetry");
        layout.download = Find(panel.transform, "UpdatesDownload"); layout.close = Find(panel.transform, "UpdatesClose");
        layout.notesScroll = layout.notesViewport.GetComponent<ScrollRect>();
        layout.notesText = Find(panel.transform, "ReleaseNotes").GetComponent<TMP_Text>();
    }

    private static void ValidateReferences(MainMenuUpdatesController controller, MainMenuUpdatesLayout layout, Canvas canvas)
    {
        Check(controller != null && layout != null && controller.transform.parent == null && controller.gameObject.activeSelf,
            "Updates controller must live on its always-active scene-root manager.");
        Check(controller.panel != null && !controller.panel.activeSelf && controller.badge != null && !controller.badge.activeSelf,
            "Authored modal and badge must start hidden.");
        Check(controller.updatesButton != null && controller.updatesButton.gameObject.activeSelf &&
            controller.updatesButton.transform.parent == canvas.transform && controller.panel.transform.parent == canvas.transform,
            "Updates button and panel must be Canvas children outside the hidden dropdown/authentication branches.");
        Check(controller.panel.GetComponent<CanvasScaler>() == null && controller.updatesButton.GetComponent<CanvasScaler>() == null,
            "Updates must inherit the original CanvasScaler.");
        Check(controller.notesText != null && !controller.notesText.richText && controller.notesScroll != null &&
            controller.notesScroll.vertical && !controller.notesScroll.horizontal && controller.notesScroll.content == layout.notesContent &&
            controller.notesScroll.viewport == layout.notesViewport && layout.notesViewport.GetComponent<RectMask2D>() != null,
            "Plain-text scrollable notes wiring is incomplete.");
        Check(controller.panel.GetComponent<Image>().raycastTarget && controller.panel.GetComponent<CanvasGroup>().blocksRaycasts,
            "Updates modal must block clicks through to the menu.");
        foreach (Button button in new[] { controller.updatesButton, controller.closeButton, controller.retryButton, controller.downloadButton })
            Check(button != null && button.onClick.GetPersistentEventCount() == 0, "Updates button has stale persistent callbacks.");
        foreach (TMP_Text text in controller.panel.GetComponentsInChildren<TMP_Text>(true))
            Check(text.font != null && text.font.name == "Bekind Sans SDF" && !text.richText && !text.raycastTarget,
                "Updates must use existing Bekind typography and plain-text, nonblocking labels: " + text.name);
        foreach (Graphic graphic in controller.badge.GetComponentsInChildren<Graphic>(true))
            Check(!graphic.raycastTarget, "The NEW UPDATE badge must not steal button clicks.");
        var entryCanvas = new SerializedObject(controller.updatesButton.GetComponent<Canvas>());
        var panelCanvas = new SerializedObject(controller.panel.GetComponent<Canvas>());
        Check(entryCanvas.FindProperty("m_OverrideSorting").boolValue && entryCanvas.FindProperty("m_SortingOrder").intValue == 10 &&
            panelCanvas.FindProperty("m_OverrideSorting").boolValue && panelCanvas.FindProperty("m_SortingOrder").intValue == 32000,
            "Updates click priority or modal sorting was not serialized.");
    }

    private static void ValidateSavedSource()
    {
        string source = File.ReadAllText(ScenePath);
        foreach (string name in new[] { ManagerName, EntryName, PanelName, "NewUpdateBadge", "ReleaseNotes" })
            Check(System.Text.RegularExpressions.Regex.Matches(source, @"(?m)^  m_Name: " + name + @"\r?$").Count == 1,
                "Saved Main Menu must contain exactly one " + name + ".");
        Check(source.Contains("guid: 8ecf31d6e8f7468b9eaa8b75ce648c8e"), "Saved Updates layout is missing.");
    }

    private static void Preview(MainMenuUpdatesController source, StringBuilder report)
    {
        Scene preview = EditorSceneManager.NewPreviewScene();
        RenderTexture target = null;
        try
        {
            GameObject canvasObject = new GameObject("Detached Updates Preview", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            canvasObject.SetActive(false); SceneManager.MoveGameObjectToScene(canvasObject, preview);
            Canvas canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            // A detached full-screen hit target represents the existing Click to
            // Play control; no source button or live callback is invoked.
            GameObject playFixture = Child(canvasObject.transform, "Detached Click To Play Hit Target");
            Image playImage = playFixture.AddComponent<Image>();
            playImage.color = new Color(1, 1, 1, 0); playImage.raycastTarget = true;
            Stretch(playFixture.transform as RectTransform, 0, 0, 0, 0);
            GameObject entry = Object.Instantiate(source.updatesButton.gameObject, canvasObject.transform, false);
            GameObject panel = Object.Instantiate(source.panel, canvasObject.transform, false);
            foreach (MonoBehaviour script in canvasObject.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (script == null) continue;
                string ns = script.GetType().Namespace;
                if (ns != "UnityEngine.UI" && ns != "TMPro") script.enabled = false;
            }
            foreach (Button button in canvasObject.GetComponentsInChildren<Button>(true)) button.onClick = new Button.ButtonClickedEvent();
            GameObject manager = new GameObject("Detached Updates Layout"); SceneManager.MoveGameObjectToScene(manager, preview);
            manager.SetActive(false);
            MainMenuUpdatesLayout layout = manager.AddComponent<MainMenuUpdatesLayout>(); layout.enabled = false;
            WireLayout(layout, entry, panel);
            foreach (Transform child in canvasObject.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 30;
            GameObject cameraObject = new GameObject("Detached Updates Camera", typeof(Camera)); SceneManager.MoveGameObjectToScene(cameraObject, preview);
            Camera camera = cameraObject.GetComponent<Camera>(); camera.scene = preview; camera.enabled = false;
            camera.orthographic = true; camera.transform.position = new Vector3(0, 0, -1000);
            camera.nearClipPlane = .1f; camera.farClipPlane = 2000; camera.cullingMask = 1 << 30;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(142, 114, 83, 255);
            canvas.worldCamera = camera;
            foreach (Canvas nested in canvasObject.GetComponentsInChildren<Canvas>(true)) nested.worldCamera = camera;
            Vector2Int[] sizes = { new Vector2Int(1280, 720), new Vector2Int(1024, 768) };
            string[] names = { "phone", "tablet" };
            for (int i = 0; i < sizes.Length; i++)
            {
                Vector2Int size = sizes[i]; target = new RenderTexture(size.x, size.y, 24); target.Create();
                camera.targetTexture = target; camera.aspect = (float)size.x / size.y; camera.orthographicSize = size.y * .5f;
                canvasObject.GetComponent<RectTransform>().sizeDelta = size;
                canvasObject.SetActive(true); panel.SetActive(false); entry.SetActive(true);
                layout.badgeRect.gameObject.SetActive(true);
                Canvas.ForceUpdateCanvases(); layout.ApplyLayout(); Canvas.ForceUpdateCanvases();
                Inside(layout.entryButton, canvasObject.transform as RectTransform, "Updates entry leaves the safe menu bounds.");
                Check(layout.entryButton.localPosition.x > 0 && layout.entryButton.localPosition.y < 0, "Updates entry is not bottom-right.");
                Capture(camera, target, "Temp/main-menu-updates-" + names[i] + "-entry.png");
                panel.SetActive(true);
                Find(panel.transform, "ReleaseTitle").GetComponent<TMP_Text>().text = "A new bridge-building adventure";
                Find(panel.transform, "ReleaseDate").GetComponent<TMP_Text>().text = "Published 14 October 2026";
                Find(panel.transform, "InstalledVersion").GetComponent<TMP_Text>().text = "Installed: 1.0 (100)";
                Find(panel.transform, "AvailableVersion").GetComponent<TMP_Text>().text = "Available: 1.1 (101)";
                Find(panel.transform, "UpdateStatus").GetComponent<TMP_Text>().text = "A newer Android build is available. Download opens the website in your browser.";
                layout.notesText.text = string.Join("\n\n", Enumerable.Repeat("Improved bridge tutorials and controls. Build, explore, and learn with Civil Craft. <b>Markup remains plain text.</b>", 12));
                layout.ApplyLayout(); Canvas.ForceUpdateCanvases(); layout.RefreshNotesLayout(); Canvas.ForceUpdateCanvases();
                foreach (TMP_Text text in panel.GetComponentsInChildren<TMP_Text>()) text.ForceMeshUpdate();
                Inside(layout.card, canvasObject.transform as RectTransform, "Updates card leaves the preview safe bounds.");
                foreach (RectTransform rect in new[] { layout.heading, layout.releaseTitle, layout.releaseDate, layout.installedVersion,
                    layout.availableVersion, layout.notesViewport, layout.status, layout.close, layout.retry, layout.download })
                    Inside(rect, layout.surface, "Updates control leaves the card: " + rect.name);
                Check(layout.notesContent.rect.height > layout.notesViewport.rect.height, "Long release notes are not scrollable.");
                Check(!layout.notesText.richText && layout.notesText.text.Contains("<b>"), "Remote markup was interpreted as rich text.");
                Check(!Bounds(layout.retry, layout.surface).Overlaps(Bounds(layout.download, layout.surface)), "Updates action buttons overlap.");
                Check(!Bounds(layout.notesViewport, layout.surface).Overlaps(Bounds(layout.status, layout.surface)), "Release notes overlap the status.");
                Check(!Bounds(layout.heading, layout.surface).Overlaps(Bounds(layout.close, layout.surface)), "Close button overlaps the panel heading.");
                RectTransform scrollHandle = layout.scrollbar.GetComponent<Scrollbar>().handleRect;
                Inside(scrollHandle, layout.scrollbar, "Scrollbar handle leaves its narrow rail.");
                Check(!Bounds(scrollHandle, layout.surface).Overlaps(Bounds(layout.availableVersion, layout.surface)) &&
                    !Bounds(scrollHandle, layout.surface).Overlaps(Bounds(layout.notesViewport, layout.surface)),
                    "Scrollbar handle covers the version text or release notes.");
                ValidateClickPriority(canvasObject, entry, panel, camera, preview);
                Capture(camera, target, "Temp/main-menu-updates-" + names[i] + ".png");
                report.AppendLine("PASS: " + names[i] + " " + size.x + "x" + size.y + " authored Updates entry/card fit; long plain-text notes scroll; actions do not overlap; entry/modal clicks take priority over the menu.");
                camera.targetTexture = null; Release(target); target = null;
            }
        }
        finally { Release(target); EditorSceneManager.ClosePreviewScene(preview); }
    }

    private static void ValidateClickPriority(GameObject canvasObject, GameObject entry, GameObject panel, Camera camera, Scene preview)
    {
        GameObject eventObject = new GameObject("Detached Updates Event System");
        eventObject.SetActive(false);
        EventSystem events = eventObject.AddComponent<EventSystem>();
        SceneManager.MoveGameObjectToScene(eventObject, preview);
        var data = new PointerEventData(events);
        var results = new List<RaycastResult>();
        try
        {
            panel.SetActive(false); PrepareNativeGraphics(canvasObject, camera);
            data.position = camera.WorldToScreenPoint(entry.transform.position);
            entry.GetComponent<GraphicRaycaster>().Raycast(data, results);
            Check(results.Any(hit => hit.gameObject == entry), "Updates entry graphic does not receive its own click.");
            Check(results.Where(hit => hit.gameObject != null).All(hit => hit.gameObject.transform.IsChildOf(entry.transform)),
                "A decorative Updates graphic captures clicks outside the button.");
            canvasObject.GetComponent<GraphicRaycaster>().Raycast(data, results);
            RaycastResult menuHit = results.Single(hit => hit.gameObject != null && hit.gameObject.name == "Detached Click To Play Hit Target");
            RaycastResult entryHit = results.Single(hit => hit.gameObject == entry);
            Check(entryHit.sortingOrder > menuHit.sortingOrder, "Click to Play wins the Updates button hit order.");
            panel.SetActive(true); PrepareNativeGraphics(canvasObject, camera); results.Clear();
            data.position = new Vector2(camera.pixelWidth * .05f, camera.pixelHeight * .05f);
            panel.GetComponent<GraphicRaycaster>().Raycast(data, results);
            Check(results.Any(hit => hit.gameObject == panel), "Updates modal dim layer does not block an empty-space menu click. " +
                NativeGeometry(panel, camera, data.position, results));
            data.position = camera.WorldToScreenPoint(entry.transform.position); results.Clear();
            panel.GetComponent<GraphicRaycaster>().Raycast(data, results);
            entry.GetComponent<GraphicRaycaster>().Raycast(data, results);
            canvasObject.GetComponent<GraphicRaycaster>().Raycast(data, results);
            RaycastResult first = results.OrderByDescending(hit => hit.sortingOrder).ThenByDescending(hit => hit.depth).First();
            Check(first.gameObject.transform.IsChildOf(panel.transform), "An open Updates modal lets a menu click through. " +
                NativeGeometry(panel, camera, data.position, results));
        }
        finally { Object.DestroyImmediate(eventObject); }
    }

    private static void PrepareNativeGraphics(GameObject root, Camera camera)
    {
        Canvas.ForceUpdateCanvases();
        foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>())
        {
            graphic.SetAllDirty();
            graphic.Rebuild(CanvasUpdate.PreRender);
        }
        Canvas.ForceUpdateCanvases();
        // Detached preview scenes have no camera loop. A newly enabled Canvas
        // can have absoluteDepth=-1 until its first native render, and the real
        // GraphicRaycaster correctly skips it. Populate the detached batches
        // after each visibility change without rendering the source/Game view.
        RenderDetached(camera, camera.targetTexture);
        Canvas.ForceUpdateCanvases();
    }

    private static string NativeGeometry(GameObject panel, Camera camera, Vector2 point, IEnumerable<RaycastResult> results)
    {
        Image dim = panel.GetComponent<Image>();
        Vector3[] corners = new Vector3[4]; dim.rectTransform.GetWorldCorners(corners);
        return "camera=" + camera.pixelRect + "; point=" + point + "; panelRect=" + dim.rectTransform.rect +
            "; corner0=" + corners[0] + "; corner2=" + corners[2] + "; active=" + dim.isActiveAndEnabled +
            "; depth=" + dim.depth + "; culled=" + dim.canvasRenderer.cull + "; target=" + dim.raycastTarget +
            "; contains=" + RectTransformUtility.RectangleContainsScreenPoint(dim.rectTransform, point, camera) +
            "; registered=" + GraphicRegistry.GetRaycastableGraphicsForCanvas(dim.canvas).Contains(dim) +
            "; hits=" + string.Join(", ", results.Select(hit => hit.gameObject.name + "/order=" + hit.sortingOrder + "/depth=" + hit.depth));
    }

    private static void Capture(Camera camera, RenderTexture target, string path)
    {
        RenderDetached(camera, target);
        RenderTexture previous = RenderTexture.active;
        Texture2D image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        try
        {
            RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
    }
    private static void RenderDetached(Camera camera, RenderTexture target)
    {
        Check(target != null, "Detached Updates preview render target is missing.");
        if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset)
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        else camera.Render();
    }
    private static void Release(RenderTexture target) { if (target != null) { target.Release(); Object.DestroyImmediate(target); } }
    private static Rect Bounds(RectTransform target, RectTransform parent)
    {
        Vector3[] corners = new Vector3[4]; target.GetWorldCorners(corners);
        Vector2 min = Vector2.positiveInfinity, max = Vector2.negativeInfinity;
        foreach (Vector3 corner in corners) { Vector2 point = parent.InverseTransformPoint(corner); min = Vector2.Min(min, point); max = Vector2.Max(max, point); }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }
    private static void Inside(RectTransform target, RectTransform parent, string message)
    {
        Check(target != null && parent != null, message);
        Rect bounds = Bounds(target, parent), area = parent.rect;
        Check(bounds.xMin >= area.xMin - .5f && bounds.yMin >= area.yMin - .5f &&
            bounds.xMax <= area.xMax + .5f && bounds.yMax <= area.yMax + .5f, message);
    }
    private static RectTransform Find(Transform root, string name) => root.GetComponentsInChildren<RectTransform>(true).Single(rect => rect.name == name);
    private static GameObject Child(Transform parent, string name)
    {
        Transform existing = parent.Find(name);
        if (existing != null) return existing.gameObject;
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer)); obj.layer = parent.gameObject.layer;
        SceneManager.MoveGameObjectToScene(obj, parent.gameObject.scene); obj.transform.SetParent(parent, false);
        if (recordingUndo) Undo.RegisterCreatedObjectUndo(obj, "Create Updates Control");
        return obj;
    }
    private static T Ensure<T>(GameObject obj) where T : Component
    {
        T component = obj.GetComponent<T>();
        return component != null ? component : recordingUndo ? Undo.AddComponent<T>(obj) : obj.AddComponent<T>();
    }
    private static Image Surface(Transform parent, string name, Color color, bool raycast)
    {
        Image image = Ensure<Image>(Child(parent, name)); image.sprite = rounded; image.type = Image.Type.Sliced;
        image.color = color; image.raycastTarget = raycast; return image;
    }
    private static TMP_Text Label(Transform parent, string name, string value, float size)
    {
        TMP_Text text = Ensure<TextMeshProUGUI>(Child(parent, name)); text.font = font; text.fontSharedMaterial = font.material;
        text.text = value; text.fontSize = size; text.color = Ink; text.richText = false; text.raycastTarget = false;
        text.enableAutoSizing = false; text.alignment = TextAlignmentOptions.Center; text.enableWordWrapping = false;
        text.margin = Vector4.zero; text.overflowMode = TextOverflowModes.Ellipsis; return text;
    }
    private static Button Button(Transform parent, string name, string value, Color color)
    {
        GameObject obj = Child(parent, name);
        Image frame = Ensure<Image>(obj); frame.sprite = rounded; frame.type = Image.Type.Sliced; frame.color = Ink; frame.raycastTarget = true;
        Image face = Surface(obj.transform, "Button Face", color, false); Stretch(face.rectTransform, 4, 4, 4, 4); face.transform.SetAsFirstSibling();
        Button button = Ensure<Button>(obj); button.targetGraphic = face; button.onClick = new Button.ButtonClickedEvent();
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        ColorBlock colors = button.colors; colors.normalColor = Color.white; colors.highlightedColor = colors.selectedColor = new Color(1f, .95f, .82f);
        colors.pressedColor = new Color(.85f, .76f, .61f); button.colors = colors;
        TMP_Text label = Label(obj.transform, "Button Label", value, 30); Stretch(label.rectTransform, 8, 4, 8, 4); label.transform.SetAsLastSibling();
        return button;
    }
    private static void SetCanvasOrder(Canvas canvas, int order)
    {
        var data = new SerializedObject(canvas); data.FindProperty("m_OverrideSorting").boolValue = true;
        data.FindProperty("m_SortingOrder").intValue = order; data.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(canvas);
    }
    private static void Stretch(RectTransform rect, float left, float bottom, float right, float top)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.pivot = Vector2.one * .5f;
        rect.offsetMin = new Vector2(left, bottom); rect.offsetMax = new Vector2(-right, -top); rect.localScale = Vector3.one;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
#endif
