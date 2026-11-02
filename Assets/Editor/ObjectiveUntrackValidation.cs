#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
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

/// <summary>Opt-in previews of copied Objectives graphics. Never invokes navigation or save callbacks.</summary>
[InitializeOnLoad]
public static class ObjectiveUntrackValidation
{
    public const string RequestPath = "Temp/objective-untrack-validation.request";
    public const string ReportPath = "Logs/objective-untrack-validation.txt";
    private static bool running;
    private static double nextPoll;
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    static ObjectiveUntrackValidation() { EditorApplication.update += Poll; }
    private static bool Busy => running || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
        EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer || PrefabStageUtility.GetCurrentPrefabStage() != null;
    private static void Poll()
    {
        if (Busy || EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1;
        if (!File.Exists(RequestPath)) return;
        File.Delete(RequestPath); Run();
    }

    [MenuItem("Tools/Civil Craft/Objectives/Validate Untrack Button (Read Only)")]
    public static void Run()
    {
        if (Busy) return;
        running = true;
        var report = new StringBuilder("Objectives Untrack: detached graphics, control bindings and mobile layout\n");
        Scene[] scenes = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).ToArray();
        bool[] dirty = scenes.Select(scene => scene.isDirty).ToArray();
        bool[] loaded = scenes.Select(scene => scene.isLoaded).ToArray();
        GameObject[][] roots = scenes.Select(scene => scene.isLoaded ? scene.GetRootGameObjects() : Array.Empty<GameObject>()).ToArray();
        var files = scenes.Where(scene => File.Exists(scene.path)).ToDictionary(scene => scene.path, scene => Hash(scene.path));
        Scene active = SceneManager.GetActiveScene(); Object[] selection = Selection.objects; Object selected = Selection.activeObject;
        ObjectiveTrackerUI tracker = ObjectiveTrackerUI.Instance;
        PlayerDataManager saves = PlayerDataManager.Instance; PlayerData player = saves != null ? saves.CurrentData : null;
        string playerJson = player != null ? JsonUtility.ToJson(player) : null; float time = Time.timeScale;
        try
        {
            ObjectiveTrackerUI[] sources = Resources.FindObjectsOfTypeAll<ObjectiveTrackerUI>().Where(source => source != null &&
                source.gameObject.scene.IsValid() && source.gameObject.scene.isLoaded && !EditorSceneManager.IsPreviewScene(source.gameObject.scene)).ToArray();
            Require(sources.Length > 0, "Open a gameplay scene containing its authored Objectives panel first.");
            foreach (ObjectiveTrackerUI source in sources) Capture(source, report);
            report.Insert(0, "PASS\n");
        }
        catch (Exception error) { report.Insert(0, "FAILED\n"); report.AppendLine(error.ToString()); }
        finally
        {
            try
            {
                Require(SceneManager.sceneCount == scenes.Length && SceneManager.GetActiveScene() == active, "Loaded scene setup changed.");
                for (int index = 0; index < scenes.Length; index++)
                    Require(SceneManager.GetSceneAt(index) == scenes[index] && scenes[index].isLoaded == loaded[index] && scenes[index].isDirty == dirty[index] &&
                        (!loaded[index] || scenes[index].GetRootGameObjects().SequenceEqual(roots[index])), "Source scene order, roots or dirty flags changed: " + scenes[index].path);
                Require(files.All(pair => Hash(pair.Key) == pair.Value), "Saved scene bytes changed.");
                Require(Selection.activeObject == selected && Selection.objects.SequenceEqual(selection) && Time.timeScale == time,
                    "Selection or time scale changed.");
                Require(ObjectiveTrackerUI.Instance == tracker && PlayerDataManager.Instance == saves &&
                    (player == null || ReferenceEquals(player, saves.CurrentData) && JsonUtility.ToJson(player) == playerJson), "Live singleton or player data changed.");
                report.AppendLine("PASS: live scene setup, source graphics/callbacks, saved bytes, selection, time scale and player data preserved. No game button was invoked or scene saved.");
            }
            catch (Exception error) { report.Insert(0, "FAILED SAFETY\n"); report.AppendLine(error.ToString()); }
            Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, report.ToString()); running = false;
        }
    }

    private static void Capture(ObjectiveTrackerUI source, StringBuilder report)
    {
        Require(source.trackerPanel != null && source.listPanel != null && source.detailsPanel != null && source.navigateButton != null,
            "Missing authored Objectives panels or Track button in " + source.gameObject.scene.name);
        var originals = source.trackerPanel.GetComponentsInChildren<Component>(true).Cast<Object>().Append(source).Distinct()
            .ToDictionary(item => item, EditorJsonUtility.ToJson);
        var originalCalls = source.trackerPanel.GetComponentsInChildren<Button>(true).ToDictionary(button => button, button => JsonUtility.ToJson(button.onClick));
        var fonts = source.trackerPanel.GetComponentsInChildren<TMP_Text>(true).Select(text => text.font).Where(font => font != null).Distinct()
            .ToDictionary(font => font, EditorJsonUtility.ToJson);
        Scene preview = EditorSceneManager.NewPreviewScene(); RenderTexture previous = RenderTexture.active;
        ContractFixture contract = null;
        try
        {
            GameObject root = Owned(preview, "Detached Objectives Canvas", typeof(RectTransform), typeof(Canvas));
            root.transform.localScale = Vector3.one * .01f;
            Canvas canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            RectTransform canvasRect = (RectTransform)root.transform;
            var map = new Dictionary<Transform, RectTransform>();
            RectTransform panel = CloneArt((RectTransform)source.trackerPanel.transform, root.transform, map);
            RemapScrollViews(map);
            // No game behaviours are cloned; the inactive controller is used only for its styling helpers.
            GameObject owner = Owned(preview, "Detached inactive Objectives controller"); owner.SetActive(false);
            ObjectiveTrackerUI copy = owner.AddComponent<ObjectiveTrackerUI>();
            copy.trackerPanel = panel.gameObject; copy.listPanel = Remap(source.listPanel, map); copy.detailsPanel = Remap(source.detailsPanel, map);
            copy.navigateButton = Remap(source.navigateButton, map); copy.completeButton = Remap(source.completeButton, map);
            copy.rewardContainer = Remap(source.rewardContainer, map); copy.questListContent = source.questListContent != null && map.TryGetValue(source.questListContent, out RectTransform content) ? content : null;
            copy.questTabPrefab = source.questTabPrefab; copy.roundedPanelSprite = source.roundedPanelSprite;
            copy.titleText = Text(source.titleText, map); copy.descriptionText = Text(source.descriptionText, map);
            copy.budgetText = Text(source.budgetText, map); copy.weightText = Text(source.weightText, map);
            copy.rewardGoldText = Text(source.rewardGoldText, map); copy.rewardExpText = Text(source.rewardExpText, map);
            copy.untrackButton = Remap(source.untrackButton, map);
            PrepareOwnedArt(preview, copy);
            // Only the copied Untrack callback is rebound to the inactive preview
            // controller. All other authored callbacks remain untouched and none run.
            Button copiedUntrack = panel.Find("Untrack Objective").GetComponent<Button>();
            for (int index = 0; index < copiedUntrack.onClick.GetPersistentEventCount(); index++)
                if (copiedUntrack.onClick.GetPersistentTarget(index) == source &&
                    copiedUntrack.onClick.GetPersistentMethodName(index) == nameof(ObjectiveTrackerUI.OnUntrackObjectiveClicked))
                    UnityEventTools.RegisterPersistentListener(copiedUntrack.onClick, index, copy.OnUntrackObjectiveClicked);
            var calls = map.Keys.Where(item => item.GetComponent<Button>() != null && map[item].GetComponent<Button>() != copiedUntrack)
                .ToDictionary(item => map[item].GetComponent<Button>(), item => JsonUtility.ToJson(map[item].GetComponent<Button>().onClick));
            panel.gameObject.SetActive(true);
            Invoke(copy, "EnsureUntrackButton");
            GameObject untrack = copy.untrackButton; Require(untrack != null, "Runtime Untrack button was not created.");
            Button untrackControl = untrack.GetComponent<Button>(); int id = untrack.GetInstanceID();
            int children = panel.GetComponentsInChildren<Transform>(true).Length;
            Invoke(copy, "EnsureUntrackButton");
            Require(copy.untrackButton.GetInstanceID() == id && children == panel.GetComponentsInChildren<Transform>(true).Length &&
                panel.GetComponentsInChildren<Button>(true).Count(button => button.gameObject.name == "Untrack Objective") == 1,
                "Repeated initialization duplicates the Untrack control.");
            ValidateUntrackBinding(untrackControl, copy);
            copy.ApplySharedVisualStyle();
            Require(copy.untrackButton == untrack,
                "Repeated shared styling duplicates the Untrack callback.");
            ValidateUntrackBinding(untrackControl, copy);
            Require(calls.All(pair => pair.Key != null && JsonUtility.ToJson(pair.Key.onClick) == pair.Value),
                "Styling changed an existing Close, Track, Accept or Collect persistent callback.");
            foreach (TMP_Text text in panel.GetComponentsInChildren<TMP_Text>(true)) text.isOrthographic = true;
            TMP_Text label = untrack.GetComponentInChildren<TMP_Text>(true);
            Require(label != null && label.text == "UNTRACK OBJECTIVE" && !label.raycastTarget && label.font != null,
                "Untrack label is missing, intercepts taps or has no gameplay font.");
            GameObject cameraObject = Owned(preview, "Detached Objectives preview camera", typeof(Camera));
            Camera camera = cameraObject.GetComponent<Camera>(); camera.scene = preview; camera.useOcclusionCulling = false;
            camera.orthographic = true; camera.nearClipPlane = .1f; camera.farClipPlane = 100f; camera.transform.position = new Vector3(0f, 0f, -20f);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(119, 156, 165, 255); canvas.worldCamera = camera;
            TMP_Text heading = panel.GetComponentsInChildren<TMP_Text>(true).Single(text => text.transform.parent == panel && text.text == "OBJECTIVES");
            contract = CreateContractFixture(preview, copy);
            AddListFixtures(preview, copy);
            CanvasScaler sourceScaler = source.trackerPanel.GetComponentInParent<CanvasScaler>(true);
            Vector2 reference = sourceScaler != null ? sourceScaler.referenceResolution : new Vector2(1920f, 1080f);
            float match = sourceScaler != null ? sourceScaler.matchWidthOrHeight : .5f;
            foreach (Vector2Int screen in new[] { new Vector2Int(1600, 720), new Vector2Int(1920, 1080), new Vector2Int(1440, 1080) })
            {
                float scale = Mathf.Pow(screen.x / reference.x, 1f - match) * Mathf.Pow(screen.y / reference.y, match);
                canvasRect.sizeDelta = new Vector2(screen.x, screen.y) / scale; canvas.scaleFactor = scale;
                camera.aspect = (float)screen.x / screen.y; camera.orthographicSize = canvasRect.rect.height * .005f;
                var texture = new RenderTexture(screen.x, screen.y, 24); texture.Create(); camera.targetTexture = texture;
                try
                {
                panel.GetComponent<ObjectiveMobileFit>().ApplyLayout();
                foreach (string state in new[] { "List", "ListBottom", "Tutorial", "ActiveContract", "AcceptContract", "CollectReward", "CompletedContract", "ListUntracked", "DetailsUntracked" })
                {
                    bool details = !state.StartsWith("List", StringComparison.Ordinal);
                    copy.listPanel.SetActive(!details); copy.detailsPanel.SetActive(details);
                    contract.Root.SetActive(false);
                    if (copy.completeButton != null) copy.completeButton.SetActive(false);
                    if (copy.rewardContainer != null) copy.rewardContainer.SetActive(false);
                    if (details && (state == "Tutorial" || state == "DetailsUntracked"))
                    {
                        if (copy.titleText != null) { copy.titleText.gameObject.SetActive(true); copy.titleText.text = "Follow Reed"; }
                        if (copy.descriptionText != null) { copy.descriptionText.gameObject.SetActive(true); copy.descriptionText.text = "Follow Reed to the factory crossing, then talk to him about the bridge."; }
                        if (copy.budgetText != null) copy.budgetText.gameObject.SetActive(false);
                        if (copy.weightText != null) copy.weightText.gameObject.SetActive(false);
                        copy.navigateButton.SetActive(true);
                        copy.navigateButton.GetComponentInChildren<TMP_Text>(true).text = "TRACK LOCATION";
                    }
                    else if (details)
                    {
                        contract.Task.isReadyToTurnIn = state == "CollectReward";
                        contract.Task.isCompleted = state == "CompletedContract";
                        Invoke(copy, "ShowContractDetails", contract.Contract, contract.Task, state == "AcceptContract");
                        // This is a detached layout fixture, not a navigation availability test.
                        // No scene route is registered and no gameplay callback is invoked.
                        if (state == "ActiveContract") copy.navigateButton.SetActive(true);
                    }
                    Transform back = copy.detailsPanel.transform.Find("Back to Objective List");
                    if (back != null) back.gameObject.SetActive(details);
                    untrack.SetActive(!state.EndsWith("Untracked", StringComparison.Ordinal));
                    untrack.transform.SetAsLastSibling(); copy.ApplyNavigationFooterLayout();
                    LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
                    ScrollRect listScroll = copy.listPanel.GetComponentInChildren<ScrollRect>(true);
                    if (!details && listScroll != null) { listScroll.StopMovement(); listScroll.verticalNormalizedPosition = state == "ListBottom" ? 0f : 1f; }
                    foreach (TMP_Text text in panel.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate(true, true);
                    foreach (Graphic graphic in panel.GetComponentsInChildren<Graphic>(false)) graphic.Rebuild(CanvasUpdate.PreRender);
                    label.ForceMeshUpdate(true, true);
                    Rect bounds = Bounds((RectTransform)untrack.transform, camera);
                    ValidateFooter(copy, heading, camera, details, state);
                    Require(label.GetParsedText() == "UNTRACK OBJECTIVE" && !label.isTextOverflowing && !label.isTextTruncated && label.fontSize >= 21f,
                        "Untrack text is truncated, overflowing or shrunk below the shared action size.");
                    foreach (TMP_CharacterInfo character in label.textInfo.characterInfo.Take(label.textInfo.characterCount))
                        if (character.isVisible) Require(Contains(bounds, GlyphBounds(label, character, camera)), "An Untrack glyph leaves its button.");
                    string path = "Temp/ObjectiveUntrack/" + source.gameObject.scene.name + "-" + state + "-" + screen.x + "x" + screen.y + ".png";
                    Render(camera, texture, path);
                    report.AppendLine("PASS: " + source.gameObject.scene.name + " " + state + " " + screen.x + "x" + screen.y +
                        "; one Untrack callback, original action bindings preserved, reserved footer, complete glyphs and " + bounds.height.ToString("0.0") + "px touch height. " + path);
                }
                }
                finally { camera.targetTexture = null; RenderTexture.active = previous; texture.Release(); Object.DestroyImmediate(texture); }
            }
            Require(calls.All(pair => pair.Key != null && JsonUtility.ToJson(pair.Key.onClick) == pair.Value),
                "Footer fixture changed existing Close, Track, Accept or Collect callbacks.");
            report.AppendLine("Contract previews use detached field fixtures with real ShowContractDetails label/state rendering; material card allocation and route availability are excluded. No contract, navigation or reward callback runs.");
        }
        finally
        {
            if (contract != null && contract.Contract != null) Object.DestroyImmediate(contract.Contract);
            RenderTexture.active = previous; EditorSceneManager.ClosePreviewScene(preview);
            bool originalSafe = RecordDifferences(originals, source.gameObject.scene.name, "component", report);
            bool fontsSafe = RecordDifferences(fonts.ToDictionary(pair => (Object)pair.Key, pair => pair.Value), source.gameObject.scene.name, "font", report);
            bool callbacksSafe = originalCalls.All(pair => pair.Key != null && JsonUtility.ToJson(pair.Key.onClick) == pair.Value);
            Require(originalSafe && fontsSafe && callbacksSafe, "Detached validation changed source Objectives graphics, callbacks or font assets; see exact diagnostics above.");
        }
    }

    private sealed class ContractFixture
    {
        public GameObject Root;
        public ContractSO Contract;
        public TrackedTask Task;
    }

    private static ContractFixture CreateContractFixture(Scene scene, ObjectiveTrackerUI copy)
    {
        // Match the production contract card's sections, and supply its private text
        // references. Avoid allocating through runtime helpers into the active scene.
        GameObject Node(Transform parent, string name, Vector2 size, Vector2 position, bool surface)
        {
            GameObject item = surface ? Owned(scene, name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)) : Owned(scene, name, typeof(RectTransform));
            item.transform.SetParent(parent, false); RectTransform rect = (RectTransform)item.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f; rect.sizeDelta = size; rect.anchoredPosition = position;
            if (surface) { Image image = item.GetComponent<Image>(); image.sprite = copy.roundedPanelSprite; image.type = Image.Type.Sliced; image.color = new Color32(255, 247, 225, 255); image.raycastTarget = false; }
            return item;
        }
        TextMeshProUGUI Text(Transform parent, string name, Vector2 size, Vector2 position, float fontSize, TextAlignmentOptions alignment)
        {
            GameObject item = Owned(scene, name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI)); item.transform.SetParent(parent, false);
            var rect = (RectTransform)item.transform; rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f; rect.sizeDelta = size; rect.anchoredPosition = position;
            var text = item.GetComponent<TextMeshProUGUI>(); text.font = copy.titleText.font; text.fontSize = fontSize; text.alignment = alignment;
            text.color = new Color32(75, 46, 29, 255); text.enableWordWrapping = true; text.raycastTarget = false; text.isOrthographic = true; return text;
        }
        void Field(string name, object value) { typeof(ObjectiveTrackerUI).GetField(name, Hidden).SetValue(copy, value); }
        GameObject root = Node(copy.detailsPanel.transform, "Detached Contract Offer Layout", new Vector2(1060f, 770f), Vector2.zero, false);
        Field("contractOfferLayout", root);
        Field("offerTitleLabel", Text(root.transform, "Contract Offer Title", new Vector2(950f, 54f), new Vector2(0f, 326f), 38f, TextAlignmentOptions.Center));
        GameObject brief = Node(root.transform, "Project Brief", new Vector2(960f, 112f), new Vector2(0f, 222f), true);
        Field("offerDescriptionLabel", Text(brief.transform, "Project Brief Text", new Vector2(920f, 88f), Vector2.zero, 25f, TextAlignmentOptions.MidlineLeft));
        GameObject requirements = Node(root.transform, "Contract Requirements", new Vector2(960f, 76f), new Vector2(0f, 120f), true);
        Field("offerBudgetValue", Text(requirements.transform, "Budget", new Vector2(300f, 64f), new Vector2(-320f, 0f), 27f, TextAlignmentOptions.Center));
        Field("offerLoadValue", Text(requirements.transform, "Live Load", new Vector2(300f, 64f), Vector2.zero, 27f, TextAlignmentOptions.Center));
        Field("offerSpanValue", Text(requirements.transform, "Required Span", new Vector2(300f, 64f), new Vector2(320f, 0f), 27f, TextAlignmentOptions.Center));
        GameObject materials = Node(root.transform, "Allowed Materials", new Vector2(960f, 185f), new Vector2(0f, -25f), true);
        Text(materials.transform, "Allowed Materials Header", new Vector2(600f, 28f), new Vector2(-150f, 72f), 23f, TextAlignmentOptions.MidlineLeft).text = "ALLOWED MATERIALS";
        Text(materials.transform, "No Material Restrictions", new Vector2(920f, 110f), new Vector2(0f, -22f), 23f, TextAlignmentOptions.MidlineLeft).text = "No material restrictions for this contract.";
        // Null skips dynamic material-card allocation while retaining the exact section bounds.
        Field("offerMaterialsContent", null); Field("offerMaterialsScrollRect", null);
        GameObject rewards = Node(root.transform, "Contract Rewards", new Vector2(960f, 64f), new Vector2(0f, -180f), true);
        Field("offerRewardValue", Text(rewards.transform, "Reward Value", new Vector2(210f, 46f), new Vector2(-350f, 0f), 22f, TextAlignmentOptions.Center));
        Field("offerGoldValue", Text(rewards.transform, "Coin Reward Value", new Vector2(125f, 46f), new Vector2(-45f, 0f), 24f, TextAlignmentOptions.MidlineLeft));
        Field("offerExperienceValue", Text(rewards.transform, "Experience Reward Value", new Vector2(125f, 46f), new Vector2(245f, 0f), 24f, TextAlignmentOptions.MidlineLeft));
        ContractSO contract = ScriptableObject.CreateInstance<ContractSO>(); contract.hideFlags = HideFlags.HideAndDontSave;
        contract.clientName = "Reed"; contract.jobDescription = "Connect the factory to Central Canyonville. Build a safe crossing for the test pickup and observe how the structure responds to bending under its weight.";
        contract.budget = 100000f; contract.liveLoadWeight = 5000f; contract.bridgeSpan = 60f; contract.goldReward = 500; contract.expReward = 100;
        return new ContractFixture { Root = root, Contract = contract, Task = new TrackedTask { title = "Reed's Request", pendingGold = 500, pendingExp = 100 } };
    }

    private static void AddListFixtures(Scene scene, ObjectiveTrackerUI copy)
    {
        if (copy.questListContent == null) return;
        for (int index = 0; index < 10; index++)
        {
            GameObject row = Owned(scene, "Detached Objective " + index, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
            row.transform.SetParent(copy.questListContent, false); row.GetComponent<LayoutElement>().preferredHeight = 128f;
            Image image = row.GetComponent<Image>(); image.sprite = copy.roundedPanelSprite; image.type = Image.Type.Sliced; image.color = new Color32(222, 204, 160, 255); image.raycastTarget = false;
            GameObject label = Owned(scene, "Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI)); label.transform.SetParent(row.transform, false);
            RectTransform rect = (RectTransform)label.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = new Vector2(24f, 10f); rect.offsetMax = new Vector2(-24f, -10f);
            var text = label.GetComponent<TextMeshProUGUI>(); text.font = copy.titleText.font; text.fontSize = 28f; text.color = new Color32(75, 46, 29, 255); text.raycastTarget = false;
            text.text = index == 9 ? "Last objective: Talk to Silas" : "Tutorial objective " + (index + 1); text.isOrthographic = true;
        }
    }

    private static void ValidateFooter(ObjectiveTrackerUI copy, TMP_Text heading, Camera camera, bool details, string state)
    {
        Rect panel = Bounds((RectTransform)copy.trackerPanel.transform, camera);
        Rect page = Bounds((RectTransform)(details ? copy.detailsPanel : copy.listPanel).transform, camera);
        Rect untrack = Bounds((RectTransform)copy.untrackButton.transform, camera); bool visible = copy.untrackButton.activeSelf;
        Require(Contains(camera.pixelRect, panel) && Contains(page, untrack), "The footer leaves its panel or screen.");
        Require(!untrack.Overlaps(Bounds(heading.rectTransform, camera)) && untrack.height >= 44f, "Untrack overlaps the heading or is smaller than 44 pixels.");
        ScrollRect scroll = copy.listPanel.GetComponentInChildren<ScrollRect>(true);
        Require(scroll != null && Mathf.Approximately(((RectTransform)scroll.transform).offsetMin.y, visible ? 120f : 30f), "The list footer inset does not follow Untrack visibility.");
        if (!details && visible) Require(!untrack.Overlaps(Bounds(scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform, camera)), "Scrollable objective rows enter the navigation footer.");
        var actions = new List<Rect>();
        if (visible) actions.Add(untrack);
        if (details)
            foreach (GameObject item in new[] { copy.navigateButton, copy.completeButton, copy.detailsPanel.transform.Find("Back to Objective List")?.gameObject })
            {
                if (item == null || !item.activeInHierarchy) continue;
                Rect bounds = Bounds((RectTransform)item.transform, camera);
                Require(Contains(page, bounds) && bounds.height >= 44f && actions.All(other => !bounds.Overlaps(other)), "Footer actions overlap or leave the touch-safe page: " + state);
                actions.Add(bounds);
                TMP_Text text = item.GetComponentInChildren<TMP_Text>(true); text.ForceMeshUpdate(true, true);
                Require(!text.isTextOverflowing && !text.isTextTruncated && text.fontSize >= 21f, "Footer action text does not fit: " + state);
                foreach (TMP_CharacterInfo glyph in text.textInfo.characterInfo.Take(text.textInfo.characterCount))
                    if (glyph.isVisible) Require(Contains(bounds, GlyphBounds(text, glyph, camera)), "An action glyph leaves its button: " + state);
            }
        RectTransform primary = (RectTransform)copy.navigateButton.transform;
        Require(primary.anchoredPosition == new Vector2(visible ? -80f : 0f, -315f) && primary.sizeDelta == new Vector2(visible ? 380f : 420f, 80f), "The primary action does not adapt when Untrack hides.");
    }

    private static void RemapScrollViews(Dictionary<Transform, RectTransform> map)
    {
        foreach (var pair in map)
        {
            ScrollRect source = pair.Key.GetComponent<ScrollRect>(), copy = pair.Value.GetComponent<ScrollRect>();
            if (source == null || copy == null) continue;
            copy.content = source.content != null && map.TryGetValue(source.content, out RectTransform content) ? content : null;
            copy.viewport = source.viewport != null && map.TryGetValue(source.viewport, out RectTransform viewport) ? viewport : null;
            copy.horizontalScrollbar = null; copy.verticalScrollbar = null;
        }
    }

    private static int CountRuntimeCalls(UnityEvent calls, Object target, string name)
    {
        IList prepared = (IList)typeof(UnityEventBase).GetMethod("PrepareInvoke", Hidden).Invoke(calls, null);
        int result = 0;
        foreach (object call in prepared)
            for (Type type = call.GetType(); type != null; type = type.BaseType)
                foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
                    if (field.GetValue(call) is Delegate callback) result += callback.GetInvocationList().Count(item => ReferenceEquals(item.Target, target) && item.Method.Name == name);
        // RuntimeOnly persistent calls are intentionally absent from PrepareInvoke
        // in Edit Mode. Include their runtime contribution without invoking them.
        if (!Application.isPlaying)
            for (int index = 0; index < calls.GetPersistentEventCount(); index++)
                if (calls.GetPersistentListenerState(index) == UnityEventCallState.RuntimeOnly &&
                    calls.GetPersistentTarget(index) == target && calls.GetPersistentMethodName(index) == name) result++;
        return result;
    }
    private static void ValidateUntrackBinding(Button button, ObjectiveTrackerUI owner)
    {
        UnityEvent calls = button.onClick;
        if (calls.GetPersistentEventCount() > 0)
            Require(calls.GetPersistentEventCount() == 1 && calls.GetPersistentTarget(0) == owner &&
                calls.GetPersistentMethodName(0) == nameof(ObjectiveTrackerUI.OnUntrackObjectiveClicked) &&
                calls.GetPersistentListenerState(0) == UnityEventCallState.RuntimeOnly,
                "Authored Untrack must have one RuntimeOnly callback to the preview owner.");
        Require(CountRuntimeCalls(calls, owner, nameof(ObjectiveTrackerUI.OnUntrackObjectiveClicked)) == 1,
            "Untrack must have exactly one effective runtime click callback, including its authored binding.");
    }
    private static bool RecordDifferences(Dictionary<Object, string> before, string scene, string kind, StringBuilder report)
    {
        bool unchanged = true;
        foreach (var pair in before)
        {
            string after = pair.Key != null ? EditorJsonUtility.ToJson(pair.Key) : "<destroyed>";
            if (after == pair.Value) continue;
            unchanged = false; int offset = 0, limit = Math.Min(pair.Value.Length, after.Length);
            while (offset < limit && pair.Value[offset] == after[offset]) offset++;
            int start = Math.Max(0, offset - 90);
            string stem = "Logs/ObjectiveUntrackChanges/" + scene + "-" + kind + "-" + (pair.Key != null ? pair.Key.GetInstanceID().ToString() : "destroyed");
            Directory.CreateDirectory("Logs/ObjectiveUntrackChanges"); File.WriteAllText(stem + "-before.json", pair.Value); File.WriteAllText(stem + "-after.json", after);
            report.AppendLine("SOURCE CHANGE: " + (pair.Key != null ? pair.Key.name + " [" + pair.Key.GetType().Name + "]" : "destroyed") +
                "; first difference at " + offset + "; before=" + pair.Value.Substring(start, Math.Min(260, pair.Value.Length - start)) +
                "; after=" + after.Substring(Math.Min(start, after.Length), Math.Min(260, after.Length - Math.Min(start, after.Length))) + "; JSON: " + stem + "-{before,after}.json");
        }
        return unchanged;
    }
    private static void PrepareOwnedArt(Scene preview, ObjectiveTrackerUI copy)
    {
        // Runtime helpers normally allocate then parent their controls. Prepare
        // only detached fixtures so no transient root enters the live scene.
        GameObject Child(Transform parent, string name, params Type[] components)
        {
            Transform existing = parent.Find(name); if (existing != null) return existing.gameObject;
            GameObject value = Owned(preview, name, components); value.transform.SetParent(parent, false); return value;
        }
        GameObject clip = Child(copy.trackerPanel.transform, "Clipboard Clip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        Child(clip.transform, "Clip Face", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        GameObject untrack = Child(copy.trackerPanel.transform, "Untrack Objective", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        Child(untrack.transform, "Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        GameObject back = Child(copy.detailsPanel.transform, "Back to Objective List", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        GameObject backLabel = Child(back.transform, "Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        TextMeshProUGUI label = backLabel.GetComponent<TextMeshProUGUI>();
        if (copy.titleText != null) label.font = copy.titleText.font;
        label.text = "BACK TO LIST"; label.raycastTarget = false;
        if (copy.rewardContainer != null)
            foreach (string name in new[] { "CoinRewardIcon", "ExperienceRewardIcon" })
                Child(copy.rewardContainer.transform, name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
    }
    private static void Invoke(ObjectiveTrackerUI owner, string name, params object[] arguments) => typeof(ObjectiveTrackerUI).GetMethod(name, Hidden).Invoke(owner, arguments);
    private static GameObject Remap(GameObject source, Dictionary<Transform, RectTransform> map) => source != null && map.TryGetValue(source.transform, out RectTransform copy) ? copy.gameObject : null;
    private static TextMeshProUGUI Text(TextMeshProUGUI source, Dictionary<Transform, RectTransform> map) => source != null && map.TryGetValue(source.transform, out RectTransform copy) ? copy.GetComponent<TextMeshProUGUI>() : null;
    private static GameObject Owned(Scene scene, string name, params Type[] components)
    {
        GameObject item = EditorUtility.CreateGameObjectWithHideFlags(name, HideFlags.HideAndDontSave, components);
        SceneManager.MoveGameObjectToScene(item, scene); return item;
    }
    private static RectTransform CloneArt(RectTransform source, Transform parent, Dictionary<Transform, RectTransform> map)
    {
        var item = EditorUtility.CreateGameObjectWithHideFlags(source.name, HideFlags.HideAndDontSave, typeof(RectTransform)); item.layer = source.gameObject.layer;
        RectTransform rect = (RectTransform)item.transform; rect.SetParent(parent, false);
        rect.anchorMin = source.anchorMin; rect.anchorMax = source.anchorMax; rect.pivot = source.pivot;
        rect.sizeDelta = source.sizeDelta; rect.anchoredPosition = source.anchoredPosition; rect.localScale = source.localScale; rect.localRotation = source.localRotation;
        map.Add(source, rect);
        foreach (Type type in new[] { typeof(Image), typeof(RawImage), typeof(TextMeshProUGUI), typeof(Button), typeof(ScrollRect), typeof(RectMask2D), typeof(Mask), typeof(VerticalLayoutGroup), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter), typeof(LayoutElement) })
        {
            Component original = source.GetComponent(type); if (original == null) continue;
            Component copy = item.AddComponent(type); EditorUtility.CopySerialized(original, copy);
            if (copy is Graphic graphic) graphic.raycastTarget = false;
            if (copy is TMP_Text text) text.isOrthographic = true;
        }
        foreach (Transform child in source) if (child is RectTransform childRect) CloneArt(childRect, rect, map);
        item.SetActive(source.gameObject.activeSelf); return rect;
    }
    private static Rect Bounds(RectTransform rect, Camera camera)
    {
        var corners = new Vector3[4]; rect.GetWorldCorners(corners); Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]), max = min;
        foreach (Vector3 corner in corners) { Vector2 point = RectTransformUtility.WorldToScreenPoint(camera, corner); min = Vector2.Min(min, point); max = Vector2.Max(max, point); }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }
    private static Rect GlyphBounds(TMP_Text text, TMP_CharacterInfo character, Camera camera)
    {
        Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, text.transform.TransformPoint(character.bottomLeft));
        Vector2 max = RectTransformUtility.WorldToScreenPoint(camera, text.transform.TransformPoint(character.topRight));
        return Rect.MinMaxRect(Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y), Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));
    }
    private static bool Contains(Rect outer, Rect inner) => inner.xMin >= outer.xMin - .5f && inner.yMin >= outer.yMin - .5f && inner.xMax <= outer.xMax + .5f && inner.yMax <= outer.yMax + .5f;
    private static void Render(Camera camera, RenderTexture target, string path)
    {
        if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null) camera.Render();
        else UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera, new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = target });
        RenderTexture.active = target; var pixels = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        try { pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply(); Directory.CreateDirectory("Temp/ObjectiveUntrack"); File.WriteAllBytes(path, pixels.EncodeToPNG()); }
        finally { Object.DestroyImmediate(pixels); }
    }
    private static string Hash(string path) { using (SHA256 hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(path))); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
#endif
