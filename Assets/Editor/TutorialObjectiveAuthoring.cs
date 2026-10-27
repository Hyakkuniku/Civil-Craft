#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Explicit opt-in for the existing Follow Professor Bhan sequence.</summary>
[InitializeOnLoad]
public static class TutorialObjectiveAuthoring
{
    public const string RequestPath = "Temp/tutorial-objective-setup.request";
    public const string ReportPath = "Logs/tutorial-objective-setup.txt";
    private const string Canyon = "Assets/Scenes/CanyonCrossing.unity";
    private static bool running;
    private static double nextPoll;
    private static bool Busy => running || EditorApplication.isCompiling || EditorApplication.isUpdating ||
        EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer ||
        PrefabStageUtility.GetCurrentPrefabStage() != null;

    static TutorialObjectiveAuthoring() { EditorApplication.update += Poll; }

    private static void Poll()
    {
        if (Busy || EditorApplication.timeSinceStartup < nextPoll || !File.Exists(RequestPath)) return;
        nextPoll = EditorApplication.timeSinceStartup + 1;
        bool verify = File.ReadAllText(RequestPath).Trim() == "verify";
        File.Delete(RequestPath);
        Run(verify);
    }

    [MenuItem("Tools/Civil Craft/Story/Show Follow Professor Bhan in Objectives")]
    public static void Apply() { Run(false); }

    private static void Run(bool verify)
    {
        if (Busy) return;
        running = true;
        int undo = -1;
        try
        {
            Scene scene = SceneManager.GetSceneByPath(Canyon);
            Require(scene.IsValid() && scene.isLoaded, "Open CanyonCrossing in Edit Mode; no other scenes were opened or saved.");
            bool initiallyDirty = scene.isDirty;
            TutorialSequence sequence = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<TutorialSequence>(true))
                .Single(value => value.lessonName == "FollowProfessorBhan");
            Require(sequence.tutorialSteps != null && sequence.tutorialSteps.Length == 1 &&
                sequence.tutorialSteps[0] != null && sequence.tutorialSteps[0].stepWaypoints != null &&
                sequence.tutorialSteps[0].stepWaypoints.Count == 1 && sequence.tutorialSteps[0].stepWaypoints[0].target != null,
                "The existing Follow Professor Bhan guide differs; stopped without replacing its steps.");
            string result = "PASS\n";
            if (!verify && (!sequence.showInObjectives || sequence.objectiveTitle != "Follow Professor Bhan"))
            {
                string backup = "Temp/TutorialObjectiveSetup/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                Directory.CreateDirectory(backup);
                File.Copy(Canyon, backup + "/CanyonCrossing.unity");
                string[] steps = sequence.tutorialSteps.Select(JsonUtility.ToJson).ToArray();
                Undo.IncrementCurrentGroup(); undo = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Track Follow Professor Bhan in Objectives");
                Undo.RecordObject(sequence, "Enable tutorial objective");
                sequence.showInObjectives = true;
                sequence.objectiveTitle = "Follow Professor Bhan";
                EditorUtility.SetDirty(sequence);
                if (PrefabUtility.IsPartOfPrefabInstance(sequence)) PrefabUtility.RecordPrefabInstancePropertyModifications(sequence);
                Require(sequence.tutorialSteps.Select(JsonUtility.ToJson).SequenceEqual(steps), "A tutorial step, waypoint or event changed.");
                EditorSceneManager.MarkSceneDirty(scene);
                Undo.CollapseUndoOperations(undo); undo = -1;
                if (!initiallyDirty)
                {
                    Require(EditorSceneManager.SaveScene(scene), "Could not save CanyonCrossing.");
                    result += "SAVED: CanyonCrossing.\n";
                }
                else result += "NEEDS SAVE: CanyonCrossing. Existing unsaved edits were preserved and were not auto-saved.\n";
                result += "BACKUP: " + backup + "\n";
            }
            Require(sequence.showInObjectives && sequence.objectiveTitle == "Follow Professor Bhan", "The objective option has not been enabled.");
            result += "FollowProfessorBhan uses Show in Objectives with its readable title. Existing steps, waypoints, arrival flags and events are retained. Other tutorial opt-ins, scenes and player saves were not changed.\n";
            Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, result);
        }
        catch (Exception error)
        {
            if (undo >= 0) Undo.RevertAllDownToGroup(undo);
            Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, "FAILED\n" + error);
        }
        finally { running = false; }
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
#endif
