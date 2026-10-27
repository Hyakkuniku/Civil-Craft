#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Scoped, opt-in repair of Bhan's terminal IDs and Lola's map handoff.</summary>
[InitializeOnLoad]
public static class BhanLolaStoryRepair
{
    public const string RequestPath = "Temp/bhan-lola-story-repair.request";
    public const string ReportPath = "Logs/bhan-lola-story-repair.txt";
    private const string CanyonPath = "Assets/Scenes/CanyonCrossing.unity";
    private static bool running;
    private static double nextPoll;
    private static bool Busy => running || EditorApplication.isPlayingOrWillChangePlaymode ||
        EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer ||
        PrefabStageUtility.GetCurrentPrefabStage() != null;

    static BhanLolaStoryRepair() { EditorApplication.update += Poll; }

    private static void Poll()
    {
        if (Busy || EditorApplication.timeSinceStartup < nextPoll || !File.Exists(RequestPath)) return;
        nextPoll = EditorApplication.timeSinceStartup + 1;
        string command = File.ReadAllText(RequestPath).Trim();
        File.Delete(RequestPath);
        if (command == "coins") UpdateLolaCurrency();
        else Run(command == "verify");
    }

    [MenuItem("Tools/Civil Craft/Story/Use Coins in Lola Dialogue")]
    public static void UpdateLolaCurrency()
    {
        if (Busy) return;
        running = true;
        var report = new StringBuilder("Lola dialogue currency wording\n");
        try
        {
            string backup = "Temp/LolaCoinDialogue/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            Directory.CreateDirectory(backup);
            foreach (string path in new[] { CanyonPath, "Assets/Scenes/Multiplayer/Multiplayer.unity" })
            {
                string text = File.ReadAllText(path);
                Match[] matches = Regex.Matches(text, @"(?ms)^--- !u!114 &-?\d+[^\r\n]*\r?\n.*?(?=^--- !u!|\z)").Cast<Match>()
                    .Where(match => match.Value.Contains("guid: a1f34bc1e3214d1bab7180c2f9e260bb") &&
                        Regex.IsMatch(match.Value, @"(?m)^  progressionSaveId: ShopKeeper\s*$")).ToArray();
                Require(matches.Length == 1, "Expected the unique Lola progression in " + path);
                string original = matches[0].Value;
                string revised = Regex.Replace(original, @"\bGold\b", "Coins");
                File.Copy(path, backup + "/" + Path.GetFileName(path));
                Scene scene = SceneManager.GetSceneByPath(path);
                if (scene.IsValid() && scene.isLoaded)
                {
                    bool initiallyDirty = scene.isDirty;
                    NPCProgressionManager lola = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<NPCProgressionManager>(true))
                        .Single(npc => npc.ProgressionSaveId == "ShopKeeper");
                    string before = EditorJsonUtility.ToJson(lola);
                    Undo.RecordObject(lola, "Use Coins in Lola dialogue");
                    foreach (NPCProgressionPhase phase in lola.AuthoredPhases)
                    {
                        if (phase == null) continue;
                        UseCoins(phase.phaseDialogue);
                        if (phase.optionalDialogueSequence != null)
                            foreach (NPCOptionalDialogueStep step in phase.optionalDialogueSequence)
                                if (step != null) UseCoins(step.dialogue);
                    }
                    string after = EditorJsonUtility.ToJson(lola);
                    Require(after == Regex.Replace(before, @"\bGold\b", "Coins"), "A field outside Lola's dialogue changed.");
                    if (before != after)
                    {
                        EditorUtility.SetDirty(lola);
                        if (PrefabUtility.IsPartOfPrefabInstance(lola)) PrefabUtility.RecordPrefabInstancePropertyModifications(lola);
                        EditorSceneManager.MarkSceneDirty(scene);
                        if (initiallyDirty) report.AppendLine("NEEDS SAVE: " + path + ". Existing unsaved edits were not auto-saved.");
                        else
                        {
                            Require(EditorSceneManager.SaveScene(scene), "Could not save " + path);
                            report.AppendLine("SAVED: " + path);
                        }
                    }
                    else report.AppendLine("ALREADY UPDATED: " + path);
                }
                else
                {
                    // Do not open or reserialize the old multiplayer scene for
                    // three text edits. Preserve every unrelated serialized byte.
                    if (original != revised)
                        File.WriteAllText(path, text.Substring(0, matches[0].Index) + revised +
                            text.Substring(matches[0].Index + original.Length), new UTF8Encoding(false));
                    report.AppendLine("SAVED CLOSED SCENE: " + path + "; only Lola's Gold-to-Coins wording changed.");
                }
            }
            report.Insert(0, "PASS\n");
            report.AppendLine("BACKUP: " + backup);
        }
        catch (Exception error) { report.Insert(0, "FAILED\n"); report.AppendLine(error.ToString()); }
        finally
        {
            Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/lola-coin-dialogue.txt", report.ToString());
            running = false;
        }
    }

    private static void UseCoins(Dialogue dialogue)
    {
        if (dialogue == null || dialogue.sentences == null) return;
        for (int index = 0; index < dialogue.sentences.Length; index++)
            if (dialogue.sentences[index] != null)
                dialogue.sentences[index] = Regex.Replace(dialogue.sentences[index], @"\bGold\b", "Coins");
    }

    [MenuItem("Tools/Civil Craft/Story/Repair Bhan IDs and Lola Handoff")]
    public static void Apply() { Run(false); }

    [MenuItem("Tools/Civil Craft/Story/Verify Bhan IDs and Lola Handoff (Read Only)")]
    public static void Verify() { Run(true); }

    private static void Run(bool verify)
    {
        if (Busy) return;
        running = true;
        var report = new StringBuilder();
        int undo = -1;
        try
        {
            Scene scene = SceneManager.GetSceneByPath(CanyonPath);
            Require(scene.IsValid() && scene.isLoaded, "Open CanyonCrossing in Edit Mode. No scenes were opened or saved.");
            bool initiallyDirty = scene.isDirty;
            NPCProgressionManager[] all = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<NPCProgressionManager>(true)).ToArray();
            NPCProgressionManager bhan = all.Single(npc => npc.ProgressionSaveId == "MainContractNPC");
            NPCProgressionManager lola = all.Single(npc => npc.ProgressionSaveId == "ShopKeeper");
            Require(bhan.PhaseCount == 16 && bhan.AuthoredPhases[10].phaseId == "Go to Shopkeeper" &&
                bhan.AuthoredPhases[11].phaseId == "CollectMap", "Bhan's authored phase layout differs; stopped without substituting phases.");
            Require((bhan.AuthoredPhases[14].phaseId == "Delete" && bhan.AuthoredPhases[15].phaseId == "Delete") ||
                (bhan.AuthoredPhases[14].phaseId == "Retired" && bhan.AuthoredPhases[15].phaseId == "Despawned"),
                "Unexpected terminal phase IDs. Review the authored story first.");
            NPCProgressionPhase repeat = lola.AuthoredPhases.Single(phase => phase.phaseId == "RepeatDIal");
            TutorialSequence tutorial = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<TutorialSequence>(true))
                .Single(sequence => sequence.lessonName == "GoBackToProfBhan");
            NPCStoryHandoff handoff = lola.GetComponent<NPCStoryHandoff>();
            bool legacy = repeat.onDialogueFinished.GetPersistentEventCount() == 1 &&
                repeat.onDialogueFinished.GetPersistentTarget(0) == tutorial &&
                repeat.onDialogueFinished.GetPersistentMethodName(0) == nameof(TutorialSequence.StartTutorial) &&
                repeat.onDialogueFinishedPhaseMove.npc == bhan && repeat.onDialogueFinishedPhaseMove.phaseId == "CollectMap";
            bool repaired = handoff != null && repeat.onDialogueFinished.GetPersistentEventCount() == 1 &&
                repeat.onDialogueFinished.GetPersistentTarget(0) == handoff &&
                repeat.onDialogueFinished.GetPersistentMethodName(0) == nameof(NPCStoryHandoff.TryHandoff) &&
                repeat.onDialogueFinishedPhaseMove.npc == null && string.IsNullOrEmpty(repeat.onDialogueFinishedPhaseMove.phaseId);
            Require(legacy || repaired, "Lola has custom handoff callbacks. They were not overwritten.");
            if (verify)
            {
                Require(repaired && bhan.AuthoredPhases[14].phaseId == "Retired" && bhan.AuthoredPhases[15].phaseId == "Despawned",
                    "The scene repair has not been applied.");
            }
            else
            {
                string backup = "Temp/BhanLolaStoryRepair/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                Directory.CreateDirectory(backup);
                File.Copy(CanyonPath, backup + "/CanyonCrossing.unity");
                string[] bhanBefore = bhan.AuthoredPhases.Select(JsonUtility.ToJson).ToArray();
                string[] lolaBefore = lola.AuthoredPhases.Select(JsonUtility.ToJson).ToArray();
                string repeatDialogue = JsonUtility.ToJson(repeat.phaseDialogue);
                Undo.IncrementCurrentGroup(); undo = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Repair Bhan IDs and Lola handoff");
                Undo.RecordObjects(new UnityEngine.Object[] { bhan, lola }, "Repair story references");
                bhan.AuthoredPhases[14].phaseId = "Retired";
                bhan.AuthoredPhases[15].phaseId = "Despawned";
                if (handoff == null) handoff = Undo.AddComponent<NPCStoryHandoff>(lola.gameObject);
                Undo.RecordObject(handoff, "Configure Lola handoff");
                handoff.targetNPC = bhan;
                handoff.eligiblePhaseId = "Go to Shopkeeper";
                handoff.destinationPhaseId = "CollectMap";
                handoff.returnTutorial = tutorial;
                handoff.completionFeatureId = "minimap";
                if (legacy)
                {
                    UnityEventTools.RemovePersistentListener(repeat.onDialogueFinished, 0);
                    UnityEventTools.AddPersistentListener(repeat.onDialogueFinished, handoff.TryHandoff);
                    repeat.onDialogueFinishedPhaseMove.npc = null;
                    repeat.onDialogueFinishedPhaseMove.phaseId = string.Empty;
                }
                for (int index = 0; index < 14; index++)
                    Require(JsonUtility.ToJson(bhan.AuthoredPhases[index]) == bhanBefore[index], "An earlier Bhan phase changed.");
                for (int index = 14; index < 16; index++)
                    Require(JsonUtility.ToJson(bhan.AuthoredPhases[index]) == bhanBefore[index].Replace("\"phaseId\":\"Delete\"",
                        "\"phaseId\":\"" + (index == 14 ? "Retired" : "Despawned") + "\""), "A terminal field other than its ID changed.");
                for (int index = 0; index < lola.PhaseCount; index++)
                    if (lola.AuthoredPhases[index] != repeat)
                        Require(JsonUtility.ToJson(lola.AuthoredPhases[index]) == lolaBefore[index], "An unrelated Lola phase changed.");
                Require(JsonUtility.ToJson(repeat.phaseDialogue) == repeatDialogue && repeat.repeatDialogue,
                    "Lola's repeatable shop conversation changed.");
                foreach (UnityEngine.Object item in new UnityEngine.Object[] { bhan, lola, handoff })
                {
                    EditorUtility.SetDirty(item);
                    if (PrefabUtility.IsPartOfPrefabInstance(item)) PrefabUtility.RecordPrefabInstancePropertyModifications(item);
                }
                Require(bhan.AuthoredPhases.Select(phase => phase.phaseId).Distinct().Count() == bhan.PhaseCount, "Bhan still has duplicate IDs.");
                EditorSceneManager.MarkSceneDirty(scene);
                Undo.CollapseUndoOperations(undo); undo = -1;
                if (!initiallyDirty)
                {
                    Require(EditorSceneManager.SaveScene(scene), "Could not save the repaired CanyonCrossing.");
                    report.AppendLine("SAVED: CanyonCrossing.");
                }
                else report.AppendLine("NEEDS SAVE: CanyonCrossing. Existing unsaved edits were preserved and were not auto-saved.");
                report.AppendLine("BACKUP: " + backup);
            }
            Require(handoff != null && handoff.targetNPC == bhan && handoff.eligiblePhaseId == "Go to Shopkeeper" &&
                handoff.destinationPhaseId == "CollectMap" && handoff.returnTutorial == tutorial && handoff.completionFeatureId == "minimap",
                "Lola's guarded map handoff is not fully configured.");
            report.Insert(0, "PASS\n");
            report.AppendLine("Bhan's 16 phase indexes, dialogue and events retained; trailing IDs are Retired and Despawned. Legacy Delete saves retain their saved index.");
            report.AppendLine("Lola's repeat dialogue uses only the guarded handoff, with no unconditional tutorial start or Bhan phase move. No player saves, gameplay callbacks or other scenes were touched.");
        }
        catch (Exception error)
        {
            if (undo >= 0) Undo.RevertAllDownToGroup(undo);
            report.Insert(0, "FAILED\n"); report.AppendLine(error.ToString());
        }
        finally
        {
            Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, report.ToString());
            running = false;
        }
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
#endif
