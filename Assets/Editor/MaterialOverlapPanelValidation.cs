#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Opt-in, detached checks. Does not enter Play Mode or save a user scene.</summary>
public static class MaterialOverlapPanelValidation
{
    private const string PrefabPath = "Assets/Prefabs/BuildingMode/MANAGERS AND CANVASES.prefab";
    private const string Request = "Temp/material-overlap-panel-validation.request";
    private const string Report = "Logs/material-overlap-panel-validation.txt";
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static int checks;
    private static double nextPoll;

    [InitializeOnLoadMethod]
    private static void Watch() { EditorApplication.update -= Poll; EditorApplication.update += Poll; }

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll || EditorApplication.isCompiling ||
            EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        nextPoll = EditorApplication.timeSinceStartup + 2d;
        if (!File.Exists(Request)) return;
        File.Delete(Request);
        RunBatch();
    }

    [MenuItem("Tools/Civil Craft/Build/Validate Material Overlap Panel")]
    public static void RunBatch()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        checks = 0;
        var report = new StringBuilder();
        var snapshots = new List<SceneSnapshot>();
        for (int i = 0; i < SceneManager.sceneCount; i++) snapshots.Add(new SceneSnapshot(SceneManager.GetSceneAt(i)));
        Scene originalActive = SceneManager.GetActiveScene();
        Point[] originalPoints = Point.AllPoints.ToArray();
        var singletons = new Dictionary<Type, object>();
        foreach (Type type in new[] { typeof(GameManager), typeof(ClipboardManager), typeof(CommandManager),
                     typeof(BuildUIController), typeof(BuildTutorialDirector), typeof(UIPanelCoordinator), typeof(AudioManager), typeof(PauseManager) })
        {
            singletons.Add(type, Static(type, "Instance"));
            SetStatic(type, "Instance", null);
        }
        GameObject contents = null;
        Scene preview = default;
        var transientAssets = new List<UnityEngine.Object>();
        bool passed = false;
        try
        {
            contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            ClipboardManager authored = contents.GetComponentInChildren<ClipboardManager>(true);
            report.AppendLine(ValidateAuthored(authored));
            preview = EditorSceneManager.NewPreviewScene();
            ValidateFlow(preview, transientAssets, report);
            ValidatePreservedBuildBackground(preview, report);
            ValidateRendering(preview, authored.overrideConfirmPanel, report);
            passed = true;
        }
        catch (Exception error)
        {
            report.AppendLine("FAIL: " + error);
            Debug.LogError("[MaterialOverlapPanelValidation] " + error);
        }
        finally
        {
            if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
            if (contents != null) PrefabUtility.UnloadPrefabContents(contents);
            foreach (UnityEngine.Object asset in transientAssets) if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
            foreach (var singleton in singletons) SetStatic(singleton.Key, "Instance", singleton.Value);
            Point.AllPoints.Clear(); Point.AllPoints.AddRange(originalPoints);
            bool intact = SceneManager.GetActiveScene() == originalActive;
            foreach (SceneSnapshot snapshot in snapshots) intact &= snapshot.IsUnchanged();
            passed &= intact;
            report.AppendLine(intact ? "PASS: open scene roots, active scene and dirty flags preserved; no player data or backend used."
                                    : "FAIL: open scene state changed.");
            report.AppendLine((passed ? "PASS" : "FAIL") + ": " + checks + " checks completed.");
            Directory.CreateDirectory("Logs"); File.WriteAllText(Report, report.ToString());
            if (passed) Debug.Log("[MaterialOverlapPanelValidation] " + checks + " checks passed. " + Report);
        }
    }

    public static string ValidateAuthored(ClipboardManager clipboard)
    {
        Check(clipboard != null, "Authored ClipboardManager exists");
        GameObject panel = clipboard.overrideConfirmPanel;
        Check(panel != null && panel.transform is RectTransform, "Existing authored overlap panel is assigned");
        Check(!panel.activeSelf, "Authored overlap panel starts closed");
        Check(clipboard.dontShowAgainToggle != null && clipboard.dontShowAgainToggle.transform.IsChildOf(panel.transform),
            "Skip confirmation toggle belongs to this panel");
        MaterialOverlapPanelLayout layout = panel.GetComponent<MaterialOverlapPanelLayout>();
        Check(layout != null && new SerializedObject(layout).FindProperty("closeButton") == null,
            "Overlap panel layout no longer contains an X/close-button reference");
        Check(layout.toggleLabel != null && layout.toggleLabel.text == "Don't ask again", "Checkbox uses the exact requested label");
        Button[] buttons = panel.GetComponentsInChildren<Button>(true);
        Check(buttons.Length == 2, "Overlap panel contains exactly its two authored actions, without an extra X");
        int confirm = 0, cancel = 0;
        foreach (Button button in buttons)
        {
            for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
            {
                string method = button.onClick.GetPersistentMethodName(i);
                if (method != nameof(ClipboardManager.ConfirmOverride) && method != nameof(ClipboardManager.CancelOverride)) continue;
                Check(button.onClick.GetPersistentTarget(i) == clipboard, method + " targets its own ClipboardManager");
                Check(button.onClick.GetPersistentListenerState(i) != UnityEventCallState.Off, method + " is enabled");
                if (method == nameof(ClipboardManager.ConfirmOverride)) confirm++; else cancel++;
            }
        }
        Check(confirm == 1, "One persistent replacement action is wired");
        Check(cancel == 1, "One persistent cancel action is wired");
        Image backdrop = panel.GetComponent<Image>();
        Check(backdrop != null && backdrop.raycastTarget, "Panel has a raycast-blocking backdrop");
        RectTransform rect = (RectTransform)panel.transform;
        Check(rect.anchorMin == Vector2.zero && rect.anchorMax == Vector2.one, "Backdrop fills its parent");
        foreach (TMP_Text label in panel.GetComponentsInChildren<TMP_Text>(true))
            Check(label.font != null, "Authored text has an assigned font: " + label.name);
        return "PASS: authored material-overlap panel, button targets, toggle, fonts and blocking backdrop.";
    }

    private static void ValidatePreservedBuildBackground(Scene scene, StringBuilder report)
    {
        // FindObjectsOfType intentionally excludes PreviewScene/HideAndDontSave
        // canvases. Use a disposable additive scene for this discovery check.
        Scene previousActive = SceneManager.GetActiveScene();
        Scene discoveryScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        scene = discoveryScene;
        var coordinator = New(scene, "Detached panel coordinator", false).AddComponent<UIPanelCoordinator>();
        SetStatic(typeof(UIPanelCoordinator), "Instance", coordinator);
        var blueprint = New(scene, "Fixture build blueprint", true, typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
        Canvas blueprintCanvas = blueprint.GetComponent<Canvas>();
        blueprintCanvas.renderMode = RenderMode.ScreenSpaceCamera;
        var alreadyDisabled = New(scene, "Fixture initially disabled canvas", true, typeof(RectTransform), typeof(Canvas));
        Canvas disabledCanvas = alreadyDisabled.GetComponent<Canvas>(); disabledCanvas.enabled = false;
        var hudCanvasObject = New(scene, "Fixture screen HUD canvas", true, typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
        Canvas hudCanvas = hudCanvasObject.GetComponent<Canvas>();
        hudCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var modalCanvasObject = New(scene, "Fixture modal canvas", true, typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
        foreach (GameObject canvasObject in new[] { blueprint, alreadyDisabled, hudCanvasObject, modalCanvasObject })
            canvasObject.hideFlags = HideFlags.None;
        var hud = New(scene, "Fixture visible HUD", true, typeof(RectTransform), typeof(Image));
        var hiddenHud = New(scene, "Fixture already hidden HUD", false, typeof(RectTransform), typeof(Image));
        var modal = New(scene, "Fixture overlap modal", false, typeof(RectTransform), typeof(Image));
        var settings = New(scene, "Fixture nested Settings", false, typeof(RectTransform), typeof(Image));
        foreach (GameObject item in new[] { hud, hiddenHud }) item.transform.SetParent(hudCanvasObject.transform, false);
        foreach (GameObject item in new[] { modal, settings }) item.transform.SetParent(modalCanvasObject.transform, false);
        coordinator.hudObjects.Add(hud); coordinator.hudObjects.Add(hiddenHud);
        coordinator.managedPanels.Add(modal); coordinator.managedPanels.Add(settings);
        try
        {
            MethodInfo preserveOpen = typeof(UIPanelCoordinator).GetMethod("OpenPanel", Members, null,
                new[] { typeof(GameObject), typeof(bool), typeof(Canvas) }, null);
            Check(preserveOpen != null, "Coordinator has an explicit single-background-canvas modal overload");
            preserveOpen.Invoke(coordinator, new object[] { modal, true, blueprintCanvas });
            Check(coordinator.IsOpen(modal) && modal.activeInHierarchy, "Build confirmation opens as the coordinated top modal");
            Check(blueprint.activeSelf && blueprintCanvas.enabled, "Explicitly preserved build blueprint stays visible beneath confirmation");
            Check(hudCanvasObject.activeSelf && !hudCanvas.enabled && !hud.activeSelf,
                "Unrelated screen HUD retains normal exclusive-modal hiding behavior");
            Check(!hiddenHud.activeSelf && alreadyDisabled.activeSelf && !disabledCanvas.enabled,
                "Preserving a blueprint does not enable previously hidden HUD or disabled canvases");

            coordinator.OpenPanel(settings);
            Check(coordinator.IsOpen(settings) && settings.activeInHierarchy && !modal.activeSelf,
                "Nested Settings uses its ordinary exclusive-modal behavior");
            Check(!blueprintCanvas.enabled && !hud.activeSelf,
                "Normal nested Settings still hides the blueprint canvas and regular HUD");
            Check(!hiddenHud.activeSelf && !disabledCanvas.enabled, "Nested Settings preserves already-disabled states");
            coordinator.ClosePanel(settings);
            Check(coordinator.IsOpen(modal) && modal.activeInHierarchy && !settings.activeSelf,
                "Closing Settings restores the pending confirmation");
            Check(blueprintCanvas.enabled && blueprint.activeSelf && !hudCanvas.enabled && !hud.activeSelf,
                "Returning from Settings restores the blueprint but keeps unrelated HUD hidden behind confirmation");
            Check(!hiddenHud.activeSelf && !disabledCanvas.enabled, "Returning from Settings keeps initially disabled states disabled");
            coordinator.ClosePanel(modal);
            Check(!coordinator.HasOpenPanel && !modal.activeSelf && !settings.activeSelf,
                "Closing build confirmation clears its modal stack");
            Check(blueprint.activeSelf && blueprintCanvas.enabled && hudCanvasObject.activeSelf && hudCanvas.enabled && hud.activeSelf &&
                !hiddenHud.activeSelf && alreadyDisabled.activeSelf && !disabledCanvas.enabled,
                "Closing confirmation restores the exact original blueprint, canvas and HUD states");
            blueprintCanvas.enabled = false;
            preserveOpen.Invoke(coordinator, new object[] { modal, true, blueprintCanvas });
            Check(!blueprintCanvas.enabled, "An initially disabled preserved blueprint canvas is not force-enabled");
            coordinator.ClosePanel(modal);
            Check(!blueprintCanvas.enabled && hudCanvas.enabled && hud.activeSelf,
                "Closing confirmation preserves the blueprint's initially disabled state and restores normal HUD");
            report.AppendLine("PASS: confirmation preserves only its explicit build blueprint canvas; other HUD and nested Settings retain exclusive behavior, and prior enabled/disabled states restore exactly.");
        }
        finally
        {
            coordinator.CloseAllPanels();
            SetStatic(typeof(UIPanelCoordinator), "Instance", null);
            foreach (GameObject item in new[] { blueprint, alreadyDisabled, hudCanvasObject, modalCanvasObject })
                if (item != null) UnityEngine.Object.DestroyImmediate(item);
            UnityEngine.Object.DestroyImmediate(coordinator.gameObject);
            EditorSceneManager.CloseScene(discoveryScene, true);
            if (previousActive.IsValid() && previousActive.isLoaded) SceneManager.SetActiveScene(previousActive);
        }
    }

    private static void ValidateFlow(Scene scene, List<UnityEngine.Object> assets, StringBuilder report)
    {
        Point.AllPoints.Clear();
        var manager = New(scene, "Detached game manager", false).AddComponent<GameManager>();
        manager.OnEnterBuildMode = new UnityEvent(); manager.OnExitBuildMode = new UnityEvent();
        var location = New(scene, "Detached build location", false).AddComponent<BuildLocation>();
        SetStatic(typeof(GameManager), "Instance", manager);
        SetProperty(manager, "CurrentState", GameManager.GameState.Building);
        SetProperty(manager, "ActiveBuildLocation", location);
        var controls = New(scene, "Detached builder and clipboard", false);
        var creator = controls.AddComponent<BarCreator>();
        var clipboard = controls.AddComponent<ClipboardManager>();
        Set(clipboard, "barCreator", creator);
        SetStatic(typeof(ClipboardManager), "Instance", clipboard);
        var command = New(scene, "Detached history", false).AddComponent<CommandManager>();
        SetStatic(typeof(CommandManager), "Instance", command);
        creator.pointParent = New(scene, "Fixture points", true).transform;
        creator.barParent = New(scene, "Fixture bars", true).transform;
        creator.pointToInstantiate = New(scene, "Point template (unused)", false);
        creator.pointToInstantiate.AddComponent<Point>();
        creator.barToInstantiate = New(scene, "Bar template", true);
        creator.barToInstantiate.AddComponent<Bar>();
        clipboard.overrideConfirmPanel = New(scene, "Detached confirmation", false, typeof(RectTransform), typeof(Image));
        var toggle = New(scene, "Detached skip toggle", true, typeof(RectTransform), typeof(Toggle));
        toggle.transform.SetParent(clipboard.overrideConfirmPanel.transform, false);
        clipboard.dontShowAgainToggle = toggle.GetComponent<Toggle>();

        BridgeMaterialSO oldMaterial = ScriptableObject.CreateInstance<BridgeMaterialSO>();
        BridgeMaterialSO newMaterial = ScriptableObject.CreateInstance<BridgeMaterialSO>();
        oldMaterial.name = "Fixture wood"; newMaterial.name = "Fixture steel";
        oldMaterial.costPerMeter = 1; newMaterial.costPerMeter = 2;
        oldMaterial.maxLength = newMaterial.maxLength = 20;
        assets.Add(oldMaterial); assets.Add(newMaterial);
        Point start = NewPoint(scene, creator.pointParent, "Existing A", new Vector3(0, 5, 0), location);
        Point end = NewPoint(scene, creator.pointParent, "Existing B", new Vector3(4, 5, 0), location);
        Bar existing = New(scene, "Existing material", true).AddComponent<Bar>();
        existing.transform.SetParent(creator.barParent, false);
        existing.materialData = oldMaterial; existing.startPoint = start; existing.endPoint = end;
        existing.StartPosition = start.transform.position; existing.EndPosition = end.transform.position; existing.currentLength = 4f;
        start.ConnectedBars.Add(existing); end.ConnectedBars.Add(existing);
        existing.AssignOwner(location);
        var ghostA = New(scene, "Ghost A", true); var ghostB = New(scene, "Ghost B", true);
        Bar ghostBar = New(scene, "Ghost bar", true).AddComponent<Bar>();
        ghostBar.MarkAsConstructionPreview(); ghostBar.materialData = newMaterial;
        var ghosts = new List<GameObject> { ghostA, ghostB };
        Set(clipboard, "copiedRelativePoints", new List<Vector3> { Vector3.zero, Vector3.right * 4 });
        Set(clipboard, "copiedBars", new List<CopiedBarInfo> { new CopiedBarInfo { startIdx = 0, endIdx = 1, mat = newMaterial } });
        Set(clipboard, "ghostPastePoints", ghosts);
        Set(clipboard, "ghostPasteBars", new List<Bar> { ghostBar });
        clipboard.isPasteMode = true;
        Invoke(clipboard, "UpdatePasteGhostsWorldPosition", start.transform.position);
        Check(Field<bool>(clipboard, "isValidPaste"), "Valid overlap preview is allowed");
        clipboard.StampPaste();
        Check(Pending(clipboard) && clipboard.overrideConfirmPanel.activeSelf, "Overlap opens confirmation");
        Check(existing.gameObject.activeSelf && UndoStack(command).Count == 0, "Opening panel changes neither bridge nor history");
        Vector3 heldRoot = Field<Vector3>(clipboard, "pasteRootPos");
        clipboard.HandlePointerDown(new PointerEventData(null) { position = Vector2.one });
        clipboard.HandleDrag(new PointerEventData(null) { position = new Vector2(600, 600) });
        Check(!clipboard.isDraggingSelection && Field<Vector3>(clipboard, "pasteRootPos") == heldRoot,
            "Pending confirmation freezes copied-preview dragging");
        clipboard.dontShowAgainToggle.SetIsOnWithoutNotify(true);
        Check(ClipboardManager.TryHandleOverrideBack(), "Back is consumed by the pending overlap dialog");
        Check(!Pending(clipboard) && !clipboard.overrideConfirmPanel.activeSelf && clipboard.isPasteMode,
            "Cancel closes dialog and retains copy preview");
        Check(!Field<bool>(clipboard, "skipOverrideConfirm"), "Cancelling never applies the skip-warnings choice");
        clipboard.ConfirmOverride();
        Check(existing.gameObject.activeSelf && UndoStack(command).Count == 0, "Confirm after cancel is a no-op");

        var budget = New(scene, "Detached budget readout", false).AddComponent<BuildUIController>();
        SetStatic(typeof(BuildUIController), "Instance", budget);
        Set(budget, "cachedBaseCost", 4f);
        budget.maxBudget = 8f;
        Check((bool)Invoke(clipboard, "CanAffordPaste"), "Replacement charges only new cost minus replaced cost");
        budget.maxBudget = 7f;
        Check(!(bool)Invoke(clipboard, "CanAffordPaste"), "Net replacement still respects insufficient budget");
        Vector3 originalEnd = end.transform.position;
        end.transform.position = originalEnd + Vector3.right * .19f;
        existing.currentLength = 4.19f;
        Set(budget, "cachedBaseCost", 4.19f);
        budget.maxBudget = 8.1f;
        Check(!(bool)Invoke(clipboard, "CanAffordPaste"),
            "Mapped endpoint tolerance cannot undercharge a 4.19m replacement as the 4m clipboard span");
        end.transform.position = originalEnd; existing.currentLength = 4f;
        Set(budget, "cachedBaseCost", 4f);
        var copied = Field<List<CopiedBarInfo>>(clipboard, "copiedBars");
        copied[0].mat = oldMaterial; budget.maxBudget = 4f;
        Check((bool)Invoke(clipboard, "CanAffordPaste"), "Identical material overlap costs nothing");
        copied[0].mat = newMaterial;
        SetStatic(typeof(BuildUIController), "Instance", null);

        Set(clipboard, "blockBuildInputThroughFrame", -1);
        clipboard.StampPaste();
        Check(Pending(clipboard), "Second confirmation can open after review");
        Check(!clipboard.dontShowAgainToggle.isOn, "A newly opened confirmation resets its unchecked preference");
        SetStatic(typeof(BuildUIController), "Instance", budget);
        budget.maxBudget = 3f;
        clipboard.ConfirmOverride();
        Check(!Pending(clipboard) && existing.gameObject.activeSelf && UndoStack(command).Count == 0,
            "Budget changed while dialog open is revalidated before committing");
        SetStatic(typeof(BuildUIController), "Instance", null);

        Set(clipboard, "blockBuildInputThroughFrame", -1);
        clipboard.StampPaste();
        SetProperty(manager, "CurrentState", GameManager.GameState.Normal);
        clipboard.ConfirmOverride();
        Check(existing.gameObject.activeSelf && UndoStack(command).Count == 0, "Stale confirmation outside build mode never commits");
        SetProperty(manager, "CurrentState", GameManager.GameState.Building);
        clipboard.CancelOverride();
        clipboard.isPasteMode = true;
        Invoke(clipboard, "UpdatePasteGhostsWorldPosition", start.transform.position);
        Set(clipboard, "blockBuildInputThroughFrame", -1);
        clipboard.StampPaste();
        clipboard.dontShowAgainToggle.SetIsOnWithoutNotify(true);
        clipboard.ConfirmOverride();
        Check(!Pending(clipboard) && !clipboard.overrideConfirmPanel.activeSelf, "Valid confirmation closes atomically");
        Check(Field<bool>(clipboard, "skipOverrideConfirm"), "Successful confirmation applies the session-only skip choice");
        Check(!existing.gameObject.activeSelf && UndoStack(command).Count == 1, "Replacement disables old material and records exactly one build action");
        HistoryAction action = UndoStack(command).Peek();
        Check(action.isBuildEvent && action.disabledObjects.Contains(existing.gameObject), "History owns replaced old material");
        Check(action.affectedObjects.Count == 1, "Replacement reuses nodes and creates only the new member");
        Bar placed = action.affectedObjects[0].GetComponent<Bar>();
        Check(placed != null && placed.materialData == newMaterial && placed.startPoint == start && placed.endPoint == end,
            "New member has intended material and original endpoints");
        clipboard.ConfirmOverride();
        Check(UndoStack(command).Count == 1 && action.affectedObjects.Count == 1, "Duplicate confirmation does not create a second action");
        action.Undo();
        Check(existing.gameObject.activeSelf && !placed.gameObject.activeSelf, "Undo restores old material and hides replacement");
        action.Redo();
        Check(!existing.gameObject.activeSelf && placed.gameObject.activeSelf, "Redo restores replacement");

        // Native Edit Mode fixtures own their temporary objects; avoid runtime deferred Destroy.
        Field<List<GameObject>>(clipboard, "ghostPastePoints").Clear();
        Field<List<Bar>>(clipboard, "ghostPasteBars").Clear();
        clipboard.CancelPasteMode();
        Check(!Pending(clipboard) && !clipboard.overrideConfirmPanel.activeSelf && !clipboard.isPasteMode,
            "Cancel paste invalidates confirmation");
        clipboard.ConfirmOverride();
        Check(UndoStack(command).Count == 1, "Confirm after paste teardown does not commit");
        report.AppendLine("PASS: pending dialog, frozen preview, Back/cancel/stale/double confirmation, net replacement budgets, late budget changes, replacement identity, atomic undo/redo and teardown.");
    }

    private static void ValidateRendering(Scene scene, GameObject source, StringBuilder report)
    {
        var camera = New(scene, "Detached UI camera", true).AddComponent<Camera>();
        camera.scene = scene; camera.cullingMask = 1 << 29; camera.useOcclusionCulling = false;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(67, 92, 98, 255);
        camera.transform.position = new Vector3(0, 0, -10);
        var canvasObject = New(scene, "Detached UI canvas", true, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
        Scene sourceScene = source.scene;
        Check(sourceScene.IsValid() && sourceScene != scene, "Source panel remains in its separate prefab-content scene");
        GameObject panel = UnityEngine.Object.Instantiate(source, canvas.transform, false);
        Check(panel.scene == scene && source.scene == sourceScene, "Only the cloned panel belongs to the rendering fixture scene");
        panel.SetActive(true);
        foreach (Transform item in panel.GetComponentsInChildren<Transform>(true)) item.gameObject.layer = 29;
        foreach (Canvas nested in panel.GetComponentsInChildren<Canvas>(true))
        {
            nested.renderMode = RenderMode.ScreenSpaceCamera;
            nested.worldCamera = camera;
            nested.planeDistance = 1f;
        }
        EventSystem eventSystem = New(scene, "Detached raycast EventSystem", false).AddComponent<EventSystem>();
        foreach (Vector2Int dimensions in new[] { new Vector2Int(1280, 720), new Vector2Int(1920, 1080),
                     new Vector2Int(1024, 768), new Vector2Int(720, 1280) })
        {
            var target = new RenderTexture(dimensions.x, dimensions.y, 24) { hideFlags = HideFlags.HideAndDontSave };
            Texture2D image = null; RenderTexture previous = RenderTexture.active;
            try
            {
                target.Create(); camera.targetTexture = target; camera.aspect = (float)dimensions.x / dimensions.y;
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)panel.transform);
                MaterialOverlapPanelLayout layout = panel.GetComponent<MaterialOverlapPanelLayout>();
                Check(layout != null, "Authored responsive overlap layout exists");
                layout.ApplyLayoutFor(((RectTransform)panel.transform).rect);
                Canvas.ForceUpdateCanvases();
                foreach (Button button in panel.GetComponentsInChildren<Button>(true))
                {
                    if (!button.gameObject.activeInHierarchy) continue;
                    RectTransform rect = (RectTransform)button.transform;
                    var corners = new Vector3[4]; rect.GetWorldCorners(corners);
                    foreach (Vector3 corner in corners)
                    {
                        Vector3 screen = camera.WorldToScreenPoint(corner);
                        Check(screen.x >= -1 && screen.x <= dimensions.x + 1 && screen.y >= -1 && screen.y <= dimensions.y + 1,
                            dimensions + " button remains onscreen: " + button.name);
                    }
                    Check(rect.rect.width >= 40 && rect.rect.height >= 40, "Comfortable authored action target: " + button.name);
                }
                foreach (TMP_Text label in panel.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (!label.gameObject.activeInHierarchy || string.IsNullOrWhiteSpace(label.text)) continue;
                    label.ForceMeshUpdate(true, true);
                    RectTransform textRect = label.rectTransform;
                    Check(label.textBounds.size.x <= textRect.rect.width + 2f && label.textBounds.size.y <= textRect.rect.height + 2f,
                        dimensions + " text fits without cropping: " + label.name);
                    Bounds glyphs = label.textBounds;
                    Rect area = textRect.rect;
                    Check(glyphs.min.x >= area.xMin - 2f && glyphs.max.x <= area.xMax + 2f &&
                        glyphs.min.y >= area.yMin - 2f && glyphs.max.y <= area.yMax + 2f,
                        dimensions + " actual glyph positions stay inside their authored text area: " + label.name);
                    Check(label.fontSize * canvas.scaleFactor >= 14f,
                        dimensions + " text remains readable without tiny autosizing: " + label.name);
                    if (label.GetComponentInParent<Button>(true) != null) continue;
                    Rect textBounds = ScreenRect(camera, textRect);
                    foreach (Button button in panel.GetComponentsInChildren<Button>(true))
                    {
                        if (!button.gameObject.activeInHierarchy) continue;
                        Rect buttonBounds = ScreenRect(camera, (RectTransform)button.transform);
                        buttonBounds.xMin += 1f; buttonBounds.xMax -= 1f;
                        buttonBounds.yMin += 1f; buttonBounds.yMax -= 1f;
                        Check(!textBounds.Overlaps(buttonBounds), dimensions + " text does not overlap action: " + label.name + " / " + button.name);
                    }
                }
                // A native render populates Graphic.depth before GraphicRaycaster
                // queries; a detached prefab fixture has no normal frame loop.
                camera.Render();
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                image = new Texture2D(dimensions.x, dimensions.y, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, dimensions.x, dimensions.y), 0, 0); image.Apply();
                Directory.CreateDirectory("output/MaterialOverlapPanel");
                File.WriteAllBytes("output/MaterialOverlapPanel/" + dimensions.x + "x" + dimensions.y + ".png", image.EncodeToPNG());
                var hits = new List<RaycastResult>();
                var pointer = new PointerEventData(eventSystem) { position = new Vector2(8, 8) };
                foreach (GraphicRaycaster raycaster in canvasObject.GetComponentsInChildren<GraphicRaycaster>(true))
                    raycaster.Raycast(pointer, hits);
                bool blocksCorner = hits.Exists(hit => hit.gameObject == panel || hit.gameObject.transform.IsChildOf(panel.transform));
                if (!blocksCorner)
                {
                    var diagnostic = new StringBuilder();
                    diagnostic.AppendLine("Detached raycast diagnostics: " + dimensions + ", hits=" + hits.Count +
                        ", camera=" + camera.pixelRect + ", rootRect=" + ((RectTransform)canvas.transform).rect +
                        ", panelRect=" + ((RectTransform)panel.transform).rect);
                    foreach (Graphic graphic in panel.GetComponentsInChildren<Graphic>(true))
                        diagnostic.AppendLine(graphic.name + ": active=" + graphic.gameObject.activeInHierarchy +
                            ", enabled=" + graphic.enabled + ", depth=" + graphic.depth + ", target=" + graphic.raycastTarget +
                            ", canvas=" + (graphic.canvas != null ? graphic.canvas.name + "/" + graphic.canvas.renderMode : "null") +
                            ", eventCamera=" + (graphic.canvas != null && graphic.canvas.worldCamera != null ? graphic.canvas.worldCamera.name : "null"));
                    report.Append(diagnostic);
                    Debug.LogWarning(diagnostic.ToString());
                }
                Check(blocksCorner, dimensions + " full-screen backdrop intercepts an empty-corner click");
            }
            finally
            {
                camera.targetTexture = null; RenderTexture.active = previous;
                if (image != null) UnityEngine.Object.DestroyImmediate(image);
                target.Release(); UnityEngine.Object.DestroyImmediate(target);
            }
        }
        report.AppendLine("PASS: detached landscape/portrait phone, wide-screen and tablet renders; readable text, no text/action collisions, onscreen buttons and click blocking. Previews: output/MaterialOverlapPanel/.");
    }

    private static Point NewPoint(Scene scene, Transform parent, string name, Vector3 position, BuildLocation location)
    {
        var go = New(scene, name, true); go.transform.SetParent(parent, false); go.transform.position = position;
        var point = go.AddComponent<Point>(); point.Runtime = true; point.AssignOwner(location);
        if (!Point.AllPoints.Contains(point)) Point.AllPoints.Add(point);
        return point;
    }
    private static GameObject New(Scene scene, string name, bool active, params Type[] components)
    {
        var go = new GameObject(name, components) { hideFlags = HideFlags.HideAndDontSave, layer = 29 };
        go.SetActive(active); SceneManager.MoveGameObjectToScene(go, scene); return go;
    }
    private static bool Pending(ClipboardManager owner) => (bool)owner.GetType().GetProperty("IsOverrideConfirmationPending", Members).GetValue(owner);
    private static Rect ScreenRect(Camera camera, RectTransform rect)
    {
        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
        Vector2 minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        foreach (Vector3 corner in corners)
        {
            Vector2 screen = camera.WorldToScreenPoint(corner);
            minimum = Vector2.Min(minimum, screen); maximum = Vector2.Max(maximum, screen);
        }
        return Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y);
    }
    private static Stack<HistoryAction> UndoStack(CommandManager owner) => Field<Stack<HistoryAction>>(owner, "undoStack");
    private static object Static(Type type, string name) => type.GetProperty(name, Members | BindingFlags.Static).GetValue(null);
    private static void SetStatic(Type type, string name, object value) => type.GetProperty(name, Members | BindingFlags.Static).SetValue(null, value);
    private static void SetProperty(object owner, string name, object value) => owner.GetType().GetProperty(name, Members).SetValue(owner, value);
    private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Members).SetValue(owner, value);
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Members).GetValue(owner);
    private static object Invoke(object owner, string name, params object[] arguments) => owner.GetType().GetMethod(name, Members).Invoke(owner, arguments);
    private static void Check(bool condition, string message) { checks++; if (!condition) throw new InvalidOperationException(message); }

    private sealed class SceneSnapshot
    {
        private readonly Scene scene; private readonly bool dirty; private readonly HashSet<int> roots = new HashSet<int>();
        public SceneSnapshot(Scene value) { scene = value; dirty = value.isDirty; foreach (GameObject root in value.GetRootGameObjects()) roots.Add(root.GetInstanceID()); }
        public bool IsUnchanged()
        {
            if (!scene.IsValid() || scene.isDirty != dirty || scene.rootCount != roots.Count) return false;
            foreach (GameObject root in scene.GetRootGameObjects()) if (!roots.Contains(root.GetInstanceID())) return false;
            return true;
        }
    }
}
#endif
