#if UNITY_EDITOR
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

/// <summary>Opt-in authored account-page layout. Never performs authentication or writes player preferences.</summary>
public static class SettingsAccountLayoutAuthoring
{
    private const string Request = "Temp/settings-account-layout-v1.request";
    private const string Report = "Temp/SettingsAccountLayoutValidation.txt";
    private static readonly string[] Scenes = {
        "Assets/Scenes/Main Menu.unity", "Assets/Scenes/Mode Selection.unity",
        "Assets/Scenes/CanyonCrossing.unity", "Assets/Scenes/BHAN HOUSE.unity",
        "Assets/Scenes/Multiplayer/Multiplayer.unity"
    };
    private static double nextCheck;

    [InitializeOnLoadMethod]
    private static void Watch() { EditorApplication.update -= CheckRequest; EditorApplication.update += CheckRequest; }

    private static void CheckRequest()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode) return;
        string dirty = DirtyTarget();
        if (dirty != null)
        { File.WriteAllText(Report, "WAIT: Save scene edits in " + dirty + " first. No target scenes were changed."); return; }
        File.Delete(Request);
        try { Author(); }
        catch (Exception error) { File.WriteAllText(Report, "FAIL: " + error); Debug.LogException(error); }
    }

    [MenuItem("Tools/Civil Craft/Fit Authored Settings Account Page")]
    public static void Author()
    {
        Check(!EditorApplication.isCompiling && !EditorApplication.isUpdating && !EditorApplication.isPlayingOrWillChangePlaymode,
            "Stop Play Mode and wait for imports/compilation before authoring Settings Account.");
        Check(DirtyTarget() == null, "Save all target scene edits before authoring Settings Account.");
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/Bekind Sans SDF.asset");
        Sprite rounded = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Elements/UI/LevelResultRounded.png");
        Check(font != null && rounded != null, "Existing Bekind font or rounded card art is missing.");
        foreach (string path in Scenes) Check(AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null, "Missing build scene: " + path);
        Scene active = SceneManager.GetActiveScene();
        var report = new StringBuilder();
        try
        {
            foreach (string path in Scenes)
            {
                Scene scene = SceneManager.GetSceneByPath(path);
                bool opened = !scene.IsValid() || !scene.isLoaded;
                if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                Undo.IncrementCurrentGroup();
                int undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Fit Settings Account in " + scene.name);
                bool saved = false;
                try
                {
                    SettingsManager[] managers = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<SettingsManager>(true)).ToArray();
                    Check(managers.Length > 0, "SettingsManager missing in " + path);
                    foreach (SettingsManager manager in managers)
                    {
                        Check(manager.settingsPanel != null, "Settings panel missing in " + path);
                        Dictionary<Component, string> originalCallbacks = CaptureCallbacks(manager.settingsPanel);
                        string managerReferences = EditorJsonUtility.ToJson(manager);
                        Undo.RegisterFullObjectHierarchyUndo(manager.settingsPanel, "Fit Authored Account Controls");
                        Build(manager, font, rounded);
                        ValidateReferences(manager);
                        Check(managerReferences == EditorJsonUtility.ToJson(manager), "SettingsManager serialized references changed in " + path);
                        foreach (var pair in originalCallbacks)
                            Check(EventJson(pair.Key) == pair.Value, "Existing settings callback changed: " + pair.Key.name);
                        if (path == Scenes[0]) ValidatePreview(manager, report);
                    }
                    Undo.FlushUndoRecordObjects();
                    EditorSceneManager.MarkSceneDirty(scene);
                    Check(EditorSceneManager.SaveScene(scene), "Unity could not save authored Settings Account in " + path);
                    saved = true;
                    Check(!scene.isDirty, "Unity left Settings Account unsaved in " + path);
                    foreach (SettingsManager manager in managers) ValidateReferences(manager);
                    report.AppendLine("PASS: " + path + " saved with authored Account header, cream status card, separated action row and unchanged existing settings/auth references and callbacks.");
                    File.WriteAllText(Report, report.ToString());
                }
                catch { if (!saved) Undo.RevertAllDownToGroup(undoGroup); throw; }
                finally
                {
                    // If a native save fails, leave its scene open for recovery.
                    if (opened && scene.IsValid() && scene.isLoaded && !scene.isDirty) EditorSceneManager.CloseScene(scene, true);
                }
            }
            report.AppendLine("NOTE: Only authored layout and disposable preview text were changed. No sign in, sign out, PlayerPrefs, purchases or player save actions ran.");
            File.WriteAllText(Report, report.ToString());
            Debug.Log("[Settings account] Authored responsive Account layout saved in all five build scenes. Detached text/state/layout checks passed.");
        }
        finally { if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active); }
    }

    private static void Build(SettingsManager manager, TMP_FontAsset font, Sprite rounded)
    {
        GameObject panel = manager.settingsPanel;
        CozyModalLayout layout = panel.GetComponent<CozyModalLayout>();
        Check(layout != null && layout.kind == CozyModalLayout.PanelKind.Settings && layout.card != null,
            "Existing authored Cozy Settings card is missing in " + manager.gameObject.scene.name);
        RectTransform account = CozyModalLayout.Find(layout.card, "AccountPage");
        Check(account != null && manager.accountStatusText != null && manager.accountActionButton != null && manager.logoutButton != null,
            "Existing Account controls are missing; nothing was rebuilt.");
        Check(manager.accountStatusText.transform.parent == account && manager.accountActionButton.transform.parent.name == "AccountActions" &&
            manager.logoutButton.transform.parent == manager.accountActionButton.transform.parent,
            "Existing Account hierarchy differs from its authored page; nothing was replaced.");

        GameObject frameObject = Child(account, "Account Status Frame");
        Image frame = Ensure<Image>(frameObject);
        frame.sprite = rounded; frame.type = Image.Type.Sliced; frame.color = CozyModalLayout.Cream; frame.raycastTarget = false;
        Outline border = Ensure<Outline>(frameObject);
        border.effectColor = CozyModalLayout.Ink; border.effectDistance = new Vector2(2, -2);
        frameObject.transform.SetAsFirstSibling();
        Label(account, "Account Section Heading", "ACCOUNT & CLOUD SAVES", font, 28);
        Label(account, "Account Save Guide", "Your saved progress stays on this device.", font, 22);
        foreach (Button button in new[] { manager.accountActionButton, manager.logoutButton })
        {
            Outline outline = Ensure<Outline>(button.gameObject);
            outline.effectColor = CozyModalLayout.Ink; outline.effectDistance = new Vector2(2, -2);
        }
        layout.ApplyLayout();
        foreach (Component component in account.GetComponentsInChildren<Component>(true)) EditorUtility.SetDirty(component);
        EditorUtility.SetDirty(layout);
    }

    private static void ValidateReferences(SettingsManager manager)
    {
        CozyModalLayout layout = manager.settingsPanel.GetComponent<CozyModalLayout>();
        RectTransform account = CozyModalLayout.Find(layout.card, "AccountPage");
        Check(account != null && CozyModalLayout.Find(account, "Account Status Frame") != null &&
            CozyModalLayout.Find(account, "Account Section Heading") != null && CozyModalLayout.Find(account, "Account Save Guide") != null,
            "Authored account decoration is missing.");
        Check(manager.accountStatusText.transform.IsChildOf(account) && manager.accountActionButton.transform.IsChildOf(account) &&
            manager.logoutButton.transform.IsChildOf(account), "Settings references no longer belong to their authored Account page.");
        Check(account.GetComponent<VerticalLayoutGroup>() != null && !account.GetComponent<VerticalLayoutGroup>().enabled &&
            !manager.accountActionButton.transform.parent.GetComponent<HorizontalLayoutGroup>().enabled,
            "Fixed account layout groups still override responsive RectTransforms.");
        foreach (Component component in new Component[] { manager.accountStatusText, manager.accountActionButton, manager.logoutButton })
            Check(component.GetComponent<LayoutElement>().ignoreLayout, "Original fixed sizing remains active on " + component.name);
        Check(manager.accountStatusText.enableAutoSizing && manager.accountStatusText.enableWordWrapping && manager.accountStatusText.richText,
            "Account status cannot fit multiline names/sync state.");
        Check(manager.logoutConfirmation != null && !manager.logoutConfirmation.gameObject.activeSelf,
            "Existing authored logout confirmation must remain inactive and referenced.");
    }

    private static void ValidatePreview(SettingsManager source, StringBuilder report)
    {
        Scene scene = EditorSceneManager.NewPreviewScene(); RenderTexture target = null;
        try
        {
            var canvasObject = new GameObject("Settings Account Preview (Temporary)", typeof(RectTransform), typeof(Canvas));
            SceneManager.MoveGameObjectToScene(canvasObject, scene);
            Canvas canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
            GameObject copy = Object.Instantiate(source.settingsPanel, canvasObject.transform, false);
            foreach (Transform node in canvasObject.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = 30;
            Check(copy.GetComponentsInChildren<PlayFabAuthManager>(true).Length == 0, "Preview unexpectedly contains an auth controller.");
            foreach (SettingsManager manager in copy.GetComponentsInChildren<SettingsManager>(true)) manager.enabled = false;
            CozyModalLayout layout = copy.GetComponent<CozyModalLayout>();
            RectTransform account = CozyModalLayout.Find(layout.card, "AccountPage");
            SettingsTabController tabs = copy.GetComponentInChildren<SettingsTabController>(true);
            Check(tabs != null && tabs.tabPanels.Length == 4, "Authored Settings tab controller is missing.");
            tabs.enabled = false; tabs.SwitchTab(3); copy.SetActive(true);
            ValidateFirstOpenTabs(tabs, account, scene, report);
            TMP_Text status = CozyModalLayout.Find(account, "AccountStatusText").GetComponent<TMP_Text>();
            Button signIn = CozyModalLayout.Find(account, "AccountActionButton").GetComponent<Button>();
            Button logout = CozyModalLayout.Find(account, "LogoutButton").GetComponent<Button>();
            TMP_Text actionLabel = signIn.GetComponentInChildren<TMP_Text>(true);
            var cameraObject = new GameObject("Settings Account Preview Camera (Temporary)", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            Camera camera = cameraObject.GetComponent<Camera>(); camera.scene = scene; camera.enabled = false;
            camera.orthographic = true; camera.transform.position = new Vector3(0, 0, -1000);
            camera.nearClipPlane = .1f; camera.farClipPlane = 2000; camera.cullingMask = 1 << 30;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(148, 113, 85, 255); canvas.worldCamera = camera;
            int callbacks = 0;
            // Detached callbacks only. The original scene callbacks are validated
            // separately and never invoked by the rendering test.
            signIn.onClick = new Button.ButtonClickedEvent(); signIn.onClick.AddListener(() => callbacks++);
            logout.onClick = new Button.ButtonClickedEvent(); logout.onClick.AddListener(() => callbacks++);
            foreach (Vector2Int size in new[] { new Vector2Int(1920, 1080), new Vector2Int(1920, 1200), new Vector2Int(2340, 1080), new Vector2Int(1280, 720) })
            {
                canvasRect.sizeDelta = size; camera.orthographicSize = size.y/2f; camera.aspect = (float)size.x/size.y;
                target = new RenderTexture(size.x, size.y, 24); target.Create(); camera.targetTexture = target;
                status.text = "Signed in as <b>hyakkimaru</b>\nProgress saves on this device and syncs online when available.";
                actionLabel.text = "SWITCH ACCOUNT"; signIn.interactable = logout.interactable = true;
                Fit(layout, account);
                Capture(camera, target, "Temp/SettingsAccount_" + size.x + "x" + size.y + ".png");
                status.text = "Playing as <b>Guest</b>\nProgress is stored locally on this device.";
                actionLabel.text = "SIGN IN"; Fit(layout, account);
                status.text = "Not signed in\nSign in to use your PlayFab account.";
                logout.interactable = false; Fit(layout, account);
                status.text = "Signed in as <b>An Engineer With A Long Display Name For A Narrow Screen</b>\nOnline sync pending. Progress is safe on this device.";
                actionLabel.text = "MANAGE IN MAIN MENU"; signIn.interactable = false; Fit(layout, account);
                Release(target); target = null;
                report.AppendLine("PASS: " + size + " actual authored Account clone fits signed-in, guest, signed-out and long-name/menu-managed states with no overflowing text or overlapping controls.");
            }
            Check(callbacks == 0, "Preview unexpectedly invoked account callbacks.");
        }
        finally { Release(target); EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static void ValidateFirstOpenTabs(SettingsTabController tabs, RectTransform account, Scene scene, StringBuilder report)
    {
        MethodInfo start = typeof(SettingsTabController).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic);
        Check(start != null, "Settings tab Start callback was not found.");
        Check(tabs.tabButtons != null && tabs.tabButtons.Length == 4 && tabs.tabButtons.All(button => button != null),
            "Actual authored Settings tab buttons are missing.");
        foreach (Button button in tabs.tabButtons) button.onClick = new Button.ButtonClickedEvent();
        // SettingsManager.OpenAccountSettings opens the parent and selects its
        // Account page before the initially inactive tabs receive Start. Sample
        // that exact ordering without calling OpenSettings/LoadSettings.
        Check(tabs.tabPanels[3] == account.gameObject, "Actual Account page does not occupy its routed tab.");
        tabs.SwitchTab(3); start.Invoke(tabs, null);
        CheckSelected(tabs, 3, "First Start reset an explicitly selected Account page.");
        tabs.tabButtons[1].onClick.Invoke();
        CheckSelected(tabs, 1, "Detached authored Audio tab listener does not navigate.");
        tabs.tabButtons[3].onClick.Invoke();
        CheckSelected(tabs, 3, "Detached authored Account tab listener does not navigate.");

        var defaultObject = new GameObject("Default Settings Tabs Fixture (Temporary)");
        defaultObject.SetActive(false); SceneManager.MoveGameObjectToScene(defaultObject, scene);
        SettingsTabController defaultTabs = defaultObject.AddComponent<SettingsTabController>(); defaultTabs.enabled = false;
        defaultTabs.tabPanels = new GameObject[4]; defaultTabs.tabButtons = new Button[4];
        defaultTabs.activeTabColor = tabs.activeTabColor; defaultTabs.inactiveTabColor = tabs.inactiveTabColor;
        for (int index = 0; index < 4; index++)
        {
            var page = new GameObject("Detached Page " + index, typeof(RectTransform));
            SceneManager.MoveGameObjectToScene(page, scene);
            page.transform.SetParent(defaultObject.transform, false); defaultTabs.tabPanels[index] = page;
            var buttonObject = new GameObject("Detached Tab " + index, typeof(RectTransform), typeof(Image), typeof(Button));
            SceneManager.MoveGameObjectToScene(buttonObject, scene);
            buttonObject.transform.SetParent(defaultObject.transform, false);
            defaultTabs.tabButtons[index] = buttonObject.GetComponent<Button>();
        }
        start.Invoke(defaultTabs, null);
        CheckSelected(defaultTabs, 0, "Unselected Settings first Start no longer defaults to Graphics.");
        defaultTabs.tabButtons[3].onClick.Invoke();
        CheckSelected(defaultTabs, 3, "Default tabs did not wire their detached Account listener.");
        Object.DestroyImmediate(defaultObject);
        report.AppendLine("PASS: Disabled, detached Settings tabs retain Account through first Start, default to Graphics only when not explicitly selected, and navigate Audio/Account through safe local button events. OpenSettings/auth/preferences were never invoked.");
    }

    private static void CheckSelected(SettingsTabController tabs, int selected, string message)
    {
        for (int index = 0; index < tabs.tabPanels.Length; index++)
        {
            Check(tabs.tabPanels[index].activeSelf == (index == selected), message);
            Image image = tabs.tabButtons[index].GetComponent<Image>();
            Check(image != null && image.color == (index == selected ? tabs.activeTabColor : tabs.inactiveTabColor),
                message + " Highlight is incorrect.");
        }
    }

    private static void Fit(CozyModalLayout layout, RectTransform account)
    {
        layout.ApplyLayout(); Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(layout.card); Canvas.ForceUpdateCanvases();
        RectTransform frame = CozyModalLayout.Find(account, "Account Status Frame");
        TMP_Text status = CozyModalLayout.Find(account, "AccountStatusText").GetComponent<TMP_Text>();
        foreach (TMP_Text text in account.GetComponentsInChildren<TMP_Text>())
        {
            text.ForceMeshUpdate(); Check(!text.isTextOverflowing, "Account text overflows: " + text.name + " / " + text.text);
            Inside(text.rectTransform, account, "Account text leaves its page: " + text.name);
        }
        Inside(status.rectTransform, frame, "Account status leaves its authored card.");
        Inside(layout.card, layout.card.parent as RectTransform, "Settings card leaves the viewport.");
        RectTransform actions = CozyModalLayout.Find(account, "AccountActions");
        Inside(frame, account, "Status frame leaves Account."); Inside(actions, account, "Actions leave Account.");
        Check(!Intersects(frame, actions, account), "Account status card overlaps its actions.");
        Button[] buttons = actions.GetComponentsInChildren<Button>();
        Check(buttons.Length == 2, "Account action count changed.");
        Check(!Intersects(buttons[0].transform as RectTransform, buttons[1].transform as RectTransform, account), "Account action buttons overlap.");
        foreach (Button button in buttons)
        {
            Inside(button.transform as RectTransform, actions, "Account action leaves its row.");
            Inside(button.GetComponentInChildren<TMP_Text>(true).rectTransform, button.transform as RectTransform, "Account button label leaves its action.");
        }
    }

    private static Rect LocalBounds(RectTransform child, RectTransform owner)
    {
        Vector3[] corners = new Vector3[4]; child.GetWorldCorners(corners);
        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity), max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        foreach (Vector3 corner in corners) { Vector2 point = owner.InverseTransformPoint(corner); min = Vector2.Min(min, point); max = Vector2.Max(max, point); }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }
    private static void Inside(RectTransform child, RectTransform owner, string message)
    {
        Rect bounds = LocalBounds(child, owner), available = owner.rect;
        Check(bounds.xMin >= available.xMin-.2f && bounds.xMax <= available.xMax+.2f &&
            bounds.yMin >= available.yMin-.2f && bounds.yMax <= available.yMax+.2f, message);
    }
    private static bool Intersects(RectTransform a, RectTransform b, RectTransform owner) => LocalBounds(a, owner).Overlaps(LocalBounds(b, owner));
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
    private static string DirtyTarget()
    {
        foreach (string path in Scenes) { Scene scene = SceneManager.GetSceneByPath(path); if (scene.IsValid() && scene.isLoaded && scene.isDirty) return path; }
        return null;
    }
    private static Dictionary<Component, string> CaptureCallbacks(GameObject root)
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
    private static GameObject Child(Transform parent, string name)
    {
        Transform existing = parent.Find(name); if (existing != null) return existing.gameObject;
        var obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        obj.layer = parent.gameObject.layer;
        if (obj.scene != parent.gameObject.scene) SceneManager.MoveGameObjectToScene(obj, parent.gameObject.scene);
        obj.transform.SetParent(parent, false); Undo.RegisterCreatedObjectUndo(obj, "Create Authored Account Decoration"); return obj;
    }
    private static T Ensure<T>(GameObject obj) where T : Component
    {
        T component = obj.GetComponent<T>(); if (component == null) component = Undo.AddComponent<T>(obj); return component;
    }
    private static void Label(Transform parent, string name, string value, TMP_FontAsset font, float size)
    {
        TMP_Text text = Ensure<TextMeshProUGUI>(Child(parent, name));
        text.text = value; text.font = font; text.fontSharedMaterial = font.material;
        text.fontSize = size; text.color = CozyModalLayout.Ink; text.raycastTarget = false;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
#endif
