#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Explicit, scene-local text/objective update for the opening of NEW Tutorial 1.
/// Never opens/saves scenes, invokes dialogue, or changes player data. The caller
/// owns the user's review/save workflow; no InitializeOnLoad authoring runs here.
/// </summary>
public static class BeamTutorialHouseAuthoring
{
    public const string CanyonPath = "Assets/Scenes/CanyonCrossing.unity";
    public const string HousePath = "Assets/Scenes/BHAN HOUSE.unity";

    private static string Em(string value) => "<b><#E09500>" + value + "</color></b>";

    private static readonly string[] Greeting =
    {
        "Ah, you must be the young " + Em("civil engineer") + " I've been expecting.",
        "You may already have some knowledge about engineering.",
        "But " + Em("knowledge") + " and " + Em("experience") + " are two different things.",
        Em("Arcadia") + " will give you the chance to put what you've learned into practice.",
        "Every " + Em("project") + " you complete will teach you something new.",
        "Before we begin, what should I call you?"
    };

    private static readonly string[] FetchAlmanac =
    {
        Em("{PlayerName}..."),
        "A fitting name for someone beginning an engineering journey.",
        "You'll be working on real bridge projects soon.",
        "But first, there's something important you will need.",
        "There is a book called the " + Em("Almanac") + ".",
        "It will become your " + Em("engineering record") + " throughout your journey.",
        "It won't only record your achievements.",
        "It will also record the " + Em("lessons") + ", " + Em("discoveries") + ", and " + Em("projects") + " you encounter.",
        "Go to the other room and retrieve it."
    };

    private static readonly string[] ReturnAlmanac =
    {
        "Good.",
        "Keep the " + Em("Almanac") + " with you.",
        "From now on, whenever you discover something important, check your " + Em("Almanac") + ".",
        "It will grow alongside your experience.",
        "Before we begin working, there is one thing you must never forget.",
        Em("Safety") + "."
    };

    private static readonly string[] AfterHat =
    {
        "Good.",
        "An engineer should always remember that construction is a " + Em("responsibility") + ".",
        "Now you're ready to begin.",
        "Let's go outside, and I'll show you how to " + Em("build") + "."
    };

    /// <returns>True only if this exact supported scene was changed.</returns>
    public static bool Apply(Scene scene, StringBuilder report = null)
    {
        RequireEditMode();
        if (!scene.IsValid() || !scene.isLoaded)
            throw new InvalidOperationException("Open the requested tutorial scene before applying its opening-story update.");
        bool changed;
        if (scene.path == CanyonPath)
        {
            TutorialSequence movement = FindOne<TutorialSequence>(scene, item => item.lessonName == "Sequence_Movement");
            DoorKnockTrigger door = FindOne<DoorKnockTrigger>(scene, item => item.uniqueDoorID == "BhansDoor");
            RequireSteps(movement, 7);
            if (!door.advancesTutorial || door.onEnterDoor == null || door.onEnterDoor.GetPersistentEventCount() == 0)
                throw new InvalidOperationException("Bhan's original knock/enter tutorial wiring is missing; opening-story update stopped.");
            changed = ApplyMovement(movement, door);
            report?.AppendLine("Opening tutorial: joystick/look/jump/stone-path/knock wording updated; sprint toggle and every existing action/waypoint/callback preserved.");
        }
        else if (scene.path == HousePath)
        {
            TutorialNameNPC bhan = FindOne<TutorialNameNPC>(scene, item => true);
            TutorialSequence house = FindOne<TutorialSequence>(scene, item => item.lessonName == "Sequence_House");
            TutorialSequence almanac = FindOne<TutorialSequence>(scene, item => item.lessonName == "Sequence_Alamnac");
            RequireSteps(house, 5);
            RequireSteps(almanac, 3);
            ValidateHouseGate(bhan, house, almanac);
            changed = ApplyHouse(bhan, house, almanac);
            report?.AppendLine("Bhan House: document dialogue applied, including the name question; Collect stays at the book until collected. Existing name/Almanac review/hat/exit gates and save IDs preserved.");
        }
        else
        {
            report?.AppendLine("Opening tutorial: unrelated scene left unchanged (" + scene.path + ").");
            return false;
        }
        if (changed) EditorSceneManager.MarkSceneDirty(scene);
        return changed;
    }

    public static void Preflight(Scene scene)
    {
        RequireEditMode();
        if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("Load the tutorial scene before preflight.");
        if (scene.path == CanyonPath)
        {
            TutorialSequence movement = FindOne<TutorialSequence>(scene, item => item.lessonName == "Sequence_Movement");
            DoorKnockTrigger door = FindOne<DoorKnockTrigger>(scene, item => item.uniqueDoorID == "BhansDoor");
            RequireSteps(movement, 7);
            if (!door.advancesTutorial || door.onEnterDoor == null || door.onEnterDoor.GetPersistentEventCount() == 0)
                throw new InvalidOperationException("Bhan's knock/enter wiring is missing.");
        }
        else if (scene.path == HousePath)
        {
            TutorialNameNPC bhan = FindOne<TutorialNameNPC>(scene, item => true);
            TutorialSequence house = FindOne<TutorialSequence>(scene, item => item.lessonName == "Sequence_House");
            TutorialSequence almanac = FindOne<TutorialSequence>(scene, item => item.lessonName == "Sequence_Alamnac");
            RequireSteps(house, 5); RequireSteps(almanac, 3);
            ValidateHouseGate(bhan, house, almanac);
        }
        else throw new InvalidOperationException("Unsupported tutorial scene.");
    }

    private static void ValidateHouseGate(TutorialNameNPC bhan, TutorialSequence house, TutorialSequence almanac)
    {
        var data = new SerializedObject(bhan);
        if (!bhan.advancesTutorial || data.FindProperty("houseTutorialId").stringValue != "Sequence_House" ||
            data.FindProperty("requiredAlmanacTutorialId").stringValue != "Sequence_Alamnac" ||
            string.IsNullOrWhiteSpace(bhan.rewardHatID) || bhan.npcWalker == null ||
            bhan.onFinalDialogueFinished == null || bhan.onFinalDialogueFinished.GetPersistentEventCount() == 0)
            throw new InvalidOperationException("Bhan House's existing registration/review/hat/exit wiring is incomplete; text update stopped before changing it.");
        TutorialStep find = house.tutorialSteps[1];
        if (find.stepWaypoints == null || find.stepWaypoints.Count != 1 || find.stepWaypoints[0].target == null ||
            house.tutorialSteps[2].stepWaypoints == null || house.tutorialSteps[2].stepWaypoints.Count != 1 ||
            house.tutorialSteps[3].pointerTarget == null || !house.tutorialSteps[3].advanceOnClick ||
            almanac.tutorialSteps[2].pointerTarget == null || !almanac.tutorialSteps[2].advanceOnClick)
            throw new InvalidOperationException("Bhan House's book pickup/open/close targets no longer match the reviewed tutorial; no update applied.");
    }

    private static bool ApplyMovement(TutorialSequence movement, DoorKnockTrigger door)
    {
        // Index 3 is the existing sprint-toggle introduction. Do not delete or
        // shift it: its pointer/button action is still useful on the same route.
        bool changed = SetMessage(movement, 0, "Use the " + Em("JOYSTICK") + " on the left side of the screen to move your character.");
        changed |= SetMessage(movement, 1, "Use the " + Em("RIGHT SIDE OF THE SCREEN") + " to look around and adjust your view.");
        changed |= SetMessage(movement, 2, "Excellent! Tap the " + Em("JUMP BUTTON") + " to climb over the ledge ahead.");
        changed |= SetMessage(movement, 4, "Follow the " + Em("STONE PATH") + " ahead.");
        changed |= SetMessage(movement, 5, "You've arrived at " + Em("Professor Bhan's House") + ". Approach the front door and knock.");
        changed |= SetMessage(movement, 6, "Go inside the " + Em("HOUSE") + ".");
        changed |= SetDialogue(door, "dialogue", new[] { "Come in! I've been expecting someone today." });
        return changed;
    }

    private static bool ApplyHouse(TutorialNameNPC bhan, TutorialSequence house, TutorialSequence almanac)
    {
        bool changed = SetDialogue(bhan, "askNameDialogue", Greeting);
        changed |= SetDialogue(bhan, "fetchAlmanacDialogue", FetchAlmanac);
        changed |= SetDialogue(bhan, "rewardDialogue", ReturnAlmanac);
        changed |= SetDialogue(bhan, "finalHouseDialogue", AfterHat);
        changed |= SetMessage(house, 0, Em("APPROACH") + " Professor Bhan.");
        changed |= SetMessage(house, 1, "Find the " + Em("ALMANAC") + " in the other room.");
        changed |= SetMessage(house, 2, "Collect the " + Em("ALMANAC") + ".");
        changed |= SetMessage(house, 3, "Open the " + Em("ALMANAC") + ".");
        changed |= SetMessage(house, 4, "Return to Professor Bhan with the " + Em("ALMANAC") + ".");
        changed |= SetMessage(almanac, 0, "This is your " + Em("ENGINEERING RECORD") + ".");
        changed |= SetMessage(almanac, 1, "New " + Em("DISCOVERIES") + " and completed " + Em("PROJECTS") + " will be added here throughout your journey.");
        changed |= SetMessage(almanac, 2, "Close the " + Em("ALMANAC") + ".");
        // Finding the book advances to Collect, not to Return. Keep its guide at
        // the already-authored book target until AlmanacPickup's Collect action
        // advances the sequence. Do not change any auto-advance flag or event.
        GuiderWaypoint pickup = house.tutorialSteps[2].stepWaypoints[0];
        Transform book = house.tutorialSteps[1].stepWaypoints[0].target;
        if (pickup.target != book)
        {
            Undo.RecordObject(house, "Update opening tutorial book objective");
            pickup.target = book;
            EditorUtility.SetDirty(house);
            changed = true;
        }
        return changed;
    }

    private static bool SetDialogue(Component owner, string field, string[] lines)
    {
        var data = new SerializedObject(owner);
        SerializedProperty dialogue = data.FindProperty(field);
        if (dialogue == null) throw new InvalidOperationException("Missing opening dialogue field: " + field);
        SerializedProperty speaker = dialogue.FindPropertyRelative("name");
        SerializedProperty sentences = dialogue.FindPropertyRelative("sentences");
        bool changed = speaker.stringValue != "Professor Bhan" || sentences.arraySize != lines.Length;
        if (!changed)
            for (int i = 0; i < lines.Length; i++)
                changed |= sentences.GetArrayElementAtIndex(i).stringValue != lines[i];
        if (!changed) return false;
        Undo.RecordObject(owner, "Update NEW Tutorial 1 opening dialogue");
        speaker.stringValue = "Professor Bhan";
        sentences.arraySize = lines.Length;
        for (int i = 0; i < lines.Length; i++) sentences.GetArrayElementAtIndex(i).stringValue = lines[i];
        data.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(owner);
        return true;
    }

    private static bool SetMessage(TutorialSequence sequence, int index, string message)
    {
        if (sequence.tutorialSteps[index].message == message) return false;
        Undo.RecordObject(sequence, "Update NEW Tutorial 1 opening objective");
        sequence.tutorialSteps[index].message = message;
        EditorUtility.SetDirty(sequence);
        return true;
    }

    private static T FindOne<T>(Scene scene, Func<T, bool> predicate) where T : Component
    {
        T[] matches = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).Where(predicate).ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException("Expected one exact " + typeof(T).Name + " tutorial target in " + scene.path + ", found " + matches.Length + ". Nothing substituted.");
        return matches[0];
    }

    private static void RequireSteps(TutorialSequence sequence, int expected)
    {
        if (sequence.tutorialSteps == null || sequence.tutorialSteps.Length != expected || sequence.tutorialSteps.Any(step => step == null))
            throw new InvalidOperationException("Tutorial " + sequence.lessonName + " changed its authored step layout; review its mapping before applying NEW Tutorial 1.");
    }

    private static void RequireEditMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || BuildPipeline.isBuildingPlayer)
            throw new InvalidOperationException("Apply the opening-story update only in idle Edit Mode.");
    }

    /// <summary>Opt-in native checks on temporary, inactive objects only.</summary>
    public static string ValidateDetachedFixtures()
    {
        RequireEditMode();
        Scene active = SceneManager.GetActiveScene();
        Scene preview = EditorSceneManager.NewPreviewScene();
        GameObject owner = null;
        int checks = 0;
        void Check(bool condition, string message)
        {
            checks++;
            if (!condition) throw new InvalidOperationException("Opening tutorial check failed: " + message);
        }
        try
        {
            owner = EditorUtility.CreateGameObjectWithHideFlags("Opening Tutorial (Inactive Test)", HideFlags.HideAndDontSave);
            owner.SetActive(false);
            SceneManager.MoveGameObjectToScene(owner, preview);
            TutorialSequence movement = owner.AddComponent<TutorialSequence>();
            movement.lessonName = "Sequence_Movement";
            movement.requiredPreviousLesson = "Original prerequisite";
            movement.playOnStart = true;
            movement.tutorialSteps = FixtureSteps(7);
            TutorialStep[] originalMovement = movement.tutorialSteps;
            string movementPolicy = SequencePolicy(movement);
            string sprintText = movement.tutorialSteps[3].message;
            DoorKnockTrigger door = owner.AddComponent<DoorKnockTrigger>();
            door.uniqueDoorID = "BhansDoor";
            door.advancesTutorial = true;
            door.delayAfterKnock = 1.5f;
            door.dialogue = new Dialogue { name = "Old speaker", sentences = new[] { "Old invitation" } };
            var enterEvent = door.onEnterDoor = new UnityEngine.Events.UnityEvent();
            Check(ApplyMovement(movement, door), "The first opening update must change old text.");
            Check(!ApplyMovement(movement, door), "The opening update must be idempotent.");
            Check(ReferenceEquals(originalMovement, movement.tutorialSteps) && SequencePolicy(movement) == movementPolicy,
                "Movement steps, actions, target references and locks must remain unchanged.");
            Check(movement.tutorialSteps[3].message == sprintText, "Sprint-toggle tutorial must stay authored.");
            Check(movement.tutorialSteps[4].message.Contains("STONE PATH") && movement.tutorialSteps[5].message.Contains("front door and knock"),
                "New stone-path and door objectives must be present.");
            Check(door.dialogue.sentences.SequenceEqual(new[] { "Come in! I've been expecting someone today." }) &&
                door.uniqueDoorID == "BhansDoor" && door.advancesTutorial && door.delayAfterKnock == 1.5f && ReferenceEquals(door.onEnterDoor, enterEvent),
                "Only the invitation text may change on Bhan's door.");

            TutorialNameNPC bhan = owner.AddComponent<TutorialNameNPC>();
            bhan.rewardHatID = "OriginalHat";
            bhan.rewardDisplayName = "Original hat label";
            bhan.advancesTutorial = true;
            var finalEvent = bhan.onFinalDialogueFinished = new UnityEngine.Events.UnityEvent();
            bhan.askNameDialogue = new Dialogue();
            bhan.fetchAlmanacDialogue = new Dialogue();
            bhan.rewardDialogue = new Dialogue();
            bhan.finalHouseDialogue = new Dialogue();
            bhan.reminderAlmanacDialogue = new Dialogue { name = "Reminder", sentences = new[] { "Preserve reminder" } };
            string reminder = JsonUtility.ToJson(bhan.reminderAlmanacDialogue);
            var houseOwner = EditorUtility.CreateGameObjectWithHideFlags("House Steps (Inactive Test)", HideFlags.HideAndDontSave);
            houseOwner.SetActive(false);
            houseOwner.transform.SetParent(owner.transform, false);
            TutorialSequence house = houseOwner.AddComponent<TutorialSequence>();
            house.lessonName = "Sequence_House";
            house.tutorialSteps = FixtureSteps(5);
            house.tutorialSteps[1].stepWaypoints = new List<GuiderWaypoint> { new GuiderWaypoint { target = owner.transform, advancesTutorial = true } };
            house.tutorialSteps[2].stepWaypoints = new List<GuiderWaypoint> { new GuiderWaypoint { target = houseOwner.transform, advancesTutorial = false } };
            TutorialSequence almanac = houseOwner.AddComponent<TutorialSequence>();
            almanac.lessonName = "Sequence_Alamnac";
            almanac.requiredPreviousLesson = "Sequence_Movement";
            almanac.tutorialSteps = FixtureSteps(3);
            TutorialStep[] originalHouse = house.tutorialSteps;
            TutorialStep[] originalAlmanac = almanac.tutorialSteps;
            // Capture the intended repaired target first, so the policy snapshot
            // excludes precisely that one intentional objective correction.
            house.tutorialSteps[2].stepWaypoints[0].target = owner.transform;
            string housePolicy = SequencePolicy(house);
            house.tutorialSteps[2].stepWaypoints[0].target = houseOwner.transform;
            string almanacPolicy = SequencePolicy(almanac);
            Check(ApplyHouse(bhan, house, almanac), "The first house update must change old text.");
            Check(!ApplyHouse(bhan, house, almanac), "The house update must be idempotent.");
            Check(bhan.askNameDialogue.sentences.Length == 6 && bhan.askNameDialogue.sentences.Last() == "Before we begin, what should I call you?",
                "Name registration must be explicitly requested after the greeting.");
            Check(bhan.fetchAlmanacDialogue.sentences[0].Contains("{PlayerName}") && bhan.fetchAlmanacDialogue.sentences.Length == 9,
                "Name substitution and the full engineering-record introduction must be present.");
            Check(bhan.rewardHatID == "OriginalHat" && bhan.rewardDisplayName == "Original hat label" && bhan.advancesTutorial &&
                ReferenceEquals(bhan.onFinalDialogueFinished, finalEvent) && JsonUtility.ToJson(bhan.reminderAlmanacDialogue) == reminder,
                "Hat reward, final callback, tutorial progression and reminder behavior must be preserved.");
            Check(ReferenceEquals(originalHouse, house.tutorialSteps) && ReferenceEquals(originalAlmanac, almanac.tutorialSteps) &&
                SequencePolicy(house) == housePolicy && SequencePolicy(almanac) == almanacPolicy,
                "Book tutorial IDs, arrays, pointers, close/collect actions and locks must be preserved.");
            Check(house.tutorialSteps[2].message.Contains("Collect") && house.tutorialSteps[2].stepWaypoints[0].target == owner.transform &&
                !house.tutorialSteps[2].stepWaypoints[0].advancesTutorial,
                "Collect must remain at the book and wait for the real pickup rather than proximity.");
            Check(house.lessonName == "Sequence_House" && almanac.lessonName == "Sequence_Alamnac" && movement.lessonName == "Sequence_Movement",
                "Every released opening-story save ID must be unchanged.");
            bool rejected = false;
            try { RequireSteps(house, 99); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "An unexpected authored step layout must be rejected instead of guessed.");
            return "PASS: " + checks + " detached opening-tutorial checks; no real scenes, player saves, rewards or dialogue executed.";
        }
        finally
        {
            if (owner != null) UnityEngine.Object.DestroyImmediate(owner);
            if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
            if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
        }
    }

    private static TutorialStep[] FixtureSteps(int count)
    {
        return Enumerable.Range(0, count).Select(index => new TutorialStep
        {
            message = "Original step " + index,
            canSkip = index % 2 == 0,
            lockLook = index % 2 == 1,
            pointerOffset = new Vector2(index, -index),
            stepWaypoints = new List<GuiderWaypoint>()
        }).ToArray();
    }

    private static string SequencePolicy(TutorialSequence sequence)
    {
        // Fixture-only serialization omits no fields other than the messages.
        string[] messages = sequence.tutorialSteps.Select(step => step.message).ToArray();
        try
        {
            foreach (TutorialStep step in sequence.tutorialSteps) step.message = "<content>";
            return EditorJsonUtility.ToJson(sequence);
        }
        finally
        {
            for (int i = 0; i < messages.Length; i++) sequence.tutorialSteps[i].message = messages[i];
        }
    }
}
#endif
