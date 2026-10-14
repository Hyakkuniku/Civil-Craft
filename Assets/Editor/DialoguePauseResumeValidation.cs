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

/// <summary>Opt-in Edit Mode regression checks using disposable UI, never live scene panels.</summary>
public static class DialoguePauseResumeValidation
{
    // A new request name prevents an older imported helper from consuming the
    // control-restoration check before this extended fixture is compiled.
    private const string Request = "Temp/dialogue-pause-controls-validation.request";
    private const string Report = "Temp/DialoguePauseControlsValidation.txt";
    private const string ControllerGuid = "b5cbd34b014aa30448917c62078e8895";
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static bool running;
    private static double nextCheck;
    private static bool Busy => running || EditorApplication.isCompiling || EditorApplication.isUpdating ||
        EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer ||
        PrefabStageUtility.GetCurrentPrefabStage() != null;

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
        if (Busy || !File.Exists(Request)) return;
        File.Delete(Request);
        ValidateFromCommandLine();
    }

    [MenuItem("Tools/Civil Craft/Validate Dialogue Pause Resume")]
    public static void ValidateFromCommandLine()
    {
        Directory.CreateDirectory("Temp");
        try
        {
            File.WriteAllText(Report, Validate());
            Debug.Log("[Dialogue Pause Resume] PASS: " + Report);
        }
        catch (Exception error)
        {
            File.WriteAllText(Report, "FAIL: " + error);
            Debug.LogException(error);
        }
    }

    private static string Validate()
    {
        Check(!Busy, "Run only in idle Edit Mode, outside imports, Prefab Mode and builds.");
        running = true;
        Scene originalActive = SceneManager.GetActiveScene();
        Object[] selected = Selection.objects;
        Object activeSelection = Selection.activeObject;
        float timeScale = Time.timeScale;
        CursorLockMode cursorLock = Cursor.lockState;
        bool cursorVisible = Cursor.visible;
        var scenes = new Dictionary<Scene, Tuple<bool, GameObject[]>>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            scenes.Add(scene, Tuple.Create(scene.isDirty, scene.GetRootGameObjects()));
        }
        FieldInfo pauseInstance = Field(typeof(PauseManager), "<Instance>k__BackingField");
        FieldInfo coordinatorInstance = Field(typeof(UIPanelCoordinator), "<Instance>k__BackingField");
        FieldInfo gameInstance = Field(typeof(GameManager), "<Instance>k__BackingField");
        FieldInfo layoutListeners = Field(typeof(UIReservedRegionLayout), "LayoutChanging");
        FieldInfo layoutTransition = Field(typeof(UIReservedRegionLayout), "transitionUntil");
        object oldPause = pauseInstance.GetValue(null);
        object oldCoordinator = coordinatorInstance.GetValue(null);
        object oldGame = gameInstance.GetValue(null);
        object oldListeners = layoutListeners.GetValue(null);
        object oldTransition = layoutTransition.GetValue(null);
        var known = (HashSet<DialogueManager>)Field(typeof(DialogueManager), "knownManagers").GetValue(null);
        DialogueManager[] oldKnown = known.ToArray();
        // Snapshot live input flags, but never enable, disable or search for them in production flows.
        InputManager[] liveInputs = Object.FindObjectsOfType<InputManager>(true);
        bool[] inputStates = liveInputs.Select(input => input.IsPlayerInputEnabled).ToArray();
        bool[] lookStates = liveInputs.Select(input => input.IsLookInputEnabled).ToArray();
        Scene temporary = default;
        GameObject root = null;
        RuntimeAnimatorController controller = null;
        string controllerBefore = null;
        bool controllerDirty = false;
        InputManager fixtureInput = null;
        var report = new StringBuilder();
        try
        {
            pauseInstance.SetValue(null, null);
            coordinatorInstance.SetValue(null, null);
            // Fixture InputManager/PlayerInteract destruction must never remove
            // listeners from the real game's event lists.
            gameInstance.SetValue(null, null);
            layoutListeners.SetValue(null, null);
            layoutTransition.SetValue(null, 0f);
            known.Clear();
            temporary = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(temporary);
            root = new GameObject("Dialogue Pause Resume Validation (Disposable)");
            SceneManager.MoveGameObjectToScene(root, temporary);
            PauseManager pause = Child(root.transform, "Synthetic pause owner", false).AddComponent<PauseManager>();
            UIPanelCoordinator coordinator = Child(root.transform, "Synthetic coordinator", false).AddComponent<UIPanelCoordinator>();
            DialogueManager dialogue = Child(root.transform, "Synthetic dialogue owner", false).AddComponent<DialogueManager>();
            pauseInstance.SetValue(null, pause);
            coordinatorInstance.SetValue(null, coordinator);
            known.Add(dialogue);
            GameObject canvasObject = Child(root.transform, "Fixture canvas", true);
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            GameObject box = Child(canvasObject.transform, "DialogueBox", true);
            RectTransform boxRect = box.GetComponent<RectTransform>();
            boxRect.sizeDelta = new Vector2(1400f, 380f);
            dialogue.animator = box.AddComponent<Animator>();
            Set(dialogue, "dialogueBox", box);
            dialogue.dialogueText = Child(box.transform, "Sentence", true).AddComponent<TextMeshProUGUI>();
            dialogue.dialogueText.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            Check(dialogue.dialogueText.font != null, "The static fixture font is missing.");
            dialogue.dialogueText.rectTransform.sizeDelta = new Vector2(1000f, 220f);
            dialogue.nameText = Child(box.transform, "Speaker", true).AddComponent<TextMeshProUGUI>();
            GameObject skip = Child(canvasObject.transform, "Synthetic skip", true);
            Button skipButton = skip.AddComponent<Button>();
            Set(dialogue, "skipDialogueButton", skipButton);
            Image holdFill = Child(skip.transform, "Hold fill", true).AddComponent<Image>();
            DialogueHoldToSkip hold = skip.AddComponent<DialogueHoldToSkip>();
            hold.Configure(dialogue, skipButton, holdFill, .9f);
            GameObject pausePanel = Child(canvasObject.transform, "Synthetic Pause", true);
            GameObject settingsPanel = Child(canvasObject.transform, "Synthetic Settings", true);
            pausePanel.SetActive(false);
            settingsPanel.SetActive(false);
            pause.pausePanel = pausePanel;
            pause.settingsPanel = settingsPanel;
            coordinator.managedPanels.Add(pausePanel);
            coordinator.managedPanels.Add(settingsPanel);
            controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(AssetDatabase.GUIDToAssetPath(ControllerGuid));
            Check(controller != null, "The authored dialogue AnimatorController is missing.");
            controllerBefore = EditorJsonUtility.ToJson(controller);
            controllerDirty = EditorUtility.IsDirty(controller);
            dialogue.animator.runtimeAnimatorController = controller;
            Call(dialogue, "PreserveDialogueAnimationState");
            Check(dialogue.animator.keepAnimatorStateOnDisable, "Dialogue Animator state retention is not enabled.");
            dialogue.animator.Rebind();
            dialogue.animator.SetBool("isOpen", true);
            dialogue.animator.Play("Base Layer.DialogueBoxOpen", 0, 0f);
            dialogue.animator.Update(0f);
            int openHash = dialogue.animator.GetCurrentAnimatorStateInfo(0).fullPathHash;
            Check(openHash == Animator.StringToHash("Base Layer.DialogueBoxOpen"), "The real dialogue controller failed to enter its open state.");
            float openY = boxRect.anchoredPosition.y;
            Set(dialogue, "isDialogueActive", true);
            var queue = new Queue<string>(new[] { "Second sentence", "Third sentence" });
            Set(dialogue, "sentences", queue);
            Set(dialogue, "cachedTypingWait", new WaitForSeconds(.03f));
            int completions = 0;
            Action completion = () => completions++;
            Set(dialogue, "onDialogueEndCallback", completion);
            IEnumerator typing = (IEnumerator)Call(dialogue, "TypeSentence", "First sentence remains readable.");
            for (int i = 0; i < 5; i++) Check(typing.MoveNext(), "Typewriter fixture finished too early.");
            int partialCharacters = dialogue.dialogueText.maxVisibleCharacters;
            Check(partialCharacters >= 3, "Typewriter fixture did not begin revealing characters.");
            for (int cycle = 0; cycle < 3; cycle++)
            {
                // Multiplayer leaves timeScale at 1. Advance the real iterator while pause is active.
                Time.timeScale = 1f;
                pause.isPaused = true;
                OpenFixturePanel(coordinator, pausePanel);
                Check(!box.activeInHierarchy && !skip.activeInHierarchy, "Pause did not hide the sibling dialogue/skip panels.");
                Check(dialogue.isActiveAndEnabled && DialogueManager.IsAnyDialogueActive,
                    "Hiding the dialogue box incorrectly ended the active conversation.");
                for (int frame = 0; frame < 6; frame++)
                {
                    Check(typing.MoveNext() && typing.Current == null, "Paused typewriter should wait without a typing delay.");
                    Check(dialogue.dialogueText.maxVisibleCharacters == partialCharacters, "Multiplayer pause advanced the typewriter.");
                }
                dialogue.DisplayNextSentence();
                dialogue.SkipDialogue();
                Check(queue.Count == 2 && completions == 0 && (bool)Get(dialogue, "isTyping"),
                    "Paused Continue/Skip consumed a sentence, invoked completion, or cleared typing.");
                Check(ReferenceEquals(Get(dialogue, "onDialogueEndCallback"), completion), "Pause discarded the dialogue callback.");
                OpenFixturePanel(coordinator, settingsPanel);
                Check(settingsPanel.activeInHierarchy && !pausePanel.activeInHierarchy && !box.activeInHierarchy,
                    "Nested Settings did not keep the dialogue suspended.");
                coordinator.ClosePanel(settingsPanel);
                Check(pausePanel.activeInHierarchy && !settingsPanel.activeSelf && !box.activeInHierarchy,
                    "Closing nested Settings prematurely restored the dialogue.");
                pause.isPaused = false;
                coordinator.ClosePanel(pausePanel);
                dialogue.animator.Update(0f);
                Check(box.activeInHierarchy && skip.activeInHierarchy && !coordinator.HasOpenPanel,
                    "Resume failed to restore the exact dialogue and skip visibility.");
                Check(dialogue.animator.GetBool("isOpen") &&
                    dialogue.animator.GetCurrentAnimatorStateInfo(0).fullPathHash == openHash &&
                    Mathf.Abs(boxRect.anchoredPosition.y - openY) < .1f,
                    "Resume reset the authored open animation to its off-screen closed state.");
                Check(DialogueManager.IsAnyDialogueActive, "Resume released input ownership of an ongoing dialogue.");
                Check(typing.MoveNext(), "Resumed typewriter finished unexpectedly.");
                Check(dialogue.dialogueText.maxVisibleCharacters == partialCharacters + 1, "Typewriter did not resume at the next character.");
                partialCharacters++;
            }
            report.AppendLine("PASS: Three repeated Pause -> Settings -> Pause -> Resume cycles restore the actual authored open Animator state, sentence, callback, skip button and typewriter position.");
            report.AppendLine("PASS: Paused Continue/Skip do not consume queued sentences or invoke completion; multiplayer timeScale=1 does not advance text.");
            Set(hold, "holding", true);
            Set(hold, "heldTime", .8f);
            holdFill.fillAmount = .8f;
            holdFill.enabled = true;
            pause.isPaused = true;
            Call(hold, "Update");
            Check(!(bool)Get(hold, "holding") && !(bool)Get(hold, "completed") &&
                Mathf.Approximately((float)Get(hold, "heldTime"), 0f) && !holdFill.enabled &&
                Mathf.Approximately(holdFill.fillAmount, 0f), "Pause retained a stale hold-to-skip press.");
            pause.isPaused = false;
            Call(hold, "Update");
            Check(completions == 0 && queue.Count == 2, "Resuming a stale press skipped the conversation.");
            report.AppendLine("PASS: Hold-to-skip progress resets on pause; no stale press skips dialogue after Resume.");
            while (typing.MoveNext()) { }
            Check(!(bool)Get(dialogue, "isTyping"), "Resumed typewriter did not finish.");
            Set(dialogue, "isDialogueActive", false);
            Set(dialogue, "closeHideDelay", 0f);
            Check(!DialogueManager.IsAnyDialogueActive && DialogueManager.IsAnyDialogueVisible,
                "Closing-only dialogue wrongly retains movement ownership or loses visible-state tracking.");
            IEnumerator closing = (IEnumerator)Call(dialogue, "HideDialogueBoxAfterClose");
            pause.isPaused = true;
            for (int i = 0; i < 4; i++)
                Check(closing.MoveNext() && box.activeSelf, "Closing coroutine disabled dialogue while Pause still owned its visibility snapshot.");
            pause.isPaused = false;
            Check(!closing.MoveNext() && !box.activeSelf, "Closing coroutine failed to hide the finished dialogue after pause cleared.");
            Check(!DialogueManager.IsAnyDialogueVisible, "A finished, hidden dialogue remains visible to other UI.");
            report.AppendLine("PASS: Closing animation does not lock movement; its hide coroutine waits for Pause to clear before disabling the box.");
            ValidateControlRestoration(root, dialogue, pause, coordinator, pausePanel, box, report, out fixtureInput);
        }
        finally
        {
            // Generated PlayerInput.Dispose uses deferred Destroy, which Unity
            // forbids in Edit Mode. Disable/detach only this fixture's wrapper,
            // then destroy its transient action asset with the editor primitive.
            // The live input wrappers are never disposed or otherwise touched.
            if (fixtureInput != null)
            {
                PlayerInput fixtureActions = (PlayerInput)Get(fixtureInput, "playerInput");
                if (fixtureActions != null)
                {
                    fixtureActions.Disable();
                    var fixtureAsset = fixtureActions.asset;
                    Set(fixtureInput, "playerInput", null);
                    fixtureInput.onFoot = default;
                    if (fixtureAsset != null && !EditorUtility.IsPersistent(fixtureAsset))
                        Object.DestroyImmediate(fixtureAsset);
                }
                Call(fixtureInput, "OnDestroy");
            }
            if (root != null) Object.DestroyImmediate(root);
            if (originalActive.IsValid() && originalActive.isLoaded) SceneManager.SetActiveScene(originalActive);
            if (temporary.IsValid() && temporary.isLoaded) EditorSceneManager.CloseScene(temporary, true);
            known.Clear();
            foreach (DialogueManager manager in oldKnown) known.Add(manager);
            pauseInstance.SetValue(null, oldPause);
            coordinatorInstance.SetValue(null, oldCoordinator);
            gameInstance.SetValue(null, oldGame);
            layoutListeners.SetValue(null, oldListeners);
            layoutTransition.SetValue(null, oldTransition);
            Time.timeScale = timeScale;
            Cursor.lockState = cursorLock;
            Cursor.visible = cursorVisible;
            Selection.objects = selected;
            Selection.activeObject = activeSelection;
            running = false;
        }
        Check(SceneManager.sceneCount == scenes.Count, "Fixture changed the loaded scene count.");
        foreach (var entry in scenes)
        {
            Check(entry.Key.isDirty == entry.Value.Item1 && entry.Key.GetRootGameObjects().SequenceEqual(entry.Value.Item2),
                "Fixture changed original scene dirty state or roots.");
        }
        for (int i = 0; i < liveInputs.Length; i++)
            Check(liveInputs[i] != null && liveInputs[i].IsPlayerInputEnabled == inputStates[i] &&
                liveInputs[i].IsLookInputEnabled == lookStates[i], "Fixture changed live movement/look input state.");
        Check(SceneManager.GetActiveScene() == originalActive && Selection.objects.SequenceEqual(selected) &&
            Selection.activeObject == activeSelection && Mathf.Approximately(Time.timeScale, timeScale),
            "Fixture failed to restore active scene, selection or time scale.");
        Check(Cursor.lockState == cursorLock && Cursor.visible == cursorVisible,
            "Fixture failed to restore the cursor state.");
        Check(known.SetEquals(oldKnown) && ReferenceEquals(pauseInstance.GetValue(null), oldPause) &&
            ReferenceEquals(coordinatorInstance.GetValue(null), oldCoordinator) &&
            ReferenceEquals(gameInstance.GetValue(null), oldGame), "Fixture did not restore dialogue/pause/coordinator/game registry state.");
        Check(controller != null && controllerDirty == EditorUtility.IsDirty(controller) &&
            controllerBefore == EditorJsonUtility.ToJson(controller), "Fixture modified the authored AnimatorController asset.");
        report.AppendLine("PASS: Scene roots/dirty states, active scene, selection, time scale, dialogue registry, singleton values, listeners, controller asset and live input flags restored.");
        report.AppendLine("Scope: actual modal helpers, dialogue resolver/End/Skip and generated local input action map; Pause's input lines are checked against production source and applied only to the synthetic player. Public PauseGame/ResumeGame and global panel discovery are not invoked. No live panel, audio, input, account or network object is modified; no scene/asset saves or automatic Play Mode.");
        report.AppendLine("Completed UTC: " + DateTime.UtcNow.ToString("O"));
        return report.ToString();
    }

    private static void ValidateControlRestoration(GameObject root, DialogueManager dialogue,
        PauseManager pause, UIPanelCoordinator coordinator, GameObject pausePanel,
        GameObject box, StringBuilder report, out InputManager input)
    {
        input = null;
        string pauseSource = File.ReadAllText("Assets/Script/PauseManager.cs");
        Check(pauseSource.Contains("inputObj.SetPlayerInputEnable(false);") &&
              pauseSource.Contains("inputObj.SetLookEnabled(false);") &&
              pauseSource.Contains("bool resumePlayerControls = !DialogueManager.IsAnyDialogueActive;") &&
              pauseSource.Contains("inputObj.SetPlayerInputEnable(resumePlayerControls);") &&
              pauseSource.Contains("inputObj.SetLookEnabled(resumePlayerControls);"),
            "The native fixture's isolated Pause input handoff no longer matches production; update the fixture.");

        // The native fixture never invokes global-discovery Pause methods. The
        // actual full Start -> Pause -> Resume -> End flow is exercised by the
        // standalone DialoguePauseControls source-extracted regression harness.
        GameObject player = Child(root.transform, "Synthetic local player (no motor, save or account)", false);
        PlayerLook look = player.AddComponent<PlayerLook>();
        PlayerInteract interact = player.AddComponent<PlayerInteract>();
        input = player.AddComponent<InputManager>();
        Call(input, "EnsureInputInitialized");
        Set(dialogue, "playerInteract", interact);
        Set(dialogue, "playerTransform", player.transform);
        Set(dialogue, "inputManager", null);
        Call(dialogue, "ResolvePlayerTransform");
        Check(ReferenceEquals(Get(dialogue, "inputManager"), input) &&
            ReferenceEquals(Get(dialogue, "playerTransform"), player.transform),
            "A cached player transform bypassed resolving its missing InputManager.");

        Set(dialogue, "inputManager", null);
        Set(dialogue, "playerInteract", null);
        Call(dialogue, "ResolvePlayerInput");
        Check(ReferenceEquals(Get(dialogue, "inputManager"), input),
            "A cached player transform did not resolve its own InputManager without an interaction component.");
        Set(dialogue, "playerInteract", interact);

        int completions = 0;
        for (int cycle = 0; cycle < 3; cycle++)
        {
            input.SetPlayerInputEnable(true);
            input.SetLookEnabled(true);
            interact.enabled = true;
            Check(input.IsPlayerInputEnabled && input.onFoot.enabled && look.canLook,
                "Synthetic player's actual generated action map did not initialize.");
            Set(dialogue, "inputManager", null);
            Call(dialogue, "ResolvePlayerInput");
            Check(ReferenceEquals(Get(dialogue, "inputManager"), input),
                "Missing dialogue binding did not resolve the associated local player's InputManager.");
            Set(dialogue, "isDialogueActive", true);
            Set(dialogue, "isTyping", false);
            Set(dialogue, "sentences", new Queue<string>(new[] { "Still talking" }));
            Set(dialogue, "onDialogueEndCallback", (Action)(() => completions++));
            box.SetActive(true);
            input.SetPlayerInputEnable(false);
            input.SetLookEnabled(false);
            interact.enabled = false;

            pause.isPaused = true;
            OpenFixturePanel(coordinator, pausePanel);
            input.SetPlayerInputEnable(false);
            input.SetLookEnabled(false);
            dialogue.SkipDialogue();
            Check(DialogueManager.IsAnyDialogueActive && completions == cycle &&
                !input.IsPlayerInputEnabled && !input.onFoot.enabled && !look.canLook && !interact.enabled,
                "Skip during Pause prematurely ended dialogue or unlocked the real action map.");

            pause.isPaused = false;
            coordinator.ClosePanel(pausePanel);
            bool resumeControls = !DialogueManager.IsAnyDialogueActive;
            input.SetPlayerInputEnable(resumeControls);
            input.SetLookEnabled(resumeControls);
            Check(!resumeControls && !input.IsPlayerInputEnabled && !input.onFoot.enabled && !look.canLook,
                "Resume prematurely released controls still owned by the active conversation.");

            // Also cover a missing binding at completion (older saved scenes or
            // script reload): EndDialogue must resolve before restoring controls.
            Set(dialogue, "inputManager", null);
            if (cycle == 2) dialogue.SkipDialogue();
            else Call(dialogue, "EndDialogue");
            Check(input.IsPlayerInputEnabled && input.onFoot.enabled && look.canLook &&
                input.IsLookInputEnabled && interact.enabled && !DialogueManager.IsAnyDialogueActive,
                "Finished dialogue failed to restore the actual movement action map, camera look or interaction after Pause.");
            Check(completions == cycle + 1, "Dialogue completion callback did not run exactly once.");
            dialogue.SkipDialogue();
            Call(dialogue, "EndDialogue");
            Check(completions == cycle + 1, "A finished dialogue completed more than once.");
        }
        report.AppendLine("PASS: Missing-binding/cached-transform dialogue resolves the synthetic local InputManager. Three Pause -> Resume -> End/Skip cycles restore the actual generated onFoot action map, PlayerLook.canLook and PlayerInteract.enabled; paused Skip never unlocks them; completion runs once.");
        string sceneSource = File.ReadAllText("Assets/Scenes/BHAN HOUSE.unity");
        int dialogueScript = sceneSource.IndexOf("guid: 7d6f54b33e45eb7439cbdfb8f6d57665", StringComparison.Ordinal);
        Check(dialogueScript >= 0, "The read-only Bhan scene descriptor has no DialogueManager.");
        int nextObject = sceneSource.IndexOf("\n--- !u!", dialogueScript, StringComparison.Ordinal);
        string sceneBinding = nextObject < 0 ? sceneSource.Substring(dialogueScript)
            : sceneSource.Substring(dialogueScript, nextObject - dialogueScript);
        report.AppendLine(sceneBinding.Contains("inputManager: {fileID: 0}")
            ? "READ-ONLY: BHAN HOUSE has an unassigned serialized DialogueManager input binding; this exact missing-binding case is covered without rewriting the scene."
            : "READ-ONLY: BHAN HOUSE now has a serialized input binding; missing bindings are still covered by the isolated fixture.");
    }

    // Use the real coordinator frame/hide/restore mechanics, but intentionally skip
    // RefreshManagedPanels and HideOtherCanvases, which scan live authored scenes.
    private static void OpenFixturePanel(UIPanelCoordinator coordinator, GameObject panel)
    {
        Type frameType = typeof(UIPanelCoordinator).GetNestedType("PanelFrame", BindingFlags.NonPublic);
        object frame = Activator.CreateInstance(frameType, true);
        Field(frameType, "panel").SetValue(frame, panel);
        var recorded = new HashSet<GameObject>();
        CallStatic(typeof(UIPanelCoordinator), "RecordStates", coordinator.hudObjects, frame, recorded);
        CallStatic(typeof(UIPanelCoordinator), "RecordStates", coordinator.managedPanels, frame, recorded);
        foreach (GameObject target in coordinator.hudObjects.Concat(coordinator.managedPanels))
            if (target != null && target != panel && !panel.transform.IsChildOf(target.transform)) target.SetActive(false);
        CallStatic(typeof(UIPanelCoordinator), "EnableTargetParentChain", panel, frame, recorded);
        CallStatic(typeof(UIPanelCoordinator), "EnableTargetCanvases", panel, frame);
        CallStatic(typeof(UIPanelCoordinator), "HideSameCanvasSiblings", panel, frame, recorded);
        panel.SetActive(true);
        object stack = Get(coordinator, "panelStack");
        stack.GetType().GetMethod("Push").Invoke(stack, new[] { frame });
    }

    private static GameObject Child(Transform parent, string name, bool rect)
    {
        var child = rect ? new GameObject(name, typeof(RectTransform)) : new GameObject(name);
        child.transform.SetParent(parent, false);
        return child;
    }

    private static FieldInfo Field(Type type, string name) => type.GetField(name, Flags) ??
        throw new MissingFieldException(type.FullName, name);
    private static object Get(object target, string name) => Field(target.GetType(), name).GetValue(target);
    private static void Set(object target, string name, object value) => Field(target.GetType(), name).SetValue(target, value);
    private static object Call(object target, string name, params object[] args) =>
        target.GetType().GetMethod(name, Flags).Invoke(target, args);
    private static object CallStatic(Type type, string name, params object[] args) =>
        type.GetMethod(name, Flags).Invoke(null, args);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
