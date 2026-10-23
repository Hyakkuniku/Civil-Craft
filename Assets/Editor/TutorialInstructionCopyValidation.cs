#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Read-only final-state text previews. Never creates a live tutorial owner.</summary>
[InitializeOnLoad]
public static class TutorialInstructionCopyValidation
{
    public const string RequestPath = "Temp/tutorial-instruction-copy.request";
    public const string ReportPath = "Logs/tutorial-instruction-copy.txt";
    private static bool running;
    private static double nextPoll;

    private sealed class MessageCase
    {
        public string label;
        public string message;
        public TutorialPosition position;
    }

    static TutorialInstructionCopyValidation() { EditorApplication.update += Poll; }
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

    [MenuItem("Tools/Civil Craft/Tutorials/Validate Concise Instruction Copy (Read Only)")]
    public static void Run()
    {
        if (Busy) return;
        running = true;
        var report = new StringBuilder("Detached concise tutorial instruction checks\n");
        var failures = new List<string>();
        Scene[] scenes = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).ToArray();
        bool[] dirty = scenes.Select(scene => scene.isDirty).ToArray();
        GameObject[][] roots = scenes.Select(scene => scene.GetRootGameObjects()).ToArray();
        var hashes = scenes.Where(scene => File.Exists(scene.path)).ToDictionary(scene => scene.path, scene => Hash(scene.path));
        Scene active = SceneManager.GetActiveScene();
        Object[] selection = Selection.objects; Object selected = Selection.activeObject;
        float timeScale = Time.timeScale;
        PlayerDataManager save = PlayerDataManager.Instance;
        PlayerData player = save != null ? save.CurrentData : null;
        string playerJson = player != null ? JsonUtility.ToJson(player) : null;
        try
        {
            VerifyMirroredLeftStyle();
            TutorialManager[] managers = Resources.FindObjectsOfTypeAll<TutorialManager>().Where(manager => manager != null &&
                manager.gameObject.scene.IsValid() && manager.gameObject.scene.isLoaded &&
                !EditorSceneManager.IsPreviewScene(manager.gameObject.scene)).ToArray();
            Require(managers.Length > 0, "Open a saved gameplay scene with its authored TutorialManager first.");
            foreach (TutorialManager manager in managers) Capture(manager, report, failures);
            Require(SceneManager.sceneCount == scenes.Length && SceneManager.GetActiveScene() == active, "Loaded scene setup changed.");
            for (int index = 0; index < scenes.Length; index++)
                Require(SceneManager.GetSceneAt(index) == scenes[index] && scenes[index].isDirty == dirty[index] &&
                    scenes[index].GetRootGameObjects().SequenceEqual(roots[index]), "Loaded roots/order/dirty state changed.");
            Require(hashes.All(pair => Hash(pair.Key) == pair.Value), "Saved scene bytes changed.");
            Require(Selection.activeObject == selected && Selection.objects.SequenceEqual(selection) && Time.timeScale == timeScale,
                "Editor selection or time scale changed.");
            Require(PlayerDataManager.Instance == save && (player == null ||
                (ReferenceEquals(player, save.CurrentData) && JsonUtility.ToJson(player) == playerJson)), "Player data changed.");
            report.AppendLine("PASS: all source UI/fonts, instruction fields, action gates, callbacks, scene files/dirty flags, selection, time scale and player data preserved. No live manager, tutorial events, UI clicks or saves invoked.");
            if (failures.Count > 0) report.Insert(0, "FAILED: " + failures.Count + " residual text/layout issues; no layout changes made.\n");
            else report.Insert(0, "PASS\n");
        }
        catch (Exception error) { report.Insert(0, "FAILED\n"); report.AppendLine(error.ToString()); }
        finally { Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, report.ToString()); running = false; }
    }

    private static void Capture(TutorialManager source, StringBuilder report, List<string> failures)
    {
        var properties = new SerializedObject(source);
        var sourceCenter = ((GameObject)properties.FindProperty("centerPanel").objectReferenceValue)?.transform as RectTransform;
        var sourceLeft = ((GameObject)properties.FindProperty("leftPanel").objectReferenceValue)?.transform as RectTransform;
        var sourceCenterText = properties.FindProperty("centerText").objectReferenceValue as TextMeshProUGUI;
        var sourceLeftText = properties.FindProperty("leftText").objectReferenceValue as TextMeshProUGUI;
        Require(sourceCenter != null && sourceLeft != null && sourceCenterText != null && sourceLeftText != null,
            "Authored center/left UI is missing in " + source.gameObject.scene.name);
        TutorialSequence[] sequences = source.gameObject.scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<TutorialSequence>(true)).ToArray();
        var cases = new List<MessageCase>();
        foreach (TutorialSequence sequence in sequences)
            for (int index = 0; index < (sequence.tutorialSteps?.Length ?? 0); index++)
            {
                TutorialStep step = sequence.tutorialSteps[index];
                if (step == null) continue;
                cases.Add(new MessageCase { label = sequence.lessonName + "[" + index + "]", message = step.message ?? string.Empty,
                    position = step.screenPosition });
            }
        Require(cases.Count > 0, "No authored tutorial steps found in " + source.gameObject.scene.name);
        var observed = sourceCenter.GetComponentsInChildren<Component>(true).Cast<Object>()
            .Concat(sourceLeft.GetComponentsInChildren<Component>(true)).Concat(sequences).Append(source).Distinct()
            .ToDictionary(item => item, EditorJsonUtility.ToJson);
        var fontHashes = new[] { sourceCenterText.font, sourceLeftText.font }.Where(font => font != null)
            .Select(AssetDatabase.GetAssetPath).Where(path => !string.IsNullOrEmpty(path)).Distinct()
            .ToDictionary(path => path, AssetDatabase.GetAssetDependencyHash);
        Vector2 centerPosition = sourceCenter.anchoredPosition, centerSize = sourceCenter.rect.size;
        Vector2 leftPosition = sourceLeft.anchoredPosition, leftSize = sourceLeft.rect.size;
        Vector2 lowOffset = properties.FindProperty("lowCenterOffset").vector2Value;
        float pulse = Mathf.Abs(properties.FindProperty("pulseAmount").floatValue);
        // TMP glyph quads include font/SDF overhang, not just layout advances.
        // A text RectTransform is not a clip boundary without an actual mask.
        // Inspect the authored hierarchy (including inactive ancestors), not the
        // graphic-only clone, before using the existing Center card invariant.
        bool unmaskedCenter = sourceCenterText.GetComponentsInParent<RectMask2D>(true).Length == 0 &&
            sourceCenterText.GetComponentsInParent<Mask>(true).Length == 0;
        report.AppendLine("SOURCE: " + source.gameObject.scene.name + " Center ancestor mask proof=" +
            (unmaskedCenter ? "no RectMask2D or Mask, including inactive ancestors; enforce 6-unit cream-card interior" :
                "mask present; retain strict TMP text-rectangle containment"));
        CanvasScaler scaler = sourceCenter.GetComponentInParent<CanvasScaler>(true);
        Vector2 reference = scaler != null ? scaler.referenceResolution : new Vector2(1920, 1080);
        float match = scaler != null ? scaler.matchWidthOrHeight : .5f;
        Scene preview = EditorSceneManager.NewPreviewScene();
        RenderTexture oldTarget = RenderTexture.active;
        try
        {
            var root = new GameObject("Detached concise instruction canvas", typeof(RectTransform), typeof(Canvas));
            SceneManager.MoveGameObjectToScene(root, preview);
            var parent = (RectTransform)root.transform; parent.localScale = Vector3.one * .01f;
            Canvas canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            RectTransform center = CloneArt(sourceCenter, parent), left = CloneArt(sourceLeft, parent);
            TextMeshProUGUI centerText = center.Find(AnimationUtility.CalculateTransformPath(sourceCenterText.transform, sourceCenter)).GetComponent<TextMeshProUGUI>();
            TextMeshProUGUI leftText = left.Find(AnimationUtility.CalculateTransformPath(sourceLeftText.transform, sourceLeft)).GetComponent<TextMeshProUGUI>();
            MirrorLeftStyle(leftText);
            var cameraObject = new GameObject("Detached concise instruction camera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, preview);
            Camera camera = cameraObject.GetComponent<Camera>(); camera.scene = preview; camera.useOcclusionCulling = false;
            camera.orthographic = true; camera.nearClipPlane = .1f; camera.farClipPlane = 100;
            camera.transform.position = new Vector3(0, 0, -20);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(37, 79, 216, 255);
            canvas.worldCamera = camera;
            Vector2Int[] screens = { new Vector2Int(1600, 720), new Vector2Int(1920, 1080), new Vector2Int(2048, 1280), new Vector2Int(1126, 337) };
            int compacted = cases.Count(item => TutorialInstructionText.Compact(item.message) != item.message);
            foreach (Vector2Int screen in screens)
            {
                float scale = Mathf.Pow(screen.x / reference.x, 1f - match) * Mathf.Pow(screen.y / reference.y, match);
                parent.sizeDelta = new Vector2(screen.x, screen.y) / scale;
                camera.aspect = (float)screen.x / screen.y; camera.orthographicSize = parent.rect.height * .005f;
                Canvas.ForceUpdateCanvases();
                float minimumLeftFont = 30;
                int screenFailures = 0;
                foreach (MessageCase item in cases)
                {
                    try
                    {
                        Show(item, center, centerText, left, leftText, centerPosition, centerSize, leftPosition, leftSize, lowOffset, parent.rect);
                        bool useLeft = item.position != TutorialPosition.Center && item.position != TutorialPosition.LowCenter;
                        TextMeshProUGUI text = useLeft ? leftText : centerText;
                        ValidateText(useLeft ? left : center, text, parent.rect, useLeft ? 22f : 26f, item.label,
                            !useLeft && unmaskedCenter);
                        if (useLeft)
                        {
                            minimumLeftFont = Mathf.Min(minimumLeftFont, text.fontSize);
                            text.transform.localScale = Vector3.one * (1f + pulse);
                            ValidateText(left, text, parent.rect, 22f, item.label + " / maximum existing idle pulse");
                            text.transform.localScale = Vector3.one;
                        }
                    }
                    catch (Exception error)
                    {
                        string problem = source.gameObject.scene.name + "/" + screen.x + "x" + screen.y + "/" + item.position + "/" + item.label + ": " + error.Message;
                        failures.Add(problem); report.AppendLine("RESIDUAL: " + problem); screenFailures++;
                    }
                }
                var shots = new Dictionary<string, MessageCase>();
                MessageCase selection = cases.FirstOrDefault(item => item.message.Contains("When a structure contains several members")) ??
                    cases.FirstOrDefault(item => item.message.Contains("Selection Tool"));
                if (selection != null) shots.Add("Selection", selection);
                MessageCase longestLeft = cases.Where(item => item.position != TutorialPosition.Center && item.position != TutorialPosition.LowCenter)
                    .OrderByDescending(item => Plain(TutorialInstructionText.Compact(item.message)).Length).FirstOrDefault();
                MessageCase longestCenter = cases.Where(item => item.position == TutorialPosition.Center || item.position == TutorialPosition.LowCenter)
                    .OrderByDescending(item => Plain(TutorialInstructionText.Compact(item.message)).Length).FirstOrDefault();
                if (longestLeft != null) shots.Add("LongestLeft", longestLeft);
                if (longestCenter != null) shots.Add("LongestCenter", longestCenter);
                foreach (var shot in shots)
                {
                    try { Show(shot.Value, center, centerText, left, leftText, centerPosition, centerSize, leftPosition, leftSize, lowOffset, parent.rect); }
                    catch (Exception error) { report.AppendLine("PREVIEW WARNING: " + shot.Key + " / " + error.Message); }
                    string path = "Temp/TutorialInstructionCopy/" + source.gameObject.scene.name + "-" + shot.Key + "-" + screen.x + "x" + screen.y + ".png";
                    Render(camera, screen, path);
                }
                report.AppendLine((screenFailures == 0 ? "PASS: " : "CHECK: ") + source.gameObject.scene.name + " " + screen.x + "x" + screen.y +
                    "; " + cases.Count + " actual Center/LowCenter/Left steps, " + compacted + " exact stock copy replacements; minimum Left font=" + minimumLeftFont.ToString("F2") +
                    " (" + (minimumLeftFont * scale).ToString("F2") + " physical pixels); " + screenFailures + " residual issues. Preview folder Temp/TutorialInstructionCopy.");
            }
        }
        finally
        {
            RenderTexture.active = oldTarget;
            EditorSceneManager.ClosePreviewScene(preview);
            Require(observed.All(pair => pair.Key != null && EditorJsonUtility.ToJson(pair.Key) == pair.Value) &&
                fontHashes.All(pair => AssetDatabase.GetAssetDependencyHash(pair.Key) == pair.Value),
                "Source UI, fonts, instruction gates/callbacks or manager changed during detached previews.");
        }
    }

    private static void Show(MessageCase item, RectTransform center, TextMeshProUGUI centerText, RectTransform left, TextMeshProUGUI leftText,
        Vector2 centerPosition, Vector2 centerSize, Vector2 leftPosition, Vector2 leftSize, Vector2 lowOffset, Rect safe)
    {
        bool useLeft = item.position != TutorialPosition.Center && item.position != TutorialPosition.LowCenter;
        center.gameObject.SetActive(!useLeft); left.gameObject.SetActive(useLeft);
        TextMeshProUGUI text = useLeft ? leftText : centerText;
        text.transform.localScale = Vector3.one;
        string compact = TutorialInstructionText.Compact(item.message);
        Require(TutorialInstructionText.Compact(compact) == compact, "Compaction is not idempotent.");
        text.text = compact;
        if (useLeft)
        {
            left.anchoredPosition = leftPosition;
            left.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, leftSize.x);
            left.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, leftSize.y);
            MirrorLeftStyle(leftText);
        }
        else Require(TutorialCenterLayout.ApplyWithin(center, centerText, centerPosition, centerSize,
            item.position == TutorialPosition.LowCenter ? lowOffset : Vector2.zero, safe), "Compacted center message does not fit.");
        Canvas.ForceUpdateCanvases(); text.ForceMeshUpdate(true, true);
    }

    private static void ValidateText(RectTransform panel, TextMeshProUGUI text, Rect safe, float minimumFont, string label,
        bool unmaskedCenter = false)
    {
        text.ForceMeshUpdate(true, true);
        Require(text.font != null && text.fontSize >= minimumFont - .05f && !text.isTextTruncated && !text.isTextOverflowing,
            "Font too small, ellipsis or truncation: " + label + "; size=" + text.fontSize + "; rect=" + text.rectTransform.rect + "; text=" + text.text);
        Require(Contains(safe, Bounds(panel, panel.parent as RectTransform)), "Panel leaves canvas bounds: " + label);
        string expected = Plain(text.text).TrimEnd();
        Require(text.GetParsedText().TrimEnd() == expected, "Rendered character stream lost the end of the instruction (trailing newlines are allowed): " + label);
        Rect textRect = text.rectTransform.rect;
        Rect panelInterior = panel.rect;
        if (unmaskedCenter)
        { panelInterior.xMin += 6f; panelInterior.xMax -= 6f; panelInterior.yMin += 6f; panelInterior.yMax -= 6f; }
        for (int index = 0; index < text.textInfo.characterCount; index++)
        {
            TMP_CharacterInfo character = text.textInfo.characterInfo[index];
            if (!character.isVisible) continue;
            if (!unmaskedCenter)
                Require(character.bottomLeft.x >= textRect.xMin - 1 && character.bottomLeft.y >= textRect.yMin - 1 &&
                    character.topRight.x <= textRect.xMax + 1 && character.topRight.y <= textRect.yMax + 1,
                    "Glyph clips in text rectangle: " + label + "/" + index + "; " + GlyphDiagnostics(text, character, panel));
            Vector3 min = panel.InverseTransformPoint(text.transform.TransformPoint(character.bottomLeft));
            Vector3 max = panel.InverseTransformPoint(text.transform.TransformPoint(character.topRight));
            float tolerance = unmaskedCenter ? .5f : 1f;
            Require(min.x >= panelInterior.xMin - tolerance && min.y >= panelInterior.yMin - tolerance &&
                max.x <= panelInterior.xMax + tolerance && max.y <= panelInterior.yMax + tolerance,
                "Glyph leaves authored panel/interior at existing scale/pulse: " + label + "/" + index + "; " + GlyphDiagnostics(text, character, panel));
        }
    }

    private static string GlyphDiagnostics(TextMeshProUGUI text, TMP_CharacterInfo character, RectTransform panel)
    {
        Material material = text.fontSharedMaterial;
        string clip = material != null && material.HasProperty("_ClipRect") ? material.GetVector("_ClipRect").ToString("F3") : "no _ClipRect property";
        return "character=" + character.character + " (U+" + ((int)character.character).ToString("X4") + ")" +
            "; quad=" + character.bottomLeft.ToString("F3") + ".." + character.topRight.ToString("F3") +
            "; ascender=" + character.ascender + "; descender=" + character.descender +
            "; textRect=" + text.rectTransform.rect + "; panelRect=" + panel.rect + "; margin=" + text.margin +
            "; font=" + (text.font != null ? text.font.name : "null") + "; size=" + text.fontSize +
            "; faceAscent=" + (text.font != null ? text.font.faceInfo.ascentLine.ToString("F3") : "null") +
            "; faceCap=" + (text.font != null ? text.font.faceInfo.capLine.ToString("F3") : "null") +
            "; overflow=" + text.overflowMode + "; maskable=" + text.maskable +
            "; ancestorRectMasks=" + text.GetComponentsInParent<RectMask2D>(true).Length +
            "; ancestorStencilMasks=" + text.GetComponentsInParent<Mask>(true).Length +
            "; material=" + (material != null ? material.name : "null") + "; clip=" + clip;
    }

    private static void MirrorLeftStyle(TextMeshProUGUI text)
    {
        // Mirrors TutorialManager's current fixed presentation policy. This
        // validator reports copy overflow instead of introducing a new layout.
        text.fontSize = 30f; text.enableAutoSizing = true; text.fontSizeMin = 22f; text.fontSizeMax = 30f;
        text.fontStyle = FontStyles.Bold; text.alignment = TextAlignmentOptions.MidlineLeft;
        text.enableWordWrapping = true; text.overflowMode = TextOverflowModes.Ellipsis;
        text.characterSpacing = .5f; text.lineSpacing = 3f; text.margin = new Vector4(8, 4, 8, 4);
    }

    private static void VerifyMirroredLeftStyle()
    {
        string source = File.ReadAllText("Assets/Script/Level/Tutorial/TutorialManager.cs");
        foreach (string expected in new[] { "leftText.fontSize = 30f", "leftText.enableAutoSizing = true", "leftText.fontSizeMin = 22f",
            "leftText.fontSizeMax = 30f", "leftText.fontStyle = FontStyles.Bold", "leftText.alignment = TextAlignmentOptions.MidlineLeft",
            "leftText.enableWordWrapping = true", "leftText.overflowMode = TextOverflowModes.Ellipsis", "leftText.characterSpacing = 0.5f",
            "leftText.lineSpacing = 3f", "leftText.margin = new Vector4(8f, 4f, 8f, 4f)" })
            Require(source.Contains(expected), "Update the native style fixture after the real Left style changed: " + expected);
    }

    private static string Plain(string value) => Regex.Replace(Regex.Replace(value ?? string.Empty, "<br\\s*/?>", "\n", RegexOptions.IgnoreCase), "<[^>]*>", string.Empty);
    private static RectTransform CloneArt(RectTransform original, RectTransform parent)
    {
        var owner = new GameObject(original.name, typeof(RectTransform));
        RectTransform copy = (RectTransform)owner.transform; copy.SetParent(parent, false);
        copy.anchorMin = original.anchorMin; copy.anchorMax = original.anchorMax; copy.pivot = original.pivot;
        copy.sizeDelta = original.sizeDelta; copy.anchoredPosition = original.anchoredPosition;
        copy.localScale = original.localScale; copy.localRotation = original.localRotation;
        Graphic originalGraphic = original.GetComponent<Graphic>();
        Require(originalGraphic == null || originalGraphic is Image || originalGraphic is TextMeshProUGUI,
            "The detached graphic-only copier must explicitly support this graphic type: " + original.name);
        Image sourceImage = original.GetComponent<Image>();
        if (sourceImage != null)
        {
            Image image = owner.AddComponent<Image>(); image.sprite = sourceImage.sprite; image.color = sourceImage.color;
            image.type = sourceImage.type; image.preserveAspect = sourceImage.preserveAspect; image.fillCenter = sourceImage.fillCenter;
            image.pixelsPerUnitMultiplier = sourceImage.pixelsPerUnitMultiplier; image.raycastTarget = false;
        }
        TextMeshProUGUI sourceText = original.GetComponent<TextMeshProUGUI>();
        if (sourceText != null)
        {
            var text = owner.AddComponent<TextMeshProUGUI>(); text.font = sourceText.font; text.fontSharedMaterial = sourceText.fontSharedMaterial;
            text.text = sourceText.text; text.fontSize = sourceText.fontSize; text.fontStyle = sourceText.fontStyle; text.color = sourceText.color;
            text.alignment = sourceText.alignment; text.richText = sourceText.richText; text.enableWordWrapping = sourceText.enableWordWrapping;
            text.characterSpacing = sourceText.characterSpacing; text.wordSpacing = sourceText.wordSpacing; text.lineSpacing = sourceText.lineSpacing;
            text.paragraphSpacing = sourceText.paragraphSpacing; text.margin = sourceText.margin; text.raycastTarget = false;
        }
        foreach (Transform child in original)
            if (child is RectTransform rect) CloneArt(rect, copy).gameObject.SetActive(child.gameObject.activeSelf);
        owner.SetActive(true); return copy;
    }

    private static Rect Bounds(RectTransform item, RectTransform relative)
    {
        var corners = new Vector3[4]; item.GetWorldCorners(corners);
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
        RenderTexture old = RenderTexture.active;
        try
        {
            camera.targetTexture = target; Canvas.ForceUpdateCanvases();
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null) camera.Render();
            else UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,
                new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture.active = target;
            var pixels = new Texture2D(screen.x, screen.y, TextureFormat.RGBA32, false);
            try { pixels.ReadPixels(new Rect(0, 0, screen.x, screen.y), 0, 0); pixels.Apply(); Directory.CreateDirectory("Temp/TutorialInstructionCopy"); File.WriteAllBytes(path, pixels.EncodeToPNG()); }
            finally { Object.DestroyImmediate(pixels); }
        }
        finally { camera.targetTexture = null; RenderTexture.active = old; target.Release(); Object.DestroyImmediate(target); }
    }
    private static string Hash(string path) { using (var algorithm = SHA256.Create()) return BitConverter.ToString(algorithm.ComputeHash(File.ReadAllBytes(path))); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
#endif
