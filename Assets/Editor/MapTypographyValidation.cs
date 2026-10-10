using System;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Opt-in map/book font isolation regression. Disposable UI only; no scene or save writes.</summary>
[InitializeOnLoad]
public static class MapTypographyValidation
{
    private const string Request = "Temp/MapTypographyValidation.request";
    private const string Report = "Temp/MapTypographyValidation.txt";
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static double nextCheck;

    static MapTypographyValidation() { EditorApplication.update += Check; }

    private static void Check()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 1;
        if (!File.Exists(Request) || EditorApplication.isPlayingOrWillChangePlaymode ||
            EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer ||
            PrefabStageUtility.GetCurrentPrefabStage() != null) return;
        File.Delete(Request);
        Validate();
    }

    [MenuItem("Tools/Civil Craft/Almanac/Check Map and Book Font Isolation")]
    public static void Validate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
            EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer) return;
        Scene preview = EditorSceneManager.NewPreviewScene();
        TMP_FontAsset originalDefault = TMP_Settings.defaultFontAsset;
        try
        {
            TMP_FontAsset mapFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/Bekind Sans SDF");
            TMP_FontAsset bookFont = Resources.Load<TMP_FontAsset>(AlmanacTypography.FontResource);
            Require(mapFont != null && bookFont != null && mapFont != bookFont, "Both distinct font assets must exist.");
            var root = new GameObject("MapFontIsolationPreview", typeof(RectTransform), typeof(Canvas));
            SceneManager.MoveGameObjectToScene(root, preview);
            root.GetComponent<RectTransform>().sizeDelta = new Vector2(1920, 1080);
            var panel = NewRect("MinimapPanel", root.transform);
            panel.sizeDelta = new Vector2(500, 300);
            var image = NewRect("MapImage", panel).gameObject.AddComponent<RawImage>();
            image.rectTransform.anchorMin = Vector2.zero; image.rectTransform.anchorMax = Vector2.one;
            image.rectTransform.sizeDelta = Vector2.zero;
            var oldMapLabel = NewRect("ExistingCompactLabel", panel).gameObject.AddComponent<TextMeshProUGUI>();
            oldMapLabel.text = "Existing compact map";
            oldMapLabel.font = bookFont; oldMapLabel.fontSharedMaterial = bookFont.material;
            oldMapLabel.fontSize = 23f; oldMapLabel.color = Color.magenta;
            oldMapLabel.enableAutoSizing = false; oldMapLabel.raycastTarget = false;
            var bookLabel = NewRect("AlmanacOnlyLabel", root.transform).gameObject.AddComponent<TextMeshProUGUI>();
            bookLabel.text = "Engineer field notes";
            AlmanacTypography.ApplyFont(bookLabel);
            Require(bookLabel.font == bookFont, "Almanac must still use Internet Friends.");
            var unrelated = NewRect("UnrelatedMenuLabel", root.transform).gameObject.AddComponent<TextMeshProUGUI>();
            unrelated.text = "Other menu"; unrelated.font = originalDefault;
            Material otherMaterial = unrelated.fontSharedMaterial;

            var cameraObject = new GameObject("FontIsolationMapCamera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, preview);
            var camera = cameraObject.GetComponent<Camera>(); camera.orthographic = true;
            var controller = cameraObject.AddComponent<ExpandedMinimapController>();
            Set(controller, "minimapCamera", camera); Set(controller, "minimapPanel", panel); Set(controller, "mapImage", image);
            // Exercise stale cached handwriting and existing wrong-atlas labels.
            Set(controller, "uiFont", bookFont);
            // Do not register a disposable panel in the live UI reservation system.
            Set(controller, "reservedMinimapRegion", panel);
            Invoke(controller, "ResolveReferencesAndBuildUI");
            AssertMapFonts(panel, mapFont);
            Require(oldMapLabel.fontSize == 23f && oldMapLabel.color == Color.magenta &&
                !oldMapLabel.enableAutoSizing && !oldMapLabel.raycastTarget, "Font repair changed compact label design/input.");

            object[] player = { "ValidationRemotePlayer", "Other builder", Color.cyan, null, null };
            typeof(ExpandedMinimapController).GetMethod("CreatePlayerMapMarker", Private).Invoke(controller, player);
            var locationObject = new GameObject("ValidationContractLocation");
            SceneManager.MoveGameObjectToScene(locationObject, preview);
            var location = locationObject.AddComponent<BuildLocation>();
            typeof(ExpandedMinimapController).GetMethod("CreateLocationMarker", Private)
                .Invoke(controller, new object[] { location });
            AssertMapFonts(panel, mapFont);

            // The compact and expanded views share this same map hierarchy.
            Set(controller, "isExpanded", true);
            Invoke(controller, "ResolveReferencesAndBuildUI");
            AssertMapFonts(panel, mapFont);
            Require(bookLabel.font == bookFont && bookLabel.fontSharedMaterial == bookFont.material,
                "Map repair changed the Almanac font or atlas.");
            Require(unrelated.font == originalDefault && unrelated.fontSharedMaterial == otherMaterial &&
                TMP_Settings.defaultFontAsset == originalDefault, "Map repair changed another menu or the TMP default.");
            int count = panel.GetComponentsInChildren<TMP_Text>(true).Length;
            File.WriteAllText(Report, "PASS: " + count + " compact/expanded map labels, controls, contract markers and remote-player labels use Bekind Sans SDF with its matching material. Cached Internet Friends and pre-existing wrong font repaired; label sizing/colors/input retained; Almanac stays Internet Friends; other menus and TMP default unchanged. Disposable preview scene only, no scene or player save writes.\n");
            Debug.Log("[Map typography] PASS: compact/expanded font isolation and matching atlas.");
            Set(controller, "reservedMinimapRegion", null);
        }
        catch (Exception error) { File.WriteAllText(Report, "FAIL: " + error); Debug.LogException(error); }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
    }

    private static void AssertMapFonts(Transform panel, TMP_FontAsset expected)
    {
        TMP_Text[] texts = panel.GetComponentsInChildren<TMP_Text>(true);
        Require(texts.Length >= 9, "Map controls and player labels were not created.");
        foreach (TMP_Text text in texts)
            Require(text.font == expected && text.fontSharedMaterial == expected.material,
                text.name + " retained handwriting or an incorrect glyph atlas.");
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        return obj.GetComponent<RectTransform>();
    }

    private static void Set(object target, string name, object value)
    { target.GetType().GetField(name, Private).SetValue(target, value); }

    private static void Invoke(object target, string name)
    { target.GetType().GetMethod(name, Private).Invoke(target, null); }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
