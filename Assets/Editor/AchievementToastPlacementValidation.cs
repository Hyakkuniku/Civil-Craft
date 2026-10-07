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

/// <summary>Opt-in, disposable smart-toast regressions. Never unlocks achievements or saves a scene.</summary>
public static class AchievementToastPlacementValidation
{
    private const string Request = "Temp/achievement-smart-toast-validation.request";
    private const string Report = "Temp/AchievementSmartToastValidation.txt";
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

    [MenuItem("Tools/Civil Craft/Validate Smart Achievement Toast")]
    public static void ValidateFromCommandLine()
    {
        Directory.CreateDirectory("Temp");
        try
        {
            File.WriteAllText(Report, Validate());
            Debug.Log("[Smart Achievement Toast] PASS: " + Report);
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
            fixture = new GameObject("Smart Achievement Toast Validation (Disposable)");
            SceneManager.MoveGameObjectToScene(fixture, temporary);
            ValidatePlacement(fixture.transform, report);
            reservations.Clear();
            occupied.Clear();
            ValidateNotificationPriority(fixture.transform, dialogues, report);
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

    private static void ValidatePlacement(Transform fixture, StringBuilder report)
    {
        Rect safe = SafeArea();
        float margin = safe.height * .02f;
        float gap = safe.height * .01f;
        Canvas itemCanvas = MakeCanvas(fixture, "Toast Canvas 0.7x", .7f);
        Canvas hudCanvas = MakeCanvas(fixture, "HUD Canvas 1.35x", 1.35f);
        RectTransform item = MakeRect(itemCanvas.transform, "Toast", safe.center,
            new Vector2(safe.width * .30f, safe.height * .11f), itemCanvas.scaleFactor);
        Vector2 preferred = new Vector2(safe.center.x, safe.yMax - margin - safe.height * .055f);
        Canvas.ForceUpdateCanvases();
        Check(Find(item, preferred, false, margin, gap, out Vector2 initial), "Empty top-edge space was rejected.");
        Check(InSafeArea(item, initial, safe, margin) && IsTopEdge(item, initial, safe), "Empty placement is outside safe area or not at the top.");
        Check(UIReservedRegionLayout.TrySetScreenCenter(item, initial), "Pixel-space placement could not be applied.");
        Vector2 oldBottom = new Vector2(safe.center.x, safe.yMin + margin + safe.height * .055f);
        Check(UIReservedRegionLayout.TrySetScreenCenter(item, oldBottom), "Legacy bottom-position fixture failed.");
        Check(Find(item, preferred, true, margin, gap, out Vector2 correctedTop) && IsTopEdge(item, correctedTop, safe),
            "Stable-position logic incorrectly retained a legacy bottom position.");
        Check(UIReservedRegionLayout.TrySetScreenCenter(item, correctedTop), "Legacy position could not be corrected to the top.");

        RectTransform bottomHud = MakeRect(hudCanvas.transform, "Bottom Material/Stress/Tool Docks",
            new Vector2(safe.center.x, safe.yMin + safe.height * .12f),
            new Vector2(safe.width, safe.height * .24f), hudCanvas.scaleFactor);
        RectTransform tutorial = MakeRect(hudCanvas.transform, "Centre Tutorial",
            safe.center, new Vector2(safe.width * .76f, safe.height * .32f), hudCanvas.scaleFactor);
        UIReservedRegionLayout.Register(bottomHud);
        UIReservedRegionLayout.Register(tutorial);
        Canvas.ForceUpdateCanvases();
        Check(Find(item, preferred, true, margin, gap, out Vector2 clear), "A clear top edge was not found when bottom HUD is occupied.");
        Check(InSafeArea(item, clear, safe, margin) && IsTopEdge(item, clear, safe), "Avoidance moved the toast away from the top.");
        Check(!Intersects(item, clear, bottomHud, gap) && !Intersects(item, clear, tutorial, gap), "Toast covers independent-scale HUD or tutorial bounds.");
        Check(UIReservedRegionLayout.TrySetScreenCenter(item, clear), "Avoidance position could not be applied.");
        Check(Find(item, preferred, true, margin, gap, out Vector2 stable) && Vector2.Distance(stable, clear) < .75f,
            "A safe current top-edge placement jitters back to another position.");

        RectTransform topHud = MakeRect(hudCanvas.transform, "Top HUD/Notification Band",
            new Vector2(safe.center.x, safe.yMax - safe.height * .16f),
            new Vector2(safe.width, safe.height * .32f), hudCanvas.scaleFactor);
        UIReservedRegionLayout.Register(topHud);
        Canvas.ForceUpdateCanvases();
        Check(!Find(item, preferred, true, margin, gap, out _), "Toast was placed through HUD or away from the top when the top is occupied.");

        bottomHud.gameObject.SetActive(false);
        Check(!Find(item, preferred, true, margin, gap, out _),
            "A fully occupied top fell back to the clear bottom edge.");
        topHud.gameObject.SetActive(false);
        Check(Find(item, preferred, false, margin, gap, out Vector2 afterHide) && !Intersects(item, afterHide, tutorial, gap),
            "Inactive HUD reservations still prevent a free top edge.");
        bottomHud.gameObject.SetActive(true);
        topHud.gameObject.SetActive(true);
        hudCanvas.enabled = false;
        Check(Find(item, preferred, false, margin, gap, out _), "A disabled Canvas still occupies screen space.");
        hudCanvas.enabled = true;

        CanvasGroup fadedHud = hudCanvas.gameObject.AddComponent<CanvasGroup>();
        fadedHud.alpha = 0f;
        Check(Find(item, preferred, false, margin, gap, out _), "Zero-alpha HUD reservations occupy screen space.");
        CanvasGroup independentTop = topHud.gameObject.AddComponent<CanvasGroup>();
        independentTop.ignoreParentGroups = true;
        independentTop.alpha = 1f;
        Check(UIReservedRegionLayout.IsRenderable(topHud) && !UIReservedRegionLayout.IsRenderable(bottomHud),
            "ignoreParentGroups does not respect the independently visible child.");
        independentTop.alpha = 0f;
        Check(!UIReservedRegionLayout.IsRenderable(topHud), "A child's zero alpha is ignored when ignoreParentGroups is true.");
        independentTop.ignoreParentGroups = false;
        independentTop.alpha = .02f;
        fadedHud.alpha = .02f;
        Check(!UIReservedRegionLayout.IsRenderable(topHud), "Combined nearly-invisible parent/child alpha occupies screen space.");
        independentTop.ignoreParentGroups = true;
        Check(UIReservedRegionLayout.IsRenderable(topHud), "ignoreParentGroups multiplies an ignored parent opacity.");
        independentTop.alpha = 1f;
        fadedHud.alpha = 1f;
        UIReservedRegionLayout.Unregister(bottomHud);
        UIReservedRegionLayout.Unregister(topHud);
        UIReservedRegionLayout.Unregister(tutorial);

        RectTransform button = MakeRect(hudCanvas.transform, "Unregistered Mobile Control", preferred,
            new Vector2(safe.width * .22f, safe.height * .15f), hudCanvas.scaleFactor);
        button.gameObject.AddComponent<Button>();
        Canvas.ForceUpdateCanvases();
        Check(Find(item, preferred, false, margin, gap, out Vector2 avoidingButton) &&
            !Intersects(item, avoidingButton, button, gap), "An unregistered visible mobile control is covered.");
        Object.DestroyImmediate(button.gameObject);

        GameObject buildObject = Child(fixture, "Isolated Action Log Owner");
        buildObject.SetActive(false);
        var build = buildObject.AddComponent<BuildUIController>();
        RectTransform textRect = (RectTransform)new GameObject("Visible Build Action Log", typeof(RectTransform)).transform;
        textRect.SetParent(hudCanvas.transform, false);
        textRect.anchorMin = textRect.anchorMax = textRect.pivot = Vector2.one * .5f;
        textRect.sizeDelta = new Vector2(safe.width * .45f, safe.height * .14f) / hudCanvas.scaleFactor;
        var actionText = textRect.gameObject.AddComponent<TextMeshProUGUI>();
        actionText.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/Bekind Sans SDF.asset");
        actionText.text = "Copied bridge";
        build.actionLogText = actionText;
        Field(typeof(BuildUIController), "<Instance>k__BackingField").SetValue(null, build);
        Check(UIReservedRegionLayout.TrySetScreenCenter(textRect, preferred), "Action-log fixture placement failed.");
        Check(Find(item, preferred, false, margin, gap, out Vector2 avoidingLog) &&
            !Intersects(item, avoidingLog, textRect, gap), "Visible non-interactive build action text is covered.");
        actionText.enabled = false;
        Check(Find(item, preferred, false, margin, gap, out Vector2 disabledLog) && Vector2.Distance(disabledLog, preferred) < .75f,
            "Disabled build action text still occupies toast space.");
        actionText.enabled = true;
        actionText.text = string.Empty;
        Check(Find(item, preferred, false, margin, gap, out Vector2 emptyLog) && Vector2.Distance(emptyLog, preferred) < .75f,
            "Empty build action text still occupies toast space.");
        Field(typeof(BuildUIController), "<Instance>k__BackingField").SetValue(null, null);
        Object.DestroyImmediate(textRect.gameObject);
        Object.DestroyImmediate(buildObject);

        CanvasGroup transparentItem = item.gameObject.AddComponent<CanvasGroup>();
        transparentItem.alpha = 0f;
        Check(Find(item, preferred, false, margin, gap, out _), "A toast cannot acquire initial placement at alpha zero.");

        item.sizeDelta = new Vector2(safe.width * 1.1f / itemCanvas.scaleFactor, safe.height * .11f / itemCanvas.scaleFactor);
        Canvas.ForceUpdateCanvases();
        Check(!Find(item, preferred, false, margin, gap, out _), "An over-wide toast is clipped instead of deferred.");
        Object.DestroyImmediate(itemCanvas.gameObject);
        Object.DestroyImmediate(hudCanvas.gameObject);
        report.AppendLine("PASS: Top-only screen-pixel placement fits the safe area, corrects legacy bottom positions, avoids bottom docks, central tutorial reservations, unregistered mobile buttons and visible build-action text across 0.7x/1.35x Canvas scales, remains stable once safe, defers when the top is occupied even with a clear bottom, ignores inactive/disabled/zero-alpha HUD and disabled/empty action text, respects ignoreParentGroups, measures alpha-zero toast and rejects oversized items.");
    }

    private static void ValidateNotificationPriority(Transform fixture, HashSet<DialogueManager> dialogues, StringBuilder report)
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
        Check(asset != null, "The existing toast prefab is missing.");
        GameObject clone = Object.Instantiate(asset, fixture, false);
        AchievementPopupNotification popup = clone.GetComponent<AchievementPopupNotification>();
        Check(popup != null, "The existing toast prefab has no notification controller.");
        Call(popup, "ResolveVisualReferences");
        GameObject root = (GameObject)Get(popup, "popupRoot");
        RectTransform rect = (RectTransform)root.transform;
        Canvas canvas = clone.GetComponent<Canvas>();
        root.SetActive(true);
        var group = root.GetComponent<CanvasGroup>();
        Check(root.GetComponentsInChildren<Graphic>(true).All(graphic => !graphic.raycastTarget), "Toast graphics consume gameplay input.");
        Call(popup, "ForceAbsoluteOverlay");
        Check(!group.blocksRaycasts && !group.interactable, "Toast CanvasGroup consumes gameplay input.");

        GameObject dialogueObject = Child(fixture, "Priority Dialogue");
        var dialogue = dialogueObject.AddComponent<DialogueManager>();
        dialogues.Add(dialogue);
        GameObject box = Child(dialogueObject.transform, "Closing Dialogue Box");
        Field(dialogue.GetType(), "dialogueBox").SetValue(dialogue, box);
        Field(dialogue.GetType(), "isDialogueActive").SetValue(dialogue, true);
        object queue = Get(popup, "pendingNotifications");
        MethodInfo enqueue = queue.GetType().GetMethod("Enqueue");
        enqueue.Invoke(queue, new[] { Sample("First notification") });
        enqueue.Invoke(queue, new[] { Sample("Second notification") });
        IEnumerator play = (IEnumerator)Call(popup, "PlayQueuedNotifications");
        Check(play.MoveNext() && Count(queue) == 2, "An interrupted queue dequeues or drops notifications.");
        Call(popup, "ForceAbsoluteOverlay");
        Check(!canvas.enabled, "Toast renders over a dialogue.");
        Field(dialogue.GetType(), "isDialogueActive").SetValue(dialogue, false);
        Check(play.MoveNext() && Count(queue) == 2, "A closing dialogue drops or releases queued notifications early.");

        Vector2 position = rect.anchoredPosition;
        float alpha = group.alpha;
        IEnumerator slide = (IEnumerator)Call(popup, "AnimatePopup", 0f, 1f);
        IEnumerator hold = (IEnumerator)Call(popup, "HoldPopup");
        for (int i = 0; i < 12; i++)
        {
            Check(slide.MoveNext() && hold.MoveNext(), "A hidden toast consumes its animation/visible time.");
            Check(rect.anchoredPosition == position && group.alpha == alpha, "A hidden toast advances its slide or alpha.");
        }
        box.SetActive(false);
        dialogues.Remove(dialogue);
        Field(typeof(SessionChatUI), "<IsOpen>k__BackingField").SetValue(null, true);
        Check(play.MoveNext() && Count(queue) == 2, "Open multiplayer chat does not defer queued notifications.");
        Call(popup, "ForceAbsoluteOverlay");
        Check(!canvas.enabled, "Toast renders over multiplayer chat.");
        Field(typeof(SessionChatUI), "<IsOpen>k__BackingField").SetValue(null, false);

        GameObject coordinatorObject = Child(fixture, "Priority Modal Coordinator");
        coordinatorObject.SetActive(false);
        var coordinator = coordinatorObject.AddComponent<UIPanelCoordinator>();
        Type frameType = typeof(UIPanelCoordinator).GetNestedType("PanelFrame", BindingFlags.NonPublic);
        Check(frameType != null, "Modal-stack fixture type is missing.");
        object frame = Activator.CreateInstance(frameType, true);
        frameType.GetField("panel", Instance).SetValue(frame, Child(fixture, "Temporary Priority Modal"));
        object stack = Get(coordinator, "panelStack");
        stack.GetType().GetMethod("Push").Invoke(stack, new[] { frame });
        Field(typeof(UIPanelCoordinator), "<Instance>k__BackingField").SetValue(null, coordinator);
        Check(play.MoveNext() && Count(queue) == 2, "A coordinated modal does not defer queued notifications.");
        Call(popup, "ForceAbsoluteOverlay");
        Check(!canvas.enabled, "Toast renders over a coordinated modal.");
        Field(typeof(UIPanelCoordinator), "<Instance>k__BackingField").SetValue(null, null);

        GameObject loadingObject = Child(fixture, "Priority Loading State");
        loadingObject.SetActive(false);
        var loading = loadingObject.AddComponent<LoadingScreenManager>();
        Field(typeof(LoadingScreenManager), "isLoading").SetValue(loading, true);
        Field(typeof(LoadingScreenManager), "<Instance>k__BackingField").SetValue(null, loading);
        Check(play.MoveNext() && Count(queue) == 2, "Loading does not defer queued notifications.");
        Call(popup, "ForceAbsoluteOverlay");
        Check(!canvas.enabled, "Toast renders over loading.");
        Field(typeof(LoadingScreenManager), "<Instance>k__BackingField").SetValue(null, null);

        GameObject gameObject = Child(fixture, "Priority Game Transition State");
        gameObject.SetActive(false);
        var game = gameObject.AddComponent<GameManager>();
        Field(typeof(GameManager), "isTransitioning").SetValue(game, true);
        Field(typeof(GameManager), "<Instance>k__BackingField").SetValue(null, game);
        Check(play.MoveNext() && Count(queue) == 2, "A game transition does not defer queued notifications.");
        Call(popup, "ForceAbsoluteOverlay");
        Check(!canvas.enabled, "Toast renders during a game transition.");
        Field(typeof(GameManager), "<Instance>k__BackingField").SetValue(null, null);

        UIReservedRegionLayout.NotifyTransition(10f);
        Check(play.MoveNext() && Count(queue) == 2, "A UI transition does not defer queued notifications.");
        Call(popup, "ForceAbsoluteOverlay");
        Check(!canvas.enabled, "Toast renders during a UI transition.");
        Field(typeof(UIReservedRegionLayout), "transitionUntil").SetValue(null, 0f);

        Canvas busyCanvas = MakeCanvas(fixture, "Top-Occupied Bottom-Clear HUD", 1f);
        Rect safe = SafeArea();
        RectTransform busy = MakeRect(busyCanvas.transform, "Entire Top Band Occupied",
            new Vector2(safe.center.x, safe.yMax - safe.height * .16f),
            new Vector2(safe.width, safe.height * .32f), 1f);
        UIReservedRegionLayout.Register(busy);
        Canvas.ForceUpdateCanvases();
        Check(play.MoveNext() && Count(queue) == 2, "Top-occupied/bottom-clear deferral consumes a queued notification.");
        Call(popup, "ForceAbsoluteOverlay");
        Check(!canvas.enabled, "Toast falls back to the clear bottom when the top is occupied.");
        UIReservedRegionLayout.Unregister(busy);
        busy.gameObject.SetActive(false);
        Call(popup, "ResetPlacementRetry");
        Check(play.MoveNext() && Count(queue) == 1, "The queue does not resume when priority UI and top space clear.");
        Check(((TMP_Text)Get(popup, "achievementNameText")).text == "First notification",
            "Queue resumed with the wrong notification.");
        object second = queue.GetType().GetMethod("Peek").Invoke(queue, null);
        Check((string)second.GetType().GetField("title").GetValue(second) == "Second notification",
            "Queue order changed after interruptions.");
        Call(popup, "ForceAbsoluteOverlay");
        Check(canvas.enabled, "An available top edge does not restore toast rendering.");
        Check(UIReservedRegionLayout.TryGetScreenRect(rect, out Rect visibleBounds) && IsTopEdge(rect, visibleBounds.center, safe),
            "Resumed notification still uses the prefab's authored bottom anchors.");

        IEnumerator activeFade = play.Current as IEnumerator;
        Check(activeFade != null, "Resumed queue did not enter the fade animation.");
        group.alpha = .65f;
        position = rect.anchoredPosition;
        alpha = group.alpha;
        busy.gameObject.SetActive(true);
        UIReservedRegionLayout.Register(busy);
        IEnumerator activeHold = (IEnumerator)Call(popup, "HoldPopup");
        for (int i = 0; i < 12; i++)
        {
            Check(activeFade.MoveNext() && activeHold.MoveNext(), "Top-occupied/bottom-clear interruption expires fade/hold clocks.");
            Check(!canvas.enabled && rect.anchoredPosition == position && group.alpha == alpha,
                "A top-occupied/bottom-clear interruption renders at the bottom or advances the active toast.");
        }
        UIReservedRegionLayout.Unregister(busy);
        busy.gameObject.SetActive(false);
        Call(popup, "ResetPlacementRetry");
        Call(popup, "ForceAbsoluteOverlay");
        Check(canvas.enabled && rect.anchoredPosition == position && group.alpha == alpha,
            "Clearing a temporary obstruction does not restore the same toast and fade state.");
        report.AppendLine("PASS: Existing prefab remains non-raycasting; FIFO requests stay queued during dialogue including its close animation, multiplayer chat, coordinated modals, loading, game/UI transitions and top-occupied/bottom-clear conditions. First notification resumes at the top with second still queued. Dialogue/top-space interruptions leave fade/hold enumerators pending without changing position/alpha, and the same top toast restores after clearance; no bottom fallback.");
    }

    private static object Sample(string title)
    {
        Type type = typeof(AchievementPopupNotification).GetNestedType("PopupRequest", BindingFlags.NonPublic);
        object request = Activator.CreateInstance(type);
        type.GetField("title").SetValue(request, title);
        type.GetField("detail").SetValue(request, "Permanent feature unlock");
        type.GetField("kind").SetValue(request, Enum.Parse(type.GetField("kind").FieldType, "Feature"));
        return request;
    }

    private static bool Find(RectTransform item, Vector2 preferred, bool stable, float margin, float gap, out Vector2 chosen)
    {
        MethodInfo method = typeof(UIReservedRegionLayout).GetMethod("TryFindTopEdgePlacement", Static);
        Check(method != null, "Smart top-edge-placement API is not loaded. Refresh Unity before running validation.");
        object[] args = { item, preferred, stable, margin, gap, Vector2.zero };
        bool found = (bool)method.Invoke(null, args);
        chosen = (Vector2)args[5];
        return found;
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
        Check(UIReservedRegionLayout.TrySetScreenCenter(rect, center), "Fixture rectangle could not be placed in screen pixels.");
        return rect;
    }

    private static Rect SafeArea()
    {
        Rect safe = Screen.safeArea;
        return safe.width > 0f && safe.height > 0f ? safe : new Rect(0, 0, Screen.width, Screen.height);
    }

    private static Rect At(RectTransform item, Vector2 center)
    {
        Check(UIReservedRegionLayout.TryGetScreenRect(item, out Rect bounds), "Fixture rectangle has no rendered bounds.");
        bounds.center = center;
        return bounds;
    }

    private static bool InSafeArea(RectTransform item, Vector2 center, Rect safe, float margin)
    {
        Rect rect = At(item, center);
        return rect.xMin >= safe.xMin + margin - .75f && rect.xMax <= safe.xMax - margin + .75f &&
            rect.yMin >= safe.yMin + margin - .75f && rect.yMax <= safe.yMax - margin + .75f;
    }

    private static bool IsTopEdge(RectTransform item, Vector2 center, Rect safe)
    {
        Rect rect = At(item, center);
        return rect.yMin >= safe.yMax - safe.height * .31f;
    }

    private static bool Intersects(RectTransform item, Vector2 center, RectTransform reservation, float gap)
    {
        Check(UIReservedRegionLayout.TryGetScreenRect(reservation, out Rect occupied), "Reservation has no bounds.");
        occupied = Rect.MinMaxRect(occupied.xMin - gap, occupied.yMin - gap, occupied.xMax + gap, occupied.yMax + gap);
        return At(item, center).Overlaps(occupied);
    }

    private static GameObject Child(Transform parent, string name)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent, false);
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
