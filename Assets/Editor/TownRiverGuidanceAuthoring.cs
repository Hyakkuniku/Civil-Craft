#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Adds checked objectives and interaction-needed attention to the existing Town River story.</summary>
[InitializeOnLoad]
public static class TownRiverGuidanceAuthoring
{
    public const string RequestPath = "Temp/townriver-guidance.request";
    public const string ReportPath = "Logs/townriver-guidance.txt";
    private const string Canyon = "Assets/Scenes/CanyonCrossing.unity";
    private const string CatalogPath = "Assets/Resources/TutorialObjectiveCatalog.asset";
    private const string RootName = "Town River Objective Guidance";
    private static double nextPoll;

    private sealed class Story
    {
        public readonly string npcId, title, instruction;
        public readonly int phase;
        public readonly bool route, follow;
        public Story(string npcId, int phase, string title, string instruction, bool route = true, bool follow = false)
        { this.npcId = npcId; this.phase = phase; this.title = title; this.instruction = instruction; this.route = route; this.follow = follow; }
    }
    private sealed class Existing
    {
        public readonly ulong id;
        public readonly string npcId, lesson, title;
        public Existing(ulong id, string npcId, string lesson, string title)
        { this.id = id; this.npcId = npcId; this.lesson = lesson; this.title = title; }
    }
    private static readonly string[] NpcIds = { "MarshNPC", "FordNPC", "BrooksNpc", "RonanNpc" };
    private static readonly string[][] PhaseIds = {
        new[] { "MeetingMarsh", "ExplainSituation", "Material Introduction", "ProjectBriefing", "MarshReview", "Resolved" },
        new[] { "MeetingFord", "ExplainSituation", "LessonIntroduction", "ProjectBriefing", "FordReview", "Resolved" },
        new[] { "MeetingBrooks", "ExplainSituation", "LessonIntroduction", "ProjectBriefing", "BrooksReview", "Resolved" },
        new[] { "MeetingRonan", "ExplainSituation", "LessonIntroduction", "ProjectBriefing", "RonanReview", "Resolved" }
    };
    private static readonly Story[] Stories = {
        new Story("MarshNPC", 0, "Meet Marsh", "Talk to Marsh about the residential crossing."),
        new Story("MarshNPC", 1, "Follow Marsh and inspect the crossing", "Follow Marsh to the residential crossing, then talk to him about the site.", true, true),
        new Story("MarshNPC", 2, "Learn about concrete and Arch Bridges", "Listen to Marsh, collect the Concrete Beam, Road and Pier introductions, and read the Arch Bridge lesson."),
        new Story("MarshNPC", 4, "Review Arch Bridges and complete the quiz", "Review the Arch Bridge lesson in the Almanac, then open the Quiz tab and submit the quiz.", false),
        new Story("FordNPC", 0, "Meet Ford", "Talk to Ford about the construction route."),
        new Story("FordNPC", 1, "Talk to Ford at the construction site", "Meet Ford at the construction site and discuss the crossing.", true, true),
        new Story("FordNPC", 2, "Learn Material Stress", "Listen to Ford and read the Material Stress lesson before designing the cargo-truck crossing."),
        new Story("FordNPC", 4, "Review Material Stress and complete the quiz", "Review Material Stress in the Almanac, then open the Quiz tab and submit the quiz.", false),
        new Story("BrooksNpc", 0, "Meet Brooks", "Talk to Brooks about connecting the market to the main road."),
        new Story("BrooksNpc", 1, "Talk to Brooks at the market crossing", "Meet Brooks at the market crossing and discuss the route.", true, true),
        new Story("BrooksNpc", 2, "Learn Moving Loads", "Listen to Brooks and read the Moving Load lesson before building the market crossing."),
        new Story("BrooksNpc", 4, "Review Moving Loads and complete the quiz", "Review Moving Load in the Almanac, then open the Quiz tab and submit the quiz.", false),
        new Story("RonanNpc", 0, "Meet Ronan", "Talk to Ronan about the southern crossing."),
        new Story("RonanNpc", 1, "Talk to Ronan at the southern crossing", "Meet Ronan at the southern crossing and discuss the site.", true, true),
        new Story("RonanNpc", 2, "Learn Shear", "Listen to Ronan and read the Shear lesson before planning the southern crossing."),
        new Story("RonanNpc", 4, "Review Shear and complete the quiz", "Review Shear in the Almanac, submit the quiz, and finish Ronan's conversation about Ironhaven.", false)
    };
    private static readonly Existing[] ExistingGuides = {
        new Existing(1274516943, "FordNPC", "Story/Ford/FollowToSite-v1", "Follow Ford to the construction crossing"),
        new Existing(1922576282, "BrooksNpc", "Story/Brooks/FollowToSite-v1", "Follow Brooks to the market crossing"),
        new Existing(1042508027, "RonanNpc", "Story/Ronan/FollowToSite-v1", "Follow Ronan to the southern crossing")
    };

    static TownRiverGuidanceAuthoring() { EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        nextPoll = EditorApplication.timeSinceStartup + 1;
        if (!File.Exists(RequestPath)) return;
        string command = File.ReadAllText(RequestPath).Trim(); File.Delete(RequestPath);
        try
        {
            if (command == "apply") Apply();
            else if (command == "validate") Validate();
            else if (command == "inspect") Inspect();
            else throw new InvalidOperationException("Use inspect, apply or validate for Town River guidance.");
        }
        catch (Exception error) { Report("FAILED\n" + error); Debug.LogException(error); }
    }

    [MenuItem("Tools/Civil Craft/Story/Town River Objectives/1. Apply (Do Not Save Scene)")]
    public static void Apply()
    {
        Scene scene = Loaded(); NPCProgressionManager[] npcs = NpcIds.Select(id => NPC(scene, id)).ToArray(); Preflight(scene, npcs);
        if (GuidanceRoot(scene) != null) { Validate(); return; }
        Require(npcs.All(npc => npc.GetComponent<NPCStoryObjectiveGuidance>() == null), "A target NPC already has custom objective guidance. It was not overwritten.");
        TutorialObjectiveCatalog catalog = Catalog();
        Require(!EditorUtility.IsDirty(catalog), "The objective catalog has unsaved edits. Save that asset first; nothing was applied.");
        var protectedState = CaptureProtectedState(scene, npcs);
        var npcState = npcs.ToDictionary(npc => npc, npc => WithoutAttention(EditorJsonUtility.ToJson(npc)));
        var guideState = ExistingGuides.ToDictionary(entry => entry.id, entry => WithoutObjective(EditorJsonUtility.ToJson(Sequence(scene, entry))));
        var catalogState = catalog.entries.ToDictionary(entry => entry.objectiveId, JsonUtility.ToJson, StringComparer.Ordinal);
        PlayerDataManager saves = PlayerDataManager.Instance; PlayerData player = saves != null ? saves.CurrentData : null;
        string playerBefore = player != null ? JsonUtility.ToJson(player) : null;
        string backup = "Logs/TownRiverGuidanceBackups/" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        Directory.CreateDirectory(backup); File.Copy(Canyon, backup + "/CanyonCrossing.unity"); File.Copy(CatalogPath, backup + "/TutorialObjectiveCatalog.asset");
        bool initiallyDirty = scene.isDirty;
        Scene originalActive = SceneManager.GetActiveScene();
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Enable Town River objectives and attention");
        try
        {
            if (SceneManager.GetActiveScene() != scene) SceneManager.SetActiveScene(scene);
            Require(SceneManager.GetActiveScene() == scene, "Could not activate CanyonCrossing for the owned objective definitions.");
            GameObject root = NewObject(scene, RootName, null);
            foreach (NPCProgressionManager npc in npcs)
            {
                Undo.RecordObject(npc, "Enable interaction-needed NPC attention");
                var data = new SerializedObject(npc); data.FindProperty("onlyShowExclamationWhenInteractionNeeded").boolValue = true; data.ApplyModifiedProperties();
                foreach (NPCProgressionPhase phase in npc.AuthoredPhases) phase.showExclamationMark = phase.phaseId != "Resolved";
                Mark(npc);
                NPCStoryObjectiveGuidance guidance = Undo.AddComponent<NPCStoryObjectiveGuidance>(npc.gameObject);
                var owner = new SerializedObject(guidance); owner.FindProperty("npc").objectReferenceValue = npc; owner.ApplyModifiedProperties();
                foreach (Story definition in Stories.Where(entry => entry.npcId == npc.ProgressionSaveId))
                {
                    NPCProgressionPhase phase = npc.AuthoredPhases[definition.phase];
                    TutorialSequence objective = Undo.AddComponent<TutorialSequence>(NewObject(scene, npc.ProgressionSaveId + " - " + definition.title, root.transform));
                    objective.lessonName = "Objective/" + npc.ProgressionSaveId + "/" + phase.phaseId;
                    objective.showInObjectives = true; objective.objectiveTitle = definition.title;
                    objective.ConfigureObjectiveIdentity(StoryIdentity(npc.ProgressionSaveId, phase.phaseId));
                    objective.requiredPreviousLesson = string.Empty; objective.playOnStart = false; objective.nextSequence = null; objective.autoStartNextSequence = false;
                    Transform target = Target(npc, definition);
                    objective.tutorialSteps = new[] { new TutorialStep { message = definition.instruction, screenPosition = TutorialPosition.Left,
                        showNextButton = false, canSkip = false, usePointer = false, advanceOnClick = false,
                        stepWaypoints = target != null ? new List<GuiderWaypoint> { new GuiderWaypoint { target = target, advancesTutorial = false } } : new List<GuiderWaypoint>() } };
                    guidance.entries.Add(new NPCStoryObjectiveGuidance.Entry { phaseId = phase.phaseId, objective = objective,
                        completeAfterDialogue = false, completionFeatureId = string.Empty });
                    Mark(objective);
                }
                Mark(guidance);
            }
            foreach (Existing entry in ExistingGuides)
            {
                TutorialSequence sequence = Sequence(scene, entry); Undo.RecordObject(sequence, "Enable existing follow tutorial objective checkbox");
                sequence.showInObjectives = true; sequence.objectiveTitle = entry.title; sequence.ConfigureObjectiveIdentity(ExistingIdentity(entry)); Mark(sequence);
            }
            Undo.RecordObject(catalog, "Merge Town River follow definitions");
            foreach (Existing entry in ExistingGuides)
            {
                TutorialObjectiveCatalog.Entry existing = catalog.entries.SingleOrDefault(value => value.objectiveId == ExistingIdentity(entry));
                if (existing == null) catalog.entries.Add(CatalogEntry(entry));
                else Require(SameCatalog(existing, entry), "A catalog identity conflicts with this existing follow guide: " + entry.title);
            }
            ValidateScene(scene, npcs);
            foreach (var pair in npcState) Require(WithoutAttention(EditorJsonUtility.ToJson(pair.Key)) == pair.Value, "An NPC changed outside attention settings; update rolled back.");
            foreach (Existing entry in ExistingGuides) Require(WithoutObjective(EditorJsonUtility.ToJson(Sequence(scene, entry))) == guideState[entry.id], "Existing tutorial steps, routes or callbacks changed; update rolled back.");
            foreach (var pair in catalogState) Require(catalog.entries.Any(entry => entry.objectiveId == pair.Key && JsonUtility.ToJson(entry) == pair.Value), "An existing catalog definition changed; update rolled back.");
            foreach (var pair in protectedState) Require(pair.Key != null && EditorJsonUtility.ToJson(pair.Key) == pair.Value, "An unrelated component, contract, camera, tutorial or transform changed; update rolled back.");
            Require(saves == PlayerDataManager.Instance && (player == null || ReferenceEquals(player, saves.CurrentData) && JsonUtility.ToJson(player) == playerBefore), "Editor authoring changed live player data; update rolled back.");
            EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssetIfDirty(catalog); EditorSceneManager.MarkSceneDirty(scene); Undo.CollapseUndoOperations(group);
            Report("PASS: Marsh, Ford, Brooks and Ronan use interaction-needed attention; Resolved phases remain idle.\n" +
                "PASS: " + Stories.Length + " checked passive story objectives and three existing follow tutorials cover the current story; contract objectives are unchanged.\n" +
                "PASS: existing dialogues, phase IDs, callbacks, lessons, quizzes, contracts, rewards, budgets, loads, materials, scenery and player data preserved.\n" +
                "BACKUP: " + backup + "\nSCENE_WAS_DIRTY: " + initiallyDirty + "\nNEEDS_SAVE: CanyonCrossing. No scene was saved.");
        }
        catch { Undo.RevertAllDownToGroup(group); throw; }
        finally { if (originalActive.IsValid() && originalActive.isLoaded && SceneManager.GetActiveScene() != originalActive) SceneManager.SetActiveScene(originalActive); }
    }

    [MenuItem("Tools/Civil Craft/Story/Town River Objectives/2. Validate (Read Only)")]
    public static void Validate()
    {
        Scene scene = Loaded(); ValidateScene(scene, NpcIds.Select(id => NPC(scene, id)).ToArray());
        Report("PASS: Town River objective definitions, existing follow checkboxes, catalog and interaction-needed attention are wired.\nNEEDS_SAVE: " + (scene.isDirty ? "CanyonCrossing" : "none"));
    }
    [MenuItem("Tools/Civil Craft/Story/Town River Objectives/3. Inspect (Read Only)")]
    public static void Inspect()
    {
        Scene scene = Loaded(); Preflight(scene, NpcIds.Select(id => NPC(scene, id)).ToArray());
        Report("PASS: Marsh, Ford, Brooks and Ronan phase identities and three existing follow guides verified.\nSCENE_DIRTY: " + scene.isDirty + "\nAlready applied: " + (GuidanceRoot(scene) != null));
    }
    private static void Preflight(Scene scene, NPCProgressionManager[] npcs)
    {
        for (int index = 0; index < npcs.Length; index++)
        {
            Require(npcs[index].AuthoredPhases.Select(phase => phase?.phaseId).SequenceEqual(PhaseIds[index]), "An audited NPC phase identity/order differs: " + NpcIds[index]);
            Require(npcs[index].AuthoredPhases[3].contract != null && npcs[index].AuthoredPhases.Where((phase, phaseIndex) => phaseIndex != 3).All(phase => phase.contract == null),
                "An audited main-contract binding differs: " + NpcIds[index]);
        }
        foreach (Existing entry in ExistingGuides)
        {
            TutorialSequence sequence = Sequence(scene, entry);
            Require(sequence.lessonName == entry.lesson && sequence.tutorialSteps != null && sequence.tutorialSteps.Length == 1 &&
                !sequence.playOnStart && !sequence.autoStartNextSequence && sequence.nextSequence == null,
                "The existing follow tutorial identity/structure differs: " + entry.title);
            Require(All<TutorialSequence>(scene).Count(value => value.lessonName == entry.lesson) == 1, "Legacy follow history is ambiguous: " + entry.lesson);
            NPCProgressionManager npc = npcs.Single(value => value.ProgressionSaveId == entry.npcId);
            Require(sequence.tutorialSteps[0].stepWaypoints != null && sequence.tutorialSteps[0].stepWaypoints.Count == 1 &&
                sequence.tutorialSteps[0].stepWaypoints[0].target == npc.AuthoredPhases[1].targetLocation && sequence.tutorialSteps[0].stepWaypoints[0].advancesTutorial,
                "An existing genuine follow-arrival binding differs: " + entry.title);
        }
        TutorialObjectiveCatalog catalog = Catalog();
        Require(catalog.entries != null && catalog.entries.All(entry => entry != null && !string.IsNullOrWhiteSpace(entry.objectiveId)) &&
            catalog.entries.Select(entry => entry.objectiveId).Distinct(StringComparer.Ordinal).Count() == catalog.entries.Count, "The objective catalog has missing or duplicate identities.");
        foreach (Story entry in Stories)
        {
            NPCProgressionManager npc = npcs.Single(value => value.ProgressionSaveId == entry.npcId);
            Require(npc.AuthoredPhases[entry.phase].contract == null && npc.AuthoredPhases[entry.phase].phaseId != "Resolved", "An objective would duplicate a contract or resolved phase.");
            Require(!entry.route || Target(npc, entry) != null, "An authored objective target is missing: " + entry.title);
        }
    }
    private static void ValidateScene(Scene scene, NPCProgressionManager[] npcs)
    {
        Preflight(scene, npcs); GameObject root = GuidanceRoot(scene); Require(root != null, "Town River objective definitions have not been applied.");
        Require(root.GetComponentsInChildren<TutorialSequence>(true).Length == Stories.Length, "Unexpected objective definitions or custom children under the owned guidance root.");
        foreach (NPCProgressionManager npc in npcs)
        {
            Require(new SerializedObject(npc).FindProperty("onlyShowExclamationWhenInteractionNeeded").boolValue &&
                npc.AuthoredPhases.All(phase => phase.showExclamationMark == (phase.phaseId != "Resolved")), "Interaction-needed attention/idle exclusion differs: " + npc.ProgressionSaveId);
            NPCStoryObjectiveGuidance guidance = npc.GetComponent<NPCStoryObjectiveGuidance>();
            Require(guidance != null && new SerializedObject(guidance).FindProperty("npc").objectReferenceValue == npc, "Story objective owner is missing/stale.");
            Story[] expected = Stories.Where(entry => entry.npcId == npc.ProgressionSaveId).ToArray();
            Require(guidance.entries.Count == expected.Length, "Unexpected objective entries for " + npc.ProgressionSaveId);
            foreach (Story definition in expected)
            {
                string phaseId = npc.AuthoredPhases[definition.phase].phaseId;
                NPCStoryObjectiveGuidance.Entry entry = guidance.entries.Single(value => value.phaseId == phaseId);
                TutorialSequence sequence = entry.objective;
                Require(sequence != null && sequence.transform.parent == root.transform && sequence.showInObjectives && sequence.objectiveTitle == definition.title &&
                    sequence.ObjectiveId == StoryIdentity(npc.ProgressionSaveId, phaseId) && sequence.lessonName == "Objective/" + npc.ProgressionSaveId + "/" + phaseId &&
                    !sequence.playOnStart && !sequence.autoStartNextSequence && sequence.nextSequence == null && !entry.completeAfterDialogue && string.IsNullOrEmpty(entry.completionFeatureId),
                    "Objective checkbox, identity or phase ownership differs: " + definition.title);
                Require(sequence.tutorialSteps != null && sequence.tutorialSteps.Length == 1, "A passive definition must contain one instruction.");
                TutorialStep step = sequence.tutorialSteps[0]; Transform target = Target(npc, definition);
                Require(step.message == definition.instruction && step.screenPosition == TutorialPosition.Left && !step.showNextButton && !step.canSkip &&
                    !step.usePointer && !step.advanceOnClick && step.requiredAction == TutorialStepAction.None && !step.lockLook && !step.lockJump && !step.lockRun &&
                    step.OnStepStart.GetPersistentEventCount() == 0 && step.stepWaypoints != null && step.stepWaypoints.Count == (target == null ? 0 : 1) &&
                    (target == null || step.stepWaypoints[0].target == target && !step.stepWaypoints[0].advancesTutorial), "An objective executes story actions: " + definition.title);
            }
        }
        foreach (Existing entry in ExistingGuides)
        {
            TutorialSequence sequence = Sequence(scene, entry);
            Require(sequence.showInObjectives && sequence.objectiveTitle == entry.title && sequence.ObjectiveId == ExistingIdentity(entry), "Existing follow objective checkbox/title/identity differs.");
            Require(Catalog().entries.Count(value => value.objectiveId == ExistingIdentity(entry) && SameCatalog(value, entry)) == 1, "A follow catalog definition is missing/duplicated.");
        }
    }
    private static Transform Target(NPCProgressionManager npc, Story entry) => !entry.route ? null : entry.follow ? npc.AuthoredPhases[entry.phase].targetLocation : npc.transform;
    private static Dictionary<UnityEngine.Object, string> CaptureProtectedState(Scene scene, NPCProgressionManager[] targets)
    {
        var mutable = new HashSet<TutorialSequence>(ExistingGuides.Select(entry => Sequence(scene, entry)));
        var result = new Dictionary<UnityEngine.Object, string>();
        foreach (Component item in All<Component>(scene))
            if (item != null && !targets.Contains(item as NPCProgressionManager) && !mutable.Contains(item as TutorialSequence)) result[item] = EditorJsonUtility.ToJson(item);
        foreach (ContractSO contract in All<NPCProgressionManager>(scene).SelectMany(npc => npc.AuthoredPhases ?? Array.Empty<NPCProgressionPhase>())
            .Where(phase => phase?.contract != null).Select(phase => phase.contract).Distinct())
            result[contract] = EditorJsonUtility.ToJson(contract);
        return result;
    }
    private static string WithoutAttention(string json) => Regex.Replace(Regex.Replace(json, "\"showExclamationMark\":(?:true|false)", "\"showExclamationMark\":false"),
        "\"onlyShowExclamationWhenInteractionNeeded\":(?:true|false)", "\"onlyShowExclamationWhenInteractionNeeded\":false");
    private static string WithoutObjective(string json) => Regex.Replace(json, "\"(?:showInObjectives|objectiveTitle|objectiveId)\":(?:true|false|\"(?:\\\\.|[^\"])*\")", "\"objective-setting\":null");
    private static TutorialObjectiveCatalog.Entry CatalogEntry(Existing entry) => new TutorialObjectiveCatalog.Entry {
        objectiveId = ExistingIdentity(entry), lessonId = entry.lesson, scenePath = Canyon, title = entry.title, description = entry.title, allowLegacyLessonMatch = true };
    private static bool SameCatalog(TutorialObjectiveCatalog.Entry value, Existing entry) => value.objectiveId == ExistingIdentity(entry) && value.lessonId == entry.lesson &&
        value.scenePath == Canyon && value.title == entry.title && value.description == entry.title && value.allowLegacyLessonMatch;
    private static string StoryIdentity(string npc, string phase) => Canyon + "|story|" + npc + "|" + phase;
    private static string ExistingIdentity(Existing entry) => Canyon + "|" + entry.id.ToString(CultureInfo.InvariantCulture);
    private static TutorialSequence Sequence(Scene scene, Existing entry) => All<TutorialSequence>(scene).Single(sequence => GlobalObjectId.GetGlobalObjectIdSlow(sequence).targetObjectId == entry.id);
    private static NPCProgressionManager NPC(Scene scene, string id) => All<NPCProgressionManager>(scene).Single(npc => npc.ProgressionSaveId == id);
    private static GameObject GuidanceRoot(Scene scene) => scene.GetRootGameObjects().SingleOrDefault(root => root.name == RootName);
    private static TutorialObjectiveCatalog Catalog() => AssetDatabase.LoadAssetAtPath<TutorialObjectiveCatalog>(CatalogPath) ?? throw new InvalidOperationException("Missing the existing objective catalog.");
    private static IEnumerable<T> All<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true));
    private static GameObject NewObject(Scene scene, string name, Transform parent)
    {
        GameObject value = new GameObject(name); Undo.RegisterCreatedObjectUndo(value, "Create passive Town River objective definition"); SceneManager.MoveGameObjectToScene(value, scene);
        if (parent != null) Undo.SetTransformParent(value.transform, parent, "Parent passive Town River objective definition"); return value;
    }
    private static void Mark(Component item) { EditorUtility.SetDirty(item); if (PrefabUtility.IsPartOfPrefabInstance(item)) PrefabUtility.RecordPrefabInstancePropertyModifications(item); }
    private static Scene Loaded()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating && !BuildPipeline.isBuildingPlayer &&
            PrefabStageUtility.GetCurrentPrefabStage() == null, "Stop Play Mode and finish importing in Edit Mode.");
        Scene scene = SceneManager.GetSceneByPath(Canyon); Require(scene.IsValid() && scene.isLoaded, "Open CanyonCrossing; this tool never opens or closes scenes."); return scene;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Report(string value) { Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, value); Debug.Log(value); }
}
#endif
