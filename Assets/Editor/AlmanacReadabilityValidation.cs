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
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;

/// <summary>Renders real runtime bookmarks/profile on disposable copies. Never saves a source scene.</summary>
[InitializeOnLoad]
public static class AlmanacReadabilityValidation
{
    public const string RequestPath = "Temp/almanac-readability-v1.request";
    public const string ReportPath = "Logs/almanac-readability-v1.txt";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static double nextCheck;
    private static bool running;
    static AlmanacReadabilityValidation() { EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (running || EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 1;
        if (!File.Exists(RequestPath) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer || PrefabStageUtility.GetCurrentPrefabStage() != null) return;
        File.Delete(RequestPath);
        Run();
    }

    [MenuItem("Tools/Civil Craft/Almanac/Validate Runtime Book Layout (Read Only)")]
    public static void Run()
    {
        if (running || EditorApplication.isPlayingOrWillChangePlaymode) return;
        running = true;
        var report = new StringBuilder();
        try
        {
            AlmanacManager[] sources = Resources.FindObjectsOfTypeAll<AlmanacManager>().Where(book => book != null &&
                book.gameObject.scene.IsValid() && book.gameObject.scene.isLoaded &&
                !EditorSceneManager.IsPreviewScene(book.gameObject.scene) && book.Panel != null && book.categories.Count > 0).ToArray();
            Require(sources.Length > 0, "Open a gameplay scene with an Almanac first.");
            foreach (AlmanacManager source in sources) Capture(source, report);
            report.AppendLine("PASS: source hierarchies, callbacks, scene dirty flags and player data unchanged. No scene save or live gameplay action.");
            Write("PASS\n" + report);
            Debug.Log("[Almanac readability] Runtime bookmarks/profile checks passed. " + ReportPath);
        }
        catch (Exception error) { Write("FAILED\n" + error + "\n" + report); Debug.LogException(error); }
        finally { running = false; }
    }

    private static void Capture(AlmanacManager source, StringBuilder report)
    {
        Transform sourcePanel = source.categories[0].leftPageZone.parent.parent.parent;
        bool dirty = source.gameObject.scene.isDirty;
        var before = sourcePanel.GetComponentsInChildren<Component>(true).Where(item => item is RectTransform || item is Button || item is TMP_Text || item is AlmanacPlayerStats)
            .ToDictionary(item => item, EditorJsonUtility.ToJson);
        PlayerData player = PlayerDataManager.Instance != null ? PlayerDataManager.Instance.CurrentData : null;
        string playerBefore = player != null ? JsonUtility.ToJson(player) : null;
        Scene preview = EditorSceneManager.NewPreviewScene();
        RenderTexture previous = RenderTexture.active;
        try
        {
            var root = new GameObject("Almanac Runtime Layout Canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            SceneManager.MoveGameObjectToScene(root, preview);
            var canvasRect = (RectTransform)root.transform;
            canvasRect.sizeDelta = new Vector2(1920, 1080); canvasRect.localScale = Vector3.one * .01f;
            Canvas canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            GameObject clone = Object.Instantiate(sourcePanel.gameObject, root.transform, false);
            clone.SetActive(true); Stretch((RectTransform)clone.transform);
            foreach (CanvasGroup group in clone.GetComponentsInChildren<CanvasGroup>(true))
            { group.alpha = 1f; group.interactable = true; group.blocksRaycasts = true; }
            var ownerObject = new GameObject("Almanac Runtime Layout Owner"); SceneManager.MoveGameObjectToScene(ownerObject, preview);
            var owner = ownerObject.AddComponent<AlmanacManager>(); owner.almanacCanvas = clone;
            owner.selectedTabUpOffset = source.selectedTabUpOffset;
            foreach (AlmanacCategory category in source.categories)
                owner.categories.Add(new AlmanacCategory {
                    categoryName = category.categoryName, tabType = category.tabType, visibilityMode = category.visibilityMode,
                    leftPageZone = Map(clone.transform, category.leftPageZone, sourcePanel),
                    rightPageZone = Map(clone.transform, category.rightPageZone, sourcePanel),
                    tabButton = category.tabButton != null ? Map(clone.transform, category.tabButton.transform, sourcePanel).GetComponent<Button>() : null,
                    tabAlertIcon = category.tabAlertIcon != null ? Map(clone.transform, category.tabAlertIcon.transform, sourcePanel).gameObject : null,
                    activeSprite = category.activeSprite, inactiveSprite = category.inactiveSprite
                });
            AlmanacBookMotion motion = ownerObject.AddComponent<AlmanacBookMotion>();
            typeof(AlmanacManager).GetField("bookMotion", Private).SetValue(owner, motion);
            GameObject[] sourceFrames = (GameObject[])typeof(AlmanacBookMotion).GetField("chapterFrames", Private).GetValue(source.BookMotion);
            GameObject[] frames = sourceFrames.Select(frame => frame != null ? Map(clone.transform, frame.transform, sourcePanel).gameObject : null).ToArray();
            typeof(AlmanacBookMotion).GetField("chapterFrames", Private).SetValue(motion, frames);
            // Exactly the production order: install the runtime quiz before the
            // book captures bookmark Y positions and initializes its pages.
            AlmanacQuizAccess.Install(owner);
            Require(owner.categories.Count == source.categories.Count + 1, "Runtime Quiz category missing/duplicated.");
            typeof(AlmanacManager).GetMethod("InitializeBook", Private).Invoke(owner, null);
            frames = (GameObject[])typeof(AlmanacBookMotion).GetField("chapterFrames", Private).GetValue(motion);
            var corners = frames.Where(frame => frame != null).ToDictionary(frame => frame, frame => Corners((RectTransform)frame.transform));
            motion.Bind(owner);
            foreach (var pair in corners)
                Require(pair.Value.Zip(Corners((RectTransform)pair.Key.transform), (a, b) => Vector3.Distance(a, b)).All(distance => distance < .001f),
                    "Moving the cover layer shifted its geometry.");
            SetText(clone, "EngineerName", "Engineer: Engineer");
            SetText(clone, "RankValue", "Journeyman");
            SetText(clone, "GoldValue", "2,147,483,647");
            SetText(clone, "SecondaryCurrencyValue", "Unavailable");
            SetText(clone, "ExpValue", "357 / 600");
            SetText(clone, "ExpRemaining", "243 EXP to next rank");
            SetText(clone, "AchievementSummary", "4 achievements unlocked\nLatest: Budget Manager");
            var cameraObject = new GameObject("Almanac Runtime Layout Camera", typeof(Camera)); SceneManager.MoveGameObjectToScene(cameraObject, preview);
            Camera camera = cameraObject.GetComponent<Camera>(); camera.scene = preview; canvas.worldCamera = camera;
            camera.orthographic = true; camera.nearClipPlane = .1f; camera.farClipPlane = 100;
            camera.transform.position = new Vector3(0, 0, -20); camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(138, 111, 88, 255); camera.useOcclusionCulling = false;
            foreach (Vector2 screen in new[] { new Vector2(1920, 900), new Vector2(1600, 720), new Vector2(2048, 1280) })
            {
                CanvasScaler scaler = source.Panel.GetComponentInParent<CanvasScaler>();
                Vector2 reference = scaler != null ? scaler.referenceResolution : new Vector2(1920, 1080);
                float match = scaler != null ? scaler.matchWidthOrHeight : .5f;
                float scale = Mathf.Pow(screen.x / reference.x, 1 - match) * Mathf.Pow(screen.y / reference.y, match);
                canvasRect.sizeDelta = screen / scale; camera.orthographicSize = canvasRect.sizeDelta.y * .005f;
                foreach (bool materialsUnlocked in new[] { true, false })
                foreach (bool quizSelected in new[] { false, true })
                {
                    Show(owner, motion, quizSelected ? owner.categories.Count - 1 : 0, materialsUnlocked);
                    if (quizSelected) ShowAvailableQuizFixture(owner);
                    Canvas.ForceUpdateCanvases();
                    foreach (TMP_Text text in clone.GetComponentsInChildren<TMP_Text>(false)) text.ForceMeshUpdate(false, true);
                    ValidateBookmarks(owner, frames, report);
                    if (!quizSelected) ValidateProfile(clone);
                    string file = "Temp/AlmanacReadability_" + source.gameObject.scene.name + "_" + (quizSelected ? "Quiz" : "Profile") +
                        (materialsUnlocked ? "_MaterialsUnlocked" : "") + "_" + (int)screen.x + "x" + (int)screen.y + ".png";
                    Render(camera, screen, file);
                    // Native CanvasRenderer depths are assigned by rendering,
                    // not merely by ForceUpdateCanvases in a preview scene.
                    ValidateClick(owner.categories.Last().tabButton, root.GetComponent<GraphicRaycaster>(), camera, true);
                    if (quizSelected) ValidateClick((Button)typeof(AlmanacQuizAccess).GetField("start", Private)
                        .GetValue(owner.GetComponent<AlmanacQuizAccess>()), root.GetComponent<GraphicRaycaster>(), camera, false);
                    report.AppendLine("PASS: " + file + "; cover < tabs < pages, compact visible slots, readable one-line currency.");
                }
            }
            // Reapply/rebind and change visible chapter count to expose cumulative
            // offset/reparenting bugs without touching real unlock/player state.
            motion.Bind(owner); AlmanacBookmarkLayout.Reflow(owner.categories);
            ValidateBookmarks(owner, frames, report);
        }
        finally
        {
            RenderTexture.active = previous; EditorSceneManager.ClosePreviewScene(preview);
            Require(source.gameObject.scene.isDirty == dirty && before.All(pair => pair.Key != null && EditorJsonUtility.ToJson(pair.Key) == pair.Value), "Source book changed during detached checks.");
            Require(player == null || (PlayerDataManager.Instance != null && ReferenceEquals(player, PlayerDataManager.Instance.CurrentData) && JsonUtility.ToJson(player) == playerBefore), "Player data changed during preview.");
        }
    }

    private static void Show(AlmanacManager book, AlmanacBookMotion motion, int selected, bool materialsUnlocked)
    {
        book.categories.Last().tabButton.interactable = false;
        foreach (AlmanacCategory category in book.categories)
        {
            category.tabButton.gameObject.SetActive(category.tabType != AlmanacTabType.Materials || materialsUnlocked);
            if (category.tabAlertIcon != null) category.tabAlertIcon.SetActive(false);
            foreach (Transform zone in new[] { category.leftPageZone, category.rightPageZone })
            {
                zone.gameObject.SetActive(true);
                foreach (Transform child in zone) child.gameObject.SetActive(category == book.categories[selected]);
            }
            RectTransform rect = (RectTransform)category.tabButton.transform;
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, 4f + (category == book.categories[selected] ? book.selectedTabUpOffset : 0f));
        }
        AlmanacBookmarkLayout.Reflow(book.categories);
        book.OnCategoryChanged?.Invoke(selected); // Preview-local visuals only; no SelectCategory/mark-read actions.
    }

    private static void ShowAvailableQuizFixture(AlmanacManager book)
    {
        // Presentation fixture only: no TryOffer, real callback owner or session.
        AlmanacQuizAccess access = book.GetComponent<AlmanacQuizAccess>();
        Button tab = book.categories.Last().tabButton; tab.interactable = true;
        typeof(AlmanacQuizAccess).GetMethod("RefreshTabContrast", Private).Invoke(access, null);
        SetText(book.Panel, "Quiz Title", "Truss Bridge Quiz");
        SetText(book.Panel, "Quiz Guidance", "Review your lessons before starting.\n\nAnswer all 5 questions, then press Submit. You can move between questions before submitting.\n\nOnce started, finish the quiz before returning to the Almanac. Any score lets you continue; no rewards are added.");
        Button start = (Button)typeof(AlmanacQuizAccess).GetField("start", Private).GetValue(access);
        start.gameObject.SetActive(true); start.interactable = true;
    }

    private static void ValidateClick(Button button, GraphicRaycaster raycaster, Camera camera, bool bookmark)
    {
        Require(button != null, "Preview button is missing.");
        RectTransform rect = bookmark ? button.GetComponentInChildren<TMP_Text>(true).rectTransform : (RectTransform)button.transform;
        Vector2 point = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.center));
        var hits = new List<RaycastResult>();
        raycaster.Raycast(new PointerEventData(null) { position = point }, hits);
        GameObject handler = hits.Count > 0 ? ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject) : null;
        Require(handler == button.gameObject, "Visible control is covered or cannot receive input: " + button.name +
            "; graphic depth=" + (button.targetGraphic != null ? button.targetGraphic.depth.ToString() : "none") +
            "; registered=" + GraphicRegistry.GetGraphicsForCanvas(raycaster.GetComponent<Canvas>()).Count +
            "; hits=" + string.Join(",", hits.Select(hit => hit.gameObject.name)));
    }

    private static void ValidateBookmarks(AlmanacManager book, GameObject[] frames, StringBuilder report)
    {
        var visible = book.categories.Where(category => category.tabButton.gameObject.activeSelf).ToArray();
        RectTransform first = (RectTransform)visible[0].tabButton.transform;
        Transform covers = first.parent.Find("Almanac Cover Layer");
        Require(covers != null, "Separate cover layer missing.");
        float previousRight = -1;
        foreach (AlmanacCategory category in visible)
        {
            var tab = (RectTransform)category.tabButton.transform;
            Require(covers.GetSiblingIndex() < tab.GetSiblingIndex() && tab.GetSiblingIndex() < category.leftPageZone.parent.GetSiblingIndex(), "Bookmark must render above cover and below paper.");
            Require(tab.anchorMin.x > previousRight && tab.anchorMax.x < 1f, "Visible bookmarks overlap or leave the book.");
            previousRight = tab.anchorMax.x;
        }
        Require(frames.All(frame => frame == null || frame.transform.parent == covers), "A chapter cover remained above bookmarks.");
        TMP_Text label = book.categories.Last().tabButton.GetComponentInChildren<TMP_Text>(true);
        Require(label != null && !label.raycastTarget && !label.enableWordWrapping && label.textInfo.lineCount <= 1 &&
            !label.isTextOverflowing && !label.isTextTruncated && label.color.r < .6f, "Quiz bookmark label is unreadable or intercepts input.");
    }

    private static void ValidateProfile(GameObject clone)
    {
        foreach (string name in new[] { "GoldValue", "SecondaryCurrencyValue", "GoldLabel", "SecondaryCurrencyLabel", "RankValue", "ExpValue", "ExpRemaining", "BridgesLabel", "ContractsLabel" })
        {
            TMP_Text text = FindText(clone, name);
            Require(text != null && text.textInfo.lineCount == 1 && !text.isTextTruncated && !text.isTextOverflowing,
                "Profile text does not fit one line: " + name + " at " + (text != null ? text.fontSize.ToString() : "missing"));
        }
        Require(FindText(clone, "SecondaryCurrencyValue").text == "Unavailable", "Wallet availability was misrepresented.");
    }
    private static TMP_Text FindText(GameObject root, string name) => root.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(text => text.name == name);
    private static void SetText(GameObject root, string name, string value) { TMP_Text text = FindText(root, name); if (text != null) text.text = value; }
    private static Transform Map(Transform clone, Transform target, Transform source) => target != null ? clone.Find(AnimationUtility.CalculateTransformPath(target, source)) : null;
    private static Vector3[] Corners(RectTransform rect) { var result = new Vector3[4]; rect.GetWorldCorners(result); return result; }
    private static void Stretch(RectTransform rect) { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; rect.localScale = Vector3.one; }
    private static void Render(Camera camera, Vector2 screen, string path)
    {
        int height = 900, width = Mathf.RoundToInt(height * screen.x / screen.y);
        var target = new RenderTexture(width, height, 24); target.Create();
        try
        {
            camera.targetTexture = target; camera.aspect = screen.x / screen.y;
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null) camera.Render();
            else UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera, new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture.active = target; var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try { texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply(); File.WriteAllBytes(path, texture.EncodeToPNG()); }
            finally { Object.DestroyImmediate(texture); }
        }
        finally { camera.targetTexture = null; RenderTexture.active = null; target.Release(); Object.DestroyImmediate(target); }
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Write(string text) { Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, text); }
}
#endif
