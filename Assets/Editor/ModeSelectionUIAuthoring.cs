using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Explicit, one-shot editor authoring. Nothing creates or styles UI at runtime.</summary>
public static class ModeSelectionUIAuthoring
{
    private const string ScenePath = "Assets/Scenes/Mode Selection.unity";
    private const string Request = "Temp/mode-selection-ui-authoring.request";
    private static readonly Color Wood = new Color32(90, 55, 31, 255);
    private static readonly Color Ink = new Color32(77, 53, 36, 255);
    private static readonly Color Cream = new Color32(248, 233, 204, 255);
    private static readonly Color Paper = new Color32(255, 248, 231, 255);
    private static readonly Color Gold = new Color32(231, 158, 35, 255);
    private static Sprite rounded;
    private static TMP_FontAsset font;
    private static double nextCheck;

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
        if (!File.Exists(Request) || EditorApplication.isPlayingOrWillChangePlaymode ||
            EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        File.Delete(Request);
        try { Author(); }
        catch (Exception error)
        {
            File.WriteAllText("Temp/ModeSelectionUIValidation.txt", "FAIL: " + error);
            Debug.LogException(error);
        }
    }
    [MenuItem("Tools/Civil Craft/Author Mode Selection Theme (Join Only)")]
    public static void Author()
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (!opened && scene.isDirty)
            throw new InvalidOperationException("Save your current Mode Selection edits before authoring; no scene was overwritten.");
        if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            rounded = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Elements/UI/LevelResultRounded.png");
            var manager = All<ModeSelectionManager>(scene).Single();
            var data = new SerializedObject(manager);
            TMP_Text heading = Ref<TMP_Text>(data, "modeHeading");
            TMP_Text body = Ref<TMP_Text>(data, "modeDescription");
            font = heading.font;
            var layout = Find(scene, "Mode Selection Layout");
            var headingPanel = Find(scene, "Mode Heading");
            Frame(headingPanel); Rect(headingPanel, .32f, .84f, .68f, .96f);
            Rect(heading.gameObject, .06f, .17f, .94f, .72f); Text(heading, 44);
            var headerHint = Label(headingPanel.transform, "Mode Header Caption", "CHOOSE YOUR PATH", 19);
            Rect(headerHint.gameObject, .08f, .72f, .92f, .94f); headerHint.characterSpacing = 4;
            var card = Find(scene, "Description Card");
            Frame(card); Rect(card, .04f, .27f, .32f, .77f);
            Rect(body.gameObject, .1f, .39f, .9f, .75f); Text(body, 28);
            var kicker = Label(card.transform, "Mode Card Kicker", "YOUR ADVENTURE", 27);
            Rect(kicker.gameObject, .08f, .79f, .92f, .92f); kicker.fontStyle = FontStyles.Bold;
            var line = CreateImage(card.transform, "Mode Card Divider", new Color32(177, 131, 76, 255));
            Rect(line.gameObject, .12f, .765f, .88f, .77f);
            var hint = Label(card.transform, "Mode Card Hint", "Your world. Your bridges. Your progress.", 21);
            Rect(hint.gameObject, .1f, .045f, .9f, .135f);
            manager.modes[0].description = "Explore the canyon, meet its people, and take on <b>bridge-building contracts</b>.\n\nLearn, build, and connect the community at your own pace.";
            manager.modes[1].description = "<b>Join a friend's world</b> with their room code.\n\nExplore together and compete to build the <b>strongest, most efficient bridge</b>.";
            for (int i = 0; i < manager.modes.Length; i++)
            {
                var button = manager.modes[i].uiButton;
                ThemeButton(button, true, i == 0 ? "PLAY STORY" : "JOIN WORLD");
                Rect(button, .08f, .17f, .92f, .31f);
            }
            ThemeButton(Find(scene, "Button"), false, "MAIN MENU");
            Rect(Find(scene, "Button"), .03f, .87f, .17f, .95f);
            ThemeButton(Find(scene, "MobileSettingsButton"), false, "SETTINGS");
            Rect(Find(scene, "MobileSettingsButton"), .8f, .91f, .97f, .975f);
            ThemeButton(Find(scene, "Open Leaderboards Button"), false, "LEADERBOARDS");
            var leaderboardSafe = Find(scene, "Leaderboard Button Safe Area");
            Rect(leaderboardSafe, 0, 0, 1, 1);
            Rect(Find(scene, "Open Leaderboards Button"), .8f, .825f, .97f, .89f);
            NavigationButton(manager.previousButton, "2003672ec51804347af58385f2e67380");
            Rect(manager.previousButton, .085f, .48f, .155f, .59f);
            NavigationButton(manager.nextButton, "8e60bf4e6f32b79458f4ecb4b6e5b195");
            Rect(manager.nextButton, .845f, .48f, .915f, .59f);

            GameObject modal = Ref<GameObject>(data, "multiplayerEntryPanel");
            modal.GetComponent<Image>().color = new Color(0.08f, 0.05f, 0.035f, .52f);
            GameObject joinCard = Find(scene, "Session Choice Card");
            Frame(joinCard); Rect(joinCard, .27f, .16f, .73f, .84f);
            var title = Find(scene, "Session Choice Title").GetComponent<TMP_Text>();
            title.text = "JOIN A WORLD"; Text(title, 40); Rect(title.gameObject, .08f, .8f, .92f, .94f);
            TMP_Text prompt = Ref<TMP_Text>(data, "multiplayerPromptText");
            prompt.text = "Enter your friend's <b>6-character room code</b> to join their world.";
            Text(prompt, 28); Rect(prompt.gameObject, .09f, .61f, .91f, .77f);
            TMP_InputField input = Ref<TMP_InputField>(data, "roomCodeInput");
            Frame(input.gameObject, Paper); Rect(input.gameObject, .13f, .43f, .87f, .56f);
            input.targetGraphic = input.transform.Find("Theme Cream Inset").GetComponent<Image>();
            input.characterLimit = 8; input.caretColor = Ink; input.customCaretColor = true;
            input.selectionColor = new Color(Gold.r, Gold.g, Gold.b, .35f);
            Text(input.textComponent, 38); input.textComponent.richText = false;
            input.textComponent.characterSpacing = 8;
            var placeholder = (TMP_Text)input.placeholder;
            Text(placeholder, 30); placeholder.text = "ROOM CODE"; placeholder.color = new Color(Ink.r, Ink.g, Ink.b, .58f);
            var inputArea = (RectTransform)input.textViewport;
            inputArea.anchorMin = Vector2.zero; inputArea.anchorMax = Vector2.one;
            inputArea.offsetMin = new Vector2(24, 10); inputArea.offsetMax = new Vector2(-24, -10);
            var joinButton = Ref<GameObject>(data, "joinButtonObject");
            ThemeButton(joinButton, true, "JOIN WORLD"); Rect(joinButton, .13f, .24f, .87f, .36f);
            var back = Find(scene, "Back Button");
            ThemeButton(back, false, "BACK"); Rect(back, .3f, .065f, .7f, .17f);
            var inputHint = Label(joinCard.transform, "Join Code Hint", "The host must have Multiplayer turned on in their world.", 21);
            Rect(inputHint.gameObject, .1f, .365f, .9f, .425f);
            GameObject hostButton = Ref<GameObject>(data, "hostButtonObject");
            if (hostButton != null) Object.DestroyImmediate(hostButton);
            data.Update();
            data.FindProperty("hostButtonObject").objectReferenceValue = null;
            data.FindProperty("hostButtonLabel").objectReferenceValue = null;
            data.FindProperty("joinOnly").boolValue = true;
            data.FindProperty("modeKicker").objectReferenceValue = kicker;
            data.FindProperty("modeHint").objectReferenceValue = hint;
            data.ApplyModifiedPropertiesWithoutUndo();
            // Defaults on disk remain Story; Join input is authored and visible
            // inside its inactive modal, not generated after clicking Join.
            input.gameObject.SetActive(true); modal.SetActive(false);
            var group = modal.GetComponent<CanvasGroup>(); group.alpha = 1; group.interactable = true; group.blocksRaycasts = true;
            ShowMode(manager, data, 0);
            EditorUtility.SetDirty(manager); EditorSceneManager.MarkSceneDirty(scene);
            Validate(scene, manager);
            EditorSceneManager.SaveScene(scene);
            var report = new StringBuilder("PASS: Authored Mode Selection theme saved. Host button removed; join-only code input, serialized references and action listeners verified. Story scene navigation and in-world hosting are untouched.\n");
            Preview(scene, manager, report);
            File.WriteAllText("Temp/ModeSelectionUIValidation.txt", report.ToString());
            Debug.Log("[Mode Selection authoring] PASS: Temp/ModeSelectionUIValidation.txt");
        }
        finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
    }

    private static void ShowMode(ModeSelectionManager manager, SerializedObject data, int mode)
    {
        for (int i = 0; i < manager.modes.Length; i++) manager.modes[i].uiButton.SetActive(i == mode);
        manager.previousButton.SetActive(mode > 0); manager.nextButton.SetActive(mode < manager.modes.Length - 1);
        Ref<TMP_Text>(data, "modeHeading").text = manager.modes[mode].modeName.ToUpperInvariant();
        Ref<TMP_Text>(data, "modeDescription").text = manager.modes[mode].description;
        Ref<TMP_Text>(data, "modeKicker").text = mode == 0 ? "YOUR ADVENTURE" : "PLAY TOGETHER";
        Ref<TMP_Text>(data, "modeHint").text = mode == 0 ? "Your world. Your bridges. Your progress."
            : "Want to host? Start Story, then turn on Multiplayer in your world.";
        var card = Ref<CanvasGroup>(data, "descriptionGroup"); card.alpha = 1;
        Rect(card.gameObject, mode == 0 ? .04f : .68f, .27f, mode == 0 ? .32f : .96f, .77f);
    }
    private static void Validate(Scene scene, ModeSelectionManager manager)
    {
        var data = new SerializedObject(manager);
        if (!data.FindProperty("joinOnly").boolValue || Ref<GameObject>(data, "hostButtonObject") != null ||
            All<Button>(scene).Any(button => button.name == "Host Game Button")) throw new Exception("Menu hosting was not removed.");
        var input = Ref<TMP_InputField>(data, "roomCodeInput");
        if (input.textComponent == null || input.textViewport == null || input.placeholder == null || input.textComponent.font == null)
            throw new Exception("Join input is missing authored text/caret references.");
        foreach (var button in All<Button>(scene))
            for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                if (button.onClick.GetPersistentMethodName(i) == "HostMultiplayer") throw new Exception("An authored event still starts menu hosting.");
        var join = Ref<GameObject>(data, "joinButtonObject").GetComponent<Button>();
        if (!Enumerable.Range(0, join.onClick.GetPersistentEventCount()).Any(i => join.onClick.GetPersistentMethodName(i) == "JoinMultiplayer"))
            throw new Exception("Join action lost its serialized listener.");
    }

    private static void Preview(Scene scene, ModeSelectionManager manager, StringBuilder report)
    {
        // Temporary editor-only camera/canvas changes are always restored and
        // are never saved. No Photon connection or PlayerPrefs mutation occurs.
        var data = new SerializedObject(manager);
        Canvas canvas = Find(scene, "Canvas").GetComponent<Canvas>();
        Camera camera = manager.mainCamera;
        var oldPosition = camera.transform.position; var oldRotation = camera.transform.rotation;
        var oldMode = canvas.renderMode; var oldCamera = canvas.worldCamera; var oldPlane = canvas.planeDistance;
        var canvasRect = (RectTransform)canvas.transform;
        var oldCanvasPosition = canvasRect.position; var oldCanvasRotation = canvasRect.rotation;
        var oldCanvasScale = canvasRect.localScale; var oldCanvasSize = canvasRect.sizeDelta;
        var oldCanvasPivot = canvasRect.pivot;
        var scaler = canvas.GetComponent<CanvasScaler>(); bool oldScalerEnabled = scaler.enabled;
        var oldTarget = camera.targetTexture; var oldAspect = camera.aspect;
        var oldMask = camera.cullingMask;
        GameObject modal = Ref<GameObject>(data, "multiplayerEntryPanel");
        RenderTexture previous = RenderTexture.active;
        try
        {
            // An offscreen editor camera does not update an Overlay canvas's
            // zero-scale authoring transform. Explicitly project the authored
            // rects as a world canvas for previews only, using the same scaler.
            canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = camera; canvas.planeDistance = 1;
            scaler.enabled = false;
            canvasRect.pivot = new Vector2(.5f, .5f);
            camera.cullingMask |= 1 << 5;
            foreach (var size in new[] { new Vector2Int(1600, 900), new Vector2Int(1280, 800) })
            {
                var target = new RenderTexture(size.x, size.y, 24); target.Create();
                camera.targetTexture = target; camera.aspect = (float)size.x / size.y;
                try
                {
                    foreach (int mode in new[] { 0, 1, 2 })
                    {
                        ShowMode(manager, data, mode == 0 ? 0 : 1); modal.SetActive(mode == 2);
                        if (mode == 2) { manager.previousButton.SetActive(false); manager.nextButton.SetActive(false); }
                        camera.transform.SetPositionAndRotation(manager.modes[mode == 0 ? 0 : 1].cameraTarget.position,
                            manager.modes[mode == 0 ? 0 : 1].cameraTarget.rotation);
                        float scaleFactor = Mathf.Pow(size.x / 1920f, 1f - scaler.matchWidthOrHeight) *
                            Mathf.Pow(size.y / 1080f, scaler.matchWidthOrHeight);
                        canvasRect.sizeDelta = new Vector2(size.x, size.y) / scaleFactor;
                        float distance = camera.nearClipPlane + 0.5f;
                        canvasRect.SetPositionAndRotation(camera.transform.TransformPoint(0, 0, distance), camera.transform.rotation);
                        float worldHeight = camera.orthographic ? 2 * camera.orthographicSize :
                            2 * distance * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * .5f);
                        canvasRect.localScale = Vector3.one * (worldHeight / canvasRect.sizeDelta.y);
                        Canvas.ForceUpdateCanvases();
                        foreach (TMP_Text text in All<TMP_Text>(scene).Where(t => t.gameObject.activeInHierarchy && t.transform.IsChildOf(canvas.transform)))
                            text.ForceMeshUpdate();
                        if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null) camera.Render();
                        else
                        {
                            var request = new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = target };
                            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera, request);
                        }
                        RenderTexture.active = target;
                        var image = new Texture2D(size.x, size.y, TextureFormat.RGBA32, false);
                        try
                        {
                            image.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0); image.Apply();
                            File.WriteAllBytes($"Temp/ModeSelection{(mode == 0 ? "Story" : mode == 1 ? "Multiplayer" : "Join")}_{size.x}x{size.y}.png", image.EncodeToPNG());
                        }
                        finally { Object.DestroyImmediate(image); }
                    }
                }
                finally { camera.targetTexture = oldTarget; target.Release(); Object.DestroyImmediate(target); }
            }
            report.AppendLine("PASS: Editor camera previews rendered for Story, Multiplayer and Join at 16:9 and 16:10. Live navigation, code typing and Photon joining still require a Play Mode test.");
        }
        finally
        {
            ShowMode(manager, data, 0); modal.SetActive(false);
            camera.transform.SetPositionAndRotation(oldPosition, oldRotation); camera.targetTexture = oldTarget; camera.aspect = oldAspect;
            camera.cullingMask = oldMask;
            canvas.renderMode = oldMode; canvas.worldCamera = oldCamera; canvas.planeDistance = oldPlane; RenderTexture.active = previous;
            scaler.enabled = oldScalerEnabled; canvasRect.sizeDelta = oldCanvasSize; canvasRect.localScale = oldCanvasScale;
            canvasRect.pivot = oldCanvasPivot;
            canvasRect.SetPositionAndRotation(oldCanvasPosition, oldCanvasRotation);
            EditorSceneManager.MarkSceneDirty(scene); // Re-save only restored, authored state.
            EditorSceneManager.SaveScene(scene);
        }
    }
    private static T[] All<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
    private static GameObject Find(Scene scene, string name) => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true))
        .First(t => t.name == name).gameObject;
    private static T Ref<T>(SerializedObject data, string field) where T : Object => (T)data.FindProperty(field).objectReferenceValue;
    private static void Rect(GameObject obj, float left, float bottom, float right, float top)
    {
        var rect = (RectTransform)obj.transform;
        rect.anchorMin = new Vector2(left, bottom); rect.anchorMax = new Vector2(right, top);
        rect.pivot = new Vector2(.5f, .5f); rect.anchoredPosition = Vector2.zero; rect.sizeDelta = Vector2.zero; rect.localScale = Vector3.one;
    }
    private static Image CreateImage(Transform parent, string name, Color color)
    {
        var child = parent.Find(name);
        var obj = child != null ? child.gameObject : new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        obj.layer = 5; obj.transform.SetParent(parent, false);
        var image = obj.GetComponent<Image>(); image.color = color; image.raycastTarget = false; return image;
    }
    private static void Frame(GameObject obj, Color? fill = null)
    {
        var outer = obj.GetComponent<Image>(); outer.sprite = rounded; outer.type = Image.Type.Sliced; outer.color = Wood;
        var inner = CreateImage(obj.transform, "Theme Cream Inset", fill ?? Cream); inner.sprite = rounded; inner.type = Image.Type.Sliced;
        Rect(inner.gameObject, 0, 0, 1, 1); ((RectTransform)inner.transform).sizeDelta = new Vector2(-14, -14); inner.transform.SetAsFirstSibling();
    }
    private static TMP_Text Label(Transform parent, string name, string value, float size)
    {
        var child = parent.Find(name);
        var obj = child != null ? child.gameObject : new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        obj.layer = 5; obj.transform.SetParent(parent, false);
        var label = obj.GetComponent<TMP_Text>(); label.font = font; label.text = value; Text(label, size); return label;
    }
    private static void Text(TMP_Text text, float size)
    {
        text.color = Ink; text.fontSize = size; text.enableAutoSizing = true; text.fontSizeMin = size * .78f; text.fontSizeMax = size;
        text.alignment = TextAlignmentOptions.Center; text.enableWordWrapping = true; text.overflowMode = TextOverflowModes.Overflow;
        text.richText = true; text.raycastTarget = false; text.margin = Vector4.zero;
    }
    private static void ThemeButton(GameObject obj, bool primary, string value)
    {
        Frame(obj, primary ? Gold : Cream);
        var button = obj.GetComponent<Button>(); button.targetGraphic = obj.transform.Find("Theme Cream Inset").GetComponent<Image>();
        button.transition = Selectable.Transition.ColorTint;
        var colors = button.colors; colors.normalColor = Color.white; colors.highlightedColor = new Color(1, .95f, .82f);
        colors.pressedColor = new Color(.83f, .76f, .65f); colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = new Color(.65f, .65f, .65f, .75f); button.colors = colors;
        var labels = obj.GetComponentsInChildren<TMP_Text>(true);
        foreach (var label in labels) { label.text = value; Text(label, primary ? 29 : 25); }
    }
    private static void NavigationButton(GameObject obj, string spriteGuid)
    {
        // Existing navigation artwork already contains its cream/brown frame
        // AND chevron. Preserve it rather than replacing its root with a fill.
        var inset = obj.transform.Find("Theme Cream Inset");
        if (inset != null) Object.DestroyImmediate(inset.gameObject);
        var image = obj.GetComponent<Image>();
        image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(spriteGuid));
        image.type = Image.Type.Simple; image.color = Color.white; image.preserveAspect = true;
        obj.GetComponent<Button>().targetGraphic = image;
    }
}
