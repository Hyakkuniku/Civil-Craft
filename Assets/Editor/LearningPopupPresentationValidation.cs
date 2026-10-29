#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
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

/// <summary>Opt-in, detached previews of authored readers. No gameplay buttons or discovery callbacks are invoked.</summary>
[InitializeOnLoad]
public static class LearningPopupPresentationValidation
{
    public const string RequestPath = "Temp/learning-popup-presentation.request";
    public const string ReportPath = "Logs/learning-popup-presentation.txt";
    private const string Output = "Temp/LearningPopupPresentation";
    private const string Reward = "Assets/Prefabs/UI/CollectRewardSystem.prefab";
    private static readonly string[] Scenes = { "Assets/Scenes/CanyonCrossing.unity", "Assets/Scenes/BHAN HOUSE.unity" };
    private static bool running;
    private static double nextPoll;
    private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    static LearningPopupPresentationValidation() { EditorApplication.update += Poll; }
    private static bool Busy => running || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
        EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer || PrefabStageUtility.GetCurrentPrefabStage() != null;
    private static void Poll()
    {
        if (Busy || EditorApplication.timeSinceStartup < nextPoll || !File.Exists(RequestPath)) return;
        nextPoll = EditorApplication.timeSinceStartup + 1; File.Delete(RequestPath); Run();
    }

    [MenuItem("Tools/Civil Craft/Learning Popups/Validate Authored Phone and Tablet Layouts")]
    public static void Run()
    {
        if (Busy) return;
        running = true; var report = new StringBuilder("Authored material and lesson readers: layout, text, scrolling and preserved callbacks\n");
        Scene[] before = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).ToArray();
        bool[] loaded = before.Select(scene => scene.isLoaded).ToArray(), dirty = before.Select(scene => scene.isDirty).ToArray();
        GameObject[][] roots = before.Select(scene => scene.isLoaded ? scene.GetRootGameObjects() : Array.Empty<GameObject>()).ToArray();
        Scene active = SceneManager.GetActiveScene(); Object[] selection = Selection.objects;
        PlayerDataManager saves = PlayerDataManager.Instance; PlayerData player = saves != null ? saves.CurrentData : null;
        string playerJson = player != null ? JsonUtility.ToJson(player) : null;
        LessonUIManager lessonSingleton = LessonUIManager.Instance; ItemUnlockUI itemSingleton = ItemUnlockUI.Instance;
        float timeScale = Time.timeScale; RenderTexture previous = RenderTexture.active;
        var hashes = Scenes.Append(Reward).Where(File.Exists).ToDictionary(path => path, Hash);
        var opened = new List<Scene>(); var originalComponents = new Dictionary<Object, string>();
        GameObject prefab = null;
        try
        {
            var lessonSources = new List<LearningPopupLayout>();
            NameRegistrationUI registrationReference = null;
            foreach (string path in Scenes)
            {
                Scene scene = SceneManager.GetSceneByPath(path);
                if (!scene.IsValid() || !scene.isLoaded) { scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive); opened.Add(scene); }
                LessonUIManager[] lessons = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<LessonUIManager>(true)).ToArray();
                Require(lessons.Length == 1, "Expected one authored lesson manager in " + scene.name);
                LessonUIManager manager = lessons[0]; LearningPopupLayout layout = manager.Panel != null ? manager.Panel.GetComponent<LearningPopupLayout>() : null;
                Require(layout != null, "The actual lesson panel has no authored reader layout in " + scene.name);
                ValidateReferences(layout, "Lesson / " + scene.name, false);
                Require(!manager.Panel.activeSelf, "The authored lesson panel starts open in " + scene.name);
                Require(ReferenceEquals(Field<TMP_Text>(manager, "lessonTitleText"), layout.titleText) &&
                    ReferenceEquals(Field<Image>(manager, "lessonImage"), layout.contentImage) &&
                    ReferenceEquals(Field<TMP_Text>(manager, "lessonDescriptionText"), layout.descriptionText) &&
                    ReferenceEquals(Field<ScrollRect>(manager, "descriptionScrollRect"), layout.descriptionScroll) &&
                    ReferenceEquals(Field<Button>(manager, "closeButton"), layout.dismissButton),
                    "The authored reader was disconnected from its existing lesson manager in " + scene.name);
                Remember(manager, originalComponents); RememberTree(layout, originalComponents); lessonSources.Add(layout);
                if (path == Scenes[1])
                {
                    registrationReference = scene.GetRootGameObjects().SelectMany(root=>root.GetComponentsInChildren<NameRegistrationUI>(true)).SingleOrDefault();
                    if (registrationReference != null && registrationReference.nameInputPanel != null)
                    {
                        Remember(registrationReference,originalComponents);
                        RememberTree(registrationReference.nameInputPanel.transform,originalComponents);
                    }
                }
                foreach (ItemUnlockUI rewards in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<ItemUnlockUI>(true)))
                {
                    Require(rewards.materialPopup != null, "The scene reward manager lacks an authored material reader: " + scene.name);
                    ValidateReferences(rewards.materialPopup, "Material / " + scene.name, true);
                    Require(!rewards.materialPopup.gameObject.activeSelf, "The authored material reader starts open in " + scene.name);
                    Remember(rewards, originalComponents); RememberTree(rewards.materialPopup, originalComponents);
                }
                report.AppendLine("AUTHORED: " + scene.name + "; existing manager references, startup state and dismiss control verified.");
            }
            prefab = PrefabUtility.LoadPrefabContents(Reward);
            ItemUnlockUI rewardManager = prefab.GetComponent<ItemUnlockUI>();
            Require(rewardManager != null && rewardManager.materialPopup != null, "The shared reward prefab lacks its authored material reader.");
            LearningPopupLayout materialSource = rewardManager.materialPopup;
            ValidateReferences(materialSource, "Shared Material Introduction", true);
            Require(!materialSource.gameObject.activeSelf, "The shared material introduction starts open.");
            Remember(rewardManager, originalComponents); RememberTree(materialSource, originalComponents);
            LessonData lesson = AssetDatabase.LoadAssetAtPath<LessonData>("Assets/BridgeBuilder/Data/Lessons/LiveLoadLesson.asset");
            BridgeMaterialSO wood = AssetDatabase.LoadAssetAtPath<BridgeMaterialSO>("Assets/BridgeBuilder/Data/Resources/Wood Road.asset");
            BridgeMaterialSO pier = AssetDatabase.LoadAssetAtPath<BridgeMaterialSO>("Assets/BridgeBuilder/Data/Resources/Concrete Pier.asset");
            Require(lesson != null && wood != null && pier != null, "A required existing preview lesson or material is missing.");
            foreach (Object source in new Object[] { lesson, wood, pier }) Remember(source, originalComponents);
            Directory.CreateDirectory(Output); int cases = 0;
            if (registrationReference != null && registrationReference.nameInputPanel != null)
                CaptureRegistrationReference(registrationReference.nameInputPanel,report);
            foreach (Vector2Int device in new[] { new Vector2Int(2340,1080), new Vector2Int(1280,720), new Vector2Int(800,360), new Vector2Int(1920,1200), new Vector2Int(1080,1920) })
            {
                string tag = device.x + "x" + device.y;
                Capture(materialSource, "Material-WoodRoad-" + tag, device, wood.GetDisplayName(), wood.PopupImage, wood.PopupDescription, report); cases++;
                Capture(materialSource, "Material-ConcretePier-" + tag, device, pier.GetDisplayName(), pier.PopupImage, pier.PopupDescription, report); cases++;
                Capture(lessonSources[0], "Lesson-LiveLoad-" + tag, device, lesson.Title, lesson.Image, lesson.PopupDescription, report); cases++;
                string longBody = string.Join("\n\n", Enumerable.Repeat(ItemUnlockUI.BuildMaterialReferenceDetails(pier), 5));
                Capture(materialSource, "LongReference-" + tag, device, pier.GetDisplayName(), pier.PopupImage, longBody, report, true); cases++;
                Capture(lessonSources[0], "NoImage-" + tag, device, "Bridge Lesson", null, lesson.PopupDescription, report); cases++;
            }
            Capture(lessonSources[1], "BhanHouse-Lesson-2340x1080", new Vector2Int(2340,1080), lesson.Title, lesson.Image, lesson.PopupDescription, report); cases++;
            foreach (var pair in originalComponents) Require(pair.Key != null && EditorJsonUtility.ToJson(pair.Key) == pair.Value, "A source reader, callback or data asset changed: " + pair.Key);
            report.Insert(0, "PASS\n"); report.AppendLine("PASS: " + cases + " detached phone/tablet/portrait layouts; authored controls, full scrollable text and image aspect ratios verified.");
        }
        catch (Exception error) { report.Insert(0, "FAILED\n"); report.AppendLine(error.ToString()); }
        finally
        {
            RenderTexture.active = previous;
            if (prefab != null) PrefabUtility.UnloadPrefabContents(prefab);
            foreach (Scene scene in opened.AsEnumerable().Reverse())
                if (scene.IsValid() && scene.isLoaded)
                    // Keep any originally unloaded scene heading/handle in its
                    // existing place; remove only scenes opened by this check.
                    EditorSceneManager.CloseScene(scene, !before.Any(original => original.path == scene.path));
            if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
            try
            {
                Require(SceneManager.sceneCount == before.Length && SceneManager.GetActiveScene() == active, "The original scene setup changed.");
                for (int i = 0; i < before.Length; i++) Require(SceneManager.GetSceneAt(i) == before[i] && before[i].isLoaded == loaded[i] && before[i].isDirty == dirty[i] &&
                    (!loaded[i] || before[i].GetRootGameObjects().SequenceEqual(roots[i])), "An original scene/root/dirty flag changed.");
                Require(hashes.All(pair => Hash(pair.Key) == pair.Value), "Saved scene or reward prefab bytes changed during validation.");
                Require(Selection.objects.SequenceEqual(selection) && Time.timeScale == timeScale && LessonUIManager.Instance == lessonSingleton && ItemUnlockUI.Instance == itemSingleton &&
                    PlayerDataManager.Instance == saves && (player == null || ReferenceEquals(player, saves.CurrentData) && JsonUtility.ToJson(player) == playerJson),
                    "Live UI singleton, selection, time scale or player data changed.");
                report.AppendLine("PASS: original scene setup/dirty edits, source callbacks, file bytes, selection, player data and UI singletons unchanged. No game button was invoked or scene saved.");
            }
            catch (Exception error) { report.Insert(0, "FAILED SAFETY\n"); report.AppendLine(error.ToString()); }
            Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, report.ToString()); running = false;
        }
    }

    private static void ValidateReferences(LearningPopupLayout layout, string label, bool material)
    {
        Require(layout.authoredVersion >= 3, "The Name Registration reference design has not been authored: " + label);
        Require(layout.card != null && layout.cardSurface != null && layout.titleTab != null && layout.titleText != null &&
            layout.dismissButton != null && layout.imageFrame != null && layout.imageSurface != null && layout.contentImage != null &&
            layout.descriptionFrame != null && layout.descriptionSurface != null && layout.descriptionText != null && layout.descriptionScroll != null &&
            layout.descriptionScroll.viewport != null && layout.descriptionScroll.content != null, "Incomplete authored reader references: " + label);
        Require(layout.contentImage.preserveAspect, "The authored picture can be stretched: " + label);
        Require(layout.dismissButton.targetGraphic != null && layout.dismissButton.GetComponents<Graphic>().Any(graphic => graphic.enabled && graphic.raycastTarget),
            "The dismiss control has no hit surface: " + label);
        Require(layout.titleText.font != null && layout.descriptionText.font != null, "The reader lost its authored font: " + label);
        Require(layout.descriptionScroll.vertical && !layout.descriptionScroll.horizontal, "The long reader must scroll vertically: " + label);
        TMP_Text buttonText = layout.dismissButton.GetComponentInChildren<TMP_Text>(true);
        Require(buttonText != null && buttonText.text == (material ? "GOT IT" : "CLOSE"), "The authored rectangular button label differs: " + label);
        TMP_Text context = Field<TMP_Text>(layout,"contextText"); RectTransform divider = Field<RectTransform>(layout,"headerDivider");
        Require(context != null && !string.IsNullOrWhiteSpace(context.text) && divider != null,
            "The authored centered introduction heading or divider is missing: " + label);
        Require(context.font != null && context.horizontalAlignment == HorizontalAlignmentOptions.Center && layout.titleText.horizontalAlignment == HorizontalAlignmentOptions.Center,
            "The reader no longer matches the reference's centered headings: " + label);
        Require(ColorMatches(layout.card.GetComponent<Image>()?.color,new Color32(90,55,31,255)) &&
            ColorMatches(layout.cardSurface.GetComponent<Image>()?.color,new Color32(248,233,204,255)),
            "The thick wood frame/warm cream surface differs from the Name Registration reference: " + label);
        Require(layout.dismissButton.GetComponentsInChildren<Image>(true).Any(image=>ColorMatches(image.color,new Color32(231,158,35,255))),
            "The primary acknowledgement is missing the reference's gold fill: " + label);
    }

    private static void Capture(LearningPopupLayout source, string label, Vector2Int device, string title, Sprite picture, string text, StringBuilder report, bool requireScroll = false)
    {
        Scene active = SceneManager.GetActiveScene(); Scene staging = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        RenderTexture previous = RenderTexture.active; var target = new RenderTexture(device.x, device.y, 24); target.Create();
        Texture2D pixels = null; Camera camera = null;
        try
        {
            GameObject cameraObject = Owned(staging, "Detached learning preview camera", typeof(Camera)); camera = cameraObject.GetComponent<Camera>();
            camera.transform.position = new Vector3(0,0,-100); camera.orthographic = true; camera.orthographicSize = 540f;
            camera.nearClipPlane = .1f; camera.farClipPlane = 200f; camera.targetTexture = target; camera.useOcclusionCulling = false;
            camera.scene = staging;
            camera.cullingMask = 1 << 5; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(152,175,177,255);
            GameObject canvasObject = Owned(staging, "Detached learning preview Canvas", typeof(RectTransform), typeof(Canvas)); canvasObject.layer = 5;
            Canvas canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = camera; canvas.scaleFactor = device.y / 1080f;
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(device.x * 1080f / device.y,1080f);
            GameObject clone = Object.Instantiate(source.gameObject, canvas.transform, false); clone.hideFlags = HideFlags.HideAndDontSave;
            Require(clone.GetComponentInChildren<LessonUIManager>(true) == null && clone.GetComponentInChildren<ItemUnlockUI>(true) == null,
                "The copied popup unexpectedly contains a live gameplay manager.");
            CozyModalLayout.Stretch((RectTransform)clone.transform,0,0,0,0); clone.SetActive(true);
            LearningPopupLayout layout = clone.GetComponent<LearningPopupLayout>();
            layout.SetContent(title,picture,text); Canvas.ForceUpdateCanvases(); layout.ApplyLayout(); Canvas.ForceUpdateCanvases();
            foreach (TMP_Text value in clone.GetComponentsInChildren<TMP_Text>(true)) value.ForceMeshUpdate(true);
            Rect screen = new Rect(-device.x * 540f / device.y,-540f,device.x * 1080f / device.y,1080f);
            Rect card = WorldRect(layout.card), close = WorldRect((RectTransform)layout.dismissButton.transform), heading = WorldRect(layout.titleText.rectTransform), bodyFrame = WorldRect(layout.descriptionFrame);
            TMP_Text context = Field<TMP_Text>(layout,"contextText"); RectTransform divider = Field<RectTransform>(layout,"headerDivider");
            Require(context != null && divider != null,"The copied reference header/divider is missing: " + label);
            Rect contextRect = WorldRect(context.rectTransform), dividerRect = WorldRect(divider);
            Require(Contains(screen,card,.05f) && Contains(card,close,.05f) && Contains(card,heading,.05f) && Contains(card,bodyFrame,.05f), "The reader extends off screen/card: " + label);
            Require(!close.Overlaps(heading) && !close.Overlaps(bodyFrame) && !heading.Overlaps(bodyFrame), "Reader heading, dismiss control or body overlap: " + label);
            Require(Contains(card,contextRect,.05f) && Contains(card,dividerRect,.05f) && !contextRect.Overlaps(heading) && !contextRect.Overlaps(bodyFrame) &&
                !contextRect.Overlaps(close) && !dividerRect.Overlaps(heading) && !dividerRect.Overlaps(bodyFrame) && !dividerRect.Overlaps(close),
                "The centered heading, divider, reading area or footer overlap: " + label);
            Require(close.yMax <= bodyFrame.yMin + .05f && close.center.y < card.center.y && close.width >= card.width*.55f && Mathf.Abs(close.center.x-card.center.x) < 1f,
                "The acknowledgement does not match the reference's wide centered bottom action: " + label);
            Require(close.height * device.y / 1080f >= 43.9f && close.width * device.y / 1080f >= 43.9f, "Dismiss touch target is below 44 pixels: " + label);
            Require(!layout.titleText.isTextTruncated && !layout.titleText.isTextOverflowing, "The heading is truncated or overflows: " + label);
            Require(!context.isTextTruncated && !context.isTextOverflowing,"The introduction/lesson context heading is truncated: " + label);
            TMP_Text buttonText = layout.dismissButton.GetComponentInChildren<TMP_Text>(true);
            Require(buttonText != null && !buttonText.isTextTruncated && !buttonText.isTextOverflowing, "Dismiss label overflows: " + label);
            if (picture != null)
            {
                Rect image = WorldRect(layout.imageFrame); Require(Contains(card,image,.05f) && !image.Overlaps(bodyFrame) && !image.Overlaps(heading) && !image.Overlaps(close) &&
                    !image.Overlaps(contextRect) && !image.Overlaps(dividerRect), "The picture overlaps another region: " + label);
                Require(layout.imageFrame.gameObject.activeSelf && layout.contentImage.sprite == picture && layout.contentImage.preserveAspect &&
                    Mathf.Abs(layout.contentImage.transform.lossyScale.x - layout.contentImage.transform.lossyScale.y) < .0001f, "The picture is missing or stretched: " + label);
            }
            else Require(!layout.imageFrame.gameObject.activeSelf && bodyFrame.width >= card.width * .75f, "Missing-image content did not expand safely: " + label);
            ScrollRect scroll = layout.descriptionScroll; RectTransform content = scroll.content;
            Require(Contains(WorldRect(scroll.viewport),WorldRect(scroll.transform as RectTransform),.1f) || Contains(WorldRect(scroll.transform as RectTransform),WorldRect(scroll.viewport),.1f), "The scroll viewport is disconnected: " + label);
            Require(layout.descriptionText.overflowMode == TextOverflowModes.Overflow && !layout.descriptionText.isTextTruncated &&
                content.rect.height + .5f >= layout.descriptionText.preferredHeight, "The complete description is cut off: " + label);
            float scrollRange = Mathf.Max(0,content.rect.height-scroll.viewport.rect.height);
            Require(!requireScroll || scrollRange > 10f, "The long reference did not create a scroll range: " + label);
            ValidateScrollbar(scroll,device.y/1080f,label + " / top");
            Save(camera,target,label); // Initial complete layout at top.
            if (requireScroll)
            {
                scroll.verticalNormalizedPosition = 0f; Canvas.ForceUpdateCanvases();
                Rect viewport = WorldRect(scroll.viewport), bottom = WorldRect(content);
                Require(Mathf.Abs(viewport.yMin-bottom.yMin) <= 2f, "The end of the long reference cannot be reached: " + label);
                ValidateScrollbar(scroll,device.y/1080f,label + " / bottom");
                Save(camera,target,label + "-bottom");
            }
            report.AppendLine("LAYOUT: " + label + "; close=" + (close.width*device.y/1080f).ToString("0.0") + "x" + (close.height*device.y/1080f).ToString("0.0") +
                "px; body=" + layout.descriptionText.fontSize.ToString("0.0") + " units; scroll=" + scrollRange.ToString("0.0") + "; full text and image aspect verified.");
        }
        finally
        {
            RenderTexture.active = previous; if (camera != null) camera.targetTexture = null;
            if (pixels != null) Object.DestroyImmediate(pixels); target.Release(); Object.DestroyImmediate(target);
            EditorSceneManager.CloseScene(staging,true); if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
        }
    }

    private static void ValidateScrollbar(ScrollRect scroll,float physicalScale,string label)
    {
        Scrollbar scrollbar = scroll.verticalScrollbar;
        if (scrollbar == null || !scrollbar.gameObject.activeInHierarchy) return;
        Require(scrollbar.handleRect != null,"The visible reading scrollbar has no thumb: " + label);
        Rect rail = WorldRect((RectTransform)scrollbar.transform), thumb = WorldRect(scrollbar.handleRect);
        Require(Contains(WorldRect((RectTransform)scroll.transform),rail,.1f),"The reading scrollbar rail leaves its scroll area: " + label);
        Require(Contains(rail,thumb,.1f),"The reading scrollbar thumb leaves its narrow rail or covers the body: " + label);
        Require(rail.width > 0f && rail.width*physicalScale <= 12.01f && thumb.width > 0f && thumb.width*physicalScale <= 12.01f,
            "The reading scrollbar/handle is wider than 12 pixels and can obscure text: " + label + "; rail/thumb=" +
            (rail.width*physicalScale).ToString("0.00") + "/" + (thumb.width*physicalScale).ToString("0.00") + "px");
        Require(!WorldRect(scroll.viewport).Overlaps(thumb),"The scrollbar thumb overlaps the description viewport: " + label);
    }

    private static void CaptureRegistrationReference(GameObject source,StringBuilder report)
    {
        const int width = 2340,height = 1080;
        Scene active = SceneManager.GetActiveScene(), staging = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        RenderTexture previous = RenderTexture.active; var target = new RenderTexture(width,height,24); target.Create(); Camera camera = null;
        try
        {
            GameObject cameraObject = Owned(staging,"Read-only registration reference camera",typeof(Camera)); camera = cameraObject.GetComponent<Camera>();
            camera.scene = staging; camera.transform.position = new Vector3(0,0,-100); camera.orthographic = true; camera.orthographicSize = 540f;
            camera.nearClipPlane = .1f; camera.farClipPlane = 200f; camera.targetTexture = target; camera.useOcclusionCulling = false;
            camera.cullingMask = 1<<5; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(152,175,177,255);
            GameObject canvasObject = Owned(staging,"Read-only registration reference Canvas",typeof(RectTransform),typeof(Canvas)); canvasObject.layer = 5;
            Canvas canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = camera; canvas.scaleFactor = 1f;
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(width,height);
            GameObject clone = Object.Instantiate(source,canvas.transform,false); clone.hideFlags = HideFlags.HideAndDontSave;
            Require(clone.GetComponentInChildren<NameRegistrationUI>(true) == null,"The registration reference panel contains a gameplay registration manager.");
            CozyModalLayout.Stretch((RectTransform)clone.transform,0,0,0,0); clone.SetActive(true); Canvas.ForceUpdateCanvases();
            foreach (TMP_Text value in clone.GetComponentsInChildren<TMP_Text>(true)) value.ForceMeshUpdate(true);
            Save(camera,target,"Reference-NameRegistration-2340x1080");
            report.AppendLine("REFERENCE: actual authored Name Registration panel copied and rendered read-only for visual comparison. No registration operation or callback ran.");
        }
        finally
        {
            RenderTexture.active = previous; if (camera != null) camera.targetTexture = null;
            target.Release(); Object.DestroyImmediate(target); EditorSceneManager.CloseScene(staging,true);
            if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
        }
    }

    private static void Save(Camera camera, RenderTexture target, string label)
    {
        var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
        if (RenderPipeline.SupportsRenderRequest(camera,request)) RenderPipeline.SubmitRenderRequest(camera,request); else camera.Render();
        RenderTexture.active = target; var pixels = new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
        try { pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0); pixels.Apply(); File.WriteAllBytes(Output + "/" + label + ".png",pixels.EncodeToPNG()); }
        finally { Object.DestroyImmediate(pixels); }
    }
    private static Rect WorldRect(RectTransform transform)
    {
        var points = new Vector3[4]; transform.GetWorldCorners(points);
        return Rect.MinMaxRect(points.Min(p=>p.x),points.Min(p=>p.y),points.Max(p=>p.x),points.Max(p=>p.y));
    }
    private static bool Contains(Rect outer, Rect inner, float epsilon) => inner.xMin >= outer.xMin-epsilon && inner.xMax <= outer.xMax+epsilon && inner.yMin >= outer.yMin-epsilon && inner.yMax <= outer.yMax+epsilon;
    private static bool ColorMatches(Color? actual,Color32 expected)
    {
        if (!actual.HasValue) return false;
        Color value = actual.Value, target = expected;
        return Mathf.Abs(value.r-target.r) < .005f && Mathf.Abs(value.g-target.g) < .005f && Mathf.Abs(value.b-target.b) < .005f && Mathf.Abs(value.a-target.a) < .005f;
    }
    private static GameObject Owned(Scene scene, string label, params Type[] components)
    { var item = new GameObject(label,components); item.hideFlags = HideFlags.HideAndDontSave; SceneManager.MoveGameObjectToScene(item,scene); return item; }
    private static T Field<T>(object owner,string name) => (T)owner.GetType().GetField(name,Hidden).GetValue(owner);
    private static void Remember(Object value,Dictionary<Object,string> states) { if (value != null && !states.ContainsKey(value)) states.Add(value,EditorJsonUtility.ToJson(value)); }
    private static void RememberTree(Component component,Dictionary<Object,string> states) { foreach (Component item in component.GetComponentsInChildren<Component>(true)) Remember(item,states); }
    private static string Hash(string path) { using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(path))); }
    private static void Require(bool condition,string message) { if (!condition) throw new InvalidOperationException(message); }
}
#endif
