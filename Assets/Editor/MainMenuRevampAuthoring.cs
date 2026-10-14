#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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

/// <summary>Explicit, undoable presentation-only authoring. Never invokes menu callbacks or player services.</summary>
public static class MainMenuRevampAuthoring
{
    private const string ScenePath = "Assets/Scenes/Main Menu.unity";
    private const string Request = "Temp/main-menu-centered-intro.request";
    private const string Report = "Temp/main-menu-revamp-report.txt";
    private const string ValidationRequest = "Temp/main-menu-revamp-validation.request";
    private const string ValidationReport = "Temp/main-menu-revamp-validation-report.txt";
    private const string ManagerName = "MainMenuPresentationManager";
    private const string BoardName = "MainMenuHeroBoard";
    private const string SurfaceName = "MainMenuPaperSurface";
    private static readonly Color Ink = new Color32(75, 50, 36, 255);
    private static readonly Color Cream = new Color32(255, 249, 238, 255);
    private static readonly Color Sand = new Color32(239, 226, 204, 255);
    private static readonly Color Gold = new Color32(239, 166, 49, 255);
    private static readonly Color Tape = new Color32(236, 207, 143, 225);
    private static readonly Color QuitRed = new Color32(193, 45, 45, 255);
    private static Sprite rounded;
    private static TMP_FontAsset font;
    private static bool recordingUndo;
    private static double nextCheck;

    [InitializeOnLoadMethod]
    private static void Watch()
    {
        EditorApplication.update -= CheckRequest;
        EditorApplication.update += CheckRequest;
    }

    private static bool Busy => EditorApplication.isCompiling || EditorApplication.isUpdating ||
        EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer ||
        PrefabStageUtility.GetCurrentPrefabStage() != null;

    private static void CheckRequest()
    {
        if (EditorApplication.timeSinceStartup < nextCheck || Busy) return;
        nextCheck = EditorApplication.timeSinceStartup + 2;
        if (File.Exists(Request))
        {
            File.Delete(Request);
            try { Author(); }
            catch (Exception error) { File.WriteAllText(Report, "FAIL: " + error); Debug.LogException(error); }
        }
        if (File.Exists(ValidationRequest))
        {
            File.Delete(ValidationRequest);
            try { Validate(); }
            catch (Exception error) { File.WriteAllText(ValidationReport, "FAIL: " + error); Debug.LogException(error); }
        }
    }

    [MenuItem("Tools/Civil Craft/Revamp Main Menu Presentation")]
    public static void Author()
    {
        Check(!Busy, "Stay in Edit Mode and finish imports before revamping the Main Menu.");
        LoadAssets();
        Scene previous = SceneManager.GetActiveScene();
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        bool alreadyDirty = scene.isDirty, saved = false;
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Revamp Main Menu Presentation");
        Snapshot before = null;
        var report = new StringBuilder();
        try
        {
            MainMenuUIController menu = Components<MainMenuUIController>(scene).Single();
            RectTransform dropdown = new SerializedObject(menu).FindProperty("dropdownPanel").objectReferenceValue as RectTransform;
            Check(dropdown != null, "Existing menu dropdown is missing.");
            Canvas canvas = dropdown.GetComponentInParent<Canvas>(true).rootCanvas;
            Check(canvas.name == "MainMenuCanvas", "Expected the existing MainMenuCanvas.");
            GameObject manager = scene.GetRootGameObjects().SingleOrDefault(root => root.name == ManagerName);
            before = new Snapshot(scene, canvas.gameObject, manager, menu);
            Undo.RegisterFullObjectHierarchyUndo(canvas.gameObject, "Restyle Existing Main Menu");
            Undo.RecordObject(menu, "Hide Hero Board With Existing Dropdown");
            if (manager == null)
            {
                manager = new GameObject(ManagerName);
                SceneManager.MoveGameObjectToScene(manager, scene);
                Undo.RegisterCreatedObjectUndo(manager, "Create Presentation Layout Manager");
            }
            else
            {
                Check(manager.GetComponent<MainMenuPresentationLayout>() != null, "An unrelated presentation manager exists.");
                Undo.RegisterFullObjectHierarchyUndo(manager, "Update Presentation Layout");
            }
            recordingUndo = true;
            MainMenuPresentationLayout layout = Ensure<MainMenuPresentationLayout>(manager);
            BuildCenteredIntro(canvas, menu, dropdown, layout);
            // A second pass must use the same original controls and labels.
            // This catches sibling-order-dependent authoring before saving.
            BuildCenteredIntro(canvas, menu, dropdown, layout);
            recordingUndo = false;
            Canvas.ForceUpdateCanvases(); layout.ApplyLayout(); Canvas.ForceUpdateCanvases();
            before.VerifyPreserved(menu, layout.splashBoard.gameObject, layout.centeredIntro);
            ValidateReferences(menu, layout);
            Preview(layout, report);
            Undo.FlushUndoRecordObjects();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!alreadyDirty)
            {
                Check(EditorSceneManager.SaveScene(scene), "Could not save the Main Menu presentation.");
                saved = true; ValidateSavedSource();
                report.Insert(0, "PASS: Centered Main Menu intro saved. Dropdown presentation, menu/authentication/unlock configuration and callbacks preserved; only the recoverable Hero board is intentionally inactive.\n");
            }
            else report.Insert(0, "PENDING SAVE: Presentation authored in the already-dirty Main Menu. Save together with your existing edits; nothing was discarded or automatically saved.\n");
            report.AppendLine("NOTE: Detached UI previews only. No authentication, requests, player services, preferences, live callbacks or source-camera rendering ran.");
            File.WriteAllText(Report, report.ToString());
            Debug.Log("[Main Menu Presentation] " + (alreadyDirty ? "Authored; save with your existing edits." : "Saved; phone/tablet/wide checks passed."));
        }
        catch
        {
            recordingUndo = false;
            if (!saved)
            {
                Undo.RevertAllDownToGroup(undoGroup);
                if (before != null)
                {
                    before.VerifyRollback(scene);
                    if (!alreadyDirty) ClearVerifiedDirtiness(scene);
                }
            }
            throw;
        }
        finally
        {
            recordingUndo = false;
            if (opened && scene.IsValid() && scene.isLoaded && !scene.isDirty) EditorSceneManager.CloseScene(scene, true);
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
        }
    }

    [MenuItem("Tools/Civil Craft/Validate Main Menu Presentation")]
    public static void Validate()
    {
        Check(!Busy, "Stay in Edit Mode before validation.");
        LoadAssets();
        Scene previous = SceneManager.GetActiveScene();
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            ValidateSavedSource();
            MainMenuPresentationLayout layout = Components<MainMenuPresentationLayout>(scene).Single();
            ValidateReferences(Components<MainMenuUIController>(scene).Single(), layout);
            var report = new StringBuilder(); Preview(layout, report);
            report.AppendLine("RESULT: PASS. Source scene left untouched; no player services or live callbacks ran.");
            File.WriteAllText(ValidationReport, report.ToString());
        }
        finally
        {
            if (opened && scene.IsValid() && scene.isLoaded && !scene.isDirty) EditorSceneManager.CloseScene(scene, true);
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
        }
    }

    private static void LoadAssets()
    {
        Check(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null, "Main Menu scene is missing.");
        rounded = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Elements/UI/LevelResultRounded.png");
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/Bekind Sans SDF.asset");
        Check(rounded != null && font != null, "Existing rounded UI artwork or Bekind font is missing.");
    }

    private static void Build(Canvas canvas, MainMenuUIController menu, RectTransform dropdown, MainMenuPresentationLayout layout)
    {
        Transform canvasRoot = canvas.transform;
        // Keep the hero inside the original MainPanel branch: its containing
        // full-screen Image is an intentional input/background layer. A new
        // root-Canvas sibling below that layer would look correct in an
        // isolated fixture but lose real Play clicks. Reuse an earlier-authored
        // board when moving it, rather than creating another copy.
        RectTransform existingBoard = Find(canvasRoot, BoardName);
        if (existingBoard != null) Reparent(existingBoard, dropdown.parent);
        Image board = Surface(dropdown.parent, BoardName, Ink, false);
        board.transform.SetAsFirstSibling();
        Image paper = Surface(board.transform, "MainMenuHeroPaper", Cream, false); Stretch(paper.rectTransform, 7, 7, 7, 7);
        RectTransform title = Find(canvasRoot, "Civil Craft");
        RectTransform edition = Find(canvasRoot, "txt_Subheader");
        RectTransform play = Find(canvasRoot, "btn_Play");
        Check(title != null && edition != null && play.GetComponent<Button>() != null, "Existing title, subtitle or Play button is missing.");
        Reparent(title, paper.transform); Reparent(edition, paper.transform); Reparent(play, paper.transform);
        title.GetComponent<TMP_Text>().text = "CIVIL\nCRAFT";
        StyleText(title.GetComponent<TMP_Text>(), 76, TextAlignmentOptions.Center, true);
        StyleText(edition.GetComponent<TMP_Text>(), 30, TextAlignmentOptions.Center, false);
        TMP_Text tagline = Label(paper.transform, "MainMenuHeroTagline", "BUILD. EXPLORE. LEARN.", 28);
        TMP_Text footer = Label(paper.transform, "MainMenuHeroFooter", "A bridge-building adventure", 25);
        TMP_Text playLabel = StyleButton(play.GetComponent<Button>(), "PLAY", Gold, 40, true);
        Image tapeLeft = Surface(board.transform, "MainMenuTapeLeft", Tape, false);
        Image tapeRight = Surface(board.transform, "MainMenuTapeRight", Tape, false);
        tapeLeft.transform.localRotation = Quaternion.Euler(0, 0, 17);
        tapeRight.transform.localRotation = Quaternion.Euler(0, 0, -17);
        RectTransform motif = Child(paper.transform, "MainMenuBridgeMotif").transform as RectTransform;
        motif.sizeDelta = new Vector2(360, 100);
        BuildBridgeMotif(motif);

        // The existing controller already records/restores each supporting UI
        // object's activeSelf. Add only the new wrapper, leaving every other
        // manager, callback, animation setting and hide-list entry untouched.
        SerializedObject menuData = new SerializedObject(menu);
        SerializedProperty hide = menuData.FindProperty("uiToHideWhileDropdownOpen");
        bool containsBoard = false;
        for (int i = 0; i < hide.arraySize; i++) containsBoard |= hide.GetArrayElementAtIndex(i).objectReferenceValue == board.gameObject;
        if (!containsBoard)
        {
            int index = hide.arraySize; hide.InsertArrayElementAtIndex(index);
            hide.GetArrayElementAtIndex(index).objectReferenceValue = board.gameObject;
            menuData.ApplyModifiedProperties();
        }

        Image dropdownFrame = Ensure<Image>(dropdown.gameObject);
        dropdownFrame.enabled = true; dropdownFrame.sprite = rounded; dropdownFrame.type = Image.Type.Sliced;
        dropdownFrame.color = Ink; dropdownFrame.raycastTarget = true;
        Image dropdownPaper = Surface(dropdown, SurfaceName, Cream, false);
        Stretch(dropdownPaper.rectTransform, 7, 7, 7, 7); dropdownPaper.transform.SetAsLastSibling();
        TMP_Text heading = Label(dropdownPaper.transform, "MainMenuBoardHeading", "YOUR NEXT ADVENTURE", 38);
        TMP_Text subtitle = Label(dropdownPaper.transform, "MainMenuBoardSubtitle", "Choose where to begin", 26);
        string[] names = { "ModeSelectionButton", "AchievementButton", "StoreButton", "SettingsButton", "LoginButton", "CreditsButton", "BackButton" };
        string[] captions = { "PLAY & EXPLORE", "ACHIEVEMENTS", "STORE", "SETTINGS", "ACCOUNT", "CREDITS", "BACK" };
        var buttons = new List<RectTransform>();
        for (int i = 0; i < names.Length; i++)
        {
            RectTransform rect = Find(dropdown, names[i]);
            Check(rect != null && rect.GetComponent<Button>() != null, "Original dropdown entry is missing: " + names[i]);
            Reparent(rect, dropdownPaper.transform);
            StyleButton(rect.GetComponent<Button>(), captions[i], i == 0 ? Gold : Sand, i < 3 ? 32 : 28, i == 0);
            buttons.Add(rect);
        }
        RectTransform achievementsLocked = Find(dropdown, "Achievements Locked Tile");
        RectTransform storeLocked = Find(dropdown, "Store Locked Tile");
        Check(achievementsLocked != null && storeLocked != null, "Existing locked feature placeholders are missing.");
        foreach (RectTransform locked in new[] { achievementsLocked, storeLocked })
        {
            Reparent(locked, dropdownPaper.transform);
            StyleLockedTile(locked);
            locked.SetAsFirstSibling();
        }
        // All old decorative headings/viewport artwork remain authored, with
        // their active states and callbacks unchanged, but cannot overdraw or
        // intercept this paper board. Existing feature controls are reused.
        foreach (Graphic graphic in dropdown.GetComponentsInChildren<Graphic>(true))
            if (graphic.transform != dropdown && !graphic.transform.IsChildOf(dropdownPaper.transform))
            { graphic.enabled = false; graphic.raycastTarget = false; }
        foreach (RectTransform rect in buttons) rect.SetAsLastSibling();
        heading.transform.SetAsLastSibling(); subtitle.transform.SetAsLastSibling();

        RectTransform quit = Find(canvasRoot, "btn_Quit");
        StyleButton(quit.GetComponent<Button>(), "QUIT", Sand, 26, false);
        RectTransform version = Find(canvasRoot, "txt_version");
        StyleText(version.GetComponent<TMP_Text>(), 24, TextAlignmentOptions.Left, false);
        version.GetComponent<TMP_Text>().color = Cream;
        WireLayout(layout, canvasRoot, board.gameObject, dropdown.gameObject);
        layout.authoredVersion = 1; EditorUtility.SetDirty(layout); EditorUtility.SetDirty(menu);
    }

    private static void BuildCenteredIntro(Canvas canvas, MainMenuUIController menu, RectTransform dropdown, MainMenuPresentationLayout layout)
    {
        Transform root = canvas.transform;
        RectTransform board = Find(root, BoardName);
        Check(board != null, "The recoverable Hero decoration is missing.");
        RectTransform title = Find(root, "Civil Craft"), edition = Find(root, "txt_Subheader"), play = Find(root, "btn_Play");
        Check(title != null && edition != null && play != null, "The existing intro controls are missing.");
        Reparent(title, dropdown.parent); Reparent(edition, dropdown.parent); Reparent(play, dropdown.parent);
        play.SetSiblingIndex(0); title.SetSiblingIndex(1); edition.SetSiblingIndex(2);
        TMP_Text titleText = title.GetComponent<TMP_Text>();
        StyleText(titleText, 100, TextAlignmentOptions.Center, false); titleText.enabled = true;
        titleText.text = "Civil Craft"; titleText.color = Color.white;
        TMP_Text editionText = edition.GetComponent<TMP_Text>();
        StyleText(editionText, 54, TextAlignmentOptions.Center, false); editionText.enabled = true;
        editionText.text = "bridge edition"; editionText.color = Color.white;
        Button playAction = play.GetComponent<Button>(); TMP_Text playText = PrimaryLabel(playAction);
        Check(playAction != null && playText != null, "The original Play action or primary label is missing.");
        Image playGraphic = play.GetComponent<Image>();
        Check(playGraphic != null, "The original Play target image is missing.");
        playGraphic.enabled = true; playGraphic.raycastTarget = true; playGraphic.color = new Color(1,1,1,0);
        playAction.targetGraphic = playGraphic; playAction.transition = Selectable.Transition.None;
        foreach (Graphic graphic in play.GetComponentsInChildren<Graphic>(true))
            if (graphic != playGraphic && graphic != playText) { graphic.enabled = false; graphic.raycastTarget = false; }
        playText.enabled = true; playText.text = "Press To Play";
        StyleText(playText, 44, TextAlignmentOptions.Center, false); playText.color = Color.white;
        Stretch(playText.rectTransform, 12, 8, 12, 8);
        RectTransform quit = Find(root, "btn_Quit"); TMP_Text quitText = PrimaryLabel(quit != null ? quit.GetComponent<Button>() : null);
        Check(quitText != null, "The existing Quit label is missing."); quitText.color = QuitRed;
        board.gameObject.SetActive(false);

        // Remove only the no-longer-visible wrapper from the existing support
        // list. Title, subtitle, Play and every legacy/null entry stay identical.
        SerializedObject menuData = new SerializedObject(menu);
        SerializedProperty hide = menuData.FindProperty("uiToHideWhileDropdownOpen");
        for (int i = hide.arraySize - 1; i >= 0; i--)
            if (hide.GetArrayElementAtIndex(i).objectReferenceValue == board.gameObject)
            {
                hide.GetArrayElementAtIndex(i).objectReferenceValue = null;
                hide.DeleteArrayElementAtIndex(i);
            }
        menuData.ApplyModifiedProperties();
        WireLayout(layout, root, board.gameObject, dropdown.gameObject);
        layout.centeredIntro = true; layout.authoredVersion = 1;
        EditorUtility.SetDirty(layout); EditorUtility.SetDirty(menu);
    }

    private static void WireLayout(MainMenuPresentationLayout layout, Transform canvasRoot, GameObject board, GameObject dropdown)
    {
        layout.canvasRect = canvasRoot as RectTransform;
        layout.splashBoard = board.transform as RectTransform;
        layout.splashSurface = Find(board.transform, "MainMenuHeroPaper");
        layout.title = Find(canvasRoot, "Civil Craft"); layout.edition = Find(canvasRoot, "txt_Subheader");
        layout.tagline = Find(board.transform, "MainMenuHeroTagline"); layout.footer = Find(board.transform, "MainMenuHeroFooter");
        layout.playButton = Find(canvasRoot, "btn_Play"); layout.playLabel = PrimaryLabel(layout.playButton.GetComponent<Button>()).rectTransform;
        layout.bridgeMotif = Find(board.transform, "MainMenuBridgeMotif");
        layout.tapeLeft = Find(board.transform, "MainMenuTapeLeft"); layout.tapeRight = Find(board.transform, "MainMenuTapeRight");
        layout.dropdownPanel = dropdown.transform as RectTransform; layout.dropdownSurface = Find(dropdown.transform, SurfaceName);
        layout.dropdownHeading = Find(dropdown.transform, "MainMenuBoardHeading"); layout.dropdownSubtitle = Find(dropdown.transform, "MainMenuBoardSubtitle");
        layout.modeButton = Find(dropdown.transform, "ModeSelectionButton"); layout.achievementButton = Find(dropdown.transform, "AchievementButton");
        layout.storeButton = Find(dropdown.transform, "StoreButton"); layout.achievementLocked = Find(dropdown.transform, "Achievements Locked Tile");
        layout.storeLocked = Find(dropdown.transform, "Store Locked Tile"); layout.settingsButton = Find(dropdown.transform, "SettingsButton");
        layout.loginButton = Find(dropdown.transform, "LoginButton"); layout.creditsButton = Find(dropdown.transform, "CreditsButton");
        layout.backButton = Find(dropdown.transform, "BackButton");
        layout.quitButton = Find(canvasRoot, "btn_Quit"); layout.versionLabel = Find(canvasRoot, "txt_version");
    }

    private static TMP_Text StyleButton(Button button, string caption, Color color, float fontSize, bool large)
    {
        Check(button != null, "Expected an existing Button.");
        RectTransform rect = button.transform as RectTransform;
        TMP_Text label = PrimaryLabel(button);
        Check(label != null, "Existing button label is missing: " + button.name);
        Image frame = Ensure<Image>(button.gameObject); frame.enabled = true; frame.sprite = rounded;
        frame.type = Image.Type.Sliced; frame.color = Ink; frame.raycastTarget = true;
        Image face = Surface(button.transform, "MainMenuButtonFace", color, false);
        Stretch(face.rectTransform, 5, 5, 5, 5); face.transform.SetAsFirstSibling();
        // Decorative label/icon graphics must never steal the parent's click.
        foreach (Graphic graphic in button.GetComponentsInChildren<Graphic>(true))
        {
            if (graphic.gameObject == button.gameObject || graphic == face || graphic == label) continue;
            graphic.enabled = false; graphic.raycastTarget = false;
        }
        label.enabled = true; label.text = caption; StyleText(label, fontSize, TextAlignmentOptions.Center, true);
        Stretch(label.rectTransform, 15, 9, 15, 9); label.transform.SetAsLastSibling();
        TMP_Text tileCaption = button.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(text => text.name == "Tile Caption");
        if (tileCaption != null && tileCaption.gameObject.activeSelf)
        {
            tileCaption.enabled = true;
            switch (button.name)
            {
                case "ModeSelectionButton": tileCaption.text = "Discover contracts & build bridges"; break;
                case "AchievementButton": tileCaption.text = "Milestones you've earned"; break;
                case "StoreButton": tileCaption.text = "Browse the shop"; break;
                case "SettingsButton": tileCaption.text = "Audio & display"; break;
                case "LoginButton": tileCaption.text = "Account & cloud saves"; break;
            }
            StyleText(tileCaption, 22, TextAlignmentOptions.Center, false);
            tileCaption.enableWordWrapping = true;
            tileCaption.rectTransform.anchorMin = new Vector2(.07f, .10f); tileCaption.rectTransform.anchorMax = new Vector2(.93f, .39f);
            tileCaption.rectTransform.offsetMin = tileCaption.rectTransform.offsetMax = Vector2.zero;
            label.rectTransform.anchorMin = new Vector2(.07f, .43f); label.rectTransform.anchorMax = new Vector2(.93f, .88f);
            label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
            tileCaption.transform.SetAsLastSibling();
        }
        if (large && button.name == "ModeSelectionButton")
        {
            label.rectTransform.anchorMin = new Vector2(.05f, .21f); label.rectTransform.anchorMax = new Vector2(.95f, .43f);
            label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
            RectTransform motif = Child(button.transform, "MainMenuModeBridgeMotif").transform as RectTransform;
            motif.anchorMin = new Vector2(.15f, .50f); motif.anchorMax = new Vector2(.85f, .88f);
            motif.offsetMin = motif.offsetMax = Vector2.zero;
            BuildBridgeMotif(motif);
            TMP_Text hint = tileCaption != null ? tileCaption : Label(button.transform, "Tile Caption", "Discover contracts & build bridges", 24);
            hint.enabled = true; hint.text = "Discover contracts & build bridges";
            hint.enableWordWrapping = true;
            hint.rectTransform.anchorMin = new Vector2(.08f, .04f); hint.rectTransform.anchorMax = new Vector2(.92f, .18f);
            hint.rectTransform.offsetMin = hint.rectTransform.offsetMax = Vector2.zero;
        }
        button.targetGraphic = face;
        ColorBlock colors = button.colors; colors.normalColor = Color.white;
        colors.highlightedColor = colors.selectedColor = new Color(1, .96f, .86f);
        colors.pressedColor = new Color(.83f, .74f, .6f); button.colors = colors;
        // Keep interactable, callbacks, navigation and activeSelf as authored.
        rect.localScale = Vector3.one;
        return label;
    }

    private static void StyleLockedTile(RectTransform rect)
    {
        Image frame = Ensure<Image>(rect.gameObject); frame.enabled = true; frame.sprite = rounded; frame.type = Image.Type.Sliced;
        frame.color = Ink; frame.raycastTarget = false;
        Image face = Surface(rect, "MainMenuLockedFace", Sand, false); Stretch(face.rectTransform, 5, 5, 5, 5); face.transform.SetAsFirstSibling();
        foreach (Graphic graphic in rect.GetComponentsInChildren<Graphic>(true))
        {
            graphic.raycastTarget = false;
            if (graphic.gameObject == rect.gameObject || graphic == face) continue;
            graphic.enabled = false;
        }
        string caption = rect.name.StartsWith("Achievements", StringComparison.Ordinal) ? "ACHIEVEMENTS" : "STORE";
        TMP_Text label = Label(rect, "Title", caption, 30);
        TMP_Text existingNote = rect.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(text => text.name == "Unlock Hint");
        TMP_Text note = Label(rect, "Unlock Hint", existingNote != null ? existingNote.text : "Unlock as you play", 23);
        note.enableWordWrapping = true;
        label.rectTransform.anchorMin = new Vector2(.06f, .43f); label.rectTransform.anchorMax = new Vector2(.94f, .78f);
        label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
        note.rectTransform.anchorMin = new Vector2(.06f, .18f); note.rectTransform.anchorMax = new Vector2(.94f, .43f);
        note.rectTransform.offsetMin = note.rectTransform.offsetMax = Vector2.zero;
        label.transform.SetAsLastSibling(); note.transform.SetAsLastSibling();
    }

    private static void BuildBridgeMotif(RectTransform parent)
    {
        Vector2[] points = { new Vector2(.05f, .15f), new Vector2(.23f, .66f), new Vector2(.5f, .87f), new Vector2(.77f, .66f), new Vector2(.95f, .15f) };
        for (int i = 0; i < 4; i++)
        {
            MotifLine(parent, "TrussUpper" + i, points[i], points[i + 1]);
            MotifLine(parent, "TrussDiagonal" + i, points[i], new Vector2(points[i + 1].x, .15f));
        }
        MotifLine(parent, "BridgeDeck", new Vector2(.05f, .15f), new Vector2(.95f, .15f));
        for (int i = 1; i < 4; i++) MotifLine(parent, "TrussUpright" + i, points[i], new Vector2(points[i].x, .15f));
    }

    private static void MotifLine(RectTransform parent, string name, Vector2 from, Vector2 to)
    {
        Image line = Surface(parent, name, Ink, false); line.sprite = null; line.type = Image.Type.Simple;
        RectTransform rect = line.rectTransform;
        Vector2 midpoint = (from + to) * .5f;
        rect.anchorMin = rect.anchorMax = midpoint; rect.pivot = new Vector2(.5f, .5f); rect.anchoredPosition = Vector2.zero;
        Vector2 size = new Vector2(360, 100), delta = Vector2.Scale(to - from, size);
        rect.sizeDelta = new Vector2(delta.magnitude, 5);
        rect.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
    }

    private static void ValidateReferences(MainMenuUIController menu, MainMenuPresentationLayout layout)
    {
        Check(layout != null && layout.authoredVersion == 1 && layout.transform.parent == null && layout.gameObject.activeSelf,
            "Presentation layout must be authored on an always-active scene-root manager.");
        Check(layout.splashBoard.parent == layout.dropdownPanel.parent && layout.dropdownPanel.IsChildOf(layout.canvasRect) &&
            layout.dropdownPanel.GetComponentInParent<Canvas>(true).rootCanvas.transform == layout.canvasRect,
            "Hero and dropdown must share the preserved MainPanel branch under the existing root Canvas.");
        Check(layout.playButton.GetComponent<Button>() != null, "The original Play action is missing.");
        if (layout.centeredIntro)
        {
            Check(!layout.splashBoard.gameObject.activeSelf && layout.playButton.parent == layout.dropdownPanel.parent &&
                layout.title.parent == layout.dropdownPanel.parent && layout.edition.parent == layout.dropdownPanel.parent,
                "Centered intro must be direct MainPanel text/Play controls with the recoverable paper board inactive.");
            RectTransform quitDialog = Find(layout.canvasRect, "quitConfirmationPanel");
            Check(quitDialog != null && quitDialog.parent == layout.playButton.parent &&
                new[] {layout.playButton,layout.title,layout.edition}.All(rect=>rect.GetSiblingIndex()<quitDialog.GetSiblingIndex()),
                "Centered intro would overdraw the existing quit confirmation.");
            Check(layout.title.GetComponent<TMP_Text>().text == "Civil Craft" && layout.title.GetComponent<TMP_Text>().color == Color.white &&
                layout.edition.GetComponent<TMP_Text>().text == "bridge edition" && layout.edition.GetComponent<TMP_Text>().color == Color.white &&
                PrimaryLabel(layout.playButton.GetComponent<Button>()).color == Color.white && layout.playButton.GetComponent<Image>().color.a == 0 &&
                PrimaryLabel(layout.quitButton.GetComponent<Button>()).color == QuitRed,
                "Centered intro typography, transparent Play target or red Quit text is incorrect.");
        }
        else
        {
            Check(layout.playButton.IsChildOf(layout.splashBoard), "Original Play is not on the hero board.");
            Check(layout.splashBoard.rect.width < layout.canvasRect.rect.width * .65f, "Hero board covers too much of the 3D scenery.");
        }
        SerializedObject data = new SerializedObject(menu); SerializedProperty hide = data.FindProperty("uiToHideWhileDropdownOpen");
        Check(Enumerable.Range(0, hide.arraySize).Count(i => hide.GetArrayElementAtIndex(i).objectReferenceValue == layout.splashBoard.gameObject) == (layout.centeredIntro ? 0 : 1),
            "Hero board hide-list membership does not match the chosen intro presentation.");
        Check(data.FindProperty("initialPlayButton").objectReferenceValue == layout.playButton.gameObject &&
            data.FindProperty("dropdownPanel").objectReferenceValue == layout.dropdownPanel, "Existing Play/dropdown references changed.");
        foreach (Graphic graphic in layout.splashBoard.GetComponentsInChildren<Graphic>(true))
            if (graphic.gameObject != layout.playButton.gameObject) Check(!graphic.raycastTarget, "Hero decoration captures input: " + graphic.name);
        foreach (TMP_Text text in layout.splashBoard.GetComponentsInChildren<TMP_Text>(true).Concat(layout.dropdownSurface.GetComponentsInChildren<TMP_Text>(true)))
            if (text.enabled) Check(text.font == font && !text.raycastTarget && !text.richText, "Menu typography differs from existing plain Bekind style: " + text.name);
        ValidateVisibleLabels(layout);
        Check(layout.achievementButton.GetSiblingIndex() > layout.achievementLocked.GetSiblingIndex() &&
            layout.storeButton.GetSiblingIndex() > layout.storeLocked.GetSiblingIndex(), "Unlocked features must paint over their existing placeholders.");
        RectTransform legacy = Find(layout.canvasRect, "Leaderboards");
        if (legacy != null) Check(!legacy.gameObject.activeSelf, "Legacy leaderboard control was unexpectedly enabled.");
    }

    private static void ValidateSavedSource()
    {
        string source = File.ReadAllText(ScenePath);
        foreach (string name in new[] { ManagerName, BoardName, SurfaceName, "MainMenuBridgeMotif" })
            Check(Regex.Matches(source, @"(?m)^  m_Name: " + name + @"\r?$").Count == 1, "Saved Main Menu must have one " + name + ".");
    }

    private static void Preview(MainMenuPresentationLayout source, StringBuilder report)
    {
        Scene preview = EditorSceneManager.NewPreviewScene(); RenderTexture target = null;
        try
        {
            GameObject root = new GameObject("Detached Main Menu Presentation", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            root.SetActive(false); SceneManager.MoveGameObjectToScene(root, preview);
            Canvas canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            GameObject backdrop = Child(root.transform, "Detached Existing Container Input Layer");
            Image backdropImage = backdrop.AddComponent<Image>(); backdropImage.color = new Color(.26f, .18f, .12f, .078f);
            backdropImage.raycastTarget = true; Stretch(backdrop.transform as RectTransform, 0, 0, 0, 0);
            backdrop.transform.SetAsFirstSibling();
            GameObject board = Object.Instantiate(source.splashBoard.gameObject, root.transform, false);
            GameObject dropdown = Object.Instantiate(source.dropdownPanel.gameObject, root.transform, false);
            if (source.centeredIntro)
            {
                Object.Instantiate(source.title.gameObject, root.transform, false);
                Object.Instantiate(source.edition.gameObject, root.transform, false);
                Object.Instantiate(source.playButton.gameObject, root.transform, false);
            }
            GameObject quit = Object.Instantiate(source.quitButton.gameObject, root.transform, false);
            GameObject version = Object.Instantiate(source.versionLabel.gameObject, root.transform, false);
            RectTransform sourceQuitDialog = Find(source.canvasRect, "quitConfirmationPanel");
            Check(sourceQuitDialog != null, "The existing quit confirmation is missing.");
            GameObject quitDialog = Object.Instantiate(sourceQuitDialog.gameObject, root.transform, false);
            quitDialog.SetActive(false);
            RectTransform sourceUpdates = Find(source.canvasRect, "MainMenuUpdatesButton");
            Check(sourceUpdates != null, "Existing Updates button is missing.");
            GameObject updates = Object.Instantiate(sourceUpdates.gameObject, root.transform, false);
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true)) transform.name = transform.name.Replace("(Clone)", "");
            foreach (MonoBehaviour script in root.GetComponentsInChildren<MonoBehaviour>(true))
                if (script != null && script.GetType().Namespace != "UnityEngine.UI" && script.GetType().Namespace != "TMPro") script.enabled = false;
            foreach (Button button in root.GetComponentsInChildren<Button>(true)) button.onClick = new Button.ButtonClickedEvent();
            GameObject manager = new GameObject("Detached Presentation Layout"); manager.SetActive(false); SceneManager.MoveGameObjectToScene(manager, preview);
            MainMenuPresentationLayout layout = manager.AddComponent<MainMenuPresentationLayout>(); layout.enabled = false;
            WireLayout(layout, root.transform, board, dropdown);
            layout.centeredIntro = source.centeredIntro;
            MainMenuUpdatesLayout updatesLayout = manager.AddComponent<MainMenuUpdatesLayout>(); updatesLayout.enabled = false;
            updatesLayout.entryButton = updates.transform as RectTransform;
            updatesLayout.badgeRect = Find(updates.transform, "NewUpdateBadge");
            updatesLayout.badgeRect.gameObject.SetActive(true);
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 30;
            GameObject cameraObject = new GameObject("Detached Presentation Camera", typeof(Camera)); SceneManager.MoveGameObjectToScene(cameraObject, preview);
            Camera camera = cameraObject.GetComponent<Camera>(); camera.scene = preview; camera.enabled = false;
            camera.orthographic = true; camera.transform.position = new Vector3(0, 0, -1000);
            camera.nearClipPlane = .1f; camera.farClipPlane = 2000; camera.cullingMask = 1 << 30;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(113, 139, 124, 255);
            canvas.worldCamera = camera;
            foreach (Canvas nested in root.GetComponentsInChildren<Canvas>(true)) nested.worldCamera = camera;
            Vector2Int[] sizes = { new Vector2Int(1280, 720), new Vector2Int(1024, 768), new Vector2Int(2340, 1080) };
            string[] names = { "phone", "tablet", "wide" };
            for (int i = 0; i < sizes.Length; i++)
            {
                Vector2Int size = sizes[i]; target = new RenderTexture(size.x, size.y, 24); target.Create();
                CanvasScaler sourceScaler = source.canvasRect.GetComponent<CanvasScaler>();
                Vector2 reference = sourceScaler != null ? sourceScaler.referenceResolution : new Vector2(1920, 1080);
                float match = sourceScaler != null ? sourceScaler.matchWidthOrHeight : .5f;
                float canvasScale = Mathf.Pow((float)size.x / reference.x, 1f - match) * Mathf.Pow((float)size.y / reference.y, match);
                Vector2 logicalSize = (Vector2)size / canvasScale;
                camera.targetTexture = target; camera.aspect = (float)size.x / size.y; camera.orthographicSize = logicalSize.y * .5f;
                RectTransform rootRect = root.transform as RectTransform; rootRect.sizeDelta = logicalSize;
                root.SetActive(true); board.SetActive(!layout.centeredIntro); dropdown.SetActive(false); quit.SetActive(true); version.SetActive(true);
                layout.title.gameObject.SetActive(true); layout.edition.gameObject.SetActive(true); layout.playButton.gameObject.SetActive(true);
                layout.ApplyLayoutForPreview(rootRect.rect); updatesLayout.ApplyLayout(); Canvas.ForceUpdateCanvases();
                ValidateHeroGeometry(layout, rootRect);
                ValidatePrimaryLabel(layout.playButton, layout.centeredIntro ? "Press To Play" : "PLAY"); ValidatePrimaryLabel(layout.quitButton, "QUIT");
                Check(!Bounds(layout.playButton, rootRect).Overlaps(Bounds(updatesLayout.entryButton, rootRect)), "Play overlaps Updates.");
                ValidateRaycast(root, camera, preview, layout.playButton, false);
                ValidateRaycast(root, camera, preview, updatesLayout.entryButton, true);
                Capture(camera, target, "Temp/main-menu-revamp-" + names[i] + "-title.png");
                if (layout.centeredIntro) ValidateQuitDialogPriority(root,camera,preview,layout.playButton,quitDialog);
                board.SetActive(false); dropdown.SetActive(true); quit.SetActive(false); version.SetActive(false);
                if (layout.centeredIntro) { layout.title.gameObject.SetActive(false); layout.edition.gameObject.SetActive(false); layout.playButton.gameObject.SetActive(false); }
                dropdown.GetComponent<CanvasGroup>().alpha = 1; dropdown.GetComponent<CanvasGroup>().interactable = dropdown.GetComponent<CanvasGroup>().blocksRaycasts = true;
                // Presentation fixture only: emulate both feature visibility
                // states without reading or changing PlayerDataManager.
                layout.achievementButton.gameObject.SetActive(true); layout.storeButton.gameObject.SetActive(true);
                layout.dropdownPanel.anchoredPosition = Vector2.zero;
                layout.ApplyLayoutForPreview(rootRect.rect); Canvas.ForceUpdateCanvases();
                ValidateDropdownGeometry(layout, rootRect);
                ValidateVisibleLabels(layout);
                Check(!Bounds(layout.dropdownPanel, rootRect).Overlaps(Bounds(updatesLayout.entryButton, rootRect)) &&
                    !Bounds(layout.dropdownPanel, rootRect).Overlaps(Bounds(updatesLayout.badgeRect, rootRect)), "Menu board covers Updates or its badge.");
                ValidateRaycast(root, camera, preview, layout.modeButton, true);
                ValidateRaycast(root, camera, preview, layout.achievementButton, true);
                ValidateRaycast(root, camera, preview, layout.storeButton, true);
                Capture(camera, target, "Temp/main-menu-revamp-" + names[i] + "-menu.png");
                layout.achievementButton.gameObject.SetActive(false); layout.storeButton.gameObject.SetActive(false);
                ValidateLockedLabel(layout.achievementLocked, "ACHIEVEMENTS", "LOCKED\nEarn your first achievement");
                ValidateLockedLabel(layout.storeLocked, "STORE", "LOCKED\nUnlock through Story");
                Capture(camera, target, "Temp/main-menu-revamp-" + names[i] + "-locked.png");
                report.AppendLine("PASS: " + names[i] + " " + size.x + "x" + size.y + " centered title/plain Play fit without a visible paper container; red Quit text and native clicks pass; dropdown/locked labels remain visible and nonoverlapping.");
                camera.targetTexture = null; Release(target); target = null;
            }
        }
        finally { Release(target); EditorSceneManager.ClosePreviewScene(preview); }
    }

    private static void ValidateHeroGeometry(MainMenuPresentationLayout layout, RectTransform parent)
    {
        if (layout.centeredIntro)
        {
            Check(!layout.splashBoard.gameObject.activeSelf, "Centered intro unexpectedly shows the old paper container.");
            foreach (RectTransform rect in new[] { layout.title, layout.edition, layout.playButton, layout.quitButton, layout.versionLabel })
                Inside(rect, parent, "Centered intro control leaves the safe bounds: " + rect.name);
            foreach (RectTransform rect in new[] { layout.title, layout.edition, layout.playButton })
                Check(Mathf.Abs(Bounds(rect,parent).center.x-parent.rect.center.x)<.5f, "Intro control is not centered: " + rect.name);
            NonOverlap(new[] { layout.title,layout.edition,layout.playButton },parent);
            Check(layout.playButton.rect.width < parent.rect.width*.65f && layout.playButton.rect.height < parent.rect.height*.2f,
                "Plain Press To Play still has a fullscreen hit target.");
            foreach (RectTransform rect in new[] { layout.title,layout.edition,layout.playButton,layout.quitButton }) AssertTextFits(rect);
            return;
        }
        Inside(layout.splashBoard, parent, "Hero paper leaves safe bounds.");
        Inside(layout.quitButton, parent, "Quit leaves safe bounds. " + Bounds(layout.quitButton, parent) + " / " + parent.rect);
        Inside(layout.versionLabel, parent, "Version label leaves safe bounds.");
        foreach (RectTransform rect in new[] { layout.title, layout.edition, layout.tagline, layout.playButton, layout.footer, layout.bridgeMotif })
            Inside(rect, layout.splashSurface, "Hero item leaves the paper: " + rect.name);
        RectTransform[] items = { layout.title, layout.edition, layout.tagline, layout.playButton, layout.footer, layout.bridgeMotif };
        NonOverlap(items, layout.splashSurface);
        Check(layout.splashBoard.rect.width < parent.rect.width * .65f, "Hero board hides the 3D scenery.");
        Check(layout.playButton.rect.width < parent.rect.width * .45f && layout.playButton.rect.height < parent.rect.height * .2f,
            "Play still acts as a fullscreen hit target.");
        AssertTextFits(layout.splashBoard);
    }

    private static void ValidateDropdownGeometry(MainMenuPresentationLayout layout, RectTransform parent)
    {
        Inside(layout.dropdownPanel, parent, "Dropdown board leaves safe bounds.");
        RectTransform[] items = { layout.dropdownHeading, layout.dropdownSubtitle, layout.modeButton, layout.achievementButton,
            layout.storeButton, layout.settingsButton, layout.loginButton, layout.creditsButton, layout.backButton };
        foreach (RectTransform rect in items) Inside(rect, layout.dropdownSurface, "Dropdown item leaves the paper: " + rect.name);
        NonOverlap(items, layout.dropdownSurface);
        Rect a = Bounds(layout.achievementButton, layout.dropdownSurface), al = Bounds(layout.achievementLocked, layout.dropdownSurface);
        Rect s = Bounds(layout.storeButton, layout.dropdownSurface), sl = Bounds(layout.storeLocked, layout.dropdownSurface);
        Check(Mathf.Abs(a.x-al.x)<1 && Mathf.Abs(a.y-al.y)<1 && Mathf.Abs(a.width-al.width)<1 && Mathf.Abs(a.height-al.height)<1 &&
            Mathf.Abs(s.x-sl.x)<1 && Mathf.Abs(s.y-sl.y)<1 && Mathf.Abs(s.width-sl.width)<1 && Mathf.Abs(s.height-sl.height)<1,
            "Locked placeholders do not share their existing feature tile positions.");
        AssertTextFits(layout.dropdownSurface);
    }

    private static void AssertTextFits(RectTransform root)
    {
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>())
        {
            if (!text.enabled || string.IsNullOrWhiteSpace(text.text)) continue;
            text.ForceMeshUpdate();
            Check(!text.isTextOverflowing, "Main Menu text overflows: " + text.name + " / " + text.text);
        }
    }

    private static TMP_Text PrimaryLabel(Button button)
    {
        if (button == null) return null;
        TMP_Text[] labels = button.GetComponentsInChildren<TMP_Text>(true).Where(text => text.gameObject.activeSelf &&
            text.name != "Tile Caption" && text.name != "Tile Kicker" && text.name != "Unlock Hint" &&
            text.name != "MainMenuModeHint" && text.name != "MainMenuLockedNote").ToArray();
        Check(labels.Length <= 1, "Functional button has duplicate primary labels: " + button.name);
        // The original label remains selectable even if a previous presentation
        // pass disabled its Graphic; activeSelf must never be changed to repair it.
        TMP_Text original = labels.FirstOrDefault(text => text.name == "Text (TMP)" || text.name == "Text" || text.name == "Title");
        return original != null ? original : labels.SingleOrDefault();
    }

    private static void ValidatePrimaryLabel(RectTransform button, string expected)
    {
        TMP_Text label = PrimaryLabel(button != null ? button.GetComponent<Button>() : null);
        Check(label != null && label.enabled && label.gameObject.activeSelf && label.text == expected,
            "Functional button has a missing, disabled or incorrect primary label: " + button?.name + " / " + label?.text);
    }

    private static void ValidateLockedLabel(RectTransform tile, string title, string hint)
    {
        TMP_Text titleText = Find(tile, "Title")?.GetComponent<TMP_Text>();
        TMP_Text hintText = Find(tile, "Unlock Hint")?.GetComponent<TMP_Text>();
        Check(titleText != null && titleText.enabled && titleText.gameObject.activeSelf && titleText.text == title &&
            hintText != null && hintText.enabled && hintText.gameObject.activeSelf && hintText.text == hint,
            "Locked feature title or instructions are missing: " + tile?.name);
    }

    private static void ValidateVisibleLabels(MainMenuPresentationLayout layout)
    {
        ValidatePrimaryLabel(layout.playButton, layout.centeredIntro ? "Press To Play" : "PLAY"); ValidatePrimaryLabel(layout.quitButton, "QUIT");
        ValidatePrimaryLabel(layout.modeButton, "PLAY & EXPLORE");
        ValidatePrimaryLabel(layout.achievementButton, "ACHIEVEMENTS"); ValidatePrimaryLabel(layout.storeButton, "STORE");
        ValidatePrimaryLabel(layout.settingsButton, "SETTINGS"); ValidatePrimaryLabel(layout.loginButton, "ACCOUNT");
        ValidatePrimaryLabel(layout.creditsButton, "CREDITS"); ValidatePrimaryLabel(layout.backButton, "BACK");
        ValidateLockedLabel(layout.achievementLocked, "ACHIEVEMENTS", "LOCKED\nEarn your first achievement");
        ValidateLockedLabel(layout.storeLocked, "STORE", "LOCKED\nUnlock through Story");
    }

    private static void NonOverlap(RectTransform[] items, RectTransform parent)
    {
        for (int i = 0; i < items.Length; i++)
            for (int j = i + 1; j < items.Length; j++)
                Check(!Bounds(items[i], parent).Overlaps(Bounds(items[j], parent)), "Menu items overlap: " + items[i].name + " / " + items[j].name);
    }

    private static void ValidateRaycast(GameObject root, Camera camera, Scene preview, RectTransform button, bool menu)
    {
        Canvas.ForceUpdateCanvases();
        foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>()) { graphic.SetAllDirty(); graphic.Rebuild(CanvasUpdate.PreRender); }
        Canvas.ForceUpdateCanvases(); RenderDetached(camera, camera.targetTexture); Canvas.ForceUpdateCanvases();
        GameObject eventObject = new GameObject("Detached Menu Raycast Event System"); eventObject.SetActive(false);
        EventSystem events = eventObject.AddComponent<EventSystem>(); SceneManager.MoveGameObjectToScene(eventObject, preview);
        try
        {
            var data = new PointerEventData(events) { position = camera.WorldToScreenPoint(button.position) };
            var results = new List<RaycastResult>();
            foreach (GraphicRaycaster raycaster in root.GetComponentsInChildren<GraphicRaycaster>()) raycaster.Raycast(data, results);
            Check(results.Count > 0, "Existing menu button receives no native raycast: " + button.name);
            RaycastResult first = results.OrderByDescending(hit => hit.sortingOrder).ThenByDescending(hit => hit.depth).First();
            Check(first.gameObject == button.gameObject, "A decoration or wrong control steals " + button.name + " clicks: " + first.gameObject.name);
            if (!menu)
            {
                results.Clear(); data.position = new Vector2(camera.pixelWidth * .9f, camera.pixelHeight * .5f);
                foreach (GraphicRaycaster raycaster in root.GetComponentsInChildren<GraphicRaycaster>()) raycaster.Raycast(data, results);
                Check(!results.Any(hit => hit.gameObject == button.gameObject), "Empty scenery still activates fullscreen Play.");
            }
        }
        finally { Object.DestroyImmediate(eventObject); }
    }

    private static void ValidateQuitDialogPriority(GameObject root,Camera camera,Scene preview,RectTransform play,GameObject dialog)
    {
        dialog.SetActive(true);Canvas.ForceUpdateCanvases();
        foreach(Graphic graphic in root.GetComponentsInChildren<Graphic>()){graphic.SetAllDirty();graphic.Rebuild(CanvasUpdate.PreRender);}
        RenderDetached(camera,camera.targetTexture);Canvas.ForceUpdateCanvases();
        GameObject eventObject=new GameObject("Detached Quit Dialog Event System");eventObject.SetActive(false);
        EventSystem events=eventObject.AddComponent<EventSystem>();SceneManager.MoveGameObjectToScene(eventObject,preview);
        try
        {
            var data=new PointerEventData(events){position=camera.WorldToScreenPoint(play.position)};
            var hits=new List<RaycastResult>();
            foreach(GraphicRaycaster raycaster in root.GetComponentsInChildren<GraphicRaycaster>())raycaster.Raycast(data,hits);
            Check(hits.Count>0,"Quit dialog has no native input cover.");
            RaycastResult first=hits.OrderByDescending(hit=>hit.sortingOrder).ThenByDescending(hit=>hit.depth).First();
            Check(first.gameObject==dialog||first.gameObject.transform.IsChildOf(dialog.transform),"Plain Play captures a click through the existing quit confirmation.");
        }
        finally{Object.DestroyImmediate(eventObject);dialog.SetActive(false);}
    }

    private sealed class Snapshot
    {
        private readonly int[] roots;
        private readonly List<KeyValuePair<Object,string>> serialized = new List<KeyValuePair<Object,string>>();
        private readonly Dictionary<Button,string> callbacks;
        private readonly Dictionary<GameObject,bool> active;
        private readonly MainMenuUIController menu;
        private readonly string configuration;
        private readonly GameObject[] hideList;
        private readonly RectTransform dropdown;
        private readonly Transform dropdownParent;
        private readonly string dropdownPresentation;
        public Snapshot(Scene scene, GameObject canvas, GameObject manager, MainMenuUIController menu)
        {
            roots = scene.GetRootGameObjects().Select(root => root.GetInstanceID()).ToArray(); this.menu = menu;
            configuration = WithoutHideList(JsonUtility.ToJson(menu));
            dropdown = new SerializedObject(menu).FindProperty("dropdownPanel").objectReferenceValue as RectTransform;
            dropdownParent = dropdown != null ? dropdown.parent : null;
            dropdownPresentation = PresentationSignature(dropdown);
            SerializedProperty property = new SerializedObject(menu).FindProperty("uiToHideWhileDropdownOpen");
            hideList = Enumerable.Range(0,property.arraySize).Select(i => property.GetArrayElementAtIndex(i).objectReferenceValue as GameObject).ToArray();
            callbacks = Components<Button>(scene).ToDictionary(button => button, button => JsonUtility.ToJson(button.onClick));
            active = Components<Transform>(scene).ToDictionary(transform => transform.gameObject, transform => transform.gameObject.activeSelf);
            Capture(canvas); if (manager != null) Capture(manager);
            serialized.Add(new KeyValuePair<Object,string>(menu, EditorJsonUtility.ToJson(menu)));
        }
        private void Capture(GameObject root)
        {
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                serialized.Add(new KeyValuePair<Object,string>(transform.gameObject,EditorJsonUtility.ToJson(transform.gameObject)));
                foreach(Component component in transform.GetComponents<Component>()) if(component != null)
                    serialized.Add(new KeyValuePair<Object,string>(component,EditorJsonUtility.ToJson(component)));
            }
        }
        public void VerifyPreserved(MainMenuUIController current, GameObject board, bool centered)
        {
            Check(WithoutHideList(JsonUtility.ToJson(current)) == configuration, "Menu/authentication/feature/animation configuration changed.");
            Check(dropdown != null && dropdown.parent == dropdownParent, "Existing dropdown hierarchy changed.");
            SerializedProperty property = new SerializedObject(current).FindProperty("uiToHideWhileDropdownOpen");
            GameObject[] now = Enumerable.Range(0,property.arraySize).Select(i => property.GetArrayElementAtIndex(i).objectReferenceValue as GameObject).ToArray();
            GameObject[] expected = centered ? hideList.Where(entry=>entry!=board).ToArray() : hideList.Contains(board) ? hideList : hideList.Concat(new[] {board}).ToArray();
            Check(expected.SequenceEqual(now), "Existing menu hide-list entries changed.");
            Check(PresentationSignature(dropdown)==dropdownPresentation, "The existing dropdown presentation changed during intro-only authoring.");
            foreach (var pair in callbacks) Check(pair.Key != null && JsonUtility.ToJson(pair.Key.onClick)==pair.Value,"Existing callback changed: " + pair.Key?.name);
            foreach (var pair in active) Check(pair.Key != null && pair.Key.activeSelf==(centered && pair.Key==board ? false : pair.Value),"Existing initial active state changed: " + pair.Key?.name);
        }
        public void VerifyRollback(Scene scene)
        {
            Check(roots.SequenceEqual(scene.GetRootGameObjects().Select(root => root.GetInstanceID())), "Rollback roots differ; scene remains loaded and unsaved.");
            foreach (var pair in serialized) Check(pair.Key != null && EditorJsonUtility.ToJson(pair.Key)==pair.Value,"Rollback differs: " + pair.Key?.name + "; scene remains unsaved.");
        }
    }

    private static string PresentationSignature(RectTransform root)
    {
        if (root == null) return "missing";
        var signature = new StringBuilder();
        foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(true))
        {
            signature.Append(graphic.GetInstanceID()).Append('|').Append(graphic.GetType().Name).Append('|')
                .Append(graphic.enabled).Append('|').Append(graphic.raycastTarget).Append('|').Append(graphic.color).Append('|');
            if (graphic is Image image) signature.Append(image.sprite != null ? image.sprite.GetInstanceID() : 0)
                .Append('|').Append(image.type).Append('|').Append(image.preserveAspect).Append('|');
            if (graphic is TMP_Text text) signature.Append(text.text).Append('|').Append(text.font != null ? text.font.GetInstanceID() : 0)
                .Append('|').Append(text.fontSharedMaterial != null ? text.fontSharedMaterial.GetInstanceID() : 0)
                .Append('|').Append(text.fontStyle).Append('|').Append(text.richText).Append('|').Append(text.enableWordWrapping)
                .Append('|').Append(text.alignment).Append('|').Append(text.overflowMode).Append('|');
            signature.AppendLine();
        }
        return signature.ToString();
    }

    private static string WithoutHideList(string json) => Regex.Replace(json, "\"uiToHideWhileDropdownOpen\":\\[[^\\]]*\\]", "\"uiToHideWhileDropdownOpen\":[]");
    private static void ClearVerifiedDirtiness(Scene scene)
    {
        var method = typeof(EditorSceneManager).GetMethod("ClearSceneDirtiness",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic,null,new[]{typeof(Scene)},null);
        Check(method != null, "Verified rollback clean-state operation unavailable; scene left loaded.");
        method.Invoke(null,new object[]{scene}); Check(!scene.isDirty,"Verified rollback remains dirty; scene left loaded.");
    }
    private static IEnumerable<T> Components<T>(Scene scene) where T:Component => scene.GetRootGameObjects().SelectMany(root=>root.GetComponentsInChildren<T>(true));
    private static RectTransform Find(Transform root,string name) => root.GetComponentsInChildren<RectTransform>(true).SingleOrDefault(rect=>rect.name==name);
    private static GameObject Child(Transform parent,string name)
    {
        Transform existing=parent.Find(name); if(existing!=null)return existing.gameObject;
        GameObject obj=new GameObject(name,typeof(RectTransform),typeof(CanvasRenderer));obj.layer=parent.gameObject.layer;
        SceneManager.MoveGameObjectToScene(obj,parent.gameObject.scene);obj.transform.SetParent(parent,false);
        if(recordingUndo)Undo.RegisterCreatedObjectUndo(obj,"Create Menu Presentation");return obj;
    }
    private static T Ensure<T>(GameObject obj) where T:Component => obj.GetComponent<T>() ?? (recordingUndo?Undo.AddComponent<T>(obj):obj.AddComponent<T>());
    private static void Reparent(RectTransform rect,Transform parent)
    {
        if(rect.parent!=parent) { if(recordingUndo)Undo.SetTransformParent(rect,parent,"Move Existing Menu Control");else rect.SetParent(parent,false); }
        rect.localRotation=Quaternion.identity;rect.localScale=Vector3.one;
    }
    private static Image Surface(Transform parent,string name,Color color,bool raycast)
    {
        Image image=Ensure<Image>(Child(parent,name));image.enabled=true;image.sprite=rounded;image.type=Image.Type.Sliced;image.color=color;image.raycastTarget=raycast;return image;
    }
    private static TMP_Text Label(Transform parent,string name,string value,float size)
    {
        TMP_Text text=Ensure<TextMeshProUGUI>(Child(parent,name));text.enabled=true;text.text=value;StyleText(text,size,TextAlignmentOptions.Center,false);return text;
    }
    private static void StyleText(TMP_Text text,float size,TextAlignmentOptions alignment,bool bold)
    {
        Check(text!=null,"Existing menu TMP label is missing.");text.font=font;text.fontSharedMaterial=font.material;text.color=Ink;
        text.richText=false;text.raycastTarget=false;text.enableAutoSizing=true;text.fontSize=size;text.fontSizeMax=size;text.fontSizeMin=size*.78f;
        text.fontStyle=bold?FontStyles.Bold:FontStyles.Normal;text.alignment=alignment;text.enableWordWrapping=false;
        text.characterSpacing=0;text.wordSpacing=0;text.lineSpacing=0;text.paragraphSpacing=0;
        text.margin=Vector4.zero;text.overflowMode=TextOverflowModes.Ellipsis;
    }
    private static void Stretch(RectTransform rect,float left,float bottom,float right,float top)
    {
        rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.pivot=Vector2.one*.5f;
        rect.offsetMin=new Vector2(left,bottom);rect.offsetMax=new Vector2(-right,-top);rect.localScale=Vector3.one;
    }
    private static Rect Bounds(RectTransform target,RectTransform parent)
    {
        Vector3[] corners=new Vector3[4];target.GetWorldCorners(corners);Vector2 min=Vector2.positiveInfinity,max=Vector2.negativeInfinity;
        foreach(Vector3 corner in corners){Vector2 point=parent.InverseTransformPoint(corner);min=Vector2.Min(min,point);max=Vector2.Max(max,point);}
        return Rect.MinMaxRect(min.x,min.y,max.x,max.y);
    }
    private static void Inside(RectTransform target,RectTransform parent,string message)
    {
        Check(target!=null&&parent!=null,message);Rect b=Bounds(target,parent),a=parent.rect;
        Check(b.xMin>=a.xMin-.5f&&b.yMin>=a.yMin-.5f&&b.xMax<=a.xMax+.5f&&b.yMax<=a.yMax+.5f,message);
    }
    private static void Capture(Camera camera,RenderTexture target,string path)
    {
        RenderDetached(camera,target);RenderTexture previous=RenderTexture.active;Texture2D image=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
        try{RenderTexture.active=target;image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());}
        finally{RenderTexture.active=previous;Object.DestroyImmediate(image);}
    }
    private static void RenderDetached(Camera camera,RenderTexture target)
    {
        Check(target!=null,"Detached preview target is missing.");
        if(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset)RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});else camera.Render();
    }
    private static void Release(RenderTexture target){if(target!=null){target.Release();Object.DestroyImmediate(target);}}
    private static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
#endif
