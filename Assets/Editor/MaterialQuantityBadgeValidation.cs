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
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class MaterialQuantityBadgeValidation
{
    private const string TemplatePath = "Assets/Resources/UI/MaterialQuantityBadge.prefab";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string Output = "Logs/MaterialQuantityBadges";
    private static readonly MethodInfo Recount = typeof(BuildUIController).GetMethod("RecalculateStaticBridge", Private,
        null, new[] { typeof(bool), typeof(BuildLocation), typeof(IEnumerable<Bar>) }, null);

    [MenuItem("Tools/Civil Craft/Materials/Validate Quantity Badges")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(TemplatePath) == null) AuthorTemplate();
        Directory.CreateDirectory(Output);
        var report = new StringBuilder();
        GameManager previousGame = GameManager.Instance;
        BuildUIController previousUI = BuildUIController.Instance;
        Scene preview = EditorSceneManager.NewPreviewScene();
        ContractSO contract = ScriptableObject.CreateInstance<ContractSO>();
        try
        {
            var root = new GameObject("Detached quantity checks");
            SceneManager.MoveGameObjectToScene(root, preview);
            var game = root.AddComponent<GameManager>();
            var ui = root.AddComponent<BuildUIController>();
            SetStatic(typeof(GameManager), game);
            SetStatic(typeof(BuildUIController), ui);
            SetProperty(game, "CurrentContract", contract);
            ui.maxBudget = 10000000;
            var siteGo = new GameObject("Quantity test site"); siteGo.transform.SetParent(root.transform, false);
            BuildLocation site = siteGo.AddComponent<BuildLocation>();
            site.activeContract = contract;
            SetProperty(game, "ActiveBuildLocation", site);
            var canvasGo = new GameObject("Quantity preview", typeof(RectTransform), typeof(Canvas));
            canvasGo.transform.SetParent(root.transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            ((RectTransform)canvasGo.transform).sizeDelta = new Vector2(1280, 720);
            canvasGo.transform.localScale = Vector3.one * .01f;
            var cameraGo = new GameObject("Quantity camera", typeof(Camera), typeof(UniversalAdditionalCameraData));
            cameraGo.transform.SetParent(root.transform, false);
            Camera camera = cameraGo.GetComponent<Camera>();
            camera.enabled = false; camera.scene = preview; camera.orthographic = true; camera.orthographicSize = 3.8f;
            camera.transform.position = new Vector3(0, 0, -20);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(49, 57, 74, 255);
            foreach (string path in new[] { "Assets/Scenes/CanyonCrossing.unity", "Assets/Scenes/BHAN HOUSE.unity" })
            {
                Scene source = SceneManager.GetSceneByPath(path);
                bool opened = !source.IsValid() || !source.isLoaded;
                if (opened) source = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                try
                {
                    var buttons = source.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<MaterialButtonTrigger>(true))
                        .Where(button => button.buttonMaterial != null).ToArray();
                    CheckSource(buttons, canvasGo.transform, camera, ui, site, contract, Path.GetFileNameWithoutExtension(path), report);
                }
                finally { if (opened) EditorSceneManager.CloseScene(source, true); }
            }
            var shared = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/BuildingMode/MANAGERS AND CANVASES.prefab");
            CheckSource(shared.GetComponentsInChildren<MaterialButtonTrigger>(true).Where(button => button.buttonMaterial != null).ToArray(),
                canvasGo.transform, camera, ui, site, contract, "Shared build UI", report);
            report.AppendLine("PASS: temporary scenes and contracts only; no player data, source scenes, allowances or material properties saved.");
            File.WriteAllText(Output + "/Validation.txt", report.ToString());
            Debug.Log("[Material Quantity Badges] PASS\n" + report);
        }
        catch (Exception error) { File.WriteAllText(Output + "/Validation.txt", "FAIL: " + error + "\n" + report); throw; }
        finally
        {
            EditorSceneManager.ClosePreviewScene(preview);
            SetStatic(typeof(GameManager), previousGame);
            SetStatic(typeof(BuildUIController), previousUI);
            Object.DestroyImmediate(contract);
        }
    }

    private static void CheckSource(MaterialButtonTrigger[] source, Transform canvas, Camera camera, BuildUIController ui,
        BuildLocation site, ContractSO contract, string label, StringBuilder report)
    {
        contract.allowedMaterials = source.Select(button => button.buttonMaterial).Distinct()
            .Select(material => new MaterialAllowance { material = material, maxPieces = 3 }).ToList();
        var copies = new List<GameObject>();
        for (int index = 0; index < source.Length; index++)
        {
            MaterialButtonTrigger original = source[index];
            var cell = new GameObject("Quantity cell", typeof(RectTransform)); cell.transform.SetParent(canvas, false); copies.Add(cell);
            RectTransform cellRect = (RectTransform)cell.transform;
            cellRect.anchoredPosition = new Vector2((index % 5 - 2) * 230, 150 - index / 5 * 240);
            cellRect.sizeDelta = new Vector2(200, 180);
            GameObject clone = Object.Instantiate(original.gameObject, cell.transform, false);
            MaterialButtonTrigger button = clone.GetComponent<MaterialButtonTrigger>();
            button.enabled = false; button.parentWrapper = cell; clone.SetActive(true);
            Vector3 axes = CanvasScale(original.transform);
            RectTransform input = (RectTransform)clone.transform;
            input.anchorMin = input.anchorMax = input.pivot = Vector2.one * .5f;
            input.anchoredPosition = Vector2.zero;
            input.localScale = axes;
            input.sizeDelta = new Vector2(140 / axes.x, 110 / axes.y);
            foreach (MaterialIconAspectLayout fit in clone.GetComponentsInChildren<MaterialIconAspectLayout>(true))
            { fit.fitBox = input; fit.selectionMotionRoot = input; fit.RefreshNow(); }
            var bars = new List<Bar>();
            void Refresh()
            {
                Recount.Invoke(ui, new object[] { false, site, bars });
                button.EvaluateMaterialRestriction();
                button.RefreshQuantityBadgeLayout();
            }
            void Expect(int remaining)
            {
                Refresh();
                Require(button.quantityText.text == remaining.ToString(), label + " " + original.buttonMaterial.Id + " incorrect remaining count.");
            }
            contract.allowedMaterials.Find(entry => entry.material == button.buttonMaterial).maxPieces = 15;
            Expect(15);
            Bar first = CreateBar(site, button.buttonMaterial, 20000 + index * 100, bars);
            Expect(14);
            contract.allowedMaterials.Find(entry => entry.material == button.buttonMaterial).maxPieces = 3;
            Expect(2);
            var build = new HistoryAction { isBuildEvent = true }; build.affectedObjects.Add(first.gameObject);
            build.Undo(); Expect(3);
            build.Redo(); Expect(2);
            var cut = new HistoryAction { isBuildEvent = false }; cut.affectedObjects.Add(first.gameObject);
            first.gameObject.SetActive(false); Expect(3); cut.Undo(); Expect(2); cut.Redo(); Expect(3); cut.Undo();
            var paste = new HistoryAction { isBuildEvent = true };
            for (int i = 0; i < 2; i++) paste.affectedObjects.Add(CreateBar(site, button.buttonMaterial, 20010 + index * 100 + i * 10, bars).gameObject);
            Expect(0); Require(!button.IsAvailableForSelection, "Exhausted material stayed selectable.");
            paste.Undo(); Expect(2); paste.Redo(); Expect(0);
            var nested = new GameObject("Nested model Bar").AddComponent<Bar>();
            nested.transform.SetParent(first.transform, false); nested.materialData = button.buttonMaterial; bars.Add(nested);
            Bar ghost = CreateBar(site, button.buttonMaterial, 20050 + index * 100, bars); ghost.MarkAsConstructionPreview();
            Expect(0);
            Require(ui.GetMaterialUsageCount(button.buttonMaterial) == 3, "Paired/model/preview bars were counted as extra pieces.");
            CheckPasteLimits(ui, site, contract, button.buttonMaterial, first, bars);
            paste.Undo(); Expect(2);
            button.quantityText.ForceMeshUpdate();
            Vector3 badgeScale = CanvasScale(button.badgeContainer.transform);
            Vector2 badgeSize = ((RectTransform)button.badgeContainer.transform).rect.size;
            Require(Mathf.Abs(badgeScale.x * badgeSize.x - 34) < .1f && Mathf.Abs(badgeScale.y * badgeSize.y - 24) < .1f &&
                button.quantityText.transform.localScale == Vector3.one && !button.quantityText.isTextOverflowing,
                "Badge is stretched or text clips: " + label + " " + button.buttonMaterial.Id);
            var template = AssetDatabase.LoadAssetAtPath<GameObject>(TemplatePath);
            var badgeImage = button.badgeContainer.GetComponent<RawImage>();
            Require(badgeImage != null && badgeImage.texture == template.GetComponent<RawImage>().texture &&
                badgeImage.color == Color.white && ((RectTransform)button.badgeContainer.transform).anchorMax == Vector2.one &&
                button.quantityText.font == template.GetComponentInChildren<TMP_Text>(true).font &&
                button.quantityText.color == template.GetComponentInChildren<TMP_Text>(true).color,
                "Material badge does not match the shared authored design.");
            contract.allowedMaterials.Find(entry => entry.material == button.buttonMaterial).maxPieces = 0;
            Refresh(); Require(!button.badgeContainer.activeSelf, "Unlimited material shows a count badge.");
            contract.allowedMaterials.Find(entry => entry.material == button.buttonMaterial).maxPieces = 3;
            Expect(2);
            var titleGo = new GameObject("Material name", typeof(RectTransform), typeof(TextMeshProUGUI));
            titleGo.transform.SetParent(cell.transform, false);
            var title = titleGo.GetComponent<TextMeshProUGUI>(); title.font = button.quantityText.font; title.fontSize = 16;
            title.text = button.buttonMaterial.GetDisplayName(); title.alignment = TextAlignmentOptions.Center;
            title.rectTransform.sizeDelta = new Vector2(220, 40); title.rectTransform.anchoredPosition = new Vector2(0, -70);
            report.AppendLine("PASS " + label + " " + button.buttonMaterial.Id + ": 15→14; 3→2→3 Undo→2 Redo; cut/restore; paste 2→0→2; nested/preview excluded; unlimited hidden; shared 34×24 badge design.");
            foreach (Bar bar in bars) if (bar != null && bar.transform.parent == site.transform) Object.DestroyImmediate(bar.gameObject);
            foreach (Point point in site.GetComponentsInChildren<Point>(true)) Object.DestroyImmediate(point.gameObject);
        }
        Capture(camera, 1920, 1080, Output + "/" + label + "-1920.png");
        Capture(camera, 1440, 1080, Output + "/" + label + "-1440.png");
        foreach (GameObject copy in copies) Object.DestroyImmediate(copy);
    }

    [MenuItem("Tools/Civil Craft/Materials/Author Shared Quantity Badge")]
    public static void AuthorTemplate()
    {
        Require(AssetDatabase.LoadAssetAtPath<GameObject>(TemplatePath) == null, "The authored badge already exists; edit that prefab directly.");
        Scene preview = EditorSceneManager.NewPreviewScene();
        try
        {
            var root = new GameObject("MaterialQuantityBadge", typeof(RectTransform), typeof(RawImage));
            root.SetActive(false); SceneManager.MoveGameObjectToScene(root, preview);
            ((RectTransform)root.transform).sizeDelta = new Vector2(34, 24);
            RawImage image = root.GetComponent<RawImage>();
            image.texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Elements/UI/bm_ui/Rounded Corners - Coco Code package/Rounded Corners - Coco Code package/Rounded40px@2x.png");
            Require(image.texture != null, "Existing white rounded badge artwork is missing.");
            image.color = Color.white; image.raycastTarget = false;
            var label = new GameObject("Remaining Pieces", typeof(RectTransform), typeof(TextMeshProUGUI));
            label.transform.SetParent(root.transform, false);
            var text = label.GetComponent<TextMeshProUGUI>();
            text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/Bekind Sans SDF.asset");
            Require(text.font != null, "The usual Bekind Sans font is missing.");
            text.text = "15"; text.fontSize = text.fontSizeMax = 14; text.fontSizeMin = 9; text.enableAutoSizing = true;
            text.enableWordWrapping = false; text.alignment = TextAlignmentOptions.Center;
            text.color = new Color32(50, 45, 41, 255); text.raycastTarget = false; text.richText = false;
            text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(2, 1); text.rectTransform.offsetMax = new Vector2(-2, -1);
            Directory.CreateDirectory(Path.GetDirectoryName(TemplatePath));
            Require(PrefabUtility.SaveAsPrefabAsset(root, TemplatePath) != null, "Could not author the shared badge prefab.");
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
    }

    private static void CheckPasteLimits(BuildUIController ui, BuildLocation site, ContractSO contract, BridgeMaterialSO material,
        Bar existing, List<Bar> bars)
    {
        GameObject clipboardGo = new GameObject("Detached clipboard"); clipboardGo.transform.SetParent(site.transform, false);
        var clipboard = clipboardGo.AddComponent<ClipboardManager>();
        Set(clipboard, "copiedRelativePoints", new List<Vector3> { Vector3.zero, new Vector3(5, 0, 0) });
        Set(clipboard, "copiedBars", new List<CopiedBarInfo> { new CopiedBarInfo { startIdx = 0, endIdx = 1, mat = material } });
        Set(clipboard, "pasteRootPos", new Vector3(30000, 10, 0));
        MethodInfo check = typeof(ClipboardManager).GetMethod("CanAffordPaste", Private);
        Require(!(bool)check.Invoke(clipboard, null), "Paste bypasses the exhausted quantity limit.");
        Set(clipboard, "pasteRootPos", existing.startPoint.transform.position);
        Require((bool)check.Invoke(clipboard, null), "Reusing an existing same-material connection consumes another piece.");
        Object.DestroyImmediate(clipboardGo);
    }

    private static Bar CreateBar(BuildLocation site, BridgeMaterialSO material, float x, List<Bar> bars)
    {
        Point PointAt(float px)
        {
            var go = new GameObject("Count point"); go.transform.SetParent(site.transform, false); go.transform.position = new Vector3(px, 10, 0);
            Point point = go.AddComponent<Point>(); point.AssignOwner(site); return point;
        }
        var obj = new GameObject("Count member"); obj.transform.SetParent(site.transform, false);
        Bar bar = obj.AddComponent<Bar>(); bar.materialData = material; bar.AssignOwner(site);
        bar.startPoint = PointAt(x); bar.endPoint = PointAt(x + 5); bar.currentLength = 5;
        bar.startPoint.ConnectedBars.Add(bar); bar.endPoint.ConnectedBars.Add(bar); bars.Add(bar); return bar;
    }

    private static Vector3 CanvasScale(Transform target)
    {
        Transform canvas = null;
        for (Transform cursor = target; cursor != null; cursor = cursor.parent) if (cursor.GetComponent<Canvas>() != null) canvas = cursor;
        Vector3 scale = Vector3.one;
        for (Transform cursor = target; cursor != null && cursor != canvas; cursor = cursor.parent) scale = Vector3.Scale(scale, cursor.localScale);
        return new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
    }
    private static void Set(object value, string name, object data) => value.GetType().GetField(name, Private).SetValue(value, data);
    private static void SetProperty(object value, string name, object data) => value.GetType().GetProperty(name).SetValue(value, data);
    private static void SetStatic(Type type, object value) => type.GetProperty("Instance").SetValue(null, value);
    private static void Require(bool passed, string message) { if (!passed) throw new InvalidOperationException(message); }
    private static void Capture(Camera camera, int width, int height, string path)
    {
        RenderTexture previous = RenderTexture.active;
        var target = new RenderTexture(width, height, 24); target.Create();
        var image = new Texture2D(width, height, TextureFormat.RGBA32, false);
        try
        {
            Canvas.ForceUpdateCanvases(); camera.targetTexture = target;
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply(); File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally { camera.targetTexture = null; RenderTexture.active = previous; target.Release(); Object.DestroyImmediate(image); Object.DestroyImmediate(target); }
    }
}
#endif
