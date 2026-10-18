#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Explicit migration of the existing material-overlap panel, never a runtime replacement.</summary>
public static class MaterialOverlapPanelAuthoring
{
    public const string ReportPath = "Logs/MaterialOverlapPanelAuthoring.txt";
    public const int Version = 2;
    private const string RequestPath = "Temp/material-overlap-panel-authoring.request";
    private static double nextPoll;
    private const string FontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/Bekind Sans SDF.asset";
    private const string SpritePath = "Assets/Elements/UI/LevelResultRounded.png";
    private const string ManagersPrefab = "Assets/Prefabs/BuildingMode/MANAGERS AND CANVASES.prefab";
    private static readonly string[] ScenePaths = {
        "Assets/Scenes/CanyonCrossing.unity", "Assets/Scenes/Multiplayer/Multiplayer.unity", "Assets/Scenes/BHAN HOUSE.unity"
    };
    private static readonly Color Ink = new Color32(75, 50, 36, 255);
    private static readonly Color Cream = new Color32(255, 249, 238, 255);
    private static readonly Color Sand = new Color32(239, 226, 204, 255);
    private static readonly Color Gold = new Color32(239, 166, 49, 255);

    [InitializeOnLoadMethod]
    private static void WatchRequest()
    {
        EditorApplication.update -= PollRequest;
        EditorApplication.update += PollRequest;
    }

    private static void PollRequest()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 2d;
        if (!File.Exists(RequestPath)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            Directory.CreateDirectory("Logs");
            File.WriteAllText(ReportPath, "WAIT: Leave Play Mode and finish script imports. No scene edits saved.");
            return;
        }
        File.Delete(RequestPath);
        try { AuthorAndValidateBatch(); }
        catch (Exception error) { Debug.LogException(error); }
    }

    public static void AuthorAndValidateBatch()
    {
        AuthorAll();
        MaterialOverlapPanelValidation.RunBatch();
        ClipboardBudgetValidation.Validate();
        if (File.Exists("Temp/ClipboardBudgetValidation.txt"))
            File.Copy("Temp/ClipboardBudgetValidation.txt", "Logs/MaterialOverlapClipboardBudget.txt", true);
    }

    [MenuItem("Tools/Civil Craft/Build/Author Material Overlap Confirmation")]
    public static void AuthorAll()
    {
        Check(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating,
            "Leave Play Mode and finish compilation/imports before authoring the material-overlap panel.");
        Directory.CreateDirectory("Logs");
        var report = new StringBuilder("Material-overlap confirmation: existing authored roots and controls only.\n");
        Scene active = SceneManager.GetActiveScene();
        try
        {
            foreach (string path in ScenePaths)
            {
                Check(AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null, "Target scene missing: " + path);
                Scene scene = SceneManager.GetSceneByPath(path);
                bool opened = !scene.IsValid() || !scene.isLoaded;
                if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                bool wasDirty = scene.isDirty;
                Undo.IncrementCurrentGroup();
                int undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Restyle Authored Material Overlap Confirmation");
                bool completed = false;
                try
                {
                    ClipboardManager[] managers = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<ClipboardManager>(true)).ToArray();
                    if (managers.Length == 0)
                    {
                        report.AppendLine("SKIP: " + path + " has no ClipboardManager. Legacy panels were not guessed or changed.");
                        completed = true;
                        continue;
                    }
                    foreach (ClipboardManager manager in managers)
                    {
                        MaterialOverlapPanelLayout layout = AuthorPanel(manager);
                        report.AppendLine(MaterialOverlapPanelValidation.ValidateAuthored(manager));
                        report.AppendLine("PASS: " + path + " / " + Hierarchy(layout.transform) + ": original root, buttons, toggle and title retained; explicit replacement/cancel callbacks authored.");
                    }
                    Undo.FlushUndoRecordObjects();
                    EditorSceneManager.MarkSceneDirty(scene);
                    if (wasDirty)
                        report.AppendLine("UNSAVED: " + path + " already had unsaved edits. Changes are Undoable and left unsaved for the user; existing edits were preserved.");
                    else
                        Check(EditorSceneManager.SaveScene(scene), "Could not save target scene: " + path);
                    completed = true;
                }
                catch
                {
                    Undo.RevertAllDownToGroup(undoGroup);
                    throw;
                }
                finally
                {
                    if (opened && completed && scene.IsValid() && scene.isLoaded && !scene.isDirty)
                        EditorSceneManager.CloseScene(scene, true);
                }
            }

            Check(AssetDatabase.LoadAssetAtPath<GameObject>(ManagersPrefab) != null, "Building managers prefab missing.");
            GameObject prefab = PrefabUtility.LoadPrefabContents(ManagersPrefab);
            try
            {
                ClipboardManager[] managers = prefab.GetComponentsInChildren<ClipboardManager>(true);
                Check(managers.Length > 0, "Managers prefab has no ClipboardManager; nothing substituted.");
                foreach (ClipboardManager manager in managers)
                {
                    AuthorPanel(manager);
                    report.AppendLine(MaterialOverlapPanelValidation.ValidateAuthored(manager));
                }
                PrefabUtility.SaveAsPrefabAsset(prefab, ManagersPrefab, out bool success);
                Check(success, "Could not save the authored managers prefab.");
                report.AppendLine("PASS: " + ManagersPrefab + " saved with its existing material-overlap controls wired and restyled.");
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
            report.AppendLine("No player data, gameplay materials, purchases or unrelated panel callbacks were changed.");
            File.WriteAllText(ReportPath, report.ToString());
            Debug.Log("[Material overlap] Existing authored confirmation panels are wired and styled. " + ReportPath);
        }
        catch (Exception error)
        {
            report.AppendLine("FAIL: " + error);
            File.WriteAllText(ReportPath, report.ToString());
            throw;
        }
        finally { if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active); }
    }

    /// <summary>Authors the referenced root in place. Callers decide whether their scene may be saved.</summary>
    public static MaterialOverlapPanelLayout AuthorPanel(ClipboardManager manager)
    {
        Check(manager != null, "A ClipboardManager is required.");
        var managerData = new SerializedObject(manager);
        SerializedProperty panelProperty = managerData.FindProperty("overrideConfirmPanel");
        Check(panelProperty != null, "ClipboardManager is missing its serialized overrideConfirmPanel reference.");
        GameObject panel = panelProperty.objectReferenceValue as GameObject;
        Check(panel != null && panel.transform is RectTransform, "ClipboardManager has no authored material-overlap panel. Nothing was generated in its place.");
        MaterialOverlapPanelLayout oldLayout = panel.GetComponent<MaterialOverlapPanelLayout>();
        Button confirm = oldLayout != null ? oldLayout.confirmButton : null;
        Button cancel = oldLayout != null ? oldLayout.cancelButton : null;
        if (confirm == null) confirm = ExistingButton(panel, "ConfirmOverride", "Confirm");
        if (cancel == null) cancel = ExistingButton(panel, "CancelOverride", "Cancel");
        Check(confirm != null && cancel != null && confirm != cancel, "The existing material-overlap Confirm/Cancel controls could not be identified unambiguously.");
        Toggle toggle = managerData.FindProperty("dontShowAgainToggle")?.objectReferenceValue as Toggle;
        if (toggle == null)
        {
            Toggle[] toggles = panel.GetComponentsInChildren<Toggle>(true);
            Check(toggles.Length == 1, "The existing material-overlap checkbox could not be identified unambiguously.");
            toggle = toggles[0];
        }
        Check(toggle.transform.IsChildOf(panel.transform), "Material-overlap checkbox belongs to another panel.");
        TMP_Text title = oldLayout != null ? oldLayout.titleText : null;
        if (title == null)
        {
            TMP_Text[] candidates = panel.GetComponentsInChildren<TMP_Text>(true)
                .Where(text => text.GetComponentInParent<Button>(true) == null && text.GetComponentInParent<Toggle>(true) == null &&
                    (text.name == "TITLE" || text.text.IndexOf("MATERIAL OVERLAPPING", StringComparison.OrdinalIgnoreCase) >= 0)).ToArray();
            Check(candidates.Length == 1, "The existing material-overlap title could not be identified unambiguously.");
            title = candidates[0];
        }
        TMP_Text toggleLabel = toggle.GetComponentInChildren<TMP_Text>(true);
        Check(toggleLabel != null, "The existing material-overlap checkbox has no text label.");
        Canvas originalCanvas = panel.GetComponentInParent<Canvas>(true);
        Check(originalCanvas != null && originalCanvas.rootCanvas != null, "Material-overlap panel is not inside an authored Canvas.");
        Canvas rootCanvas = originalCanvas.rootCanvas;
        Sprite rounded = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        Check(rounded != null && font != null, "Current rounded UI sprite or Bekind font is unavailable.");

        Undo.RegisterFullObjectHierarchyUndo(panel, "Restyle Authored Material Overlap Confirmation");
        Undo.RegisterCompleteObjectUndo(manager, "Wire Authored Material Overlap Confirmation");
        var outsideCallbacks = CaptureOutsideCallbacks(rootCanvas.gameObject, panel);
        if (panel.transform.parent != rootCanvas.transform) Undo.SetTransformParent(panel.transform, rootCanvas.transform, "Place Material Overlap Blocker on Root Canvas");
        panel.SetActive(false);
        Stretch(panel.transform as RectTransform);
        panel.transform.SetAsLastSibling();
        Image overlay = Ensure<Image>(panel);
        overlay.sprite = null; overlay.type = Image.Type.Simple;
        overlay.color = new Color(.08f, .05f, .035f, .58f); overlay.raycastTarget = true;
        Canvas canvas = Ensure<Canvas>(panel);
        canvas.overrideSorting = true; canvas.sortingOrder = 31000;
        GraphicRaycaster raycaster = Ensure<GraphicRaycaster>(panel);
        raycaster.enabled = true;
        CanvasGroup group = Ensure<CanvasGroup>(panel);
        group.alpha = 1f; group.interactable = true; group.blocksRaycasts = true; group.ignoreParentGroups = false;
        MaterialOverlapPanelLayout layout = Ensure<MaterialOverlapPanelLayout>(panel);

        RectTransform card = Child(panel.transform, "Material Overlap Card");
        Image frame = Ensure<Image>(card.gameObject);
        frame.sprite = rounded; frame.type = Image.Type.Sliced; frame.color = Ink; frame.raycastTarget = true;
        RectTransform surface = Child(card, "Cream Surface");
        Image paper = Ensure<Image>(surface.gameObject);
        paper.sprite = rounded; paper.type = Image.Type.Sliced; paper.color = Cream; paper.raycastTarget = false;
        surface.SetAsFirstSibling();
        RectTransform divider = Child(card, "Header Divider");
        Image line = Ensure<Image>(divider.gameObject);
        line.sprite = null; line.color = new Color32(177, 131, 76, 255); line.raycastTarget = false;

        Reparent(title.transform, card);
        Reparent(confirm.transform, card);
        Reparent(cancel.transform, card);
        Reparent(toggle.transform, card);
        TMP_Text message = Label(card, "Material Overlap Message", font);
        message.text = "Some pasted pieces use a different material on the same connection.\n\nReplace those pieces? Crossing materials are not replaced.";
        title.text = "Replace overlapping materials?";
        SetButton(confirm, "REPLACE", manager.ConfirmOverride, Gold, rounded, font);
        SetButton(cancel, "KEEP EXISTING", manager.CancelOverride, Sand, rounded, font);
        Transform oldClose = card.Find("Close Material Overlap");
        if (oldClose != null) Undo.DestroyObjectImmediate(oldClose.gameObject);
        title.transform.SetAsLastSibling();

        toggle.SetIsOnWithoutNotify(false);
        toggleLabel.text = "Don't ask again";
        toggle.transition = Selectable.Transition.ColorTint;
        toggle.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = confirm, selectOnDown = cancel };
        RectTransform background = toggle.transform.Find("Background") as RectTransform;
        Check(background != null && toggle.graphic != null, "Existing material-overlap checkbox is missing its Background or Checkmark.");
        Image box = Ensure<Image>(background.gameObject);
        box.sprite = rounded; box.type = Image.Type.Sliced; box.color = Sand; box.raycastTarget = false;
        Border(box.gameObject);
        Image check = toggle.graphic as Image;
        Check(check != null, "Existing material-overlap checkbox does not use an Image checkmark.");
        check.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd");
        check.color = Ink; check.preserveAspect = true; check.raycastTarget = false;
        Image toggleHit = Ensure<Image>(toggle.gameObject);
        toggleHit.sprite = null; toggleHit.color = Color.clear; toggleHit.raycastTarget = true;
        toggle.targetGraphic = toggleHit;

        foreach (LayoutGroup item in panel.GetComponentsInChildren<LayoutGroup>(true)) { Undo.RecordObject(item, "Fit Material Overlap Controls"); item.enabled = false; }
        foreach (ContentSizeFitter item in panel.GetComponentsInChildren<ContentSizeFitter>(true)) { Undo.RecordObject(item, "Fit Material Overlap Controls"); item.enabled = false; }
        foreach (AspectRatioFitter item in panel.GetComponentsInChildren<AspectRatioFitter>(true)) { Undo.RecordObject(item, "Fit Material Overlap Controls"); item.enabled = false; }
        foreach (TMP_Text text in panel.GetComponentsInChildren<TMP_Text>(true))
        {
            text.font = font; text.fontSharedMaterial = font.material; text.color = Ink;
            text.margin = Vector4.zero;
            text.raycastTarget = false; text.richText = false;
        }
        confirm.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = cancel, selectOnRight = cancel, selectOnUp = toggle, selectOnDown = toggle };
        cancel.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = confirm, selectOnRight = confirm, selectOnUp = toggle, selectOnDown = toggle };
        layout.card = card; layout.surface = surface; layout.divider = divider;
        layout.titleText = title; layout.messageText = message;
        layout.confirmButton = confirm; layout.cancelButton = cancel;
        layout.dontShowAgainToggle = toggle; layout.toggleLabel = toggleLabel;
        layout.toggleBackground = background; layout.toggleCheckmark = check.rectTransform;
        layout.authoredVersion = Version;
        layout.ApplyLayout();
        managerData.Update();
        managerData.FindProperty("overrideConfirmPanel").objectReferenceValue = panel;
        managerData.FindProperty("dontShowAgainToggle").objectReferenceValue = toggle;
        managerData.ApplyModifiedPropertiesWithoutUndo();
        // Native registration of an inactive nested Canvas can rewrite getters;
        // persist the intended order after TMP and all other controls are authored.
        var canvasData = new SerializedObject(canvas);
        canvasData.FindProperty("m_OverrideSorting").boolValue = true;
        canvasData.FindProperty("m_SortingOrder").intValue = 31000;
        canvasData.ApplyModifiedPropertiesWithoutUndo();
        foreach (var pair in outsideCallbacks) Check(pair.Key != null && EventJson(pair.Key) == pair.Value, "A callback outside the authored overlap panel changed: " + pair.Key.name);
        EditorUtility.SetDirty(layout); EditorUtility.SetDirty(canvas); EditorUtility.SetDirty(manager);
        Check(confirm.onClick.GetPersistentEventCount() == 1 && confirm.onClick.GetPersistentTarget(0) == manager && confirm.onClick.GetPersistentMethodName(0) == "ConfirmOverride", "Replace button is not persistently wired exactly once.");
        Check(cancel.onClick.GetPersistentEventCount() == 1 && cancel.onClick.GetPersistentTarget(0) == manager && cancel.onClick.GetPersistentMethodName(0) == "CancelOverride", "Keep Existing button is not persistently wired exactly once.");
        return layout;
    }

    private static Button ExistingButton(GameObject panel, string method, string legacyLabel)
    {
        Button[] buttons = panel.GetComponentsInChildren<Button>(true);
        Button[] matches = buttons.Where(button => Enumerable.Range(0, button.onClick.GetPersistentEventCount()).Any(index => button.onClick.GetPersistentMethodName(index) == method)).ToArray();
        if (matches.Length == 1) return matches[0];
        if (matches.Length > 1) return null;
        matches = buttons.Where(button => button.GetComponentInChildren<TMP_Text>(true)?.text.Trim() == legacyLabel).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static void SetButton(Button button, string label, UnityAction action, Color color, Sprite rounded, TMP_FontAsset font)
    {
        Image image = Ensure<Image>(button.gameObject);
        image.enabled = true; image.sprite = rounded; image.type = Image.Type.Sliced; image.color = color; image.raycastTarget = true;
        button.targetGraphic = image; button.interactable = true; button.transition = Selectable.Transition.ColorTint;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white; colors.highlightedColor = new Color(1f, .96f, .86f); colors.pressedColor = new Color(.87f, .80f, .68f);
        colors.selectedColor = colors.highlightedColor; colors.disabledColor = new Color(.6f, .6f, .6f, .6f); button.colors = colors;
        Border(button.gameObject);
        TMP_Text text = button.GetComponentInChildren<TMP_Text>(true);
        if (text == null) text = Label(button.transform, "Label", font);
        text.text = label;
        for (int index = button.onClick.GetPersistentEventCount() - 1; index >= 0; index--) UnityEventTools.RemovePersistentListener(button.onClick, index);
        UnityEventTools.AddPersistentListener(button.onClick, action);
        button.onClick.SetPersistentListenerState(0, UnityEventCallState.RuntimeOnly);
        EditorUtility.SetDirty(button);
    }

    private static void Border(GameObject target)
    {
        Outline outline = Ensure<Outline>(target);
        outline.effectColor = Ink; outline.effectDistance = new Vector2(2f, -2f); outline.useGraphicAlpha = true;
    }

    private static TMP_Text Label(Transform parent, string name, TMP_FontAsset font)
    {
        RectTransform rect = Child(parent, name);
        TMP_Text text = rect.GetComponent<TMP_Text>();
        if (text == null) text = Undo.AddComponent<TextMeshProUGUI>(rect.gameObject);
        text.font = font; text.fontSharedMaterial = font.material; text.color = Ink; text.raycastTarget = false;
        return text;
    }

    private static RectTransform Child(Transform parent, string name)
    {
        Transform existing = parent.Find(name);
        if (existing != null)
        {
            Check(existing is RectTransform, "Existing authored child is not UI: " + name);
            return existing as RectTransform;
        }
        GameObject item = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        item.layer = parent.gameObject.layer;
        SceneManager.MoveGameObjectToScene(item, parent.gameObject.scene);
        Undo.RegisterCreatedObjectUndo(item, "Author Material Overlap Decoration");
        item.transform.SetParent(parent, false);
        return item.transform as RectTransform;
    }

    private static void Reparent(Transform item, Transform parent)
    {
        if (item.parent != parent) Undo.SetTransformParent(item, parent, "Arrange Existing Material Overlap Control");
        item.localScale = Vector3.one; item.localRotation = Quaternion.identity;
    }

    private static T Ensure<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        if (component != null) return component;
        if (typeof(T) == typeof(Image))
        {
            RawImage raw = target.GetComponent<RawImage>();
            if (raw != null) Undo.DestroyObjectImmediate(raw);
        }
        return Undo.AddComponent<T>(target);
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(.5f, .5f); rect.offsetMin = rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one; rect.localRotation = Quaternion.identity;
    }

    private static Dictionary<Component, string> CaptureOutsideCallbacks(GameObject canvas, GameObject panel)
    {
        var result = new Dictionary<Component, string>();
        foreach (Component component in canvas.GetComponentsInChildren<Component>(true))
            if (component != null && !component.transform.IsChildOf(panel.transform) &&
                (component is Button || component is Toggle || component is Slider || component is TMP_Dropdown)) result[component] = EventJson(component);
        return result;
    }

    private static string EventJson(Component component)
    {
        if (component is Button button) return JsonUtility.ToJson(button.onClick);
        if (component is Toggle toggle) return JsonUtility.ToJson(toggle.onValueChanged);
        if (component is Slider slider) return JsonUtility.ToJson(slider.onValueChanged);
        if (component is TMP_Dropdown dropdown) return JsonUtility.ToJson(dropdown.onValueChanged);
        throw new InvalidOperationException("Unexpected event owner.");
    }

    private static string Hierarchy(Transform item)
    {
        string path = item.name;
        while (item.parent != null) { item = item.parent; path = item.name + "/" + path; }
        return path;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
