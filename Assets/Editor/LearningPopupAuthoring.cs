#if UNITY_EDITOR
using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Authors the shared reference layout, preserving lesson/reward callbacks.</summary>
[InitializeOnLoad]
public static class LearningPopupAuthoring
{
    private const string SessionKey = "CivilCraft.LearningPopupLayout.v2";
    private const string RewardPrefab = "Assets/Prefabs/UI/CollectRewardSystem.prefab";
    private const string Output = "Temp/LearningPopupPreviews-v3";
    private static readonly string[] Scenes = { "Assets/Scenes/CanyonCrossing.unity",
        "Assets/Scenes/BHAN HOUSE.unity", "Assets/Scenes/Multiplayer/Multiplayer.unity" };
    private static TMP_FontAsset font;
    private static Sprite rounded, header, circle;

    static LearningPopupAuthoring()
    {
        EditorApplication.delayCall += TryAuthor;
        EditorApplication.delayCall += TryCapture;
        EditorApplication.playModeStateChanged += state => {
            if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += TryAuthor;
        };
    }

    private static void TryAuthor()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SessionState.GetBool(SessionKey, false)) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        { EditorApplication.delayCall += TryAuthor; return; }
        Author();
    }

    private static void TryCapture()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        { EditorApplication.delayCall += TryCapture; return; }
        if (!File.Exists(Output + "/Material-phone.png")) CapturePreviews();
    }

    [MenuItem("Tools/Civil Craft/Author Learning Popup Layouts")]
    public static void Author()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/Bekind Sans SDF.asset");
        rounded = SpriteAt("5c9d591e2ae349944bb4b30824b742f3");
        header = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Elements/UI/SettingsUi/settings.png");
        circle = SpriteAt("cc0921ac0d17ffe48812192bd23dd726");
        if (font == null || rounded == null || header == null || circle == null)
        { Debug.LogError("[Learning UI] Existing font/frame artwork is missing."); return; }

        Scene active = SceneManager.GetActiveScene();
        bool ready = true;
        GameObject prefab = PrefabUtility.LoadPrefabContents(RewardPrefab);
        try
        {
            ItemUnlockUI manager = prefab.GetComponent<ItemUnlockUI>();
            bool changed = false;
            if (manager != null && manager.materialPopup == null)
            {
                RectTransform popup = Rect(prefab.transform, "Material Introduction Popup");
                manager.materialPopup = BuildReader(popup.gameObject);
                popup.gameObject.SetActive(false);
                EditorUtility.SetDirty(manager);
                changed = true;
            }
            if (manager != null && manager.materialPopup != null && manager.materialPopup.card.rect.width < 800f)
            {
                // A full-stretch prefab root has no canvas dimensions in Prefab
                // isolation. Seed its authored rectangles at the reference size,
                // then restore the original root so Collect's design is untouched.
                RectTransform rootRect = prefab.transform as RectTransform;
                Vector2 originalSize = rootRect.sizeDelta;
                try
                {
                    rootRect.sizeDelta = new Vector2(1920f, 1080f);
                    BridgeMaterialSO example = AssetDatabase.LoadAssetAtPath<BridgeMaterialSO>("Assets/BridgeBuilder/Data/Resources/ConcreteBeam.asset");
                    manager.materialPopup.SetContent("Material", example != null ? example.PopupImage : null,
                        "Discover a material to learn its strengths, uses, and limits.");
                    changed = true;
                }
                finally { rootRect.sizeDelta = originalSize; }
            }
            if (changed) PrefabUtility.SaveAsPrefabAsset(prefab, RewardPrefab);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }

        foreach (string path in Scenes)
        {
            Scene scene = SceneManager.GetSceneByPath(path);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (!opened && scene.isDirty)
            {
                ready = false;
                Debug.LogWarning("[Learning UI] Save " + scene.name + ", then run Tools/Civil Craft/Author Learning Popup Layouts.");
                continue;
            }
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                bool changed = false;
                foreach (GameObject root in scene.GetRootGameObjects())
                    foreach (LessonUIManager manager in root.GetComponentsInChildren<LessonUIManager>(true))
                    {
                        if (manager.Panel == null || manager.Panel.GetComponent<LearningPopupLayout>() != null) continue;
                        SerializedObject data = new SerializedObject(manager);
                        TMP_Text title = data.FindProperty("lessonTitleText").objectReferenceValue as TMP_Text;
                        Image image = data.FindProperty("lessonImage").objectReferenceValue as Image;
                        TMP_Text body = data.FindProperty("lessonDescriptionText").objectReferenceValue as TMP_Text;
                        ScrollRect scroll = data.FindProperty("descriptionScrollRect").objectReferenceValue as ScrollRect;
                        Button close = data.FindProperty("closeButton").objectReferenceValue as Button;
                        if (title == null || image == null || body == null || scroll == null || close == null)
                        { ready = false; Debug.LogWarning("[Learning UI] Incomplete lesson references in " + scene.name); continue; }
                        Canvas canvas = manager.Panel.GetComponentInParent<Canvas>(true);
                        if (canvas != null)
                        {
                            manager.Panel.transform.SetParent(canvas.rootCanvas.transform, false);
                            CanvasScaler scaler = canvas.rootCanvas.GetComponent<CanvasScaler>();
                            if (scaler != null)
                            {
                                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                                scaler.referenceResolution = new Vector2(1920f, 1080f);
                                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                                scaler.matchWidthOrHeight = .5f;
                            }
                        }
                        BuildReader(manager.Panel, title, image, body, scroll, close);
                        manager.Panel.SetActive(false);
                        changed = true;
                    }
                if (changed) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); }
            }
            catch (Exception exception) { ready = false; Debug.LogException(exception); }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }
        if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
        if (ready) SessionState.SetBool(SessionKey, true);
        Debug.Log("[Learning UI] Authored centered lesson/material readers using the existing artwork and Bekind font. Cosmetic Collect rewards are unchanged.");
        if (ready && !File.Exists(Output + "/Lesson-phone.png")) CapturePreviews();
    }

    private static LearningPopupLayout BuildReader(GameObject root, TMP_Text title = null, Image image = null,
        TMP_Text body = null, ScrollRect scroll = null, Button close = null)
    {
        CozyModalLayout.Stretch(root.transform as RectTransform, 0f, 0f, 0f, 0f);
        Image overlay = Ensure<Image>(root);
        overlay.sprite = null; overlay.material = null;
        overlay.color = new Color(.12f, .075f, .04f, .38f); overlay.raycastTarget = true;
        LearningPopupLayout layout = Ensure<LearningPopupLayout>(root);
        layout.card = Panel(root.transform, "Learning Card", CozyModalLayout.Ink);
        layout.cardSurface = Panel(layout.card, "Cream Surface", CozyModalLayout.Cream);
        layout.cardSurface.SetAsFirstSibling();
        layout.titleTab = Rect(layout.card, "Title Tab");
        Image tab = Ensure<Image>(layout.titleTab.gameObject);
        tab.sprite = header; tab.type = Image.Type.Simple; tab.color = Color.white; tab.raycastTarget = false;
        if (title == null) title = Text(layout.titleTab, "Title", "Material", true);
        else title.transform.SetParent(layout.titleTab, false);
        StyleText(title, true);
        layout.titleText = title;
        layout.imageFrame = Panel(layout.card, "Picture Frame", new Color32(213, 155, 68, 255));
        layout.imageSurface = Panel(layout.imageFrame, "Picture Surface", CozyModalLayout.TitleCream);
        if (image == null) image = Ensure<Image>(Rect(layout.imageFrame, "Picture").gameObject);
        else image.transform.SetParent(layout.imageFrame, false);
        image.material = null; image.color = Color.white;
        image.type = Image.Type.Simple; image.preserveAspect = true; image.raycastTarget = false;
        layout.contentImage = image;
        layout.descriptionFrame = Panel(layout.card, "Description Frame", CozyModalLayout.Ink);
        layout.descriptionSurface = Panel(layout.descriptionFrame, "Description Surface", CozyModalLayout.TitleCream);

        if (scroll == null)
        {
            scroll = Ensure<ScrollRect>(Rect(layout.descriptionFrame, "Description Scroll").gameObject);
            scroll.viewport = Rect(scroll.transform, "Viewport");
            scroll.content = Rect(scroll.viewport, "Content");
        }
        else scroll.transform.SetParent(layout.descriptionFrame, false);
        Image scrollHit = Ensure<Image>(scroll.gameObject);
        scrollHit.color = Color.clear; scrollHit.sprite = null; scrollHit.material = null; scrollHit.raycastTarget = true;
        Image viewportHit = Ensure<Image>(scroll.viewport.gameObject);
        viewportHit.color = Color.clear; viewportHit.sprite = null; viewportHit.material = null; viewportHit.raycastTarget = true;
        Mask oldMask = scroll.viewport.GetComponent<Mask>();
        if (oldMask != null) oldMask.enabled = false;
        Ensure<RectMask2D>(scroll.viewport.gameObject);
        if (body == null) body = Text(scroll.content, "Description", "Discover a material to learn how to use it.", false);
        else body.transform.SetParent(scroll.content, false);
        StyleText(body, false);
        body.alignment = TextAlignmentOptions.Center;
        body.overflowMode = TextOverflowModes.Overflow;
        foreach (Behaviour component in scroll.content.GetComponentsInChildren<Behaviour>(true))
            if (component is LayoutGroup || component is ContentSizeFitter) component.enabled = false;
        scroll.horizontal = false; scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.inertia = true; scroll.decelerationRate = .135f; scroll.scrollSensitivity = 34f;
        scroll.verticalScrollbar = BuildScrollbar(scroll);
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        layout.descriptionScroll = scroll;
        layout.descriptionText = body;
        if (close == null) close = Ensure<Button>(Rect(layout.card, "Close").gameObject);
        else close.transform.SetParent(layout.card, false);
        Image closeHit = Ensure<Image>(close.gameObject);
        closeHit.sprite = circle; closeHit.type = Image.Type.Simple; closeHit.color = CozyModalLayout.Ink;
        closeHit.material = null; closeHit.raycastTarget = true;
        RectTransform face = Rect(close.transform, "Cream Face");
        Image faceImage = Ensure<Image>(face.gameObject);
        faceImage.sprite = circle; faceImage.color = CozyModalLayout.Cream; faceImage.raycastTarget = false;
        CozyModalLayout.Stretch(face, 5f, 5f, 5f, 5f);
        TMP_Text cross = Text(close.transform, "Close Symbol", "×", true);
        cross.fontSize = 66f; cross.alignment = TextAlignmentOptions.Center;
        CozyModalLayout.Stretch(cross.rectTransform, 8f, 8f, 8f, 12f);
        close.targetGraphic = closeHit;
        close.navigation = new Navigation { mode = Navigation.Mode.None };
        ColorBlock colors = close.colors;
        colors.normalColor = Color.white; colors.highlightedColor = new Color(1f, .94f, .82f);
        colors.pressedColor = new Color(.80f, .72f, .62f); colors.fadeDuration = .08f; close.colors = colors;
        Ensure<BuildModeIconFeedback>(close.gameObject);
        layout.dismissButton = close;
        layout.titleTab.SetAsLastSibling();
        close.transform.SetAsLastSibling();
        for (int i = 0; i < root.transform.childCount; i++)
            if (root.transform.GetChild(i) != layout.card) root.transform.GetChild(i).gameObject.SetActive(false);
        layout.authoredVersion = 1;
        layout.ApplyLayout();
        EditorUtility.SetDirty(layout);
        return layout;
    }

    private static Scrollbar BuildScrollbar(ScrollRect scroll)
    {
        RectTransform rail = Rect(scroll.transform, "Reading Scrollbar");
        rail.anchorMin = new Vector2(1f, 0f); rail.anchorMax = Vector2.one;
        rail.pivot = new Vector2(1f, .5f); rail.anchoredPosition = Vector2.zero; rail.sizeDelta = new Vector2(7f, 0f);
        Image track = Ensure<Image>(rail.gameObject); track.color = Color.clear; track.raycastTarget = true;
        RectTransform handle = Panel(rail, "Handle", new Color32(146, 109, 70, 255));
        CozyModalLayout.Stretch(handle, 0f, 0f, 0f, 0f);
        Scrollbar scrollbar = Ensure<Scrollbar>(rail.gameObject);
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scrollbar.handleRect = handle; scrollbar.targetGraphic = handle.GetComponent<Image>();
        scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };
        return scrollbar;
    }

    private static TMP_Text Text(Transform parent, string name, string value, bool bold)
    {
        TMP_Text text = Ensure<TextMeshProUGUI>(Rect(parent, name).gameObject);
        text.text = value; StyleText(text, bold); return text;
    }
    private static void StyleText(TMP_Text text, bool bold)
    {
        text.font = font; text.fontSharedMaterial = font.material;
        text.color = CozyModalLayout.Ink; text.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        text.enableAutoSizing = false; text.enableWordWrapping = true; text.richText = true;
        text.alignment = TextAlignmentOptions.Center; text.raycastTarget = false;
        text.margin = Vector4.zero;
    }
    private static RectTransform Panel(Transform parent, string name, Color color)
    {
        RectTransform rect = Rect(parent, name);
        Image image = Ensure<Image>(rect.gameObject);
        image.sprite = rounded; image.type = Image.Type.Sliced; image.color = color;
        image.material = null; image.raycastTarget = false; return rect;
    }
    private static RectTransform Rect(Transform parent, string name)
    {
        RectTransform rect = parent.Find(name) as RectTransform;
        if (rect != null) return rect;
        GameObject go = new GameObject(name, typeof(RectTransform)); go.layer = 5;
        rect = go.transform as RectTransform; rect.SetParent(parent, false); return rect;
    }
    private static T Ensure<T>(GameObject go) where T : Component
    { return go.GetComponent<T>() ?? go.AddComponent<T>(); }
    private static Sprite SpriteAt(string guid)
    { return AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(guid)); }

    [MenuItem("Tools/Civil Craft/Capture Learning Popup Previews")]
    public static void CapturePreviews()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        Directory.CreateDirectory(Output);
        Scene active = SceneManager.GetActiveScene();
        Scene scene = SceneManager.GetSceneByPath(Scenes[0]);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(Scenes[0], OpenSceneMode.Additive);
        try
        {
            LessonData lesson = AssetDatabase.LoadAssetAtPath<LessonData>("Assets/BridgeBuilder/Data/Lessons/LiveLoadLesson.asset");
            BridgeMaterialSO material = AssetDatabase.LoadAssetAtPath<BridgeMaterialSO>("Assets/BridgeBuilder/Data/Resources/ConcreteBeam.asset");
            LearningPopupLayout lessonLayout = null;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (LessonUIManager manager in root.GetComponentsInChildren<LessonUIManager>(true))
                    if (manager.Panel != null) lessonLayout = manager.Panel.GetComponent<LearningPopupLayout>();
            GameObject prefab = PrefabUtility.LoadPrefabContents(RewardPrefab);
            try
            {
                LearningPopupLayout materialLayout = prefab.GetComponent<ItemUnlockUI>().materialPopup;
                foreach (bool tablet in new[] { false, true })
                {
                    int width = tablet ? 1920 : 2340, height = tablet ? 1200 : 1080;
                    string device = tablet ? "tablet" : "phone";
                    if (lessonLayout != null && lesson != null)
                        Render(lessonLayout, "Lesson-" + device, width, height, lesson.Title, lesson.Image, lesson.PopupDescription);
                    if (materialLayout != null && material != null)
                    {
                        Render(materialLayout, "Material-" + device, width, height, material.GetDisplayName(), material.PopupImage, material.PopupDescription);
                        Render(materialLayout, "LongMaterial-" + device, width, height, material.GetDisplayName(), material.PopupImage,
                            ItemUnlockUI.BuildMaterialReferenceDetails(material));
                        Render(materialLayout, "LongMaterialBottom-" + device, width, height, material.GetDisplayName(), material.PopupImage,
                            ItemUnlockUI.BuildMaterialReferenceDetails(material), true);
                        Render(materialLayout, "NoImage-" + device, width, height, "Building Material", null, material.PopupDescription);
                    }
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
        }
        finally
        {
            if (opened) EditorSceneManager.CloseScene(scene, true);
            if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
        }
        Debug.Log("[Learning UI QA] Phone/tablet previews saved to " + Output);
    }

    private static void Render(LearningPopupLayout source, string label, int width, int height,
        string title, Sprite image, string description, bool scrollBottom = false)
    {
        // URP excludes editor preview scenes from a normal camera's scene mask.
        // Use a temporary additive scene, as the existing modal QA tool does.
        Scene staging = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        RenderTexture texture = new RenderTexture(width, height, 24);
        RenderTexture previous = RenderTexture.active;
        Texture2D pixels = null;
        Camera camera = null;
        try
        {
            GameObject cameraObject = new GameObject("Learning Preview Camera", typeof(Camera));
            camera = cameraObject.GetComponent<Camera>();
            camera.transform.position = new Vector3(0f, 0f, -100f);
            camera.orthographic = true; camera.orthographicSize = 540f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.36f, .25f, .17f);
            camera.cullingMask = 1 << 5; camera.targetTexture = texture;
            GameObject canvasObject = new GameObject("Learning Preview Canvas", typeof(RectTransform), typeof(Canvas));
            canvasObject.layer = 5;
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = camera;
            (canvas.transform as RectTransform).sizeDelta = new Vector2(width * 1080f / height, 1080f);
            GameObject clone = UnityEngine.Object.Instantiate(source.gameObject, canvas.transform, false);
            CozyModalLayout.Stretch(clone.transform as RectTransform, 0f, 0f, 0f, 0f);
            clone.SetActive(true);
            LearningPopupLayout layout = clone.GetComponent<LearningPopupLayout>();
            layout.SetContent(title, image, description);
            Canvas.ForceUpdateCanvases(); layout.RefreshContent();
            foreach (TMP_Text text in clone.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate(true);
            Canvas.ForceUpdateCanvases();
            if (scrollBottom)
            {
                layout.descriptionScroll.verticalNormalizedPosition = 0f;
                Canvas.ForceUpdateCanvases();
            }
            if (layout.imageFrame != null && layout.imageFrame.gameObject.activeSelf)
            {
                float imageBottom = layout.imageFrame.anchoredPosition.y + layout.card.rect.height - layout.imageFrame.rect.height * .5f;
                float descriptionTop = layout.descriptionFrame.anchoredPosition.y + layout.descriptionFrame.rect.height * .5f;
                if (imageBottom <= descriptionTop) throw new InvalidOperationException("Image and description overlap in " + label);
            }
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = texture };
            if (RenderPipeline.SupportsRenderRequest(camera, request)) RenderPipeline.SubmitRenderRequest(camera, request);
            else camera.Render();
            RenderTexture.active = texture;
            pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0f, 0f, width, height), 0, 0); pixels.Apply();
            File.WriteAllBytes(Output + "/" + label + ".png", pixels.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
            if (camera != null) camera.targetTexture = null;
            texture.Release(); UnityEngine.Object.DestroyImmediate(texture);
            EditorSceneManager.CloseScene(staging, true);
        }
    }
}
#endif
