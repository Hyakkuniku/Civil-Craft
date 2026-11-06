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
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Graphic-only, detached composite previews. Never plays tutorial events or saves scenes.</summary>
[InitializeOnLoad]
public static class BridgeStatisticsGuideLayoutValidation
{
    private const string Request = "Temp/bridge-statistics-layout.request";
    private const string Report = "Logs/bridge-statistics-layout.txt";
    private const string Output = "Temp/BridgeStatisticsGuide";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static bool running;
    private static double next;
    static BridgeStatisticsGuideLayoutValidation() { EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (running || EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + 1;
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode ||
            BuildPipeline.isBuildingPlayer || PrefabStageUtility.GetCurrentPrefabStage() != null) return;
        File.Delete(Request); Run();
    }
    [MenuItem("Tools/Civil Craft/Tutorials/Bridge Statistics/Validate Composite Layout (Detached)")]
    public static void Run()
    {
        if (running || EditorApplication.isPlayingOrWillChangePlaymode) return;
        running = true; Directory.CreateDirectory("Logs"); Directory.CreateDirectory(Output);
        var report = new StringBuilder("Bridge Statistics: detached authored composite previews\n");
        var failures = new List<string>();
        Scene[] scenes = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).ToArray();
        var dirty = scenes.Select(s => s.isDirty).ToArray(); var roots = scenes.Select(s => s.GetRootGameObjects()).ToArray();
        var hashes = scenes.Where(s => File.Exists(s.path)).ToDictionary(s => s.path, s => Hash(s.path));
        Scene active = SceneManager.GetActiveScene(); Object[] selection = Selection.objects; Object selected = Selection.activeObject;
        float time = Time.timeScale; PlayerDataManager save = PlayerDataManager.Instance;
        PlayerData data = save != null ? save.CurrentData : null; string player = data != null ? JsonUtility.ToJson(data) : null;
        object oldController = typeof(BuildUIController).GetField("<Instance>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        var observed = new Dictionary<Object, string>(); Scene preview = default; RenderTexture originalTarget = RenderTexture.active;
        try
        {
            var scene = SceneManager.GetSceneByPath("Assets/Scenes/CanyonCrossing.unity");
            Require(scene.IsValid() && scene.isLoaded, "Open CanyonCrossing in Edit Mode.");
            TutorialManager manager = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<TutorialManager>(true)).Single();
            BuildUIController controls = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<BuildUIController>(true)).Single();
            TutorialSequence sequence = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<TutorialSequence>(true)).Single(s => s.lessonName == "Sequence_Build1");
            var explanationSteps = sequence.tutorialSteps.Where(s => s != null && s.OnStepStart != null &&
                Enumerable.Range(0, s.OnStepStart.GetPersistentEventCount()).Any(i => s.OnStepStart.GetPersistentMethodName(i) == nameof(BuildTutorialDirector.ExplainBridgeStatistics))).ToArray();
            Require(explanationSteps.Length == TutorialInstructionText.BridgeStatisticsExplanations.Length, "Apply the authored explanation pages before previewing them.");
            var serialized = new SerializedObject(manager);
            var sourceCard = ((GameObject)serialized.FindProperty("centerPanel").objectReferenceValue).transform as RectTransform;
            var sourceText = serialized.FindProperty("centerText").objectReferenceValue as TextMeshProUGUI;
            var sourceNext = ((GameObject)serialized.FindProperty("nextButton").objectReferenceValue).transform as RectTransform;
            var sourceStats = controls.statsPanel.transform as RectTransform;
            Require(sourceCard != null && sourceText != null && sourceNext != null && sourceStats != null && manager.SharedPointer != null, "Missing authored composite controls.");
            foreach (var value in sourceCard.GetComponentsInChildren<Component>(true).Concat(sourceStats.GetComponentsInChildren<Component>(true))
                .Concat(sourceNext.GetComponentsInChildren<Component>(true)).Cast<Object>().Append(manager).Append(controls).Append(sequence).Distinct())
                observed[value] = EditorJsonUtility.ToJson(value);
            var fonts = sourceCard.GetComponentsInChildren<TextMeshProUGUI>(true).Concat(sourceStats.GetComponentsInChildren<TextMeshProUGUI>(true))
                .Where(t => t.font != null).Select(t => AssetDatabase.GetAssetPath(t.font)).Distinct().ToDictionary(p => p, AssetDatabase.GetAssetDependencyHash);
            preview = EditorSceneManager.NewPreviewScene();
            var owner = new GameObject("Detached Statistics guide canvas", typeof(RectTransform), typeof(Canvas)); SceneManager.MoveGameObjectToScene(owner, preview);
            var canvasRect = (RectTransform)owner.transform; canvasRect.localScale = Vector3.one * .01f;
            var canvas = owner.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            var tutorialLayer = Container(canvasRect, "Authored tutorial canvas"); var buildLayer = Container(canvasRect, "Authored build canvas");
            Canvas tutorialCanvas = sourceCard.GetComponentInParent<Canvas>(true).rootCanvas, buildCanvas = sourceStats.GetComponentInParent<Canvas>(true).rootCanvas;
            var copies = new Dictionary<Transform, RectTransform>();
            RectTransform stats = CloneBranch(sourceStats, buildCanvas.transform, buildLayer, copies);
            RectTransform card = CloneBranch(sourceCard, tutorialCanvas.transform, tutorialLayer, copies);
            RectTransform nextButton = copies.TryGetValue(sourceNext, out RectTransform nextClone) ? nextClone : CloneBranch(sourceNext, tutorialCanvas.transform, tutorialLayer, copies);
            TextMeshProUGUI text = copies[sourceText.transform].GetComponent<TextMeshProUGUI>();
            var fixtureObject = new GameObject("Detached BuildUIController style fixture"); SceneManager.MoveGameObjectToScene(fixtureObject, preview);
            var fixture = fixtureObject.AddComponent<BuildUIController>(); fixture.statsPanel = stats.gameObject;
            fixture.targetCargoWeightText = LabelCopy(controls.targetCargoWeightText, copies);
            fixture.totalLengthText = LabelCopy(controls.totalLengthText, copies);
            fixture.membersCountText = LabelCopy(controls.membersCountText, copies);
            fixture.deadLoadText = LabelCopy(controls.deadLoadText, copies);
            fixture.estimatedCapacityText = LabelCopy(controls.estimatedCapacityText, copies);
            fixture.efficiencyRatioText = LabelCopy(controls.efficiencyRatioText, copies);
            fixture.factorOfSafetyText = controls.factorOfSafetyText != null ? LabelCopy(controls.factorOfSafetyText, copies) : null;
            typeof(BuildUIController).GetField("statsPanelRect", Private).SetValue(fixture, stats);
            typeof(BuildUIController).GetField("showEstimatedSafetyReadout", Private).SetValue(fixture, false);
            typeof(BuildUIController).GetMethod("StyleStatsPanel", Private).Invoke(fixture, null);
            TextMeshProUGUI[] readouts = { fixture.targetCargoWeightText, fixture.totalLengthText, fixture.membersCountText, fixture.deadLoadText, fixture.estimatedCapacityText, fixture.efficiencyRatioText };
            string[] samples = { "<color=#F2BF72>LIVE LOAD</color>  <b>200 kg</b>", "<color=#F2BF72>ROAD LENGTH</color>  <b>20.0 m</b>",
                "<color=#F2BF72>STRUCTURE</color>  <b>6 members  |  7 joints</b>", "<color=#F2BF72>DEAD LOAD</color>  <b>480.0 kg</b>",
                "<color=#F2BF72>EST. CAPACITY</color>  <b>~900 kg</b>", "<color=#F2BF72>EFFICIENCY RATIO</color>  <b>1.88</b>" };
            for (int i = 0; i < readouts.Length; i++) readouts[i].text = samples[i];
            stats.gameObject.SetActive(true); card.gameObject.SetActive(true); nextButton.gameObject.SetActive(true);
            var cameraOwner = new GameObject("Detached composite camera", typeof(Camera)); SceneManager.MoveGameObjectToScene(cameraOwner, preview);
            Camera camera = cameraOwner.GetComponent<Camera>(); camera.scene = preview; camera.useOcclusionCulling = false; camera.orthographic = true;
            camera.transform.position = new Vector3(0, 0, -20); camera.nearClipPlane = .1f; camera.farClipPlane = 100;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(37, 79, 216, 255); canvas.worldCamera = camera;
            RectTransform arrow = CloneArt((RectTransform)manager.SharedPointer.transform, canvasRect, new Dictionary<Transform, RectTransform>());
            arrow.anchorMin = arrow.anchorMax = new Vector2(.5f, .5f); arrow.localScale = Vector3.one;
            var pointer = arrow.gameObject.AddComponent<TutorialPointer>(); typeof(TutorialPointer).GetMethod("Awake", Private).Invoke(pointer, null);
            pointer.bounceAmount = manager.SharedPointer.bounceAmount; pointer.bounceSpeed = manager.SharedPointer.bounceSpeed;
            Vector2 position = sourceCard.anchoredPosition, size = sourceCard.rect.size;
            var screens = new[] { new Vector2Int(1600, 720), new Vector2Int(2048, 1280), new Vector2Int(1126, 337) };
            Require(TutorialInstructionText.BridgeStatisticsExplanations.Length == readouts.Length, "Expected exactly six visible readout explanations.");
            foreach (Vector2Int screen in screens)
            {
                // Give the real pointer its preview pixel rectangle before it
                // resolves safe-area placement; the Editor Game view may differ.
                var viewport = new RenderTexture(screen.x, screen.y, 24); viewport.Create(); camera.targetTexture = viewport;
                try
                {
                float tutorialScale = Scale(tutorialCanvas, screen), buildScale = Scale(buildCanvas, screen);
                canvasRect.sizeDelta = new Vector2(screen.x, screen.y) / tutorialScale;
                tutorialLayer.sizeDelta = canvasRect.rect.size; buildLayer.sizeDelta = new Vector2(screen.x, screen.y) / buildScale;
                buildLayer.localScale = Vector3.one * (buildScale / tutorialScale);
                camera.aspect = (float)screen.x / screen.y; camera.orthographicSize = canvasRect.rect.height * .005f;
                for (int i = 0; i < readouts.Length; i++)
                {
                    string label = screen.x + "x" + screen.y + "/row" + (i + 1); string file = Output + "/" + screen.x + "x" + screen.y + "-" + (i + 1) + ".png";
                    text.text = TutorialInstructionText.BridgeStatisticsExplanations[i]; text.transform.localScale = Vector3.one;
                    bool fits = TutorialCenterLayout.ApplyWithin(card, text, position, size, Vector2.zero, ((RectTransform)card.parent).rect);
                    Canvas.ForceUpdateCanvases(); text.ForceMeshUpdate(true, true);
                    pointer.PointAtBuildTarget(readouts[i].rectTransform, explanationSteps[i].pointerOffset, explanationSteps[i].pointerRotation);
                    typeof(TutorialPointer).GetMethod("LateUpdate", Private).Invoke(pointer, null);
                    try
                    {
                        Require(fits && text.fontSize >= 26 && !text.isTextOverflowing && !text.isTextTruncated, "Explanation text did not fit in full.");
                        Require(Contains(canvasRect.rect, Bounds(card, canvasRect)) && Contains(canvasRect.rect, Bounds(stats, canvasRect)),
                            "An instruction card or readout leaves the screen.");
                        Require(!Bounds(card, canvasRect).Overlaps(Bounds(stats, canvasRect)), "Instruction card overlaps Bridge Statistics.");
                        // The authored Next control may be a full-screen transparent
                        // tap target. Only visible Continue artwork competes for space.
                        foreach (var graphic in nextButton.GetComponentsInChildren<Graphic>(true).Where(g => g.gameObject.activeInHierarchy &&
                            g.color.a > .01f && (!(g is TextMeshProUGUI labelText) || !string.IsNullOrWhiteSpace(labelText.text))))
                        {
                            Rect visible = Bounds(graphic.rectTransform, canvasRect);
                            Require(Contains(canvasRect.rect, visible), "Visible Continue artwork leaves the screen.");
                            Require(!Bounds(card, canvasRect).Overlaps(visible), "Instruction card overlaps visible Continue artwork.");
                            Require(!Bounds(stats, canvasRect).Overlaps(visible), "Bridge Statistics overlaps visible Continue artwork.");
                        }
                        foreach (var row in readouts)
                        {
                            row.ForceMeshUpdate(true, true); Require(!row.isTextOverflowing && !row.isTextTruncated, "Readout is truncated: " + row.text);
                            Require(Contains(stats.rect, Bounds(row.rectTransform, stats)), "Readout leaves the stats panel: " + row.text);
                        }
                        Require(fixture.factorOfSafetyText == null || !fixture.factorOfSafetyText.gameObject.activeSelf, "Estimated Safety is visible.");
                        Require(pointer.IsPointingAt(readouts[i].rectTransform) && pointer.GetComponent<Canvas>().enabled &&
                            Contains(canvasRect.rect, Bounds(arrow, canvasRect)), "The row arrow is hidden or clipped.");
                        ValidateGlyphs(card, text);
                        report.AppendLine("PASS: " + label + " full explanation, six readouts, hidden Safety, visible row arrow, Continue and no card/readout/control overlap.");
                    }
                    catch (Exception e) { failures.Add(label + ": " + e.Message); report.AppendLine("RESIDUAL: " + label + ": " + e.Message); }
                    Render(camera, screen, file);
                    pointer.Hide();
                }
                report.AppendLine("GEOMETRY: " + screen + " card=" + Bounds(card, canvasRect) + " stats=" + Bounds(stats, canvasRect) + " Continue=" + Bounds(nextButton, canvasRect));
                }
                finally { camera.targetTexture = null; viewport.Release(); Object.DestroyImmediate(viewport); }
            }
            Require(fonts.All(p => AssetDatabase.GetAssetDependencyHash(p.Key) == p.Value), "Source font dependencies changed.");
        }
        catch (Exception e) { failures.Add(e.ToString()); report.AppendLine("FAILED: " + e); }
        finally
        {
            try
            {
                typeof(BuildUIController).GetField("<Instance>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, oldController);
                RenderTexture.active = originalTarget; if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
                Require(SceneManager.sceneCount == scenes.Length && SceneManager.GetActiveScene() == active, "Scene setup changed.");
                for (int i = 0; i < scenes.Length; i++) Require(SceneManager.GetSceneAt(i) == scenes[i] && scenes[i].isDirty == dirty[i] && scenes[i].GetRootGameObjects().SequenceEqual(roots[i]), "Scene roots/order/dirty flag changed.");
                Require(hashes.All(p => Hash(p.Key) == p.Value) && observed.All(p => p.Key != null && EditorJsonUtility.ToJson(p.Key) == p.Value), "Source scene bytes, tutorial data, or graphics changed.");
                Require(Selection.activeObject == selected && Selection.objects.SequenceEqual(selection) && Time.timeScale == time, "Selection/time scale changed.");
                Require(PlayerDataManager.Instance == save && (data == null || ReferenceEquals(data, save.CurrentData) && JsonUtility.ToJson(data) == player), "Player data changed.");
                report.AppendLine("PASS: live scenes, roots, dirty flags, source graphics/fonts, manager/sequence state, selection, time scale and player data preserved.");
            }
            catch (Exception e) { failures.Add(e.ToString()); report.AppendLine("FAILED PRESERVATION: " + e); }
            report.Insert(0, failures.Count == 0 ? "PASS\n" : "FAILED: " + failures.Count + " layout/preservation issues; source layout not changed.\n");
            report.AppendLine("Previews: " + Output); File.WriteAllText(Report, report.ToString()); running = false;
        }
    }
    private static TextMeshProUGUI LabelCopy(TextMeshProUGUI source, Dictionary<Transform, RectTransform> copies)
    { Require(source != null && copies.ContainsKey(source.transform), "Missing authored stats-row mapping."); return copies[source.transform].GetComponent<TextMeshProUGUI>(); }
    private static RectTransform Container(RectTransform parent, string name)
    {
        var owner = new GameObject(name, typeof(RectTransform)); var rect = (RectTransform)owner.transform; rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f); rect.pivot = new Vector2(.5f, .5f); return rect;
    }
    private static float Scale(Canvas canvas, Vector2Int screen)
    {
        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>(); Vector2 reference = scaler != null ? scaler.referenceResolution : new Vector2(1920, 1080);
        float match = scaler != null ? scaler.matchWidthOrHeight : .5f;
        return Mathf.Pow(screen.x / reference.x, 1 - match) * Mathf.Pow(screen.y / reference.y, match);
    }
    private static RectTransform CloneBranch(RectTransform source, Transform sourceCanvas, RectTransform parent, Dictionary<Transform, RectTransform> copies)
    {
        if (copies.TryGetValue(source, out RectTransform existing)) return existing;
        var chain = new Stack<RectTransform>(); Transform cursor = source.parent;
        while (cursor != null && cursor != sourceCanvas)
        { Require(cursor is RectTransform, "Unsupported non-UI parent in composite hierarchy."); chain.Push((RectTransform)cursor); cursor = cursor.parent; }
        Require(cursor == sourceCanvas, "Authored control is not beneath its canvas.");
        while (chain.Count > 0)
        {
            RectTransform original = chain.Pop();
            if (!copies.TryGetValue(original, out RectTransform ancestor)) { ancestor = Frame(original, parent); copies[original] = ancestor; }
            parent = ancestor;
        }
        return CloneArt(source, parent, copies);
    }
    private static RectTransform Frame(RectTransform source, RectTransform parent)
    {
        var owner = new GameObject(source.name, typeof(RectTransform)); var rect = (RectTransform)owner.transform; rect.SetParent(parent, false);
        rect.anchorMin = source.anchorMin; rect.anchorMax = source.anchorMax; rect.pivot = source.pivot; rect.sizeDelta = source.sizeDelta;
        rect.anchoredPosition = source.anchoredPosition; rect.localScale = source.localScale; rect.localRotation = source.localRotation; return rect;
    }
    private static RectTransform CloneArt(RectTransform source, RectTransform parent, Dictionary<Transform, RectTransform> copies)
    {
        RectTransform rect = Frame(source, parent); copies[source] = rect;
        if (source.TryGetComponent(out Image original))
        {
            Image image = rect.gameObject.AddComponent<Image>(); image.sprite = original.sprite; image.color = original.color; image.type = original.type;
            image.preserveAspect = original.preserveAspect; image.fillCenter = original.fillCenter; image.pixelsPerUnitMultiplier = original.pixelsPerUnitMultiplier; image.raycastTarget = false;
        }
        if (source.TryGetComponent(out TextMeshProUGUI originalText))
        {
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>(); text.font = originalText.font; text.fontSharedMaterial = originalText.fontSharedMaterial;
            text.text = originalText.text; text.fontSize = originalText.fontSize; text.fontStyle = originalText.fontStyle; text.color = originalText.color; text.alignment = originalText.alignment;
            text.richText = originalText.richText; text.enableWordWrapping = originalText.enableWordWrapping; text.enableAutoSizing = originalText.enableAutoSizing;
            text.fontSizeMin = originalText.fontSizeMin; text.fontSizeMax = originalText.fontSizeMax; text.characterSpacing = originalText.characterSpacing;
            text.wordSpacing = originalText.wordSpacing; text.lineSpacing = originalText.lineSpacing; text.paragraphSpacing = originalText.paragraphSpacing; text.margin = originalText.margin;
            text.overflowMode = originalText.overflowMode; text.raycastTarget = false;
        }
        foreach (Transform child in source) if (child is RectTransform childRect) CloneArt(childRect, rect, copies).gameObject.SetActive(child.gameObject.activeSelf);
        rect.gameObject.SetActive(true); return rect;
    }
    private static void ValidateGlyphs(RectTransform panel, TextMeshProUGUI text)
    {
        Rect inside = panel.rect; inside.xMin += 6; inside.xMax -= 6; inside.yMin += 6; inside.yMax -= 6;
        for (int i = 0; i < text.textInfo.characterCount; i++)
        {
            TMP_CharacterInfo c = text.textInfo.characterInfo[i]; if (!c.isVisible) continue;
            Vector3 min = panel.InverseTransformPoint(text.transform.TransformPoint(c.bottomLeft)), max = panel.InverseTransformPoint(text.transform.TransformPoint(c.topRight));
            Require(min.x >= inside.xMin - .5f && min.y >= inside.yMin - .5f && max.x <= inside.xMax + .5f && max.y <= inside.yMax + .5f, "Explanation glyph leaves the cream card.");
        }
    }
    private static Rect Bounds(RectTransform source, RectTransform relative)
    {
        var corners = new Vector3[4]; source.GetWorldCorners(corners); Vector3 min = relative.InverseTransformPoint(corners[0]), max = min;
        foreach (var corner in corners) { Vector3 p = relative.InverseTransformPoint(corner); min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }
    private static bool Contains(Rect outer, Rect inner) => inner.xMin >= outer.xMin - .5f && inner.yMin >= outer.yMin - .5f && inner.xMax <= outer.xMax + .5f && inner.yMax <= outer.yMax + .5f;
    private static void Render(Camera camera, Vector2Int screen, string file)
    {
        RenderTexture previousCameraTarget = camera.targetTexture;
        var target = previousCameraTarget ?? new RenderTexture(screen.x, screen.y, 24);
        bool owned = previousCameraTarget == null; if (owned) target.Create();
        RenderTexture previous = RenderTexture.active; Texture2D pixels = null;
        try
        {
            camera.targetTexture = target; Canvas.ForceUpdateCanvases();
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null) camera.Render();
            else UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera, new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture.active = target; pixels = new Texture2D(screen.x, screen.y, TextureFormat.RGBA32, false);
            pixels.ReadPixels(new Rect(0, 0, screen.x, screen.y), 0, 0); pixels.Apply(); File.WriteAllBytes(file, pixels.EncodeToPNG());
        }
        finally { camera.targetTexture = previousCameraTarget; RenderTexture.active = previous; if (pixels != null) Object.DestroyImmediate(pixels); if (owned) { target.Release(); Object.DestroyImmediate(target); } }
    }
    private static string Hash(string file) { using (var algorithm = SHA256.Create()) return BitConverter.ToString(algorithm.ComputeHash(File.ReadAllBytes(file))); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
#endif
