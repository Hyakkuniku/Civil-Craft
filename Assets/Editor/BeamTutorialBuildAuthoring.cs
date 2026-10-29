using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

/// <summary>
/// Explicit migration of Tutorial 1's existing build instructions. Never runs on
/// import, opens scenes, saves scenes, or replaces authored blueprint references.
/// The guarded Bhan content migration can call Apply with its loaded saved scene.
/// </summary>
public static class BeamTutorialBuildAuthoring
{
    private const string ScenePath = "Assets/Scenes/CanyonCrossing.unity";
    private const string LessonId = "Sequence_Build1";
    private const string RoadFeedback = "Great! The <b><#E09500>Wood Road</color></b> provides the surface that vehicles use to cross the bridge.";
    private const string BeamComplete = "<b><#E09500>BEAM BRIDGE COMPLETE!</color></b> Excellent! Your first Beam Bridge is complete. You connected the anchors using a roadway and structural supports. Now test whether it can carry the required load.";

    [MenuItem("Tools/Civil Craft/Tutorial 1/Update Build Instructions (Do Not Save)")]
    private static void ApplyFromMenu()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
            EditorApplication.isUpdating || scene.isDirty)
            throw new InvalidOperationException("Exit Play Mode and save CanyonCrossing before updating the build instructions. No scene was saved.");
        Apply(scene);
        Debug.Log("[Tutorial 1] Updated the existing build instructions. Review and save CanyonCrossing when ready; this command did not save it.");
    }

    public static void Apply(Scene scene)
    {
        Prepare(scene, out TutorialSequence sequence, out TutorialStep[] updated);
        Undo.RecordObject(sequence, "Update Tutorial 1 build instructions");
        sequence.tutorialSteps = updated;
        EditorUtility.SetDirty(sequence);
        EditorSceneManager.MarkSceneDirty(scene);
    }

    /// <summary>Performs the complete migration audit without assigning data, recording Undo, or marking/saving scenes.</summary>
    public static void Preflight(Scene scene)
    {
        Prepare(scene, out _, out _);
    }

    private static void Prepare(Scene scene, out TutorialSequence sequence, out TutorialStep[] updated)
    {
        if (!scene.IsValid() || !scene.isLoaded || scene.path != ScenePath)
            throw new InvalidOperationException("Tutorial 1 build authoring requires the loaded CanyonCrossing scene.");

        TutorialSequence[] sequences = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<TutorialSequence>(true))
            .Where(item => string.Equals(item.lessonName, LessonId, StringComparison.Ordinal)).ToArray();
        if (sequences.Length != 1)
            throw new InvalidOperationException("Expected exactly one existing Sequence_Build1; no substitute tutorial will be created.");
        sequence = sequences[0];
        BuildTutorialDirector director = sequence.GetComponent<BuildTutorialDirector>();
        if (director == null)
            throw new InvalidOperationException("Sequence_Build1 is missing its authored BuildTutorialDirector.");
        BuildUIController[] controllers = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<BuildUIController>(true)).ToArray();
        if (controllers.Length != 1)
            throw new InvalidOperationException("Expected exactly one authored BuildUIController for the existing tutorial UI.");
        BuildUIController controller = controllers[0];
        if (controller.usedBudgetText == null || controller.budgetFillBar == null ||
            controller.maxBudgetText == null || controller.statsPanel == null)
            throw new InvalidOperationException("The existing cost, budget, or statistics UI is missing. No scene changes were applied.");

        updated = BuildUpdatedSteps(sequence.tutorialSteps, director, controller);
    }

    // Separated from scene mutation so detached validation can verify all
    // preserved anchors, blueprints, camera gestures, and persistent callbacks.
    internal static TutorialStep[] BuildUpdatedSteps(
        TutorialStep[] source, BuildTutorialDirector director, BuildUIController controller)
    {
        // Restore only an in-memory alignment slot for the original index map.
        // The deleted statistics-close instruction is never returned or invoked.
        // Its pointer used to be shared with the preceding open-statistics step.
        if (source != null && source.Length == 28 && source[11] != null &&
            source[21] != null && source[26] != null &&
            source[21].message == RoadFeedback && source[26].message == BeamComplete)
        {
            var aligned = source.ToList();
            aligned.Insert(12, CopyStep(source[11]));
            source = aligned.ToArray();
        }
        TutorialStep[] legacy;
        if (source != null && source.Length == 29 && source[22] != null && source[27] != null &&
            source[22].message == RoadFeedback && source[27].message == BeamComplete)
        {
            legacy = source.Take(22).Concat(source.Skip(23).Take(4)).Concat(source.Skip(28)).ToArray();
        }
        else if (source != null && source.Length == 27) legacy = source;
        else throw new InvalidOperationException("Sequence_Build1 no longer matches the audited original 27-step, previous 29-step, or current 28-step sequence. Review it manually instead of replacing custom content.");

        if (legacy.Any(step => step == null) || director == null || controller == null)
            throw new InvalidOperationException("Tutorial 1 has a missing step, director, or UI controller.");
        RequireEvent(legacy[2], "PlayPanAnimation");
        RequireEvent(legacy[3], "PlayPinchAnimation");
        RequireEvent(legacy[4], "PlaySwipeAnimation");
        RequireEvent(legacy[19], "PromptMaterialClick");
        RequireEvent(legacy[21], "PromptDrawBridge");
        RequireEvent(legacy[23], "PromptDrawBridge");
        RequireEvent(legacy[25], "PromptDrawBridge");
        RequireEvent(legacy[26], "UnlockSimulation");
        if (legacy[12].pointerTarget == null || legacy[13].pointerTarget == null ||
            legacy[14].pointerTarget == null)
            throw new InvalidOperationException("The audited statistics, grid, or materials pointer target is missing.");

        TutorialStep[] steps = legacy.Select(CopyStep).ToArray();
        steps[0].message = "Welcome to <b><#E09500>Build Mode!</color></b> In this tutorial, you'll learn the basic tools needed to design, build, and test your first bridge.";
        steps[1].message = "Your objective is to connect the <b><#E09500>two red anchors</color></b> using the available materials. First, learn how to move the build camera.";
        steps[5].message = "Use the <b><#E09500>2D Camera</color></b> for a clear side view. It helps you inspect the structure and align its components. Tap the <b><#E09500>Camera Button</color></b> to reset the camera tilt.";
        steps[6].message = "This is the current <b><#E09500>construction cost</color></b>. It displays the total cost of the materials currently used in your bridge.";
        PointAt(steps[6], controller.usedBudgetText != null ? controller.usedBudgetText.rectTransform : null);
        steps[7].message = "Compare the construction cost with the <b><#E09500>budget progress bar</color></b> to see how much of your available budget has been used and how much remains.";
        PointAt(steps[7], controller.budgetFillBar != null ? controller.budgetFillBar.rectTransform : null);
        steps[8].message = "This is the <b><#E09500>budget limit</color></b>. Stay within it to complete the project. Using too many materials may prevent you from meeting the contract requirements.";
        PointAt(steps[8], controller.maxBudgetText != null ? controller.maxBudgetText.rectTransform : null);
        steps[9].message = "The <b><#E09500>Stress Indicator</color></b> shows how close each bridge member is to its allowable stress limit.";
        steps[10].message = "<color=#25833D>Green = Safe</color>\n<color=#9C7600>Yellow = Moderate Stress</color>\n<color=#BD5E00>Orange = High Stress</color>\n<color=#B72D29>Red = Critical Stress</color>\n<color=#252525>Black = Failure</color>";

        Observe(steps[11], legacy[12], director.PromptOpenStatistics,
            "Open the <b><#E09500>Bridge Statistics</color></b> panel. It shows live load, dead load, road length, member count, estimated capacity, efficiency, and factor of safety.");
        Observe(steps[13], legacy[13], director.PromptToggleGrid,
            "The <b><#E09500>Grid</color></b> turns the construction grid on or off and helps align components accurately. Toggle it once to continue.");
        steps[14].message = "This section contains the <b><#E09500>materials</color></b> available for your current project.";
        steps[14].showNextButton = true;
        steps[14].advanceOnClick = false;
        Observe(steps[15], legacy[14], director.PromptInspectMaterial,
            "Press and hold any <b><#E09500>material</color></b> to inspect its name, cost, mass, strength limit, maximum length, and type.");
        steps[16].message = "You're ready to begin construction! Let's build your first <b><#E09500>Beam Bridge</color></b>.";
        steps[17].message = "Your bridge must connect the <b><#E09500>two red anchors</color></b>. Use the available materials to create a stable structure.";
        steps[18].message = "These highlighted points are the <b><#E09500>red anchors</color></b>. They mark the two ends that your bridge must connect.";
        steps[19].message = "Select the <b><#E09500>Wood Road</color></b> material to begin.";
        steps[20].message = "Start at the highlighted anchor and connect the roadway across the gap. Follow the guide to place the <b><#E09500>Wood Road</color></b>.";
        steps[21].message = "Drag from the <b><#E09500>highlighted anchor</color></b> across the gap to trace the guided roadway.";
        steps[22].message = "Now select the <b><#E09500>Wood Pier</color></b>. Place it beneath the road to provide structural support.";
        steps[23].message = "Follow the guide to place a <b><#E09500>Wood Pier</color></b> beneath the roadway. Connect it to the existing road point.";
        steps[24].message = "Add the remaining <b><#E09500>Wood Pier</color></b> support shown by the guide.";
        steps[25].message = "Trace the remaining guided <b><#E09500>Wood Pier</color></b> beneath the roadway and connect it to the existing point.";
        steps[26].message = "<b><#E09500>Tutorial Complete!</color></b> You learned the basic tools to construct and edit a bridge. Ready for testing? Press <b><#E09500>Play</color></b> to begin the structural simulation.";
        var result = new List<TutorialStep>(steps);
        result.Insert(22, NewMessage(RoadFeedback, director));
        result.Insert(result.Count - 1, NewMessage(BeamComplete, director));
        // Keep the real open action, then continue straight to Grid. The
        // click-through statistics readout closes on accepted bridge placement.
        result.RemoveAt(12);
        return result.ToArray();
    }

    private static void RequireEvent(TutorialStep step, string method)
    {
        UnityEvent events = step.OnStepStart;
        for (int i = 0; events != null && i < events.GetPersistentEventCount(); i++)
            if (events.GetPersistentMethodName(i) == method && events.GetPersistentTarget(i) != null) return;
        throw new InvalidOperationException("The audited tutorial callback '" + method + "' is missing. No scene changes were applied.");
    }

    private static TutorialStep CopyStep(TutorialStep source)
    {
        return new TutorialStep
        {
            message = source.message, screenPosition = source.screenPosition,
            showNextButton = source.showNextButton, canSkip = source.canSkip,
            lockLook = source.lockLook, lockJump = source.lockJump, lockRun = source.lockRun,
            stepWaypoints = source.stepWaypoints, worldHighlightObject = source.worldHighlightObject,
            usePointer = source.usePointer, pointerTarget = source.pointerTarget,
            pointerOffset = source.pointerOffset, pointerRotation = source.pointerRotation,
            advanceOnClick = source.advanceOnClick, requiredAction = source.requiredAction,
            OnStepStart = source.OnStepStart
        };
    }

    private static void PointAt(TutorialStep step, RectTransform target)
    {
        step.usePointer = target != null;
        step.pointerTarget = target;
        step.pointerOffset = new Vector2(-90f, 0f);
        step.pointerRotation = -90f;
    }

    private static void Observe(TutorialStep step, TutorialStep pointerSource, UnityAction action, string message)
    {
        step.message = message;
        step.showNextButton = false;
        step.advanceOnClick = false;
        step.usePointer = true;
        step.pointerTarget = pointerSource.pointerTarget;
        step.pointerOffset = pointerSource.pointerOffset;
        step.pointerRotation = pointerSource.pointerRotation;
        step.OnStepStart = new UnityEvent();
        UnityEventTools.AddPersistentListener(step.OnStepStart, action);
    }

    private static TutorialStep NewMessage(string message, BuildTutorialDirector director)
    {
        var result = new TutorialStep { message = message, showNextButton = true, canSkip = false };
        UnityEventTools.AddPersistentListener(result.OnStepStart, director.LockAllUI);
        return result;
    }
}
