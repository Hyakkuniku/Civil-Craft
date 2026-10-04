using System;
using System.Collections;
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

/// <summary>Opt-in authored toast update and isolated checks. Never unlocks or awards anything.</summary>
public static class AchievementPopupUIAuthoring
{
    private const string Request = "Temp/achievement-bottom-popup-v3.request";
    private const string PreviewRequest = "Temp/achievement-bottom-preview-v1.request";
    private const string Report = "Temp/AchievementPopupUIValidation.txt";
    private const string Prefab = "Assets/Prefabs/UI/AchievementPopupCanvas.prefab";
    private static readonly string[] Scenes = { "Assets/Scenes/Main Menu.unity", "Assets/Scenes/CanyonCrossing.unity", "Assets/Scenes/BHAN HOUSE.unity" };
    private static readonly Color Paper = new Color32(249, 235, 206, 255), Wood = new Color32(87, 55, 31, 255), Ink = new Color32(118, 89, 64, 255);
    private static Sprite rounded, coin, experience;
    private static TMP_FontAsset font;
    private static double nextCheck;

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
            try { PrepareAssets(); var previewReport = new StringBuilder(); ValidateAndCapture(previewReport); File.WriteAllText("Temp/AchievementPopupPreviewValidation.txt", "PASS: isolated dialogue/queue, authored widgets and layout checks. No source scene/prefab or player save changed.\n" + previewReport); }
            catch (Exception error) { File.WriteAllText("Temp/AchievementPopupPreviewValidation.txt", "FAIL: " + error); Debug.LogException(error); }
        }
        if (!File.Exists(Request)) return;
        if (Scenes.Any(path => SceneManager.GetSceneByPath(path).isDirty))
        { File.WriteAllText(Report, "WAIT: Stop Play Mode and save scene edits. Unsaved scenes have not been overwritten."); return; }
        File.Delete(Request);
        try { Author(); }
        catch (Exception error) { File.WriteAllText(Report, "FAIL: " + error); Debug.LogException(error); }
    }

    [MenuItem("Tools/Civil Craft/Style Bottom Achievement Popup")]
    public static void Author()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || Scenes.Any(path => SceneManager.GetSceneByPath(path).isDirty))
            throw new Exception("Stop Play Mode and save scene edits before authoring.");
        PrepareAssets();
        var prefab = PrefabUtility.LoadPrefabContents(Prefab);
        try { Style(prefab.GetComponent<AchievementPopupNotification>()); PrefabUtility.SaveAsPrefabAsset(prefab, Prefab); }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        var report = new StringBuilder();
        ValidateAndCapture(report);
        foreach (string path in Scenes)
        {
            Scene scene = SceneManager.GetSceneByPath(path); bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                var popups = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<AchievementPopupNotification>(true)).ToArray();
                // Gameplay scenes normally use the persistent Main Menu popup.
                // Style its shared prefab rather than authoring another singleton.
                if (popups.Length == 0)
                {
                    report.AppendLine("PASS: " + scene.name + " uses the styled persistent popup; no duplicate scene popup added.");
                    continue;
                }
                // Preserve existing scene/prefab copies. Whichever singleton is
                // kept by the runtime should have the same authored treatment.
                foreach (var popup in popups) Style(popup);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new Exception("Could not save " + path);
                report.AppendLine("PASS: saved " + popups.Length + " existing authored bottom popup copy/copies in " + scene.name + "; none added or removed.");
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }
        AssetDatabase.SaveAssets();
        report.AppendLine("PASS: cream/brown framed design, bottom-center slide, queue retained during dialogue and closing animation, interrupted animation/hold paused, all graphics non-raycasting, authored reward widgets, sample rewards unchanged, 16:9/16:10/phone bounds. No player saves, achievements, rewards, purchases or dialogue callbacks changed. Live conversation testing still required.");
        File.WriteAllText(Report, report.ToString());
        Debug.Log("[Achievement popup UI] Authored bottom toast and dialogue priority checks passed.");
    }
    private static void PrepareAssets()
    {
        rounded = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Elements/UI/LevelResultRounded.png");
        coin = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Elements/UI/peso.png");
        experience = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Elements/UI/exp icon.png");
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/Bekind Sans SDF.asset");
        if (rounded == null || coin == null || experience == null || font == null) throw new Exception("Existing UI artwork is missing.");
    }

    private static void Style(AchievementPopupNotification controller)
    {
        var data = new SerializedObject(controller);
        var root = data.FindProperty("popupRoot").objectReferenceValue as GameObject;
        if (root == null) throw new Exception("Popup root is missing.");
        controller.transform.localScale = Vector3.one; Record(controller.transform);
        var rect = (RectTransform)root.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0); rect.pivot = new Vector2(.5f, 0);
        rect.sizeDelta = new Vector2(760, 164); rect.anchoredPosition = new Vector2(0, -180); rect.localScale = Vector3.one; Record(rect);
        var frame = root.GetComponent<Image>(); frame.sprite = rounded; frame.type = Image.Type.Sliced; frame.color = Wood;
        var outline = root.GetComponent<Outline>(); if (outline != null) { outline.enabled = false; Record(outline); }
        Image face = ImageChild(root.transform, "CreamFace", Paper); Stretch(face.rectTransform, new Vector2(6, 6), new Vector2(-6, -6)); face.transform.SetAsFirstSibling();
        Image accent = ImageChild(root.transform, "GoldAccent", new Color32(155, 88, 20, 255));
        Fixed(accent.rectTransform, new Vector2(0, .5f), new Vector2(.5f, .5f), new Vector2(19, 0), new Vector2(6, 118));
        Image badge = ImageChild(root.transform, "IconBadge", new Color32(255, 246, 226, 255));
        Fixed(badge.rectTransform, new Vector2(0, .5f), new Vector2(.5f, .5f), new Vector2(86, 0), new Vector2(112, 112)); badge.transform.SetSiblingIndex(2);
        var icon = data.FindProperty("iconImage").objectReferenceValue as Image;
        Fixed(icon.rectTransform, new Vector2(0, .5f), new Vector2(.5f, .5f), new Vector2(86, 0), new Vector2(96, 96)); icon.preserveAspect = true; icon.transform.SetAsLastSibling();
        TMP_Text heading = root.transform.Find("Heading").GetComponent<TMP_Text>();
        TMP_Text name = data.FindProperty("achievementNameText").objectReferenceValue as TMP_Text;
        TMP_Text reward = data.FindProperty("rewardText").objectReferenceValue as TMP_Text;
        TextStyle(heading, 22, 18, FontStyles.Normal); TextRect(heading, new Vector2(158, -18), new Vector2(570, 30));
        TextStyle(name, 34, 24, FontStyles.Bold); TextRect(name, new Vector2(158, -52), new Vector2(570, 50));
        TextStyle(reward, 22, 18, FontStyles.Normal); TextRect(reward, new Vector2(158, -111), new Vector2(570, 30));
        Image coinImage = ImageChild(root.transform, "RewardCoinIcon", Color.white); coinImage.sprite = coin; coinImage.preserveAspect = true;
        Image expImage = ImageChild(root.transform, "RewardExperienceIcon", Color.white); expImage.sprite = experience; expImage.preserveAspect = true;
        Fixed(coinImage.rectTransform, new Vector2(0, .5f), Vector2.one * .5f, new Vector2(250, -44), new Vector2(25, 25));
        Fixed(expImage.rectTransform, new Vector2(0, .5f), Vector2.one * .5f, new Vector2(390, -44), new Vector2(25, 25));
        TMP_Text coinValue = TextChild(root.transform, "RewardCoinValue"), expValue = TextChild(root.transform, "RewardExperienceValue");
        TextStyle(coinValue, 22, 16, FontStyles.Bold); TextStyle(expValue, 22, 16, FontStyles.Bold);
        TextRect(coinValue, new Vector2(268, -111), new Vector2(110, 30)); TextRect(expValue, new Vector2(408, -111), new Vector2(110, 30));
        coinImage.gameObject.SetActive(false); expImage.gameObject.SetActive(false); coinValue.gameObject.SetActive(false); expValue.gameObject.SetActive(false);
        data.FindProperty("visiblePosition").vector2Value = new Vector2(0, 28); data.FindProperty("hiddenPosition").vector2Value = new Vector2(0, -180);
        data.FindProperty("slideDuration").floatValue = .3f;
        foreach (string prefix in new[] { "", "feature", "cosmetic", "almanac" })
        {
            data.FindProperty(prefix == "" ? "backgroundColor" : prefix + "BackgroundColor").colorValue = Paper;
            data.FindProperty(prefix == "" ? "primaryTextColor" : prefix + "PrimaryTextColor").colorValue = Wood;
            data.FindProperty(prefix == "" ? "secondaryTextColor" : prefix + "SecondaryTextColor").colorValue = Ink;
        }
        data.FindProperty("accentColor").colorValue = new Color32(155, 88, 20, 255);
        data.FindProperty("featureAccentColor").colorValue = new Color32(69, 122, 76, 255);
        data.FindProperty("cosmeticAccentColor").colorValue = new Color32(128, 82, 118, 255);
        data.FindProperty("almanacAccentColor").colorValue = new Color32(168, 92, 33, 255);
        data.FindProperty("backgroundImage").objectReferenceValue = face; data.FindProperty("accentImage").objectReferenceValue = accent;
        data.FindProperty("headingText").objectReferenceValue = heading; data.FindProperty("rewardCoinIcon").objectReferenceValue = coinImage;
        data.FindProperty("rewardExperienceIcon").objectReferenceValue = expImage; data.FindProperty("rewardCoinValueText").objectReferenceValue = coinValue;
        data.FindProperty("rewardExperienceValueText").objectReferenceValue = expValue;
        data.ApplyModifiedPropertiesWithoutUndo();
        CanvasGroup group = root.GetComponent<CanvasGroup>(); group.alpha = 0; group.blocksRaycasts = group.interactable = false; group.ignoreParentGroups = true;
        foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(true)) { graphic.raycastTarget = false; graphic.gameObject.layer = 5; Record(graphic); Record(graphic.rectTransform); }
        root.SetActive(false); Record(group); Record(controller);
    }
    private static Image ImageChild(Transform parent, string name, Color color)
    {
        Transform child = parent.Find(name);
        if (child == null) { child = new GameObject(name, typeof(RectTransform), typeof(Image)).transform; child.SetParent(parent, false); }
        Image image = child.GetComponent<Image>(); image.sprite = rounded; image.type = Image.Type.Sliced; image.color = color; image.raycastTarget = false;
        return image;
    }
    private static TMP_Text TextChild(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child == null) { child = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).transform; child.SetParent(parent, false); }
        return child.GetComponent<TMP_Text>();
    }
    private static void TextStyle(TMP_Text text, float max, float min, FontStyles style)
    {
        text.font = font; text.fontSize = max; text.enableAutoSizing = true; text.fontSizeMin = min; text.fontSizeMax = max;
        text.fontStyle = style; text.color = Wood; text.enableWordWrapping = false; text.overflowMode = TextOverflowModes.Ellipsis;
        text.alignment = TextAlignmentOptions.MidlineLeft; text.raycastTarget = false;
    }
    private static void TextRect(TMP_Text text, Vector2 position, Vector2 size) => Fixed(text.rectTransform, new Vector2(0, 1), new Vector2(0, 1), position, size);
    private static void Fixed(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
    { rect.anchorMin = rect.anchorMax = anchor; rect.pivot = pivot; rect.sizeDelta = size; rect.anchoredPosition = position; rect.localScale = Vector3.one; }
    private static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
    { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = min; rect.offsetMax = max; rect.localScale = Vector3.one; }
    private static void Record(Component component)
    { EditorUtility.SetDirty(component); if (PrefabUtility.IsPartOfPrefabInstance(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component); }

    private static object Field(object obj, string name) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(obj);
    private static object Call(object obj, string method, params object[] args) => obj.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(obj, args);
    private static object Sample(string title, string kind, int gold = 0, int exp = 0)
    {
        Type type = typeof(AchievementPopupNotification).GetNestedType("PopupRequest", BindingFlags.NonPublic);
        object request = Activator.CreateInstance(type);
        type.GetField("title").SetValue(request, title); type.GetField("detail").SetValue(request, "Added to your field book");
        type.GetField("kind").SetValue(request, Enum.Parse(type.GetField("kind").FieldType, kind));
        type.GetField("goldReward").SetValue(request, gold); type.GetField("experienceReward").SetValue(request, exp);
        type.GetField("icon").SetValue(request, AssetDatabase.LoadAssetAtPath<AchievementSO>("Assets/BridgeBuilder/Data/Achievements/Master Builder.asset").achievementIcon);
        return request;
    }
    private static void ValidateAndCapture(StringBuilder report)
    {
        Scene preview = EditorSceneManager.NewPreviewScene();
        try
        {
            GameObject clone = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab)); SceneManager.MoveGameObjectToScene(clone, preview);
            var popup = clone.GetComponent<AchievementPopupNotification>(); Style(popup); Call(popup, "ResolveVisualReferences");
            GameObject root = (GameObject)Field(popup, "popupRoot"); var rect = (RectTransform)root.transform; root.SetActive(true);
            int widgets = root.GetComponentsInChildren<Transform>(true).Length;
            Call(popup, "Populate", Sample("Master Builder", "Achievement", 1500, 250));
            if (((TMP_Text)Field(popup, "rewardCoinValueText")).text != 1500.ToString("N0") || ((TMP_Text)Field(popup, "rewardExperienceValueText")).text != 250.ToString("N0")) throw new Exception("Displayed rewards changed.");
            if (root.GetComponentsInChildren<Graphic>(true).Any(g => g.raycastTarget) || ((CanvasGroup)Field(popup, "popupGroup")).blocksRaycasts) throw new Exception("Toast consumes dialogue input.");
            GameObject dialogueObject = new GameObject("TemporaryDialogueVisibilityCheck"); SceneManager.MoveGameObjectToScene(dialogueObject, preview);
            var dialogue = dialogueObject.AddComponent<DialogueManager>(); Call(dialogue, "OnEnable");
            var box = new GameObject("TemporaryDialogueBox"); box.transform.SetParent(dialogueObject.transform, false);
            typeof(DialogueManager).GetField("dialogueBox", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(dialogue, box);
            FieldInfo activeDialogue = typeof(DialogueManager).GetField("isDialogueActive", BindingFlags.Instance | BindingFlags.NonPublic);
            activeDialogue.SetValue(dialogue, true); box.SetActive(false);
            object queue = Field(popup, "pendingNotifications"); MethodInfo enqueue = queue.GetType().GetMethod("Enqueue");
            enqueue.Invoke(queue, new[] { Sample("First queued achievement", "Achievement", 100, 0) });
            enqueue.Invoke(queue, new[] { Sample("Second queued achievement", "Achievement", 0, 200) });
            IEnumerator play = (IEnumerator)Call(popup, "PlayQueuedNotifications");
            if (!play.MoveNext() || (int)queue.GetType().GetProperty("Count").GetValue(queue) != 2) throw new Exception("Dialogue lost a queued popup.");
            Call(popup, "ForceAbsoluteOverlay"); if (clone.GetComponent<Canvas>().enabled) throw new Exception("Popup renders over a dialogue/closing box.");
            // Box stays active during the close animation even after isDialogueActive is false.
            activeDialogue.SetValue(dialogue, false); box.SetActive(true);
            if (!play.MoveNext() || (int)queue.GetType().GetProperty("Count").GetValue(queue) != 2) throw new Exception("Popup did not wait for closing dialogue.");
            box.SetActive(false); if (!play.MoveNext()) throw new Exception("Queue did not resume after dialogue.");
            if (((TMP_Text)Field(popup, "achievementNameText")).text != "First queued achievement" || (int)queue.GetType().GetProperty("Count").GetValue(queue) != 1) throw new Exception("Popup order changed.");
            IEnumerator slide = (IEnumerator)play.Current; Vector2 position = rect.anchoredPosition; float alpha = root.GetComponent<CanvasGroup>().alpha;
            box.SetActive(true);
            for (int i = 0; i < 12; i++) if (!slide.MoveNext()) throw new Exception("Interrupted slide expired during dialogue.");
            if (position != rect.anchoredPosition || alpha != root.GetComponent<CanvasGroup>().alpha) throw new Exception("Slide progressed while hidden by dialogue.");
            IEnumerator hold = (IEnumerator)Call(popup, "HoldPopup");
            for (int i = 0; i < 12; i++) if (!hold.MoveNext()) throw new Exception("Visible time expired during dialogue.");
            box.SetActive(false); Call(popup, "ForceAbsoluteOverlay"); if (!clone.GetComponent<Canvas>().enabled) throw new Exception("Popup did not resume rendering.");
            Call(dialogue, "OnDisable"); Object.DestroyImmediate(dialogueObject);
            foreach (string kind in new[] { "Achievement", "Feature", "Cosmetic", "Almanac", "Multiplayer" }) Call(popup, "Populate", Sample("Builder's Field Book", kind));
            if (root.GetComponentsInChildren<Transform>(true).Length != widgets) throw new Exception("Runtime added UI widgets.");
            Call(popup, "Populate", Sample("Master Builder", "Achievement", 1500, 250)); root.GetComponent<CanvasGroup>().alpha = 1;
            Capture(root, report);
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
    }
    private static void Capture(GameObject source, StringBuilder report)
    {
        Scene preview = EditorSceneManager.NewPreviewScene(); RenderTexture previous = RenderTexture.active;
        try
        {
            var root = new GameObject("ToastPreviewCanvas", typeof(RectTransform), typeof(Canvas)); SceneManager.MoveGameObjectToScene(root, preview);
            var canvasRect = root.GetComponent<RectTransform>(); root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace; root.transform.localScale = Vector3.one * .01f;
            GameObject clone = Object.Instantiate(source, root.transform, false); var rect = (RectTransform)clone.transform; rect.anchoredPosition = new Vector2(0, 28); clone.SetActive(true);
            var cameraObject = new GameObject("ToastPreviewCamera", typeof(Camera)); SceneManager.MoveGameObjectToScene(cameraObject, preview);
            Camera camera = cameraObject.GetComponent<Camera>(); camera.scene = preview; camera.orthographic = true; camera.orthographicSize = 5.4f; camera.transform.position = new Vector3(0, 0, -20);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(138, 111, 88, 255);
            foreach (int width in new[] { 1920, 1728, 2340 })
            {
                canvasRect.sizeDelta = new Vector2(width, 1080); Canvas.ForceUpdateCanvases();
                var corners = new Vector3[4]; rect.GetWorldCorners(corners);
                Vector3 min = canvasRect.InverseTransformPoint(corners[0]), max = canvasRect.InverseTransformPoint(corners[2]);
                if (min.x < canvasRect.rect.xMin || max.x > canvasRect.rect.xMax || min.y < canvasRect.rect.yMin || max.y > canvasRect.rect.yMin + 200)
                    throw new Exception("Toast is not below at width " + width);
                var texture = new RenderTexture(width, 1080, 24); texture.Create(); camera.targetTexture = texture;
                try
                {
                    if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null) camera.Render();
                    else UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera, new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = texture });
                    RenderTexture.active = texture; var pixels = new Texture2D(width, 1080, TextureFormat.RGBA32, false);
                    try { pixels.ReadPixels(new Rect(0, 0, width, 1080), 0, 0); pixels.Apply(); File.WriteAllBytes("Temp/AchievementBottomPopup_" + width + ".png", pixels.EncodeToPNG()); }
                    finally { Object.DestroyImmediate(pixels); }
                }
                finally { camera.targetTexture = null; RenderTexture.active = null; texture.Release(); Object.DestroyImmediate(texture); }
                report.AppendLine("PASS: bottom placement and authored preview at " + width + "x1080");
            }
        }
        finally { RenderTexture.active = previous; EditorSceneManager.ClosePreviewScene(preview); }
    }
}
