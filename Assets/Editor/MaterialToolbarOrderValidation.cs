#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[InitializeOnLoad]
internal static class MaterialToolbarOrderValidation
{
    private const string Request = "Temp/material-toolbar-order.request";
    private const string Report = "Logs/material-toolbar-order.txt";
    static MaterialToolbarOrderValidation() { EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Request)) return;
        File.Delete(Request); var report = new StringBuilder(); GameObject prefab = null;
        try
        {
            var scene = SceneManager.GetSceneByPath("Assets/Scenes/CanyonCrossing.unity");
            bool dirty = scene.isDirty;
            if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("Open CanyonCrossing in Edit Mode.");
            var dock = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<ScrollRect>(true)).Single(scroll => scroll.name == "MaterialsScrollPanel" && scroll.GetComponentsInParent<Canvas>(true).Any(canvas => canvas.name == "BuildCanvas"));
            Check(dock, "CanyonCrossing", report);
            prefab = PrefabUtility.LoadPrefabContents("Assets/Prefabs/BuildingMode/MANAGERS AND CANVASES.prefab");
            Check(prefab.GetComponentsInChildren<ScrollRect>(true).Single(scroll => scroll.name == "MaterialsScrollPanel"), "Shared build UI", report);
            if (scene.isDirty != dirty) throw new InvalidOperationException("Detached ordering checks changed the source scene's dirty state.");
            report.AppendLine("PASS: source scene, shared prefab, material restrictions, icons and bindings unchanged. No scene save required.");
        }
        catch (Exception error) { report.Insert(0, "FAIL\n"); report.AppendLine(error.ToString()); }
        finally { if (prefab != null) PrefabUtility.UnloadPrefabContents(prefab); File.WriteAllText(Report, report.ToString()); }
    }
    private static void Check(ScrollRect source, string label, StringBuilder report)
    {
        Scene preview = EditorSceneManager.NewPreviewScene();
        try
        {
            var root = new GameObject("Detached toolbar", typeof(RectTransform), typeof(Canvas)); SceneManager.MoveGameObjectToScene(root, preview);
            root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var dock = UnityEngine.Object.Instantiate(source, root.transform, false); dock.gameObject.SetActive(true);
            var buttons = dock.GetComponentsInChildren<MaterialButtonTrigger>(true);
            foreach (var button in buttons) button.enabled = false;
            var identities = buttons.ToDictionary(button => button, button => new { wrapper = button.parentWrapper, parent = button.transform.parent, material = button.buttonMaterial, normal = button.defaultSprite, selected = button.selectedOutlineSprite });
            var apply = typeof(MaterialButtonTrigger).Assembly.GetType("MaterialToolbarOrdering").GetMethod("Apply", BindingFlags.Static | BindingFlags.NonPublic);
            foreach (Vector2 resolution in new[] { new Vector2(1280, 720), new Vector2(1600, 1200) })
            {
                ((RectTransform)root.transform).sizeDelta = resolution;
                foreach (string scenario in new[] { "all available", "steel available first", "steel hidden", "steel beam exhausted", "quota restored" })
                {
                    foreach (var button in buttons)
                    {
                        bool steel = button.buttonMaterial.Id.StartsWith("Steel", StringComparison.Ordinal);
                        bool hidden = scenario == "steel hidden" && steel;
                        button.parentWrapper.SetActive(!hidden);
                        Set(button, "isMaterialHidden", hidden);
                        Set(button, "isMaterialAllowed", scenario != "steel available first" || steel || button.buttonMaterial.name == "Concrete Road");
                        Set(button, "hasReachedQuantityLimit", scenario == "steel beam exhausted" && button.buttonMaterial.Id == "SteelBeam");
                    }
                    apply.Invoke(null, new object[] { buttons, preview, false });
                    LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)root.transform); LayoutRebuilder.ForceRebuildLayoutImmediate(dock.content);
                    var ordered = buttons.OrderBy(button => button.parentWrapper.transform.GetSiblingIndex()).ToArray();
                    for (int i = 1; i < ordered.Length; i++)
                    {
                        int a = Rank(ordered[i - 1]), b = Rank(ordered[i]);
                        if (a > b) throw new InvalidOperationException("Incorrect available/category order: " + label + " " + scenario);
                    }
                    float lastX = float.NegativeInfinity;
                    foreach (var button in ordered)
                    {
                        var before = identities[button];
                        if (button.parentWrapper != before.wrapper || button.transform.parent != before.parent || button.buttonMaterial != before.material || button.defaultSprite != before.normal || button.selectedOutlineSprite != before.selected) throw new InvalidOperationException("Ordering changed a material/icon/input binding.");
                        bool hidden = scenario == "steel hidden" && button.buttonMaterial.Id.StartsWith("Steel", StringComparison.Ordinal);
                        if (button.parentWrapper.activeSelf == hidden) throw new InvalidOperationException("Sorting changed a hidden material's active state.");
                        if (hidden) continue;
                        var cell = (RectTransform)button.parentWrapper.transform;
                        float x = dock.content.InverseTransformPoint(cell.TransformPoint(cell.rect.center)).x;
                        if (x <= lastX || cell.rect.width <= 0 || cell.rect.height <= 0) throw new InvalidOperationException("Material cells did not reflow in order.");
                        lastX = x;
                    }
                    report.AppendLine($"PASS {label} {resolution.x:0}x{resolution.y:0} {scenario}: " + string.Join(" > ", ordered.Where(button => button.parentWrapper.activeSelf).Select(button => button.buttonMaterial.GetDisplayName())));
                }
            }
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
    }
    private static int Rank(MaterialButtonTrigger button)
    {
        bool available = (bool)typeof(MaterialButtonTrigger).GetProperty("IsAvailableForSelection").GetValue(button);
        var material = button.buttonMaterial;
        return (available ? 0 : 10) + (material.isRoad ? 0 : material.isPier ? 2 : material.isRope ? 3 : 1);
    }
    private static void Set(MaterialButtonTrigger button, string field, bool value) => typeof(MaterialButtonTrigger).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(button, value);
}
#endif
