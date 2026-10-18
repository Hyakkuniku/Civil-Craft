#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Opt-in native text/layout checks on disposable copies of the authored tutorial card.</summary>
[InitializeOnLoad]
public static class TutorialCenterLayoutValidation
{
    public const string RequestPath = "Temp/tutorial-center-layout-v1.request";
    public const string ReportPath = "Logs/tutorial-center-layout-v1.txt";
    private static bool running;
    private static double nextCheck;

    private sealed class MessageCase
    {
        public string label;
        public string message;
        public Vector2 offset;
    }

    static TutorialCenterLayoutValidation() { EditorApplication.update += Poll; }

    private static bool Busy => running || EditorApplication.isCompiling || EditorApplication.isUpdating ||
        EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer ||
        PrefabStageUtility.GetCurrentPrefabStage() != null;

    private static void Poll()
    {
        if (Busy || EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 1;
        if (!File.Exists(RequestPath)) return;
        File.Delete(RequestPath);
        Run();
    }

    [MenuItem("Tools/Civil Craft/Tutorials/Validate Center Card Layout (Read Only)")]
    public static void Run()
    {
        if (Busy) return;
        running = true;
        var report = new StringBuilder();
        Scene active = SceneManager.GetActiveScene();
        Object[] selection = Selection.objects;
        Object selected = Selection.activeObject;
        float timeScale = Time.timeScale;
        var scenes = new Dictionary<Scene, Tuple<bool, GameObject[]>>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            scenes.Add(scene, Tuple.Create(scene.isDirty, scene.GetRootGameObjects()));
        }
        PlayerData player = PlayerDataManager.Instance != null ? PlayerDataManager.Instance.CurrentData : null;
        string playerBefore = player != null ? JsonUtility.ToJson(player) : null;
        try
        {
            Directory.CreateDirectory("Temp");
            TutorialManager[] managers = Resources.FindObjectsOfTypeAll<TutorialManager>().Where(manager =>
                manager != null && manager.gameObject.scene.IsValid() && manager.gameObject.scene.isLoaded &&
                !EditorSceneManager.IsPreviewScene(manager.gameObject.scene)).ToArray();
            Require(managers.Length > 0, "Open a gameplay scene with its authored TutorialManager first.");
            foreach (TutorialManager manager in managers) Capture(manager, report);
            Require(SceneManager.sceneCount == scenes.Count, "Loaded scene count changed.");
            foreach (var entry in scenes)
                Require(entry.Key.isDirty == entry.Value.Item1 && entry.Key.GetRootGameObjects().SequenceEqual(entry.Value.Item2),
                    "Source scene roots or dirty state changed: " + entry.Key.name);
            Require(SceneManager.GetActiveScene() == active && Selection.objects.SequenceEqual(selection) &&
                Selection.activeObject == selected && Time.timeScale == timeScale, "Editor selection, active scene or time scale changed.");
            Require(player == null || (PlayerDataManager.Instance != null &&
                ReferenceEquals(player, PlayerDataManager.Instance.CurrentData) && JsonUtility.ToJson(player) == playerBefore),
                "Player data changed during detached rendering.");
            report.AppendLine("PASS: complete authored messages fit; safe bounds, readable font, repeated steps, LowCenter, resize and oversize containment checked.");
            report.AppendLine("PASS: Continue controls, scene content/dirty state, selection, time scale and player data unchanged. No tutorial events, real UI clicks, scene saves or gameplay actions were invoked.");
            Write("PASS\n" + report);
            Debug.Log("[Tutorial center layout] Native layout checks passed. " + ReportPath);
        }
        catch (Exception error)
        {
            Write("FAILED\n" + error + "\n" + report);
            Debug.LogException(error);
        }
        finally { running = false; }
    }

    private static void Capture(TutorialManager source, StringBuilder report)
    {
        var serialized = new SerializedObject(source);
        GameObject sourcePanelObject = serialized.FindProperty("centerPanel").objectReferenceValue as GameObject;
        TextMeshProUGUI sourceText = serialized.FindProperty("centerText").objectReferenceValue as TextMeshProUGUI;
        GameObject next = serialized.FindProperty("nextButton").objectReferenceValue as GameObject;
        Require(sourcePanelObject != null && sourceText != null && next != null,
            "Missing authored Center panel, text or Continue control in " + source.gameObject.scene.name);
        var sourcePanel = sourcePanelObject.transform as RectTransform;
        Require(sourcePanel != null && sourceText.transform.IsChildOf(sourcePanel), "Center text must remain inside its authored card.");
        Vector2 authoredPosition = sourcePanel.anchoredPosition, authoredSize = sourcePanel.rect.size;
        Vector2 lowOffset = serialized.FindProperty("lowCenterOffset").vector2Value;
        var sequences = source.gameObject.scene.GetRootGameObjects().SelectMany(root =>
            root.GetComponentsInChildren<TutorialSequence>(true)).ToArray();
        var cases = new List<MessageCase>();
        foreach (TutorialSequence sequence in sequences)
        {
            if (sequence.tutorialSteps == null) continue;
            for (int i = 0; i < sequence.tutorialSteps.Length; i++)
            {
                TutorialStep step = sequence.tutorialSteps[i];
                if (step == null || (step.screenPosition != TutorialPosition.Center && step.screenPosition != TutorialPosition.LowCenter)) continue;
                cases.Add(new MessageCase { label = sequence.lessonName + " step " + i, message = step.message ?? string.Empty,
                    offset = step.screenPosition == TutorialPosition.LowCenter ? lowOffset : Vector2.zero });
            }
        }
        Require(cases.Count > 0, "No authored centered tutorial messages in " + source.gameObject.scene.name);
        var sourceObjects = sourcePanelObject.GetComponentsInChildren<Component>(true).Cast<Object>()
            .Concat(next.GetComponentsInChildren<Component>(true)).Concat(sequences).Append(source).Distinct()
            .ToDictionary(item => item, EditorJsonUtility.ToJson);
        bool dirty = source.gameObject.scene.isDirty;
        string textPath = AnimationUtility.CalculateTransformPath(sourceText.transform, sourcePanel);
        CanvasScaler scaler = sourcePanel.GetComponentInParent<CanvasScaler>(true);
        Vector2 reference = scaler != null ? scaler.referenceResolution : new Vector2(1920f, 1080f);
        float match = scaler != null ? scaler.matchWidthOrHeight : .5f;
        Scene preview = EditorSceneManager.NewPreviewScene();
        RenderTexture previousTarget = RenderTexture.active;
        try
        {
            var root = new GameObject("Tutorial Center Layout Preview", typeof(RectTransform), typeof(Canvas));
            SceneManager.MoveGameObjectToScene(root, preview);
            RectTransform canvasRect = root.GetComponent<RectTransform>();
            canvasRect.sizeDelta = reference;
            canvasRect.localScale = Vector3.one * .01f;
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var card = Object.Instantiate(sourcePanelObject, root.transform, false).GetComponent<RectTransform>();
            card.gameObject.SetActive(true);
            card.localScale = Vector3.one;
            foreach (CanvasGroup group in card.GetComponentsInChildren<CanvasGroup>(true)) group.alpha = 1f;
            TextMeshProUGUI text = card.Find(textPath).GetComponent<TextMeshProUGUI>();
            TMP_FontAsset font = text.font;
            bool richText = text.richText;
            var cameraObject = new GameObject("Tutorial Center Layout Camera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, preview);
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.scene = preview; canvas.worldCamera = camera;
            camera.orthographic = true; camera.nearClipPlane = .1f; camera.farClipPlane = 100f;
            camera.transform.position = new Vector3(0f, 0f, -20f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(37, 79, 216, 255);
            camera.useOcclusionCulling = false;
            RectTransform budget = MakeBudget(root.transform, font);
            Vector2[] screens = { new Vector2(1920, 1080), new Vector2(1600, 720), new Vector2(2048, 1280), new Vector2(1126, 337) };
            Vector2 initialSize = Vector2.zero, initialPosition = Vector2.zero;
            foreach (Vector2 screen in screens)
            {
                ConfigureCanvas(canvasRect, camera, screen, reference, match);
                foreach (MessageCase item in cases)
                {
                    ValidateMessage(card, text, authoredPosition, authoredSize, item.offset, canvasRect.rect, item.message,
                        source.gameObject.scene.name + "/" + screen + "/" + item.label, font, richText);
                    Require(!LocalBounds(card, canvasRect).Overlaps(LocalBounds(budget, canvasRect)), "Tutorial card overlaps the synthetic budget.");
                }
                string prefix = "Temp/TutorialCenter_" + source.gameObject.scene.name + "_" + (int)screen.x + "x" + (int)screen.y;
                ValidateMessage(card, text, authoredPosition, authoredSize, Vector2.zero, canvasRect.rect,
                    TrussTutorialBuildAuthoring.FirstSectionFeedback, prefix + " first truss section", font, richText);
                if (screen == screens[0]) { initialSize = card.rect.size; initialPosition = card.anchoredPosition; }
                Render(camera, screen, prefix + ".png");
                ValidateMessage(card, text, authoredPosition, authoredSize, Vector2.zero, canvasRect.rect,
                    TrussTutorialBuildAuthoring.RoadFeedback, prefix + " road feedback", font, richText);
                Render(camera, screen, prefix + "_Road.png");
                ValidateMessage(card, text, authoredPosition, authoredSize, Vector2.zero, canvasRect.rect,
                    TrussTutorialBuildAuthoring.CompletionFeedback, prefix + " completed structure", font, richText);
                Render(camera, screen, prefix + "_Completion.png");
                ValidateMessage(card, text, authoredPosition, authoredSize, lowOffset, canvasRect.rect,
                    TrussTutorialBuildAuthoring.CompletionFeedback, prefix + " LowCenter", font, richText);
                Rect insetSafe = Rect.MinMaxRect(canvasRect.rect.xMin + 54f, canvasRect.rect.yMin + 18f,
                    canvasRect.rect.xMax - 20f, canvasRect.rect.yMax - 36f);
                ValidateMessage(card, text, authoredPosition, authoredSize, Vector2.zero, insetSafe,
                    TrussTutorialBuildAuthoring.FirstSectionFeedback, prefix + " safe-area inset", font, richText);
                report.AppendLine("PASS: " + source.gameObject.scene.name + " " + (int)screen.x + "x" + (int)screen.y +
                    "; " + cases.Count + " authored Center/LowCenter messages, full glyph containment, repeats, three Truss feedbacks and safe-area inset. " + prefix + ".png");
            }
            ConfigureCanvas(canvasRect, camera, screens[0], reference, match);
            ValidateMessage(card, text, authoredPosition, authoredSize, Vector2.zero, canvasRect.rect,
                TrussTutorialBuildAuthoring.FirstSectionFeedback, "resize round trip", font, richText);
            Require(Vector2.Distance(initialSize, card.rect.size) < .01f && Vector2.Distance(initialPosition, card.anchoredPosition) < .01f,
                "Returning to the original screen size accumulated card offsets.");
            text.text = string.Join(" ", Enumerable.Repeat("A deliberately oversized future tutorial message.", 500));
            Require(!TutorialCenterLayout.ApplyWithin(card, text, authoredPosition, authoredSize, Vector2.zero, canvasRect.rect),
                "Oversized hypothetical content should report that it cannot fully fit.");
            Require(text.fontSize >= 26f && text.overflowMode == TextOverflowModes.Truncate &&
                Contains(canvasRect.rect, LocalBounds(card, canvasRect)), "Oversized content escaped the safe card or used unreadably small text.");
        }
        finally
        {
            RenderTexture.active = previousTarget;
            EditorSceneManager.ClosePreviewScene(preview);
            Require(source.gameObject.scene.isDirty == dirty && sourceObjects.All(pair => pair.Key != null &&
                EditorJsonUtility.ToJson(pair.Key) == pair.Value), "Source card, Continue control, sequence callbacks or manager changed during preview.");
        }
    }

    private static void ValidateMessage(RectTransform card, TextMeshProUGUI text, Vector2 position, Vector2 size,
        Vector2 offset, Rect safe, string message, string label, TMP_FontAsset font, bool richText)
    {
        text.text = message;
        Require(TutorialCenterLayout.ApplyWithin(card, text, position, size, offset, safe), "Complete message did not fit: " + label);
        Canvas.ForceUpdateCanvases();
        text.ForceMeshUpdate(true, true);
        Require(text.font == font && text.richText == richText && text.text == message && text.fontSize >= 26f &&
            !text.isTextTruncated && !text.isTextOverflowing, "Message/font changed, became too small or was truncated: " + label);
        Require(Contains(safe, LocalBounds(card, card.parent as RectTransform)), "Card extends outside the safe area: " + label);
        Rect cardInterior = card.rect;
        cardInterior.xMin += 6f; cardInterior.xMax -= 6f; cardInterior.yMin += 6f; cardInterior.yMax -= 6f;
        for (int i = 0; i < text.textInfo.characterCount; i++)
        {
            TMP_CharacterInfo character = text.textInfo.characterInfo[i];
            if (!character.isVisible) continue;
            Vector3 min = card.InverseTransformPoint(text.transform.TransformPoint(character.bottomLeft));
            Vector3 max = card.InverseTransformPoint(text.transform.TransformPoint(character.topRight));
            Require(min.x >= cardInterior.xMin - .5f && min.y >= cardInterior.yMin - .5f &&
                max.x <= cardInterior.xMax + .5f && max.y <= cardInterior.yMax + .5f,
                "Rendered glyph extends outside the cream card: " + label + " / character " + i);
        }
        Vector2 beforeSize = card.rect.size, beforePosition = card.anchoredPosition;
        float beforeFont = text.fontSize;
        Require(TutorialCenterLayout.ApplyWithin(card, text, position, size, offset, safe) &&
            Vector2.Distance(beforeSize, card.rect.size) < .01f && Vector2.Distance(beforePosition, card.anchoredPosition) < .01f &&
            Mathf.Abs(beforeFont - text.fontSize) < .01f, "Repeated layout accumulated changes: " + label);
    }

    private static void ConfigureCanvas(RectTransform canvas, Camera camera, Vector2 screen, Vector2 reference, float match)
    {
        float scale = Mathf.Pow(screen.x / reference.x, 1f - match) * Mathf.Pow(screen.y / reference.y, match);
        canvas.sizeDelta = screen / scale;
        camera.orthographicSize = canvas.rect.height * .005f;
        camera.aspect = screen.x / screen.y;
        Canvas.ForceUpdateCanvases();
    }

    private static RectTransform MakeBudget(Transform parent, TMP_FontAsset font)
    {
        var background = new GameObject("Synthetic budget context", typeof(RectTransform), typeof(Image));
        background.transform.SetParent(parent, false);
        RectTransform rect = background.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -56f); rect.sizeDelta = new Vector2(360f, 96f);
        background.GetComponent<Image>().color = new Color32(77, 67, 90, 255);
        var value = new GameObject("Budget context label", typeof(RectTransform), typeof(TextMeshProUGUI));
        value.transform.SetParent(background.transform, false);
        var valueRect = value.GetComponent<RectTransform>();
        valueRect.anchorMin = Vector2.zero; valueRect.anchorMax = Vector2.one;
        valueRect.offsetMin = new Vector2(8f, 8f); valueRect.offsetMax = new Vector2(-8f, -8f);
        var label = value.GetComponent<TextMeshProUGUI>();
        label.font = font; label.fontSize = 30f; label.text = "Budget\n75,000";
        label.alignment = TextAlignmentOptions.Center; label.color = Color.white; label.raycastTarget = false;
        return rect;
    }

    private static Rect LocalBounds(RectTransform item, RectTransform relativeTo)
    {
        var corners = new Vector3[4]; item.GetWorldCorners(corners);
        Vector3 min = relativeTo.InverseTransformPoint(corners[0]), max = min;
        for (int i = 1; i < corners.Length; i++)
        { Vector3 point = relativeTo.InverseTransformPoint(corners[i]); min = Vector3.Min(min, point); max = Vector3.Max(max, point); }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private static bool Contains(Rect outer, Rect inner) => inner.xMin >= outer.xMin - .5f && inner.yMin >= outer.yMin - .5f &&
        inner.xMax <= outer.xMax + .5f && inner.yMax <= outer.yMax + .5f;

    private static void Render(Camera camera, Vector2 screen, string path)
    {
        int width = Mathf.RoundToInt(screen.x), height = Mathf.RoundToInt(screen.y);
        var target = new RenderTexture(width, height, 24); target.Create();
        RenderTexture previous = RenderTexture.active;
        try
        {
            camera.targetTexture = target;
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null) camera.Render();
            else UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,
                new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture.active = target;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try { texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply(); File.WriteAllBytes(path, texture.EncodeToPNG()); }
            finally { Object.DestroyImmediate(texture); }
        }
        finally { camera.targetTexture = null; RenderTexture.active = previous; target.Release(); Object.DestroyImmediate(target); }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Write(string report) { Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, report); }
}
#endif
