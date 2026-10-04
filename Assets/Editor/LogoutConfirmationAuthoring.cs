using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Opt-in scene authoring. Never signs out, calls PlayFab, or changes player preferences.</summary>
public static class LogoutConfirmationAuthoring
{
    private const string Request = "Temp/logout-confirmation-ui-v1.request";
    private const string RecoveryRequest = "Temp/logout-confirmation-recover-v1.request";
    private const string DiagnosticRequest = "Temp/logout-confirmation-diagnose-v1.request";
    private const string Diagnostics = "Temp/LogoutConfirmationDiagnostics.txt";
    private const string Report = "Temp/LogoutConfirmationValidation.txt";
    private const string Title = "LOG OUT?";
    private const string Message = "Sign out of this session?\n\nYour saved progress stays on this device.\nSign in again to return to your account.";
    private static readonly string[] Scenes = {
        "Assets/Scenes/Main Menu.unity", "Assets/Scenes/Mode Selection.unity",
        "Assets/Scenes/CanyonCrossing.unity", "Assets/Scenes/BHAN HOUSE.unity",
        "Assets/Scenes/Multiplayer/Multiplayer.unity"
    };
    private static readonly Color Wood = new Color32(90, 55, 31, 255), Ink = new Color32(77, 53, 36, 255);
    private static readonly Color Cream = new Color32(248, 233, 204, 255), Sand = new Color32(239, 219, 177, 255);
    private static readonly Color Gold = new Color32(231, 158, 35, 255);
    private static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private static Sprite rounded;
    private static TMP_FontAsset font;
    private static double nextCheck;
    private static bool recordingAuthoringUndo;

    [InitializeOnLoadMethod]
    private static void Watch() { EditorApplication.update -= CheckRequest; EditorApplication.update += CheckRequest; }

    private static void CheckRequest()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 2;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (File.Exists(DiagnosticRequest))
        {
            File.Delete(DiagnosticRequest);
            DescribeLoadedSettings();
            return;
        }
        if (File.Exists(RecoveryRequest))
        {
            File.Delete(RecoveryRequest);
            try { RecoverPartialRoot(); }
            catch (Exception error) { File.WriteAllText(Report, "FAIL: Partial logout recovery: " + error); Debug.LogException(error); }
            return;
        }
        if (!File.Exists(Request)) return;
        string dirty = DirtyTarget();
        if (dirty != null)
        { File.WriteAllText(Report, "WAIT: Save scene edits in " + dirty + " first. No target scenes were changed."); return; }
        File.Delete(Request);
        try { Author(); }
        catch (Exception error) { File.WriteAllText(Report, "FAIL: " + error); Debug.LogException(error); }
    }

    [MenuItem("Tools/Civil Craft/Add Authored Logout Confirmation")]
    public static void Author()
    {
        Check(!EditorApplication.isCompiling && !EditorApplication.isUpdating && !EditorApplication.isPlayingOrWillChangePlaymode,
            "Stop Play Mode and wait for imports/compilation before authoring logout confirmation.");
        Check(DirtyTarget() == null, "Save all target scene edits before authoring logout confirmation.");
        rounded = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Elements/UI/LevelResultRounded.png");
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/Bekind Sans SDF.asset");
        Check(rounded != null && font != null, "Existing rounded frame/Bekind font missing.");
        foreach (string path in Scenes) Check(AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null, "Missing build scene: " + path);
        Scene active = SceneManager.GetActiveScene();
        var report = new StringBuilder();
        File.WriteAllText(Diagnostics, "Logout authoring native diagnostics\n");
        try
        {
            foreach (string path in Scenes)
            {
                Scene scene = SceneManager.GetSceneByPath(path);
                bool opened = !scene.IsValid() || !scene.isLoaded;
                if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                Undo.IncrementCurrentGroup();
                int undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Author Logout Confirmation in " + scene.name);
                bool saved = false;
                try
                {
                    SettingsManager[] managers = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<SettingsManager>(true)).ToArray();
                    Check(managers.Length > 0, "SettingsManager missing in " + path);
                    foreach (SettingsManager manager in managers)
                    {
                        Check(manager.settingsPanel != null, "Settings panel missing in " + path);
                        Describe(manager, "before Build");
                        var originalActions = CaptureEvents(manager.settingsPanel);
                        Undo.RegisterFullObjectHierarchyUndo(manager.settingsPanel, "Author Logout Confirmation");
                        // Complete snapshots are immediate. RegisterCreatedObjectUndo
                        // stops active RecordObject tracking when children are created.
                        Undo.RegisterCompleteObjectUndo(manager, "Wire Logout Confirmation");
                        recordingAuthoringUndo = true;
                        AuthoredSaveChoiceDialog dialog = Build(manager);
                        recordingAuthoringUndo = false;
                        Describe(manager, "after Build");
                        ValidateReferences(manager, dialog);
                        report.AppendLine(LogoutConfirmationValidation.ValidateAuthored(manager));
                        foreach (var pair in originalActions) Check(EventJson(pair.Key) == pair.Value, "Existing settings callback changed: " + pair.Key.name + " in " + path);
                        EditorUtility.SetDirty(manager);
                    }
                    Undo.FlushUndoRecordObjects();
                    foreach (SettingsManager manager in managers) Describe(manager, "before SaveScene");
                    EditorSceneManager.MarkSceneDirty(scene);
                    Check(EditorSceneManager.SaveScene(scene), "Unity could not save authored logout confirmation in " + path);
                    saved = true;
                    Check(!scene.isDirty, "Unity left authored logout confirmation unsaved in " + path);
                    foreach (SettingsManager manager in managers)
                    {
                        Describe(manager, "after SaveScene");
                        ValidateReferences(manager, manager.logoutConfirmation);
                    }
                    if (path == Scenes[0])
                    {
                        Undo.FlushUndoRecordObjects();
                        ValidatePreview(managers[0].logoutConfirmation, report);
                        foreach (SettingsManager manager in managers)
                        {
                            Describe(manager, "after disposable Preview");
                            ValidateReferences(manager, manager.logoutConfirmation);
                        }
                    }
                    report.AppendLine("PASS: " + path + " saved with inactive serialized LogoutConfirmation, Stay selection reference, full-screen dim blocker, nested Canvas 32000, explicit navigation and unchanged existing settings callbacks.");
                }
                catch
                {
                    recordingAuthoringUndo = false;
                    if (!saved) Undo.RevertAllDownToGroup(undoGroup);
                    throw;
                }
                finally
                {
                    recordingAuthoringUndo = false;
                    // Never discard changes if Unity refuses a native save. Leave
                    // that utility-opened scene available for the user to recover.
                    if (opened && scene.IsValid() && scene.isLoaded && !scene.isDirty) EditorSceneManager.CloseScene(scene, true);
                }
            }
            report.AppendLine(LogoutConfirmationValidation.Run());
            report.AppendLine("NOTE: Only authored UI and disposable preview callbacks were used. No actual logout, PlayFab calls, PlayerPrefs, purchases or save changes were performed.");
            File.WriteAllText(Report, report.ToString());
            Debug.Log("[Logout confirmation] Saved authored Log Out/Stay confirmation in all five build scenes; isolated references, layout, animation and callback checks passed.");
        }
        finally { if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active); }
    }

    // Reads loaded targets and builds only a disposable preview-scene fixture.
    // No real scene settings, authentication, preferences or saves are changed.
    private static void DescribeLoadedSettings()
    {
        File.WriteAllText(Diagnostics, "Read-only loaded settings diagnostics\n");
        foreach (string path in Scenes)
        {
            Scene scene = SceneManager.GetSceneByPath(path);
            if (!scene.IsValid() || !scene.isLoaded)
            { File.AppendAllText(Diagnostics, path + " is not loaded.\n"); continue; }
            foreach (SettingsManager manager in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<SettingsManager>(true)))
                Describe(manager, "read-only current state");
        }
        DescribeCanvasFixture();
    }

    private static void DescribeCanvasFixture()
    {
        Scene preview = EditorSceneManager.NewPreviewScene();
        Scene active = SceneManager.GetActiveScene();
        bool previousRecording = recordingAuthoringUndo;
        recordingAuthoringUndo = false;
        try
        {
            rounded = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Elements/UI/LevelResultRounded.png");
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/Bekind Sans SDF.asset");
            var canvasObject = new GameObject("Logout Sorting Fixture Canvas (Temporary)", typeof(RectTransform), typeof(Canvas));
            SceneManager.MoveGameObjectToScene(canvasObject, preview);
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            canvasObject.GetComponent<RectTransform>().sizeDelta = new Vector2(1920, 1080);
            var controllerObject = new GameObject("Logout Sorting Fixture Controller (Temporary)"); controllerObject.SetActive(false);
            SceneManager.MoveGameObjectToScene(controllerObject, preview);
            var manager = controllerObject.AddComponent<SettingsManager>(); manager.enabled = false;
            manager.settingsPanel = Child(canvasObject.transform, "Inactive Fixture Settings");
            Place(manager.settingsPanel.transform as RectTransform, 0, 0, 1, 1);
            manager.settingsPanel.SetActive(false);
            AuthoredSaveChoiceDialog dialog = Build(manager);
            Describe(manager, "FIXTURE: after exact Build, settings inactive, dialog inactive");
            ValidateReferences(manager, dialog);
            manager.settingsPanel.SetActive(true);
            Describe(manager, "FIXTURE: settings active, dialog inactive");
            dialog.gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases();
            Describe(manager, "FIXTURE: settings active, dialog visible");
            Canvas canvas = dialog.GetComponent<Canvas>();
            Check(canvas.overrideSorting && canvas.sortingOrder == 32000 && dialog.GetComponent<GraphicRaycaster>() != null,
                "Visible fixture canvas did not use authored sorting/raycasting.");
            dialog.gameObject.SetActive(false);
            manager.settingsPanel.SetActive(false);
            Describe(manager, "FIXTURE: hidden again");
            ValidateReferences(manager, dialog);
            File.AppendAllText(Diagnostics, "PASS: Disposable exact-Build fixture retains serialized sorting while inactive and actual 32000 override sorting when visible. No real logout or preferences executed.\n");
        }
        catch (Exception error)
        {
            File.AppendAllText(Diagnostics, "FIXTURE FAIL: " + error + "\n");
            Debug.LogException(error);
        }
        finally
        {
            recordingAuthoringUndo = previousRecording;
            EditorSceneManager.ClosePreviewScene(preview);
            if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
        }
    }

    private static void Describe(SettingsManager manager, string phase)
    {
        var text = new StringBuilder();
        text.AppendLine("PHASE: " + phase);
        text.AppendLine("manager=" + DescribeObject(manager) + " sceneDirty=" + manager.gameObject.scene.isDirty);
        text.AppendLine("settingsPanel=" + DescribeObject(manager.settingsPanel));
        AuthoredSaveChoiceDialog dialog = manager.logoutConfirmation;
        GameObject root = dialog != null ? dialog.gameObject : manager.settingsPanel != null && manager.settingsPanel.transform.Find("LogoutConfirmation") != null
            ? manager.settingsPanel.transform.Find("LogoutConfirmation").gameObject : null;
        text.AppendLine("dialog=" + DescribeObject(dialog));
        text.AppendLine("root=" + DescribeObject(root));
        if (root != null)
        {
            Canvas canvas = root.GetComponent<Canvas>();
            text.AppendLine("canvas=" + DescribeObject(canvas) + (canvas != null
                ? " overrideSorting=" + canvas.overrideSorting + " sortingOrder=" + canvas.sortingOrder + " renderMode=" + canvas.renderMode + " rootCanvas=" + DescribeObject(canvas.rootCanvas) : ""));
            if (canvas != null)
            {
                var serializedCanvas = new SerializedObject(canvas);
                SerializedProperty overrideSorting = serializedCanvas.FindProperty("m_OverrideSorting");
                SerializedProperty sortingOrder = serializedCanvas.FindProperty("m_SortingOrder");
                text.AppendLine("canvasSerialized overrideSorting=" + (overrideSorting != null ? overrideSorting.boolValue.ToString() : "missing") +
                    " sortingOrder=" + (sortingOrder != null ? sortingOrder.intValue.ToString() : "missing"));
            }
            text.AppendLine("raycaster=" + DescribeObject(root.GetComponent<GraphicRaycaster>()));
            text.AppendLine("components=" + string.Join(",", root.GetComponents<Component>().Select(component => component == null ? "missing" : component.GetType().Name + ":" + component.hideFlags)));
        }
        File.AppendAllText(Diagnostics, text.ToString());
    }

    private static string DescribeObject(Object obj)
    {
        if (obj == null) return "null";
        GameObject gameObject = obj is GameObject go ? go : obj is Component component ? component.gameObject : null;
        return obj.name + " id=" + obj.GetInstanceID() + " hideFlags=" + obj.hideFlags + " persistent=" + EditorUtility.IsPersistent(obj) +
            " assetContains=" + AssetDatabase.Contains(obj) + " assetPath=" + AssetDatabase.GetAssetPath(obj) +
            (gameObject != null ? " scene=" + gameObject.scene.path + " sceneHandle=" + gameObject.scene.handle + " preview=" + EditorSceneManager.IsPreviewScene(gameObject.scene) +
                " activeSelf=" + gameObject.activeSelf + " parent=" + (gameObject.transform.parent != null ? gameObject.transform.parent.name : "none") : "");
    }

    // The first attempted run failed before Canvas creation. Recovery is opt-in
    // and accepts only that exact, empty, unreferenced root; it never clears the
    // scene's dirty flag or discards/saves pre-existing unsaved user edits.
    private static void RecoverPartialRoot()
    {
        string failure = File.Exists(Report) ? File.ReadAllText(Report) : "";
        Check(failure.Contains("MissingComponentException") && failure.Contains("LogoutConfirmationAuthoring.Build"),
            "The known partial-authoring failure was not recorded; automatic recovery refused.");
        Scene scene = SceneManager.GetSceneByPath(Scenes[0]);
        Check(scene.IsValid() && scene.isLoaded, "Keep Main Menu loaded to recover its partial logout root.");
        SettingsManager[] managers = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<SettingsManager>(true)).ToArray();
        var matches = new List<GameObject>();
        foreach (SettingsManager manager in managers)
        {
            if (manager.settingsPanel == null) continue;
            Transform found = manager.settingsPanel.transform.Find("LogoutConfirmation");
            if (found == null) continue;
            GameObject root = found.gameObject;
            RectTransform rect = found as RectTransform;
            Check(!root.activeSelf && found.childCount == 0 && rect != null && root.GetComponent<AuthoredSaveChoiceDialog>() == null,
                "LogoutConfirmation no longer matches the empty failed root; preserve it and inspect manually.");
            Check(manager.logoutConfirmation == null && manager.logoutStayButton == null && root.layer == manager.settingsPanel.layer &&
                rect.anchorMin == Vector2.zero && rect.anchorMax == Vector2.one && rect.sizeDelta == Vector2.zero &&
                rect.anchoredPosition == Vector2.zero && rect.localScale == Vector3.one && rect.localRotation == Quaternion.identity,
                "Partial logout root has been referenced or edited; recovery refused.");
            Check(root.GetComponents<Component>().All(component => component != null &&
                (component is RectTransform || component is CanvasRenderer || component is Canvas)),
                "Partial logout root contains additional components; recovery refused.");
            matches.Add(root);
        }
        Check(matches.Count == 1, "Expected exactly one known partial logout root; recovery refused.");
        bool originallyDirty = scene.isDirty;
        Undo.IncrementCurrentGroup(); Undo.SetCurrentGroupName("Recover Partial Logout Confirmation");
        Undo.DestroyObjectImmediate(matches[0]);
        if (!originallyDirty)
        {
            Check(EditorSceneManager.SaveScene(scene), "Unity could not save the scoped partial logout cleanup.");
            File.WriteAllText(Report, "PASS: Removed only the empty failed LogoutConfirmation root from Main Menu and natively saved its cleanup. The scene had no pre-existing unsaved edits. Other objects, callbacks and manager references are unchanged; the authoring retry can proceed. The removal can be recovered with Undo.");
            Debug.Log("[Logout confirmation] Recovered and saved only the empty failed Main Menu root; no pre-existing unsaved scene edits were present.");
        }
        else
        {
            File.WriteAllText(Report, "WAIT: Removed only the empty failed LogoutConfirmation root from Main Menu. Other objects, callbacks and manager references are unchanged. Existing unsaved scene edits were preserved: save Main Menu with Ctrl+S before the queued authoring retry. The removal can be recovered with Undo.");
            Debug.Log("[Logout confirmation] Recovered the empty failed Main Menu root only. Save Main Menu before the queued retry; existing unsaved edits were preserved and no scene dirty flags were cleared.");
        }
    }

    private static string DirtyTarget()
    {
        foreach (string path in Scenes)
        {
            Scene scene = SceneManager.GetSceneByPath(path);
            if (scene.IsValid() && scene.isLoaded && scene.isDirty) return path;
        }
        return null;
    }

    private static AuthoredSaveChoiceDialog Build(SettingsManager manager)
    {
        Transform found = manager.settingsPanel.transform.Find("LogoutConfirmation");
        GameObject root;
        if (found != null)
        {
            root = found.gameObject;
            Check(root.GetComponent<AuthoredSaveChoiceDialog>() != null, "Existing LogoutConfirmation is not the authored dialog; nothing was replaced.");
        }
        else root = Child(manager.settingsPanel.transform, "LogoutConfirmation");
        root.SetActive(false);
        Place(root.transform as RectTransform, 0, 0, 1, 1);
        root.transform.SetAsLastSibling();
        Canvas canvas = Ensure<Canvas>(root); canvas.overrideSorting = true; canvas.sortingOrder = 32000;
        Ensure<GraphicRaycaster>(root);
        Check(root.GetComponent<CanvasScaler>() == null, "Nested logout Canvas must inherit the root CanvasScaler.");
        Image overlay = Ensure<Image>(root); overlay.sprite = null; overlay.type = Image.Type.Simple;
        overlay.color = new Color(.08f, .055f, .035f, .62f); overlay.raycastTarget = true;
        CanvasGroup group = Ensure<CanvasGroup>(root); group.alpha = 1; group.interactable = group.blocksRaycasts = true;

        Image frame = Surface(root.transform, "Logout Wood Frame", Wood, true);
        Place(frame.rectTransform, .28f, .25f, .72f, .75f);
        Image face = Surface(frame.transform, "Logout Cream Surface", Cream, true);
        Place(face.rectTransform, 0, 0, 1, 1); face.rectTransform.sizeDelta = new Vector2(-16, -16);
        TMP_Text title = Label(face.transform, "Logout Title", Title, 46, 34);
        Place(title.rectTransform, .08f, .77f, .92f, .90f);
        Image divider = Surface(face.transform, "Logout Header Divider", new Color32(177, 131, 76, 255), false);
        divider.sprite = null; divider.type = Image.Type.Simple; Place(divider.rectTransform, .09f, .73f, .91f, .733f);
        TMP_Text message = Label(face.transform, "Logout Message", Message, 28, 23);
        Place(message.rectTransform, .08f, .31f, .92f, .69f);
        Button stay = Button(face.transform, "Stay Signed In", "STAY", Sand);
        Button logout = Button(face.transform, "Confirm Logout", "LOG OUT", Gold);
        Place(stay.transform as RectTransform, .08f, .11f, .485f, .255f);
        Place(logout.transform as RectTransform, .515f, .11f, .92f, .255f);
        stay.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnRight = logout, selectOnLeft = logout };
        logout.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = stay, selectOnRight = stay };

        AuthoredSaveChoiceDialog dialog = Ensure<AuthoredSaveChoiceDialog>(root);
        dialog.SetAuthoringReferences(title, message, stay.GetComponentInChildren<TMP_Text>(true), logout.GetComponentInChildren<TMP_Text>(true), stay, logout);
        AuthoredUIReveal reveal = Ensure<AuthoredUIReveal>(root);
        var revealData = new SerializedObject(reveal);
        revealData.FindProperty("visual").objectReferenceValue = frame.rectTransform;
        revealData.FindProperty("group").objectReferenceValue = group;
        revealData.FindProperty("duration").floatValue = .18f;
        revealData.FindProperty("startScale").floatValue = .985f;
        revealData.ApplyModifiedPropertiesWithoutUndo();
        manager.logoutConfirmation = dialog;
        manager.logoutStayButton = stay;
        // Inactive nested canvases do not always expose their authored order via
        // the native getter until registration. Write serialized state last,
        // after all required components and TMP canvas setup have been created.
        var canvasData = new SerializedObject(canvas);
        Check(canvasData.FindProperty("m_OverrideSorting") != null && canvasData.FindProperty("m_SortingOrder") != null,
            "Canvas serialized sorting fields are unavailable.");
        canvasData.FindProperty("m_OverrideSorting").boolValue = true;
        canvasData.FindProperty("m_SortingOrder").intValue = 32000;
        canvasData.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(canvas);
        EditorUtility.SetDirty(dialog); EditorUtility.SetDirty(reveal);
        return dialog;
    }

    private static void ValidateReferences(SettingsManager manager, AuthoredSaveChoiceDialog dialog)
    {
        Check(dialog != null && manager.logoutConfirmation == dialog && !dialog.gameObject.activeSelf,
            "Authored logout dialog is missing or saved active.");
        Check(dialog.transform.parent == manager.settingsPanel.transform, "Logout dialog must remain inside its full-screen settings panel.");
        Check(dialog.gameObject.scene == manager.gameObject.scene && manager.settingsPanel.scene == manager.gameObject.scene &&
            !EditorUtility.IsPersistent(dialog) && !AssetDatabase.Contains(dialog) &&
            (dialog.gameObject.hideFlags & HideFlags.DontSave) == 0,
            "Logout dialog belongs to a preview/asset scene or is excluded from scene serialization.");
        RectTransform rect = dialog.transform as RectTransform;
        Check(rect != null && rect.anchorMin == Vector2.zero && rect.anchorMax == Vector2.one && rect.sizeDelta == Vector2.zero,
            "Logout blocker does not fill the settings panel.");
        Canvas canvas = dialog.GetComponent<Canvas>();
        Check(canvas != null && dialog.GetComponent<GraphicRaycaster>() != null, "Logout modal Canvas/raycasting component is not authored.");
        var canvasData = new SerializedObject(canvas);
        Check(canvasData.FindProperty("m_OverrideSorting").boolValue && canvasData.FindProperty("m_SortingOrder").intValue == 32000,
            "Logout modal serialized sorting is not authored.");
        Check(dialog.GetComponent<CanvasScaler>() == null && dialog.GetComponent<Image>().raycastTarget,
            "Logout blocker has independent scaling or fails to block the underlying settings.");
        foreach (string field in new[] { "titleText", "messageText", "leftLabel", "rightLabel", "leftButton", "rightButton" })
            Check(Get<Object>(dialog, field) != null, "Logout dialog serialized reference missing: " + field);
        Button stay = Get<Button>(dialog, "leftButton"), logout = Get<Button>(dialog, "rightButton");
        Check(manager.logoutStayButton == stay && stay.onClick.GetPersistentEventCount() == 0 && logout.onClick.GetPersistentEventCount() == 0,
            "Logout dialog contains stale persistent actions or the safe selection reference is missing.");
        Check(stay.navigation.mode == Navigation.Mode.Explicit && stay.navigation.selectOnRight == logout &&
            logout.navigation.mode == Navigation.Mode.Explicit && logout.navigation.selectOnLeft == stay, "Logout confirmation keyboard navigation is not explicit.");
        var serialized = new SerializedObject(manager);
        Check(serialized.FindProperty("logoutConfirmation").objectReferenceValue == dialog &&
            serialized.FindProperty("logoutStayButton").objectReferenceValue == stay, "Settings confirmation references were not serialized.");
        var reveal = new SerializedObject(dialog.GetComponent<AuthoredUIReveal>());
        Check(reveal.FindProperty("visual").objectReferenceValue != null && reveal.FindProperty("group").objectReferenceValue != null,
            "Logout reveal visual/group references were not authored.");
    }

    private static Dictionary<Component, string> CaptureEvents(GameObject root)
    {
        var result = new Dictionary<Component, string>();
        foreach (Component component in root.GetComponentsInChildren<Component>(true))
            if (component is Button || component is Toggle || component is Slider || component is TMP_Dropdown) result[component] = EventJson(component);
        return result;
    }
    private static string EventJson(Component component)
    {
        if (component is Button button) return JsonUtility.ToJson(button.onClick);
        if (component is Toggle toggle) return JsonUtility.ToJson(toggle.onValueChanged);
        if (component is Slider slider) return JsonUtility.ToJson(slider.onValueChanged);
        if (component is TMP_Dropdown dropdown) return JsonUtility.ToJson(dropdown.onValueChanged);
        throw new InvalidOperationException("Unexpected callback owner.");
    }

    private static void ValidatePreview(AuthoredSaveChoiceDialog source, StringBuilder report)
    {
        Scene scene = EditorSceneManager.NewPreviewScene(); RenderTexture target = null;
        try
        {
            var canvasObject = new GameObject("Logout Preview (Temporary)", typeof(RectTransform), typeof(Canvas));
            SceneManager.MoveGameObjectToScene(canvasObject, scene);
            Canvas canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
            var copy = Object.Instantiate(source.gameObject, canvasObject.transform, false);
            AuthoredSaveChoiceDialog dialog = copy.GetComponent<AuthoredSaveChoiceDialog>();
            foreach (Transform node in canvasObject.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = 30;
            var cameraObject = new GameObject("Logout Preview Camera (Temporary)", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            Camera camera = cameraObject.GetComponent<Camera>(); camera.scene = scene; camera.enabled = false;
            camera.orthographic = true; camera.transform.position = new Vector3(0, 0, -1000);
            camera.nearClipPlane = .1f; camera.farClipPlane = 2000; camera.cullingMask = 1 << 30;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(148, 113, 85, 255); canvas.worldCamera = camera;
            int stays = 0, logouts = 0;
            Action stayAction = () => { stays++; dialog.Hide(); };
            Action logoutAction = () => { logouts++; dialog.Hide(); };
            Check(dialog.Show(Title, Message, "STAY", "LOG OUT", stayAction, logoutAction), "Preview dialog could not open.");
            Canvas visibleCanvas = copy.GetComponent<Canvas>();
            Check(visibleCanvas.overrideSorting && visibleCanvas.sortingOrder == 32000 && copy.GetComponent<GraphicRaycaster>() != null,
                "Visible logout preview does not use its authored modal order/raycasting.");
            Get<Button>(dialog, "leftButton").onClick.Invoke();
            Check(stays == 1 && logouts == 0 && !dialog.IsVisible, "Stay preview does not cancel independently.");
            // Reopening must not duplicate callbacks. These are local counters,
            // never the runtime SettingsManager or actual authentication method.
            Check(dialog.Show(Title, Message, "STAY", "LOG OUT", stayAction, logoutAction), "Preview dialog could not reopen.");
            Check(dialog.Show(Title, Message, "STAY", "LOG OUT", stayAction, logoutAction), "Repeated preview request failed.");
            Get<Button>(dialog, "rightButton").onClick.Invoke();
            Check(stays == 1 && logouts == 1 && !dialog.IsVisible, "Preview confirmation duplicated or invoked the wrong callback.");
            report.AppendLine("PASS: Disposable Show/Stay/reopen/Confirm callbacks route once each; no real authentication actions executed.");
            foreach (Vector2Int size in new[] { new Vector2Int(1920, 1080), new Vector2Int(1920, 1200), new Vector2Int(2340, 1080) })
            {
                canvasRect.sizeDelta = size; camera.orthographicSize = size.y / 2f; camera.aspect = (float)size.x / size.y;
                target = new RenderTexture(size.x, size.y, 24); target.Create(); camera.targetTexture = target;
                Check(dialog.Show(Title, Message, "STAY", "LOG OUT", stayAction, logoutAction), "Preview dialog could not open for rendering.");
                Fit(copy);
                AuthoredUIReveal reveal = copy.GetComponent<AuthoredUIReveal>();
                reveal.ApplyProgress(.5f);
                Check(copy.GetComponent<CanvasGroup>().alpha > 0 && copy.GetComponent<CanvasGroup>().alpha < 1, "Logout reveal does not animate its authored CanvasGroup.");
                reveal.Restore(); Fit(copy);
                Capture(camera, target, "Temp/LogoutConfirmation_" + size.x + "x" + size.y + ".png");
                dialog.Hide(); Release(target); target = null;
                report.AppendLine("PASS: " + size + " actual authored clone fits the screen and its card, with no overflowing text; reveal restores the authored pose.");
            }
        }
        finally { Release(target); EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static void Fit(GameObject root)
    {
        Canvas.ForceUpdateCanvases(); RectTransform screen = root.transform as RectTransform;
        RectTransform card = root.transform.Find("Logout Wood Frame") as RectTransform;
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>())
        {
            text.ForceMeshUpdate(); Check(!text.isTextOverflowing, "Logout confirmation text overflows: " + text.name);
            CheckInside(text.rectTransform, card, "Logout text leaves the card: " + text.name);
        }
        foreach (Button button in root.GetComponentsInChildren<Button>()) CheckInside(button.transform as RectTransform, card, "Logout action leaves the card.");
        CheckInside(card, screen, "Logout card leaves the screen.");
    }
    private static void CheckInside(RectTransform child, RectTransform owner, string message)
    {
        Vector3[] corners = new Vector3[4]; child.GetWorldCorners(corners);
        foreach (Vector3 corner in corners)
        {
            Vector3 p = owner.InverseTransformPoint(corner); Rect r = owner.rect;
            Check(p.x >= r.xMin - .1f && p.x <= r.xMax + .1f && p.y >= r.yMin - .1f && p.y <= r.yMax + .1f, message);
        }
    }
    private static void Capture(Camera camera, RenderTexture target, string path)
    {
        Canvas.ForceUpdateCanvases();
        if (GraphicsSettings.currentRenderPipeline == null) camera.Render();
        else RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        RenderTexture previous = RenderTexture.active; var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        try { RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply(); File.WriteAllBytes(path, image.EncodeToPNG()); }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
    }
    private static void Release(RenderTexture target) { if (target != null) { target.Release(); Object.DestroyImmediate(target); } }
    private static T Get<T>(AuthoredSaveChoiceDialog dialog, string field) where T : Object => (T)typeof(AuthoredSaveChoiceDialog).GetField(field, Fields).GetValue(dialog);
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private static GameObject Child(Transform parent, string name)
    {
        Transform existing = parent.Find(name);
        if (existing != null) return existing.gameObject;
        var obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        obj.layer = parent.gameObject.layer;
        if (obj.scene != parent.gameObject.scene) SceneManager.MoveGameObjectToScene(obj, parent.gameObject.scene);
        obj.transform.SetParent(parent, false);
        if (recordingAuthoringUndo) Undo.RegisterCreatedObjectUndo(obj, "Create Authored Logout Control");
        return obj;
    }
    private static T Ensure<T>(GameObject obj) where T : Component
    {
        T component = obj.GetComponent<T>();
        if (component == null) component = recordingAuthoringUndo ? Undo.AddComponent<T>(obj) : obj.AddComponent<T>();
        return component;
    }
    private static Image Surface(Transform parent, string name, Color fill, bool raycast)
    {
        Image image = Ensure<Image>(Child(parent, name));
        image.sprite = rounded; image.type = Image.Type.Sliced; image.color = fill; image.raycastTarget = raycast; return image;
    }
    private static TMP_Text Label(Transform parent, string name, string value, float maximum, float minimum)
    {
        TMP_Text text = Ensure<TextMeshProUGUI>(Child(parent, name));
        text.font = font; text.fontSharedMaterial = font.material; text.text = value; text.color = Ink;
        text.fontStyle = FontStyles.Normal; text.alignment = TextAlignmentOptions.Center; text.characterSpacing = 0;
        text.fontSize = text.fontSizeMax = maximum; text.fontSizeMin = minimum; text.enableAutoSizing = true;
        text.raycastTarget = false; text.richText = false; text.margin = Vector4.zero; text.enableWordWrapping = true;
        text.overflowMode = TextOverflowModes.Overflow; return text;
    }
    private static Button Button(Transform parent, string name, string value, Color fill)
    {
        GameObject obj = Child(parent, name);
        Image frame = Ensure<Image>(obj); frame.sprite = rounded; frame.type = Image.Type.Sliced; frame.color = Wood; frame.raycastTarget = true;
        Image face = Surface(obj.transform, "Button Face", fill, false);
        Place(face.rectTransform, 0, 0, 1, 1); face.rectTransform.sizeDelta = new Vector2(-8, -8); face.transform.SetAsFirstSibling();
        Button button = Ensure<Button>(obj); button.targetGraphic = face; button.transition = Selectable.Transition.ColorTint;
        button.onClick = new Button.ButtonClickedEvent();
        ColorBlock colors = button.colors; colors.normalColor = Color.white; colors.highlightedColor = new Color(1, .95f, .82f);
        colors.selectedColor = colors.highlightedColor; colors.pressedColor = new Color(.85f, .76f, .61f); button.colors = colors;
        TMP_Text label = Label(obj.transform, "Button Label", value, 30, 23);
        Place(label.rectTransform, .04f, .07f, .96f, .93f); label.transform.SetAsLastSibling(); return button;
    }
    private static void Place(RectTransform rect, float x0, float y0, float x1, float y1)
    {
        rect.anchorMin = new Vector2(x0, y0); rect.anchorMax = new Vector2(x1, y1); rect.pivot = Vector2.one * .5f;
        rect.anchoredPosition = rect.sizeDelta = Vector2.zero; rect.localScale = Vector3.one; rect.localRotation = Quaternion.identity;
    }
}
