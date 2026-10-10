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
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Opt-in font asset generation and isolated book previews; never saves a scene.</summary>
[InitializeOnLoad]
public static class AlmanacTypographyAuthoring
{
    private const string Request = "Temp/AlmanacTypography.request";
    private const string Report = "Temp/AlmanacTypographyValidation.txt";
    private const string FontPath = "Assets/Resources/Fonts/Internet Friends SDF.asset";
    private const string SourcePath = "Assets/Font/Internet Friends.ttf";
    private const string LiberationPath = "Assets/TextMesh Pro/Fonts/LiberationSans.ttf";
    private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static double nextCheck;
    private static bool running;

    static AlmanacTypographyAuthoring() { EditorApplication.update += Check; }

    private static void Check()
    {
        if (running || EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 1;
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer ||
            PrefabStageUtility.GetCurrentPrefabStage() != null) return;
        File.Delete(Request);
        Run();
    }

    [MenuItem("Tools/Civil Craft/Almanac/Generate Handwritten Font and Check Readability")]
    public static void Run()
    {
        if (running || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
            EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer ||
            PrefabStageUtility.GetCurrentPrefabStage() != null) return;
        running = true;
        var report = new StringBuilder();
        try
        {
            TMP_FontAsset font = EnsureFont(report);
            AlmanacManager source = Resources.FindObjectsOfTypeAll<AlmanacManager>()
                .FirstOrDefault(book => book != null && book.gameObject.scene.IsValid() &&
                    book.gameObject.scene.isLoaded && !EditorSceneManager.IsPreviewScene(book.gameObject.scene) &&
                    book.Panel != null && book.categories.Count > 0);
            if (source != null) CaptureBook(source, font, report);
            else report.AppendLine("WAIT: Font generated successfully. Open a scene containing an Almanac and run this menu again for mobile previews. No scene was opened or saved.");
            EditorUtility.SetDirty(font);
            AssetDatabase.SaveAssetIfDirty(font);
            File.WriteAllText(Report, (source != null ? "PASS: " : "WAIT: ") +
                "Handwritten font generated; isolated preview status below.\n" + report);
            Debug.Log("[Almanac typography] " + report);
        }
        catch (Exception error)
        {
            File.WriteAllText(Report, "FAIL: " + error + "\n" + report);
            Debug.LogException(error);
        }
        finally { running = false; }
    }

    private static TMP_FontAsset EnsureFont(StringBuilder report)
    {
        Font source = AssetDatabase.LoadAssetAtPath<Font>(SourcePath);
        if (source == null) throw new InvalidOperationException("Internet Friends source font is missing or not imported: " + SourcePath);
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder("Assets/Resources/Fonts")) AssetDatabase.CreateFolder("Assets/Resources", "Fonts");

        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
        {
            if (File.Exists(FontPath)) throw new InvalidOperationException("Another asset already occupies the new font path; nothing overwritten.");
            font = TMP_FontAsset.CreateFontAsset(source, 90, 9, GlyphRenderMode.SDFAA,
                1024, 1024, AtlasPopulationMode.Dynamic, true);
            if (font == null) throw new InvalidOperationException("Internet Friends font face could not be loaded.");
            font.name = "Internet Friends SDF";
            AssetDatabase.CreateAsset(font, FontPath);
            PersistSubassets(font, font);
        }
        if (font.sourceFontFile != source || font.atlasPopulationMode != AtlasPopulationMode.Dynamic)
            throw new InvalidOperationException("Existing Internet Friends SDF does not reference the requested dynamic source. Nothing replaced.");

        TMP_FontAsset bekind = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            "Assets/TextMesh Pro/Resources/Fonts & Materials/Bekind Sans SDF.asset");
        TMP_FontAsset liberation = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
        string characters = CollectCharacters();
        font.TryAddCharacters(characters, out string missing, true);
        EnsurePrimaryUnderline(font, report);
        foreach (char character in "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789")
            if (!font.HasCharacter(character))
                throw new InvalidOperationException("The handwriting face lacks a required letter/digit: " + character);

        // Existing static fallbacks are read-only. A private symbols face prevents
        // currency glyphs from modifying Liberation's shared dynamic fallback.
        TMP_FontAsset symbols = AssetDatabase.LoadAllAssetsAtPath(FontPath).OfType<TMP_FontAsset>()
            .FirstOrDefault(asset => asset != font && asset.name == "Almanac Symbols SDF");
        string extra = new string((missing ?? string.Empty).Where(character =>
            !char.IsControl(character) && !char.IsWhiteSpace(character) &&
            (bekind == null || !bekind.HasCharacter(character)) &&
            (liberation == null || !liberation.HasCharacter(character))).Distinct().ToArray());
        if (!string.IsNullOrEmpty(extra) && symbols == null)
        {
            Font symbolSource = AssetDatabase.LoadAssetAtPath<Font>(LiberationPath);
            if (symbolSource == null) throw new InvalidOperationException("Existing Liberation font source is missing for book symbols.");
            symbols = TMP_FontAsset.CreateFontAsset(symbolSource, 72, 8, GlyphRenderMode.SDFAA,
                512, 512, AtlasPopulationMode.Dynamic, true);
            if (symbols == null) throw new InvalidOperationException("Book symbol font could not be created.");
            symbols.name = "Almanac Symbols SDF";
            AssetDatabase.AddObjectToAsset(symbols, font);
            PersistSubassets(symbols, font);
        }
        if (symbols != null)
        {
            symbols.TryAddCharacters(extra, out string unavailable, true);
            string stillUnavailable = new string((unavailable ?? string.Empty)
                .Where(character => !symbols.HasCharacter(character)).Distinct().ToArray());
            if (!string.IsNullOrEmpty(stillUnavailable))
                report.AppendLine("Unprovided font symbols: " + UnicodeList(stillUnavailable));
            PersistSubassets(symbols, font);
            EditorUtility.SetDirty(symbols);
        }
        font.fallbackFontAssetTable = new List<TMP_FontAsset>();
        if (symbols != null) font.fallbackFontAssetTable.Add(symbols);
        if (bekind != null) font.fallbackFontAssetTable.Add(bekind);
        if (liberation != null) font.fallbackFontAssetTable.Add(liberation);
        PersistSubassets(font, font);
        foreach (char character in "AaZz 0123456789 ₱ % kg N m ’ • –")
            if (!char.IsWhiteSpace(character) && !font.HasCharacter(character, true, false))
                throw new InvalidOperationException("Missing essential book glyph: " + UnicodeList(character.ToString()));
        EditorUtility.SetDirty(font);
        AssetDatabase.SaveAssetIfDirty(font);
        report.AppendLine("FONT: " + FontPath + "; " + font.characterTable.Count + " handwritten glyphs; " +
            font.atlasTextures.Length + " prewarmed 1024px atlas(es). Source font retained for dynamic names; private symbol fallback, shared fonts and TMP defaults unchanged.");
        return font;
    }

    private static void EnsurePrimaryUnderline(TMP_FontAsset font, StringBuilder report)
    {
        // TMP reads its underline texture from the primary face, not fallbacks.
        // Internet Friends omits '_'; its own existing hyphen is the same simple
        // stroke needed by TMP's underline strip. Alias that baked glyph only.
        font.ReadFontAssetDefinition();
        if (!font.HasCharacter('_'))
        {
            if (!font.characterLookupTable.TryGetValue(0x2D, out TMP_Character hyphen) ||
                hyphen.glyph == null || hyphen.glyph.glyphRect.width <= 0 || hyphen.glyph.glyphRect.height <= 0 ||
                hyphen.glyph.atlasIndex < 0 || hyphen.glyph.atlasIndex >= font.atlasTextures.Length ||
                font.atlasTextures[hyphen.glyph.atlasIndex] == null)
                throw new InvalidOperationException("Internet Friends has no baked hyphen glyph for its primary underline stroke.");
            font.characterTable.Add(new TMP_Character(0x5F, font, hyphen.glyph));
            font.ReadFontAssetDefinition();
            report.AppendLine("PRIMARY GLYPH: underscore uses Internet Friends' existing baked hyphen stroke; no new atlas or source-font edit.");
        }
        if (!font.HasCharacter('_')) throw new InvalidOperationException("Primary underline glyph is still unavailable.");
        // Keeping baked dynamic data prevents build processing from discarding
        // both the warmed atlas and the primary-font alias.
        var serialized = new SerializedObject(font);
        SerializedProperty clearOnBuild = serialized.FindProperty("m_ClearDynamicDataOnBuild");
        if (clearOnBuild == null) throw new InvalidOperationException("TMP clear-dynamic-data build setting is unavailable.");
        clearOnBuild.boolValue = false;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(font);
    }

    private static void PersistSubassets(TMP_FontAsset face, TMP_FontAsset owner)
    {
        if (face.material != null)
        {
            face.material.name = face.name + " Material";
            if (!AssetDatabase.Contains(face.material)) AssetDatabase.AddObjectToAsset(face.material, owner);
            EditorUtility.SetDirty(face.material);
        }
        for (int i = 0; i < face.atlasTextures.Length; i++)
        {
            Texture2D atlas = face.atlasTextures[i];
            if (atlas == null) continue;
            atlas.name = face.name + " Atlas " + i;
            if (!AssetDatabase.Contains(atlas)) AssetDatabase.AddObjectToAsset(atlas, owner);
            EditorUtility.SetDirty(atlas);
        }
    }

    private static string CollectCharacters()
    {
        var characters = new HashSet<char>();
        for (int i = 32; i <= 126; i++) characters.Add((char)i);
        AddCharacters(characters, "₱ % × ° ² ³ • ’ ‘ “ ” – — … ← → é ñ Engineering Engineer Guest Novice Builder");
        foreach (string type in new[] { "LessonData", "BridgeMaterialSO", "ContractSO" })
            foreach (string guid in AssetDatabase.FindAssets("t:" + type))
            {
                Object data = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                if (data == null) continue;
                var serialized = new SerializedObject(data);
                SerializedProperty property = serialized.GetIterator();
                while (property.Next(true))
                    if (property.propertyType == SerializedPropertyType.String) AddCharacters(characters, property.stringValue);
            }
        return new string(characters.Where(character => !char.IsControl(character) && !char.IsSurrogate(character))
            .OrderBy(character => character).ToArray());
    }

    private static void AddCharacters(ISet<char> destination, string value)
    { if (value != null) foreach (char character in value) destination.Add(character); }

    private static string UnicodeList(string value)
    { return string.Join(" ", value.Distinct().Select(character => "U+" + ((int)character).ToString("X4"))); }

    private static void CaptureBook(AlmanacManager source, TMP_FontAsset font, StringBuilder report)
    {
        if (source.categories[0].leftPageZone == null) throw new InvalidOperationException("Loaded book has no first page zone.");
        Transform sourcePanel = source.categories[0].leftPageZone.parent.parent.parent;
        var snapshots = sourcePanel.GetComponentsInChildren<Component>(true).Where(component => component != null)
            .ToDictionary(component => component, component => EditorJsonUtility.ToJson(component));
        bool originalDirty = source.gameObject.scene.isDirty;
        Scene preview = EditorSceneManager.NewPreviewScene();
        RenderTexture previous = RenderTexture.active;
        try
        {
            var root = new GameObject("AlmanacTypographyPreviewCanvas", typeof(RectTransform), typeof(Canvas));
            SceneManager.MoveGameObjectToScene(root, preview);
            RectTransform canvasRect = (RectTransform)root.transform;
            canvasRect.sizeDelta = new Vector2(1920, 1080);
            root.transform.localScale = Vector3.one * .01f;
            Canvas canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            GameObject clone = Object.Instantiate(sourcePanel.gameObject, root.transform, false);
            clone.SetActive(true); Stretch((RectTransform)clone.transform);
            // A closed authored book may retain an invisible animation group.
            // Normalize only the disposable preview, never the source hierarchy.
            foreach (CanvasGroup group in clone.GetComponentsInChildren<CanvasGroup>(true)) group.alpha = 1f;
            var ownerObject = new GameObject("AlmanacTypographyPreviewOwner");
            SceneManager.MoveGameObjectToScene(ownerObject, preview);
            AlmanacManager owner = ownerObject.AddComponent<AlmanacManager>(); owner.almanacCanvas = clone;
            typeof(AlmanacManager).GetField("bookMotion", PrivateInstance)
                .SetValue(owner, ownerObject.AddComponent<AlmanacBookMotion>());
            foreach (AlmanacCategory category in source.categories)
                owner.categories.Add(new AlmanacCategory {
                    tabType = category.tabType, categoryName = category.categoryName,
                    leftPageZone = FindClone(clone.transform, category.leftPageZone, sourcePanel),
                    rightPageZone = FindClone(clone.transform, category.rightPageZone, sourcePanel)
                });
            // Assign before building runtime entries so their template has the new atlas.
            foreach (TMP_Text label in clone.GetComponentsInChildren<TMP_Text>(true))
            { label.font = font; label.fontSharedMaterial = font.material; }
            typeof(AlmanacManager).GetMethod("InitializeBook", PrivateInstance).Invoke(owner, null);
            Type typography = typeof(AlmanacManager).Assembly.GetType("AlmanacTypography", true);
            typography.GetMethod("ApplyExistingBook", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { owner });
            foreach (AlmanacCategory category in owner.categories)
                foreach (GameObject page in category.leftPages.Concat(category.rightPages)) page.SetActive(true);

            var cameraObject = new GameObject("AlmanacTypographyPreviewCamera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, preview);
            Camera camera = cameraObject.GetComponent<Camera>(); camera.scene = preview;
            camera.orthographic = true; camera.transform.position = new Vector3(0, 0, -20);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(138, 111, 88, 255);
            foreach (AlmanacCategory category in owner.categories)
            {
                ShowCategory(owner, category);
                if (category.tabType == AlmanacTabType.Contracts)
                {
                    ContractAlmanacTab contract = clone.GetComponentsInChildren<ContractAlmanacTab>(true)
                        .FirstOrDefault(tab => tab.titleText != null && tab.snapshotImage != null);
                    if (contract != null)
                    {
                        typeof(ContractAlmanacTab).GetMethod("EnsurePresentation", PrivateInstance).Invoke(contract, null);
                        contract.titleText.text = "Canyon Crossing";
                        contract.clientText.text = "COMMISSIONED BY / THE COMMUNITY";
                        ContractSO longest = LoadAll<ContractSO>().OrderByDescending(item => (item.jobDescription ?? string.Empty).Length).FirstOrDefault();
                        contract.descriptionText.text = longest != null ? longest.jobDescription : "Build a reliable connection for the community. Review your design, budget, and strongest result.";
                        contract.snapshotCaptionText.text = "Bridge photograph";
                        contract.pageCounterText.text = "PROJECT 01 / 30";
                        contract.rewardsText.text = "<b>2,500</b> CONTRACT PAY\n<b>150</b> EXPERIENCE";
                    }
                }
                CaptureSizes(source, owner, canvasRect, camera, category.tabType.ToString(), font, report);
                if (category.tabType == AlmanacTabType.Lessons || category.tabType == AlmanacTabType.Materials)
                {
                    AlmanacLearningContent content = category.tabType == AlmanacTabType.Lessons
                        ? AlmanacLearningContent.Lessons : AlmanacLearningContent.Materials;
                    AlmanacLearningHub hub = clone.GetComponents<AlmanacLearningHub>().First(item =>
                        (AlmanacLearningContent)typeof(AlmanacLearningHub).GetField("contentType", PrivateInstance).GetValue(item) == content);
                    string title, description, facts; Sprite image;
                    if (content == AlmanacLearningContent.Lessons)
                    {
                        LessonData lesson = LoadAll<LessonData>().OrderByDescending(item => (item.AlmanacDescription ?? string.Empty).Length).FirstOrDefault();
                        if (lesson == null) throw new InvalidOperationException("No lesson asset available for a real long-description preview.");
                        title = lesson.Title; description = lesson.AlmanacDescription; image = lesson.AlmanacImage;
                        facts = "Review the principle, then look for it in your next structure.";
                    }
                    else
                    {
                        BridgeMaterialSO material = LoadAll<BridgeMaterialSO>().OrderByDescending(item => (item.AlmanacDescription ?? string.Empty).Length).FirstOrDefault();
                        if (material == null) throw new InvalidOperationException("No material asset available for a real long-description preview.");
                        title = material.GetDisplayName(); description = material.AlmanacDescription; image = material.AlmanacImage;
                        facts = "<b>COST / METER</b>   ₱2,500\n<b>MASS / METER</b>   150 kg\n<b>MAX LENGTH</b>   12 m\n<b>TENSION LIMIT</b>   999,999 N\n<b>COMPRESSION</b>   999,999 N";
                    }
                    typeof(AlmanacLearningHub).GetMethod("PopulateDetail", PrivateInstance).Invoke(hub,
                        new object[] { content == AlmanacLearningContent.Lessons ? "ENGINEERING LESSON" : "BUILDING MATERIAL", title, image, description, facts });
                    typeof(AlmanacLearningHub).GetMethod("SetDetailVisible", PrivateInstance).Invoke(hub, new object[] { true });
                    CaptureSizes(source, owner, canvasRect, camera, category.tabType + "_Detail", font, report);
                }
            }
        }
        finally
        {
            RenderTexture.active = previous;
            EditorSceneManager.ClosePreviewScene(preview);
            if (source.gameObject.scene.isDirty != originalDirty || snapshots.Any(pair => pair.Key == null ||
                    EditorJsonUtility.ToJson(pair.Key) != pair.Value))
                throw new InvalidOperationException("Source book changed during isolated preview; scene was not saved.");
        }
        report.AppendLine("ISOLATION: source book components and scene dirty state unchanged. No player saves, discoveries, purchases, or scene edits.");
    }

    private static IEnumerable<T> LoadAll<T>() where T : Object
    { return AssetDatabase.FindAssets("t:" + typeof(T).Name).Select(guid => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid))).Where(item => item != null); }

    private static Transform FindClone(Transform clone, Transform target, Transform source)
    { return target != null ? clone.Find(AnimationUtility.CalculateTransformPath(target, source)) : null; }

    private static void ShowCategory(AlmanacManager owner, AlmanacCategory selected)
    {
        foreach (AlmanacCategory category in owner.categories)
        {
            if (category.leftPageZone == null || category.rightPageZone == null) continue;
            category.leftPageZone.parent.gameObject.SetActive(category == selected);
            category.leftPageZone.gameObject.SetActive(true); category.rightPageZone.gameObject.SetActive(true);
            foreach (Image paper in new[] { category.leftPageZone.GetComponent<Image>(), category.rightPageZone.GetComponent<Image>() })
                if (paper != null) paper.enabled = true;
            Transform binding = category.leftPageZone.parent.Find("AuthoredBookBinding");
            if (binding != null) binding.gameObject.SetActive(true);
        }
    }

    private static void CaptureSizes(AlmanacManager source, AlmanacManager owner, RectTransform canvas, Camera camera,
        string label, TMP_FontAsset font, StringBuilder report)
    {
        foreach (bool phone in new[] { true, false })
        {
            Vector2 screen = phone ? new Vector2(2340, 1080) : new Vector2(2048, 1280);
            CanvasScaler scaler = source.Panel.GetComponentInParent<CanvasScaler>();
            Vector2 reference = scaler != null ? scaler.referenceResolution : new Vector2(1920, 1080);
            float match = scaler != null ? scaler.matchWidthOrHeight : .5f;
            float scale = Mathf.Pow(screen.x / reference.x, 1 - match) * Mathf.Pow(screen.y / reference.y, match);
            canvas.sizeDelta = screen / scale; camera.orthographicSize = canvas.sizeDelta.y * .005f;
            Canvas.ForceUpdateCanvases();
            foreach (ScrollRect scroll in owner.Panel.GetComponentsInChildren<ScrollRect>(false))
            { scroll.StopMovement(); scroll.verticalNormalizedPosition = 1f; }
            Canvas.ForceUpdateCanvases();
            ValidateMobileScrolling(owner, label, report);
            AlmanacPhotoMount[] mounts = owner.Panel.GetComponentsInChildren<AlmanacPhotoMount>(false);
            foreach (AlmanacPhotoMount mount in mounts)
                if (mount.raycastTarget || mount.transform.parent.GetComponent<RawImage>() != null ||
                    mount.transform.GetSiblingIndex() != mount.transform.parent.childCount - 1)
                    throw new InvalidOperationException(label + ": picture tape blocks input, follows the shared portrait, or renders behind the picture.");
            if ((label == "General" || label == "Contracts" || label.EndsWith("_Detail", StringComparison.Ordinal)) && mounts.Length == 0)
                throw new InvalidOperationException(label + ": visible picture has no book tape.");
            report.AppendLine("PICTURES: " + label + "; " + mounts.Length + " visible taped photo mounts, non-raycasting and frontmost.");
            int checkedText = 0;
            foreach (TMP_Text text in owner.Panel.GetComponentsInChildren<TMP_Text>(false))
            {
                if (string.IsNullOrWhiteSpace(text.text)) continue;
                text.ForceMeshUpdate(false, true);
                if (text.font != font) throw new InvalidOperationException(label + ": book text retained the old font: " + text.name);
                foreach (TMP_CharacterInfo character in text.textInfo.characterInfo.Take(text.textInfo.characterCount))
                    if (!char.IsWhiteSpace(character.character) && !char.IsControl(character.character) &&
                        !font.HasCharacter(character.character, true, false))
                        throw new InvalidOperationException(label + ": missing glyph " + UnicodeList(character.character.ToString()));
                if (text.isTextOverflowing || text.isTextTruncated)
                    report.AppendLine("OVERFLOW REVIEW: " + label + "/" + text.name + " at " + text.fontSize.ToString("0.#") + "pt (" + (phone ? "phone" : "tablet") + ").");
                ++checkedText;
            }
            string path = "Temp/AlmanacTypography_" + label + (phone ? "_Phone.png" : "_Tablet.png");
            var target = new RenderTexture(phone ? 1950 : 1440, 900, 24); target.Create();
            try
            {
                camera.targetTexture = target;
                if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null) camera.Render();
                else UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,
                    new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = target });
                RenderTexture.active = target;
                var texture = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
                try { texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply(); File.WriteAllBytes(path, texture.EncodeToPNG()); }
                finally { Object.DestroyImmediate(texture); }
            }
            finally { camera.targetTexture = null; RenderTexture.active = null; target.Release(); Object.DestroyImmediate(target); }
            report.AppendLine("PREVIEW: " + path + "; " + checkedText + " visible book labels checked for font/glyphs/overflow.");
        }
    }

    private static void ValidateMobileScrolling(AlmanacManager owner, string label, StringBuilder report)
    {
        foreach (ScrollRect scroll in owner.Panel.GetComponentsInChildren<ScrollRect>(false))
        {
            if (scroll.viewport == null || scroll.content == null) throw new InvalidOperationException(label + ": unbound book scroll.");
            Image swipe = scroll.viewport.GetComponent<Image>();
            if (swipe == null || !swipe.raycastTarget || !swipe.enabled)
                throw new InvalidOperationException(label + ": " + scroll.name + " has no mobile swipe target.");
            if (scroll.horizontal || !scroll.vertical)
                throw new InvalidOperationException(label + ": book reader is not a vertical scroll.");

            TMP_Text body = scroll.content.GetComponent<TMP_Text>();
            if (body == null || string.IsNullOrWhiteSpace(body.text)) continue;
            float expected = scroll.name.StartsWith("ContractBrief", StringComparison.Ordinal) ? 30f : 28f;
            if (body.enableAutoSizing || !Mathf.Approximately(body.fontSize, expected))
                throw new InvalidOperationException(label + ": scrollable body shrank below its fixed " + expected + "pt size.");
            string original = body.text;
            Vector2 position = scroll.content.anchoredPosition;
            try
            {
                body.ForceMeshUpdate(false, true);
                if (body.preferredHeight > scroll.viewport.rect.height + 1f &&
                    scroll.content.rect.height + 1f < body.preferredHeight)
                    throw new InvalidOperationException(label + ": long body was clipped instead of increasing scroll content height.");
                bool originalOverflows = scroll.content.rect.height > scroll.viewport.rect.height + 10f;
                if (!originalOverflows)
                {
                    // A short real asset may fit. Stress only the disposable clone,
                    // proving that a later long entry still scrolls at the same size.
                    for (int repeat = 0; repeat < 5 && body.preferredHeight < scroll.viewport.rect.height + 40f; repeat++)
                    { body.text += "\n\n" + original; body.ForceMeshUpdate(false, true); }
                    LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
                    Canvas.ForceUpdateCanvases();
                }
                if (scroll.content.rect.height <= scroll.viewport.rect.height + 10f)
                    throw new InvalidOperationException(label + ": long-copy scroll test did not grow past the viewport.");
                scroll.verticalNormalizedPosition = 1f;
                float top = scroll.content.anchoredPosition.y;
                scroll.verticalNormalizedPosition = 0f;
                if (Mathf.Abs(scroll.content.anchoredPosition.y - top) <= 5f)
                    throw new InvalidOperationException(label + ": long-copy reader cannot move down the page.");
                report.AppendLine("SCROLL: " + label + "/" + scroll.name + "; fixed " + expected +
                    "pt, clear swipe target, content grows and scrolls (" + (originalOverflows ? "longest real entry" : "isolated long-copy stress") + ").");
            }
            finally
            {
                body.text = original;
                LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
                Canvas.ForceUpdateCanvases();
                scroll.content.anchoredPosition = position;
                scroll.verticalNormalizedPosition = 1f;
                body.ForceMeshUpdate(false, true);
            }
        }
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero; rect.localScale = Vector3.one;
    }
}
