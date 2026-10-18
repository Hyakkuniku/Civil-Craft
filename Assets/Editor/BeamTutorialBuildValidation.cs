using System;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
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

    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object Get(object value, string field) => value.GetType().GetField(field, PrivateInstance).GetValue(value);
    private static void Set(object value, string field, object data) => value.GetType().GetField(field, PrivateInstance).SetValue(value, data);
    private static void Invoke(object value, string method, params object[] args) => value.GetType().GetMethod(method, PrivateInstance).Invoke(value, args);
    private static object Singleton(Type type) => type.GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    private static void SetSingleton(Type type, object value) => type.GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, value);
}
