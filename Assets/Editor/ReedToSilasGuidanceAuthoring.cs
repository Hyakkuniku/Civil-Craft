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

/// <summary>Opts the existing Reed-to-Silas story into saved objectives and interaction-needed attention.</summary>
[InitializeOnLoad]
public static class ReedToSilasGuidanceAuthoring
{
    public const string RequestPath = "Temp/reed-to-silas-guidance.request";
    public const string ReportPath = "Logs/reed-to-silas-guidance.txt";
    private const string Canyon = "Assets/Scenes/CanyonCrossing.unity";
    private const string CatalogPath = "Assets/Resources/TutorialObjectiveCatalog.asset";
    private const string RootName = "Reed to Silas Objective Guidance";
    private static double nextPoll;

    private sealed class Story
    {
        public readonly string npcId;
        public readonly int phase;
        public readonly string title, instruction;
        public readonly bool route, terminal;
        public Story(string npcId, int phase, string title, string instruction, bool route = true, bool terminal = false)
        { this.npcId = npcId; this.phase = phase; this.title = title; this.instruction = instruction; this.route = route; this.terminal = terminal; }
    }
    private sealed class Existing
    {
        public readonly ulong id;
        public readonly string lesson, title;
        public Existing(ulong id, string lesson, string title) { this.id = id; this.lesson = lesson; this.title = title; }
    }
    private static readonly string[] NpcIds = { "ReedNPC", "VanceNPC", "SilasNPC" };
    private static readonly string[][] PhaseIds = {
        new[] { "MeetingReed", "ExplainSituation", "Reed Contract Give", "ReedFareWell", "SideContract" },
        new[] { "MeetingVance", "The Supply Route", "The Supply Route Part Two", "Cargo and Live-Load Introduction", "Project Briefing", "GoodByeForNow", "SideQuestStart" },
        new[] { "Meeting Silas", "The Final Supply Gap ", "AfterCinematic", "Project Briefing", "SilasLoadTransfer", "ExplainSituation2", "ExplainSituationContinuation", "MATERIAL INTRODUCTION", "ProjectBriefing2", "ProjectBriefing3", "Multiplayer" }
    };
    private static readonly Story[] Stories = {
        new Story("ReedNPC", 0, "Meet Reed", "Talk to Reed about the factory crossing."),
        new Story("ReedNPC", 1, "Follow Reed and learn about Bending", "Follow Reed to the factory crossing. Talk to him and read the Bending lesson."),
        new Story("ReedNPC", 3, "Review Bending and complete the quiz", "Review Bending in the Almanac, open the Quiz tab, and submit the quiz. Any score lets you continue.", false),
        new Story("VanceNPC", 0, "Meet Vance", "Talk to Vance about the supply route."),
        new Story("VanceNPC", 1, "Follow Vance", "Follow Vance to the next crossing, then talk to him about the route."),
        new Story("VanceNPC", 2, "Learn Tension & Compression", "Listen to Vance and read the Tension & Compression lesson before preparing the cargo."),
        new Story("VanceNPC", 5, "Review Tension & Compression and complete the quiz", "Review Tension & Compression in the Almanac, then submit the quiz and talk to Vance about Silas.", false),
        new Story("SilasNPC", 0, "Meet Silas", "Talk to Silas about connecting the community."),
        new Story("SilasNPC", 1, "Follow Silas", "Follow Silas to the community crossing, then talk to him."),
        new Story("SilasNPC", 2, "Learn Load Distribution", "Listen to Silas and read the Load Distribution lesson."),
        new Story("SilasNPC", 4, "Review Load Distribution and complete the quiz", "Review Load Distribution in the Almanac, submit the quiz, and hear about the eastern passage.", false),
        new Story("SilasNPC", 5, "Follow Silas to the eastern passage", "Follow Silas to the eastern passage, then talk to him about the site."),
        new Story("SilasNPC", 6, "Inspect the eastern passage", "Talk to Silas about the existing towers and the road toward Town River."),
        new Story("SilasNPC", 7, "Learn Load Transfer", "Collect the Rope introduction and read the Load Transfer lesson with Silas."),
        new Story("SilasNPC", 9, "Review Load Transfer and complete the quiz", "Review Load Transfer in the Almanac, submit the quiz, and finish Silas's finale conversation.", false),
        new Story("SilasNPC", 10, "Unlock Multiplayer", "Talk to Silas to learn how to build with a friend and unlock Multiplayer.", true, true)
    };
    private static readonly Existing[] ExistingGuides = {
        new Existing(1143740769, "LoadCargoVance", "Load all four cargo items"),
        new Existing(1487862495, "GobacktoVance ", "Return to Vance"),
        new Existing(22004694, "EraseReedSideQuest", "Inspect Reed's bridge"),
        new Existing(669876432, "MoveVanceSideQuest", "Realign Vance's bridge")
    };

    static ReedToSilasGuidanceAuthoring() { EditorApplication.update += Poll; }
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
            else throw new InvalidOperationException("Unknown Reed-to-Silas guidance command: " + command);
        }
        catch (Exception error) { Report("FAILED\n" + error); Debug.LogException(error); }
    }

    [MenuItem("Tools/Civil Craft/Story/Reed to Silas Objectives/1. Apply (Do Not Save Scene)")]
    public static void Apply()
    {
        Scene scene = Loaded(); NPCProgressionManager[] npcs = NpcIds.Select(id => NPC(scene, id)).ToArray(); Preflight(scene, npcs);
        if (GuidanceRoot(scene) != null) { Validate(); return; }
        Require(npcs.All(npc => npc.GetComponent<NPCStoryObjectiveGuidance>() == null), "A target NPC already has custom story objective guidance. It was not overwritten.");
        TutorialObjectiveCatalog catalog = Catalog();
        Require(!EditorUtility.IsDirty(catalog), "The objective catalog has unsaved edits. Save that asset first; nothing was applied.");
        var protectedState = CaptureProtectedState(scene, npcs);
        var npcState = npcs.ToDictionary(npc => npc, npc => WithoutAttention(EditorJsonUtility.ToJson(npc)));
        var guideState = ExistingGuides.ToDictionary(entry => entry.id, entry => WithoutObjective(EditorJsonUtility.ToJson(Sequence(scene, entry))));
        var catalogState = catalog.entries.ToDictionary(entry => entry.objectiveId, JsonUtility.ToJson, StringComparer.Ordinal);
        PlayerData player = PlayerDataManager.Instance != null ? PlayerDataManager.Instance.CurrentData : null;
        string playerBefore = player != null ? JsonUtility.ToJson(player) : null;
        string backup = "Temp/ReedToSilasGuidance/" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        Directory.CreateDirectory(backup); File.Copy(Canyon, backup + "/CanyonCrossing.unity"); File.Copy(CatalogPath, backup + "/TutorialObjectiveCatalog.asset");
        bool initiallyDirty = scene.isDirty;
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Enable Reed to Silas objectives and attention");
        try
        {
            GameObject root = NewObject(scene, RootName, null);
            foreach (NPCProgressionManager npc in npcs)
            {
                Undo.RecordObject(npc, "Enable interaction-needed NPC attention");
                var data = new SerializedObject(npc); data.FindProperty("onlyShowExclamationWhenInteractionNeeded").boolValue = true; data.ApplyModifiedProperties();
                foreach (NPCProgressionPhase phase in npc.AuthoredPhases) phase.showExclamationMark = true;
                Mark(npc);
                NPCStoryObjectiveGuidance guidance = Undo.AddComponent<NPCStoryObjectiveGuidance>(npc.gameObject);
                var owner = new SerializedObject(guidance); owner.FindProperty("npc").objectReferenceValue = npc; owner.ApplyModifiedProperties();
                foreach (Story definition in Stories.Where(entry => entry.npcId == npc.ProgressionSaveId))
                {
                    NPCProgressionPhase phase = npc.AuthoredPhases[definition.phase];
                    GameObject child = NewObject(scene, npc.ProgressionSaveId + " - " + definition.title, root.transform);
                    TutorialSequence objective = Undo.AddComponent<TutorialSequence>(child);
                    objective.lessonName = "Objective/" + npc.ProgressionSaveId + "/" + phase.phaseId;
                    objective.showInObjectives = true; objective.objectiveTitle = definition.title;
                    objective.ConfigureObjectiveIdentity(StoryIdentity(npc.ProgressionSaveId, phase.phaseId));
                    objective.requiredPreviousLesson = string.Empty; objective.playOnStart = false; objective.nextSequence = null; objective.autoStartNextSequence = false;
                    Transform target = Target(npc, definition);
                    objective.tutorialSteps = new[] { new TutorialStep { message = definition.instruction, screenPosition = TutorialPosition.Left,
                        showNextButton = false, canSkip = false, usePointer = false, advanceOnClick = false,
                        stepWaypoints = target != null ? new List<GuiderWaypoint> { new GuiderWaypoint { target = target, advancesTutorial = false } } : new List<GuiderWaypoint>() } };
                    guidance.entries.Add(new NPCStoryObjectiveGuidance.Entry { phaseId = phase.phaseId, objective = objective, completeAfterDialogue = definition.terminal,
                        completionFeatureId = definition.terminal ? "multiplayer" : string.Empty });
                    Mark(objective);
                }
                Mark(guidance);
            }
            foreach (Existing entry in ExistingGuides)
            {
                TutorialSequence sequence = Sequence(scene, entry); Undo.RecordObject(sequence, "Enable existing tutorial objective checkbox");
                sequence.showInObjectives = true; sequence.objectiveTitle = entry.title; sequence.ConfigureObjectiveIdentity(ExistingIdentity(entry)); Mark(sequence);
            }
            Undo.RecordObject(catalog, "Merge the four existing guide definitions");
            foreach (Existing entry in ExistingGuides)
            {
                string identity = ExistingIdentity(entry);
                TutorialObjectiveCatalog.Entry existing = catalog.entries.SingleOrDefault(value => value.objectiveId == identity);
                if (existing == null) catalog.entries.Add(CatalogEntry(entry));
                else Require(SameCatalog(existing, entry), "An existing catalog definition conflicts with this guide: " + entry.title);
            }
            ValidateScene(scene, npcs);
            foreach (var pair in npcState) Require(WithoutAttention(EditorJsonUtility.ToJson(pair.Key)) == pair.Value, "An NPC changed outside attention settings. Update rolled back.");
            foreach (Existing entry in ExistingGuides) Require(WithoutObjective(EditorJsonUtility.ToJson(Sequence(scene, entry))) == guideState[entry.id], "An existing tutorial changed outside objective settings. Update rolled back.");
            foreach (var pair in catalogState) Require(catalog.entries.Any(entry => entry.objectiveId == pair.Key && JsonUtility.ToJson(entry) == pair.Value), "An existing catalog entry changed. Update rolled back.");
            foreach (var pair in protectedState) Require(pair.Key != null && EditorJsonUtility.ToJson(pair.Key) == pair.Value, "An unrelated story object, contract, tutorial, camera, cargo or transform changed. Update rolled back.");
            Require(player == null || (PlayerDataManager.Instance != null && ReferenceEquals(player, PlayerDataManager.Instance.CurrentData) && JsonUtility.ToJson(player) == playerBefore), "Editor authoring changed live player data. Update rolled back.");
            EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssetIfDirty(catalog); EditorSceneManager.MarkSceneDirty(scene); Undo.CollapseUndoOperations(group);
            Report("PASS: Reed, Vance and Silas use interaction-needed attention on their existing phases.\n" +
                "PASS: 16 checked objective definitions cover conversations, follow-ups, lessons, quizzes and Multiplayer; contract objectives are not duplicated.\n" +
                "PASS: four existing cargo, return and side-task tutorial checkboxes enabled with stable objective IDs; original steps and lesson IDs preserved.\n" +
                "PASS: Bhan catalog entries, story phase order/callbacks, contracts, rewards, cargo loading, geometry and player data preserved.\n" +
                "BACKUP: " + backup + "\nSCENE_WAS_DIRTY: " + initiallyDirty + "\nNEEDS_SAVE: CanyonCrossing. No scene was saved.");
        }
        catch { Undo.RevertAllDownToGroup(group); throw; }
    }

    [MenuItem("Tools/Civil Craft/Story/Reed to Silas Objectives/2. Validate (Read Only)")]
    public static void Validate()
    {
        Scene scene = Loaded(); ValidateScene(scene, NpcIds.Select(id => NPC(scene, id)).ToArray());
        Report("PASS: Reed-to-Silas objectives, existing checked tutorials, catalog and interaction-needed attention are wired.\nNEEDS_SAVE: " + (scene.isDirty ? "CanyonCrossing" : "none"));
    }
    [MenuItem("Tools/Civil Craft/Story/Reed to Silas Objectives/3. Inspect (Read Only)")]
    public static void Inspect()
    {
        Scene scene = Loaded(); Preflight(scene, NpcIds.Select(id => NPC(scene, id)).ToArray());
        Report("PASS: existing Reed, Vance and Silas phase IDs and four tutorial identities verified.\nSCENE_DIRTY: " + scene.isDirty + "\nAlready applied: " + (GuidanceRoot(scene) != null));
    }
    private static void Preflight(Scene scene, NPCProgressionManager[] npcs)
    {
        for (int i = 0; i < npcs.Length; i++) Require(npcs[i].AuthoredPhases.Select(phase => phase.phaseId).SequenceEqual(PhaseIds[i]), "An NPC phase order differs: " + NpcIds[i]);
        foreach (Existing entry in ExistingGuides)
        {
            TutorialSequence sequence = Sequence(scene, entry);
            Require(sequence.lessonName == entry.lesson && sequence.tutorialSteps != null && sequence.tutorialSteps.Length > 0, "The audited tutorial identity differs: " + entry.title);
            Require(All<TutorialSequence>(scene).Count(value => value.lessonName == entry.lesson) == 1, "Legacy objective history is ambiguous for: " + entry.lesson);
        }
        TutorialObjectiveCatalog catalog = Catalog();
        Require(catalog.entries != null && catalog.entries.All(entry => entry != null && !string.IsNullOrWhiteSpace(entry.objectiveId)) &&
            catalog.entries.Select(entry => entry.objectiveId).Distinct(StringComparer.Ordinal).Count() == catalog.entries.Count, "The existing objective catalog contains missing/duplicate identities.");
        foreach (Story entry in Stories)
        {
            NPCProgressionManager npc = npcs.Single(value => value.ProgressionSaveId == entry.npcId);
            Require(npc.AuthoredPhases[entry.phase].contract == null, "An objective would duplicate a contract phase.");
            Require(!entry.route || Target(npc, entry) != null, "An authored objective route target is missing: " + entry.title);
        }
    }
    private static void ValidateScene(Scene scene, NPCProgressionManager[] npcs)
    {
        Preflight(scene, npcs); GameObject root = GuidanceRoot(scene); Require(root != null, "Reed-to-Silas objective definitions have not been applied.");
        Require(root.GetComponentsInChildren<TutorialSequence>(true).Length == Stories.Length, "Unexpected objective definition count or custom children under the guidance root.");
        foreach (NPCProgressionManager npc in npcs)
        {
            Require(new SerializedObject(npc).FindProperty("onlyShowExclamationWhenInteractionNeeded").boolValue && npc.AuthoredPhases.All(phase => phase.showExclamationMark), "Interaction-needed attention is missing: " + npc.ProgressionSaveId);
            NPCStoryObjectiveGuidance guidance = npc.GetComponent<NPCStoryObjectiveGuidance>();
            Require(guidance != null && new SerializedObject(guidance).FindProperty("npc").objectReferenceValue == npc, "Story objective owner is missing/stale.");
            Story[] expected = Stories.Where(entry => entry.npcId == npc.ProgressionSaveId).ToArray();
            Require(guidance.entries.Count == expected.Length, "Unexpected story objective entries for " + npc.ProgressionSaveId);
            foreach (Story definition in expected)
            {
                string phaseId = npc.AuthoredPhases[definition.phase].phaseId;
                NPCStoryObjectiveGuidance.Entry entry = guidance.entries.Single(value => value.phaseId == phaseId);
                TutorialSequence sequence = entry.objective;
                Require(sequence != null && sequence.transform.parent == root.transform && sequence.showInObjectives && sequence.objectiveTitle == definition.title &&
                    sequence.ObjectiveId == StoryIdentity(npc.ProgressionSaveId, phaseId) && sequence.lessonName == "Objective/" + npc.ProgressionSaveId + "/" + phaseId &&
                    !sequence.playOnStart && !sequence.autoStartNextSequence && sequence.nextSequence == null && entry.completeAfterDialogue == definition.terminal &&
                    entry.completionFeatureId == (definition.terminal ? "multiplayer" : string.Empty),
                    "Objective checkbox, stable identity or phase ownership differs: " + definition.title);
                Require(sequence.tutorialSteps != null && sequence.tutorialSteps.Length == 1, "An objective definition must contain one passive instruction.");
                TutorialStep step = sequence.tutorialSteps[0]; Transform target = Target(npc, definition);
                Require(step.message == definition.instruction && step.screenPosition == TutorialPosition.Left && !step.showNextButton && !step.canSkip &&
                    !step.usePointer && !step.advanceOnClick && step.requiredAction == TutorialStepAction.None && !step.lockLook && !step.lockJump && !step.lockRun &&
                    step.OnStepStart.GetPersistentEventCount() == 0 && step.stepWaypoints != null && step.stepWaypoints.Count == (target == null ? 0 : 1) &&
                    (target == null || step.stepWaypoints[0].target == target && !step.stepWaypoints[0].advancesTutorial), "Objective must not execute story actions: " + definition.title);
            }
        }
        foreach (Existing entry in ExistingGuides)
        {
            TutorialSequence sequence = Sequence(scene, entry);
            Require(sequence.showInObjectives && sequence.objectiveTitle == entry.title && sequence.ObjectiveId == ExistingIdentity(entry), "Existing objective checkbox/title/identity differs: " + entry.title);
            Require(Catalog().entries.Count(value => value.objectiveId == ExistingIdentity(entry) && SameCatalog(value, entry)) == 1, "Existing guide catalog entry is missing/duplicated: " + entry.title);
        }
    }
    private static Transform Target(NPCProgressionManager npc, Story entry) => !entry.route ? null :
        ((entry.npcId == "ReedNPC" && entry.phase == 1) || (entry.npcId == "VanceNPC" && entry.phase == 1) ||
         (entry.npcId == "SilasNPC" && (entry.phase == 1 || entry.phase == 5))) ? npc.AuthoredPhases[entry.phase].targetLocation : npc.transform;
    private static Dictionary<UnityEngine.Object, string> CaptureProtectedState(Scene scene, NPCProgressionManager[] targets)
    {
        var result = new Dictionary<UnityEngine.Object, string>(); var mutable = new HashSet<TutorialSequence>(ExistingGuides.Select(entry => Sequence(scene, entry)));
        IEnumerable<Component> items = All<NPCProgressionManager>(scene).Cast<Component>().Concat(All<NPCSpawnerCondition>(scene)).Concat(All<BuildLocation>(scene))
            .Concat(All<CinematicDirector>(scene)).Concat(All<Camera>(scene)).Concat(All<CargoItem>(scene)).Concat(All<VehicleCargoSlot>(scene))
            .Concat(All<LiveLoadVehicle>(scene)).Concat(All<TutorialSequence>(scene)).Concat(All<Transform>(scene));
        foreach (Component item in items) if (!targets.Contains(item as NPCProgressionManager) && !mutable.Contains(item as TutorialSequence) && !result.ContainsKey(item)) result[item] = EditorJsonUtility.ToJson(item);
        foreach (ContractSO contract in targets.SelectMany(npc => npc.AuthoredPhases).Where(phase => phase.contract != null).Select(phase => phase.contract).Distinct()) result[contract] = EditorJsonUtility.ToJson(contract);
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
    private static TutorialObjectiveCatalog Catalog() => AssetDatabase.LoadAssetAtPath<TutorialObjectiveCatalog>(CatalogPath) ?? throw new InvalidOperationException("Missing existing tutorial objective catalog.");
    private static IEnumerable<T> All<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true));
    private static GameObject NewObject(Scene scene, string name, Transform parent)
    {
        GameObject value = new GameObject(name); Undo.RegisterCreatedObjectUndo(value, "Create passive story objective definition"); SceneManager.MoveGameObjectToScene(value, scene);
        if (parent != null) Undo.SetTransformParent(value.transform, parent, "Parent passive story objective definition"); return value;
    }
    private static void Mark(Component item)
    { EditorUtility.SetDirty(item); if (PrefabUtility.IsPartOfPrefabInstance(item)) PrefabUtility.RecordPrefabInstancePropertyModifications(item); }
    private static Scene Loaded()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating && !BuildPipeline.isBuildingPlayer &&
            PrefabStageUtility.GetCurrentPrefabStage() == null, "Stop Play Mode and finish importing in Edit Mode.");
        Scene scene = SceneManager.GetSceneByPath(Canyon); Require(scene.IsValid() && scene.isLoaded, "Open CanyonCrossing; this tool never opens or closes scenes."); return scene;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Report(string text) { Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, text); Debug.Log(text); }
}
#endif
