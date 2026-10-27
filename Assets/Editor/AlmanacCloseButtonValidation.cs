#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Opt-in graphic-only Almanac CLOSE previews. Never opens/closes a live book or invokes a button.</summary>
[InitializeOnLoad]
public static class AlmanacCloseButtonValidation
{
    public const string RequestPath = "Temp/almanac-close-button.request";
    public const string ReportPath = "Logs/almanac-close-button.txt";
    private static bool running;
    private static double nextPoll;

    static AlmanacCloseButtonValidation() { EditorApplication.update += Poll; }
    private static bool Busy => running || EditorApplication.isPlayingOrWillChangePlaymode ||
        EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer ||
        PrefabStageUtility.GetCurrentPrefabStage() != null;

    private static void Poll()
    {
        if (Busy || EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1;
        if (!File.Exists(RequestPath)) return;
        File.Delete(RequestPath);
        Run();
    }

    [MenuItem("Tools/Civil Craft/Almanac/Validate CLOSE Button (Read Only)")]
    public static void Run()
    {
        if (Busy) return;
        running = true;
        var report = new StringBuilder("Almanac CLOSE: detached existing-control styling and button preservation\n");
        Scene[] scenes = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).ToArray();
        bool[] dirty = scenes.Select(scene => scene.isDirty).ToArray();
        GameObject[][] roots = scenes.Select(scene => scene.GetRootGameObjects()).ToArray();
        var files = scenes.Where(scene => File.Exists(scene.path)).ToDictionary(scene => scene.path, scene => Hash(scene.path));
        Scene active = SceneManager.GetActiveScene(); Object[] selection = Selection.objects; Object selected = Selection.activeObject;
        float timeScale = Time.timeScale;
        PlayerDataManager save = PlayerDataManager.Instance;
        PlayerData player = save != null ? save.CurrentData : null;
        string playerJson = player != null ? JsonUtility.ToJson(player) : null;
        try
        {
            AlmanacManager[] books = Resources.FindObjectsOfTypeAll<AlmanacManager>().Where(book => book != null &&
                book.gameObject.scene.IsValid() && book.gameObject.scene.isLoaded &&
                !EditorSceneManager.IsPreviewScene(book.gameObject.scene)).ToArray();
            Require(books.Length > 0, "Open a gameplay scene containing its authored Almanac first.");
            foreach (AlmanacManager source in books) Capture(source, report);
            report.Insert(0, "PASS\n");
        }
        catch (Exception error) { report.Insert(0, "FAILED\n"); report.AppendLine(error.ToString()); }
        finally
        {
            try
            {
                Require(SceneManager.sceneCount == scenes.Length && SceneManager.GetActiveScene() == active, "Loaded scene setup changed.");
                for (int index = 0; index < scenes.Length; index++)
                    Require(SceneManager.GetSceneAt(index) == scenes[index] && scenes[index].isDirty == dirty[index] &&
                        scenes[index].GetRootGameObjects().SequenceEqual(roots[index]), "Source scene order/roots/dirty state changed.");
                Require(files.All(pair => Hash(pair.Key) == pair.Value), "Saved scene bytes changed.");
                Require(Selection.activeObject == selected && Selection.objects.SequenceEqual(selection) && Time.timeScale == timeScale,
                    "Editor selection or time scale changed.");
                Require(PlayerDataManager.Instance == save && (player == null ||
                    (ReferenceEquals(player, save.CurrentData) && JsonUtility.ToJson(player) == playerJson)), "Live player data changed.");
                report.AppendLine("PASS: source scenes/bytes/dirty flags, callbacks, tutorial target references, selection, time scale and player data preserved. No live book, tutorial, UI click or save callbacks ran.");
            }
            catch (Exception error) { report.Insert(0, "FAILED SAFETY\n"); report.AppendLine(error.ToString()); }
            Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, report.ToString());
            running = false;
        }
    }

    private static void Capture(AlmanacManager source, StringBuilder report)
    {
        Require(source.Panel != null && source.Panel.transform is RectTransform, "Almanac panel is missing its authored rectangle.");
        Button[] matches = source.Panel.GetComponentsInChildren<Button>(true).Where(button =>
            Enumerable.Range(0, button.onClick.GetPersistentEventCount()).Any(index =>
                button.onClick.GetPersistentTarget(index) == source && button.onClick.GetPersistentMethodName(index) == "CloseAlmanac")).ToArray();
        Require(matches.Length == 1, "Expected the existing unique Almanac CloseAlmanac button in " + source.gameObject.scene.name);
        Button sourceClose = matches[0];
        RectTransform sourcePanel = (RectTransform)source.Panel.transform;
        var originals = source.Panel.GetComponentsInChildren<Component>(true).Cast<Object>().Append(source).Distinct()
            .ToDictionary(item => item, EditorJsonUtility.ToJson);
        var callbackJson = source.Panel.GetComponentsInChildren<Button>(true).ToDictionary(button => button, button => JsonUtility.ToJson(button.onClick));
        var tutorialJson = source.gameObject.scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<TutorialSequence>(true))
            .ToDictionary(sequence => sequence, sequence => EditorJsonUtility.ToJson(sequence));
        TMP_FontAsset font = Resources.Load<TMP_FontAsset>("Fonts & Materials/Bekind Sans SDF");
        if (font == null) font = source.Panel.GetComponentsInChildren<TMP_Text>(true).Select(text => text.font).FirstOrDefault(item => item != null);
        Require(font != null, "The gameplay font is unavailable for the CLOSE label.");
        var fonts = source.Panel.GetComponentsInChildren<TMP_Text>(true).Select(text => text.font).Append(font)
            .Where(item => item != null).Select(AssetDatabase.GetAssetPath).Where(path => !string.IsNullOrEmpty(path)).Distinct()
            .ToDictionary(path => path, AssetDatabase.GetAssetDependencyHash);
        CanvasScaler sourceScaler = source.Panel.GetComponentInParent<CanvasScaler>(true);
        Vector2 reference = sourceScaler != null ? sourceScaler.referenceResolution : new Vector2(1920, 1080);
        float match = sourceScaler != null ? sourceScaler.matchWidthOrHeight : .5f;
        Scene preview = EditorSceneManager.NewPreviewScene();
        RenderTexture previous = RenderTexture.active;
        try
        {
            var root = new GameObject("Detached Almanac CLOSE preview", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            SceneManager.MoveGameObjectToScene(root, preview);
            var canvasRect = (RectTransform)root.transform; canvasRect.localScale = Vector3.one * .01f;
            Canvas canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            var map = new Dictionary<Transform, RectTransform>();
            RectTransform panel = CloneArt(sourcePanel, canvasRect, map);
            panel.anchorMin = Vector2.zero; panel.anchorMax = Vector2.one;
            panel.offsetMin = panel.offsetMax = Vector2.zero; panel.localScale = Vector3.one; panel.localRotation = Quaternion.identity;
            panel.gameObject.SetActive(true);
            // Only display the General chapter, but expose every authored tab so
            // locked categories cannot hide an overlap in this geometry fixture.
            if (source.categories != null)
                for (int index = 0; index < source.categories.Count; index++)
                {
                    AlmanacCategory category = source.categories[index];
                    if (category == null) continue;
                    if (category.leftPageZone != null && map.TryGetValue(category.leftPageZone.parent, out RectTransform frame))
                        frame.gameObject.SetActive(index == 0);
                    if (category.tabButton != null && map.TryGetValue(category.tabButton.transform, out RectTransform tab))
                        tab.gameObject.SetActive(true);
                }
            RectTransform closeRect = map[sourceClose.transform];
            Button close = closeRect.gameObject.AddComponent<Button>();
            EditorUtility.CopySerialized(sourceClose, close);
            if (sourceClose.targetGraphic != null && map.TryGetValue(sourceClose.targetGraphic.transform, out RectTransform target))
                close.targetGraphic = target.GetComponent<Graphic>();
            string calls = JsonUtility.ToJson(close.onClick);
            Transform parentBefore = close.transform.parent;
            int buttonId = close.GetInstanceID(), rectId = close.transform.GetInstanceID();
            RectTransform control = AlmanacCloseButtonStyle.Apply(close, font);
            Require(control != null && close.GetInstanceID() == buttonId && close.transform.GetInstanceID() == rectId &&
                close.transform.parent == parentBefore && JsonUtility.ToJson(close.onClick) == calls,
                "Styling replaced the original control/target/callback or reparented the tutorial's button reference.");
            int children = panel.GetComponentsInChildren<Transform>(true).Length;
            Require(AlmanacCloseButtonStyle.Apply(close, font) == control &&
                panel.GetComponentsInChildren<Transform>(true).Length == children && JsonUtility.ToJson(close.onClick) == calls,
                "Repeated styling duplicates controls or changes the existing callback.");
            Require(close.GetComponent<TutorialHoldToSkip>() == null, "Almanac CLOSE must stay an ordinary tap, not a hold-to-skip control.");
            ValidateDirectFixture(canvasRect, font, sourceClose);
            var cameraObject = new GameObject("Detached Almanac CLOSE camera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, preview);
            Camera camera = cameraObject.GetComponent<Camera>(); camera.scene = preview; camera.useOcclusionCulling = false;
            camera.orthographic = true; camera.nearClipPlane = .1f; camera.farClipPlane = 100;
            camera.transform.position = new Vector3(0, 0, -20);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(119, 156, 165, 255);
            canvas.worldCamera = camera;
            Vector2Int[] screens = { new Vector2Int(1280, 720), new Vector2Int(1600, 1200), new Vector2Int(2340, 1080) };
            Vector2 firstPosition = Vector2.zero;
            foreach (Vector2Int screen in screens)
            {
                float scale = Mathf.Pow(screen.x / reference.x, 1f - match) * Mathf.Pow(screen.y / reference.y, match);
                canvasRect.sizeDelta = new Vector2(screen.x, screen.y) / scale; canvas.scaleFactor = scale;
                camera.aspect = (float)screen.x / screen.y; camera.orthographicSize = canvasRect.rect.height * .005f;
                var texture = new RenderTexture(screen.x, screen.y, 24); texture.Create(); camera.targetTexture = texture;
                try
                {
                    Canvas.ForceUpdateCanvases();
                    foreach (TMP_Text text in panel.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate(true, true);
                    TMP_Text label = ValidateStyledControl(close, control, font, canvasRect, camera);
                    Rect buttonBounds = Bounds(control, camera);
                    if (source.categories != null)
                        foreach (AlmanacCategory category in source.categories)
                            if (category != null && category.tabButton != null && map.TryGetValue(category.tabButton.transform, out RectTransform tab))
                                Require(!buttonBounds.Overlaps(Bounds(tab, camera)), "CLOSE overlaps an Almanac category tab.");
                    foreach (TMP_Text text in panel.GetComponentsInChildren<TMP_Text>(false))
                    {
                        if (text == label || text.transform.IsChildOf(close.transform)) continue;
                        foreach (TMP_CharacterInfo character in text.textInfo.characterInfo.Take(text.textInfo.characterCount))
                        {
                            if (!character.isVisible) continue;
                            Rect glyph = GlyphBounds(text, character, camera);
                            Require(!buttonBounds.Overlaps(glyph), "CLOSE overlaps page/header text: " + text.name);
                        }
                    }
                    // A detached world-space Canvas needs its first native
                    // render before GraphicRaycaster has valid graphic depths.
                    // Keep a PNG even on a later hit-test failure for diagnosis.
                    string path = "Temp/AlmanacCloseButton/" + source.gameObject.scene.name + "-CLOSE-" + screen.x + "x" + screen.y + ".png";
                    Render(camera, texture, path);
                    var hits = new List<RaycastResult>();
                    Vector2 pointerPosition = RectTransformUtility.WorldToScreenPoint(camera, closeRect.TransformPoint(closeRect.rect.center));
                    root.GetComponent<GraphicRaycaster>().Raycast(new PointerEventData(null) {
                        position = pointerPosition }, hits);
                    Require(hits.Count > 0 && hits[0].gameObject == close.gameObject,
                        "The existing CLOSE button is not the top native raycast hit after rendering. " +
                        "position=" + pointerPosition + "; expected=" + Hierarchy(close.transform) +
                        "; depth=" + close.targetGraphic.depth + "; cull=" + close.targetGraphic.canvasRenderer.cull +
                        "; active=" + close.targetGraphic.isActiveAndEnabled + "; raycast=" + close.targetGraphic.raycastTarget +
                        "; cameraPixelRect=" + camera.pixelRect + "; target=" + texture.width + "x" + texture.height +
                        "; canvas=" + canvas.renderMode + "/enabled:" + canvas.enabled +
                        "; hits=" + hits.Count + " [" + string.Join("; ", hits.Select(hit =>
                            Hierarchy(hit.gameObject.transform) + " depth=" + hit.depth + " distance=" + hit.distance)) + "]" +
                        "; active graphics [" + string.Join("; ", panel.GetComponentsInChildren<Graphic>(false).Select(graphic =>
                            Hierarchy(graphic.transform) + " layer=" + graphic.gameObject.layer + " depth=" + graphic.depth +
                            " cull=" + graphic.canvasRenderer.cull + " raycast=" + graphic.raycastTarget)) + "]");
                    if (screen == screens[0]) firstPosition = control.anchoredPosition;
                    report.AppendLine("PASS: " + source.gameObject.scene.name + " " + screen.x + "x" + screen.y +
                        "; existing close Button/Rect/callback preserved, SKIP-style cream/brown CLOSE, complete readable glyphs, native tap target, no tab/header overlap and in-bounds layout. " + path);
                }
                finally { camera.targetTexture = null; texture.Release(); Object.DestroyImmediate(texture); }
            }
            Vector2Int first = screens[0];
            float firstScale = Mathf.Pow(first.x / reference.x, 1f - match) * Mathf.Pow(first.y / reference.y, match);
            canvasRect.sizeDelta = new Vector2(first.x, first.y) / firstScale;
            Canvas.ForceUpdateCanvases();
            Require(Vector2.Distance(firstPosition, control.anchoredPosition) < .01f && JsonUtility.ToJson(close.onClick) == calls,
                "Resize/repeated rendering changed CLOSE placement or its persistent callback.");
            report.AppendLine("PASS: " + source.gameObject.scene.name + " wrapped and direct-prefab-style controls are idempotent, preserve callbacks, retain tutorial target identity and remain single-tap actions.");
        }
        finally
        {
            RenderTexture.active = previous;
            EditorSceneManager.ClosePreviewScene(preview);
            Require(originals.All(pair => pair.Key != null && EditorJsonUtility.ToJson(pair.Key) == pair.Value) &&
                callbackJson.All(pair => pair.Key != null && JsonUtility.ToJson(pair.Key.onClick) == pair.Value) &&
                tutorialJson.All(pair => pair.Key != null && EditorJsonUtility.ToJson(pair.Key) == pair.Value) &&
                fonts.All(pair => AssetDatabase.GetAssetDependencyHash(pair.Key) == pair.Value),
                "An authored book, close callback, tutorial pointer reference or font asset changed during detached validation.");
        }
    }

    private static void ValidateDirectFixture(RectTransform parent, TMP_FontAsset font, Button source)
    {
        var owner = new GameObject("Direct prefab-style CLOSE fixture", typeof(RectTransform), typeof(Image), typeof(Button));
        owner.transform.SetParent(parent, false);
        Button button = owner.GetComponent<Button>(); EditorUtility.CopySerialized(source, button);
        button.targetGraphic = owner.GetComponent<Image>();
        string calls = JsonUtility.ToJson(button.onClick);
        RectTransform control = AlmanacCloseButtonStyle.Apply(button, font);
        int count = owner.GetComponentsInChildren<Transform>(true).Length;
        Require(control == owner.transform && AlmanacCloseButtonStyle.Apply(button, font) == control &&
            count == owner.GetComponentsInChildren<Transform>(true).Length && JsonUtility.ToJson(button.onClick) == calls &&
            owner.GetComponent<TutorialHoldToSkip>() == null, "Direct prefab-style CLOSE styling is not idempotent or changes its callback/input behavior.");
        owner.SetActive(false);
    }

    private static TMP_Text ValidateStyledControl(Button button, RectTransform control, TMP_FontAsset font, RectTransform canvas, Camera camera)
    {
        TMP_Text[] labels = button.GetComponentsInChildren<TMP_Text>(true).Where(text => text.gameObject.activeInHierarchy && text.text == "CLOSE").ToArray();
        Require(labels.Length == 1, "Expected exactly one active CLOSE label, not X or duplicate text.");
        TMP_Text label = labels[0]; label.ForceMeshUpdate(true, true);
        Require(label.font == font && label.fontSize >= 22f && label.fontSize <= 32.1f && !label.isTextTruncated && !label.isTextOverflowing &&
            label.GetParsedText() == "CLOSE" && !label.raycastTarget,
            "CLOSE label changed font, shrank too far, lost characters or blocks taps.");
        Require(button.targetGraphic != null && button.targetGraphic.gameObject == button.gameObject && button.targetGraphic.raycastTarget,
            "Existing close Button no longer owns its root hit graphic.");
        Require(button.GetComponentsInChildren<Graphic>(true).Where(graphic => graphic.transform != button.transform).All(graphic => !graphic.raycastTarget),
            "Decorative CLOSE face/label captures button taps.");
        Image background = button.GetComponent<Image>();
        Require(background != null && background.sprite != null && background.type == Image.Type.Sliced &&
            Same(background.color, new Color32(145, 84, 48, 255)), "CLOSE outer frame does not match the SKIP style.");
        Require(button.GetComponentsInChildren<Image>(true).Any(image => image != background && image.gameObject.activeInHierarchy &&
            Same(image.color, new Color32(239, 216, 167, 255))), "CLOSE cream face does not match SKIP.");
        Require(Same(label.color, new Color32(132, 74, 43, 255)), "CLOSE label color does not match SKIP.");
        Rect bounds = Bounds(control, camera);
        Require(Contains(camera.pixelRect, bounds), "CLOSE leaves the screen at this aspect ratio.");
        Require(bounds.height >= 44f && bounds.width >= 88f, "CLOSE tap target is too small on a phone.");
        Require(Mathf.Abs(control.rect.width - 190f) < .1f && Mathf.Abs(control.rect.height - 76f) < .1f,
            "CLOSE no longer uses the SKIP button's 190x76 presentation.");
        foreach (TMP_CharacterInfo character in label.textInfo.characterInfo.Take(label.textInfo.characterCount))
            if (character.isVisible) Require(Contains(bounds, GlyphBounds(label, character, camera)), "A CLOSE glyph is clipped by its button.");
        return label;
    }

    private static RectTransform CloneArt(RectTransform source, Transform parent, Dictionary<Transform, RectTransform> map)
    {
        var item = new GameObject(source.name, typeof(RectTransform)); item.layer = source.gameObject.layer;
        var rect = (RectTransform)item.transform; rect.SetParent(parent, false);
        rect.anchorMin = source.anchorMin; rect.anchorMax = source.anchorMax; rect.pivot = source.pivot;
        rect.sizeDelta = source.sizeDelta; rect.anchoredPosition = source.anchoredPosition;
        rect.localScale = source.localScale; rect.localRotation = source.localRotation;
        map.Add(source, rect);
        Image image = source.GetComponent<Image>();
        RawImage raw = source.GetComponent<RawImage>();
        TextMeshProUGUI text = source.GetComponent<TextMeshProUGUI>();
        if (image != null)
        {
            Image copy = item.AddComponent<Image>(); EditorUtility.CopySerialized(image, copy); copy.raycastTarget = false;
        }
        else if (raw != null)
        {
            RawImage copy = item.AddComponent<RawImage>(); EditorUtility.CopySerialized(raw, copy); copy.raycastTarget = false;
        }
        else if (text != null)
        {
            TextMeshProUGUI copy = item.AddComponent<TextMeshProUGUI>(); EditorUtility.CopySerialized(text, copy); copy.raycastTarget = false;
        }
        foreach (Transform child in source)
            if (child is RectTransform childRect) CloneArt(childRect, rect, map);
        item.SetActive(source.gameObject.activeSelf);
        return rect;
    }

    private static Rect GlyphBounds(TMP_Text text, TMP_CharacterInfo character, Camera camera)
    {
        Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, text.transform.TransformPoint(character.bottomLeft));
        Vector2 max = RectTransformUtility.WorldToScreenPoint(camera, text.transform.TransformPoint(character.topRight));
        return Rect.MinMaxRect(Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y), Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));
    }
    private static Rect Bounds(RectTransform rect, Camera camera)
    {
        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
        Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]), max = min;
        foreach (Vector3 corner in corners)
        { Vector2 point = RectTransformUtility.WorldToScreenPoint(camera, corner); min = Vector2.Min(min, point); max = Vector2.Max(max, point); }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }
    private static string Hierarchy(Transform transform)
    {
        var names = new Stack<string>();
        for (Transform current = transform; current != null; current = current.parent) names.Push(current.name);
        return string.Join("/", names);
    }
    private static bool Contains(Rect outer, Rect inner) => inner.xMin >= outer.xMin - .5f && inner.yMin >= outer.yMin - .5f &&
        inner.xMax <= outer.xMax + .5f && inner.yMax <= outer.yMax + .5f;
    private static bool Same(Color value, Color32 expected)
    {
        Color32 actual = value;
        return actual.r == expected.r && actual.g == expected.g && actual.b == expected.b && actual.a == expected.a;
    }
    private static void Render(Camera camera, RenderTexture target, string path)
    {
        if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null) camera.Render();
        else UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,
            new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = target });
        RenderTexture.active = target;
        var pixels = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        try
        {
            pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
            Directory.CreateDirectory("Temp/AlmanacCloseButton"); File.WriteAllBytes(path, pixels.EncodeToPNG());
        }
        finally { Object.DestroyImmediate(pixels); }
    }
    private static string Hash(string path) { using (var algorithm = SHA256.Create()) return BitConverter.ToString(algorithm.ComputeHash(File.ReadAllBytes(path))); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
#endif
