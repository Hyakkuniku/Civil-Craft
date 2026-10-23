using System;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using Object = UnityEngine.Object;

/// <summary>Detached checks only. Does not author, open, or save gameplay scenes.</summary>
public static class BeamTutorialBuildValidation
{
    [MenuItem("Tools/Civil Craft/Tutorial 1/Validate Build Action Gates (Detached)")]
    public static void ValidateFromMenu() => Debug.Log(ValidateDetached());

    public static string ValidateDetached()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run the detached tutorial checks in Edit Mode.");
        var report = new StringBuilder();
        int checks = 0;
        Scene preview = EditorSceneManager.NewPreviewScene();
        var fixture = new GameObject("Detached Beam Tutorial Action Checks") { hideFlags = HideFlags.HideAndDontSave };
        SceneManager.MoveGameObjectToScene(fixture, preview);
        object oldTutorial = Singleton(typeof(TutorialManager));
        object oldController = Singleton(typeof(BuildUIController));
        object oldDirector = Singleton(typeof(BuildTutorialDirector));
        object oldTooltip = Singleton(typeof(MaterialTooltipManager));
        float originalTimeScale = Time.timeScale;
        try
        {
            TutorialManager tutorial = fixture.AddComponent<TutorialManager>();
            BuildUIController controller = fixture.AddComponent<BuildUIController>();
            BuildTutorialDirector director = fixture.AddComponent<BuildTutorialDirector>();
            MaterialTooltipManager tooltip = fixture.AddComponent<MaterialTooltipManager>();
            SetSingleton(typeof(TutorialManager), tutorial);
            SetSingleton(typeof(BuildUIController), controller);
            SetSingleton(typeof(BuildTutorialDirector), director);
            SetSingleton(typeof(MaterialTooltipManager), tooltip);
            TutorialSequence sequence = fixture.AddComponent<TutorialSequence>();
            sequence.lessonName = "Detached_Beam_Action_Test";
            sequence.tutorialSteps = new[] { new TutorialStep(), new TutorialStep() };
            Set(tutorial, "currentSequence", sequence);
            Set(tutorial, "<IsTutorialActive>k__BackingField", true);
            Set(tutorial, "currentStepIndex", 0);
            // Stop at the real advancement boundary: do not run tutorial UI or
            // progress persistence in this detached test scene.
            Set(tutorial, "isAdvancingStep", true);
            Time.timeScale = 1f;
            var tool = new GameObject("Existing tool", typeof(RectTransform), typeof(Image), typeof(Button));
            tool.transform.SetParent(fixture.transform, false);
            controller.contractToolBindings.Add(new BuildToolUIBinding { tool = BuildModeTool.Statistics, uiObject = tool });
            controller.contractToolBindings.Add(new BuildToolUIBinding { tool = BuildModeTool.Grid, uiObject = tool });
            controller.statsPanel = new GameObject("Existing statistics readout");
            controller.statsPanel.transform.SetParent(fixture.transform, false);
            var creatorObject = new GameObject("Existing creator");
            creatorObject.transform.SetParent(fixture.transform, false);
            controller.barCreator = creatorObject.AddComponent<BarCreator>();
            tooltip.tooltipPanel = new GameObject("Existing material information", typeof(RectTransform), typeof(CanvasGroup));
            tooltip.tooltipPanel.transform.SetParent(fixture.transform, false);
            CanvasGroup tooltipGroup = tooltip.tooltipPanel.GetComponent<CanvasGroup>();
            Set(tooltip, "tooltipCanvasGroup", tooltipGroup);

            Action reset = () =>
            {
                Set(tutorial, "currentStepIndex", 0);
                director.isTutorialRunning = true;
                director.BeginStep(0);
            };
            Action<float> tick = elapsed => Invoke(director, "UpdateRequiredObservation", elapsed);
            Func<bool> advanced = () => (bool)Get(director, "hasAdvancedFromRequiredClickThisStep");
            Action<bool, string> require = (condition, label) =>
            {
                if (!condition) throw new InvalidOperationException("Beam tutorial check failed: " + label);
                checks++;
                report.AppendLine("PASS " + label);
            };

            ValidateFactory(fixture, director, controller, require);

            reset();
            controller.statsPanel.SetActive(false);
            director.PromptOpenStatistics();
            tick(1f);
            require(!advanced(), "statistics open cannot be skipped by waiting");
            director.OnToolClicked(tool);
            tick(1f);
            require(!advanced(), "statistics click alone cannot advance before panel opens");
            controller.statsPanel.SetActive(true);
            tick(0.02f);
            require(advanced(), "actual statistics open advances exactly once");
            require(Get(director, "requiredObservation").ToString() == "None", "completed observation is cleared");

            reset();
            director.PromptCloseStatistics();
            director.OnToolClicked(tool);
            tick(1f);
            require(!advanced(), "statistics close waits for real readout to close");
            controller.statsPanel.SetActive(false);
            tick(0.02f);
            require(advanced(), "actual statistics close advances");

            reset();
            controller.barCreator.isGridSnappingEnabled = true;
            director.PromptToggleGrid();
            director.OnToolClicked(tool);
            tick(1f);
            require(!advanced(), "grid requires a state change, not just a click");
            controller.barCreator.isGridSnappingEnabled = false;
            tick(0.02f);
            require(advanced(), "grid can be toggled off");
            reset();
            director.PromptToggleGrid();
            director.OnToolClicked(tool);
            controller.barCreator.isGridSnappingEnabled = true;
            tick(0.02f);
            require(advanced(), "grid can be toggled on");

            reset();
            director.PromptInspectMaterial();
            tooltipGroup.alpha = 1f;
            tick(0.2f);
            require(!advanced(), "quick material tap cannot complete hold instruction");
            tooltipGroup.alpha = 0f;
            tick(1f);
            tooltipGroup.alpha = 1f;
            tick(0.25f);
            require(!advanced(), "separate short taps do not accumulate held time");
            Time.timeScale = 0f;
            tick(10f);
            require(!advanced(), "pause cannot complete material inspection");
            Time.timeScale = 1f;
            tick(0.2f);
            require(!advanced(), "inspection hold starts again after pause");
            tick(0.25f);
            require(advanced(), "deliberate material inspection advances");

            reset();
            director.PromptInspectMaterial();
            tooltipGroup.alpha = 1f;
            Set(tutorial, "currentStepIndex", 1);
            tick(1f);
            require(!advanced() && Get(director, "requiredObservation").ToString() == "None", "changed step cancels a stale hold");
            reset();
            director.PromptInspectMaterial();
            director.EndTutorial();
            require(Get(director, "requiredObservation").ToString() == "None", "sequence teardown cancels observations");

            SimulationLessonDefinition legacyDefault = ScriptableObject.CreateInstance<SimulationLessonDefinition>();
            try
            {
                require(string.IsNullOrEmpty(legacyDefault.vehicleApproachingEnd), "legacy lessons have no new near-end text by default");
                SimulationLessonDefinition beam = AssetDatabase.LoadAssetAtPath<SimulationLessonDefinition>(
                    "Assets/BridgeBuilder/Data/Simulation Lessons/Beam Bridge Lesson.asset");
                require(beam != null && !string.IsNullOrWhiteSpace(beam.vehicleApproachingEnd), "Beam lesson has authored near-end guidance");
                require(beam.vehicleNearEndProgress > beam.vehicleMidpointProgress && beam.vehicleNearEndProgress < 1f, "near-end threshold follows midpoint but precedes completion");
                require(beam.simulationStarted.Contains("200 kg") && beam.bridgeSucceeded.Contains("200 kg"), "Beam load messages agree on 200 kg");
                require(beam.vehicleFailed.Contains("Wood Pier") && beam.stressLimitExceeded.Contains("Wood Pier"), "failure hints retain road/support guidance");
            }
            finally { Object.DestroyImmediate(legacyDefault); }
            report.AppendLine("PASS: " + checks + " detached Beam tutorial checks. Gameplay scenes and saves were not changed.");
            return report.ToString();
        }
        finally
        {
            Time.timeScale = originalTimeScale;
            SetSingleton(typeof(TutorialManager), oldTutorial);
            SetSingleton(typeof(BuildUIController), oldController);
            SetSingleton(typeof(BuildTutorialDirector), oldDirector);
            SetSingleton(typeof(MaterialTooltipManager), oldTooltip);
            Object.DestroyImmediate(fixture);
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    private static void ValidateFactory(GameObject fixture, BuildTutorialDirector director,
        BuildUIController controller, Action<bool, string> require)
    {
        // All objects, callbacks and blueprints below belong to the temporary
        // preview fixture. No authored sequence is assigned, invoked or saved.
        var ui = new GameObject("Detached factory UI", typeof(RectTransform));
        ui.transform.SetParent(fixture.transform, false);
        var cost = new GameObject("Detached cost", typeof(RectTransform), typeof(TextMeshProUGUI));
        cost.transform.SetParent(ui.transform, false);
        var budget = new GameObject("Detached budget", typeof(RectTransform), typeof(TextMeshProUGUI));
        budget.transform.SetParent(ui.transform, false);
        var fill = new GameObject("Detached budget fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(ui.transform, false);
        controller.usedBudgetText = cost.GetComponent<TextMeshProUGUI>();
        controller.maxBudgetText = budget.GetComponent<TextMeshProUGUI>();
        controller.budgetFillBar = fill.GetComponent<Image>();
        var gestures = new GameObject("Detached gesture callbacks", typeof(CanvasGroup), typeof(TutorialGestureUI));
        gestures.transform.SetParent(fixture.transform, false);
        TutorialGestureUI gesture = gestures.GetComponent<TutorialGestureUI>();
        BridgeMaterialSO material = ScriptableObject.CreateInstance<BridgeMaterialSO>();
        try
        {
            var legacy = new TutorialStep[27];
            for (int i = 0; i < legacy.Length; i++)
            {
                var target = new GameObject("Detached reference " + i, typeof(RectTransform));
                target.transform.SetParent(ui.transform, false);
                legacy[i] = new TutorialStep {
                    message = "Audited fixture step " + i,
                    screenPosition = i % 2 == 0 ? TutorialPosition.Left : TutorialPosition.Center,
                    showNextButton = i % 2 == 0, canSkip = false,
                    lockLook = i % 3 == 0, lockJump = i % 3 == 1, lockRun = i % 3 == 2,
                    stepWaypoints = new System.Collections.Generic.List<GuiderWaypoint> {
                        new GuiderWaypoint { target = target.transform, advancesTutorial = false }
                    }, worldHighlightObject = target,
                    usePointer = true, pointerTarget = target.GetComponent<RectTransform>(),
                    pointerOffset = new Vector2(i, -i), pointerRotation = i,
                    advanceOnClick = i % 2 != 0, requiredAction = TutorialStepAction.None
                };
                UnityEventTools.AddBoolPersistentListener(legacy[i].OnStepStart, target.SetActive, true);
            }
            UnityEventTools.AddPersistentListener(legacy[2].OnStepStart, gesture.PlayPanAnimation);
            UnityEventTools.AddPersistentListener(legacy[3].OnStepStart, gesture.PlayPinchAnimation);
            UnityEventTools.AddPersistentListener(legacy[4].OnStepStart, gesture.PlaySwipeAnimation);
            UnityEventTools.AddObjectPersistentListener<BridgeMaterialSO>(legacy[19].OnStepStart, director.PromptMaterialClick, material);
            foreach (int index in new[] { 21, 23, 25 })
            {
                UnityEventTools.AddPersistentListener(legacy[index].OnStepStart, director.PromptDrawBridge);
                UnityEventTools.AddObjectPersistentListener<GameObject>(legacy[index].OnStepStart,
                    director.PromptDrawBridgeForContainer, legacy[index].worldHighlightObject);
            }
            UnityEventTools.AddPersistentListener(legacy[26].OnStepStart, director.UnlockSimulation);

            string[] legacyBefore = legacy.Select(JsonUtility.ToJson).ToArray();
            TutorialStep[] first = BeamTutorialBuildAuthoring.BuildUpdatedSteps(legacy, director, controller);
            require(first.Length == 28, "original 27-step factory input produces exactly 28 instructions");
            VerifyFactoryOutput(first, director, require);
            // The previous migration inserted only these two feedback entries.
            // Reconstruct its 29-step shape with the retired close callback to
            // prove it can be removed without altering the other live gates.
            var old29 = first.ToList();
            TutorialStep close = CloneStep(first[11]);
            close.message = "Legacy statistics-close fixture";
            close.OnStepStart = new UnityEvent();
            UnityEventTools.AddPersistentListener(close.OnStepStart, director.PromptCloseStatistics);
            old29.Insert(12, close);
            TutorialStep[] previous = old29.ToArray();
            string[] previousBefore = previous.Select(JsonUtility.ToJson).ToArray();
            TutorialStep[] migrated = BeamTutorialBuildAuthoring.BuildUpdatedSteps(previous, director, controller);
            require(migrated.Length == 28 && SameSteps(first, migrated), "previous 29-step factory input removes only the retired instruction");
            VerifyFactoryOutput(migrated, director, require);
            TutorialStep[] repeated = BeamTutorialBuildAuthoring.BuildUpdatedSteps(migrated, director, controller);
            require(repeated.Length == 28 && SameSteps(migrated, repeated), "current 28-step factory input is idempotent without reintroducing close");
            require(legacyBefore.SequenceEqual(legacy.Select(JsonUtility.ToJson)) && previousBefore.SequenceEqual(previous.Select(JsonUtility.ToJson)),
                "factory calls do not mutate either source array or its authored event/reference fields");

            // Except for the intentionally rebuilt observed gates and feedback,
            // all callback objects and blueprint/anchor/control references stay
            // attached to the same logical instruction after index shifting.
            foreach (int sourceIndex in new[] { 2, 3, 4, 18, 19, 20, 21, 22, 23, 24, 25, 26 })
            {
                int resultIndex = sourceIndex < 12 ? sourceIndex : sourceIndex < 22 ? sourceIndex - 1 : sourceIndex;
                if (sourceIndex == 26) resultIndex = 27;
                TutorialStep before = legacy[sourceIndex], after = first[resultIndex];
                require(!ReferenceEquals(before, after) && ReferenceEquals(before.OnStepStart, after.OnStepStart),
                    "retained step copies its original callback object: " + sourceIndex);
                require(ReferenceEquals(before.stepWaypoints, after.stepWaypoints) && before.worldHighlightObject == after.worldHighlightObject &&
                    before.pointerTarget == after.pointerTarget && before.pointerOffset == after.pointerOffset && before.pointerRotation == after.pointerRotation &&
                    before.lockLook == after.lockLook && before.lockJump == after.lockJump && before.lockRun == after.lockRun &&
                    before.requiredAction == after.requiredAction && before.advanceOnClick == after.advanceOnClick &&
                    before.showNextButton == after.showNextButton && before.canSkip == after.canSkip && before.screenPosition == after.screenPosition,
                    "retained blueprint/anchor/material/drawing fields survive index shift: " + sourceIndex);
            }
            for (int index = 13; index < previous.Length; index++)
            {
                // These two gates are deliberately reauthored by Observe, and
                // the two explanatory feedback steps get fresh LockAllUI events.
                if (index == 13 || index == 15 || index == 22 || index == 27) continue;
                require(ReferenceEquals(previous[index].OnStepStart, migrated[index - 1].OnStepStart),
                    "29-to-28 retains following callback identity: " + index);
            }
            bool rejected = false;
            try { BeamTutorialBuildAuthoring.BuildUpdatedSteps(first.Take(26).ToArray(), director, controller); }
            catch (InvalidOperationException) { rejected = true; }
            require(rejected, "unexpected/custom step counts remain guarded instead of guessed");
        }
        finally { Object.DestroyImmediate(material); }
    }

    private static void VerifyFactoryOutput(TutorialStep[] steps, BuildTutorialDirector director,
        Action<bool, string> require)
    {
        Func<TutorialStep, string, bool> calls = (step, method) => Enumerable.Range(0, step.OnStepStart.GetPersistentEventCount())
            .Any(index => step.OnStepStart.GetPersistentTarget(index) == director && step.OnStepStart.GetPersistentMethodName(index) == method);
        require(calls(steps[11], nameof(BuildTutorialDirector.PromptOpenStatistics)) && !steps[11].showNextButton && !steps[11].advanceOnClick,
            "statistics still requires its real open action, without a new Next shortcut");
        require(calls(steps[12], nameof(BuildTutorialDirector.PromptToggleGrid)) && !steps[12].showNextButton && !steps[12].advanceOnClick,
            "Grid immediately follows statistics and retains its real state-change gate");
        require(calls(steps[14], nameof(BuildTutorialDirector.PromptInspectMaterial)) && !steps[14].showNextButton && !steps[14].advanceOnClick,
            "material inspection retains its deliberate hold gate");
        require(!steps.Any(step => calls(step, nameof(BuildTutorialDirector.PromptCloseStatistics))),
            "no returned instruction requires the statistics-close action");
        require(calls(steps[20], nameof(BuildTutorialDirector.PromptDrawBridge)) && calls(steps[23], nameof(BuildTutorialDirector.PromptDrawBridge)) &&
            calls(steps[25], nameof(BuildTutorialDirector.PromptDrawBridge)) && calls(steps[27], nameof(BuildTutorialDirector.UnlockSimulation)),
            "road, both pier drawing callbacks and final simulation unlock keep their order");
    }

    private static TutorialStep CloneStep(TutorialStep source) => (TutorialStep)typeof(object)
        .GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(source, null);

    private static bool SameSteps(TutorialStep[] left, TutorialStep[] right) =>
        left.Length == right.Length && left.Select(JsonUtility.ToJson).SequenceEqual(right.Select(JsonUtility.ToJson));

    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object Get(object value, string field) => value.GetType().GetField(field, PrivateInstance).GetValue(value);
    private static void Set(object value, string field, object data) => value.GetType().GetField(field, PrivateInstance).SetValue(value, data);
    private static void Invoke(object value, string method, params object[] args) => value.GetType().GetMethod(method, PrivateInstance).Invoke(value, args);
    private static object Singleton(Type type) => type.GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    private static void SetSingleton(Type type, object value) => type.GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, value);
}
