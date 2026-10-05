using System;
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

/// <summary>Opt-in, scene-authored registration redesign. No player saves or tutorial events are invoked.</summary>
public static class NameRegistrationUIAuthoring
{
    private const string Path = "Assets/Scenes/BHAN HOUSE.unity";
    private const string Request = "Temp/bhan-name-registration-ui-v1.request";
    private const string Report = "Temp/NameRegistrationUIValidation.txt";
    private static readonly Color Wood = new Color32(90, 55, 31, 255), Ink = new Color32(77, 53, 36, 255);
    private static readonly Color Cream = new Color32(248, 233, 204, 255), Paper = new Color32(255, 248, 231, 255);
    private static readonly Color Gold = new Color32(231, 158, 35, 255), Sand = new Color32(239, 219, 177, 255);
    private static Sprite rounded;
    private static TMP_FontAsset font;
    private static double nextCheck;

    [InitializeOnLoadMethod]
    private static void Watch() { EditorApplication.update -= CheckRequest; EditorApplication.update += CheckRequest; }
    private static void CheckRequest()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (SceneManager.GetSceneByPath(Path).isDirty)
        { File.WriteAllText(Report, "WAIT: Save Bhan House scene edits first. Nothing overwritten."); return; }
        File.Delete(Request);
        try { Author(); }
        catch (Exception error) { File.WriteAllText(Report, "FAIL: " + error); Debug.LogException(error); }
    }

    [MenuItem("Tools/Civil Craft/Restyle Bhan Name Registration")]
    public static void Author()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetSceneByPath(Path).isDirty)
            throw new InvalidOperationException("Stop Play Mode and save Bhan House edits first.");
        rounded = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Elements/UI/LevelResultRounded.png");
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/Bekind Sans SDF.asset");
        Check(rounded != null && font != null, "Existing themed font/rounded frame missing.");
        Scene scene = SceneManager.GetSceneByPath(Path); bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(Path, OpenSceneMode.Additive);
        var report = new StringBuilder();
        try
        {
            var ui = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<NameRegistrationUI>(true)).Single();
            string rules = Rules(ui);
            var buttons = ui.nameInputPanel.GetComponentsInChildren<Button>(true).Concat(ui.confirmationPanel.GetComponentsInChildren<Button>(true)).ToArray();
            var actions = buttons.ToDictionary(button => button, button => JsonUtility.ToJson(button.onClick));
            // Prove the proposed layout on disposable clones before changing the
            // authored scene, including the actual Submit/Edit presentation path.
            ValidatePreview(ui, report);
            StyleUI(ui);
            Check(Rules(ui) == rules, "Registration references, keyboard rules or follow-up events changed.");
            foreach (var action in actions) Check(JsonUtility.ToJson(action.Key.onClick) == action.Value, "A registration button action changed.");
            EditorUtility.SetDirty(ui); EditorSceneManager.MarkSceneDirty(scene);
            Check(EditorSceneManager.SaveScene(scene), "Could not save the authored Bhan registration UI.");
            report.AppendLine("PASS: Saved entry/confirmation panels with existing cream/brown artwork, Bekind font and gold primary actions. Original input references, plain-name validation, HUD hiding, button targets and Bhan OnNameRegistered event preserved.");
            report.AppendLine("NOTE: No ConfirmNameYes, save, dialogue callback or real registration was executed. Live tutorial/keyboard check still required.");
            File.WriteAllText(Report, report.ToString());
            Debug.Log("[Name registration UI] Authored themed panels and isolated layout/registration-presentation checks passed.");
        }
        finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
    }

    private static string Rules(NameRegistrationUI ui) => JsonUtility.ToJson(ui.onNameConfirmed) + "|" +
        string.Join(",", ui.canvasesToHide.Select(obj => obj != null ? obj.GetInstanceID() : 0)) + "|" +
        ui.nameInputPanel.GetInstanceID() + "|" + ui.confirmationPanel.GetInstanceID() + "|" + ui.nameInputField.GetInstanceID() + "|" +
        ui.confirmationText.GetInstanceID() + "|" + ui.nameInputField.characterLimit + "|" + ui.nameInputField.contentType + "|" + ui.nameInputField.keyboardType;

    private static void StyleUI(NameRegistrationUI ui)
    {
        Button submit = Action(ui.nameInputPanel, "SubmitName"), yes = Action(ui.confirmationPanel, "ConfirmNameYes"), no = Action(ui.confirmationPanel, "ConfirmNameNo");
        TMP_Text entryTitle = ui.nameInputPanel.GetComponentsInChildren<TMP_Text>(true).First(text => text.text == "Enter your name" || text.name == "Registration Heading");
        Transform entry = Frame(ui.nameInputPanel, "Registration Wood Frame");
        entryTitle.name = "Registration Heading"; Text(entryTitle, "WELCOME, ENGINEER", 46, 32);
        Place(entryTitle.rectTransform, .08f, .785f, .92f, .88f);
        TMP_Text kicker = Label(entry, "Registration Kicker", "BHAN’S HOUSE  /  ENGINEER REGISTRATION", 20, 17);
        Place(kicker.rectTransform, .08f, .915f, .92f, .96f); kicker.characterSpacing = 1;
        Divider(entry);
        TMP_Text subtitle = Label(entry, "Registration Introduction", "What should Professor Bhan call you?", 29, 24);
        Place(subtitle.rectTransform, .08f, .70f, .92f, .765f);
        TMP_Text fieldLabel = Label(entry, "Name Field Caption", "ENGINEER NAME", 22, 18); fieldLabel.alignment = TextAlignmentOptions.MidlineLeft;
        Place(fieldLabel.rectTransform, .10f, .63f, .90f, .685f);
        TMP_InputField input = ui.nameInputField;
        Place(input.transform as RectTransform, .10f, .455f, .90f, .61f);
        Paint(input.GetComponent<Image>(), Wood, true);
        Image inputFace = ImageChild(input.transform, "Registration Input Face", Paper);
        Place(inputFace.rectTransform, 0, 0, 1, 1); inputFace.rectTransform.sizeDelta = new Vector2(-8, -8); inputFace.transform.SetAsFirstSibling();
        input.targetGraphic = inputFace; input.customCaretColor = true; input.caretColor = Ink;
        input.selectionColor = new Color(1f, .68f, .20f, .3f); input.richText = false; input.pointSize = 34;
        var colors = input.colors; colors.normalColor = Color.white; colors.highlightedColor = new Color(1f, .98f, .91f);
        colors.selectedColor = new Color(1f, .93f, .76f); colors.pressedColor = new Color(.95f, .89f, .77f); input.colors = colors;
        Place(input.textViewport, 0, 0, 1, 1); input.textViewport.sizeDelta = new Vector2(-38, -20); input.textViewport.SetAsLastSibling();
        Text(input.textComponent, input.textComponent.text, 34, 34); input.textComponent.enableAutoSizing = false;
        input.textComponent.richText = false; input.textComponent.alignment = TextAlignmentOptions.MidlineLeft;
        Place(input.textComponent.rectTransform, 0, 0, 1, 1);
        if (input.placeholder is TMP_Text placeholder)
        {
            Text(placeholder, "Enter your name...", 34, 28); placeholder.color = new Color32(147, 113, 80, 255);
            placeholder.alignment = TextAlignmentOptions.MidlineLeft; Place(placeholder.rectTransform, 0, 0, 1, 1);
        }
        TMP_Text hint = Label(entry, "Registration Name Hint", "Your name appears in your Almanac and multiplayer.", 21, 18);
        Place(hint.rectTransform, .10f, .365f, .90f, .425f);
        ThemeButton(submit, "CONTINUE", Gold); Place(submit.transform as RectTransform, .10f, .15f, .90f, .285f);
        TMP_Text fallback = Label(entry, "Registration Fallback Hint", "A blank name will use ‘Engineer’.", 20, 17);
        Place(fallback.rectTransform, .10f, .045f, .90f, .105f);

        TMP_Text existingFooter = ui.confirmationPanel.GetComponentsInChildren<TMP_Text>(true)
            .First(text => text != ui.confirmationText && text.GetComponentInParent<Button>() == null && (text.name == "Registration Confirmation Hint" || text.text.Contains("change your name")));
        Transform confirmation = Frame(ui.confirmationPanel, "Confirmation Wood Frame");
        TMP_Text confirmationKicker = Label(confirmation, "Confirmation Kicker", "BHAN’S HOUSE  /  ENGINEER REGISTRATION", 20, 17);
        Place(confirmationKicker.rectTransform, .08f, .915f, .92f, .96f); confirmationKicker.characterSpacing = 1;
        TMP_Text confirmationTitle = Label(confirmation, "Confirmation Heading", "CONFIRM YOUR NAME", 46, 32);
        Place(confirmationTitle.rectTransform, .08f, .785f, .92f, .88f); Divider(confirmation);
        TMP_Text caption = Label(confirmation, "Confirmation Name Caption", "YOUR ENGINEER NAME", 22, 18);
        Place(caption.rectTransform, .10f, .675f, .90f, .74f);
        Image plaque = ImageChild(confirmation, "Engineer Name Plaque", Sand); Place(plaque.rectTransform, .10f, .45f, .90f, .66f);
        TMP_Text name = Label(plaque.transform, "Confirmed Engineer Name", "Engineer", 54, 24);
        name.color = new Color32(121, 80, 15, 255); name.fontStyle = FontStyles.Bold;
        name.richText = false; name.overflowMode = TextOverflowModes.Ellipsis; Place(name.rectTransform, .035f, .08f, .965f, .92f);
        ui.confirmationNameText = name;
        Text(ui.confirmationText, "This is how Bhan and other engineers will know you.", 28, 23);
        Place(ui.confirmationText.rectTransform, .10f, .305f, .90f, .405f);
        ThemeButton(no, "EDIT NAME", Sand); Place(no.transform as RectTransform, .10f, .14f, .485f, .27f);
        ThemeButton(yes, "CONFIRM NAME", Gold); Place(yes.transform as RectTransform, .515f, .14f, .90f, .27f);
        existingFooter.name = "Registration Confirmation Hint";
        Text(existingFooter, "Not quite right? Choose Edit Name to make a correction.", 20, 17);
        Place(existingFooter.rectTransform, .08f, .045f, .92f, .105f);
        ui.nameInputPanel.SetActive(false); ui.confirmationPanel.SetActive(false);
    }

    private static Transform Frame(GameObject root, string name)
    {
        Place(root.transform as RectTransform, 0, 0, 1, 1);
        Image overlay = root.GetComponent<Image>(); overlay.enabled = true; overlay.sprite = null; overlay.type = Image.Type.Simple;
        overlay.color = new Color(.08f, .055f, .035f, .58f); overlay.raycastTarget = true;
        Image frame = ImageChild(root.transform, name, Wood); Paint(frame, Wood, true);
        Place(frame.rectTransform, .27f, .22f, .73f, .78f);
        Transform face = root.GetComponentsInChildren<Transform>(true).First(node => node.name == "panel");
        face.SetParent(frame.transform, false); Place(face as RectTransform, 0, 0, 1, 1); ((RectTransform)face).sizeDelta = new Vector2(-16, -16);
        Paint(face.GetComponent<Image>(), Cream, true); return face;
    }
    private static Button Action(GameObject root, string action) => root.GetComponentsInChildren<Button>(true).Single(button =>
        Enumerable.Range(0, button.onClick.GetPersistentEventCount()).Any(index => button.onClick.GetPersistentMethodName(index) == action));
    private static void Place(RectTransform rect, float x0, float y0, float x1, float y1)
    {
        rect.anchorMin = new Vector2(x0, y0); rect.anchorMax = new Vector2(x1, y1); rect.pivot = Vector2.one * .5f;
        rect.anchoredPosition = rect.sizeDelta = Vector2.zero; rect.localScale = Vector3.one; rect.localRotation = Quaternion.identity;
    }
    private static Image ImageChild(Transform parent, string name, Color color)
    {
        Transform existing = parent.Find(name);
        var obj = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        obj.layer = parent.gameObject.layer; obj.transform.SetParent(parent, false);
        Image image = obj.GetComponent<Image>(); Paint(image, color, false); return image;
    }
    private static void Paint(Image image, Color color, bool raycast)
    { image.enabled = true; image.sprite = rounded; image.type = Image.Type.Sliced; image.color = color; image.raycastTarget = raycast; }
    private static TMP_Text Label(Transform parent, string name, string value, float maximum, float minimum)
    {
        Transform existing = parent.Find(name);
        var obj = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        obj.layer = parent.gameObject.layer; obj.transform.SetParent(parent, false);
        TMP_Text text = obj.GetComponent<TMP_Text>(); Text(text, value, maximum, minimum); return text;
    }
    private static void Text(TMP_Text text, string value, float maximum, float minimum)
    {
        text.font = font; text.fontSharedMaterial = font.material; text.text = value; text.color = Ink;
        text.fontStyle = FontStyles.Normal; text.alignment = TextAlignmentOptions.Center; text.characterSpacing = 0;
        text.fontSize = text.fontSizeMax = maximum; text.fontSizeMin = minimum; text.enableAutoSizing = true;
        text.raycastTarget = false; text.richText = false; text.margin = Vector4.zero; text.enableWordWrapping = true;
    }
    private static void Divider(Transform parent)
    {
        Image divider = ImageChild(parent, "Registration Header Divider", new Color32(177, 131, 76, 255));
        divider.sprite = null; divider.type = Image.Type.Simple; Place(divider.rectTransform, .10f, .765f, .90f, .769f);
    }
    private static void ThemeButton(Button button, string label, Color fill)
    {
        Paint(button.GetComponent<Image>(), Wood, true);
        Image face = ImageChild(button.transform, "Registration Button Face", fill);
        Place(face.rectTransform, 0, 0, 1, 1); face.rectTransform.sizeDelta = new Vector2(-8, -8); face.transform.SetAsFirstSibling();
        button.targetGraphic = face; button.transition = Selectable.Transition.ColorTint;
        ColorBlock colors = button.colors; colors.normalColor = Color.white; colors.highlightedColor = new Color(1, .95f, .82f);
        colors.selectedColor = colors.highlightedColor; colors.pressedColor = new Color(.85f, .76f, .61f); button.colors = colors;
        TMP_Text text = button.GetComponentInChildren<TMP_Text>(true); Text(text, label, 30, 23);
        Place(text.rectTransform, .04f, .07f, .96f, .93f); text.transform.SetAsLastSibling();
    }

    private static void ValidatePreview(NameRegistrationUI source, StringBuilder report)
    {
        Scene scene = EditorSceneManager.NewPreviewScene(); RenderTexture target = null;
        try
        {
            var canvasObj = new GameObject("Registration Preview (Temporary)", typeof(RectTransform), typeof(Canvas));
            SceneManager.MoveGameObjectToScene(canvasObj, scene); canvasObj.layer = 30;
            var canvas = canvasObj.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            var canvasRect = canvasObj.GetComponent<RectTransform>(); canvasRect.sizeDelta = new Vector2(1920, 1080);
            var controllerObj = new GameObject("Registration Controller Check (Temporary)"); controllerObj.SetActive(false); SceneManager.MoveGameObjectToScene(controllerObj, scene);
            var fixture = controllerObj.AddComponent<NameRegistrationUI>();
            fixture.nameInputPanel = Object.Instantiate(source.nameInputPanel, canvasObj.transform, false);
            fixture.confirmationPanel = Object.Instantiate(source.confirmationPanel, canvasObj.transform, false);
            fixture.nameInputField = fixture.nameInputPanel.GetComponentInChildren<TMP_InputField>(true);
            fixture.confirmationText = fixture.confirmationPanel.GetComponentsInChildren<TextMeshProUGUI>(true).Single(text => text.name == source.confirmationText.name &&
                text.GetComponentInParent<Button>() == null && (text.text.Contains("pendingName") || text.text.Contains("engineers will know you")));
            StyleUI(fixture);
            foreach (Transform node in canvasObj.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = 30;
            var cameraObj = new GameObject("Registration Preview Camera (Temporary)", typeof(Camera)); SceneManager.MoveGameObjectToScene(cameraObj, scene);
            var camera = cameraObj.GetComponent<Camera>(); camera.scene = scene; camera.enabled = false;
            camera.orthographic = true; camera.orthographicSize = 540; camera.transform.position = new Vector3(0, 0, -1000);
            camera.nearClipPlane = .1f; camera.farClipPlane = 2000; camera.cullingMask = 1 << 30;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(148, 113, 85, 255); canvas.worldCamera = camera;
            foreach (Vector2Int size in new[] { new Vector2Int(1920, 1080), new Vector2Int(1920, 1200), new Vector2Int(2340, 1080) })
            {
                canvasRect.sizeDelta = size; camera.orthographicSize = size.y / 2f; camera.aspect = (float)size.x / size.y;
                target = new RenderTexture(size.x * 2 / 3, size.y * 2 / 3, 24); target.Create(); camera.targetTexture = target;
                fixture.nameInputPanel.SetActive(true); fixture.confirmationPanel.SetActive(false);
                fixture.nameInputField.SetTextWithoutNotify(""); ValidateFit(fixture.nameInputPanel);
                Capture(camera, target, "Temp/NameRegistrationEntry_" + size.x + "x" + size.y + ".png");
                foreach (string name in new[] { "  Ada Engineer  ", "  ", new string('W', 32), new string('W', 120) })
                {
                    fixture.nameInputField.SetTextWithoutNotify(name); fixture.SubmitName();
                    string expected = string.IsNullOrWhiteSpace(name) ? "Engineer" : name.Trim();
                    Check(fixture.confirmationNameText.text == expected && !fixture.confirmationNameText.richText, "Confirmation changes or parses the player's name.");
                    Check(!fixture.nameInputPanel.activeSelf && fixture.confirmationPanel.activeSelf, "Submit did not open confirmation.");
                    ValidateFit(fixture.confirmationPanel);
                    if (name == "  Ada Engineer  ") Capture(camera, target, "Temp/NameRegistrationConfirmation_" + size.x + "x" + size.y + ".png");
                    fixture.ConfirmNameNo(); Check(fixture.nameInputPanel.activeSelf && !fixture.confirmationPanel.activeSelf && fixture.nameInputField.text == name,
                        "Edit Name loses the entered value or fails to reopen the field.");
                }
                TMP_Text hint = fixture.nameInputPanel.GetComponentsInChildren<TMP_Text>(true)
                    .Single(text => text.name == "Registration Name Hint");
                foreach (string name in new[] { "<#BF40BF>Ada", "Ada#BF40BF", "<b>Ada</b>", ".dev_Ada", ".DEV_Ada" })
                {
                    fixture.nameInputField.SetTextWithoutNotify(name); fixture.SubmitName();
                    Check(!PlayerNamePolicy.TryValidate(name, out string error) && hint.text == error &&
                        fixture.nameInputPanel.activeSelf && !fixture.confirmationPanel.activeSelf &&
                        fixture.nameInputField.text == name, "Invalid names must retain input and show a plain-name error.");
                    ValidateFit(fixture.nameInputPanel);
                }
                Release(target); target = null;
                report.AppendLine("PASS: " + size + " entry/confirmation layout, trim/blank-name fallback, hex/markup/reserved-name rejection, 32/120-character confirmation, retained text on Edit Name and untouched save/dialogue flow.");
            }
        }
        finally { Release(target); EditorSceneManager.ClosePreviewScene(scene); }
    }
    private static void ValidateFit(GameObject root)
    {
        Canvas.ForceUpdateCanvases();
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>())
        {
            text.ForceMeshUpdate(); Check(!text.isTextOverflowing || text.overflowMode == TextOverflowModes.Ellipsis, "Registration text overflows: " + text.name);
            Vector3[] corners = new Vector3[4]; text.rectTransform.GetWorldCorners(corners);
            RectTransform owner = root.transform as RectTransform;
            foreach (Vector3 corner in corners) Check(owner.rect.Contains((Vector2)owner.InverseTransformPoint(corner)), "A registration label leaves the screen.");
        }
    }
    private static void Capture(Camera camera, RenderTexture target, string path)
    {
        Canvas.ForceUpdateCanvases();
        if (GraphicsSettings.currentRenderPipeline == null) camera.Render();
        else RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        RenderTexture previous = RenderTexture.active; var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        try { RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply(); File.WriteAllBytes(path, image.EncodeToPNG()); }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
    }
    private static void Release(RenderTexture target) { if (target != null) { target.Release(); Object.DestroyImmediate(target); } }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
