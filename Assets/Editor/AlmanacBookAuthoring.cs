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

/// <summary>Authors one consistent paper/binding treatment across all four chapters.</summary>
public static class AlmanacBookAuthoring
{
    private const string Request = "Temp/almanac-book-unified-v2.request";
    private const string Report = "Temp/AlmanacBookValidation.txt";
    private static readonly string[] Paths = { "Assets/Scenes/CanyonCrossing.unity", "Assets/Scenes/BHAN HOUSE.unity" };
    private static readonly Color Paper = new Color32(255, 246, 222, 255);
    private static readonly Color Ink = new Color32(77, 53, 36, 255);
    private static readonly Color Cover = new Color32(90, 55, 31, 255);
    private static Sprite rounded;
    private static TMP_FontAsset font;
    private static double nextCheck;

    [InitializeOnLoadMethod]
    private static void Watch() { EditorApplication.update -= Check; EditorApplication.update += Check; }
    private static void Check()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (Paths.Any(path => SceneManager.GetSceneByPath(path).isDirty))
        { File.WriteAllText(Report, "WAIT: Save your CanyonCrossing/Bhan House scene edits first. Nothing overwritten."); return; }
        File.Delete(Request);
        try { Author(); }
        catch (Exception error) { File.WriteAllText(Report, "FAIL: " + error); Debug.LogException(error); }
    }

    [MenuItem("Tools/Civil Craft/Author Almanac Book Archives")]
    public static void Author()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || Paths.Any(path => SceneManager.GetSceneByPath(path).isDirty))
            throw new Exception("Stop Play Mode and save your scene edits first.");
        rounded = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Elements/UI/LevelResultRounded.png");
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/Bekind Sans SDF.asset");
        if (rounded == null || font == null) throw new Exception("Existing UI sprite/font missing.");
        var report = new StringBuilder();
        foreach (string path in Paths)
        {
            Scene scene = SceneManager.GetSceneByPath(path);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                AlmanacManager manager = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<AlmanacManager>(true)).Single();
                AlmanacCategory profile = manager.categories.Single(c => c.tabType == AlmanacTabType.General);
                // Preserve live data/portrait bindings and every authored action,
                // while allowing the Profile's cosmetic layout to match the book.
                var stats = profile.rightPageZone.GetComponentInChildren<AlmanacPlayerStats>(true);
                if (stats == null) throw new Exception("Profile stats page is missing.");
                string before = EditorJsonUtility.ToJson(stats);
                var portraits = profile.leftPageZone.GetComponentsInChildren<RawImage>(true)
                    .ToDictionary(image => image, EditorJsonUtility.ToJson);
                var originalEvents = manager.almanacCanvas.GetComponentsInChildren<Button>(true)
                    .ToDictionary(c => c, c => JsonUtility.ToJson(c.onClick));
                Transform container = profile.leftPageZone.parent.parent;
                foreach (Graphic graphic in container.GetComponents<Graphic>())
                    graphic.enabled = false; // Old open-book bitmap, not the modal dimmer.
                foreach (Image image in container.GetComponentsInChildren<Image>(true))
                {
                    if (image.sprite == null) continue;
                    string asset = AssetDatabase.GetAssetPath(image.sprite);
                    if (asset == "Assets/Elements/Almanac/page only-clean.png" ||
                        asset == "Assets/Elements/Almanac/cover only-clean.png" ||
                        asset == "Assets/Elements/Almanac/page only.png" ||
                        asset == "Assets/Elements/Almanac/cover only.png" ||
                        asset == "Assets/Elements/Almanac/book_open.png" ||
                        asset == "Assets/Elements/Almanac/Almanac_open_png.png")
                        image.enabled = false;
                }
                var frames = new GameObject[manager.categories.Count];
                foreach (AlmanacCategory category in manager.categories)
                {
                    int index = manager.categories.IndexOf(category);
                    RectTransform parent = category.leftPageZone.parent as RectTransform;
                    if (parent == null || category.rightPageZone.parent != parent)
                        throw new Exception("Archive page halves have different parents: " + category.categoryName);
                    Place(parent, .025f, .06f, .975f, .91f);
                    Transform existing = parent.Find("AuthoredBookBinding");
                    if (existing != null) Object.DestroyImmediate(existing.gameObject);
                    RectTransform binding = Rect("AuthoredBookBinding", parent);
                    Place(binding, 0, 0, 1, 1); binding.SetAsFirstSibling();
                    BuildBinding(binding);
                    frames[index] = binding.gameObject;
                    PaperSheet(category.leftPageZone as RectTransform, true);
                    PaperSheet(category.rightPageZone as RectTransform, false);
                    if (category.tabType == AlmanacTabType.General)
                        StyleProfile(category);
                    PaperFurniture(category.leftPageZone, category.tabType, index, true);
                    PaperFurniture(category.rightPageZone, category.tabType, index, false);
                    if (category.tabType == AlmanacTabType.Contracts)
                    {
                        // The archive heading used a large button-style wooden frame.
                        // Preserve its text object but print it directly on the page.
                        Transform heading = category.leftPageZone.GetComponentsInChildren<Transform>(true)
                            .FirstOrDefault(t => t.name == "PlayerName");
                        if (heading != null)
                        {
                            Image image = heading.GetComponent<Image>();
                            if (image != null) image.enabled = false;
                            Place((RectTransform)heading, .10f, .865f, .90f, .96f);
                            foreach (TMP_Text text in heading.GetComponentsInChildren<TMP_Text>(true))
                            {
                                Place(text.rectTransform, 0, 0, 1, 1); text.color = Ink;
                                text.enableAutoSizing = true; text.fontSizeMin = 28f; text.fontSizeMax = 42f;
                                text.fontSize = 42f; text.fontStyle = FontStyles.Bold;
                                text.alignment = TextAlignmentOptions.MidlineLeft;
                            }
                        }
                    }
                    if (category.tabButton != null)
                    {
                        // Keep the authored bookmark positions, labels, alerts, and listeners.
                        Image bookmark = category.tabButton.GetComponent<Image>();
                        if (bookmark != null)
                        {
                            bookmark.sprite = rounded; bookmark.type = Image.Type.Sliced;
                            bookmark.color = category.tabType == AlmanacTabType.Contracts
                                ? new Color32(231, 158, 35, 255)
                                : category.tabType == AlmanacTabType.Lessons
                                    ? new Color32(211, 183, 139, 255)
                                    : category.tabType == AlmanacTabType.Materials
                                        ? new Color32(188, 194, 153, 255) : new Color32(248, 233, 204, 255);
                            category.activeSprite = category.inactiveSprite = rounded;
                        }
                        foreach (TMP_Text text in category.tabButton.GetComponentsInChildren<TMP_Text>(true))
                        { text.color = Ink; text.fontStyle = FontStyles.Bold; }
                    }
                    binding.gameObject.SetActive(false);
                }
                AlmanacBookMotion motion = manager.GetComponent<AlmanacBookMotion>();
                if (motion == null) motion = manager.gameObject.AddComponent<AlmanacBookMotion>();
                var motionData = new SerializedObject(motion);
                motionData.FindProperty("manager").objectReferenceValue = manager;
                SerializedProperty array = motionData.FindProperty("chapterFrames");
                array.arraySize = frames.Length;
                for (int i = 0; i < frames.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = frames[i];
                motionData.ApplyModifiedPropertiesWithoutUndo();
                var managerData = new SerializedObject(manager);
                managerData.FindProperty("bookMotion").objectReferenceValue = motion;
                managerData.ApplyModifiedPropertiesWithoutUndo();
                if (before != EditorJsonUtility.ToJson(stats)) throw new Exception("Profile data/action bindings changed.");
                if (portraits.Any(pair => pair.Value != EditorJsonUtility.ToJson(pair.Key)))
                    throw new Exception("Profile portrait texture/settings changed.");
                if (originalEvents.Any(pair => pair.Value != JsonUtility.ToJson(pair.Key.onClick)))
                    throw new Exception("Existing bookmark callbacks changed.");
                AlmanacLayoutAuthoring.Apply(manager);
                RenderPreviews(manager, scene.name);
                EditorUtility.SetDirty(manager); EditorUtility.SetDirty(motion);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new Exception("Could not save " + path);
                report.AppendLine("PASS: " + scene.name + ": four unified authored book spreads; legacy book bitmap disabled; Profile data/portrait/actions retained; all button callbacks retained; previews rendered.");
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }
        report.AppendLine("No saves, contracts, discovery data, purchases, or multiplayer state changed. Live navigation/animation checks still required.");
        File.WriteAllText(Report, report.ToString());
        Debug.Log("[Almanac book] " + report);
    }

    private static void StyleProfile(AlmanacCategory category)
    {
        foreach (Transform zone in new[] { category.leftPageZone, category.rightPageZone })
        {
            foreach (RectTransform page in zone)
                if (page.name != "AuthoredPaperFurniture") Place(page, 0, 0, 1, 1);
            foreach (RectTransform design in zone.GetComponentsInChildren<RectTransform>(true))
            {
                if (design.name != "ProfilePageDesign" && design.name != "ProfileStatsDesign") continue;
                Place(design, .035f, .04f, .965f, .90f);
            }
        }
    }

    private static void PaperSheet(RectTransform sheet, bool left)
    {
        Place(sheet, left ? .02f : .502f, .04f, left ? .498f : .98f, .96f);
        sheet.pivot = new Vector2(left ? 1f : 0f, .5f);
        sheet.offsetMin = sheet.offsetMax = Vector2.zero;
        Image paper = sheet.GetComponent<Image>();
        if (paper != null && !(paper is AlmanacPaperImage)) { Object.DestroyImmediate(paper); paper = null; }
        if (paper == null) paper = sheet.gameObject.AddComponent<AlmanacPaperImage>();
        var data = new SerializedObject(paper);
        data.FindProperty("leftPage").boolValue = left; data.ApplyModifiedPropertiesWithoutUndo();
        paper.sprite = null; paper.color = Paper; paper.raycastTarget = false; paper.enabled = false;
        if (sheet.GetComponent<CanvasGroup>() == null) sheet.gameObject.AddComponent<CanvasGroup>();
    }

    private static void BuildBinding(RectTransform parent)
    {
        Image cover = Art("LeatherCover", parent, Cover, -.012f, -.018f, 1.012f, 1.018f);
        cover.sprite = rounded; cover.type = Image.Type.Sliced;
        for (int i = 0; i < 2; i++)
        {
            float inset = i * .006f;
            Color edge = i % 2 == 0 ? new Color32(211, 186, 143, 255) : new Color32(246, 227, 192, 255);
            Art("LeftLeafEdge_" + i, parent, edge, .004f + inset, .006f + inset, .498f, .994f - inset);
            Art("RightLeafEdge_" + i, parent, edge, .502f, .006f + inset, .996f - inset, .994f - inset);
        }
        Art("BindingSeam", parent, new Color32(116, 81, 46, 255), .498f, .003f, .502f, .997f);
    }

    private static void PaperFurniture(Transform zone, AlmanacTabType type, int chapter, bool left)
    {
        Transform previous = zone.Find("AuthoredPaperFurniture");
        if (previous != null) Object.DestroyImmediate(previous.gameObject);
        RectTransform parent = Rect("AuthoredPaperFurniture", zone);
        Place(parent, 0, 0, 1, 1); parent.SetAsLastSibling();
        string title = type == AlmanacTabType.General ? "ENGINEER'S FIELD BOOK" :
            type == AlmanacTabType.Contracts ? "COMPLETED CONTRACTS" :
            type == AlmanacTabType.Lessons ? "ENGINEERING LESSONS" : "MATERIAL FIELD GUIDE";
        // Printed inside the paper's reserved margin, not across the page-stack edge.
        Label("RunningHeader", parent, left ? title : chapter == 0 ? "PERSONAL RECORD" : "CHAPTER " + chapter.ToString("00"),
            .07f, .946f, .93f, .98f, 14, left ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.MidlineRight);
        Label("Folio", parent, (chapter * 2 + (left ? 1 : 2)).ToString("00"),
            left ? .07f : .85f, .012f, left ? .15f : .93f, .045f, 14, TextAlignmentOptions.Center);
    }

    private static RectTransform Rect(string name, Transform parent)
    {
        var obj = new GameObject(name, typeof(RectTransform)); obj.layer = parent.gameObject.layer;
        var rect = obj.GetComponent<RectTransform>(); rect.SetParent(parent, false); return rect;
    }
    private static void Place(RectTransform rect, float x0, float y0, float x1, float y1)
    {
        rect.anchorMin = new Vector2(x0, y0); rect.anchorMax = new Vector2(x1, y1);
        rect.pivot = new Vector2(.5f, .5f); rect.offsetMin = rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one; rect.localRotation = Quaternion.identity;
    }
    private static Image Art(string name, Transform parent, Color color, float x0, float y0, float x1, float y1)
    {
        RectTransform rect = Rect(name, parent); Place(rect, x0, y0, x1, y1);
        Image image = rect.gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = false; return image;
    }
    private static TMP_Text Label(string name, Transform parent, string value, float x0, float y0, float x1, float y1,
        float size, TextAlignmentOptions alignment)
    {
        RectTransform rect = Rect(name, parent); Place(rect, x0, y0, x1, y1);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>(); text.font = font; text.text = value;
        text.fontSize = size; text.color = Ink; text.alignment = alignment; text.raycastTarget = false;
        text.enableAutoSizing = true; text.fontSizeMin = 11; text.fontSizeMax = size;
        text.enableWordWrapping = false; text.overflowMode = TextOverflowModes.Ellipsis;
        return text;
    }

    internal static void CaptureAuthoredPreviews(AlmanacManager source, string sceneName)
    {
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/Bekind Sans SDF.asset");
        rounded = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Elements/UI/LevelResultRounded.png");
        RenderPreviews(source, sceneName);
    }

    private static void RenderPreviews(AlmanacManager source, string sceneName)
    {
        Scene preview = EditorSceneManager.NewPreviewScene();
        RenderTexture previous = RenderTexture.active;
        try
        {
            Transform sourcePanel = source.categories[0].leftPageZone.parent.parent.parent;
            var root = new GameObject("BookPreviewCanvas", typeof(RectTransform), typeof(Canvas));
            SceneManager.MoveGameObjectToScene(root, preview);
            root.GetComponent<RectTransform>().sizeDelta = new Vector2(1920, 1080);
            root.transform.localScale = Vector3.one * .01f;
            var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            var clone = Object.Instantiate(sourcePanel.gameObject, root.transform, false);
            clone.SetActive(true); Place((RectTransform)clone.transform, 0, 0, 1, 1);
            var ownerObj = new GameObject("PreviewOwner"); SceneManager.MoveGameObjectToScene(ownerObj, preview);
            var owner = ownerObj.AddComponent<AlmanacManager>(); owner.almanacCanvas = clone;
            typeof(AlmanacManager).GetField("bookMotion", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(owner, ownerObj.AddComponent<AlmanacBookMotion>());
            foreach (AlmanacCategory category in source.categories)
            {
                owner.categories.Add(new AlmanacCategory {
                    tabType = category.tabType, categoryName = category.categoryName,
                    leftPageZone = clone.transform.Find(AnimationUtility.CalculateTransformPath(category.leftPageZone, sourcePanel)),
                    rightPageZone = clone.transform.Find(AnimationUtility.CalculateTransformPath(category.rightPageZone, sourcePanel))
                });
            }
            // Exercise the real initialization path: decorations must not be
            // paginated or stashed with the legacy content, and pivots must not
            // relocate any authored/dynamically built page at startup.
            typeof(AlmanacManager).GetMethod("InitializeBook", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(owner, null);
            foreach (AlmanacCategory category in owner.categories)
            {
                if (category.leftPages.Any(p => p.name == "AuthoredPaperFurniture") ||
                    category.rightPages.Any(p => p.name == "AuthoredPaperFurniture") ||
                    category.leftPageZone.Find("AuthoredPaperFurniture") == null ||
                    category.rightPageZone.Find("AuthoredPaperFurniture") == null)
                    throw new Exception("Paper furniture became content during initialization.");
                foreach (Transform zone in new[] { category.leftPageZone, category.rightPageZone })
                    foreach (UnityEngine.GameObject page in zone == category.leftPageZone ? category.leftPages : category.rightPages)
                        page.SetActive(true);
            }
            var cameraObj = new GameObject("BookPreviewCamera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObj, preview);
            Camera camera = cameraObj.GetComponent<Camera>(); camera.scene = preview;
            camera.orthographic = true; camera.orthographicSize = 5.4f;
            camera.transform.position = new Vector3(0, 0, -20); camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(138, 111, 88, 255);
            var target = new RenderTexture(1600, 900, 24); target.Create(); camera.targetTexture = target;
            try
            {
                foreach (AlmanacCategory category in source.categories)
                {
                    foreach (AlmanacCategory candidate in source.categories)
                    {
                        string path = AnimationUtility.CalculateTransformPath(candidate.leftPageZone.parent, sourcePanel);
                        clone.transform.Find(path).gameObject.SetActive(candidate == category);
                        if (candidate.tabButton != null)
                        {
                            Transform bookmark = clone.transform.Find(AnimationUtility.CalculateTransformPath(candidate.tabButton.transform, sourcePanel));
                            bookmark.gameObject.SetActive(true);
                            var rect = bookmark as RectTransform;
                            rect.anchoredPosition = ((RectTransform)candidate.tabButton.transform).anchoredPosition +
                                (candidate == category ? new Vector2(0, source.selectedTabUpOffset) : Vector2.zero);
                        }
                        if (candidate.tabAlertIcon != null)
                            clone.transform.Find(AnimationUtility.CalculateTransformPath(candidate.tabAlertIcon.transform, sourcePanel)).gameObject.SetActive(false);
                    }
                    Transform left = clone.transform.Find(AnimationUtility.CalculateTransformPath(category.leftPageZone, sourcePanel));
                    Transform right = clone.transform.Find(AnimationUtility.CalculateTransformPath(category.rightPageZone, sourcePanel));
                    left.gameObject.SetActive(true); right.gameObject.SetActive(true);
                    left.GetComponent<Image>().enabled = right.GetComponent<Image>().enabled = true;
                    left.parent.Find("AuthoredBookBinding").gameObject.SetActive(true);
                    if (category.tabType == AlmanacTabType.Contracts)
                    {
                        ContractAlmanacTab contract = clone.GetComponentsInChildren<ContractAlmanacTab>(true)
                            .First(c => c.titleText != null && c.snapshotImage != null);
                        typeof(ContractAlmanacTab).GetMethod("EnsurePresentation", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(contract, null);
                        contract.titleText.text = "Canyon Crossing"; contract.clientText.text = "COMMISSIONED BY / THE COMMUNITY";
                        contract.descriptionText.text = "A completed project recorded in your engineer's field book. Review the design, rewards, and best result.";
                        contract.snapshotCaptionText.text = "Bridge photograph";
                        contract.pageCounterText.text = "PROJECT 01 / 03";
                        contract.rewardsText.text = "<b>2,500</b> CONTRACT PAY\n<b>150</b> EXPERIENCE";
                    }
                    string prefix = "Temp/AlmanacBook_" + sceneName + "_" + category.tabType;
                    SetPreviewScreen(source, (RectTransform)root.transform, camera, new Vector2(1920, 1080));
                    CapturePreview(camera, target, prefix + ".png");
                    var narrow = new RenderTexture(1440, 900, 24); narrow.Create();
                    try
                    {
                        SetPreviewScreen(source, (RectTransform)root.transform, camera, new Vector2(1728, 1080));
                        CapturePreview(camera, narrow, prefix + "_16x10.png");
                    }
                    finally { RenderTexture.active = null; SetPreviewScreen(source, (RectTransform)root.transform, camera, new Vector2(1920, 1080)); camera.targetTexture = target;
                        narrow.Release(); Object.DestroyImmediate(narrow); }
                    var phone = new RenderTexture(1950, 900, 24); phone.Create();
                    try
                    {
                        SetPreviewScreen(source, (RectTransform)root.transform, camera, new Vector2(2340, 1080));
                        CapturePreview(camera, phone, prefix + "_Phone.png");
                    }
                    finally { RenderTexture.active = null; SetPreviewScreen(source, (RectTransform)root.transform, camera, new Vector2(1920, 1080)); camera.targetTexture = target;
                        phone.Release(); Object.DestroyImmediate(phone); }
                    if (category.tabType == AlmanacTabType.Materials)
                    {
                        AlmanacLearningHub hub = clone.GetComponents<AlmanacLearningHub>().Last();
                        BridgeMaterialSO material = clone.GetComponentInChildren<AlmanacMaterialTab>(true).GetAllMaterials().Last();
                        // Populate a detail preview directly; no discovery/unlock/save mutations.
                        typeof(AlmanacLearningHub).GetMethod("PopulateDetail", BindingFlags.Instance | BindingFlags.NonPublic)
                            .Invoke(hub, new object[] { "BUILDING MATERIAL", material.GetDisplayName(), material.AlmanacImage,
                                material.AlmanacDescription,
                                "<b>COST / METER</b>    <color=#A85C21>P2,500</color>\n<b>MASS / METER</b>    150 kg\n<b>MAX LENGTH</b>    12 m\n<b>TENSION LIMIT</b>    999,999 N\n<b>COMPRESSION</b>    999,999 N" });
                        typeof(AlmanacLearningHub).GetMethod("SetDetailVisible", BindingFlags.Instance | BindingFlags.NonPublic)
                            .Invoke(hub, new object[] { true });
                        CapturePreview(camera, target, prefix + "_Detail.png");
                        Vector2 oldPivot = ((RectTransform)right).pivot;
                        Vector3 oldPosition = right.localPosition;
                        AlmanacBookMotion.SetPivotWithoutMoving((RectTransform)right, new Vector2(0, .5f));
                        right.localRotation = Quaternion.Euler(0, 45, 0);
                        CapturePreview(camera, target, prefix + "_Turn45.png");
                        right.localRotation = Quaternion.identity; ((RectTransform)right).pivot = oldPivot; right.localPosition = oldPosition;
                    }
                }
            }
            finally { camera.targetTexture = null; RenderTexture.active = null; target.Release(); Object.DestroyImmediate(target); }
        }
        finally { RenderTexture.active = previous; EditorSceneManager.ClosePreviewScene(preview); }
    }

    private static void CapturePreview(Camera camera, RenderTexture target, string path)
    {
        camera.targetTexture = target; Canvas.ForceUpdateCanvases();
        if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null) camera.Render();
        else UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,
            new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = target });
        RenderTexture.active = target;
        var texture = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        try { texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply(); File.WriteAllBytes(path, texture.EncodeToPNG()); }
        finally { Object.DestroyImmediate(texture); }
    }

    private static void SetPreviewScreen(AlmanacManager source, RectTransform canvas, Camera camera, Vector2 screen)
    {
        var scaler = source.almanacCanvas.GetComponentInParent<CanvasScaler>();
        Vector2 reference = scaler != null ? scaler.referenceResolution : new Vector2(1920, 1080);
        float match = scaler != null ? scaler.matchWidthOrHeight : .5f;
        float scale = Mathf.Pow(screen.x / reference.x, 1 - match) * Mathf.Pow(screen.y / reference.y, match);
        canvas.sizeDelta = screen / scale;
        camera.orthographicSize = canvas.sizeDelta.y * .005f;
    }
}
