using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Detached clock/lifecycle checks. No production scenes or player data are touched.</summary>
public static class SimulationPlaybackIntegrationValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int assertions;

    public static void RunAll()
    {
        Run();
        SimulationSpeedUIAuthoring.ApplyAndValidate();
    }

    [MenuItem("Tools/Civil Craft/Simulation Speed/Validate Playback Lifecycle")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        float originalTime = Time.timeScale, fixedStep = Time.fixedDeltaTime;
        GameManager previousGame = GameManager.Instance;
        PauseManager previousPause = PauseManager.Instance;
        var report = new StringBuilder();
        GameObject fixture = new GameObject("Detached Playback Lifecycle Fixture");
        fixture.hideFlags = HideFlags.HideAndDontSave;
        fixture.SetActive(false);
        BridgePhysicsManager physics = null;
        assertions = 0;
        try
        {
            var game = fixture.AddComponent<GameManager>();
            var pause = fixture.AddComponent<PauseManager>();
            physics = fixture.AddComponent<BridgePhysicsManager>();
            SetStatic(typeof(GameManager), "<Instance>k__BackingField", game);
            SetStatic(typeof(PauseManager), "<Instance>k__BackingField", pause);
            Set(pause, "allowPauseInBuildMode", true);
            Set(game, "<CurrentState>k__BackingField", GameManager.GameState.Building);
            Set(physics, "pendingSimulationStart", true);
            Time.timeScale = 1f;
            Call(physics, "OnEnable");
            Call(physics, "BeginSimulationPlayback");
            Require(physics.CanAdjustSimulationPlayback && physics.SimulationPlaybackLabel == "0x", "Neutral is running speed, not frozen time.");
            Require(physics.ChangeSimulationPlayback(1), "First fast-forward click accepted.");
            Require(physics.ChangeSimulationPlayback(1), "Second fast-forward click accepted.");
            Equal(Time.timeScale, 2f, "Fast forward composes to2x.");
            Require(physics.ChangeSimulationPlayback(1), "Third fast-forward click accepted.");
            Equal(Time.timeScale, 3f, "Fast-forward reaches 3x.");
            Require(physics.SimulationPlaybackStep == SimulationPlaybackState.MaximumStep && physics.SimulationPlaybackLabel == "3x",
                "The final notch displays three-times playback.");
            Require(!physics.ChangeSimulationPlayback(1) && !physics.ChangeSimulationPlayback(int.MaxValue),
                "Fast-forward clamps at three-times speed without wrapping.");
            object lesson = new object();
            Require(physics.SetSimulationLessonPlayback(lesson, .5f), "A lesson may open at three-times playback.");
            Equal(Time.timeScale, 1.5f, "Lesson half-speed composes with three-times player speed.");
            physics.ClearSimulationLessonPlayback(lesson);
            Equal(Time.timeScale, 3f, "Closing the lesson restores three-times player speed.");
            Require(physics.ChangeSimulationPlayback(-1), "Highest fast-forward speed can step down.");
            Equal(Time.timeScale, 2f, "Stepping down reaches 2x.");
            Require(physics.SetSimulationLessonPlayback(lesson, .5f), "Authored lesson lease accepted.");
            Equal(Time.timeScale, 1f, "Lesson half-speed composes with2x player speed.");
            Require(physics.ChangeSimulationPlayback(-1), "Player may change speed while lesson is active.");
            Equal(Time.timeScale, .75f, "Authored half-speed times1.5x player speed.");
            physics.ClearSimulationLessonPlayback(lesson);
            Equal(Time.timeScale, 1.5f, "Closing lesson retains the selected player speed.");
            Call(physics, "EndSimulationPlayback");
            Equal(Time.timeScale, 1f, "End restores entry scale.");
            Require(!physics.CanAdjustSimulationPlayback && !physics.ChangeSimulationPlayback(1), "Finished attempt rejects speed input.");

            for (int step = SimulationPlaybackState.MinimumStep; step <= SimulationPlaybackState.MaximumStep; step++)
            {
                Time.timeScale = 1f;
                Call(physics, "BeginSimulationPlayback");
                for (int i = 0; i < Math.Abs(step); i++) physics.ChangeSimulationPlayback(Math.Sign(step));
                float expected = Time.timeScale;
                pause.PauseGame();
                Equal(Time.timeScale, 0f, "Pause freezes at step" + step);
                Require(!physics.CanAdjustSimulationPlayback && !physics.ChangeSimulationPlayback(1), "Pause blocks speed input.");
                pause.ResumeGame();
                Equal(Time.timeScale, expected, "Resume restores selected step" + step);
                Call(physics, "EndSimulationPlayback");
                Equal(Time.timeScale, 1f, "End restores normal after step" + step);
            }

            Call(physics, "BeginSimulationPlayback");
            physics.ChangeSimulationPlayback(1);
            pause.PauseGame();
            Call(physics, "EndSimulationPlayback");
            Equal(Time.timeScale, 0f, "Ending while paused does not unpause.");
            pause.ResumeGame();
            Equal(Time.timeScale, 1f, "Ending while paused replaces the captured modified speed.");
            Call(physics, "Update");

            Time.timeScale = .8f;
            Call(physics, "BeginSimulationPlayback");
            physics.ChangeSimulationPlayback(-1);
            Equal(Time.timeScale, .4f, "Entry speed is preserved as baseline.");
            Call(physics, "FinishSimulationPlayback", null, null);
            Equal(Time.timeScale, .8f, "Success restores baseline immediately, before retry/save.");
            Time.timeScale = 1f;
            Call(physics, "BeginSimulationPlayback");
            physics.ChangeSimulationPlayback(1);
            Call(physics, "FailSimulationPlayback", "Detached failure");
            Equal(Time.timeScale, 1f, "Failure restores baseline immediately.");
            Call(physics, "BeginSimulationPlayback");
            Require(physics.SimulationPlaybackStep == 0, "Retry starts neutral.");
            physics.ChangeSimulationPlayback(-1);
            Set(physics, "lockStressTracking", true);
            Call(physics, "Update");
            Equal(Time.timeScale, 1f, "Stopped stress tracking ends playback ownership.");
            Set(physics, "lockStressTracking", false);
            Call(physics, "BeginSimulationPlayback");
            physics.ChangeSimulationPlayback(1);
            Time.timeScale = .9f;
            Call(physics, "EndSimulationPlayback");
            Equal(Time.timeScale, .9f, "Teardown preserves another tool's later clock change.");

            Time.timeScale = 1f;
            Call(physics, "BeginSimulationPlayback");
            physics.ChangeSimulationPlayback(1);
            Time.timeScale = .9f;
            Call(physics, "Update");
            Require(!physics.CanAdjustSimulationPlayback && !physics.ChangeSimulationPlayback(1), "External clock ownership is released until a new attempt.");
            Equal(Time.timeScale, .9f, "Later input does not fight the external clock owner.");
            Time.timeScale = 1f;
            Call(physics, "BeginSimulationPlayback");
            physics.ChangeSimulationPlayback(1);
            Set(game, "<CurrentState>k__BackingField", GameManager.GameState.Normal);
            Call(physics, "Update");
            Equal(Time.timeScale, 1f, "Leaving Build Mode restores overworld speed even before delayed teardown.");
            Set(game, "<CurrentState>k__BackingField", GameManager.GameState.CargoTesting);
            Call(physics, "BeginSimulationPlayback");
            Require(physics.CanAdjustSimulationPlayback, "Solo player-carried cargo tests support playback.");
            Set(game, "<CurrentState>k__BackingField", GameManager.GameState.Building);

            var challengeLocation = fixture.AddComponent<BuildLocation>();
            physics.ChangeSimulationPlayback(1);
            Set(physics, "sessionTestLocation", challengeLocation);
            Require(!physics.CanAdjustSimulationPlayback && !physics.ChangeSimulationPlayback(1), "Challenge entry blocks local playback immediately.");
            Call(physics, "Update");
            Equal(Time.timeScale, 1f, "Entering a challenge releases the solo clock.");
            Call(physics, "BeginSimulationPlayback");
            Require(!physics.SetSimulationLessonPlayback(lesson, .5f), "Challenge lessons cannot alter the shared clock.");
            Set(physics, "sessionTestLocation", null);

            var cloud = fixture.AddComponent<CloudSaveManager>();
            Set(cloud, "pausedForConflict", true);
            Set(cloud, "previousTimeScale", .5f);
            Time.timeScale = 0f;
            cloud.ReplacePausedTimeScale(.5f, 1f);
            Call(cloud, "CloseConflictOverlay");
            Equal(Time.timeScale, 1f, "Cloud conflict can restore the corrected baseline after playback teardown.");

            Time.timeScale = 1f;
            Call(physics, "BeginSimulationPlayback");
            physics.ChangeSimulationPlayback(-1);
            physics.SetSimulationLessonPlayback(lesson, .5f);
            Call(physics, "DisposeSimulationPlayback");
            Equal(Time.timeScale, 1f, "Disable/teardown releases both player speed and lesson leases.");
            Call(physics, "OnEnable");
            Call(physics, "BeginSimulationPlayback");
            Require(physics.SimulationPlaybackStep == 0, "Re-enable/domain-reload-off starts clean.");
            Equal(Time.fixedDeltaTime, fixedStep, "All playback operations preserve the physics step size.");
            report.AppendLine("PASS: " + assertions + " native clock, lesson, pause, result, retry and teardown assertions.");
        }
        catch (Exception error)
        {
            report.AppendLine("FAIL: " + error);
            throw;
        }
        finally
        {
            if (physics != null) Call(physics, "DisposeSimulationPlayback");
            SetStatic(typeof(GameManager), "<Instance>k__BackingField", previousGame);
            SetStatic(typeof(PauseManager), "<Instance>k__BackingField", previousPause);
            Object.DestroyImmediate(fixture);
            Time.timeScale = originalTime;
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/simulation-playback-lifecycle.txt", report.ToString());
        }
        Debug.Log(report.ToString());
    }

    private static void Require(bool value, string message)
    { assertions++; if (!value) throw new InvalidOperationException(message); }
    private static void Equal(float actual, float expected, string message)
    { Require(Mathf.Approximately(actual, expected), message + ": " + actual + " != " + expected); }
    private static void Set(object target, string name, object value)
    { target.GetType().GetField(name, Private | BindingFlags.Public).SetValue(target, value); }
    private static void SetStatic(Type type, string name, object value)
    { type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, value); }
    private static void Call(object target, string name, params object[] values)
    { target.GetType().GetMethod(name, Private).Invoke(target, values); }
}
