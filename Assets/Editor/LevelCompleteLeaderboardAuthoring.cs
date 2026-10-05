#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Opt-in, native Unity authoring of the completion leaderboard and isolated UI previews.</summary>
public static class LevelCompleteLeaderboardAuthoring
{
    private const string SourceScene = "Assets/Scenes/Mode Selection.unity";
    private const string ManagerPrefab = "Assets/Prefabs/BuildingMode/MANAGERS AND CANVASES.prefab";
    private const string Request = "Temp/level-complete-leaderboard.request";
    private const string Report = "Temp/LevelCompleteLeaderboardValidation.txt";
    private const string PreviewDirectory = "Temp/LevelCompleteLeaderboardPreviews";
    private const string CanvasName = "Completed Location Leaderboard Canvas";
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly string[] Targets = {
        "Assets/Scenes/CanyonCrossing.unity", "Assets/Scenes/BHAN HOUSE.unity",
        "Assets/Scenes/Multiplayer/Multiplayer.unity"
    };
    private static double nextCheck;

    [InitializeOnLoadMethod]
    private static void Watch()
    {
        EditorApplication.update -= CheckRequest;
        EditorApplication.update += CheckRequest;
    }

    private static void CheckRequest()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(Request)) return;
        string waiting = UnsafeReason();
        if (waiting != null)
        {
            File.WriteAllText(Report, "WAIT: " + waiting + ". No unsaved edits overwritten.");
            return;
        }
        File.Delete(Request);
        try { Author(); }
        catch (Exception error)
        {
            File.AppendAllText(Report, "FAIL: " + error + "\n");
            Debug.LogException(error);
        }
    }

    private static string UnsafeReason()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "Stop Play Mode before authoring";
        if (BuildPipeline.isBuildingPlayer) return "Wait for the current player build";
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return "Wait for Unity compilation/import";
        foreach (string path in Targets.Concat(new[] { SourceScene }))
        {
            Scene loaded = SceneManager.GetSceneByPath(path);
            if (loaded.IsValid() && loaded.isLoaded && loaded.isDirty) return "Save scene edits in " + path;
        }
        var stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage != null && stage.assetPath == ManagerPrefab)
            return "Close the manager prefab stage before authoring";
        return null;
    }

    [MenuItem("Tools/Civil Craft/Add Level Complete Leaderboard")]
    public static void Author()
    {
        Check(UnsafeReason() == null, UnsafeReason());
        Check(typeof(LevelCompleteManager).GetMethod("OpenCompletedLocationLeaderboard") != null,
            "The completed-location manager API is not compiled yet.");
        Check(FindOverlayType() != null, "The completion leaderboard overlay component is not compiled yet.");
        var report = new StringBuilder();
        File.WriteAllText(Report, "RUNNING: Native Unity completion leaderboard authoring.\n");
        Directory.CreateDirectory(PreviewDirectory);
        Scene active = SceneManager.GetActiveScene();
        Scene sourceScene = SceneManager.GetSceneByPath(SourceScene);
        bool openedSource = !sourceScene.IsValid() || !sourceScene.isLoaded;
        if (openedSource) sourceScene = EditorSceneManager.OpenScene(SourceScene, OpenSceneMode.Additive);
        try
        {
            LeaderboardPanelUI source = InScene<LeaderboardPanelUI>(sourceScene).Single();
            GameObject sourcePanel = Get<GameObject>(source, "panel");
            Check(sourcePanel != null && sourcePanel.GetComponent<CozyModalLayout>() != null,
                "The current authored Mode Selection leaderboard modal is missing.");
            string sourceState = EditorJsonUtility.ToJson(source);
            GameObject prefab = PrefabUtility.LoadPrefabContents(ManagerPrefab);
            try
            {
                LevelCompleteManager manager = prefab.GetComponentsInChildren<LevelCompleteManager>(true).Single();
                AuthorManager(manager, source, report);
                Preview(manager, "Prefab", report);
                Check(PrefabUtility.SaveAsPrefabAsset(prefab, ManagerPrefab) != null, "Could not save manager prefab.");
                report.AppendLine("SAVED: " + ManagerPrefab);
                File.WriteAllText(Report, report.ToString());
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }

            foreach (string path in Targets)
            {
                Scene scene = SceneManager.GetSceneByPath(path);
                bool opened = !scene.IsValid() || !scene.isLoaded;
                if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                Undo.IncrementCurrentGroup();
                int undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Add Level Complete Leaderboard");
                try
                {
                    LevelCompleteManager manager = InScene<LevelCompleteManager>(scene).Single();
                    AuthorManager(manager, source, report);
                    Preview(manager, scene.name.Replace(' ', '_'), report);
                    EditorSceneManager.MarkSceneDirty(scene);
                    Check(EditorSceneManager.SaveScene(scene), "Could not save " + path);
                    report.AppendLine("SAVED: " + path);
                    File.WriteAllText(Report, report.ToString());
                    Undo.CollapseUndoOperations(undoGroup);
                }
                catch { Undo.RevertAllDownToGroup(undoGroup); throw; }
                finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
            }
            Check(EditorJsonUtility.ToJson(source) == sourceState && !sourceScene.isDirty,
                "The source Mode Selection controller changed during authoring.");
            report.AppendLine("PASS: Mode Selection source unchanged. Only its authored modal was cloned; no source Canvas or mode-selection controls copied.");
            report.AppendLine("PASS: New button calls only OpenCompletedLocationLeaderboard. Original Retry/Save callbacks retained; modal close has no result save/retry/scene action.");
            report.AppendLine("NOTE: Preview data was temporary. No PlayFab calls, player saves, purchases or multiplayer sessions were used.");
            File.WriteAllText(Report, report.ToString());
            Debug.Log("[Level Complete Leaderboard] Saved all completion buttons and local leaderboard modals; isolated UI checks passed.");
        }
        finally
        {
            if (openedSource) EditorSceneManager.CloseScene(sourceScene, true);
            if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
        }
    }

    private static void AuthorManager(LevelCompleteManager manager, LeaderboardPanelUI source, StringBuilder report)
    {
        Check(manager.levelCompletePanel != null, "Completion panel is missing.");
        Transform paper = manager.levelCompletePanel.transform.Find("Completion Safe Area/Wood Frame/Cream Panel");
        Check(paper != null, "The authored completion paper is missing.");
        bool useUndo = !EditorSceneManager.IsPreviewScene(manager.gameObject.scene);
        if (useUndo)
        {
            Undo.RecordObject(manager, "Connect completion leaderboard");
            Undo.RecordObjects(paper.GetComponentsInChildren<RectTransform>(true), "Fit completion footer actions");
        }
        var originalActions = paper.GetComponentsInChildren<Button>(true)
            .Where(button => button.name != "Leaderboard")
            .ToDictionary(button => button, button => EditorJsonUtility.ToJson(button));
        var serialized = new SerializedObject(manager);
        SerializedProperty boardReference = serialized.FindProperty("completedLocationLeaderboard");
        Check(boardReference != null, "The completion leaderboard reference field is not compiled yet.");
        LeaderboardPanelUI board = boardReference.objectReferenceValue as LeaderboardPanelUI;
        if (board == null)
        {
            Check(!manager.transform.root.GetComponentsInChildren<Transform>(true).Any(node => node.name == CanvasName),
                "An unbound completed-location leaderboard Canvas already exists; inspect before replacing it.");
            board = CloneModal(manager, source, useUndo);
            boardReference.objectReferenceValue = board;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        ValidateBoard(board, manager.levelCompletePanel, source, report);

        Button retry = paper.Find("Retry")?.GetComponent<Button>() ?? paper.Find("Back to Build")?.GetComponent<Button>();
        Button save = paper.Find("Save & Continue")?.GetComponent<Button>() ?? paper.Find("Save for Session")?.GetComponent<Button>();
        Check(retry != null && save != null, "The existing completion actions are missing.");
        Place(paper.Find("Feedback") as RectTransform, .025f, .025f, .355f, .11f);
        Place(retry.transform as RectTransform, .57f, .025f, .70f, .11f);
        Place(save.transform as RectTransform, .72f, .025f, .975f, .11f);
        RectTransform guest = paper.Find("Guest Waiting") as RectTransform;
        if (guest != null) Place(guest, .57f, .025f, .975f, .11f);
        Button open = paper.Find("Leaderboard")?.GetComponent<Button>();
        if (open == null)
        {
            var rect = new GameObject("Leaderboard", typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<RectTransform>();
            rect.gameObject.layer = 5;
            rect.SetParent(paper, false);
            Image image = rect.GetComponent<Image>();
            Image existingImage = retry.targetGraphic as Image;
            Check(existingImage != null && existingImage.sprite != null, "Completion rounded button artwork is missing.");
            EditorUtility.CopySerialized(existingImage, image);
            image.raycastTarget = true;
            open = rect.GetComponent<Button>();
            open.targetGraphic = image;
            open.colors = retry.colors;
            open.transition = retry.transition;
            open.navigation = new Navigation { mode = Navigation.Mode.None };
            rect.gameObject.AddComponent<CompletionButtonMotion>();
            TMP_Text originalLabel = retry.GetComponentInChildren<TMP_Text>(true);
            Check(originalLabel != null, "Completion action font is missing.");
            TMP_Text label = Object.Instantiate(originalLabel, rect, false);
            label.name = "Label";
            label.text = "Leaderboard";
            label.enableAutoSizing = true;
            label.fontSize = label.fontSizeMax = 30f;
            label.fontSizeMin = 22f;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;
            var action = (UnityAction)Delegate.CreateDelegate(typeof(UnityAction), manager,
                typeof(LevelCompleteManager).GetMethod("OpenCompletedLocationLeaderboard"));
            UnityEventTools.AddPersistentListener(open.onClick, action);
            if (useUndo) Undo.RegisterCreatedObjectUndo(rect.gameObject, "Add completion leaderboard button");
        }
        Place(open.transform as RectTransform, .375f, .025f, .55f, .11f);
        Check(open.onClick.GetPersistentEventCount() == 1 && open.onClick.GetPersistentTarget(0) == manager &&
            open.onClick.GetPersistentMethodName(0) == "OpenCompletedLocationLeaderboard", "Leaderboard button callback is incorrect.");
        foreach (var action in originalActions)
        {
            // RectTransform layout is separate from the unchanged Button serialization.
            Check(EditorJsonUtility.ToJson(action.Key) == action.Value, "Existing completion action changed: " + action.Key.name);
        }
        EditorUtility.SetDirty(manager);
        report.AppendLine("PASS: " + manager.gameObject.scene.name + " footer callbacks preserved; local board reference and exact manager callback wired.");
    }

    private static LeaderboardPanelUI CloneModal(LevelCompleteManager manager, LeaderboardPanelUI source, bool useUndo)
    {
        Canvas resultCanvas = manager.levelCompletePanel.GetComponentInParent<Canvas>(true);
        Check(resultCanvas != null, "Completion Canvas is missing.");
        var canvasObject = new GameObject(CanvasName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.layer = 5;
        SceneManager.MoveGameObjectToScene(canvasObject, manager.gameObject.scene);
        canvasObject.transform.SetParent(manager.transform.root, false);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingLayerID = resultCanvas.sortingLayerID;
        Check(resultCanvas.sortingOrder < short.MaxValue, "Completion Canvas already uses the maximum sorting order.");
        canvas.sortingOrder = Math.Min(short.MaxValue, resultCanvas.sortingOrder + 20);
        canvas.overrideSorting = true;
        canvas.enabled = true;
        CanvasScaler resultScaler = resultCanvas.GetComponent<CanvasScaler>();
        if (resultScaler != null) EditorUtility.CopySerialized(resultScaler, canvasObject.GetComponent<CanvasScaler>());
        else
        {
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
        }
        GameObject sourcePanel = Get<GameObject>(source, "panel");
        GameObject panel = Object.Instantiate(sourcePanel, canvasObject.transform, false);
        panel.name = "Completed Location Leaderboard Panel";
        panel.SetActive(false);
        var board = canvasObject.AddComponent<LeaderboardPanelUI>();
        EditorUtility.CopySerialized(source, board);
        var map = BuildObjectMap(sourcePanel, panel);
        map[source] = board;
        Remap(board, map);
        foreach (Component component in panel.GetComponentsInChildren<Component>(true))
            if (component != null) Remap(component, map);
        var data = new SerializedObject(board);
        data.FindProperty("openButton").objectReferenceValue = null;
        data.ApplyModifiedPropertiesWithoutUndo();
        Type overlayType = FindOverlayType();
        if (panel.GetComponent(overlayType) == null) panel.AddComponent(overlayType);
        if (useUndo) Undo.RegisterCreatedObjectUndo(canvasObject, "Add completion leaderboard modal");
        return board;
    }

    private static Dictionary<Object, Object> BuildObjectMap(GameObject original, GameObject clone)
    {
        var map = new Dictionary<Object, Object>();
        Transform[] originals = original.GetComponentsInChildren<Transform>(true);
        Transform[] clones = clone.GetComponentsInChildren<Transform>(true);
        Check(originals.Length == clones.Length, "Cloned modal hierarchy does not match its source.");
        for (int index = 0; index < originals.Length; index++)
        {
            map[originals[index].gameObject] = clones[index].gameObject;
            Component[] components = originals[index].GetComponents<Component>();
            Component[] copies = clones[index].GetComponents<Component>();
            Check(components.Length == copies.Length, "Cloned modal component count changed.");
            for (int c = 0; c < components.Length; c++)
                if (components[c] != null && copies[c] != null) map[components[c]] = copies[c];
        }
        return map;
    }

    private static void Remap(Component component, Dictionary<Object, Object> map)
    {
        var data = new SerializedObject(component);
        SerializedProperty property = data.GetIterator();
        while (property.Next(true))
        {
            if (property.propertyType == SerializedPropertyType.ObjectReference &&
                property.objectReferenceValue != null && map.TryGetValue(property.objectReferenceValue, out Object mapped))
                property.objectReferenceValue = mapped;
        }
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void ValidateBoard(LeaderboardPanelUI board, GameObject completion, LeaderboardPanelUI source, StringBuilder report)
    {
        GameObject panel = Get<GameObject>(board, "panel");
        Canvas canvas = board.GetComponent<Canvas>();
        Canvas resultCanvas = completion.GetComponentInParent<Canvas>(true);
        Check(panel != null && panel.transform.IsChildOf(board.transform) && !panel.activeSelf,
            "The local leaderboard modal must be inactive under its own controller Canvas.");
        Check(canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay && canvas.sortingLayerID == resultCanvas.sortingLayerID &&
            canvas.sortingOrder > resultCanvas.sortingOrder, "The local board must render above completion.");
        Check(canvas.transform.childCount == 1, "Only the authored modal should be copied to the local Canvas.");
        Check(Get<Button>(board, "openButton") == null, "The Mode Selection open button was copied.");
        Check(panel.GetComponent(FindOverlayType()) != null, "Nested completion overlay lifecycle hook is missing.");
        foreach (Component component in panel.GetComponentsInChildren<Component>(true).Concat(new Component[] { board }))
        {
            Check(component != null, "Cloned modal contains a missing script.");
            var data = new SerializedObject(component);
            SerializedProperty property = data.GetIterator();
            while (property.Next(true))
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                Object reference = property.objectReferenceValue;
                if (reference == null || EditorUtility.IsPersistent(reference)) continue;
                Transform transform = reference is GameObject go ? go.transform : reference is Component other ? other.transform : null;
                Check(transform == null || transform == board.transform || transform == panel.transform || transform.IsChildOf(panel.transform),
                    "Cross-scene/Canvas reference in cloned modal: " + component.name + "." + property.propertyPath);
            }
        }
        Check(Get<LeaderboardRowUI[]>(board, "rows").Length == 15 && Get<MultiplayerLeaderboardRowUI[]>(board, "multiplayerRows").Length == 15,
            "The full current authored leaderboard row sets were not cloned.");
        var sourceCatalog = new SerializedObject(source).FindProperty("contracts");
        var catalog = new SerializedObject(board).FindProperty("contracts");
        Check(catalog.arraySize == sourceCatalog.arraySize, "Contract catalog count changed during cloning.");
        for (int index = 0; index < catalog.arraySize; index++)
            Check(catalog.GetArrayElementAtIndex(index).objectReferenceValue == sourceCatalog.GetArrayElementAtIndex(index).objectReferenceValue,
                "Contract asset reference changed during cloning.");
        foreach (Button button in panel.GetComponentsInChildren<Button>(true))
            for (int index = 0; index < button.onClick.GetPersistentEventCount(); index++)
                Check(button.onClick.GetPersistentTarget(index) != completion.GetComponentInParent<LevelCompleteManager>() &&
                    button.onClick.GetPersistentMethodName(index) != "RetrySimulation" && button.onClick.GetPersistentMethodName(index) != "SaveAndBakeBridge" &&
                    button.onClick.GetPersistentMethodName(index) != "SaveBridgeForSession", "Board action invokes a completion action.");
        report.AppendLine("PASS: Local modal retains assets/catalog; every scene UI reference remapped into clone; 15 solo + 15 multiplayer rows, overlay hook, higher sorting and inactive default verified.");
    }

    private static void Preview(LevelCompleteManager manager, string label, StringBuilder report)
    {
        LeaderboardPanelUI board = Get<LeaderboardPanelUI>(manager, "completedLocationLeaderboard");
        Scene scene = EditorSceneManager.NewPreviewScene();
        RenderTexture target = null;
        try
        {
            var canvasObject = new GameObject("Completion QA Canvas", typeof(RectTransform), typeof(Canvas));
            SceneManager.MoveGameObjectToScene(canvasObject, scene);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            GameObject result = Object.Instantiate(manager.levelCompletePanel, canvas.transform, false);
            GameObject modal = Object.Instantiate(Get<GameObject>(board, "panel"), canvas.transform, false);
            var controllerObject = new GameObject("Completion QA Controller");
            controllerObject.SetActive(false); SceneManager.MoveGameObjectToScene(controllerObject, scene);
            LeaderboardPanelUI controller = controllerObject.AddComponent<LeaderboardPanelUI>();
            EditorUtility.CopySerialized(board, controller);
            var map = BuildObjectMap(Get<GameObject>(board, "panel"), modal);
            map[board] = controller;
            Remap(controller, map);
            RemovePreviewMotion(result); RemovePreviewMotion(modal);
            TMP_Dropdown authoredDropdown = Get<TMP_Dropdown>(board, "contractDropdown");
            List<TMP_Dropdown.OptionData> authoredOptions = authoredDropdown.options
                .Select(option => new TMP_Dropdown.OptionData(option.text, option.image)).ToList();
            int authoredSelection = authoredDropdown.value;
            SelectionChecks(controller, label, report);
            TMP_Dropdown previewDropdown = Get<TMP_Dropdown>(controller, "contractDropdown");
            previewDropdown.ClearOptions();
            previewDropdown.AddOptions(authoredOptions);
            previewDropdown.SetValueWithoutNotify(authoredSelection);
            previewDropdown.RefreshShownValue();
            foreach (Transform node in canvas.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = 30;
            var cameraObject = new GameObject("Completion QA Camera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.scene = scene; camera.enabled = false; camera.orthographic = true;
            camera.transform.position = new Vector3(0, 0, -1000);
            camera.nearClipPlane = .1f; camera.farClipPlane = 2000; camera.cullingMask = 1 << 30;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(123, 98, 73, 255);
            canvas.worldCamera = camera;
            Transform paper = result.transform.Find("Completion Safe Area/Wood Frame/Cream Panel");
            Button retry = paper.Find("Retry")?.GetComponent<Button>() ?? paper.Find("Back to Build")?.GetComponent<Button>();
            Button save = paper.Find("Save & Continue")?.GetComponent<Button>() ?? paper.Find("Save for Session")?.GetComponent<Button>();
            GameObject guest = paper.Find("Guest Waiting")?.gameObject;
            foreach (Vector2Int size in new[] { new Vector2Int(1920, 1080), new Vector2Int(1280, 720) })
            {
                canvasObject.GetComponent<RectTransform>().sizeDelta = size;
                camera.orthographicSize = size.y / 2f;
                camera.aspect = (float)size.x / size.y;
                target = new RenderTexture(size.x, size.y, 24); target.Create(); camera.targetTexture = target;
                result.SetActive(true); modal.SetActive(false);
                retry.gameObject.SetActive(true); save.gameObject.SetActive(true); guest?.SetActive(false);
                paper.Find("Feedback").GetComponent<TMP_Text>().text = "Redesign Successful! (Rewards already claimed)";
                FooterFits(paper);
                Capture(camera, target, PreviewDirectory + "/" + label + "_Complete_" + size.x + "x" + size.y + ".png");
                report.AppendLine("PASS: " + label + " footer at " + size + ": feedback, Leaderboard, " + retry.name + " and " + save.name + " fit without overlap.");
                if (guest != null)
                {
                    retry.gameObject.SetActive(false); save.gameObject.SetActive(false); guest.SetActive(true);
                    paper.Find("Feedback").GetComponent<TMP_Text>().text = "Host's bridge passed the test.";
                    FooterFits(paper);
                    Capture(camera, target, PreviewDirectory + "/" + label + "_Guest_" + size.x + "x" + size.y + ".png");
                    report.AppendLine("PASS: " + label + " guest footer at " + size + ": Leaderboard remains available beside host-waiting message.");
                    retry.gameObject.SetActive(true); save.gameObject.SetActive(true); guest.SetActive(false);
                }
                modal.SetActive(true);
                modal.GetComponent<CozyModalLayout>().ApplyLayout();
                CozyModalLayout.Find(modal.transform, "Leaderboard Status").GetComponent<TMP_Text>().text = "Sign in to see the top 15 builders.";
                foreach (LeaderboardRowUI row in modal.GetComponentsInChildren<LeaderboardRowUI>(true)) row.gameObject.SetActive(false);
                foreach (MultiplayerLeaderboardRowUI row in modal.GetComponentsInChildren<MultiplayerLeaderboardRowUI>(true)) row.gameObject.SetActive(false);
                Capture(camera, target, PreviewDirectory + "/" + label + "_Leaderboard_" + size.x + "x" + size.y + ".png");
                modal.SetActive(false);
                Check(result.activeSelf && retry.gameObject.activeSelf && save.gameObject.activeSelf,
                    "Closing preview modal changed completion actions.");
                target.Release(); Object.DestroyImmediate(target); target = null;
            }
        }
        finally
        {
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static void RemovePreviewMotion(GameObject root)
    {
        foreach (MonoBehaviour component in root.GetComponentsInChildren<MonoBehaviour>(true))
            if (component is CompletionSafeArea || component is CompletionEntranceMotion || component is CompletionButtonMotion ||
                component is LeaderboardPanelMotion || component.GetType() == FindOverlayType()) Object.DestroyImmediate(component);
        foreach (CanvasGroup group in root.GetComponentsInChildren<CanvasGroup>(true))
        { group.alpha = 1; group.interactable = true; group.blocksRaycasts = true; }
    }

    private static void SelectionChecks(LeaderboardPanelUI controller, string label, StringBuilder report)
    {
        // This uses selection only, never Open/Refresh, so no online request or player save can run.
        MethodInfo select = typeof(LeaderboardPanelUI).GetMethod("TrySelectContract", Fields);
        PropertyInfo selected = typeof(LeaderboardPanelUI).GetProperty("SelectedContract", Fields);
        Check(select != null && selected != null, "The offline exact-contract selection API is missing.");
        GameObject panel = Get<GameObject>(controller, "panel");
        panel.SetActive(false);
        ContractSO first = ScriptableObject.CreateInstance<ContractSO>();
        ContractSO target = ScriptableObject.CreateInstance<ContractSO>();
        ContractSO hidden = ScriptableObject.CreateInstance<ContractSO>();
        ContractSO session = ScriptableObject.CreateInstance<ContractSO>();
        try
        {
            first.name = target.name = "Same display name";
            first.contractID = "COMPLETION_QA_OLD"; target.contractID = "COMPLETION_QA_TARGET";
            hidden.name = "Hidden preview"; hidden.contractID = "COMPLETION_QA_HIDDEN"; hidden.hideFromLeaderboard = true;
            session.name = "Session preview"; session.contractID = ChallengeBuildWorkspace.ContractPrefix + "QA";
            var data = new SerializedObject(controller);
            SerializedProperty catalog = data.FindProperty("contracts");
            catalog.arraySize = 1; catalog.GetArrayElementAtIndex(0).objectReferenceValue = first;
            data.ApplyModifiedPropertiesWithoutUndo();
            Check((bool)select.Invoke(controller, new object[] { first }), "Could not select the initial QA contract.");
            typeof(LeaderboardPanelUI).GetField("multiplayerSelected", Fields).SetValue(controller, true);
            Get<Toggle>(controller, "strongestToggle").SetIsOnWithoutNotify(true);
            Get<Toggle>(controller, "efficientToggle").SetIsOnWithoutNotify(false);
            Check((bool)select.Invoke(controller, new object[] { target }), "A completed contract missing from the catalog was rejected.");
            Check(((ContractSO)selected.GetValue(controller)).ContractID == target.ContractID,
                "The board selected a display-name match instead of the completed contract ID.");
            Check(!(bool)typeof(LeaderboardPanelUI).GetField("multiplayerSelected", Fields).GetValue(controller) &&
                Get<Toggle>(controller, "efficientToggle").isOn && !Get<Toggle>(controller, "strongestToggle").isOn,
                "A previous multiplayer/strongest selection was retained.");
            Check((bool)select.Invoke(controller, new object[] { first }) && (bool)select.Invoke(controller, new object[] { target }) &&
                ((ContractSO)selected.GetValue(controller)).ContractID == target.ContractID,
                "Repeated completion selection did not retain the exact contract ID.");
            foreach (ContractSO rejected in new[] { hidden, session, null })
                Check(!(bool)select.Invoke(controller, new object[] { rejected }), "Hidden/session/null contract was accepted.");
            Check(!panel.activeSelf, "Offline selection unexpectedly opened the leaderboard.");
            report.AppendLine("PASS: " + label + " offline selection chooses exact ID with same-name contracts, includes a missing-catalog completed contract, resets prior multiplayer/strongest, handles repeated selections and rejects hidden/session/null without opening or refreshing.");
        }
        finally
        {
            Object.DestroyImmediate(first); Object.DestroyImmediate(target);
            Object.DestroyImmediate(hidden); Object.DestroyImmediate(session);
        }
    }

    private static void FooterFits(Transform paper)
    {
        Canvas.ForceUpdateCanvases();
        var nodes = new[] { "Feedback", "Leaderboard", "Retry", "Back to Build", "Save & Continue", "Save for Session", "Guest Waiting" }
            .Select(name => paper.Find(name) as RectTransform).Where(rect => rect != null && rect.gameObject.activeSelf).OrderBy(rect => rect.anchorMin.x).ToArray();
        for (int index = 0; index < nodes.Length; index++)
        {
            RectTransform rect = nodes[index];
            Check(rect.rect.width > 80 && rect.rect.height >= 45, "Completion footer target is too small: " + rect.name);
            Check(rect.anchorMin.x >= 0 && rect.anchorMax.x <= 1 && rect.anchorMin.y >= 0 && rect.anchorMax.y <= 1,
                "Completion footer target leaves the paper: " + rect.name);
            if (index > 0) Check(nodes[index - 1].anchorMax.x <= rect.anchorMin.x, "Completion footer controls overlap.");
            foreach (TMP_Text text in rect.GetComponentsInChildren<TMP_Text>(true))
            {
                text.ForceMeshUpdate(true);
                Check(!text.isTextOverflowing, "Completion footer text overflows: " + rect.name);
            }
        }
    }

    private static void Capture(Camera camera, RenderTexture target, string path)
    {
        Canvas.ForceUpdateCanvases();
        foreach (TMP_Text text in camera.scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<TMP_Text>(true)))
            text.ForceMeshUpdate(true);
        var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
        if (RenderPipeline.SupportsRenderRequest(camera, request)) RenderPipeline.SubmitRenderRequest(camera, request);
        else camera.Render();
        RenderTexture previous = RenderTexture.active;
        var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        try
        {
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
        }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(texture); }
    }

    private static Type FindOverlayType() => typeof(LevelCompleteManager).Assembly.GetType("LevelCompleteLeaderboardOverlay");
    private static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, Fields).GetValue(target);
    private static IEnumerable<T> InScene<T>(Scene scene) where T : Component =>
        scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true));
    private static void Place(RectTransform rect, float x0, float y0, float x1, float y1)
    {
        Check(rect != null, "Completion footer rectangle is missing.");
        rect.anchorMin = new Vector2(x0, y0); rect.anchorMax = new Vector2(x1, y1);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
#endif
