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
using Object = UnityEngine.Object;

/// <summary>Isolated book typography previews. Never saves scenes or font assets.</summary>
[InitializeOnLoad]
public static class AlmanacTypographyAuthoring
{
    private const string Request = "Temp/AlmanacTypography.request";
    private const string Report = "Temp/AlmanacTypographyValidation.txt";
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

    [MenuItem("Tools/Civil Craft/Almanac/Check Book Font and Readability")]
    public static void Run()
    {
        if (running || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
            EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer ||
            PrefabStageUtility.GetCurrentPrefabStage() != null) return;
        running = true;
        var report = new StringBuilder();
        try
        {
            TMP_FontAsset font = Resources.Load<TMP_FontAsset>(AlmanacTypography.FontResource);
            if (font == null) throw new InvalidOperationException("The Almanac font asset is missing.");
            AlmanacManager source = Resources.FindObjectsOfTypeAll<AlmanacManager>()
                .FirstOrDefault(book => book != null && book.gameObject.scene.IsValid() &&
                    book.gameObject.scene.isLoaded && !EditorSceneManager.IsPreviewScene(book.gameObject.scene) &&
                    book.Panel != null && book.categories.Count > 0);
            if (source != null) CaptureBook(source, font, report);
            else report.AppendLine("WAIT: Open a scene containing an Almanac and run this menu again for mobile previews.");
            File.WriteAllText(Report, (source != null ? "PASS: " : "WAIT: ") +
                "Bekind Sans typography preview.\n" + report);
            Debug.Log("[Almanac typography] " + report);
        }
        catch (Exception error)
        {
            File.WriteAllText(Report, "FAIL: " + error + "\n" + report);
            Debug.LogException(error);
        }
        finally { running = false; }
    }

    private static string UnicodeList(string value)
    { return string.Join(" ", value.Distinct().Select(character => "U+" + ((int)character).ToString("X4"))); }

    private static void CaptureBook(AlmanacManager source, TMP_FontAsset font, StringBuilder report)
    {
        if (source.categories[0].leftPageZone == null) throw new InvalidOperationException("Loaded book has no first page zone.");
        Transform sourcePanel = source.categories[0].leftPageZone.parent.parent.parent;
        Canvas.ForceUpdateCanvases();
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
            typeof(AlmanacManager).GetMethod("InitializeBook", PrivateInstance).Invoke(owner, null);
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
                bool hasPhoto = category.tabType == AlmanacTabType.General || category.tabType == AlmanacTabType.Contracts;
                CaptureSizes(source, owner, canvasRect, camera, category.tabType.ToString(), font, report, hasPhoto);
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
                    CaptureSizes(source, owner, canvasRect, camera, category.tabType + "_Detail", font, report, image != null);
                }
            }
        }
        finally
        {
            RenderTexture.active = previous;
            EditorSceneManager.ClosePreviewScene(preview);
            string[] changed = snapshots.Where(pair => pair.Key == null || EditorJsonUtility.ToJson(pair.Key) != pair.Value)
                .Select(pair => pair.Key != null ? pair.Key.name + "/" + pair.Key.GetType().Name : "Missing component").ToArray();
            if (source.gameObject.scene.isDirty != originalDirty || changed.Length != 0)
                throw new InvalidOperationException("Source book changed during isolated preview: " + string.Join(", ", changed) + ". Scene was not saved.");
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
        string label, TMP_FontAsset font, StringBuilder report, bool hasPhoto)
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
            if (hasPhoto && mounts.Length == 0)
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
                        !font.HasCharacter(character.character, true, false) &&
                        (TMP_Settings.defaultFontAsset == null || !TMP_Settings.defaultFontAsset.HasCharacter(character.character, true, false)))
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
