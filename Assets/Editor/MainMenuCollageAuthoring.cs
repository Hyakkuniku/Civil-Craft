using System;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Explicit scene authoring. Runtime still only shows/animates existing UI.</summary>
public static class MainMenuCollageAuthoring
{
    private const string ScenePath = "Assets/Scenes/Main Menu.unity";
    private const string Request = "Temp/main-menu-collage.request";
    private const string Report = "Temp/MainMenuCollageValidation.txt";
    private static readonly Color Wood = new Color32(90, 55, 31, 255);
    private static readonly Color Ink = new Color32(77, 53, 36, 255);
    private static readonly Color Cream = new Color32(248, 233, 204, 255);
    private static readonly Color Paper = new Color32(255, 248, 231, 255);
    private static readonly Color Gold = new Color32(231, 158, 35, 255);
    private static readonly Color Sand = new Color32(239, 219, 177, 255);
    private static Sprite rounded;
    private static TMP_FontAsset font;
    private static double nextCheck;

    [InitializeOnLoadMethod]
    private static void Watch() { EditorApplication.update -= Check; EditorApplication.update += Check; }
    private static void Check()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (SceneManager.GetSceneByPath(ScenePath).isDirty)
        {
            File.WriteAllText(Report, "WAIT: Save your Main Menu scene edits first. Nothing has been overwritten."); return;
        }
        File.Delete(Request);
        try { Author(); }
        catch (Exception error) { File.WriteAllText(Report, "FAIL: " + error); Debug.LogException(error); }
    }

    [MenuItem("Tools/Civil Craft/Author Main Menu Collage")]
    public static void Author()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Stop Play Mode first.");
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        if (scene.isDirty) throw new Exception("Save your Main Menu scene edits first.");
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            rounded = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Elements/UI/LevelResultRounded.png");
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/Bekind Sans SDF.asset");
            if (rounded == null || font == null) throw new Exception("Existing UI sprite/font missing.");
            var controller = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MainMenuUIController>(true)).Single();
            var data = new SerializedObject(controller);
            var panel = (RectTransform)data.FindProperty("dropdownPanel").objectReferenceValue;
            var buttons = panel.GetComponentsInChildren<Button>(true);
            var originalEvents = buttons.ToDictionary(b => b, b => JsonUtility.ToJson(b.onClick));
            string originalRules = JsonUtility.ToJson(controller);
            var originalActive = buttons.ToDictionary(b => b, b => b.gameObject.activeSelf);
            foreach (LayoutGroup layout in panel.GetComponentsInChildren<LayoutGroup>(true)) layout.enabled = false;
            foreach (ContentSizeFitter fitter in panel.GetComponentsInChildren<ContentSizeFitter>(true)) fitter.enabled = false;
            foreach (ScrollRect scroll in panel.GetComponentsInChildren<ScrollRect>(true))
            {
                scroll.StopMovement(); scroll.velocity = Vector2.zero;
                scroll.horizontal = scroll.vertical = false; scroll.enabled = false;
            }
            foreach (Scrollbar scrollbar in panel.GetComponentsInChildren<Scrollbar>(true)) scrollbar.gameObject.SetActive(false);
            foreach (Mask mask in panel.GetComponentsInChildren<Mask>(true)) mask.enabled = false;
            foreach (RectMask2D mask in panel.GetComponentsInChildren<RectMask2D>(true)) mask.enabled = false;
            panel.anchorMin = panel.anchorMax = new Vector2(.5f, .5f);
            panel.pivot = new Vector2(.5f, .5f); panel.sizeDelta = new Vector2(1220, 800);
            panel.localScale = Vector3.one;
            Frame(panel.gameObject, Cream);
            var viewport = panel.Find("DropdownContent"); Place(viewport.gameObject, .05f, .08f, .95f, .80f);
            var viewportImage = viewport.GetComponent<Image>(); if (viewportImage != null) viewportImage.enabled = false;
            var content = viewport.Find("ButtonContent"); Place(content.gameObject, 0, 0, 1, 1);
            var party = content.Find("BottomParty");
            if (party != null) party.gameObject.SetActive(false);
            Button Get(string name) => buttons.Single(b => b.name == name);
            var mode = Get("ModeSelectionButton");
            var achievement = Get("AchievementButton");
            var store = Get("StoreButton");
            var leaders = Get("Leaderboards");
            var settings = Get("SettingsButton");
            var login = Get("LoginButton");
            var credits = Get("CreditsButton");
            var back = Get("BackButton");
            foreach (Button button in buttons) button.transform.SetParent(content, false);
            Tile(mode, Gold, "MODE\nSELECTION", "Explore your world\nor join a friend's session.", 0, .30f, .51f, 1, 50);
            var kicker = Label(mode.transform, "Tile Kicker", "YOUR NEXT ADVENTURE", 20);
            Place(kicker.gameObject, .07f, .79f, .93f, .91f); kicker.characterSpacing = 2;
            var modeLabel = mode.GetComponentsInChildren<TMP_Text>(true).First(t => t.name != "Tile Caption" && t.name != "Tile Kicker");
            Place(modeLabel.gameObject, .07f, .33f, .93f, .76f);
            Tile(achievement, Paper, "ACHIEVEMENTS", "Milestones you've earned", .53f, .53f, 1, 1, 38);
            Tile(store, Sand, "STORE", "Browse the shop", .53f, .30f, 1, .51f, 32);
            Tile(settings, Sand, "SETTINGS", "Audio & display", 0, 0, .25f, .27f, 32);
            Tile(login, Paper, "LOGIN", "Account & cloud saves", .27f, 0, .60f, .27f, 32);
            Tile(credits, Cream, "CREDITS", "", .62f, 0, .80f, .27f, 28);
            Tile(back, Cream, "BACK", "", .82f, 0, 1, .27f, 28);
            LockedTile(content, "Achievements Locked Tile", "ACHIEVEMENTS", "LOCKED\nEarn your first achievement", .53f, .53f, 1, 1, 34);
            LockedTile(content, "Store Locked Tile", "STORE", "LOCKED\nUnlock through Story", .53f, .30f, 1, .51f, 28);
            var title = Label(panel, "Collage Title", "MAIN MENU", 42); Place(title.gameObject, .12f, .875f, .88f, .965f);
            var hint = Label(panel, "Collage Hint", "CHOOSE YOUR NEXT ADVENTURE", 20); Place(hint.gameObject, .12f, .815f, .88f, .875f);
            hint.characterSpacing = 3;
            // Original feature activation and auth/slide rules are preserved.
            foreach (Button button in buttons)
            {
                button.gameObject.SetActive(originalActive[button]);
                if (originalEvents[button] != JsonUtility.ToJson(button.onClick)) throw new Exception(button.name + " listener changed.");
                if (button.GetComponent<CanvasGroup>() == null) button.gameObject.AddComponent<CanvasGroup>();
                var nav = button.navigation; nav.mode = Navigation.Mode.Automatic; button.navigation = nav;
            }
            // Keep the unused legacy object recoverable, but allocate no tile
            // space to it. Its click action was never wired in this scene.
            leaders.gameObject.SetActive(false);
            if (originalRules != JsonUtility.ToJson(controller)) throw new Exception("Auth, feature unlock, or slide configuration changed.");
            var group = (CanvasGroup)data.FindProperty("dropdownCanvasGroup").objectReferenceValue;
            group.alpha = 0; group.interactable = false; group.blocksRaycasts = false;
            panel.anchoredPosition = new Vector2(0, 1200);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            var report = new StringBuilder("PASS: Main Menu scene-authored collage saved. Store and its locked tile fill the entire right-hand row. Legacy Leaderboards stays inactive. Existing listeners/SFX, auth settings, feature unlock IDs and slide/cascade configuration preserved.\n");
            Preview(panel.gameObject, report);
            File.WriteAllText(Report, report.ToString()); Debug.Log("[Main menu collage] " + report);
        }
        finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
    }

    private static void Place(GameObject obj, float l, float b, float r, float t)
    {
        var rect = (RectTransform)obj.transform;
        rect.anchorMin = new Vector2(l, b); rect.anchorMax = new Vector2(r, t); rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = Vector2.zero; rect.sizeDelta = Vector2.zero; rect.localScale = Vector3.one; rect.localRotation = Quaternion.identity;
    }
    private static Image ImageChild(Transform parent, string name)
    {
        var child = parent.Find(name);
        var obj = child != null ? child.gameObject : new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        obj.layer = parent.gameObject.layer; obj.transform.SetParent(parent, false); return obj.GetComponent<Image>();
    }
    private static void Frame(GameObject obj, Color fill)
    {
        var image = obj.GetComponent<Image>(); image.sprite = rounded; image.type = Image.Type.Sliced; image.color = Wood;
        image.raycastTarget = obj.GetComponent<Button>() != null || obj.name == "MainMenuDropdownPanel";
        foreach (Shadow shadow in obj.GetComponents<Shadow>()) shadow.enabled = false;
        var inset = ImageChild(obj.transform, "Collage Inset"); inset.sprite = rounded; inset.type = Image.Type.Sliced;
        inset.color = fill; inset.raycastTarget = false; Place(inset.gameObject, 0, 0, 1, 1);
        ((RectTransform)inset.transform).sizeDelta = Vector2.one * (obj.name == "MainMenuDropdownPanel" ? -16 : -10);
        inset.transform.SetAsFirstSibling();
    }
    private static TMP_Text Label(Transform parent, string name, string value, float size)
    {
        var child = parent.Find(name);
        var obj = child != null ? child.gameObject : new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        obj.layer = parent.gameObject.layer; obj.transform.SetParent(parent, false);
        var text = obj.GetComponent<TMP_Text>(); StyleText(text, value, size); return text;
    }
    private static void StyleText(TMP_Text text, string value, float size)
    {
        text.font = font; text.fontSharedMaterial = font.material; text.text = value; text.color = Ink;
        text.fontStyle = FontStyles.Normal; text.fontSize = size; text.enableAutoSizing = true;
        text.fontSizeMin = size * .76f; text.fontSizeMax = size; text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false; text.margin = Vector4.zero; text.enableWordWrapping = true;
        text.richText = true; text.overflowMode = TextOverflowModes.Truncate;
    }
    private static void Tile(Button button, Color fill, string title, string caption, float l, float b, float r, float t, float size)
    {
        Place(button.gameObject, l, b, r, t); Frame(button.gameObject, fill);
        var text = button.GetComponentsInChildren<TMP_Text>(true).First(x => x.name != "Tile Caption" && x.name != "Tile Kicker");
        StyleText(text, title, size); Place(text.gameObject, .07f, caption.Length > 0 ? .43f : .12f, .93f, .87f);
        var label = Label(button.transform, "Tile Caption", caption, title.StartsWith("MODE") ? 27 : 21);
        Place(label.gameObject, .06f, .11f, .94f, .40f); label.gameObject.SetActive(caption.Length > 0);
        button.targetGraphic = button.transform.Find("Collage Inset").GetComponent<Image>();
        button.transition = Selectable.Transition.ColorTint;
        var colors = button.colors; colors.normalColor = Color.white; colors.highlightedColor = new Color(1, .95f, .82f);
        colors.selectedColor = colors.highlightedColor; colors.pressedColor = new Color(.83f, .76f, .65f);
        colors.disabledColor = new Color(.65f, .65f, .65f, .7f); button.colors = colors;
    }
    private static void LockedTile(Transform parent, string name, string title, string caption, float l, float b, float r, float t, float size)
    {
        var image = ImageChild(parent, name); Place(image.gameObject, l, b, r, t); Frame(image.gameObject, Sand);
        var label = Label(image.transform, "Title", title, size); Place(label.gameObject, .07f, .47f, .93f, .84f);
        var hint = Label(image.transform, "Unlock Hint", caption, 21); Place(hint.gameObject, .07f, .1f, .93f, .44f);
        // Always behind the opaque action tile: no new feature-unlock logic or
        // runtime UI creation. The placeholder has no Button or click handler.
        image.transform.SetAsFirstSibling();
    }

    private static void Preview(GameObject panel, StringBuilder report)
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        RenderTexture previous = RenderTexture.active;
        try
        {
            var canvasObj = new GameObject("Collage Preview", typeof(RectTransform), typeof(Canvas));
            SceneManager.MoveGameObjectToScene(canvasObj, scene); canvasObj.layer = 5;
            var canvas = canvasObj.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            var canvasRect = (RectTransform)canvas.transform; canvasRect.pivot = new Vector2(.5f, .5f);
            canvasRect.position = new Vector3(10000, 10000, 10000); canvasRect.localScale = Vector3.one * .01f;
            var copy = Object.Instantiate(panel, canvas.transform, false); copy.SetActive(true);
            ((RectTransform)copy.transform).anchoredPosition = Vector2.zero;
            var previewGroup = copy.GetComponent<CanvasGroup>();
            previewGroup.alpha = 1; previewGroup.interactable = true; previewGroup.blocksRaycasts = true;
            var cameraObj = new GameObject("Collage Preview Camera", typeof(Camera)); SceneManager.MoveGameObjectToScene(cameraObj, scene);
            var camera = cameraObj.GetComponent<Camera>(); camera.scene = scene; camera.orthographic = true;
            camera.orthographicSize = 5.4f; camera.nearClipPlane = .1f; camera.farClipPlane = 20;
            camera.transform.position = canvasRect.position + Vector3.back * 10;
            camera.cullingMask = 1 << 5; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(83, 67, 53, 255); canvas.worldCamera = camera;
            foreach (var size in new[] { new Vector2Int(1600, 900), new Vector2Int(1280, 800) })
            {
                canvasRect.sizeDelta = new Vector2(1080f * size.x / size.y, 1080);
                var target = new RenderTexture(size.x, size.y, 24); target.Create();
                try
                {
                    camera.targetTexture = target; camera.aspect = (float)size.x / size.y;
                    foreach (bool unlocked in new[] { true, false })
                    {
                        foreach (Button button in copy.GetComponentsInChildren<Button>(true))
                        {
                            // Only vary actual feature tiles. Do not force saved
                            // inactive controls visible and conceal runtime gaps.
                            if (button.name == "AchievementButton" || button.name == "StoreButton")
                                button.gameObject.SetActive(unlocked);
                            var group = button.GetComponent<CanvasGroup>(); if (group != null) group.alpha = 1;
                        }
                        Canvas.ForceUpdateCanvases();
                        var store = copy.GetComponentsInChildren<Button>(true).Single(b => b.name == "StoreButton");
                        if (((RectTransform)store.transform).anchorMax.x != 1 ||
                            ((RectTransform)copy.transform.Find("DropdownContent/ButtonContent/Store Locked Tile")).anchorMax.x != 1 ||
                            copy.GetComponentsInChildren<Button>(true).Single(b => b.name == "Leaderboards").gameObject.activeSelf)
                            throw new Exception("Store row is not full width or the legacy Leaderboards is visible.");
                        foreach (TMP_Text text in copy.GetComponentsInChildren<TMP_Text>(true).Where(t => t.gameObject.activeInHierarchy))
                        {
                            text.ForceMeshUpdate();
                            if (text.isTextOverflowing) throw new Exception("Text overflows: " + text.transform.parent.name + "/" + text.name);
                        }
                        var activeButtons = copy.GetComponentsInChildren<Button>().Select(b => (RectTransform)b.transform).ToArray();
                        for (int i = 0; i < activeButtons.Length; i++)
                            for (int j = i + 1; j < activeButtons.Length; j++)
                            {
                                Rect A(RectTransform rt) => new Rect(rt.anchorMin, rt.anchorMax - rt.anchorMin);
                                if (A(activeButtons[i]).Overlaps(A(activeButtons[j]))) throw new Exception("Tiles overlap.");
                            }
                        if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null) camera.Render();
                        else UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,
                            new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = target });
                        RenderTexture.active = target;
                        var image = new Texture2D(size.x, size.y, TextureFormat.RGBA32, false);
                        try
                        {
                            image.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0); image.Apply();
                            File.WriteAllBytes($"Temp/MainMenuCollage_{(unlocked ? "Unlocked" : "Locked")}_{size.x}x{size.y}.png", image.EncodeToPNG());
                        }
                        finally { Object.DestroyImmediate(image); }
                    }
                }
                finally { camera.targetTexture = null; target.Release(); Object.DestroyImmediate(target); }
            }
            report.AppendLine("PASS: Locked/unlocked UI previews at 16:9 and 16:10; no tile overlap or text overflow. No accounts, saves, or unlock data changed. Live clicks and slide animations still require Play Mode testing.");
        }
        finally { RenderTexture.active = previous; EditorSceneManager.ClosePreviewScene(scene); }
    }
}
