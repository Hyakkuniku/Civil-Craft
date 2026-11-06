using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Opt-in, disposable fixed notification placement regressions. Never unlocks achievements or saves a scene.</summary>
public static class NotificationPausePlacementValidation
{
    private const string Request = "Temp/notification-pause-placement.request";
    private const string Report = "Logs/notification-pause-placement.txt";
    private const string Prefab = "Assets/Prefabs/UI/AchievementPopupCanvas.prefab";
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static bool running;
    private static double nextCheck;

    [InitializeOnLoadMethod]
    private static void Watch()
    {
        EditorApplication.update -= CheckRequest;
        EditorApplication.update += CheckRequest;
    }

    private static bool Busy => running || EditorApplication.isCompiling || EditorApplication.isUpdating ||
        EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer ||
        PrefabStageUtility.GetCurrentPrefabStage() != null;

    private static void CheckRequest()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 2;
        if (Busy || !File.Exists(Request)) return;
        File.Delete(Request);
        ValidateFromCommandLine();
    }

    [MenuItem("Tools/Civil Craft/Validate Notification Pause Placement")]
    public static void ValidateFromCommandLine()
    {
        Directory.CreateDirectory("Logs");
        try
        {
            File.WriteAllText(Report, Validate());
            Debug.Log("[Notification Pause Placement] PASS: " + Report);
        }
        catch (Exception error)
        {
            File.WriteAllText(Report, "FAIL: " + error);
            Debug.LogException(error);
        }
    }

    private static string Validate()
    {
        Check(!Busy, "Run the fixture in idle Edit Mode, outside imports, Play Mode, Prefab Mode and builds.");
        Check(Screen.width >= 64 && Screen.height >= 64, "A non-zero Game View/screen is required for pixel-space checks.");
        running = true;
        Scene activeScene = SceneManager.GetActiveScene();
        Object[] selection = Selection.objects;
        Object activeSelection = Selection.activeObject;
        var originalScenes = new Dictionary<Scene, Tuple<bool, GameObject[]>>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            originalScenes.Add(scene, Tuple.Create(scene.isDirty, scene.GetRootGameObjects()));
        }
        var reservations = (HashSet<RectTransform>)Field(typeof(UIReservedRegionLayout), "reservedRects").GetValue(null);
        RectTransform[] reservedBefore = reservations.ToArray();
        var occupied = (List<Rect>)Field(typeof(UIReservedRegionLayout), "occupiedScreenRects").GetValue(null);
        Rect[] occupiedBefore = occupied.ToArray();
        var touchRegions = (List<RectTransform>)Field(typeof(UIReservedRegionLayout), "touchControlRegions").GetValue(null);
        RectTransform[] touchRegionsBefore = touchRegions.ToArray();
        FieldInfo layoutEvent = Field(typeof(UIReservedRegionLayout), "LayoutChanging");
        object layoutListeners = layoutEvent.GetValue(null);
        var staticValues = new Dictionary<FieldInfo, object>();
        BackupAndSet(staticValues, Field(typeof(UIReservedRegionLayout), "transitionUntil"), 0f);
        BackupAndSet(staticValues, Field(typeof(UIPanelCoordinator), "<Instance>k__BackingField"), null);
        BackupAndSet(staticValues, Field(typeof(AlmanacManager), "<Instance>k__BackingField"), null);
        BackupAndSet(staticValues, Field(typeof(ObjectiveTrackerUI), "<Instance>k__BackingField"), null);
        BackupAndSet(staticValues, Field(typeof(AchievementPopupNotification), "<Instance>k__BackingField"), null);
        BackupAndSet(staticValues, Field(typeof(TutorialManager), "<Instance>k__BackingField"), null);
        BackupAndSet(staticValues, Field(typeof(GameManager), "<Instance>k__BackingField"), null);
        BackupAndSet(staticValues, Field(typeof(LoadingScreenManager), "<Instance>k__BackingField"), null);
        BackupAndSet(staticValues, Field(typeof(BuildUIController), "<Instance>k__BackingField"), null);
        BackupAndSet(staticValues, Field(typeof(PauseManager), "<Instance>k__BackingField"), null);
        BackupAndSet(staticValues, Field(typeof(LessonUIManager), "<Instance>k__BackingField"), null);
        BackupAndSet(staticValues, Field(typeof(FusionConnectionManager), "<Instance>k__BackingField"), null);
        BackupAndSet(staticValues, Field(typeof(ClipboardManager), "<Instance>k__BackingField"), null);
        BackupAndSet(staticValues, Field(typeof(MultiplayerChallengeLobbyUI), "presentationOwner"), null);
        BackupAndSet(staticValues, Field(typeof(SessionChatUI), "<IsOpen>k__BackingField"), false);
        // Selectable's native Edit Mode registry otherwise includes real scene buttons.
        // Its original array/indices stay untouched while synthetic controls register.
        BackupAndSet(staticValues, Field(typeof(Selectable), "s_Selectables"), new Selectable[32]);
        BackupAndSet(staticValues, Field(typeof(Selectable), "s_SelectableCount"), 0);
        BackupAndSet(staticValues, Field(typeof(UIReservedRegionLayout), "selectableBuffer"), new Selectable[32]);
        BackupAndSet(staticValues, Field(typeof(UIReservedRegionLayout), "touchControlsCached"), true);
        var dialogues = (HashSet<DialogueManager>)Field(typeof(DialogueManager), "knownManagers").GetValue(null);
        DialogueManager[] dialoguesBefore = dialogues.ToArray();
        Scene temporary = default;
        GameObject fixture = null;
        var report = new StringBuilder();
        try
        {
            layoutEvent.SetValue(null, null);
            reservations.Clear();
            occupied.Clear();
            touchRegions.Clear();
            dialogues.Clear();
            temporary = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(temporary);
            fixture = new GameObject("Notification Pause Placement Validation (Disposable)");
            SceneManager.MoveGameObjectToScene(fixture, temporary);
            ValidatePausePlacement(fixture.transform, report);
            reservations.Clear();
            occupied.Clear();

        }
        finally
        {
            if (fixture != null) Object.DestroyImmediate(fixture);
            if (activeScene.IsValid() && activeScene.isLoaded) SceneManager.SetActiveScene(activeScene);
            if (temporary.IsValid() && temporary.isLoaded) EditorSceneManager.CloseScene(temporary, true);
            reservations.Clear();
            foreach (RectTransform rect in reservedBefore) reservations.Add(rect);
            occupied.Clear();
            occupied.AddRange(occupiedBefore);
            touchRegions.Clear();
            touchRegions.AddRange(touchRegionsBefore);
            dialogues.Clear();
            foreach (DialogueManager manager in dialoguesBefore) dialogues.Add(manager);
            foreach (var entry in staticValues) entry.Key.SetValue(null, entry.Value);
            layoutEvent.SetValue(null, layoutListeners);
            Selection.objects = selection;
            Selection.activeObject = activeSelection;
            running = false;
        }
        Check(SceneManager.sceneCount == originalScenes.Count, "Fixture changed the loaded scene count.");
        foreach (var entry in originalScenes)
        {
            Check(entry.Key.isDirty == entry.Value.Item1, "Fixture changed a user's scene dirty state.");
            Check(entry.Key.GetRootGameObjects().SequenceEqual(entry.Value.Item2), "Fixture changed user-authored scene roots.");
        }
        Check(reservations.SetEquals(reservedBefore) && dialogues.SetEquals(dialoguesBefore), "Existing UI registries were not restored.");
        Check(SceneManager.GetActiveScene() == activeScene && Selection.objects.SequenceEqual(selection) &&
            Selection.activeObject == activeSelection, "Active scene or selection was not restored.");
        report.AppendLine("PASS: Original scene roots, dirty states, active scene, selection, reservations, listeners and singleton values restored. No live panel opened, achievement unlocked, reward awarded, network RPC, player-data write, scene save or asset save.");
        report.AppendLine("Screen: " + Screen.width + "x" + Screen.height + "; safe area: " + Screen.safeArea);
        report.AppendLine("Completed UTC: " + DateTime.UtcNow.ToString("O"));
        return report.ToString();
    }

    private static void ValidatePausePlacement(Transform fixture, StringBuilder report)
    {
        Rect safe = Screen.safeArea;
        if (safe.width <= 0f || safe.height <= 0f) safe = new Rect(0f, 0f, Screen.width, Screen.height);
        Canvas hud = MakeCanvas(fixture, "Synthetic HUD", 1f);
        RectTransform pauseRect = MakeRect(hud.transform, "Synthetic Pause Button",
            new Vector2(safe.center.x, safe.yMax - safe.height * .10f),
            new Vector2(safe.height * .07f, safe.height * .07f), 1f);
        Image pauseImage = pauseRect.GetComponent<Image>();
        pauseImage.raycastPadding = new Vector4(-10f, -20f, -10f, -20f);
        GameObject pauseHost = InactiveChild(fixture, "Synthetic Pause Manager");
        PauseManager pause = pauseHost.AddComponent<PauseManager>();
        pause.pauseButton = pauseRect.gameObject;
        Field(typeof(PauseManager), "<Instance>k__BackingField").SetValue(null, pause);

        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
        Check(asset != null, "Notification prefab is missing.");
        GameObject clone = Object.Instantiate(asset, fixture, false);
        AchievementPopupNotification popup = clone.GetComponent<AchievementPopupNotification>();
        Check(popup != null, "Notification prefab lacks its controller.");
        Call(popup, "ResolveVisualReferences");
        GameObject root = (GameObject)Get(popup, "popupRoot");
        var rect = (RectTransform)root.transform;
        var group = root.GetComponent<CanvasGroup>();
        Canvas canvas = clone.GetComponent<Canvas>();
        CanvasScaler scaler = clone.GetComponent<CanvasScaler>();
        if (scaler != null) scaler.enabled = false;
        root.SetActive(true);
        group.alpha = .65f;
        Check(root.GetComponentsInChildren<Graphic>(true).All(graphic => !graphic.raycastTarget),
            "Notification graphics consume gameplay input.");

        float[] toastScales = { .55f, .8f, 1.25f };
        float[] hudScales = { 1.3f, .7f, 1f };
        Vector2 stable = Vector2.zero;
        for (int index = 0; index < toastScales.Length; index++)
        {
            canvas.scaleFactor = toastScales[index];
            hud.scaleFactor = hudScales[index];
            rect.sizeDelta = new Vector2(safe.width * .34f, safe.height * .12f) / toastScales[index];
            pauseRect.sizeDelta = new Vector2(safe.height * .07f, safe.height * .07f) / hudScales[index];
            Canvas.ForceUpdateCanvases();
            Check(UIReservedRegionLayout.TrySetScreenCenter(pauseRect,
                new Vector2(safe.center.x, safe.yMax - safe.height * .10f)), "Pause fixture could not be positioned.");
            Call(popup, "ResetPlacementRetry");
            Call(popup, "ForceAbsoluteOverlay");
            Check(canvas.enabled, "Fixed placement failed at independent toast/HUD scales.");
            Check(UIReservedRegionLayout.TryGetScreenRect(rect, out Rect toastBounds), "Notification bounds cannot be measured.");
            Check(UIReservedRegionLayout.TryGetScreenRect(pauseRect, out Rect pauseBounds), "Pause bounds cannot be measured.");
            float pauseScreenScale = pauseBounds.height / pauseRect.rect.height;
            float touchBottom = pauseBounds.yMin + pauseImage.raycastPadding.y * pauseScreenScale;
            float gap = (float)Get(popup, "protectedPanelGap") * canvas.scaleFactor;
            Check(Mathf.Abs(toastBounds.center.x - pauseBounds.center.x) < 1f,
                "Notification is not centered beneath Pause.");
            Check(Mathf.Abs(toastBounds.yMax - (touchBottom - gap)) < 1.5f,
                "Notification is not directly below Pause's padded touch area.");
            Check(toastBounds.xMin >= safe.xMin && toastBounds.xMax <= safe.xMax &&
                  toastBounds.yMin >= safe.yMin && toastBounds.yMax <= safe.yMax, "Notification escapes the safe area.");
            stable = rect.anchoredPosition;
            Call(popup, "ForceAbsoluteOverlay");
            Check(rect.anchoredPosition == stable, "Identical frames move the notification.");
            Check(!group.blocksRaycasts && !group.interactable, "Notification group consumes gameplay input.");
        }
        report.AppendLine("PASS: Authored notification prefab stays centered immediately below Pause's padded touch bounds at three independent toast/HUD canvas-scale pairs; safe bounds, stable position and non-raycasting preserved.");

        object queue = Get(popup, "pendingNotifications");
        MethodInfo enqueue = queue.GetType().GetMethod("Enqueue");
        enqueue.Invoke(queue, new[] { Sample("First notification") });
        enqueue.Invoke(queue, new[] { Sample("Second notification") });
        IEnumerator play = (IEnumerator)Call(popup, "PlayQueuedNotifications");
        Action assertQueued = () =>
        {
            Call(popup, "ResetPlacementRetry");
            Check(play.MoveNext() && Count(queue) == 2, "Priority UI consumes or reorders queued notifications.");
            Call(popup, "ForceAbsoluteOverlay");
            Check(!canvas.enabled, "Notification renders while priority UI is active.");
        };
        pauseRect.gameObject.SetActive(false);
        assertQueued();
        pauseRect.gameObject.SetActive(true);
        pause.pauseButton = null;
        assertQueued();
        pause.pauseButton = pauseRect.gameObject;

        Call(popup, "ResetPlacementRetry");
        Call(popup, "ForceAbsoluteOverlay");
        Check(canvas.enabled, "Notification does not resume after restoring Pause.");
        Check(UIReservedRegionLayout.TryGetScreenRect(rect, out Rect unobstructedBounds),
            "Restored notification cannot be measured.");
        RectTransform obstruction = MakeRect(hud.transform, "Synthetic Occupied Fixed Notification Slot",
            unobstructedBounds.center, unobstructedBounds.size, hud.scaleFactor);
        UIReservedRegionLayout.Register(obstruction);
        stable = rect.anchoredPosition;
        assertQueued();
        Check(rect.anchoredPosition == stable, "An occupied fixed slot moves the notification elsewhere.");
        UIReservedRegionLayout.Unregister(obstruction);
        obstruction.gameObject.SetActive(false);

        GameObject gameHost = InactiveChild(fixture, "Synthetic Game Manager");
        GameManager game = gameHost.AddComponent<GameManager>();
        Field(typeof(GameManager), "<Instance>k__BackingField").SetValue(null, game);
        foreach (GameManager.GameState state in new[] { GameManager.GameState.Building, GameManager.GameState.CargoTesting })
        {
            Field(typeof(GameManager), "<CurrentState>k__BackingField").SetValue(game, state);
            assertQueued();
        }
        Field(typeof(GameManager), "<CurrentState>k__BackingField").SetValue(game, GameManager.GameState.Normal);
        Field(typeof(GameManager), "isTransitioning").SetValue(game, true);
        assertQueued();
        Field(typeof(GameManager), "isTransitioning").SetValue(game, false);

        GameObject almanacHost = InactiveChild(fixture, "Synthetic Almanac Manager");
        AlmanacManager almanac = almanacHost.AddComponent<AlmanacManager>();
        almanac.almanacCanvas = Child(fixture, "Synthetic Almanac Panel");
        Field(typeof(AlmanacManager), "<Instance>k__BackingField").SetValue(null, almanac);
        assertQueued();
        almanac.almanacCanvas.SetActive(false);
        Field(typeof(AlmanacManager), "isAnimating").SetValue(almanac, true);
        assertQueued();
        Field(typeof(AlmanacManager), "isAnimating").SetValue(almanac, false);

        GameObject objectiveHost = InactiveChild(fixture, "Synthetic Objective Manager");
        ObjectiveTrackerUI objective = objectiveHost.AddComponent<ObjectiveTrackerUI>();
        objective.trackerPanel = Child(fixture, "Synthetic Objectives And Contract Acceptance Panel");
        Field(typeof(ObjectiveTrackerUI), "<Instance>k__BackingField").SetValue(null, objective);
        assertQueued();
        // Offers and objective details intentionally share this authored root.
        Check(typeof(ObjectiveTrackerUI).GetMethod("ShowContractOffer", Instance) != null,
            "Contract acceptance no longer shares the expected ObjectiveTracker API.");
        objective.trackerPanel.SetActive(false);

        GameObject coordinatorHost = InactiveChild(fixture, "Synthetic Modal Coordinator");
        UIPanelCoordinator coordinator = coordinatorHost.AddComponent<UIPanelCoordinator>();
        Type frameType = typeof(UIPanelCoordinator).GetNestedType("PanelFrame", BindingFlags.NonPublic);
        object frame = Activator.CreateInstance(frameType, true);
        frameType.GetField("panel", Instance).SetValue(frame, Child(fixture, "Synthetic Modal"));
        object stack = Get(coordinator, "panelStack");
        stack.GetType().GetMethod("Push").Invoke(stack, new[] { frame });
        Field(typeof(UIPanelCoordinator), "<Instance>k__BackingField").SetValue(null, coordinator);
        assertQueued();
        Field(typeof(UIPanelCoordinator), "<Instance>k__BackingField").SetValue(null, null);

        UIReservedRegionLayout.NotifyTransition(10f);
        assertQueued();
        Field(typeof(UIReservedRegionLayout), "transitionUntil").SetValue(null, 0f);
        Call(popup, "ResetPlacementRetry");
        Check(play.MoveNext() && Count(queue) == 1, "Queued notifications do not resume when all blockers close.");
        Check(((TMP_Text)Get(popup, "achievementNameText")).text == "First notification", "FIFO first entry changed.");
        object second = queue.GetType().GetMethod("Peek").Invoke(queue, null);
        Check((string)second.GetType().GetField("title").GetValue(second) == "Second notification", "FIFO second entry changed.");
        Call(popup, "ForceAbsoluteOverlay");
        Check(canvas.enabled, "Toast does not restore after priority blockers close.");
        IEnumerator activeFade = play.Current as IEnumerator;
        Check(activeFade != null, "Queue did not begin the pending fade.");
        IEnumerator activeHold = (IEnumerator)Call(popup, "HoldPopup");
        group.alpha = .65f;
        stable = rect.anchoredPosition;
        objective.trackerPanel.SetActive(true);
        for (int iteration = 0; iteration < 20; iteration++)
        {
            Check(activeFade.MoveNext() && activeHold.MoveNext(), "An interrupted notification expires while hidden.");
            Check(!canvas.enabled && group.alpha == .65f && rect.anchoredPosition == stable,
                "A visible notification interruption moves, fades or renders it.");
        }
        objective.trackerPanel.SetActive(false);
        Call(popup, "ResetPlacementRetry");
        Call(popup, "ForceAbsoluteOverlay");
        Check(canvas.enabled && rect.anchoredPosition == stable && group.alpha == .65f && Count(queue) == 1,
            "Clearing the interruption does not restore the same notification and pending queue.");
        Check(((TMP_Text)Get(popup, "achievementNameText")).text == "First notification", "Interruption replaced the active notification.");
        report.AppendLine("PASS: Missing/hidden Pause, occupied fixed slot, both non-Normal game states, game/UI transitions, direct Almanac open/animation, direct Objectives/contract-acceptance root and modal coordinator all defer the FIFO without dequeue. An occupied slot does not move the card elsewhere. Opening Objectives mid-toast hides it immediately; 20 fade/hold steps preserve alpha/position/readable time, then restore the same toast with the second still queued.");
        // Do not dispose/advance the queue: the synthetic object is destroyed by the fixture.
    }

    private static object Sample(string title)
    {
        Type type = typeof(AchievementPopupNotification).GetNestedType("PopupRequest", BindingFlags.NonPublic);
        object request = Activator.CreateInstance(type);
        type.GetField("title").SetValue(request, title);
        type.GetField("detail").SetValue(request, "Notification layout fixture");
        type.GetField("kind").SetValue(request, Enum.Parse(type.GetField("kind").FieldType, "Feature"));
        return request;
    }

    private static Canvas MakeCanvas(Transform parent, string name, float scale)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Canvas));
        obj.transform.SetParent(parent, false);
        Canvas canvas = obj.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.scaleFactor = scale;
        Canvas.ForceUpdateCanvases();
        return canvas;
    }

    private static RectTransform MakeRect(Transform parent, string name, Vector2 center, Vector2 pixels, float scale)
    {
        var rect = (RectTransform)new GameObject(name, typeof(RectTransform), typeof(Image)).transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
        rect.sizeDelta = pixels / scale;
        Canvas.ForceUpdateCanvases();
        Check(UIReservedRegionLayout.TrySetScreenCenter(rect, center), "Fixture rectangle could not be positioned.");
        return rect;
    }

    private static GameObject Child(Transform parent, string name)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent, false);
        return child;
    }

    private static GameObject InactiveChild(Transform parent, string name)
    {
        GameObject child = Child(parent, name);
        child.SetActive(false);
        return child;
    }

    private static void BackupAndSet(Dictionary<FieldInfo, object> values, FieldInfo field, object value)
    {
        values.Add(field, field.GetValue(null));
        field.SetValue(null, value);
    }

    private static FieldInfo Field(Type type, string name)
    {
        FieldInfo field = type.GetField(name, Static | Instance);
        Check(field != null, "Expected fixture field is missing: " + type.Name + "." + name);
        return field;
    }

    private static object Get(object obj, string name) => Field(obj.GetType(), name).GetValue(obj);
    private static object Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, Instance).Invoke(obj, args);
    private static int Count(object queue) => (int)queue.GetType().GetProperty("Count").GetValue(queue);
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
