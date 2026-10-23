#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Scoped catalogue authoring, not a material/contract unlock or a scene migration.</summary>
[InitializeOnLoad]
public static class ConcretePierUIAuthoring
{
    public const string RequestPath = "Temp/concrete-pier-ui.request";
    // This distinct request is intentionally invisible to earlier loaded
    // authoring code, so assignment waits for the cropped-pair alignment fix.
    public const string CroppedRequestPath = "Temp/concrete-pier-cropped-v2.request";
    public const string ReportPath = "Logs/concrete-pier-ui.txt";
    public const string WrapperName = "Concrete Pier";
    private const string PrefabPath = "Assets/Prefabs/BuildingMode/MANAGERS AND CANVASES.prefab";
    private const string MaterialGuid = "7fbcf5a7e459d434f970d2c976504d5c";
    private const string WoodGuid = "b98aa9ba752ac28449feebc76f256bf2";
    private const string LegacyDefaultGuid = "d946ea579a4ea3843924add0c8643872";
    private const string LegacySelectedGuid = "0aedf27c142367f4099b5a6b3d3fdf42";
    private const string DefaultGuid = "91e91c24e8c573e4ba73a19586500fcd";
    private const string SelectedGuid = "e859e4466a733644c8291b45c979e1ce";
    private static readonly string[] ScenePaths = { "Assets/Scenes/CanyonCrossing.unity", "Assets/Scenes/BHAN HOUSE.unity" };
    private static double nextPoll;
    private static bool running;

    private sealed class Dock
    {
        public GameObject root;
        public ScrollRect scroll;
        public MaterialButtonTrigger wood;
        public RectTransform template;
        public MaterialButtonTrigger concrete;
        public string label;
        public bool sceneUndo;
    }

    static ConcretePierUIAuthoring() { EditorApplication.update += Poll; }
    private static bool Ready => !EditorApplication.isCompiling && !EditorApplication.isUpdating &&
        !EditorApplication.isPlayingOrWillChangePlaymode && !BuildPipeline.isBuildingPlayer &&
        PrefabStageUtility.GetCurrentPrefabStage() == null;

    private static void Poll()
    {
        if (running || EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1d;
        if (!Ready) return;
        string request = File.Exists(CroppedRequestPath) ? CroppedRequestPath : RequestPath;
        if (!File.Exists(request)) return;
        string command = File.ReadAllText(request).Trim();
        File.Delete(request);
        Run(command);
    }

    [MenuItem("Tools/Civil Craft/Materials/Concrete Pier/1. Inspect Loaded Docks")]
    public static void Inspect() { Run("inspect"); }
    [MenuItem("Tools/Civil Craft/Materials/Concrete Pier/2. Add UI to Loaded Scenes and Shared Prefab")]
    public static void Apply() { Run("apply"); }
    [MenuItem("Tools/Civil Craft/Materials/Concrete Pier/3. Validate Loaded Docks")]
    public static void Validate() { Run("validate"); }
    [MenuItem("Tools/Civil Craft/Materials/Concrete Pier/4. Detached Phone and Tablet Previews")]
    public static void Preview() { Run("preview"); }
    [MenuItem("Tools/Civil Craft/Materials/Concrete Pier/5. Assign Cropped Sprite Pair (Do Not Save Scenes)")]
    public static void AssignCropped() { Run("assign-cropped"); }

    private static void Run(string command)
    {
        if (running) return;
        if (!Ready) { Write("NOT_RUN\nStop Play Mode, wait for import/compilation and close Prefab Mode. No changes made.\n"); return; }
        running = true;
        var report = new StringBuilder("Concrete Pier isolated material-catalogue UI: " + command + "\n");
        GameObject prefab = null;
        Object[] selection = Selection.objects;
        Object activeSelection = Selection.activeObject;
        Scene activeScene = SceneManager.GetActiveScene();
        try
        {
            Require(command == "inspect" || command == "apply" || command == "validate" || command == "preview" || command == "assign-cropped", "Unknown request: " + command);
            BridgeMaterialSO material = Asset<BridgeMaterialSO>(MaterialGuid);
            Sprite normal = Asset<Sprite>(DefaultGuid), selected = Asset<Sprite>(SelectedGuid);
            Require(normal.rect.width > 0 && normal.rect.height > 0 && selected.rect.width > 0 && selected.rect.height > 0,
                "Both cropped Concrete Pier sprites must have valid positive canvas sizes.");
            Require(normal.rect.size == new Vector2(515f, 596f) && selected.rect.size == new Vector2(529f, 621f),
                "The approved cropped canvas dimensions changed. Recheck their shared artwork registration before assigning.");
            string[] protectedFiles = ProtectedFiles(command == "assign-cropped");
            Dictionary<string, string> fileState = protectedFiles.ToDictionary(path => path, Hash);
            var docks = LoadedDocks(report);
            Require(!EditorUtility.IsDirty(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath)),
                "The shared MANAGERS prefab has unsaved asset edits. Save those first; nothing was overwritten.");
            prefab = PrefabUtility.LoadPrefabContents(PrefabPath);
            docks.Add(FindDock(prefab, PrefabPath, false));
            foreach (Dock dock in docks) Describe(dock, report);
            if (command == "apply") ApplyDocks(docks, material, normal, selected, fileState, report);
            else if (command == "assign-cropped") AssignCroppedDocks(docks, material, normal, selected, fileState, report);
            else if (command == "validate")
            {
                foreach (Dock dock in docks) ValidateDock(dock, material, normal, selected);
                ValidateOptIn(material, report);
                report.AppendLine("PASS: one explicit opt-in Concrete Pier entry per available dock; legacy entries untouched.");
                ReportDirty(docks, report);
            }
            else if (command == "preview")
            {
                ValidateOptIn(material, report);
                foreach (Dock dock in docks)
                {
                    ValidateDock(dock, material, normal, selected);
                    CaptureDock(dock, normal, selected, report);
                }
                report.AppendLine("PASS: detached geometry, clipping and full-cell click checks. No gameplay selection/purchase or save callback invoked.");
            }
            foreach (var pair in fileState) Require(Hash(pair.Key) == pair.Value, "Protected material/model/sprite/contract file changed: " + pair.Key);
            Require(SceneManager.GetActiveScene() == activeScene, "The active scene changed unexpectedly.");
            Write("PASS\n" + report);
            Debug.Log("[Concrete Pier UI] PASS. " + ReportPath);
        }
        catch (Exception error) { Write("FAILED\n" + report + "\n" + error); Debug.LogException(error); }
        finally
        {
            if (prefab != null) PrefabUtility.UnloadPrefabContents(prefab);
            Selection.objects = selection; Selection.activeObject = activeSelection;
            running = false;
        }
    }

    private static List<Dock> LoadedDocks(StringBuilder report)
    {
        var result = new List<Dock>();
        foreach (string path in ScenePaths)
        {
            Scene scene = SceneManager.GetSceneByPath(path);
            if (!scene.IsValid() || !scene.isLoaded)
            { report.AppendLine("SKIPPED (not loaded; never opened): " + path); continue; }
            var scrolls = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<ScrollRect>(true))
                .Where(IsMaterialDock).ToArray();
            Require(scrolls.Length == 1, "Expected exactly one authored BuildCanvas material dock in " + path);
            result.Add(FindDock(scrolls[0].gameObject, path, true));
        }
        return result;
    }

    private static bool IsMaterialDock(ScrollRect scroll)
    {
        if (scroll.name != "MaterialsScrollPanel") return false;
        return scroll.GetComponentsInParent<Canvas>(true).Any(canvas => canvas.name == "BuildCanvas");
    }

    private static Dock FindDock(GameObject root, string label, bool sceneUndo)
    {
        ScrollRect[] matches = root.GetComponentsInChildren<ScrollRect>(true).Where(IsMaterialDock).ToArray();
        Require(matches.Length == 1, "Expected one material dock: " + label);
        ScrollRect scroll = matches[0];
        Require(scroll.content != null && scroll.viewport != null, "Dock has no explicit content/viewport: " + label);
        var buttons = scroll.content.GetComponentsInChildren<MaterialButtonTrigger>(true);
        var wood = buttons.Where(button => button.buttonMaterial == Asset<BridgeMaterialSO>(WoodGuid)).ToArray();
        var concrete = buttons.Where(button => button.buttonMaterial == Asset<BridgeMaterialSO>(MaterialGuid)).ToArray();
        Require(wood.Length == 1 && concrete.Length <= 1, "Duplicate/missing Wood Pier or duplicate Concrete Pier catalogue entry: " + label);
        Transform template = wood[0].transform;
        while (template != null && template.parent != scroll.content) template = template.parent;
        Require(template is RectTransform, "Wood Pier must be in a direct material-content cell: " + label);
        Require(wood[0].buttonImage != null && wood[0].defaultSprite != null && wood[0].selectedOutlineSprite != null,
            "Wood Pier reference button is incomplete: " + label);
        if (concrete.Length == 0)
            Require(scroll.content.Find(WrapperName) == null, "An unknown custom Concrete Pier cell already exists. Inspect it manually; no overwrite attempted.");
        return new Dock { root = root, scroll = scroll, wood = wood[0], template = (RectTransform)template,
            concrete = concrete.SingleOrDefault(), label = label, sceneUndo = sceneUndo };
    }

    private static void Describe(Dock dock, StringBuilder report)
    {
        report.AppendLine(dock.label + ": existing Concrete Pier entries=" + (dock.concrete != null ? 1 : 0) +
            "; Wood Pier cell=" + dock.template.name + "; cell scale=" + dock.template.localScale +
            "; selection offset=" + dock.wood.selectedUpOffset + "; dirty=" + dock.root.scene.isDirty);
    }

    private static void AssignCroppedDocks(List<Dock> docks, BridgeMaterialSO material, Sprite normal, Sprite selected,
        Dictionary<string, string> protectedFiles, StringBuilder report)
    {
        Sprite oldNormal = Asset<Sprite>(LegacyDefaultGuid), oldSelected = Asset<Sprite>(LegacySelectedGuid);
        Require(!EditorUtility.IsDirty(material), "Concrete Pier has unsaved Inspector edits. Save that exact asset first; no dirty material was overwritten.");
        Require(material.materialIcon == oldNormal || material.materialIcon == normal,
            "Concrete Pier has a custom material icon; no unknown reference was overwritten.");
        var targets = new Dictionary<Object, Dictionary<string, string>>();
        targets.Add(material, SnapshotFields(material, "materialIcon"));
        var changing = new List<Dock>();
        foreach (Dock dock in docks)
        {
            MaterialButtonTrigger button = dock.concrete;
            Require(button != null && button.enabled && button.buttonMaterial == material && button.parentWrapper != null &&
                button.parentWrapper.name == WrapperName && button.parentWrapper.transform.parent == dock.scroll.content &&
                button.buttonImage != null && button.buttonImage.name == "Concrete Pier Icon" &&
                button.buttonImage.transform.parent == button.transform,
                "The existing Concrete Pier catalogue entry is missing or custom: " + dock.label);
            MaterialIconAspectLayout fit = button.buttonImage.GetComponent<MaterialIconAspectLayout>();
            Require(fit != null && fit.fitBox == button.GetComponent<RectTransform>() && fit.selectionMotionRoot == fit.fitBox,
                "The existing Concrete Pier fitter belongs to a different cell: " + dock.label);
            bool knownOld = button.defaultSprite == oldNormal && button.selectedOutlineSprite == oldSelected &&
                (button.buttonImage.sprite == oldNormal || button.buttonImage.sprite == oldSelected) && fit.selectedSprite == oldSelected;
            bool knownNew = button.defaultSprite == normal && button.selectedOutlineSprite == selected &&
                (button.buttonImage.sprite == normal || button.buttonImage.sprite == selected) && fit.selectedSprite == selected;
            Require(knownOld || knownNew, "Concrete Pier has mixed/custom sprite references. No partial or unknown setup was overwritten: " + dock.label);
            targets.Add(button, SnapshotFields(button, "defaultSprite", "selectedOutlineSprite"));
            targets.Add(button.buttonImage, SnapshotFields(button.buttonImage, "m_Sprite"));
            targets.Add(fit, SnapshotFields(fit, RegistrationProperties()));
            targets.Add(button.buttonImage.rectTransform, SnapshotFields(button.buttonImage.rectTransform,
                "m_LocalPosition", "m_LocalScale", "m_AnchoredPosition", "m_SizeDelta"));
            if (!knownNew || button.buttonImage.sprite != normal || !HasCroppedRegistration(fit)) changing.Add(dock);
        }
        if (changing.Count == 0 && material.materialIcon == normal)
        {
            foreach (Dock dock in docks) ValidateDock(dock, material, normal, selected);
            report.AppendLine("ALREADY_ASSIGNED: read-only reapply; no save or new Undo group.");
            ReportDirty(docks, report); return;
        }
        var originals = new Dictionary<Object, string>();
        foreach (Dock dock in docks)
        foreach (Component component in dock.root.GetComponentsInChildren<Component>(true))
            if (component != null && !targets.ContainsKey(component)) originals[component] = EditorJsonUtility.ToJson(component);
        string backup = Path.Combine(Path.GetTempPath(), "CivilCraft-ConcretePierCroppedSprites-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(backup);
        foreach (string path in new[] { PrefabPath, PrefabPath + ".meta", AssetDatabase.GetAssetPath(material), AssetDatabase.GetAssetPath(material) + ".meta" })
            File.Copy(path, Path.Combine(backup, Path.GetFileName(path)));
        foreach (Dock dock in docks.Where(item => item.sceneUndo))
            File.Copy(dock.root.scene.path, Path.Combine(backup, Path.GetFileName(dock.root.scene.path)));
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Assign cropped Concrete Pier sprite pair");
        bool assetSaved = false;
        try
        {
            if (material.materialIcon != normal)
            {
                Undo.RecordObject(material, "Assign cropped Concrete Pier material icon");
                var materialData = new SerializedObject(material);
                materialData.FindProperty("materialIcon").objectReferenceValue = normal;
                materialData.ApplyModifiedProperties();
            }
            foreach (Dock dock in changing)
            {
                MaterialButtonTrigger button = dock.concrete;
                MaterialIconAspectLayout fit = button.buttonImage.GetComponent<MaterialIconAspectLayout>();
                Undo.RecordObjects(new Object[] { button, button.buttonImage, fit, button.buttonImage.rectTransform }, "Assign cropped Concrete Pier artwork");
                button.defaultSprite = normal; button.selectedOutlineSprite = selected; button.buttonImage.sprite = normal;
                ConfigureCroppedRegistration(fit, normal, selected);
                Require(fit.RefreshNow(), "Could not fit cropped Concrete Pier in " + dock.label + ": " + fit.LastFailureReason);
                foreach (Object target in new Object[] { button, button.buttonImage, fit, button.buttonImage.rectTransform })
                {
                    EditorUtility.SetDirty(target);
                    if (PrefabUtility.IsPartOfPrefabInstance(target)) PrefabUtility.RecordPrefabInstancePropertyModifications(target);
                }
            }
            foreach (Dock dock in docks) ValidateDock(dock, material, normal, selected);
            VerifyCroppedScope(targets, originals, protectedFiles);
            foreach (Dock dock in changing.Where(item => item.sceneUndo)) EditorSceneManager.MarkSceneDirty(dock.root.scene);
            Dock prefabDock = changing.SingleOrDefault(item => !item.sceneUndo);
            if (prefabDock != null)
            {
                PrefabUtility.SaveAsPrefabAsset(prefabDock.root, PrefabPath, out bool succeeded);
                Require(succeeded, "Could not save the backed-up shared prefab."); assetSaved = true;
            }
            if (EditorUtility.IsDirty(material)) { AssetDatabase.SaveAssetIfDirty(material); assetSaved = true; }
            foreach (var pair in protectedFiles) Require(Hash(pair.Key) == pair.Value, "Protected file changed: " + pair.Key);
            RequireFields(targets[material], SnapshotFields(material, "materialIcon"), "A material field other than its icon changed.");
            Undo.CollapseUndoOperations(group);
            report.AppendLine("ASSIGNED: new cropped default/selected artwork and shared pixel registration; SO materialIcon only, existing Concrete Pier cells only.");
            report.AppendLine("Original PNGs/metas, other materials/buttons, contracts, physics, IDs, and input-cell geometry retained. No scenes opened or automatically saved.");
            report.AppendLine("Recoverable pre-assignment files: " + backup);
            ReportDirty(docks, report);
        }
        catch
        {
            Undo.RevertAllDownToGroup(group);
            report.AppendLine(assetSaved ? "Stopped after an intended asset save. Inspect the recoverable backup; the narrow Undo group was reverted in memory." :
                "Stopped before asset saving. The narrow Undo group was reverted; existing scene edits were preserved.");
            throw;
        }
    }

    private static void VerifyCroppedScope(Dictionary<Object, Dictionary<string, string>> targets,
        Dictionary<Object, string> originals, Dictionary<string, string> files)
    {
        foreach (var entry in targets)
        {
            string[] ignored = entry.Key is BridgeMaterialSO ? new[] { "materialIcon" } :
                entry.Key is MaterialButtonTrigger ? new[] { "defaultSprite", "selectedOutlineSprite" } :
                entry.Key is Image ? new[] { "m_Sprite" } :
                entry.Key is MaterialIconAspectLayout ? RegistrationProperties() :
                new[] { "m_LocalPosition", "m_LocalScale", "m_AnchoredPosition", "m_SizeDelta" };
            RequireFields(entry.Value, SnapshotFields(entry.Key, ignored), "Unrelated Concrete Pier field changed on " + entry.Key.name + ".");
        }
        foreach (var entry in originals)
            Require(entry.Key != null && EditorJsonUtility.ToJson(entry.Key) == entry.Value, "An unrelated existing dock component changed: " + entry.Key);
        foreach (var entry in files) Require(Hash(entry.Key) == entry.Value, "Protected asset/source file changed: " + entry.Key);
    }

    private static string[] RegistrationProperties() => new[]
    {
        "selectedSprite", "registrationDefaultSprite", "registeredPixelCanvasSize", "defaultPixelCenterOffset", "selectedPixelCenterOffset", "selectedRegistrationScale"
    };

    private static void ConfigureCroppedRegistration(MaterialIconAspectLayout fit, Sprite normal, Sprite selected)
    {
        // Source PNGs remain untouched. These two crops share their original
        // image's pixel coordinate system; selected's white outline needs the
        // larger shared frame. Keep a common scale instead of fitting each crop.
        fit.selectedSprite = selected; fit.registrationDefaultSprite = normal;
        fit.registeredPixelCanvasSize = new Vector2(560f, 658f);
        fit.selectedRegistrationScale = 565f / 534f;
        fit.defaultPixelCenterOffset = new Vector2(9f - 11f * fit.selectedRegistrationScale,
            6.5f - 13.5f * fit.selectedRegistrationScale);
        fit.selectedPixelCenterOffset = Vector2.zero;
    }

    private static bool HasCroppedRegistration(MaterialIconAspectLayout fit) =>
        fit.registrationDefaultSprite == Asset<Sprite>(DefaultGuid) && fit.selectedSprite == Asset<Sprite>(SelectedGuid) &&
        fit.registeredPixelCanvasSize == new Vector2(560f, 658f) && fit.selectedRegistrationScale == 565f / 534f &&
        fit.defaultPixelCenterOffset == new Vector2(9f - 11f * (565f / 534f), 6.5f - 13.5f * (565f / 534f)) &&
        fit.selectedPixelCenterOffset == Vector2.zero;

    private static void ApplyDocks(List<Dock> docks, BridgeMaterialSO material, Sprite normal, Sprite selected,
        Dictionary<string, string> protectedFiles, StringBuilder report)
    {
        var properties = new SerializedObject(material);
        SerializedProperty optIn = properties.FindProperty("requiresExplicitContractAllowance");
        Require(optIn != null && optIn.boolValue, "Configure Concrete Pier's explicit-contract opt-in flag before adding its catalogue UI.");
        Require(material.isPier && material.segmentPrefab != null && material.pierCapPrefab != null,
            "Configure the Concrete Pier bottom/top prefabs and Is Pier flag first; this command never changes material physics or models.");
        foreach (Dock dock in docks) if (dock.concrete != null) ValidateDock(dock, material, normal, selected);
        var existingState = new Dictionary<Object, string>();
        foreach (Dock dock in docks)
        foreach (Component component in dock.scroll.GetComponentsInChildren<Component>(true))
            if (component != null && !(component is Transform)) existingState.Add(component, EditorJsonUtility.ToJson(component));
        string backup = Path.Combine(Path.GetTempPath(), "CivilCraft-ConcretePierUI-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(backup);
        File.Copy(PrefabPath, Path.Combine(backup, "MANAGERS AND CANVASES.prefab"));
        File.Copy(PrefabPath + ".meta", Path.Combine(backup, "MANAGERS AND CANVASES.prefab.meta"));
        foreach (Dock dock in docks.Where(item => item.sceneUndo))
            File.Copy(dock.root.scene.path, Path.Combine(backup, Path.GetFileName(dock.root.scene.path)));
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Add Explicit Opt-In Concrete Pier UI");
        var changed = new List<Dock>();
        try
        {
            foreach (Dock dock in docks)
            {
                if (dock.concrete != null) continue;
                AddButton(dock, material, normal, selected);
                changed.Add(dock);
            }
            foreach (Dock dock in docks) ValidateDock(dock, material, normal, selected);
            foreach (var pair in existingState)
                Require(pair.Key != null && EditorJsonUtility.ToJson(pair.Key) == pair.Value,
                    "An existing material-button/dock component changed. Scene additions rolled back; prefab not saved.");
            foreach (var pair in protectedFiles) Require(Hash(pair.Key) == pair.Value, "Protected material/model/contract changed: " + pair.Key);
            foreach (Dock dock in changed.Where(item => item.sceneUndo)) EditorSceneManager.MarkSceneDirty(dock.root.scene);
            Dock prefabDock = changed.SingleOrDefault(item => !item.sceneUndo);
            if (prefabDock != null)
            {
                // This is the sole asset save: the scoped shared prefab, backed up above.
                PrefabUtility.SaveAsPrefabAsset(prefabDock.root, PrefabPath, out bool succeeded);
                Require(succeeded, "Could not save the shared material-catalogue prefab. Scene additions rolled back.");
            }
            Undo.CollapseUndoOperations(group);
            report.AppendLine("PASS: " + changed.Count + " new entries authored; reapplying valid entries is read-only.");
            report.AppendLine("Backup: " + backup + ". Backups of loaded scenes contain their previously saved state; current unsaved user edits were not saved over.");
            report.AppendLine("Only the shared MANAGERS prefab was saved. No scenes opened, closed or automatically saved; no contracts, balances, discoveries, materials or models edited.");
            ReportDirty(docks, report);
        }
        catch { Undo.RevertAllDownToGroup(group); throw; }
    }

    private static void AddButton(Dock dock, BridgeMaterialSO material, Sprite normal, Sprite selected)
    {
        GameObject wrapper = NewUI(WrapperName, dock.scroll.content, false);
        if (dock.sceneUndo) Undo.RegisterCreatedObjectUndo(wrapper, "Add Concrete Pier catalogue cell");
        RectTransform cell = (RectTransform)wrapper.transform;
        CopyRect(dock.template, cell);
        cell.SetAsLastSibling();
        LayoutElement sourceLayout = dock.template.GetComponent<LayoutElement>();
        LayoutElement layout = wrapper.AddComponent<LayoutElement>();
        layout.ignoreLayout = sourceLayout != null && sourceLayout.ignoreLayout;
        layout.minWidth = sourceLayout != null ? sourceLayout.minWidth : dock.template.rect.width;
        layout.minHeight = sourceLayout != null ? sourceLayout.minHeight : dock.template.rect.height;
        layout.preferredWidth = sourceLayout != null ? sourceLayout.preferredWidth : dock.template.rect.width;
        layout.preferredHeight = sourceLayout != null ? sourceLayout.preferredHeight : dock.template.rect.height;
        layout.flexibleWidth = sourceLayout != null ? sourceLayout.flexibleWidth : 0f;
        layout.flexibleHeight = sourceLayout != null ? sourceLayout.flexibleHeight : 0f;
        layout.layoutPriority = sourceLayout != null ? sourceLayout.layoutPriority : 1;

        GameObject input = NewUI("Concrete Pier Button", cell, true);
        RectTransform inputRect = (RectTransform)input.transform; Stretch(inputRect);
        Image surface = input.AddComponent<Image>();
        surface.sprite = null; surface.color = Color.clear; surface.type = Image.Type.Simple;
        surface.raycastTarget = true; surface.maskable = true;
        MaterialButtonTrigger trigger = input.AddComponent<MaterialButtonTrigger>();
        trigger.buttonMaterial = material; trigger.parentWrapper = wrapper;
        trigger.selectedUpOffset = dock.wood.selectedUpOffset; trigger.disabledColor = dock.wood.disabledColor;

        GameObject artwork = NewUI("Concrete Pier Icon", inputRect, true);
        RectTransform iconRect = (RectTransform)artwork.transform;
        iconRect.anchorMin = iconRect.anchorMax = iconRect.pivot = Vector2.one * .5f;
        iconRect.anchoredPosition = Vector2.zero; iconRect.localRotation = Quaternion.identity;
        Image icon = artwork.AddComponent<Image>(); icon.sprite = normal;
        icon.type = Image.Type.Simple; icon.preserveAspect = true; icon.color = Color.white;
        icon.raycastTarget = false; icon.maskable = true;
        MaterialIconAspectLayout fit = artwork.AddComponent<MaterialIconAspectLayout>();
        fit.fitBox = inputRect; fit.selectionMotionRoot = inputRect;
        fit.selectedSprite = selected; fit.selectedUpOffset = dock.wood.selectedUpOffset;
        ConfigureCroppedRegistration(fit, normal, selected);
        trigger.buttonImage = icon; trigger.defaultSprite = normal; trigger.selectedOutlineSprite = selected;
        cell.ForceUpdateRectTransforms(); inputRect.ForceUpdateRectTransforms(); iconRect.ForceUpdateRectTransforms();
        Require(fit.RefreshNow(), "Could not fit the new icon in " + dock.label + ": " + fit.LastFailureReason);

        // Create, rather than clone, the quantity badge: no persistent callbacks or cross-cell references.
        GameObject badge = NewUI("Quantity Badge", iconRect, false);
        RectTransform badgeRect = (RectTransform)badge.transform;
        badgeRect.anchorMin = badgeRect.anchorMax = new Vector2(1f, 0f);
        badgeRect.pivot = new Vector2(1f, 0f); badgeRect.anchoredPosition = Vector2.zero;
        badgeRect.sizeDelta = new Vector2(30f, 20f);
        Image badgeImage = badge.AddComponent<Image>(); badgeImage.color = new Color32(82, 53, 32, 255); badgeImage.raycastTarget = false;
        GameObject label = NewUI("Remaining Pieces", badgeRect, true); Stretch((RectTransform)label.transform);
        TextMeshProUGUI quantity = label.AddComponent<TextMeshProUGUI>();
        TMP_FontAsset font = dock.wood.quantityText != null ? dock.wood.quantityText.font : null;
        if (font == null) font = dock.scroll.GetComponentsInChildren<TMP_Text>(true).Select(text => text.font).FirstOrDefault(asset => asset != null);
        quantity.font = font != null ? font : TMP_Settings.defaultFontAsset;
        Require(quantity.font != null, "A project TMP font is required for the new quantity badge.");
        quantity.text = "0"; quantity.fontSize = 14f; quantity.enableAutoSizing = false;
        quantity.alignment = TextAlignmentOptions.Center; quantity.color = new Color32(255, 248, 229, 255);
        quantity.richText = false; quantity.raycastTarget = false; quantity.enableWordWrapping = false;
        trigger.badgeContainer = badge; trigger.quantityText = quantity;
        dock.concrete = trigger;
        // Explicit opt-in only: inactive in authored scenes/prefab. The existing
        // BuildUIController finds inactive buttons and re-evaluates each contract.
        wrapper.SetActive(false);
    }

    private static void ValidateDock(Dock dock, BridgeMaterialSO material, Sprite normal, Sprite selected)
    {
        SerializedProperty optIn = new SerializedObject(material).FindProperty("requiresExplicitContractAllowance");
        Require(optIn != null && optIn.boolValue, "Concrete Pier must retain explicit-contract opt-in.");
        Require(material.materialIcon == normal, "Concrete Pier's material introduction/Almanac icon is not the approved cropped default.");
        MaterialButtonTrigger button = dock.concrete;
        Require(button != null && button.enabled && button.buttonMaterial == material, "Concrete Pier catalogue entry is missing/disabled: " + dock.label);
        GameObject wrapper = button.parentWrapper;
        Require(wrapper != null && wrapper.name == WrapperName && wrapper.transform.parent == dock.scroll.content,
            "Unknown/custom Concrete Pier wrapper. No overwrite attempted: " + dock.label);
        Require(button.transform.parent == wrapper.transform && button.name == "Concrete Pier Button",
            "New input cell must belong only to its own wrapper.");
        Require(button.buttonImage != null && button.buttonImage.name == "Concrete Pier Icon" &&
            button.buttonImage.transform.parent == button.transform && !button.buttonImage.raycastTarget,
            "New icon must be isolated from the full-cell input surface.");
        Require(button.defaultSprite == normal && button.selectedOutlineSprite == selected &&
            button.buttonImage.sprite == normal && button.buttonImage.type == Image.Type.Simple && button.buttonImage.preserveAspect,
            "Concrete Pier sprites or aspect-preserving image mode changed.");
        Require(button.selectedUpOffset == dock.wood.selectedUpOffset, "New selection-offset setting differs from the existing dock.");
        Image surface = button.GetComponent<Image>();
        Require(surface != null && surface.raycastTarget && surface.color.a == 0f && surface.sprite == null,
            "Concrete Pier needs a transparent full-cell input surface, not a sprite-shaped touch target.");
        RectTransform cell = button.GetComponent<RectTransform>();
        Require(cell != null && cell.anchorMin == Vector2.zero && cell.anchorMax == Vector2.one && cell.sizeDelta == Vector2.zero,
            "Input surface must stretch across its whole material cell.");
        MaterialIconAspectLayout fit = button.buttonImage.GetComponent<MaterialIconAspectLayout>();
        Require(fit != null && fit.fitBox == cell && fit.selectionMotionRoot == cell &&
            fit.selectedSprite == selected && fit.selectedUpOffset == button.selectedUpOffset && HasCroppedRegistration(fit),
            "Concrete Pier's new scale-compensation/selection component is missing or references a different cell.");
        Require(button.badgeContainer != null && button.quantityText != null &&
            button.quantityText.transform.IsChildOf(button.badgeContainer.transform) &&
            button.badgeContainer.transform.IsChildOf(button.buttonImage.transform), "Quantity badge is not isolated in this icon's compensated space.");
        Require(button.GetComponentsInChildren<Button>(true).Length == 0,
            "The new entry must not inherit persistent Button.onClick callbacks from another material.");
        Require(button.buttonMaterial != dock.wood.buttonMaterial, "Concrete Pier must not replace the legacy Wood Pier reference.");
    }

    private static void CaptureDock(Dock dock, Sprite normal, Sprite selected, StringBuilder report)
    {
        var protectedState = dock.scroll.GetComponentsInChildren<Component>(true).Where(item => item != null)
            .ToDictionary(item => (Object)item, item => EditorJsonUtility.ToJson(item));
        bool dirtyBefore = dock.root.scene.isDirty;
        Scene preview = EditorSceneManager.NewPreviewScene();
        RenderTexture previous = RenderTexture.active;
        try
        {
            GameObject root = new GameObject("Concrete Pier Detached Canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            SceneManager.MoveGameObjectToScene(root, preview);
            root.SetActive(false); // No cloned custom behaviour may initialize against live singletons.
            Canvas canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            RectTransform rootRect = (RectTransform)root.transform; rootRect.localScale = Vector3.one * .01f;
            Canvas sourceCanvas = null;
            for (Transform ancestor = dock.scroll.transform; ancestor != null; ancestor = ancestor.parent)
            {
                Canvas authoredCanvas = ancestor.GetComponent<Canvas>();
                if (authoredCanvas != null) sourceCanvas = authoredCanvas;
            }
            Require(sourceCanvas != null, "No authored root Canvas for detached dock preview: " + dock.label);
            var ancestors = new List<RectTransform>();
            Transform cursor = dock.scroll.transform.parent;
            while (cursor != sourceCanvas.transform)
            {
                Require(cursor is RectTransform, "Non-UI ancestor prevents a faithful detached dock preview: " + dock.label);
                ancestors.Add((RectTransform)cursor); cursor = cursor.parent;
                Require(cursor != null, "Material dock does not descend from its root Canvas.");
            }
            Transform parent = rootRect;
            for (int i = ancestors.Count - 1; i >= 0; i--)
            {
                GameObject ancestor = NewUI(ancestors[i].name, parent, true);
                CopyRect(ancestors[i], (RectTransform)ancestor.transform); parent = ancestor.transform;
            }
            GameObject clone = Object.Instantiate(dock.scroll.gameObject, parent, false); clone.SetActive(false);
            ScrollRect scroll = clone.GetComponent<ScrollRect>();
            MaterialButtonTrigger trigger = clone.GetComponentsInChildren<MaterialButtonTrigger>(true)
                .Single(button => button.buttonMaterial == dock.concrete.buttonMaterial);
            foreach (MonoBehaviour behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour != null && behaviour.GetType().Assembly == typeof(MaterialButtonTrigger).Assembly &&
                    !(behaviour is MaterialIconAspectLayout)) behaviour.enabled = false;
            foreach (CanvasGroup group in root.GetComponentsInChildren<CanvasGroup>(true))
            { group.alpha = 1f; group.interactable = true; group.blocksRaycasts = true; }
            trigger.parentWrapper.SetActive(true); trigger.gameObject.SetActive(true);
            trigger.badgeContainer.SetActive(false);
            clone.SetActive(true); root.SetActive(true);
            var cameraObject = new GameObject("Concrete Pier Detached Camera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, preview);
            Camera camera = cameraObject.GetComponent<Camera>(); camera.scene = preview; camera.enabled = false;
            camera.orthographic = true; camera.nearClipPlane = .1f; camera.farClipPlane = 100f;
            camera.transform.position = new Vector3(0, 0, -20); camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(96, 127, 145, 255); camera.useOcclusionCulling = false;
            camera.cullingMask = 1 << 30; canvas.worldCamera = camera;
            foreach (Transform item in root.GetComponentsInChildren<Transform>(true)) item.gameObject.layer = 30;
            GameObject eventObject = new GameObject("Detached Input Fixture"); eventObject.SetActive(false);
            SceneManager.MoveGameObjectToScene(eventObject, preview);
            EventSystem eventSystem = eventObject.AddComponent<EventSystem>(); eventSystem.enabled = false;
            MaterialIconAspectLayout fitter = trigger.buttonImage.GetComponent<MaterialIconAspectLayout>();
            Directory.CreateDirectory("Temp/ConcretePierUI");
            foreach (Vector2Int screen in new[] { new Vector2Int(1280, 720), new Vector2Int(1920, 1080), new Vector2Int(1600, 1200) })
            {
                float scale = CanvasScale(sourceCanvas, screen);
                rootRect.sizeDelta = (Vector2)screen / scale; camera.orthographicSize = rootRect.sizeDelta.y * .005f;
                camera.aspect = (float)screen.x / screen.y;
                RenderTexture target = new RenderTexture(screen.x, screen.y, 24) { hideFlags = HideFlags.HideAndDontSave };
                Require(target.Create(), "Could not create the detached UI render texture.");
                camera.targetTexture = target;
                try
                {
                    float defaultPixelScale = -1f; Vector2 defaultRegisteredOrigin = Vector2.zero;
                    float defaultBodyHeight = -1f;
                    foreach (bool selectedState in new[] { false, true })
                    {
                        trigger.buttonImage.sprite = selectedState ? selected : normal;
                        Prepare(scroll, fitter);
                        string path = "Temp/ConcretePierUI/" + SafeName(dock.label) + "_" + screen.x + "x" + screen.y +
                            (selectedState ? "_Selected" : "_Default") + ".png";
                        Render(camera, target, path);
                        CheckGeometry(trigger, scroll, camera, canvas, screen);
                        CheckClick(trigger, root.GetComponent<GraphicRaycaster>(), camera, eventSystem);
                        RegisteredGeometry(trigger, canvas, selectedState, out float pixelScale, out Vector2 registeredOrigin, out float bodyHeight);
                        if (!selectedState) { defaultPixelScale = pixelScale; defaultRegisteredOrigin = registeredOrigin; defaultBodyHeight = bodyHeight; }
                        else Require(Mathf.Abs(defaultPixelScale - pixelScale) < .0001f &&
                            Vector2.Distance(defaultRegisteredOrigin, registeredOrigin) < .01f && Mathf.Abs(defaultBodyHeight - bodyHeight) < .01f,
                            "Selected/unselected artwork changes its compensated body scale or body-center registration after accounting for the intended selection lift.");
                        Sprite shown = selectedState ? selected : normal;
                        report.AppendLine("PASS: " + path + "; sprite aspect=" + (shown.rect.width / shown.rect.height).ToString("F6") +
                            ", shared canvas units/pixel=" + pixelScale.ToString("F6") + ", visible body height=" + bodyHeight.ToString("F3") +
                            ", full-cell touch surface stationary; artwork inside viewport; body registration stable.");
                    }
                }
                finally { camera.targetTexture = null; target.Release(); Object.DestroyImmediate(target); }
            }
        }
        finally
        {
            RenderTexture.active = previous;
            EditorSceneManager.ClosePreviewScene(preview);
            Require(dock.root.scene.isDirty == dirtyBefore && protectedState.All(pair => pair.Key != null && EditorJsonUtility.ToJson(pair.Key) == pair.Value),
                "Detached preview changed an original dock or its dirty state.");
        }
    }

    private static void ValidateOptIn(BridgeMaterialSO material, StringBuilder report)
    {
        // Pure predicates only. Do not replace GameManager/BuildUI/player-data
        // singletons just to make an Editor fixture look like an active job.
        var method = typeof(BridgeMaterialSO).GetMethod("CanUseInContract");
        Require(method != null, "The explicit-contract runtime guard has not compiled yet.");
        string materialBefore = EditorJsonUtility.ToJson(material);
        BridgeMaterialSO wood = Asset<BridgeMaterialSO>(WoodGuid);
        string woodBefore = EditorJsonUtility.ToJson(wood);
        ContractSO contract = ScriptableObject.CreateInstance<ContractSO>();
        contract.name = "Temporary Concrete Pier Opt-In Check"; contract.hideFlags = HideFlags.HideAndDontSave;
        Func<BridgeMaterialSO, ContractSO, bool> permits = (candidate, current) =>
            (bool)method.Invoke(candidate, new object[] { current });
        try
        {
            Require(!permits(material, null), "Concrete Pier is available without a current contract.");
            Require(!permits(material, contract) && contract.IsMaterialHidden(material), "An unrestricted/empty contract list enables Concrete Pier.");
            contract.allowedMaterials.Add(new MaterialAllowance { material = wood });
            Require(!permits(material, contract), "A different material allowance enables Concrete Pier.");
            contract.allowedMaterials.Add(new MaterialAllowance { material = material });
            Require(permits(material, contract) && !contract.IsMaterialHidden(material), "An explicit Concrete Pier allowance does not enable its catalogue entry.");
            contract.hiddenMaterials.Add(material);
            Require(!permits(material, contract) && contract.IsMaterialHidden(material), "Hidden Materials does not override explicit Concrete Pier opt-in.");
            contract.hiddenMaterials.Clear(); contract.allowedMaterials = null;
            Require(!permits(material, contract), "A missing allowance list enables Concrete Pier.");
            Require(permits(wood, null) && permits(wood, contract), "Legacy Wood Pier's additional opt-in predicate changed.");
            report.AppendLine("PASS: native pure opt-in predicates deny null/empty/different/hidden contexts, permit explicit exact allowance, and preserve Wood Pier legacy behavior; temporary SO only, no player/singleton access.");
        }
        finally
        {
            Object.DestroyImmediate(contract);
            Require(EditorJsonUtility.ToJson(material) == materialBefore && EditorJsonUtility.ToJson(wood) == woodBefore,
                "Pure predicate check changed an original material asset.");
        }
    }

    private static void Prepare(ScrollRect scroll, MaterialIconAspectLayout fitter)
    {
        for (int i = 0; i < 3; i++)
        {
            Canvas.ForceUpdateCanvases(); LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
            scroll.Rebuild(CanvasUpdate.PostLayout); scroll.horizontalNormalizedPosition = 1f;
            Require(fitter.RefreshNow(), "New icon could not compensate the actual cloned dock axes: " + fitter.LastFailureReason);
        }
        Canvas.ForceUpdateCanvases();
    }

    private static void CheckGeometry(MaterialButtonTrigger trigger, ScrollRect scroll, Camera camera, Canvas canvas, Vector2Int screen)
    {
        RectTransform rect = trigger.buttonImage.rectTransform;
        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
        Vector3 right = canvas.transform.InverseTransformVector(corners[3] - corners[0]);
        Vector3 up = canvas.transform.InverseTransformVector(corners[1] - corners[0]);
        float ratio = right.magnitude / up.magnitude;
        Require(Mathf.Abs(ratio - trigger.buttonImage.sprite.rect.width / trigger.buttonImage.sprite.rect.height) < .002f,
            "Concrete Pier is squashed by its inherited nonuniform dock scale.");
        Require(Mathf.Abs(Vector3.Dot(right.normalized, up.normalized)) < .002f, "Concrete Pier artwork is sheared.");
        Require(right.magnitude > 25f && up.magnitude > 35f, "Concrete Pier artwork fit is too small.");
        foreach (Vector3 corner in corners)
        {
            Vector2 pixel = RectTransformUtility.WorldToScreenPoint(camera, corner);
            Require(pixel.x >= -1f && pixel.y >= -1f && pixel.x <= screen.x + 1f && pixel.y <= screen.y + 1f,
                "Concrete Pier artwork leaves the phone/tablet canvas.");
            Require(RectTransformUtility.RectangleContainsScreenPoint(scroll.viewport, pixel, camera),
                "Concrete Pier artwork is clipped by the material viewport; selection=" + (trigger.buttonImage.sprite == trigger.selectedOutlineSprite));
        }
        Require(trigger.GetComponent<RectTransform>().anchoredPosition == Vector2.zero, "Selection moved the full-cell touch surface.");
    }

    private static void CheckClick(MaterialButtonTrigger trigger, GraphicRaycaster raycaster, Camera camera, EventSystem eventSystem)
    {
        RectTransform cell = trigger.GetComponent<RectTransform>();
        foreach (Vector2 fraction in new[] { new Vector2(.5f, .5f), new Vector2(.1f, .1f), new Vector2(.9f, .1f), new Vector2(.1f, .9f), new Vector2(.9f, .9f) })
        {
            Vector3 local = new Vector3(Mathf.Lerp(cell.rect.xMin, cell.rect.xMax, fraction.x), Mathf.Lerp(cell.rect.yMin, cell.rect.yMax, fraction.y));
            var pointer = new PointerEventData(eventSystem) { position = RectTransformUtility.WorldToScreenPoint(camera, cell.TransformPoint(local)) };
            var hits = new List<RaycastResult>(); raycaster.Raycast(pointer, hits);
            // The production trigger is intentionally disabled in the detached
            // fixture, so test raycast-to-handler ownership without invoking it.
            Require(trigger is IPointerClickHandler && hits.Count > 0 && hits[0].gameObject == trigger.gameObject,
                "The full Concrete Pier cell is not a click target (sample " + fraction + ").");
        }
    }

    private static void RegisteredGeometry(MaterialButtonTrigger trigger, Canvas canvas, bool selectedState,
        out float unitsPerPixel, out Vector2 registeredOrigin, out float bodyHeight)
    {
        RectTransform rect = trigger.buttonImage.rectTransform;
        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
        float width = canvas.transform.InverseTransformVector(corners[3] - corners[0]).magnitude;
        float height = canvas.transform.InverseTransformVector(corners[1] - corners[0]).magnitude;
        Rect pixels = trigger.buttonImage.sprite.rect;
        float sourcePixelScale = width / pixels.width;
        Require(Mathf.Abs(sourcePixelScale - height / pixels.height) < .0001f,
            "The cropped artwork is stretched rather than uniformly scaled.");
        var fit = trigger.buttonImage.GetComponent<MaterialIconAspectLayout>();
        unitsPerPixel = sourcePixelScale / (selectedState ? fit.selectedRegistrationScale : 1f);
        float lift = Mathf.Max(0f, fit.selectedUpOffset) * (selectedState ? .5f : -.5f);
        Vector3 center = canvas.transform.InverseTransformPoint(rect.position);
        // Opaque non-white body bounds, measured from the supplied PNGs using
        // pixel edges (the selected white outline is not part of the body).
        Vector2 bodyFromImageCenter = selectedState ? new Vector2(-11f, -13.5f) : new Vector2(-9f, -6.5f);
        registeredOrigin = new Vector2(center.x + bodyFromImageCenter.x * sourcePixelScale,
            center.y + bodyFromImageCenter.y * sourcePixelScale - lift);
        bodyHeight = (selectedState ? 534f : 565f) * sourcePixelScale;
    }

    private static float CanvasScale(Canvas canvas, Vector2Int screen)
    {
        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler == null || scaler.uiScaleMode == CanvasScaler.ScaleMode.ConstantPixelSize)
            return scaler != null ? scaler.scaleFactor : 1f;
        Require(scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize, "Detached preview supports the authored pixel/scale-with-screen-size Canvas modes only.");
        Vector2 ratios = new Vector2(screen.x / scaler.referenceResolution.x, screen.y / scaler.referenceResolution.y);
        if (scaler.screenMatchMode == CanvasScaler.ScreenMatchMode.Expand) return Mathf.Min(ratios.x, ratios.y);
        if (scaler.screenMatchMode == CanvasScaler.ScreenMatchMode.Shrink) return Mathf.Max(ratios.x, ratios.y);
        return Mathf.Pow(ratios.x, 1f - scaler.matchWidthOrHeight) * Mathf.Pow(ratios.y, scaler.matchWidthOrHeight);
    }

    private static void Render(Camera camera, RenderTexture target, string path)
    {
        Texture2D pixels = null; RenderTexture previous = RenderTexture.active;
        try
        {
            if (GraphicsSettings.currentRenderPipeline == null) camera.Render();
            else RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture.active = target;
            pixels = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
            File.WriteAllBytes(path, pixels.EncodeToPNG());
        }
        finally { RenderTexture.active = previous; if (pixels != null) Object.DestroyImmediate(pixels); }
    }

    private static GameObject NewUI(string name, Transform parent, bool active)
    {
        var result = new GameObject(name, typeof(RectTransform)); result.SetActive(false);
        SceneManager.MoveGameObjectToScene(result, parent.gameObject.scene);
        result.layer = parent.gameObject.layer; result.transform.SetParent(parent, false); result.SetActive(active);
        return result;
    }

    private static void CopyRect(RectTransform source, RectTransform target)
    {
        target.anchorMin = source.anchorMin; target.anchorMax = source.anchorMax; target.pivot = source.pivot;
        target.sizeDelta = source.sizeDelta; target.anchoredPosition3D = source.anchoredPosition3D;
        target.localRotation = source.localRotation; target.localScale = source.localScale;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.pivot = Vector2.one * .5f;
        rect.sizeDelta = Vector2.zero; rect.anchoredPosition3D = Vector3.zero;
        rect.localScale = Vector3.one; rect.localRotation = Quaternion.identity;
    }

    private static T Asset<T>(string guid) where T : Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
        Require(asset != null, "Required existing asset is missing: " + guid); return asset;
    }

    private static string[] ProtectedFiles(bool allowMaterialIconChange = false)
    {
        var files = AssetDatabase.FindAssets("t:ContractSO").Select(AssetDatabase.GUIDToAssetPath).ToList();
        foreach (string guid in new[] { MaterialGuid, WoodGuid, DefaultGuid, SelectedGuid, LegacyDefaultGuid, LegacySelectedGuid,
            "351c467da0d46fd4683a1bc12102b6ee", "3c9a1e4915844954ea26f3e0a6711ab2" })
        {
            string path = AssetDatabase.GUIDToAssetPath(guid); Require(!string.IsNullOrEmpty(path), "Protected asset is missing: " + guid);
            if (!(allowMaterialIconChange && guid == MaterialGuid)) files.Add(path);
            if (File.Exists(path + ".meta")) files.Add(path + ".meta");
        }
        return files.Distinct().ToArray();
    }

    private static Dictionary<string, string> SnapshotFields(Object value, params string[] ignored)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        SerializedProperty property = new SerializedObject(value).GetIterator(); bool enter = true;
        while (property.Next(enter))
        {
            enter = property.propertyType == SerializedPropertyType.Generic;
            if (ignored.Any(path => property.propertyPath == path || property.propertyPath.StartsWith(path + ".", StringComparison.Ordinal)))
            { enter = false; continue; }
            string text;
            switch (property.propertyType)
            {
                case SerializedPropertyType.Generic: text = property.type; break;
                case SerializedPropertyType.Integer: text = property.longValue.ToString(CultureInfo.InvariantCulture); break;
                case SerializedPropertyType.Enum:
                case SerializedPropertyType.ArraySize:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.Character: text = property.intValue.ToString(CultureInfo.InvariantCulture); break;
                case SerializedPropertyType.Boolean: text = property.boolValue.ToString(); break;
                case SerializedPropertyType.Float: text = property.doubleValue.ToString("R", CultureInfo.InvariantCulture); break;
                case SerializedPropertyType.String: text = property.stringValue; break;
                case SerializedPropertyType.ObjectReference: text = property.objectReferenceInstanceIDValue.ToString(CultureInfo.InvariantCulture); break;
                case SerializedPropertyType.Color: text = property.colorValue.ToString("R"); break;
                case SerializedPropertyType.Vector2: text = property.vector2Value.ToString("R"); break;
                case SerializedPropertyType.Vector3: text = property.vector3Value.ToString("R"); break;
                case SerializedPropertyType.Vector4: text = property.vector4Value.ToString("R"); break;
                case SerializedPropertyType.Quaternion: text = property.quaternionValue.ToString("R"); break;
                default: throw new InvalidOperationException("Unsupported exact property comparison: " + property.propertyPath + " / " + property.propertyType);
            }
            result.Add(property.propertyPath, text);
        }
        return result;
    }

    private static void RequireFields(Dictionary<string, string> before, Dictionary<string, string> after, string message)
    {
        string[] changed = before.Keys.Union(after.Keys).Where(path => !before.TryGetValue(path, out string oldValue) ||
            !after.TryGetValue(path, out string newValue) || oldValue != newValue).ToArray();
        Require(changed.Length == 0, message + " Differences: " + string.Join(", ", changed.Take(20)));
    }

    private static string Hash(string path)
    {
        using (SHA256 algorithm = SHA256.Create()) return Convert.ToBase64String(algorithm.ComputeHash(File.ReadAllBytes(path)));
    }
    private static void ReportDirty(IEnumerable<Dock> docks, StringBuilder report)
    {
        string[] dirty = docks.Where(dock => dock.sceneUndo && dock.root.scene.isDirty).Select(dock => dock.root.scene.name).ToArray();
        report.AppendLine("NEEDS_SAVE: " + (dirty.Length == 0 ? "none" : string.Join(", ", dirty)));
    }
    private static string SafeName(string name) { return Path.GetFileNameWithoutExtension(name).Replace(' ', '_'); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Write(string value) { Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, value); }
}
#endif
