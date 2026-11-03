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

/// <summary>Detached graphics checks: no simulation, game button, player data or scene save.</summary>
public static class MultiLoadUIPresentationValidation
{
    public const string ReportPath = "Logs/multi-load-ui-previews.txt";
    [MenuItem("Tools/Civil Craft/Multiple Live Loads/4. Preview Phone and Tablet Layouts (Read Only)")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup(); Scene active = SceneManager.GetActiveScene();
        Object[] selected = Selection.objects; Object selection = Selection.activeObject;
        var before = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Where(scene => scene.isLoaded)
            .ToDictionary(scene => scene, scene => new { scene.isDirty, Roots = scene.GetRootGameObjects() });
        PlayerDataManager saves = PlayerDataManager.Instance; PlayerData data = saves != null ? saves.CurrentData : null;
        string playerJson = data != null ? JsonUtility.ToJson(data) : null;
        BuildUIController singleton = BuildUIController.Instance;
        var report = new StringBuilder("Multiple live-load HUD and scrollable requirements\n");
        Scene preview = default; TMP_FontAsset font = null, sourceFont = null; Material material = null; ContractSO contract = null;
        var ownedAtlases = new List<Texture2D>();
        string sourceFontJson = null, sourceMaterialJson = null; Dictionary<Texture2D, string> sourceAtlasHashes = null;
        var persistentFonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().Where(AssetDatabase.Contains)
            .ToDictionary(asset => asset, asset => EditorJsonUtility.ToJson(asset));
        RenderTexture previous = RenderTexture.active;
        void LogSourceDirtied(Scene scene)
        {
            if (before.TryGetValue(scene, out var state) && !state.isDirty)
                report.AppendLine("SOURCE DIRTY NOTIFICATION: " + scene.path + "\n" + Environment.StackTrace);
        }
        EditorSceneManager.sceneDirtied += LogSourceDirtied;
        try
        {
            BuildUIController source = Resources.FindObjectsOfTypeAll<BuildUIController>().FirstOrDefault(item => item != null && item.gameObject.scene.IsValid() && item.gameObject.scene.isLoaded && !EditorSceneManager.IsPreviewScene(item.gameObject.scene));
            Require(source != null, "Open a gameplay scene before running layout previews.");
            TMP_Text sourceText = source.stressText != null ? source.stressText : source.actionLogText;
            Require(sourceText != null && sourceText.font != null, "The gameplay font is missing.");
            sourceFont = sourceText.font; sourceFontJson = EditorJsonUtility.ToJson(sourceFont);
            sourceMaterialJson = sourceFont.material != null ? EditorJsonUtility.ToJson(sourceFont.material) : null;
            sourceAtlasHashes = sourceFont.atlasTextures.Where(atlas => atlas != null && atlas.isReadable).ToDictionary(atlas => atlas, AtlasHash);
            font = Object.Instantiate(sourceFont); font.hideFlags = HideFlags.HideAndDontSave;
            font.atlasTextures = sourceFont.atlasTextures.Select(atlas =>
            {
                if (atlas == null) return null;
                Texture2D copy = Object.Instantiate(atlas); copy.hideFlags = HideFlags.HideAndDontSave; ownedAtlases.Add(copy); return copy;
            }).ToArray();
            typeof(TMP_FontAsset).GetField("m_AtlasTexture", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(font, font.atlasTextures.FirstOrDefault());
            font.isMultiAtlasTexturesEnabled = false;
            font.fallbackFontAssetTable = new List<TMP_FontAsset>();
            if (font.material != null)
            {
                material = Object.Instantiate(font.material); material.hideFlags = HideFlags.HideAndDontSave;
                if (font.atlasTextures.Length > 0) material.mainTexture = font.atlasTextures[0];
                font.material = material;
            }
            font.ReadFontAssetDefinition();
            preview = EditorSceneManager.NewPreviewScene();
            var canvasRect = Owned(preview, "Detached Multi-load UI", typeof(RectTransform), typeof(Canvas)).GetComponent<RectTransform>();
            var canvas = canvasRect.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvasRect.localScale = Vector3.one * .01f;
            var camera = Owned(preview, "Preview Camera", typeof(Camera)).GetComponent<Camera>();
            camera.scene = preview; camera.enabled = false; camera.useOcclusionCulling = false;
            camera.transform.position = new Vector3(0f, 0f, -20f); camera.orthographic = true; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.18f, .30f, .42f); camera.nearClipPlane = .1f; camera.farClipPlane = 100f; canvas.worldCamera = camera;
            GameObject controllerObject = Owned(preview, "Inactive Preview Controller", typeof(RectTransform)); controllerObject.transform.SetParent(canvasRect, false); controllerObject.SetActive(false);
            var controller = controllerObject.AddComponent<BuildUIController>();
            var fontReference = Owned(preview, "Font Reference", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            fontReference.transform.SetParent(controllerObject.transform, false); fontReference.font = font; controller.actionLogText = fontReference;
            MultiLoadTestHUD hud = MultiLoadTestHUD.Ensure(controller);
            Require(hud != null, "Could not create the detached status template.");
            GameObject briefRoot = Owned(preview, "Project Brief", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)); briefRoot.transform.SetParent(canvasRect, false);
            RectTransform briefRect = (RectTransform)briefRoot.transform; briefRect.sizeDelta = new Vector2(960f, 112f); briefRect.anchoredPosition = new Vector2(0f, 150f);
            briefRoot.GetComponent<Image>().color = new Color32(255, 247, 224, 255);
            var brief = Owned(preview, "Brief Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            brief.transform.SetParent(briefRoot.transform, false); brief.rectTransform.sizeDelta = new Vector2(920f, 88f); brief.font = font; brief.fontSize = 25f; brief.color = new Color32(75, 46, 29, 255);
            contract = ScriptableObject.CreateInstance<ContractSO>(); contract.hideFlags = HideFlags.HideAndDontSave;
            GameObject fixtureObject = Owned(preview, "Inactive Outcome Fixture"); fixtureObject.SetActive(false);
            var physics = fixtureObject.AddComponent<BridgePhysicsManager>();
            var location = fixtureObject.AddComponent<BuildLocation>(); location.activeContract = contract;
            var outcomeVehicles = new List<LiveLoadVehicle>();
            for (int index = 0; index < 4; index++)
            {
                GameObject vehicleObject = Owned(preview, "Inactive Outcome Vehicle " + index); vehicleObject.SetActive(false);
                vehicleObject.transform.SetParent(fixtureObject.transform, false); outcomeVehicles.Add(vehicleObject.AddComponent<LiveLoadVehicle>());
            }
            foreach (Vector2Int screen in new[] { new Vector2Int(1600, 720), new Vector2Int(1920, 1080), new Vector2Int(1440, 1080) })
            {
                float scale = Mathf.Sqrt((float)screen.x / 1920f * screen.y / 1080f);
                canvasRect.sizeDelta = new Vector2(screen.x, screen.y) / scale; canvas.scaleFactor = scale;
                camera.aspect = (float)screen.x / screen.y; camera.orthographicSize = canvasRect.rect.height * .005f;
                var texture = new RenderTexture(screen.x, screen.y, 24); texture.Create(); camera.targetTexture = texture;
                try
                {
                    briefRoot.SetActive(false); hud.Panel.gameObject.SetActive(true);
                    foreach (string text in new[] { "Vehicle 2/4 — Cargo Truck\n1/4 passed", "ALL LIVE LOADS PASSED · 4/4", "LIVE LOAD TEST FAILED\nCargo Truck could not finish" })
                    {
                        hud.Status.text = text; Prepare(canvasRect);
                        Require(Contains(camera.pixelRect, Bounds(hud.Panel, camera)), "Status card leaves the screen: " + screen);
                        Require(!hud.Status.isTextTruncated && !hud.Status.isTextOverflowing && hud.Status.fontSize >= 23f, "Status text is clipped: " + text);
                        foreach (TMP_CharacterInfo glyph in hud.Status.textInfo.characterInfo.Take(hud.Status.textInfo.characterCount))
                            if (glyph.isVisible) Require(Contains(Bounds(hud.Panel, camera), GlyphBounds(hud.Status, glyph, camera)), "A status glyph leaves its card.");
                    }
                    Render(camera, texture, "Logs/MultiLoadUI/Status-" + screen.x + "x" + screen.y + ".png");
                    hud.Panel.gameObject.SetActive(false); briefRoot.SetActive(true);
                    foreach (ContractSO.VehicleTestMode mode in new[] { ContractSO.VehicleTestMode.Sequential, ContractSO.VehicleTestMode.Together })
                    for (int count = 2; count <= 4; count++)
                    {
                        contract.vehicleTestMode = mode; contract.liveLoadTests.Clear();
                        for (int index = 0; index < count; index++) contract.liveLoadTests.Add(new ContractLiveLoadEntry
                        { entryId = "preview-" + index, displayName = index == count - 1 ? "Last required heavy cargo vehicle" : "Vehicle " + (index + 1), emptyWeightKg = 1000f + index * 3000f, departureDelay = index * 3f, allowCargo = index == count - 1, minimumLoadedCargo = index == count - 1 ? 4 : 0 });
                        ContractLiveLoadPresentation.ShowScrollableBrief(brief, contract, "Connect the factory and market. Every listed vehicle must safely cross this bridge.");
                        ScrollRect scroll = briefRoot.GetComponentInChildren<ScrollRect>(true); Require(scroll != null, "Missing touch-scrollable requirements.");
                        Prepare(canvasRect); Require(scroll.content.rect.height > scroll.viewport.rect.height, "Roster fixture does not overflow its scroll slot.");
                        TMP_Text roster = scroll.content.GetComponent<TMP_Text>();
                        Require(roster.text.Contains("Last required heavy cargo vehicle") && roster.text.Contains("4 items") && !roster.isTextTruncated,
                            "A required vehicle or cargo rule is missing.");
                        scroll.verticalNormalizedPosition = 0f; Prepare(canvasRect);
                        Rect content = Bounds(scroll.content, camera), viewport = Bounds(scroll.viewport, camera);
                        Require(Mathf.Abs(content.yMin - viewport.yMin) < 3f, "The last roster row cannot scroll into view.");
                        string path = "Logs/MultiLoadUI/Requirements-" + mode + "-" + count + "-" + screen.x + "x" + screen.y + "-bottom.png";
                        Render(camera, texture, path);
                        report.AppendLine("PASS: " + mode + "; " + count + " vehicles; " + screen.x + "x" + screen.y + "; complete roster, cargo and bottom scroll. " + path);
                    }
                    contract.vehicleTestMode = ContractSO.VehicleTestMode.Single;
                    ContractLiveLoadPresentation.ShowScrollableBrief(brief, contract, "Legacy brief");
                    Require(brief.gameObject.activeSelf && !briefRoot.transform.Find("Multi Live Load Requirements").gameObject.activeSelf, "Single-load brief visibility was not restored.");
                    ValidateOutcomes(preview, canvasRect, briefRoot, brief, font, contract, location, physics, outcomeVehicles, camera, texture, screen, report);
                }
                finally { camera.targetTexture = null; RenderTexture.active = previous; texture.Release(); Object.DestroyImmediate(texture); }
            }
            report.Insert(0, "PASS\n");
        }
        catch (Exception error) { report.Insert(0, "FAILED\n"); report.AppendLine(error.ToString()); }
        finally
        {
            RenderTexture.active = previous;
            if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
            if (contract != null) Object.DestroyImmediate(contract);
            if (material != null) Object.DestroyImmediate(material);
            if (font != null) Object.DestroyImmediate(font);
            foreach (Texture2D atlas in ownedAtlases) if (atlas != null) Object.DestroyImmediate(atlas);
            if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
            EditorSceneManager.sceneDirtied -= LogSourceDirtied;
            Selection.objects = selected; Selection.activeObject = selection;
            SceneSetup[] after = EditorSceneManager.GetSceneManagerSetup();
            bool safe = setup.Length == after.Length && setup.Zip(after, (a, b) => a.path == b.path && a.isLoaded == b.isLoaded && a.isActive == b.isActive).All(value => value) &&
                before.All(pair => pair.Key.IsValid() && pair.Key.isLoaded && pair.Key.isDirty == pair.Value.isDirty && pair.Key.GetRootGameObjects().SequenceEqual(pair.Value.Roots)) &&
                BuildUIController.Instance == singleton && PlayerDataManager.Instance == saves && (data == null || ReferenceEquals(data, saves.CurrentData) && JsonUtility.ToJson(data) == playerJson);
            safe &= sourceFont == null || (EditorJsonUtility.ToJson(sourceFont) == sourceFontJson &&
                (sourceFont.material == null ? sourceMaterialJson == null : EditorJsonUtility.ToJson(sourceFont.material) == sourceMaterialJson) &&
                sourceAtlasHashes != null && sourceAtlasHashes.All(pair => pair.Key != null && AtlasHash(pair.Key) == pair.Value));
            safe &= persistentFonts.All(pair => pair.Key != null && EditorJsonUtility.ToJson(pair.Key) == pair.Value);
            if (!safe)
            {
                report.Insert(0, "FAILED SAFETY: source scene or player state changed.\n");
                foreach (var pair in before)
                    if (!pair.Key.IsValid() || !pair.Key.isLoaded || pair.Key.isDirty != pair.Value.isDirty || !pair.Key.GetRootGameObjects().SequenceEqual(pair.Value.Roots))
                        report.AppendLine("SOURCE SCENE DIFFERENCE: " + pair.Key.path + "; dirty " + pair.Value.isDirty + " -> " + pair.Key.isDirty + "; roots " + pair.Value.Roots.Length + " -> " + pair.Key.GetRootGameObjects().Length);
                foreach (var pair in persistentFonts)
                    if (pair.Key == null || EditorJsonUtility.ToJson(pair.Key) != pair.Value)
                    {
                        string afterFont = pair.Key != null ? EditorJsonUtility.ToJson(pair.Key) : "destroyed";
                        int offset = 0; while (offset < pair.Value.Length && offset < afterFont.Length && pair.Value[offset] == afterFont[offset]) offset++;
                        report.AppendLine("SOURCE FONT DIFFERENCE: " + (pair.Key != null ? pair.Key.name : "destroyed") + "; offset " + offset + "; before " + pair.Value.Substring(Math.Max(0, offset - 30), Math.Min(160, pair.Value.Length - Math.Max(0, offset - 30))) + "; after " + afterFont.Substring(Math.Max(0, Math.Min(offset, afterFont.Length) - 30), Math.Min(160, afterFont.Length - Math.Max(0, Math.Min(offset, afterFont.Length) - 30))));
                    }
                report.AppendLine("SOURCE STATE: singleton=" + (BuildUIController.Instance == singleton) + "; save manager=" + (saves == PlayerDataManager.Instance) +
                    "; player=" + (data == null || ReferenceEquals(data, saves.CurrentData) && JsonUtility.ToJson(data) == playerJson) + "; setup=" + (setup.Length == after.Length));
            }
            else report.AppendLine("PASS: source scenes, dirty flags, player data, singleton and selection preserved. No scene save or game callback.");
            Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, report.ToString());
        }
    }
    private static void ValidateOutcomes(Scene preview, RectTransform canvas, GameObject briefRoot, TextMeshProUGUI brief, TMP_FontAsset font,
        ContractSO contract, BuildLocation location, BridgePhysicsManager physics, List<LiveLoadVehicle> vehicles,
        Camera camera, RenderTexture texture, Vector2Int screen, StringBuilder report)
    {
        contract.vehicleTestMode = ContractSO.VehicleTestMode.Together;
        var definitions = new List<LiveLoadTestParticipantDefinition>();
        for (int index = 0; index < 4; index++) definitions.Add(new LiveLoadTestParticipantDefinition("result-" + index,
            "Heavy delivery vehicle with a deliberately long display name " + (index + 1), vehicles[index], 1000f + index * 3000f, index * 3f));
        var session = new LiveLoadTestSession(physics, new LiveLoadTestRoster(contract, location, ContractSO.VehicleTestMode.Together, definitions));
        PropertyInfo state = typeof(LiveLoadTestParticipant).GetProperty("State", BindingFlags.Instance | BindingFlags.Public);
        state.SetValue(session.Participants[0], LiveLoadTestParticipantState.Passed);
        state.SetValue(session.Participants[3], LiveLoadTestParticipantState.Failed);
        string outcome = "The heavy cargo vehicle could not complete the crossing safely. Review the bridge and retest all required vehicles.\n\n" + ContractLiveLoadPresentation.ResultSummary(session);
        ContractLiveLoadPresentation.ShowScrollableText(brief, true, outcome);
        ScrollRect failureScroll = briefRoot.GetComponentInChildren<ScrollRect>(true); Prepare(canvas);
        Require(failureScroll.content.rect.height > failureScroll.viewport.rect.height, "Failure outcome fixture must scroll.");
        failureScroll.verticalNormalizedPosition = 0f; Prepare(canvas);
        Require(Mathf.Abs(Bounds(failureScroll.content, camera).yMin - Bounds(failureScroll.viewport, camera).yMin) < 3f &&
            failureScroll.content.GetComponent<TMP_Text>().text.Contains("FAILED"), "Last failed vehicle cannot be read.");
        // A refresh with the same text must not fight the user's drag position.
        ContractLiveLoadPresentation.ShowScrollableText(brief, true, outcome);
        Require(failureScroll.verticalNormalizedPosition < .01f, "Refreshing identical outcomes resets the scroll position.");
        Render(camera, texture, "Logs/MultiLoadUI/Failure-4-" + screen.x + "x" + screen.y + "-bottom.png");
        briefRoot.SetActive(false);
        GameObject viewportObject = Owned(preview, "Detached Receipt Viewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
        viewportObject.transform.SetParent(canvas, false); RectTransform viewport = (RectTransform)viewportObject.transform;
        viewport.sizeDelta = new Vector2(600f, 220f); viewport.anchoredPosition = new Vector2(0f, 100f);
        viewportObject.GetComponent<Image>().color = new Color32(255, 247, 224, 255);
        GameObject contentObject = Owned(preview, "Receipt Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentObject.transform.SetParent(viewport, false); RectTransform content = (RectTransform)contentObject.transform;
        content.anchorMin = new Vector2(0f, 1f); content.anchorMax = Vector2.one; content.pivot = new Vector2(.5f, 1f); content.sizeDelta = Vector2.zero;
        VerticalLayoutGroup layout = contentObject.GetComponent<VerticalLayoutGroup>(); layout.childControlHeight = true; layout.childForceExpandHeight = false; layout.childControlWidth = true; layout.childForceExpandWidth = true;
        contentObject.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        ScrollRect scroll = viewportObject.GetComponent<ScrollRect>(); scroll.viewport = viewport; scroll.content = content; scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped;
        // Real receipts also contain the existing 96px material rows. A result
        // row alone can legitimately fit; test the complete scrollable receipt.
        for (int index = 0; index < 3; index++)
        {
            GameObject materialRow = Owned(preview, "Material receipt row " + index, typeof(RectTransform), typeof(LayoutElement), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            materialRow.transform.SetParent(content, false);
            LayoutElement element = materialRow.GetComponent<LayoutElement>(); element.minHeight = element.preferredHeight = 96f;
            TMP_Text materialLabel = materialRow.GetComponent<TMP_Text>(); materialLabel.font = font; materialLabel.fontSize = 22f;
            materialLabel.text = "BRIDGE MATERIAL " + (index + 1); materialLabel.color = new Color32(73, 48, 29, 255);
        }
        ContractLiveLoadPresentation.AppendReceiptResults(content, font, session); Prepare(canvas);
        TMP_Text row = content.Find("Multiple Live Load Results").GetComponent<TMP_Text>();
        Require(row != null && row.fontSize == 22f && !row.isTextTruncated && !row.isTextOverflowing && row.text.Contains("PASSED") && row.text.Contains("FAILED") && row.text.Contains("NOT COMPLETED"), "Receipt loses per-vehicle outcomes or clips its measured row.");
        Require(content.rect.height > viewport.rect.height, "Long receipt outcome fixture must scroll.");
        scroll.verticalNormalizedPosition = 0f; Prepare(canvas);
        Require(Mathf.Abs(Bounds(content, camera).yMin - Bounds(viewport, camera).yMin) < 3f, "Last receipt outcome cannot scroll into view.");
        Render(camera, texture, "Logs/MultiLoadUI/Receipt-4-" + screen.x + "x" + screen.y + "-bottom.png");
        Object.DestroyImmediate(viewportObject);
        report.AppendLine("PASS: " + screen.x + "x" + screen.y + "; four long vehicle names, passed/failed/not-completed outcomes, measured receipt row, failure-detail scroll and idempotent refresh.");
        briefRoot.SetActive(true);
    }
    private static void Prepare(RectTransform canvas)
    {
        foreach (TMP_Text label in canvas.GetComponentsInChildren<TMP_Text>(true)) label.ForceMeshUpdate(true, true);
        // A Canvas without an ILayoutController is not a layout root. Rebuild
        // each copied group explicitly; batch Edit Mode has no player UI tick.
        foreach (LayoutGroup group in canvas.GetComponentsInChildren<LayoutGroup>(true).Reverse())
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)group.transform);
        LayoutRebuilder.ForceRebuildLayoutImmediate(canvas);
        foreach (Graphic graphic in canvas.GetComponentsInChildren<Graphic>(false)) graphic.Rebuild(CanvasUpdate.PreRender);
    }
    private static GameObject Owned(Scene scene, string name, params Type[] components) { var value = new GameObject(name, components); SceneManager.MoveGameObjectToScene(value, scene); return value; }
    private static Rect Bounds(RectTransform rect, Camera camera)
    {
        Vector3[] points = new Vector3[4]; rect.GetWorldCorners(points);
        Vector2 min = Vector2.one * float.PositiveInfinity, max = Vector2.one * float.NegativeInfinity;
        foreach (Vector3 point in points) { Vector2 pixel = RectTransformUtility.WorldToScreenPoint(camera, point); min = Vector2.Min(min, pixel); max = Vector2.Max(max, pixel); }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }
    private static bool Contains(Rect outer, Rect inner) => inner.xMin >= outer.xMin - 1f && inner.xMax <= outer.xMax + 1f && inner.yMin >= outer.yMin - 1f && inner.yMax <= outer.yMax + 1f;
    private static Rect GlyphBounds(TMP_Text label, TMP_CharacterInfo glyph, Camera camera)
    {
        Vector2 a = RectTransformUtility.WorldToScreenPoint(camera, label.transform.TransformPoint(glyph.bottomLeft));
        Vector2 b = RectTransformUtility.WorldToScreenPoint(camera, label.transform.TransformPoint(glyph.topRight));
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }
    private static void Render(Camera camera, RenderTexture texture, string path)
    {
        camera.Render(); RenderTexture.active = texture; var pixels = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
        try
        {
            pixels.ReadPixels(new Rect(0f, 0f, texture.width, texture.height), 0, 0); pixels.Apply();
            // Bounds checks alone cannot prove that the Canvas actually rendered.
            // Every fixture includes the shared brown UI/text palette.
            int brownPixels = 0;
            foreach (Color32 pixel in pixels.GetPixels32())
                if (pixel.r > 40 && pixel.r > pixel.g * 1.12f && pixel.g > pixel.b * 1.12f) brownPixels++;
            Require(brownPixels >= 100, "The preview did not render its UI palette: " + path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, pixels.EncodeToPNG());
        }
        finally { Object.DestroyImmediate(pixels); }
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static string AtlasHash(Texture2D atlas) { using (SHA256 hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(atlas.GetRawTextureData())); }
}
#endif
