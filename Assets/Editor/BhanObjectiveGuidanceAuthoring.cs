#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Opts Bhan's existing guidance into Objectives without rebuilding his story.</summary>
[InitializeOnLoad]
public static class BhanObjectiveGuidanceAuthoring
{
    private const string Canyon = "Assets/Scenes/CanyonCrossing.unity";
    private const string House = "Assets/Scenes/BHAN HOUSE.unity";
    private const string Catalog = "Assets/Resources/TutorialObjectiveCatalog.asset";
    private const string Request = "Temp/bhan-objective-guidance.request";
    private const string Report = "Logs/bhan-objective-guidance.txt";
    private static bool running;
    private static double nextPoll;
    private static bool Busy => running || EditorApplication.isCompiling || EditorApplication.isUpdating ||
        EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer ||
        PrefabStageUtility.GetCurrentPrefabStage() != null;

    private readonly struct Entry
    {
        public readonly ulong id;
        public readonly string lesson, title;
        public Entry(ulong id, string lesson, string title) { this.id = id; this.lesson = lesson; this.title = title; }
    }

    private static readonly Entry[] CanyonEntries = {
        new Entry(243981140, "Sequence_Movement", "Learn the controls and visit Bhan"),
        new Entry(1698283800, "Sequence_Bridge", "Meet Professor Bhan"),
        new Entry(847218647, "Go to the supply cart.", "Follow Bhan to the vehicle"),
        new Entry(1085285848, "Sequence_Build1", "Build your first Beam Bridge"),
        new Entry(1149129735, "Sequence_FirstLevelCompletion", "Review your bridge test results"),
        new Entry(2135021272, "FollowProfessorBhan", "Follow Professor Bhan"),
        new Entry(1704001129, "Sequence_Build2", "Build your first Truss Bridge"),
        new Entry(445199625, "ProceedToShopKeeper", "Meet the shopkeeper"),
        new Entry(1635574828, "GoBackToProfBhan", "Return to Professor Bhan"),
        // The existing Find Reed sequence shares a legacy lesson ID with the
        // shopkeeper guide. Resolve its scene object explicitly; never rename
        // either saved lesson ID or accidentally edit the other sequence.
        new Entry(863682474, "ProceedToShopKeeper", "Find Reed")
    };
    private static readonly Entry[] HouseEntries = {
        new Entry(2010245065, "Sequence_House", "Meet Bhan and find the Almanac"),
        new Entry(1946445662, "Sequence_Alamnac", "Review your Almanac")
    };
    private static readonly string[] PhaseIds = {
        "Greetings", "LiveLoad", "WorkBench", "Give Contract Build Location 1", "DeadLoad", "Beam",
        "Build Tutorial 2", "Follow", "City", "Shopkeeper", "Go to Shopkeeper", "CollectMap", "Farewell",
        "Find Reed", "Retired", "Despawned"
    };
    private static readonly HashSet<int> TalkPhases = new HashSet<int> { 0, 2, 3, 4, 5, 6, 7, 8, 9, 11, 12 };

    static BhanObjectiveGuidanceAuthoring() { EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (Busy || EditorApplication.timeSinceStartup < nextPoll || !File.Exists(Request)) return;
        nextPoll = EditorApplication.timeSinceStartup + 1;
        string command = File.ReadAllText(Request).Trim();
        File.Delete(Request);
        if (command == "apply") Apply();
        else if (command == "verify") Verify();
        else Write("FAILED\nUnknown Bhan objective guidance request: " + command);
    }

    [MenuItem("Tools/Civil Craft/Story/Bhan Objectives/1. Enable Through Find Reed")]
    public static void Apply() { Run(false); }
    [MenuItem("Tools/Civil Craft/Story/Bhan Objectives/2. Verify (Read Only)")]
    public static void Verify() { Run(true); }

    private static void Run(bool verify)
    {
        if (Busy) return;
        running = true;
        int undo = -1;
        try
        {
            Scene canyon = SceneManager.GetSceneByPath(Canyon);
            Require(canyon.IsValid() && canyon.isLoaded, "Open CanyonCrossing in Edit Mode. No scenes were opened or saved.");
            Scene house = SceneManager.GetSceneByPath(House);
            bool houseLoaded = house.IsValid() && house.isLoaded;
            bool canyonDirty = canyon.isDirty, houseDirty = houseLoaded && house.isDirty;
            TutorialSequence[] canyonSequences = Components<TutorialSequence>(canyon);
            TutorialSequence[] houseSequences = houseLoaded ? Components<TutorialSequence>(house) : null;
            foreach (Entry entry in CanyonEntries) RequireSequence(canyonSequences, entry);
            foreach (Entry entry in HouseEntries)
                if (houseLoaded) RequireSequence(houseSequences, entry);
                else ClosedHouseText(File.ReadAllText(House), entry, false);
            NPCProgressionManager[] npcs = Components<NPCProgressionManager>(canyon);
            NPCProgressionManager bhan = npcs.Single(npc => npc.ProgressionSaveId == "MainContractNPC");
            NPCProgressionManager reed = npcs.Single(npc => npc.ProgressionSaveId == "ReedNPC");
            Require(bhan.AuthoredPhases.Select(phase => phase.phaseId).SequenceEqual(PhaseIds), "Bhan's phase order differs. No phases were substituted.");
            Require(reed.PhaseCount > 0 && reed.AuthoredPhases[0].phaseId == "MeetingReed", "Reed's initial meeting phase differs.");
            LessonTrigger vehicle = Components<LessonTrigger>(canyon).Single(value =>
                GlobalObjectId.GetGlobalObjectIdSlow(value).targetObjectId == 993037869);
            var vehicleData = new SerializedObject(vehicle);
            Require(vehicleData.FindProperty("advanceNPC").objectReferenceValue == bhan &&
                vehicleData.FindProperty("requiredNPCPhaseIndex").intValue == 1, "The Live Load inspection no longer belongs to Bhan's LiveLoad phase.");

            var report = new StringBuilder();
            string backup = null;
            if (!verify)
            {
                backup = "Temp/BhanObjectiveGuidance/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                Directory.CreateDirectory(backup);
                File.Copy(Canyon, backup + "/CanyonCrossing.unity");
                File.Copy(House, backup + "/BHAN HOUSE.unity");
                Undo.IncrementCurrentGroup(); undo = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Enable Bhan objectives and conversation attention");
                foreach (Entry entry in CanyonEntries) Configure(RequireSequence(canyonSequences, entry), entry);
                if (houseLoaded)
                    foreach (Entry entry in HouseEntries) Configure(RequireSequence(houseSequences, entry), entry);
                ConfigureMarkers(bhan, reed);
                ConfigureReview(bhan.gameObject, "Review your first bridge record");
                ConfigureReview(vehicle.gameObject, "Review the Live Load lesson");
            }

            foreach (Entry entry in CanyonEntries) Validate(RequireSequence(canyonSequences, entry), entry, report);
            foreach (Entry entry in HouseEntries)
                if (houseLoaded) Validate(RequireSequence(houseSequences, entry), entry, report);
            Require(new SerializedObject(bhan).FindProperty("onlyShowExclamationWhenInteractionNeeded").boolValue,
                "Bhan's interaction-needed attention policy is off.");
            for (int index = 0; index < PhaseIds.Length; index++)
                Require(bhan.AuthoredPhases[index].showExclamationMark == TalkPhases.Contains(index), "Unexpected Bhan attention flag: " + PhaseIds[index]);
            Require(reed.AuthoredPhases[0].showExclamationMark, "Reed's initial meeting has no attention flag.");
            ValidateReview(bhan.gameObject, "Review your first bridge record");
            ValidateReview(vehicle.gameObject, "Review the Live Load lesson");
            ConfigureCatalog(verify, report);
            report.AppendLine("ATTENTION: Bhan conversation/offer/turn-in phases enabled; inspection, shopkeeper errand, Find Reed and terminal phases off. Reed's initial meeting enabled on this scene instance only.");
            report.AppendLine("RUNTIME OBJECTIVES: Live Load lesson review and first completed bridge record review enabled.");
            if (!verify)
            {
                // Closed House needs only the objective scalar settings. Preserve every
                // other byte rather than opening/reserializing the whole scene.
                if (!houseLoaded)
                {
                    string original = File.ReadAllText(House), updated = original;
                    foreach (Entry entry in HouseEntries) updated = ClosedHouseText(updated, entry, true);
                    if (updated != original) File.WriteAllText(House, updated, new UTF8Encoding(false));
                    report.AppendLine("SAVED: closed Bhan House; only the two tutorials' objective settings changed.");
                }
                SaveIfClean(canyon, canyonDirty, report);
                if (houseLoaded) SaveIfClean(house, houseDirty, report);
                Undo.CollapseUndoOperations(undo); undo = -1;
                report.AppendLine("BACKUP: " + backup);
            }
            if (!houseLoaded)
                foreach (Entry entry in HouseEntries)
                {
                    ClosedHouseText(File.ReadAllText(House), entry, false, true);
                    report.AppendLine("OBJECTIVE: Bhan House / " + entry.title);
                }
            report.AppendLine("PRESERVED: tutorial steps, routes, lesson IDs, callbacks, phase order, contracts, rewards and player saves. No other scenes were opened.");
            Write("PASS\n" + report);
        }
        catch (Exception error)
        {
            if (undo >= 0) Undo.RevertAllDownToGroup(undo);
            Write("FAILED\n" + error);
        }
        finally { running = false; }
    }

    private static T[] Components<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
        .SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
    private static TutorialSequence RequireSequence(TutorialSequence[] sequences, Entry entry)
    {
        TutorialSequence sequence = sequences.Single(value => GlobalObjectId.GetGlobalObjectIdSlow(value).targetObjectId == entry.id);
        Require(sequence.lessonName == entry.lesson && sequence.tutorialSteps != null && sequence.tutorialSteps.Length > 0,
            "The audited tutorial differs: " + entry.title);
        return sequence;
    }
    private static void Configure(TutorialSequence sequence, Entry entry)
    {
        string[] steps = sequence.tutorialSteps.Select(JsonUtility.ToJson).ToArray();
        Undo.RecordObject(sequence, "Enable tutorial objective");
        sequence.showInObjectives = true; sequence.objectiveTitle = entry.title;
        sequence.ConfigureObjectiveIdentity(ObjectiveId(sequence.gameObject.scene.path, entry));
        Require(sequence.tutorialSteps.Select(JsonUtility.ToJson).SequenceEqual(steps), "Tutorial steps changed: " + entry.title);
        Mark(sequence);
    }
    private static void Validate(TutorialSequence sequence, Entry entry, StringBuilder report)
    {
        Require(sequence.showInObjectives && sequence.objectiveTitle == entry.title &&
            sequence.ObjectiveId == ObjectiveId(sequence.gameObject.scene.path, entry), "Objective checkbox/title/identity missing: " + entry.title);
        report.AppendLine("OBJECTIVE: " + sequence.gameObject.scene.name + " / " + entry.title);
    }
    private static void ConfigureMarkers(NPCProgressionManager bhan, NPCProgressionManager reed)
    {
        string[] before = bhan.AuthoredPhases.Select(phase => JsonUtility.ToJson(phase)).ToArray();
        string[] reedBefore = reed.AuthoredPhases.Select(phase => JsonUtility.ToJson(phase)).ToArray();
        Undo.RecordObject(bhan, "Enable Bhan conversation attention");
        var serialized = new SerializedObject(bhan);
        serialized.FindProperty("onlyShowExclamationWhenInteractionNeeded").boolValue = true;
        serialized.ApplyModifiedProperties();
        for (int index = 0; index < PhaseIds.Length; index++) bhan.AuthoredPhases[index].showExclamationMark = TalkPhases.Contains(index);
        Undo.RecordObject(reed, "Enable Reed's initial meeting attention");
        reed.AuthoredPhases[0].showExclamationMark = true;
        for (int index = 0; index < PhaseIds.Length; index++)
            Require(WithoutAttention(before[index]) == WithoutAttention(JsonUtility.ToJson(bhan.AuthoredPhases[index])), "Bhan phase changed outside attention: " + PhaseIds[index]);
        for (int index = 0; index < reedBefore.Length; index++)
            Require(WithoutAttention(reedBefore[index]) == WithoutAttention(JsonUtility.ToJson(reed.AuthoredPhases[index])), "Reed's story changed.");
        Mark(bhan); Mark(reed);
    }
    private static string WithoutAttention(string json) => Regex.Replace(json, "\"showExclamationMark\":(?:true|false)", "\"showExclamationMark\":false");
    private static void ConfigureReview(GameObject owner, string title)
    {
        NPCOrderedLearningFlow flow = owner.GetComponent<NPCOrderedLearningFlow>();
        if (flow == null) flow = Undo.AddComponent<NPCOrderedLearningFlow>(owner);
        Undo.RecordObject(flow, "Enable review objective");
        flow.showReviewsInObjectives = true; flow.reviewObjectiveTitle = title;
        Mark(flow);
    }
    private static void ValidateReview(GameObject owner, string title)
    {
        NPCOrderedLearningFlow flow = owner.GetComponent<NPCOrderedLearningFlow>();
        Require(flow != null && flow.showReviewsInObjectives && flow.reviewObjectiveTitle == title, "Review objective missing: " + title);
    }
    private static void Mark(Component value)
    {
        EditorUtility.SetDirty(value);
        if (PrefabUtility.IsPartOfPrefabInstance(value)) PrefabUtility.RecordPrefabInstancePropertyModifications(value);
        EditorSceneManager.MarkSceneDirty(value.gameObject.scene);
    }
    private static void SaveIfClean(Scene scene, bool initiallyDirty, StringBuilder report)
    {
        if (initiallyDirty) report.AppendLine("NEEDS SAVE: " + scene.path + "; existing unsaved edits preserved.");
        else { Require(EditorSceneManager.SaveScene(scene), "Could not save " + scene.path); report.AppendLine("SAVED: " + scene.path); }
    }
    private static string ObjectiveId(string scenePath, Entry entry) => scenePath + "|" +
        entry.id.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static List<TutorialObjectiveCatalog.Entry> ExpectedCatalogEntries()
    {
        var result = new List<TutorialObjectiveCatalog.Entry>();
        foreach (Entry entry in CanyonEntries) result.Add(CatalogEntry(Canyon, entry));
        foreach (Entry entry in HouseEntries) result.Add(CatalogEntry(House, entry));
        result.Add(ReviewCatalogEntry("StoryReview/MainContractNPC/LiveLoad/inspection-review", "Review the Live Load lesson",
            "Open your <b>Almanac</b> and review the Live Load lesson. After reading, close the book to continue."));
        result.Add(ReviewCatalogEntry("StoryReview/MainContractNPC/DeadLoad/training-bridge-record-review", "Review your first bridge record",
            "Open your <b>Almanac</b> and review your first bridge entry. After reading, close the book to continue."));
        return result;
    }

    private static TutorialObjectiveCatalog.Entry CatalogEntry(string scenePath, Entry entry) => new TutorialObjectiveCatalog.Entry {
        objectiveId = ObjectiveId(scenePath, entry), lessonId = entry.lesson, scenePath = scenePath,
        title = entry.title, description = entry.title,
        // Both released sequences share this lesson key. A completion record
        // alone cannot prove which one ran; do not invent historical objectives.
        allowLegacyLessonMatch = entry.lesson != "ProceedToShopKeeper"
    };

    private static TutorialObjectiveCatalog.Entry ReviewCatalogEntry(string lesson, string title, string description) =>
        new TutorialObjectiveCatalog.Entry {
            objectiveId = Canyon + "|review|" + lesson, lessonId = lesson, scenePath = Canyon,
            title = title, description = description, allowLegacyLessonMatch = true
        };

    private static void ConfigureCatalog(bool verify, StringBuilder report)
    {
        List<TutorialObjectiveCatalog.Entry> expected = ExpectedCatalogEntries();
        Require(expected.Select(entry => entry.objectiveId).Distinct(StringComparer.Ordinal).Count() == expected.Count,
            "The audited tutorial objective catalog contains duplicate identities.");
        TutorialObjectiveCatalog catalog = AssetDatabase.LoadAssetAtPath<TutorialObjectiveCatalog>(Catalog);
        Require(catalog != null || AssetDatabase.LoadMainAssetAtPath(Catalog) == null,
            "Another asset occupies the tutorial objective catalog path.");
        if (verify) Require(catalog != null, "Run Enable Through Find Reed to create the tutorial objective catalog.");
        if (catalog != null)
            Require(catalog.entries != null && catalog.entries.All(entry => entry != null && !string.IsNullOrWhiteSpace(entry.objectiveId)) &&
                catalog.entries.Select(entry => entry.objectiveId).Distinct(StringComparer.Ordinal).Count() == catalog.entries.Count,
                "The tutorial objective catalog contains missing or duplicate identities; no entries were substituted.");
        bool needsUpdate = catalog == null || expected.Any(entry =>
            !SameCatalogEntry(catalog.entries.FirstOrDefault(value => value.objectiveId == entry.objectiveId), entry));
        if (verify) Require(!needsUpdate, "The tutorial objective catalog does not match the audited Bhan guidance definitions.");
        else if (needsUpdate)
        {
            Require(catalog == null || !EditorUtility.IsDirty(catalog),
                "Tutorial objective catalog has unsaved edits. Save that asset before running setup; no asset edits were overwritten.");
            bool created = catalog == null;
            if (created) catalog = ScriptableObject.CreateInstance<TutorialObjectiveCatalog>();
            foreach (TutorialObjectiveCatalog.Entry entry in expected)
            {
                int index = catalog.entries.FindIndex(value => value.objectiveId == entry.objectiveId);
                if (index < 0) catalog.entries.Add(entry);
                else catalog.entries[index] = entry;
            }
            if (created) AssetDatabase.CreateAsset(catalog, Catalog);
            EditorUtility.SetDirty(catalog);
            // Save only this generated catalog. Other unsaved project assets
            // and the user's current scene state are deliberately untouched.
            AssetDatabase.SaveAssetIfDirty(catalog);
        }
        report.AppendLine("CATALOG: 12 authored tutorials and 2 Almanac reviews; shared shopkeeper/Find Reed legacy lesson matches disabled.");
    }

    private static bool SameCatalogEntry(TutorialObjectiveCatalog.Entry actual, TutorialObjectiveCatalog.Entry expected) =>
        actual != null && actual.objectiveId == expected.objectiveId && actual.lessonId == expected.lessonId &&
        actual.scenePath == expected.scenePath && actual.title == expected.title && actual.description == expected.description &&
        actual.allowLegacyLessonMatch == expected.allowLegacyLessonMatch;

    private static string ClosedHouseText(string text, Entry entry, bool apply, bool verify = false)
    {
        MatchCollection matches = Regex.Matches(text, @"(?ms)^--- !u!114 &" + entry.id + @"\r?\n.*?(?=^--- !u!|\z)");
        Require(matches.Count == 1 && Regex.IsMatch(matches[0].Value, @"(?m)^  lessonName: " + Regex.Escape(entry.lesson) + @"\r?$"), "Bhan House tutorial differs: " + entry.title);
        Match match = matches[0];
        string doc = match.Value;
        if (verify) Require(Regex.IsMatch(doc, @"(?m)^  showInObjectives: 1\r?$") &&
            Regex.IsMatch(doc, @"(?m)^  objectiveTitle: " + Regex.Escape(entry.title) + @"\r?$") &&
            Regex.IsMatch(doc, @"(?m)^  objectiveId: " + Regex.Escape(ObjectiveId(House, entry)) + @"\r?$"),
            "Bhan House objective checkbox/title/identity missing: " + entry.title);
        if (!apply) return text;
        string newline = doc.Contains("\r\n") ? "\r\n" : "\n";
        string settings = "  showInObjectives: 1" + newline + "  objectiveTitle: " + entry.title + newline +
            "  objectiveId: " + ObjectiveId(House, entry) + newline;
        doc = Regex.Replace(doc, @"(?m)^  (?:showInObjectives|objectiveTitle|objectiveId):[^\r\n]*\r?\n", "");
        doc = Regex.Replace(doc, @"(?m)^  playOnStart:", settings + "  playOnStart:");
        return text.Substring(0, match.Index) + doc + text.Substring(match.Index + match.Length);
    }
    private static void Write(string report) { Directory.CreateDirectory("Logs"); File.WriteAllText(Report, report); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
#endif
