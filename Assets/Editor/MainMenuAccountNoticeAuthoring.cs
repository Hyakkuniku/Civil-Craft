using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Opt-in authored notice. Never authenticates, signs out or changes player data.</summary>
public static class MainMenuAccountNoticeAuthoring
{
    private const string ScenePath = "Assets/Scenes/Main Menu.unity";
    private const string Request = "Temp/main-menu-account-notice-v1.request";
    private const string Report = "Temp/MainMenuAccountNoticeValidation.txt";
    private const string Title = "ALREADY LOGGED IN";
    private const string Message = "You're already logged in.\nWhat would you like to do?";
    private static readonly Color Wood = new Color32(90, 55, 31, 255);
    private static readonly Color Ink = new Color32(77, 53, 36, 255);
    private static readonly Color Cream = new Color32(248, 233, 204, 255);
    private static readonly Color Gold = new Color32(231, 158, 35, 255);
    private static readonly Color Sand = new Color32(239, 219, 177, 255);
    private static readonly Color Red = new Color32(167, 65, 49, 255);
    private static readonly BindingFlags PrivateFields = BindingFlags.Instance | BindingFlags.NonPublic;
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

    private static void CheckRequest()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode) return;
        Scene loaded = SceneManager.GetSceneByPath(ScenePath);
        if (loaded.IsValid() && loaded.isLoaded && loaded.isDirty)
        {
            File.WriteAllText(Report, "WAIT: Save your Main Menu scene edits first. No authored changes were made.");
            return;
        }
        File.Delete(Request);
        try { Author(); }
        catch (Exception error)
        {
            File.WriteAllText(Report, "FAIL: " + error);
            Debug.LogException(error);
        }
    }

    [MenuItem("Tools/Civil Craft/Author Main Menu Account Notice")]
    public static void Author()
    {
        Check(!EditorApplication.isCompiling && !EditorApplication.isUpdating && !EditorApplication.isPlayingOrWillChangePlaymode,
            "Stop Play Mode and wait for imports/compilation before authoring the account notice.");
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        Check(!scene.IsValid() || !scene.isLoaded || !scene.isDirty, "Save Main Menu scene edits before authoring the account notice.");
        Check(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null, "Main Menu scene is missing.");
        rounded = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Elements/UI/LevelResultRounded.png");
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/Bekind Sans SDF.asset");
        Check(rounded != null && font != null, "The existing rounded UI sprite or Bekind font is missing.");
        Scene active = SceneManager.GetActiveScene();
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Author Main Menu Already Logged In Notice");
        bool saved = false;
        try
        {
            MainMenuUIController controller = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<MainMenuUIController>(true)).Single();
            var controllerData = new SerializedObject(controller);
            RectTransform dropdown = controllerData.FindProperty("dropdownPanel").objectReferenceValue as RectTransform;
            Check(dropdown != null, "Main Menu dropdown reference is missing.");
            Canvas menuCanvas = dropdown.GetComponentInParent<Canvas>(true);
            Check(menuCanvas != null, "Main Menu dropdown Canvas is missing.");
            menuCanvas = menuCanvas.rootCanvas;
            string originalController = ControllerConfiguration(controller);
            Dictionary<Button, string> originalEvents = menuCanvas.GetComponentsInChildren<Button>(true)
                .Where(button => button.GetComponentInParent<AuthoredNoticeDialog>(true) == null)
                .ToDictionary(button => button, button => JsonUtility.ToJson(button.onClick));
            Undo.RegisterFullObjectHierarchyUndo(menuCanvas.gameObject, "Author Account Notice");
            Undo.RegisterCompleteObjectUndo(controller, "Wire Account Notice");
            recordingUndo = true;
            AuthoredNoticeDialog notice = Build(menuCanvas.transform);
            recordingUndo = false;
            controllerData.Update();
            controllerData.FindProperty("alreadyLoggedInNotice").objectReferenceValue = notice;
            controllerData.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(controller);
            ValidateReferences(controller, notice, menuCanvas, dropdown);
            Check(ControllerConfiguration(controller) == originalController, "Existing Main Menu/authentication configuration changed.");
            foreach (var pair in originalEvents)
                Check(pair.Key != null && JsonUtility.ToJson(pair.Key.onClick) == pair.Value,
                    "An existing Main Menu callback changed: " + (pair.Key != null ? pair.Key.name : "deleted"));
            Undo.FlushUndoRecordObjects();
            EditorSceneManager.MarkSceneDirty(scene);
            Check(EditorSceneManager.SaveScene(scene), "Unity could not save the authored account notice in Main Menu.");
            saved = true;
            Check(!scene.isDirty, "Main Menu remains unsaved after authoring its account notice.");
            ValidateReferences(controller, notice, menuCanvas, dropdown);
            var report = new StringBuilder();
            report.AppendLine("PASS: Main Menu saved with an inactive scene-authored AlreadyLoggedInNotice, inherited scaling, full-screen dim blocker, cream/brown/gold frame and Stay/Switch Account/Log Out buttons. Existing authentication/slide/unlock configuration and button callbacks were preserved.");
            Preview(notice, report);
            report.AppendLine("NOTE: All Show/Stay/action/reopen and controller-route checks used disposable preview objects/delegates only. No real authentication, logout, PlayerPrefs or save operations occurred.");
            File.WriteAllText(Report, report.ToString());
            Debug.Log("[Main Menu account notice] Saved authored Already Logged In panel; isolated controller route, Stay/actions/reopen, text fitting and three-size layout checks passed.");
        }
        catch
        {
            recordingUndo = false;
            if (!saved) Undo.RevertAllDownToGroup(undoGroup);
            throw;
        }
        finally
        {
            recordingUndo = false;
            if (opened && scene.IsValid() && scene.isLoaded && !scene.isDirty) EditorSceneManager.CloseScene(scene, true);
            if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
        }
    }

    private static AuthoredNoticeDialog Build(Transform parent)
    {
        Transform existing = parent.Find("AlreadyLoggedInNotice");
        if (existing != null)
            Check(existing.GetComponent<AuthoredNoticeDialog>() != null, "An unrelated AlreadyLoggedInNotice exists; it was preserved.");
        GameObject root = existing != null ? existing.gameObject : Child(parent, "AlreadyLoggedInNotice");
        root.SetActive(false);
        Place(root.transform as RectTransform, 0, 0, 1, 1);
        root.transform.SetAsLastSibling();
        Canvas canvas = Ensure<Canvas>(root);
        Ensure<GraphicRaycaster>(root);
        Check(root.GetComponent<CanvasScaler>() == null, "The notice must inherit the Main Menu CanvasScaler.");
        Image dim = Ensure<Image>(root);
        dim.sprite = null; dim.type = Image.Type.Simple; dim.color = new Color(.08f, .055f, .035f, .62f); dim.raycastTarget = true;
        CanvasGroup group = Ensure<CanvasGroup>(root);
        group.alpha = 1; group.interactable = group.blocksRaycasts = true;
        Image frame = Surface(root.transform, "Account Notice Wood Frame", Wood, true);
        Place(frame.rectTransform, .26f, .26f, .74f, .74f);
        Image face = Surface(frame.transform, "Account Notice Cream Surface", Cream, true);
        Place(face.rectTransform, 0, 0, 1, 1); face.rectTransform.sizeDelta = new Vector2(-16, -16);
        TMP_Text title = Label(face.transform, "Account Notice Title", Title, 44, 33);
        Place(title.rectTransform, .07f, .77f, .93f, .91f);
        Image divider = Surface(face.transform, "Account Notice Divider", new Color32(177, 131, 76, 255), false);
        divider.sprite = null; divider.type = Image.Type.Simple;
        Place(divider.rectTransform, .09f, .73f, .91f, .733f);
        TMP_Text body = Label(face.transform, "Account Notice Message", Message, 29, 23);
        Place(body.rectTransform, .08f, .38f, .92f, .66f);
        Transform legacyOkay = face.transform.Find("Account Notice OK");
        if (legacyOkay != null)
        {
            // This one-button layout has not shipped, but an opt-in rerun should
            // still remove only our own obsolete acknowledgement control.
            Check(legacyOkay.GetComponent<Button>() != null, "An unrelated legacy notice child exists; it was preserved.");
            if (recordingUndo) Undo.DestroyObjectImmediate(legacyOkay.gameObject);
            else Object.DestroyImmediate(legacyOkay.gameObject);
        }
        Button stay = CreateButton(face.transform, "Account Notice Stay", "STAY", Sand);
        Button switchAccount = CreateButton(face.transform, "Account Notice Switch Account", "SWITCH\nACCOUNT", Gold);
        Button logout = CreateButton(face.transform, "Account Notice Log Out", "LOG OUT", Red);
        GetLabel(logout).color = Cream;
        Place(stay.transform as RectTransform, .07f, .115f, .31f, .29f);
        Place(switchAccount.transform as RectTransform, .34f, .115f, .66f, .29f);
        Place(logout.transform as RectTransform, .69f, .115f, .93f, .29f);
        stay.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnRight = switchAccount, selectOnLeft = logout };
        switchAccount.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = stay, selectOnRight = logout };
        logout.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = switchAccount, selectOnRight = stay };
        AuthoredNoticeDialog notice = Ensure<AuthoredNoticeDialog>(root);
        notice.SetAuthoringReferences(title, body, stay, switchAccount, logout);
        AuthoredUIReveal reveal = Ensure<AuthoredUIReveal>(root);
        var revealData = new SerializedObject(reveal);
        revealData.FindProperty("visual").objectReferenceValue = frame.rectTransform;
        revealData.FindProperty("group").objectReferenceValue = group;
        revealData.FindProperty("duration").floatValue = .18f;
        revealData.FindProperty("startScale").floatValue = .985f;
        revealData.ApplyModifiedPropertiesWithoutUndo();
        // Inactive nested Canvas getters may expose registration defaults. Write
        // the authored serialized values after TMP/required-component creation.
        var canvasData = new SerializedObject(canvas);
        canvasData.FindProperty("m_OverrideSorting").boolValue = true;
        canvasData.FindProperty("m_SortingOrder").intValue = 32000;
        canvasData.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(canvas); EditorUtility.SetDirty(notice); EditorUtility.SetDirty(reveal);
        return notice;
    }

    private static void ValidateReferences(MainMenuUIController controller, AuthoredNoticeDialog notice, Canvas canvas, RectTransform dropdown)
    {
        Check(notice != null && !notice.gameObject.activeSelf && notice.gameObject.scene == controller.gameObject.scene,
            "Account notice is missing, active in the saved scene, or belongs to another scene.");
        Check(notice.transform.parent == canvas.transform && !notice.transform.IsChildOf(dropdown),
            "Account notice must be a Main Menu Canvas child outside dropdown fading/sliding.");
        RectTransform rect = notice.transform as RectTransform;
        Check(rect != null && rect.anchorMin == Vector2.zero && rect.anchorMax == Vector2.one &&
            rect.sizeDelta == Vector2.zero && rect.anchoredPosition == Vector2.zero,
            "Account notice blocker does not fill its Canvas.");
        Canvas nested = notice.GetComponent<Canvas>();
        Check(nested != null && notice.GetComponent<GraphicRaycaster>() != null && notice.GetComponent<CanvasScaler>() == null,
            "Account notice sorting/raycasting or inherited scaling is missing.");
        var canvasData = new SerializedObject(nested);
        Check(canvasData.FindProperty("m_OverrideSorting").boolValue && canvasData.FindProperty("m_SortingOrder").intValue == 32000,
            "Account notice serialized Canvas order is incorrect.");
        Check(notice.GetComponent<Image>().raycastTarget && notice.GetComponent<CanvasGroup>().blocksRaycasts,
            "Account notice does not block underlying menu buttons.");
        foreach (string field in new[] { "titleText", "messageText", "closeButton", "switchAccountButton", "logoutButton" })
            Check(Get<Object>(notice, field) != null, "Account notice reference is missing: " + field);
        Button stay = Get<Button>(notice, "closeButton"), switchAccount = Get<Button>(notice, "switchAccountButton"), logout = Get<Button>(notice, "logoutButton");
        Check(stay != switchAccount && stay != logout && switchAccount != logout,
            "Account notice actions share the same control.");
        foreach (Button button in new[] { stay, switchAccount, logout })
            Check(button.onClick.GetPersistentEventCount() == 0, "Account notice contains stale persistent callbacks.");
        Check(stay.navigation.mode == Navigation.Mode.Explicit && stay.navigation.selectOnRight == switchAccount &&
            switchAccount.navigation.selectOnLeft == stay && switchAccount.navigation.selectOnRight == logout &&
            logout.navigation.selectOnLeft == switchAccount, "Account notice action navigation is not authored.");
        var data = new SerializedObject(controller);
        Check(data.FindProperty("alreadyLoggedInNotice").objectReferenceValue == notice,
            "Main Menu's authored account notice reference was not serialized.");
        SettingsManager settings = data.FindProperty("settingsManager").objectReferenceValue as SettingsManager;
        Check(settings != null && settings.logoutConfirmation != null && settings.accountStatusText != null &&
            data.FindProperty("authManager").objectReferenceValue != null,
            "Account action routes are missing the existing authentication/Settings/logout references.");
        var reveal = new SerializedObject(notice.GetComponent<AuthoredUIReveal>());
        Check(reveal.FindProperty("visual").objectReferenceValue != null && reveal.FindProperty("group").objectReferenceValue != null,
            "Account notice reveal references were not authored.");
    }

    private static string ControllerConfiguration(MainMenuUIController controller)
    {
        return Regex.Replace(JsonUtility.ToJson(controller), "\"alreadyLoggedInNotice\":\\{[^}]*\\}", "\"alreadyLoggedInNotice\":null");
    }

    private static void Preview(AuthoredNoticeDialog source, StringBuilder report)
    {
        Scene preview = EditorSceneManager.NewPreviewScene();
        RenderTexture target = null;
        try
        {
            var canvasObject = new GameObject("Account Notice Preview (Temporary)", typeof(RectTransform), typeof(Canvas));
            SceneManager.MoveGameObjectToScene(canvasObject, preview);
            Canvas canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
            GameObject copy = Object.Instantiate(source.gameObject, canvasObject.transform, false);
            AuthoredNoticeDialog notice = copy.GetComponent<AuthoredNoticeDialog>();
            Button stay = Get<Button>(notice, "closeButton");
            Button switchButton = Get<Button>(notice, "switchAccountButton");
            Button logoutButton = Get<Button>(notice, "logoutButton");
            var controllerObject = new GameObject("Account Notice Controller Fixture (Temporary)");
            controllerObject.SetActive(false); SceneManager.MoveGameObjectToScene(controllerObject, preview);
            MainMenuUIController fixture = controllerObject.AddComponent<MainMenuUIController>(); fixture.enabled = false;
            typeof(MainMenuUIController).GetField("alreadyLoggedInNotice", PrivateFields).SetValue(fixture, notice);
            GameObject fakeDropdown = Child(canvasObject.transform, "Account Notice Dropdown Fixture (Temporary)");
            RectTransform fakeRect = fakeDropdown.transform as RectTransform;
            Place(fakeRect, .2f, .2f, .8f, .8f); fakeRect.anchoredPosition = new Vector2(12, 34);
            CanvasGroup fakeGroup = fakeDropdown.AddComponent<CanvasGroup>();
            fakeGroup.alpha = .45f; fakeGroup.interactable = true; fakeGroup.blocksRaycasts = true;
            typeof(MainMenuUIController).GetField("dropdownPanel", PrivateFields).SetValue(fixture, fakeRect);
            typeof(MainMenuUIController).GetField("dropdownCanvasGroup", PrivateFields).SetValue(fixture, fakeGroup);
            fixture.ShowAlreadyLoggedInNotice();
            Check(copy.activeSelf && fakeRect.anchoredPosition == new Vector2(12, 34) && fakeGroup.alpha == .45f &&
                fakeGroup.interactable && fakeGroup.blocksRaycasts && fakeDropdown.activeSelf,
                "Already-logged-in controller route changed dropdown pose/visibility/input.");
            Action switchRoute = GetAction(notice, "switchAccount"), logoutRoute = GetAction(notice, "logout");
            Check(switchRoute != null && ReferenceEquals(switchRoute.Target, fixture) && switchRoute.Method.Name == "SwitchAccountFromMenu" &&
                logoutRoute != null && ReferenceEquals(logoutRoute.Target, fixture) && logoutRoute.Method.Name == "RequestLogoutFromMenu",
                "Main Menu notice account action routes were not assigned correctly.");
            stay.onClick.Invoke();
            Check(!copy.activeSelf, "Notice Stay did not close the disposable authored notice.");
            fixture.ShowAlreadyLoggedInNotice(); fixture.ShowAlreadyLoggedInNotice();
            Check(copy.activeSelf, "Repeated notice request failed.");
            stay.onClick.Invoke();
            Check(!copy.activeSelf, "Repeated notice requests duplicated/retained their Stay callback.");
            notice.Close(); stay.onClick.Invoke(); switchButton.onClick.Invoke(); logoutButton.onClick.Invoke();
            Check(!copy.activeSelf && GetAction(notice, "switchAccount") == null && GetAction(notice, "logout") == null,
                "A stale hidden action reopened the notice or retained session-changing callbacks.");
            int switches = 0, logouts = 0, replaced = 0;
            Action onSwitch = () => { Check(!copy.activeSelf, "Notice did not close before the switch action."); switches++; };
            Action onLogout = () => { Check(!copy.activeSelf, "Notice did not close before the logout action."); logouts++; };
            Check(notice.Show(Title, Message, () => replaced++, () => replaced++), "First disposable action request failed.");
            Check(notice.Show(Title, Message, onSwitch, onLogout), "Repeated disposable action request failed.");
            switchButton.onClick.Invoke(); switchButton.onClick.Invoke(); logoutButton.onClick.Invoke();
            Check(switches == 1 && logouts == 0 && replaced == 0 && !copy.activeSelf,
                "Switch callback was duplicated, used a replaced callback or ran a stale logout action.");
            Check(notice.Show(Title, Message, onSwitch, onLogout), "Disposable logout request failed.");
            logoutButton.onClick.Invoke(); logoutButton.onClick.Invoke(); switchButton.onClick.Invoke();
            Check(switches == 1 && logouts == 1 && !copy.activeSelf,
                "Logout callback was duplicated or ran a stale switch action.");
            Check(notice.Show(Title, Message, onSwitch, onLogout), "Disposable Stay request failed.");
            stay.onClick.Invoke(); switchButton.onClick.Invoke(); logoutButton.onClick.Invoke();
            Check(switches == 1 && logouts == 1 && !copy.activeSelf, "Stay executed an account action.");
            Object.DestroyImmediate(fakeDropdown);
            report.AppendLine("PASS: Disposable MainMenuUIController notice route leaves dropdown pose/group/input unchanged, assigns the intended switch/logout methods and Stay closes safely. Disposable replacement/reopen action delegates run exactly once after the panel hides; Stay and stale hidden clicks run no action. No actual login/logout was called.");
            foreach (Transform node in canvasObject.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = 30;
            var cameraObject = new GameObject("Account Notice Preview Camera (Temporary)", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, preview);
            Camera camera = cameraObject.GetComponent<Camera>(); camera.scene = preview; camera.enabled = false;
            camera.orthographic = true; camera.transform.position = new Vector3(0, 0, -1000);
            camera.nearClipPlane = .1f; camera.farClipPlane = 2000; camera.cullingMask = 1 << 30;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(148, 113, 85, 255);
            canvas.worldCamera = camera;
            foreach (Vector2Int size in new[] { new Vector2Int(1920, 1080), new Vector2Int(1920, 1200), new Vector2Int(2340, 1080) })
            {
                canvasRect.sizeDelta = size; camera.orthographicSize = size.y / 2f; camera.aspect = (float)size.x / size.y;
                target = new RenderTexture(size.x, size.y, 24); target.Create(); camera.targetTexture = target;
                Check(notice.Show(Title, Message, onSwitch, onLogout), "Authored notice clone could not show for rendering.");
                Fit(copy);
                AuthoredUIReveal reveal = notice.GetComponent<AuthoredUIReveal>();
                reveal.ApplyProgress(.5f);
                Check(copy.GetComponent<CanvasGroup>().alpha > 0 && copy.GetComponent<CanvasGroup>().alpha < 1,
                    "Authored account notice reveal does not animate its CanvasGroup.");
                reveal.Restore(); Fit(copy);
                Capture(camera, target, "Temp/MainMenuAccountNotice_" + size.x + "x" + size.y + ".png");
                notice.Close(); camera.targetTexture = null; Release(target); target = null;
                report.AppendLine("PASS: " + size + " authored notice clone fits its screen/card without text overflow; reveal restores its authored pose.");
            }
        }
        finally { Release(target); EditorSceneManager.ClosePreviewScene(preview); }
    }

    private static void Fit(GameObject root)
    {
        Canvas.ForceUpdateCanvases();
        RectTransform screen = root.transform as RectTransform;
        RectTransform card = root.transform.Find("Account Notice Wood Frame") as RectTransform;
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>())
        {
            text.ForceMeshUpdate(); Check(!text.isTextOverflowing, "Account notice text overflows: " + text.name);
            CheckInside(text.rectTransform, card, "Account notice text leaves its card: " + text.name);
        }
        foreach (Button button in root.GetComponentsInChildren<Button>())
            CheckInside(button.transform as RectTransform, card, "Account notice action leaves its card.");
        CheckInside(card, screen, "Account notice card leaves its screen.");
        RectTransform[] actions = root.GetComponentsInChildren<Button>().Select(button => button.transform as RectTransform).ToArray();
        for (int left = 0; left < actions.Length; left++)
            for (int right = left + 1; right < actions.Length; right++)
                Check(!new Rect(actions[left].anchorMin, actions[left].anchorMax - actions[left].anchorMin)
                    .Overlaps(new Rect(actions[right].anchorMin, actions[right].anchorMax - actions[right].anchorMin)),
                    "Account notice actions overlap.");
    }

    private static void CheckInside(RectTransform child, RectTransform owner, string message)
    {
        var corners = new Vector3[4]; child.GetWorldCorners(corners);
        foreach (Vector3 corner in corners)
        {
            Vector3 point = owner.InverseTransformPoint(corner); Rect rect = owner.rect;
            Check(point.x >= rect.xMin - .1f && point.x <= rect.xMax + .1f &&
                point.y >= rect.yMin - .1f && point.y <= rect.yMax + .1f, message);
        }
    }

    private static void Capture(Camera camera, RenderTexture target, string path)
    {
        Canvas.ForceUpdateCanvases();
        if (GraphicsSettings.currentRenderPipeline == null) camera.Render();
        else RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        RenderTexture previous = RenderTexture.active;
        var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        try
        {
            RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
    }

    private static void Release(RenderTexture target)
    {
        if (target != null) { target.Release(); Object.DestroyImmediate(target); }
    }
    private static T Get<T>(AuthoredNoticeDialog notice, string field) where T : Object =>
        (T)typeof(AuthoredNoticeDialog).GetField(field, PrivateFields).GetValue(notice);
    private static Action GetAction(AuthoredNoticeDialog notice, string field) =>
        (Action)typeof(AuthoredNoticeDialog).GetField(field, PrivateFields).GetValue(notice);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static GameObject Child(Transform parent, string name)
    {
        Transform existing = parent.Find(name);
        if (existing != null) return existing.gameObject;
        var obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        obj.layer = parent.gameObject.layer;
        if (obj.scene != parent.gameObject.scene) SceneManager.MoveGameObjectToScene(obj, parent.gameObject.scene);
        obj.transform.SetParent(parent, false);
        if (recordingUndo) Undo.RegisterCreatedObjectUndo(obj, "Create Authored Account Notice Control");
        return obj;
    }
    private static T Ensure<T>(GameObject obj) where T : Component
    {
        T component = obj.GetComponent<T>();
        if (component == null) component = recordingUndo ? Undo.AddComponent<T>(obj) : obj.AddComponent<T>();
        return component;
    }
    private static Image Surface(Transform parent, string name, Color fill, bool raycast)
    {
        Image image = Ensure<Image>(Child(parent, name));
        image.sprite = rounded; image.type = Image.Type.Sliced; image.color = fill; image.raycastTarget = raycast;
        return image;
    }
    private static TMP_Text Label(Transform parent, string name, string value, float maximum, float minimum)
    {
        TMP_Text text = Ensure<TextMeshProUGUI>(Child(parent, name));
        text.font = font; text.fontSharedMaterial = font.material; text.text = value; text.color = Ink;
        text.fontStyle = FontStyles.Normal; text.alignment = TextAlignmentOptions.Center; text.characterSpacing = 0;
        text.fontSize = text.fontSizeMax = maximum; text.fontSizeMin = minimum; text.enableAutoSizing = true;
        text.raycastTarget = false; text.richText = false; text.margin = Vector4.zero; text.enableWordWrapping = true;
        text.overflowMode = TextOverflowModes.Overflow;
        return text;
    }
    private static Button CreateButton(Transform parent, string name, string value, Color fill)
    {
        GameObject obj = Child(parent, name);
        Image frame = Ensure<Image>(obj); frame.sprite = rounded; frame.type = Image.Type.Sliced; frame.color = Wood; frame.raycastTarget = true;
        Image face = Surface(obj.transform, "Button Face", fill, false);
        Place(face.rectTransform, 0, 0, 1, 1); face.rectTransform.sizeDelta = new Vector2(-8, -8); face.transform.SetAsFirstSibling();
        Button button = Ensure<Button>(obj); button.targetGraphic = face; button.transition = Selectable.Transition.ColorTint;
        button.onClick = new Button.ButtonClickedEvent();
        ColorBlock colors = button.colors; colors.normalColor = Color.white;
        colors.highlightedColor = colors.selectedColor = new Color(1, .95f, .82f);
        colors.pressedColor = new Color(.85f, .76f, .61f); button.colors = colors;
        TMP_Text label = Label(obj.transform, "Button Label", value, 30, 23);
        Place(label.rectTransform, .04f, .07f, .96f, .93f); label.transform.SetAsLastSibling();
        return button;
    }
    private static TMP_Text GetLabel(Button button) => button.GetComponentInChildren<TMP_Text>(true);
    private static void Place(RectTransform rect, float x0, float y0, float x1, float y1)
    {
        rect.anchorMin = new Vector2(x0, y0); rect.anchorMax = new Vector2(x1, y1); rect.pivot = Vector2.one * .5f;
        rect.anchoredPosition = rect.sizeDelta = Vector2.zero; rect.localScale = Vector3.one; rect.localRotation = Quaternion.identity;
    }
}
