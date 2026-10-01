#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>One-time migration of existing authored panels; never replaces their callbacks.</summary>
[InitializeOnLoad]
public static class CozyModalAuthoring
{
    private const string FontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/Bekind Sans SDF.asset";
    private const string SpritePath = "Assets/Elements/UI/LevelResultRounded.png";
    private const string RowPath = "Assets/Script/Player/Achievements/AchivementRow_Prefab.prefab";
    private const int Version = 4;
    private static readonly string[] Scenes = {
        "Assets/Scenes/Main Menu.unity", "Assets/Scenes/Mode Selection.unity",
        "Assets/Scenes/CanyonCrossing.unity", "Assets/Scenes/BHAN HOUSE.unity", "Assets/Scenes/Multiplayer/Multiplayer.unity"
    };

    static CozyModalAuthoring()
    {
        EditorApplication.delayCall += TryMigrate;
        EditorApplication.playModeStateChanged += state => {
            if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += TryMigrate;
        };
    }
    private static void TryMigrate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        { EditorApplication.delayCall += TryMigrate; return; }
        Author(false);
    }
    [MenuItem("Tools/Civil Craft/Unify Mobile Modal Panels")]
    public static void AuthorFromMenu() { Author(true); }

    private static void Author(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        // Migration is one-time per editor session/version. Do not reload the
        // large gameplay scenes every time the player leaves Play Mode.
        if (!force && SessionState.GetInt("CivilCraft.CozyModalVersion", 0) == Version) return;
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
        if (font == null || sprite == null) { Debug.LogError("[Cozy UI] Bekind font or rounded panel sprite is missing."); return; }
        Scene active = SceneManager.GetActiveScene();
        int changed = 0;
        bool allReady = true;
        foreach (string path in Scenes)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null) continue;
            Scene scene = SceneManager.GetSceneByPath(path);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (!opened && scene.isDirty)
            { allReady = false; Debug.LogWarning($"[Cozy UI] {scene.name} has unsaved edits; save it, then run Tools/Civil Craft/Unify Mobile Modal Panels."); continue; }
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            bool edited = false;
            try
            {
                foreach (AchievementUIManager manager in InScene<AchievementUIManager>(scene))
                {
                    if (manager.achievementPanel == null) continue;
                    CozyModalLayout existing = manager.achievementPanel.GetComponent<CozyModalLayout>();
                    if (force || existing == null || existing.authoredVersion < Version)
                        manager.ApplyReferencePanelLayout();
                    edited |= Setup(manager.achievementPanel, CozyModalLayout.PanelKind.Achievements, font, sprite, force);
                }
                foreach (SettingsManager manager in InScene<SettingsManager>(scene))
                    if (manager.settingsPanel != null)
                        edited |= Setup(manager.settingsPanel, CozyModalLayout.PanelKind.Settings, font, sprite, force);
                foreach (LeaderboardPanelUI manager in InScene<LeaderboardPanelUI>(scene))
                {
                    SerializedObject data = new SerializedObject(manager);
                    GameObject panel = data.FindProperty("panel").objectReferenceValue as GameObject;
                    if (panel != null) edited |= Setup(panel, CozyModalLayout.PanelKind.Leaderboards, font, sprite, force);
                }
                foreach (ObjectiveTrackerUI tracker in InScene<ObjectiveTrackerUI>(scene))
                {
                    if (tracker.trackerPanel == null || (!force && tracker.trackerPanel.GetComponent<ObjectiveMobileFit>() != null)) continue;
                    tracker.ApplySharedVisualStyle();
                    edited = true;
                }
                if (edited) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); changed++; }
            }
            catch (Exception e) { allReady = false; Debug.LogError($"[Cozy UI] {path}: {e}"); }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }
        if (changed > 0 || force) StyleRowPrefab(font);
        if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
        if (allReady) SessionState.SetInt("CivilCraft.CozyModalVersion", Version);
        if (changed > 0) Debug.Log($"[Cozy UI] Authored consistent, safe-area-aware modals in {changed} scenes.");
    }

    private static bool Setup(GameObject panel, CozyModalLayout.PanelKind kind, TMP_FontAsset font, Sprite sprite, bool force)
    {
        CozyModalLayout layout = panel.GetComponent<CozyModalLayout>();
        if (layout != null && layout.authoredVersion >= Version && !force) return false;
        RectTransform card = CozyModalLayout.Find(panel.transform,
            kind == CozyModalLayout.PanelKind.Achievements ? "Panel" : kind == CozyModalLayout.PanelKind.Settings ? "ContainerBG" : "Leaderboard Card");
        RectTransform title = CozyModalLayout.Find(panel.transform,
            kind == CozyModalLayout.PanelKind.Achievements ? "TitleText" : kind == CozyModalLayout.PanelKind.Settings ? "Title" : "Leaderboard Title Tab");
        RectTransform close = CozyModalLayout.Find(panel.transform,
            kind == CozyModalLayout.PanelKind.Achievements ? "btn_close" : kind == CozyModalLayout.PanelKind.Settings ? "CloseButtonContainer" : "Close Leaderboards Button");
        if (card == null || title == null || close == null)
        { Debug.LogWarning($"[Cozy UI] {panel.name} is missing its authored card/header/close control.", panel); return false; }
        if (layout == null) layout = panel.AddComponent<CozyModalLayout>();
        layout.kind = kind; layout.card = card; layout.titleTab = title; layout.closeRoot = close;
        layout.font = font; layout.roundedSprite = sprite;
        layout.titleSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Elements/UI/SettingsUi/settings.png");
        layout.circleSprite = AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath("cc0921ac0d17ffe48812192bd23dd726"));
        // UtilityPanel in Canyon Crossing is only 100x100. Full-screen modals
        // must not inherit that wrapper's size (nor its visibility state).
        Canvas canvas = panel.GetComponentInParent<Canvas>(true);
        if (canvas != null && panel.transform.parent != canvas.rootCanvas.transform)
            panel.transform.SetParent(canvas.rootCanvas.transform, false);
        card.localScale = Vector3.one;
        title.SetParent(card, false); close.SetParent(card, false);
        title.SetAsLastSibling(); close.SetAsLastSibling();
        CozyModalLayout.Stretch(panel.transform as RectTransform, 0f, 0f, 0f, 0f);
        Image overlay = Ensure<Image>(panel);
        overlay.color = new Color(.08f, .05f, .035f, .52f); overlay.sprite = null; overlay.raycastTarget = true;
        Ensure<Image>(card.gameObject).raycastTarget = true;
        // Replace only decorative surfaces. The original controls remain authored and wired.
        Transform oldFill = card.Find(kind == CozyModalLayout.PanelKind.Settings ? "Image" : "Cream Surface");
        if (oldFill != null && oldFill.GetComponent<Image>() != null) oldFill.GetComponent<Image>().enabled = false;
        layout.surface = Surface(card, "Cozy Surface");
        layout.titleSurface = Surface(title, "Cozy Title Surface");
        RectTransform oldTitleFill = CozyModalLayout.Find(title, "Title Fill");
        if (oldTitleFill != null && oldTitleFill.GetComponent<Image>() != null) oldTitleFill.GetComponent<Image>().enabled = false;
        Ensure<Image>(title.gameObject).raycastTarget = false;
        foreach (Graphic graphic in close.GetComponentsInChildren<Graphic>(true))
            if (!(graphic is TMP_Text)) graphic.enabled = false;
        Button button = close.GetComponentInChildren<Button>(true);
        layout.closeButton = button;
        Ensure<Image>(close.gameObject).enabled = true;
        Outline closeOutline = close.GetComponent<Outline>();
        if (closeOutline != null) UnityEngine.Object.DestroyImmediate(closeOutline);
        RectTransform closeSurface = Surface(close, "Cozy Close Surface");
        closeSurface.GetComponent<Image>().enabled = true;
        if (button != null)
        {
            Image target = Ensure<Image>(button.gameObject);
            target.enabled = true; target.sprite = sprite; target.type = Image.Type.Sliced;
            target.color = button.transform == close ? CozyModalLayout.Ink : Color.clear;
            target.raycastTarget = true;
            button.targetGraphic = target;
            TMP_Text icon = button.GetComponentInChildren<TMP_Text>(true);
            if (icon == null)
            {
                RectTransform rect = CreateRect(button.transform, "Cozy Close Icon");
                icon = rect.gameObject.AddComponent<TextMeshProUGUI>();
            }
            icon.text = "×"; icon.font = font; icon.raycastTarget = false;
        }
        foreach (TMP_Text text in panel.GetComponentsInChildren<TMP_Text>(true))
        { text.font = font; text.fontSharedMaterial = font.material; text.color = CozyModalLayout.Ink; text.raycastTarget = false; }
        foreach (TMP_Dropdown dropdown in panel.GetComponentsInChildren<TMP_Dropdown>(true)) Ensure<Outline>(dropdown.gameObject);
        RectTransform rankingFrame = CozyModalLayout.Find(card, "Ranking List Frame");
        if (rankingFrame != null) Ensure<Outline>(rankingFrame.gameObject);
        foreach (Toggle toggle in panel.GetComponentsInChildren<Toggle>(true))
        {
            RectTransform box = CozyModalLayout.Find(toggle.transform, "Box") ?? CozyModalLayout.Find(toggle.transform, "Checkbox");
            if (box == null) continue;
            Ensure<Outline>(box.gameObject);
            toggle.transition = Selectable.Transition.None;
            Image check = toggle.graphic as Image;
            if (check != null)
            {
                check.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd");
                check.preserveAspect = true;
            }
            Ensure<LeaderboardToggleStyle>(toggle.gameObject).Configure(box.GetComponent<Image>());
        }
        layout.ApplyLayout();
        layout.authoredVersion = Version;
        EditorUtility.SetDirty(layout);
        return true;
    }
    private static RectTransform Surface(RectTransform parent, string name)
    {
        RectTransform rect = parent.Find(name) as RectTransform;
        if (rect == null) rect = CreateRect(parent, name);
        Ensure<Image>(rect.gameObject).raycastTarget = false;
        rect.SetAsFirstSibling();
        return rect;
    }
    private static RectTransform CreateRect(Transform parent, string name)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        go.layer = parent.gameObject.layer;
        if (go.scene != parent.gameObject.scene) SceneManager.MoveGameObjectToScene(go, parent.gameObject.scene);
        go.transform.SetParent(parent, false);
        return go.transform as RectTransform;
    }
    private static T Ensure<T>(GameObject go) where T : Component
    {
        T item = go.GetComponent<T>();
        if (item != null) return item;
        // Legacy decorative frames use RawImage, which cannot coexist with
        // Image. Migrate that artwork before adding a tintable/sliced surface.
        if (typeof(T) == typeof(Image))
        {
            RawImage legacy = go.GetComponent<RawImage>();
            if (legacy != null) UnityEngine.Object.DestroyImmediate(legacy);
        }
        return go.AddComponent<T>();
    }
    private static IEnumerable<T> InScene<T>(Scene scene) where T : Component
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (T component in root.GetComponentsInChildren<T>(true)) yield return component;
    }
    private static void StyleRowPrefab(TMP_FontAsset font)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(RowPath);
        try
        {
            AchievementRowUI row = root.GetComponent<AchievementRowUI>();
            if (row != null) row.ApplyReferenceLayout();
            foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
            { text.font = font; text.fontSharedMaterial = font.material; }
            PrefabUtility.SaveAsPrefabAsset(root, RowPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
#endif
