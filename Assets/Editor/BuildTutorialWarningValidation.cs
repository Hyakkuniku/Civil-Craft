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

/// <summary>Opt-in graphic-only previews. No tutorial callbacks or scene authoring.</summary>
[InitializeOnLoad]
public static class BuildTutorialWarningValidation
{
    public const string RequestPath = "Temp/build-tutorial-warning.request";
    public const string ReportPath = "Logs/build-tutorial-warning.txt";
    private static bool running;
    private static double nextPoll;

    static BuildTutorialWarningValidation() { EditorApplication.update += Poll; }

    private static bool Busy => running || EditorApplication.isCompiling || EditorApplication.isUpdating ||
        EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer ||
        PrefabStageUtility.GetCurrentPrefabStage() != null;

    private static void Poll()
    {
        if (Busy || EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1;
        if (!File.Exists(RequestPath)) return;
        File.Delete(RequestPath);
        Run();
    }

    [MenuItem("Tools/Civil Craft/Tutorials/Validate Undo Warning Layout (Detached)")]
    public static void Run()
    {
        if (Busy) return;
        running = true;
        var report = new StringBuilder();
        Scene[] scenes = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).ToArray();
        bool[] dirty = scenes.Select(scene => scene.isDirty).ToArray();
        GameObject[][] roots = scenes.Select(scene => scene.GetRootGameObjects()).ToArray();
        var saved = scenes.Where(scene => File.Exists(scene.path)).ToDictionary(scene => scene.path, scene => Hash(scene.path));
        Scene active = SceneManager.GetActiveScene();
        Object[] selection = Selection.objects;
        Object selected = Selection.activeObject;
        float timeScale = Time.timeScale;
        PlayerData data = PlayerDataManager.Instance != null ? PlayerDataManager.Instance.CurrentData : null;
        string dataBefore = data != null ? JsonUtility.ToJson(data) : null;
        Scene preview = default;
        RenderTexture oldTarget = RenderTexture.active;
        try
        {
            BuildTutorialDirector director = Resources.FindObjectsOfTypeAll<BuildTutorialDirector>().Single(item =>
                item != null && item.gameObject.scene.IsValid() && item.gameObject.scene.isLoaded &&
                item.gameObject.scene.name == "CanyonCrossing" && !EditorSceneManager.IsPreviewScene(item.gameObject.scene));
            TutorialManager manager = director.gameObject.scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<TutorialManager>(true)).Single();
            var managerProperties = new SerializedObject(manager);
            var sourceCard = ((GameObject)managerProperties.FindProperty("centerPanel").objectReferenceValue).transform as RectTransform;
            var sourceText = managerProperties.FindProperty("centerText").objectReferenceValue as TextMeshProUGUI;
            var sourceWarning = director.undoWarningPanel != null ? director.undoWarningPanel.transform as RectTransform : null;
            Check(sourceCard != null && sourceText != null && sourceWarning != null && director.undoButtonRect != null,
                "Open CanyonCrossing with its existing centered instruction, Undo warning and Undo button.");
            var sourceWarningText = sourceWarning.GetComponentInChildren<TextMeshProUGUI>(true);
            Check(sourceWarningText != null && sourceText.transform.IsChildOf(sourceCard), "The authored warning/instruction text is missing.");
            TutorialSequence beam = director.gameObject.scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<TutorialSequence>(true)).Single(item => item.lessonName == "Sequence_Build1");
            string pier = beam.tutorialSteps.Single(step => step.message != null &&
                step.message.Contains("Trace the remaining guided") && step.message.Contains("Wood Pier")).message;
            Check(sourceWarningText.text.Contains("Blueprint Mismatch!"), "The warning no longer matches the reported overlap.");
            string[] messages = { pier, TrussTutorialBuildAuthoring.FirstSectionFeedback };
            var observed = sourceCard.GetComponentsInChildren<Component>(true).Cast<Object>()
                .Concat(sourceWarning.GetComponentsInChildren<Component>(true))
                .Concat(director.undoButtonRect.GetComponentsInChildren<Component>(true))
                .Append(manager).Append(director).Append(beam).Distinct().ToDictionary(item => item, EditorJsonUtility.ToJson);
            CanvasScaler scaler = sourceCard.GetComponentInParent<CanvasScaler>(true);
            Vector2 reference = scaler != null ? scaler.referenceResolution : new Vector2(1920, 1080);
            float match = scaler != null ? scaler.matchWidthOrHeight : .5f;
            Vector2 cardPosition = sourceCard.anchoredPosition, cardSize = sourceCard.rect.size;
            Vector2 warningPosition = sourceWarning.anchoredPosition, warningSize = sourceWarning.rect.size;
            preview = EditorSceneManager.NewPreviewScene();
            var rootObject = new GameObject("Detached warning canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            SceneManager.MoveGameObjectToScene(rootObject, preview);
            var parent = (RectTransform)rootObject.transform;
            parent.localScale = Vector3.one * .01f;
            Canvas canvas = rootObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            RectTransform card = CloneArt(sourceCard, parent);
            RectTransform warning = CloneArt(sourceWarning, parent);
            CanvasGroup instructionGroup = card.gameObject.AddComponent<CanvasGroup>();
            TextMeshProUGUI text = card.Find(AnimationUtility.CalculateTransformPath(sourceText.transform, sourceCard)).GetComponent<TextMeshProUGUI>();
            TextMeshProUGUI warningText = warning.Find(AnimationUtility.CalculateTransformPath(sourceWarningText.transform, sourceWarning)).GetComponent<TextMeshProUGUI>();
            RectTransform undo = CloneArt(director.undoButtonRect, parent);
            foreach (Graphic graphic in undo.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            Image undoSurface = undo.GetComponent<Image>();
            if (undoSurface == null) undoSurface = undo.gameObject.AddComponent<Image>();
            undoSurface.raycastTarget = true;
            foreach (Graphic graphic in card.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            warning.SetAsLastSibling();
            var cameraObject = new GameObject("Detached warning camera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, preview);
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.scene = preview; camera.useOcclusionCulling = false; camera.orthographic = true;
            camera.nearClipPlane = .1f; camera.farClipPlane = 100f;
            camera.transform.position = new Vector3(0, 0, -20);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(42, 80, 219, 255);
            canvas.worldCamera = camera;
            Vector2Int[] screens = { new Vector2Int(1280, 720), new Vector2Int(1600, 1200), new Vector2Int(2340, 1080), new Vector2Int(1037, 365) };
            Vector2 initialPosition = Vector2.zero, initialSize = Vector2.zero;
            foreach (Vector2Int screen in screens)
            {
                Configure(parent, camera, screen, reference, match);
                for (int index = 0; index < messages.Length; index++)
                {
                    text.text = messages[index];
                    Check(TutorialCenterLayout.ApplyWithin(card, text, cardPosition, cardSize, Vector2.zero, parent.rect), "Instruction did not fit.");
                    Vector2 instructionPosition = card.anchoredPosition, instructionSize = card.rect.size;
                    TMP_FontAsset instructionFont = text.font;
                    // The lifecycle suite exercises the real TutorialManager
                    // owner gate. This graphic-only fixture renders its result
                    // without adding a manager or firing any gameplay callbacks.
                    instructionGroup.alpha = 0f;
                    instructionGroup.interactable = false;
                    instructionGroup.blocksRaycasts = false;
                    warning.gameObject.SetActive(true);
                    Validate(warning, warningText, warningPosition, warningSize, card, parent.rect, screen + "/message " + index);
                    ValidateProjected(warning, warningText, warningPosition, warningSize, card, camera, screen);
                    ValidateGlyphs(card, text, parent.rect, "Instruction");
                    Check(TutorialWarningLayout.ApplyWithin(warning, warningText, warningPosition, warningSize,
                        parent.rect, Bounds(card, parent)), "Warning second pass failed.");
                    if (screen == screens[0] && index == 0) { initialPosition = warning.anchoredPosition; initialSize = warning.rect.size; }
                    string path = "Temp/BuildTutorialWarning/Warning-" + screen.x + "x" + screen.y + (index == 0 ? ".png" : "-Truss.png");
                    Render(camera, screen, path);
                    CheckClickThrough(rootObject.GetComponent<GraphicRaycaster>(), camera, undo, warning);
                    Check(card.gameObject.activeSelf && instructionGroup.alpha == 0f && text.text == messages[index] && text.font == instructionFont &&
                        Vector2.Distance(card.anchoredPosition, instructionPosition) < .01f && Vector2.Distance(card.rect.size, instructionSize) < .01f,
                        "Priority warning changed the paused instruction's object, message, font or rectangle.");
                    warning.gameObject.SetActive(false);
                    instructionGroup.alpha = 1f;
                    instructionGroup.interactable = true;
                    instructionGroup.blocksRaycasts = true;
                    ValidateGlyphs(card, text, parent.rect, "Resumed instruction");
                    Render(camera, screen, path.Replace(".png", "-Resume.png"));
                }
                Rect inset = Rect.MinMaxRect(parent.rect.xMin + 30, parent.rect.yMin + 18, parent.rect.xMax - 20, parent.rect.yMax - 30);
                Validate(warning, warningText, warningPosition, warningSize, card, inset, screen + "/safe area");
                report.AppendLine("PASS: " + screen.x + "x" + screen.y + "; warning-only priority preview and resumed actual Wood Pier/long Truss instruction; full glyphs, >=26pt text, safe area, repeats and Undo click-through. Instruction object/message/font/rectangle unchanged. Temp/BuildTutorialWarning/Warning-" + screen.x + "x" + screen.y + ".png");
            }
            Configure(parent, camera, screens[0], reference, match);
            text.text = messages[0];
            Check(TutorialCenterLayout.ApplyWithin(card, text, cardPosition, cardSize, Vector2.zero, parent.rect), "Resize instruction failed.");
            Validate(warning, warningText, warningPosition, warningSize, card, parent.rect, "Resize round trip");
            Check(Vector2.Distance(initialPosition, warning.anchoredPosition) < .01f && Vector2.Distance(initialSize, warning.rect.size) < .01f,
                "Returning to original resolution accumulated warning offsets.");
            report.AppendLine("PASS: resize round trip is stable; warning is rendered by itself, never stacked over a visible instruction.");
            Check(TutorialWarningLayout.ApplyWithin(warning, warningText, warningPosition, warningSize, parent.rect, default),
                "A missing instruction must still fit the warning by itself.");
            ValidateGlyphs(warning, warningText, parent.rect, "Warning without instruction");
            Vector2 validPosition = warning.anchoredPosition, validSize = warning.rect.size;
            string validMessage = warningText.text;
            Check(!TutorialWarningLayout.Apply(null, warningText, warningPosition, warningSize, card) &&
                !TutorialWarningLayout.ApplyWithin(null, warningText, warningPosition, warningSize, parent.rect, parent.rect) &&
                !TutorialWarningLayout.ApplyWithin(warning, null, warningPosition, warningSize, parent.rect, parent.rect),
                "Missing panel/text must fail safely.");
            Check(Vector2.Distance(validPosition, warning.anchoredPosition) < .01f && Vector2.Distance(validSize, warning.rect.size) < .01f &&
                warningText.text == validMessage, "A null-input rejection mutated the valid warning.");
            report.AppendLine("PASS: missing instruction still fits the standalone warning; null panel/text return false without mutation. Native previews test graphics only; actual pause/resume ownership is covered by the lifecycle suite.");
            Check(observed.All(pair => pair.Key != null && EditorJsonUtility.ToJson(pair.Key) == pair.Value), "Authored UI, controls, callbacks or sequence changed.");
        }
        catch (Exception error) { report.Insert(0, "FAILED\n"); report.AppendLine(error.ToString()); }
        finally
        {
            RenderTexture.active = oldTarget;
            if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
            try
            {
                Check(SceneManager.sceneCount == scenes.Length && SceneManager.GetActiveScene() == active, "Scene setup changed.");
                for (int index = 0; index < scenes.Length; index++)
                    Check(SceneManager.GetSceneAt(index) == scenes[index] && scenes[index].isDirty == dirty[index] &&
                        scenes[index].GetRootGameObjects().SequenceEqual(roots[index]), "Scene order, roots or dirty state changed.");
                Check(saved.All(pair => Hash(pair.Key) == pair.Value), "Saved scene bytes changed.");
                Check(Selection.activeObject == selected && Selection.objects.SequenceEqual(selection) && Time.timeScale == timeScale,
                    "Selection or time scale changed.");
                Check(data == null || (PlayerDataManager.Instance != null && ReferenceEquals(data, PlayerDataManager.Instance.CurrentData) &&
                    JsonUtility.ToJson(data) == dataBefore), "Player data changed.");
                report.AppendLine("PASS: authored scenes, saved bytes, dirty flags, loaded setup, selection, time scale and player data unchanged; no scene saves or tutorial/gameplay callbacks invoked.");
            }
            catch (Exception error) { report.Insert(0, "FAILED SAFETY\n"); report.AppendLine(error.ToString()); }
            Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, report.ToString()); running = false;
        }
    }

    private static void Validate(RectTransform warning, TextMeshProUGUI text, Vector2 position, Vector2 size,
        RectTransform instruction, Rect safe, string label)
    {
        RectTransform parent = warning.parent as RectTransform;
        Rect instructionBounds = Bounds(instruction, parent);
        string content = text.text; TMP_FontAsset font = text.font; bool richText = text.richText;
        Check(TutorialWarningLayout.ApplyWithin(warning, text, position, size, safe, instructionBounds), "Warning did not fit: " + label);
        Check(text.text == content && text.font == font && text.richText == richText, "Warning content/font changed: " + label);
        Canvas.ForceUpdateCanvases();
        ValidateGlyphs(warning, text, safe, "Warning " + label);
        Vector2 beforeSize = warning.rect.size, beforePosition = warning.anchoredPosition;
        float beforeFont = text.fontSize;
        Check(TutorialWarningLayout.ApplyWithin(warning, text, position, size, safe, instructionBounds) &&
            Vector2.Distance(beforeSize, warning.rect.size) < .01f && Vector2.Distance(beforePosition, warning.anchoredPosition) < .01f &&
            Mathf.Abs(beforeFont - text.fontSize) < .01f, "Warning layout is not idempotent: " + label);
    }

    private static void ValidateProjected(RectTransform warning, TextMeshProUGUI text, Vector2 position, Vector2 size,
        RectTransform instruction, Camera camera, Vector2Int screen)
    {
        Vector2 beforePosition = warning.anchoredPosition, beforeSize = warning.rect.size;
        var target = new RenderTexture(screen.x, screen.y, 16);
        try
        {
            camera.targetTexture = target;
            Check(TutorialWarningLayout.Apply(warning, text, position, size, instruction), "Projected native layout failed.");
            Check(Vector2.Distance(beforePosition, warning.anchoredPosition) < .1f && Vector2.Distance(beforeSize, warning.rect.size) < .1f,
                "Screen-projected instruction/safe bounds differ from the explicit parent-local layout.");
        }
        finally { camera.targetTexture = null; target.Release(); Object.DestroyImmediate(target); }
    }

    private static void ValidateGlyphs(RectTransform card, TextMeshProUGUI text, Rect safe, string label)
    {
        text.ForceMeshUpdate(true, true);
        Check(text.font != null && text.fontSize >= 26f && !text.isTextOverflowing && !text.isTextTruncated,
            "Text was too small or clipped: " + label);
        Check(Contains(safe, Bounds(card, card.parent as RectTransform)), "Card left safe bounds: " + label);
        Rect interior = card.rect; interior.xMin += 5; interior.xMax -= 5; interior.yMin += 5; interior.yMax -= 5;
        for (int index = 0; index < text.textInfo.characterCount; index++)
        {
            TMP_CharacterInfo character = text.textInfo.characterInfo[index];
            if (!character.isVisible) continue;
            Vector3 min = card.InverseTransformPoint(text.transform.TransformPoint(character.bottomLeft));
            Vector3 max = card.InverseTransformPoint(text.transform.TransformPoint(character.topRight));
            Check(min.x >= interior.xMin - .5f && min.y >= interior.yMin - .5f && max.x <= interior.xMax + .5f && max.y <= interior.yMax + .5f,
                "Glyph escaped cream card: " + label + "/" + index);
        }
    }

    private static void CheckClickThrough(GraphicRaycaster raycaster, Camera camera, RectTransform undo, RectTransform warning)
    {
        Check(warning.GetComponentsInChildren<Graphic>(true).All(item => !item.raycastTarget), "Warning graphics capture input.");
        var hits = new List<RaycastResult>();
        raycaster.Raycast(new PointerEventData(null) { position = RectTransformUtility.WorldToScreenPoint(camera,
            undo.TransformPoint(undo.rect.center)) }, hits);
        Check(hits.Any(item => item.gameObject.transform == undo || item.gameObject.transform.IsChildOf(undo)) &&
            !hits.Any(item => item.gameObject.transform == warning || item.gameObject.transform.IsChildOf(warning)), "Undo cannot receive a pointer raycast.");
    }

    private static RectTransform CloneArt(RectTransform original, RectTransform parent)
    {
        var owner = new GameObject(original.name + " (detached)", typeof(RectTransform));
        RectTransform copy = (RectTransform)owner.transform; copy.SetParent(parent, false);
        copy.anchorMin = original.anchorMin; copy.anchorMax = original.anchorMax; copy.pivot = original.pivot;
        copy.sizeDelta = original.sizeDelta; copy.anchoredPosition = original.anchoredPosition;
        copy.localScale = original.localScale; copy.localRotation = original.localRotation;
        Image image = original.GetComponent<Image>();
        if (image != null)
        {
            Image art = owner.AddComponent<Image>(); art.sprite = image.sprite; art.color = image.color;
            art.type = image.type; art.preserveAspect = image.preserveAspect; art.fillCenter = image.fillCenter;
            art.pixelsPerUnitMultiplier = image.pixelsPerUnitMultiplier; art.raycastTarget = image.raycastTarget;
        }
        TextMeshProUGUI label = original.GetComponent<TextMeshProUGUI>();
        if (label != null)
        {
            var text = owner.AddComponent<TextMeshProUGUI>(); text.font = label.font; text.fontSharedMaterial = label.fontSharedMaterial;
            text.text = label.text; text.fontSize = label.fontSize; text.fontStyle = label.fontStyle; text.color = label.color;
            text.alignment = label.alignment; text.richText = label.richText; text.enableWordWrapping = label.enableWordWrapping;
            text.characterSpacing = label.characterSpacing; text.wordSpacing = label.wordSpacing;
            text.lineSpacing = label.lineSpacing; text.paragraphSpacing = label.paragraphSpacing;
            text.margin = label.margin; text.raycastTarget = label.raycastTarget;
        }
        foreach (Transform child in original)
            if (child is RectTransform childRect) CloneArt(childRect, copy).gameObject.name = child.name;
        owner.SetActive(true);
        return copy;
    }

    private static void Configure(RectTransform parent, Camera camera, Vector2Int screen, Vector2 reference, float match)
    {
        float scale = Mathf.Pow(screen.x / reference.x, 1f - match) * Mathf.Pow(screen.y / reference.y, match);
        parent.sizeDelta = new Vector2(screen.x, screen.y) / scale;
        camera.aspect = (float)screen.x / screen.y; camera.orthographicSize = parent.rect.height * .005f;
        Canvas.ForceUpdateCanvases();
    }

    private static Rect Bounds(RectTransform rect, RectTransform relative)
    {
        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
        Vector3 min = relative.InverseTransformPoint(corners[0]), max = min;
        foreach (Vector3 corner in corners)
        { Vector3 point = relative.InverseTransformPoint(corner); min = Vector3.Min(min, point); max = Vector3.Max(max, point); }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private static bool Contains(Rect outer, Rect inner) => inner.xMin >= outer.xMin - .5f && inner.yMin >= outer.yMin - .5f &&
        inner.xMax <= outer.xMax + .5f && inner.yMax <= outer.yMax + .5f;

    private static void Render(Camera camera, Vector2Int screen, string path)
    {
        var target = new RenderTexture(screen.x, screen.y, 24); target.Create();
        RenderTexture previous = RenderTexture.active;
        try
        {
            camera.targetTexture = target;
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null) camera.Render();
            else UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,
                new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture.active = target;
            var texture = new Texture2D(screen.x, screen.y, TextureFormat.RGBA32, false);
            try { texture.ReadPixels(new Rect(0, 0, screen.x, screen.y), 0, 0); texture.Apply(); Directory.CreateDirectory("Temp/BuildTutorialWarning"); File.WriteAllBytes(path, texture.EncodeToPNG()); }
            finally { Object.DestroyImmediate(texture); }
        }
        finally { camera.targetTexture = null; RenderTexture.active = previous; target.Release(); Object.DestroyImmediate(target); }
    }

    private static string Hash(string path)
    { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))); }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
#endif
