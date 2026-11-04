#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

internal static class SteelMaterialToolbarAuthoring
{
    private const string ScenePath = "Assets/Scenes/CanyonCrossing.unity";
    private const string PrefabPath = "Assets/Prefabs/BuildingMode/MANAGERS AND CANVASES.prefab";
    private const string MaterialPath = "Assets/BridgeBuilder/Data/Resources/";
    private const string ArtworkPath = "Assets/Elements/Map3/Materials/";
    private const string RequestPath = "Temp/steel-material-toolbar.request";
    private const string ReportPath = "Logs/steel-material-toolbar.txt";
    private const string Action = "Add Ironhaven steel materials to build toolbar";

    private sealed class Entry
    {
        public readonly BridgeMaterialSO Material;
        public readonly Sprite Normal;
        public readonly Sprite Selected;
        public readonly Vector2 Frame;
        public readonly Vector2 NormalOffset;
        public readonly float SelectedScale;

        public Entry(string name, string id, string artwork, Vector2 frame, Vector2 normalOffset, float selectedScale = 1.0383f)
        {
            Material = Load<BridgeMaterialSO>(MaterialPath + id + ".asset");
            Normal = Load<Sprite>(ArtworkPath + artwork + " not selected.png");
            Selected = Load<Sprite>(ArtworkPath + artwork + " selected.png");
            Frame = frame;
            NormalOffset = normalOffset;
            SelectedScale = selectedScale;
            Require(Material.Id == id && Material.materialIcon == Normal && Material.requiresExplicitContractAllowance,
                name + " must use the authored ID, normal icon and explicit contract allowance.");
        }
    }

    [InitializeOnLoadMethod]
    private static void WatchRequest() => EditorApplication.update += CheckRequest;

    private static bool Ready => !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling &&
        !EditorApplication.isUpdating && !BuildPipeline.isBuildingPlayer && PrefabStageUtility.GetCurrentPrefabStage() == null;

    private static void CheckRequest()
    {
        if (!Ready || !File.Exists(RequestPath)) return;
        string command = File.ReadAllText(RequestPath).Trim();
        File.Delete(RequestPath);
        Execute(command);
    }

    [MenuItem("Tools/Civil Craft/Materials/Steel/Author CanyonCrossing and Shared Toolbar")]
    private static void Apply() => Execute("apply");

    [MenuItem("Tools/Civil Craft/Materials/Steel/Validate Toolbars (Read Only)")]
    private static void Validate() => Execute("validate");

    private static void Execute(string command)
    {
        Directory.CreateDirectory("Logs");
        GameObject prefab = null;
        try
        {
            Require(Ready, "Stop Play Mode and close Prefab Mode first.");
            Require(command == "apply" || command == "validate", "Unknown steel toolbar command: " + command);
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            Require(scene.isLoaded, "Open CanyonCrossing in Edit Mode first.");
            bool sceneWasDirty = scene.isDirty;
            var entries = new[]
            {
                new Entry("Steel Beam", "SteelBeam", "steel beam", new Vector2(834f, 842f), new Vector2(-.22f, 3.14f)),
                new Entry("Steel Rope", "SteelRope", "steel cable", new Vector2(770f, 782f), new Vector2(6.40f, 12.77f)),
                new Entry("Steel Pier", "SteelPier", "steel pier", new Vector2(390f, 678f), new Vector2(-1.5f, 4f), 1.017f)
            };
            var sceneDock = FindDock(scene.GetRootGameObjects());
            Require(!EditorUtility.IsDirty(Load<GameObject>(PrefabPath)), "The shared prefab has unsaved edits. Save that asset before authoring.");
            prefab = PrefabUtility.LoadPrefabContents(PrefabPath);
            var prefabDock = FindDock(new[] { prefab });

            string outcome;
            if (command == "apply") outcome = Configure(scene, sceneWasDirty, prefab, sceneDock, prefabDock, entries);
            else
            {
                ValidateDock(sceneDock, entries);
                ValidateDock(prefabDock, entries);
                ValidateLayouts(sceneDock, entries);
                ValidateLayouts(prefabDock, entries);
                outcome = "Read-only validation. CanyonCrossing dirty: " + scene.isDirty + ".";
            }

            string report = "PASS: CanyonCrossing and shared toolbar each have one inactive Steel Beam, Steel Rope and Steel Pier cell. " +
                "Phone/tablet layout checks passed for registration, hidden layout width, viewport and tap targets. " + outcome;
            File.WriteAllText(ReportPath, report);
            Debug.Log(report);
        }
        catch (Exception error)
        {
            File.WriteAllText(ReportPath, "FAIL: " + error);
            Debug.LogException(error);
        }
        finally
        {
            if (prefab != null) PrefabUtility.UnloadPrefabContents(prefab);
        }
    }

    private static ScrollRect FindDock(IEnumerable<GameObject> roots) => roots
        .SelectMany(root => root.GetComponentsInChildren<ScrollRect>(true))
        .Single(scroll => scroll.name == "MaterialsScrollPanel" &&
            scroll.GetComponentsInParent<Canvas>(true).Any(canvas => canvas.name == "BuildCanvas"));

    private static string Configure(Scene scene, bool sceneWasDirty, GameObject prefab, ScrollRect sceneDock,
        ScrollRect prefabDock, Entry[] entries)
    {
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(Action);
        try
        {
            bool changedScene = AddMissingEntries(sceneDock, entries, true);
            bool changedPrefab = AddMissingEntries(prefabDock, entries, false);
            ValidateDock(sceneDock, entries);
            ValidateDock(prefabDock, entries);
            ValidateLayouts(sceneDock, entries);
            ValidateLayouts(prefabDock, entries);

            if (changedScene || changedPrefab)
            {
                string backup = Path.Combine(Path.GetTempPath(), "CivilCraft-SteelToolbar-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(backup);
                if (changedScene) File.Copy(ScenePath, Path.Combine(backup, Path.GetFileName(ScenePath)));
                if (changedPrefab) File.Copy(PrefabPath, Path.Combine(backup, Path.GetFileName(PrefabPath)));
                Debug.Log("Steel toolbar pre-save backup at " + backup);
            }
            if (changedPrefab)
            {
                PrefabUtility.SaveAsPrefabAsset(prefab, PrefabPath, out bool savedPrefab);
                Require(savedPrefab, "Could not save the shared steel toolbar prefab.");
            }
            if (changedScene) EditorSceneManager.MarkSceneDirty(scene);
            Undo.CollapseUndoOperations(group);
            if (changedScene && !sceneWasDirty && EditorSceneManager.SaveScene(scene))
                return "CanyonCrossing saved.";
            return scene.isDirty ? "NEEDS_SAVE: CanyonCrossing has unsaved edits and was left open without saving." : "CanyonCrossing already saved.";
        }
        catch
        {
            Undo.RevertAllDownToGroup(group);
            throw;
        }
    }

    private static bool AddMissingEntries(ScrollRect dock, Entry[] entries, bool undo)
    {
        var template = dock.content.GetComponentsInChildren<MaterialButtonTrigger>(true)
            .Single(button => button.buttonMaterial != null && button.buttonMaterial.Id == "Concrete Pier");
        Require(template.parentWrapper != null && template.parentWrapper.transform.parent == dock.content &&
            !template.parentWrapper.activeSelf && template.parentWrapper.GetComponentsInChildren<Button>(true).Length == 0,
            "Concrete Pier is not the expected inactive toolbar template.");
        bool changed = false;
        foreach (Entry entry in entries)
        {
            if (FindButton(dock, entry) != null) continue;
            string name = entry.Material.GetDisplayName();
            Require(dock.content.Find(name) == null, "An unknown cell already uses the name " + name + ".");
            GameObject cell = Object.Instantiate(template.parentWrapper, dock.content, false);
            try
            {
                cell.name = name;
                var button = cell.GetComponentsInChildren<MaterialButtonTrigger>(true).Single();
                button.name = name + " Button";
                button.buttonMaterial = entry.Material;
                button.defaultSprite = entry.Normal;
                button.selectedOutlineSprite = entry.Selected;
                button.buttonImage.name = name + " Icon";
                button.buttonImage.sprite = entry.Normal;
                var fit = button.buttonImage.GetComponent<MaterialIconAspectLayout>();
                fit.registrationDefaultSprite = entry.Normal;
                fit.selectedSprite = entry.Selected;
                fit.registeredPixelCanvasSize = entry.Frame;
                fit.defaultPixelCenterOffset = entry.NormalOffset;
                fit.selectedPixelCenterOffset = Vector2.zero;
                fit.selectedRegistrationScale = entry.SelectedScale;
                Require(fit.RefreshNow(), name + " icon could not fit the authored toolbar. " + fit.LastFailureReason);
                if (undo) Undo.RegisterCreatedObjectUndo(cell, Action);
                changed = true;
            }
            catch
            {
                Object.DestroyImmediate(cell);
                throw;
            }
        }
        return changed;
    }

    private static MaterialButtonTrigger FindButton(ScrollRect dock, Entry entry) => dock.content
        .GetComponentsInChildren<MaterialButtonTrigger>(true).SingleOrDefault(button => button.buttonMaterial == entry.Material);

    private static void ValidateDock(ScrollRect dock, Entry[] entries)
    {
        foreach (Entry entry in entries)
        {
            var button = FindButton(dock, entry);
            Require(button != null && button.enabled, "Missing " + entry.Material.name + " toolbar button.");
            var cell = button.parentWrapper;
            var input = (RectTransform)button.transform;
            var surface = button.GetComponent<Image>();
            var fit = button.buttonImage.GetComponent<MaterialIconAspectLayout>();
            Require(cell != null && !cell.activeSelf && cell.transform.parent == dock.content && input.parent == cell.transform &&
                button.defaultSprite == entry.Normal && button.selectedOutlineSprite == entry.Selected &&
                button.buttonImage.sprite == entry.Normal && button.buttonImage.preserveAspect && !button.buttonImage.raycastTarget &&
                button.buttonImage.transform.parent == input && surface != null && surface.raycastTarget && surface.color.a == 0f &&
                input.anchorMin == Vector2.zero && input.anchorMax == Vector2.one && input.sizeDelta == Vector2.zero &&
                fit != null && fit.fitBox == input && fit.selectionMotionRoot == input && fit.registrationDefaultSprite == entry.Normal &&
                fit.selectedSprite == entry.Selected && fit.registeredPixelCanvasSize == entry.Frame &&
                fit.defaultPixelCenterOffset == entry.NormalOffset && fit.selectedPixelCenterOffset == Vector2.zero &&
                Mathf.Approximately(fit.selectedRegistrationScale, entry.SelectedScale) &&
                button.badgeContainer != null && button.quantityText != null &&
                button.badgeContainer.transform.IsChildOf(input) && button.quantityText.transform.IsChildOf(button.badgeContainer.transform),
                "Invalid art, hit area or quantity-badge binding for " + entry.Material.name + ".");
        }
    }

    private static void ValidateLayouts(ScrollRect source, Entry[] entries)
    {
        Scene preview = EditorSceneManager.NewPreviewScene();
        try
        {
            Canvas sourceCanvas = source.GetComponentsInParent<Canvas>(true).Last();
            var root = new GameObject("Steel Toolbar Preview", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            SceneManager.MoveGameObjectToScene(root, preview);
            root.SetActive(false);
            var rootRect = (RectTransform)root.transform;
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            rootRect.localScale = Vector3.one * .01f;
            var ancestors = new Stack<RectTransform>();
            for (Transform parent = source.transform.parent; parent != sourceCanvas.transform; parent = parent.parent)
                ancestors.Push((RectTransform)parent);
            RectTransform cloneParent = rootRect;
            foreach (RectTransform original in ancestors)
            {
                var clone = new GameObject(original.name, typeof(RectTransform));
                SceneManager.MoveGameObjectToScene(clone, preview);
                var rect = (RectTransform)clone.transform;
                rect.SetParent(cloneParent, false);
                rect.anchorMin = original.anchorMin;
                rect.anchorMax = original.anchorMax;
                rect.pivot = original.pivot;
                rect.sizeDelta = original.sizeDelta;
                rect.anchoredPosition3D = original.anchoredPosition3D;
                rect.localRotation = original.localRotation;
                rect.localScale = original.localScale;
                cloneParent = rect;
            }
            var dock = Object.Instantiate(source.gameObject, cloneParent, false).GetComponent<ScrollRect>();
            foreach (MonoBehaviour behaviour in dock.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour != null && behaviour.GetType().Assembly == typeof(MaterialButtonTrigger).Assembly &&
                    !(behaviour is MaterialIconAspectLayout)) behaviour.enabled = false;
            var buttons = entries.Select(entry => FindButton(dock, entry)).ToArray();
            dock.gameObject.SetActive(true);
            root.SetActive(true);

            var cameraObject = new GameObject("Detached Camera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, preview);
            var camera = cameraObject.GetComponent<Camera>();
            camera.scene = preview;
            camera.enabled = false;
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(96, 127, 145, 255);
            camera.transform.position = new Vector3(0f, 0f, -20f);
            camera.useOcclusionCulling = false;
            camera.cullingMask = 1 << 30;
            foreach (Transform item in root.GetComponentsInChildren<Transform>(true)) item.gameObject.layer = 30;
            canvas.worldCamera = camera;
            var events = new GameObject("Detached Input");
            events.SetActive(false);
            SceneManager.MoveGameObjectToScene(events, preview);
            var eventSystem = events.AddComponent<EventSystem>();
            eventSystem.enabled = false;

            foreach (Vector2Int resolution in new[] { new Vector2Int(1280, 720), new Vector2Int(1600, 1200) })
            {
                rootRect.sizeDelta = (Vector2)resolution / CanvasScale(sourceCanvas, resolution);
                camera.orthographicSize = rootRect.sizeDelta.y * .005f;
                camera.aspect = (float)resolution.x / resolution.y;
                var texture = new RenderTexture(resolution.x, resolution.y, 24);
                camera.targetTexture = texture;
                try
                {
                    Require(texture.Create(), "Could not create the detached toolbar render target.");
                    foreach (MaterialButtonTrigger button in buttons) button.parentWrapper.SetActive(false);
                    Rebuild(dock);
                    float hiddenWidth = LayoutUtility.GetPreferredWidth(dock.content);
                    foreach (MaterialButtonTrigger button in buttons) button.parentWrapper.SetActive(true);
                    Rebuild(dock);
                    float addedWidth = LayoutUtility.GetPreferredWidth(dock.content) - hiddenWidth;
                    Require(addedWidth > 1f, "Inactive steel cells must use less toolbar width than the visible cells.");
                    foreach (MaterialButtonTrigger button in buttons)
                        ValidateButtonLayout(button, dock, canvas, camera, root.GetComponent<GraphicRaycaster>(), eventSystem);
                }
                finally
                {
                    camera.targetTexture = null;
                    texture.Release();
                    Object.DestroyImmediate(texture);
                }
            }
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
    }

    private static void ValidateButtonLayout(MaterialButtonTrigger button, ScrollRect dock, Canvas canvas, Camera camera,
        GraphicRaycaster raycaster, EventSystem eventSystem)
    {
        var fit = button.buttonImage.GetComponent<MaterialIconAspectLayout>();
        var input = (RectTransform)button.transform;
        var corners = new Vector3[4];
        float referenceScale = 0f;
        foreach (Sprite sprite in new[] { button.defaultSprite, button.selectedOutlineSprite })
        {
            button.buttonImage.sprite = sprite;
            Rebuild(dock);
            Require(fit.RefreshNow(), button.name + " could not fit the detached canvas. " + fit.LastFailureReason);
            CenterInViewport(dock, input);
            Canvas.ForceUpdateCanvases();
            if (GraphicsSettings.currentRenderPipeline == null) camera.Render();
            else RenderPipeline.SubmitRenderRequest(camera,
                new UniversalRenderPipeline.SingleCameraRequest { destination = camera.targetTexture });
            button.buttonImage.rectTransform.GetWorldCorners(corners);
            float width = canvas.transform.InverseTransformVector(corners[3] - corners[0]).magnitude;
            float height = canvas.transform.InverseTransformVector(corners[1] - corners[0]).magnitude;
            Require(width > 25f && height > 25f && Mathf.Abs(width / height - sprite.rect.width / sprite.rect.height) < .002f,
                button.name + " is too small or stretched.");
            float pixelsToCanvas = width / sprite.rect.width;
            if (sprite == button.defaultSprite) referenceScale = pixelsToCanvas;
            else Require(Mathf.Abs(referenceScale - pixelsToCanvas / fit.selectedRegistrationScale) < .0001f,
                button.name + " changes registration scale when selected.");
            foreach (Vector3 corner in corners)
                Require(RectTransformUtility.RectangleContainsScreenPoint(dock.viewport, RectTransformUtility.WorldToScreenPoint(camera, corner), camera),
                    button.name + " is clipped by the scrolled viewport.");
            foreach (Vector2 fraction in new[] { new Vector2(.5f, .5f), new Vector2(.1f, .1f), new Vector2(.9f, .1f), new Vector2(.1f, .9f), new Vector2(.9f, .9f) })
            {
                Vector2 point = input.rect.min + Vector2.Scale(input.rect.size, fraction);
                var pointer = new PointerEventData(eventSystem)
                { position = RectTransformUtility.WorldToScreenPoint(camera, input.TransformPoint(point)) };
                var hits = new List<RaycastResult>();
                raycaster.Raycast(pointer, hits);
                Require(hits.Count > 0 && hits[0].gameObject == button.gameObject, button.name + " lost part of its full-cell tap target.");
            }
        }
    }

    private static void CenterInViewport(ScrollRect dock, RectTransform cell)
    {
        RectTransform viewport = dock.viewport;
        float contentWidth = viewport.InverseTransformVector(dock.content.TransformVector(Vector3.right * dock.content.rect.width)).x;
        float scrollWidth = contentWidth - viewport.rect.width;
        if (scrollWidth <= 0f) return;
        float cellCenter = viewport.InverseTransformPoint(cell.TransformPoint(cell.rect.center)).x;
        dock.horizontalNormalizedPosition = Mathf.Clamp01(dock.horizontalNormalizedPosition +
            (cellCenter - viewport.rect.center.x) / scrollWidth);
    }

    private static void Rebuild(ScrollRect dock)
    {
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(dock.content);
        dock.Rebuild(CanvasUpdate.PostLayout);
        dock.horizontalNormalizedPosition = 1f;
        Canvas.ForceUpdateCanvases();
    }

    private static float CanvasScale(Canvas canvas, Vector2Int resolution)
    {
        var scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler == null) return 1f;
        if (scaler.uiScaleMode == CanvasScaler.ScaleMode.ConstantPixelSize) return scaler.scaleFactor;
        Require(scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize, "Unexpected authored canvas scale mode.");
        var ratios = new Vector2(resolution.x / scaler.referenceResolution.x, resolution.y / scaler.referenceResolution.y);
        if (scaler.screenMatchMode == CanvasScaler.ScreenMatchMode.Expand) return Mathf.Min(ratios.x, ratios.y);
        if (scaler.screenMatchMode == CanvasScaler.ScreenMatchMode.Shrink) return Mathf.Max(ratios.x, ratios.y);
        return Mathf.Pow(ratios.x, 1f - scaler.matchWidthOrHeight) * Mathf.Pow(ratios.y, scaler.matchWidthOrHeight);
    }

    private static T Load<T>(string path) where T : Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        Require(asset != null, "Missing imported toolbar asset at " + path);
        return asset;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
