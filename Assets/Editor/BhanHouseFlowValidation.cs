using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Opt-in Edit Mode regression checks, followed by the scoped house-door authoring repair.
/// Never enters Play Mode or calls gameplay saves, rewards, networking or scene transitions.
/// </summary>
public static class BhanHouseFlowValidation
{
    private const string Request = "Temp/BhanHouseFlowValidation.request";
    private const string Report = "Temp/BhanHouseFlowValidation.txt";
    private const string HousePath = "Assets/Scenes/BHAN HOUSE.unity";
    private const string Destination = "CanyonCrossing";
    private const string DestinationSpawn = "BhanOutsideHouse";
    private const string ArrivalLesson = "BhanHouseExited";
    private const string CompletedPrompt = "Go Outside";
    private const string LegacyLesson = "Sequence_Bridge";
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
    private static double nextCheck;
    private static string lastWaitingReason;
    private static bool running;

    [InitializeOnLoadMethod]
    private static void Watch()
    {
        EditorApplication.update -= CheckRequest;
        EditorApplication.update += CheckRequest;
    }

    private static void CheckRequest()
    {
        if (running || EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(Request)) return;
        string reason = WaitingReason();
        if (reason != null) { WriteWaiting(reason); return; }

        running = true;
        var report = new StringBuilder();
        try
        {
            // All regressions must pass before the authored house can be changed.
            ValidateFixtures(report);
            reason = WaitingReason();
            if (reason != null) { WriteWaiting(reason); return; }
            if (!ConfigureHouseDoor(report)) return;
            report.AppendLine("PASS: No Play Mode, player save, reward/unlock, API request or gameplay scene transition was invoked.");
            report.AppendLine("NOTE: Live dialogue/book animation and house-to-canyon travel still require a play test.");
            File.WriteAllText(Report, report.ToString());
            File.Delete(Request);
            lastWaitingReason = null;
            Debug.Log("[Bhan house flow] PASS. Report: " + Report);
        }
        catch (Exception error)
        {
            report.AppendLine("FAIL: " + error);
            File.WriteAllText(Report, report.ToString());
            File.Delete(Request);
            Debug.LogException(error);
        }
        finally { running = false; }
    }

    private static string WaitingReason()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "Stop Play Mode before Bhan house validation.";
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer)
            return "Waiting for compilation, imports and player builds to finish.";
        Scene house = SceneManager.GetSceneByPath(HousePath);
        if (house.IsValid() && house.isLoaded && house.isDirty)
            return "BHAN HOUSE has unsaved edits. Save or discard those edits before this scoped authoring repair.";
        return null;
    }

    private static void WriteWaiting(string reason)
    {
        // Keep the request so a dirty house or busy editor is never mistaken for completion.
        if (lastWaitingReason == reason) return;
        lastWaitingReason = reason;
        File.WriteAllText(Report, "WAITING: " + reason + "\nRequest retained; authored scenes have not been changed.\n");
    }

    private static void ValidateFixtures(StringBuilder report)
    {
        Check(WaitingReason() == null, "The editor is not ready for isolated validation.");
        Scene previousActive = SceneManager.GetActiveScene();
        var originalScenes = new Dictionary<Scene, bool>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            originalScenes.Add(scene, scene.isDirty);
        }
        FieldInfo instance = Field(typeof(PlayerDataManager), "<Instance>k__BackingField", PrivateStatic);
        FieldInfo currentData = Field(typeof(PlayerDataManager), "<CurrentData>k__BackingField", PrivateInstance);
        object previousManager = instance.GetValue(null);
        PlayerData realData = PlayerDataManager.Instance != null ? PlayerDataManager.Instance.CurrentData : null;
        string previousDataJson = realData != null ? JsonUtility.ToJson(realData) : null;
        var saveStamps = new Dictionary<string, string>();
        string guestPath = Path.Combine(Application.persistentDataPath, "playerSaveData.json");
        saveStamps.Add(guestPath, SaveStamp(guestPath));
        string activeSavePath = PlayerDataManager.Instance != null ? PlayerDataManager.Instance.CurrentSavePath : null;
        if (!string.IsNullOrEmpty(activeSavePath) && !saveStamps.ContainsKey(activeSavePath))
            saveStamps.Add(activeSavePath, SaveStamp(activeSavePath));
        FieldInfo fusionInstance = Field(typeof(FusionConnectionManager), "<Instance>k__BackingField", PrivateStatic);
        object previousFusion = fusionInstance.GetValue(null);
        FieldInfo bookInstance = Field(typeof(AlmanacManager), "<Instance>k__BackingField", PrivateStatic);
        object previousBook = bookInstance.GetValue(null);
        FieldInfo[] pendingFields =
        {
            Field(typeof(DoorTransition), "pendingArrivalScene", PrivateStatic),
            Field(typeof(DoorTransition), "pendingArrivalSpawnPoint", PrivateStatic),
            Field(typeof(DoorTransition), "pendingArrivalLessonId", PrivateStatic)
        };
        object[] previousPending = new object[pendingFields.Length];
        for (int i = 0; i < pendingFields.Length; i++) previousPending[i] = pendingFields[i].GetValue(null);
        string previousSpawn = PlayerSpawnManager.targetSpawnPointName;
        Scene preview = EditorSceneManager.NewPreviewScene();
        GameObject dataOwner = null, npcOwner = null, bookOwner = null;
        PlayerDataManager fixtureManager = null;
        object originalFixtureData = null;
        try
        {
            ValidateDoorPolicies(report);
            dataOwner = Fixture("Bhan Flow Data (Temporary, Inactive)", preview);
            PlayerDataManager manager = dataOwner.AddComponent<PlayerDataManager>();
            fixtureManager = manager;
            originalFixtureData = currentData.GetValue(manager);
            manager.enabled = false;
            Check(!dataOwner.activeInHierarchy && string.IsNullOrEmpty(manager.CurrentSavePath),
                "The isolated data manager unexpectedly ran its real Awake/load path.");
            currentData.SetValue(manager, ReviewData(true));
            instance.SetValue(null, manager);
            fusionInstance.SetValue(null, null); // A stale guest singleton cannot affect the detached fixture.

            npcOwner = Fixture("Bhan Flow NPC (Temporary)", preview);
            TutorialNameNPC npc = npcOwner.AddComponent<TutorialNameNPC>();
            npcOwner.SetActive(true); // Its Awake only discovers references; no interaction is dispatched.
            Check(npc.IsInteractionAvailable, "The isolated NPC must be available before eligibility checks.");
            MethodInfo consume = Method(typeof(TutorialNameNPC), "TryConsumeRequiredReviewCompletion", PrivateInstance);
            Check(consume.ReturnType == typeof(bool) && consume.GetParameters().Length == 0,
                "The required-review eligibility seam has changed.");

            Check(!Consume(npc, consume), "A normal Almanac opening must not arm Bhan's automatic continuation.");
            Arm(npc);
            Check(Consume(npc, consume) && !Consume(npc, consume), "Bhan's completed mandatory review must continue exactly once.");

            currentData.SetValue(manager, ReviewData(false));
            Arm(npc);
            Check(!Consume(npc, consume), "An unfinished Almanac review must not start the show/reward phase.");
            currentData.SetValue(manager, ReviewData(true));
            Check(!Consume(npc, consume), "A rejected review must consume its pending flag.");

            instance.SetValue(null, null);
            Arm(npc); Check(!Consume(npc, consume), "A missing player manager must not continue the house flow.");
            instance.SetValue(null, manager);
            currentData.SetValue(manager, null);
            Arm(npc); Check(!Consume(npc, consume), "Missing player data must not continue the house flow.");
            PlayerData data = ReviewData(true);
            currentData.SetValue(manager, data);
            data.hasAlmanac = false;
            Arm(npc); Check(!Consume(npc, consume), "A player without the Almanac must not continue the house flow.");
            data.hasAlmanac = true;
            foreach (string name in new[] { "Guest", "", "   ", null })
            {
                data.playerName = name;
                Arm(npc); Check(!Consume(npc, consume), "An unregistered player must not continue the house flow.");
            }
            data.playerName = "Fixture Engineer";
            npc.enabled = false;
            Arm(npc); Check(!Consume(npc, consume), "A disabled NPC must not continue the house flow.");
            npc.enabled = true;
            npcOwner.SetActive(false);
            Arm(npc); Check(!Consume(npc, consume), "An inactive NPC must not continue the house flow.");
            npcOwner.SetActive(true);
            foreach (string busy in new[] { "isCompletingHouseInteraction", "isWalkingAway" })
            {
                Set(npc, busy, true);
                Arm(npc); Check(!Consume(npc, consume), "An NPC already completing/leaving must not repeat house completion.");
                Set(npc, busy, false);
            }
            Check(!Consume(npc, consume), "An unrelated later interaction must not inherit a rejected pending review.");
            report.AppendLine("PASS: Bhan-requested completed review is eligible once; normal opening, incomplete review, missing data/book/name, disabled/inactive NPC and completing/leaving states cannot auto-continue.");

            bookOwner = Fixture("Bhan Flow Book (Temporary, Inactive)", preview);
            AlmanacManager book = bookOwner.AddComponent<AlmanacManager>();
            book.enabled = false;
            MethodInfo closed = Method(typeof(TutorialNameNPC), "HandleRequiredReviewClosed", PrivateInstance);
            var callback = (Action)Delegate.CreateDelegate(typeof(Action), npc, closed);
            Action unrelated = () => { };
            book.OnAlmanacClosed += unrelated;
            book.OnAlmanacClosed += callback;
            Set(npc, "requiredReviewBook", book);
            Arm(npc);
            Method(typeof(TutorialNameNPC), "OnDisable", PrivateInstance).Invoke(npc, null);
            Check(Get(npc, "requiredReviewBook") == null && !(bool)Get(npc, "isReviewingWithBhan") &&
                Get(npc, "reviewContinuation") == null, "Disabling Bhan must detach and disarm the required review.");
            var listeners = (Action)Field(typeof(AlmanacManager), "OnAlmanacClosed", PrivateInstance).GetValue(book);
            Check(listeners != null && listeners.GetInvocationList().Length == 1 && listeners == unrelated,
                "Bhan's cleanup must remove only its own Almanac close callback.");
            Method(typeof(TutorialNameNPC), "OnDisable", PrivateInstance).Invoke(npc, null);
            Check((Action)Field(typeof(AlmanacManager), "OnAlmanacClosed", PrivateInstance).GetValue(book) == unrelated,
                "Repeated Bhan cleanup must preserve unrelated listeners.");
            report.AppendLine("PASS: NPC disable unsubscribes only Bhan's close callback, clears the pending review and is safe to repeat.");
        }
        finally
        {
            // Restore real static ownership before fixture destruction/OnDestroy callbacks.
            instance.SetValue(null, previousManager);
            fusionInstance.SetValue(null, previousFusion);
            bookInstance.SetValue(null, previousBook);
            if (fixtureManager != null) currentData.SetValue(fixtureManager, originalFixtureData);
            for (int i = 0; i < pendingFields.Length; i++) pendingFields[i].SetValue(null, previousPending[i]);
            PlayerSpawnManager.targetSpawnPointName = previousSpawn;
            if (npcOwner != null) Object.DestroyImmediate(npcOwner);
            if (bookOwner != null) Object.DestroyImmediate(bookOwner);
            if (dataOwner != null) Object.DestroyImmediate(dataOwner);
            if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
            if (previousActive.IsValid() && previousActive.isLoaded) SceneManager.SetActiveScene(previousActive);
        }
        foreach (var original in originalScenes)
            Check(original.Key.IsValid() && original.Key.isLoaded && original.Key.isDirty == original.Value,
                "An authored scene's loaded/dirty state changed during isolated validation.");
        Check(instance.GetValue(null) == previousManager && fusionInstance.GetValue(null) == previousFusion &&
            bookInstance.GetValue(null) == previousBook, "Real singleton ownership was not restored.");
        Check(realData == null || JsonUtility.ToJson(realData) == previousDataJson,
            "Real player data changed during isolated validation.");
        foreach (var save in saveStamps)
            Check(SaveStamp(save.Key) == save.Value, "A real player-save file changed during isolated validation.");
        report.AppendLine("PASS: Real player data JSON and active/guest save-file stamps unchanged; singleton ownership restored.");
        report.AppendLine("PASS: Temporary preview fixtures removed; existing loaded scenes, active scene and pending spawn/arrival state preserved.");
    }

    private static void ValidateDoorPolicies(StringBuilder report)
    {
        var data = new PlayerData();
        const string original = "Follow Professor Bhan Outside";
        Check(Prompt(data, original) == original, "The first-exit prompt must preserve the authored instruction.");
        data.completedLessons.Add("Sequence_House");
        Check(Prompt(data, original) == original, "Finishing the house dialogue alone must not count as going outside.");
        DoorTransition.QueuePendingArrival(Destination, DestinationSpawn, ArrivalLesson);
        Check(data.completedLessons.Count == 1 && Prompt(data, original) == original,
            "Queuing travel must not complete an arrival or change the saved caption.");
        Check(!DoorTransition.TryConsumePendingArrival("OtherScene", DestinationSpawn, out string lesson) && lesson == null,
            "A wrong scene must not complete the pending arrival.");
        DoorTransition.QueuePendingArrival(Destination, DestinationSpawn, ArrivalLesson);
        Check(!DoorTransition.TryConsumePendingArrival(Destination, "OtherSpawn", out lesson) && lesson == null,
            "A wrong spawn must not complete the pending arrival.");
        DoorTransition.QueuePendingArrival(Destination, DestinationSpawn, ArrivalLesson);
        Check(DoorTransition.TryConsumePendingArrival(Destination, DestinationSpawn, out lesson) && lesson == ArrivalLesson,
            "The correct scene and spawn must resolve the configured arrival lesson.");
        Check(!DoorTransition.TryConsumePendingArrival(Destination, DestinationSpawn, out lesson) && lesson == null,
            "An arrival must be consumed only once.");
        data.completedLessons.Add(ArrivalLesson); // In-memory fixture only; never CompleteLesson/SaveGame.
        Check(Prompt(data, original) == CompletedPrompt, "A recorded exit must replace the follow instruction.");
        data.completedLessons.Clear(); data.completedLessons.Add(LegacyLesson);
        Check(Prompt(data, original) == CompletedPrompt, "Later bridge progress must repair older save captions.");
        Check(DoorTransition.ResolvePromptMessage(data, original, "", CompletedPrompt, LegacyLesson) == original &&
            DoorTransition.ResolvePromptMessage(data, original, ArrivalLesson, "", LegacyLesson) == original &&
            DoorTransition.ResolvePromptMessage(null, original, ArrivalLesson, CompletedPrompt, LegacyLesson) == original,
            "Unconfigured doors and absent data must preserve the original prompt.");
        data.completedLessons = null;
        Check(Prompt(data, original) == original, "Absent lesson collections must preserve the original prompt.");

        DoorTransition.QueuePendingArrival(Destination, DestinationSpawn, ArrivalLesson);
        DoorTransition.QueuePendingArrival("ReplacementScene", "ReplacementSpawn", "ReplacementLesson");
        Check(DoorTransition.TryConsumePendingArrival("ReplacementScene", "ReplacementSpawn", out lesson) && lesson == "ReplacementLesson",
            "A newer route must replace the earlier route.");
        Check(!DoorTransition.TryConsumePendingArrival(Destination, DestinationSpawn, out lesson), "A replaced route must not remain pending.");
        DoorTransition.QueuePendingArrival(Destination, DestinationSpawn, ArrivalLesson);
        DoorTransition.ClearPendingArrival();
        Check(!DoorTransition.TryConsumePendingArrival(Destination, DestinationSpawn, out lesson), "Clearing a failed/cancelled route must disarm arrival.");
        DoorTransition.QueuePendingArrival(Destination, DestinationSpawn, ArrivalLesson);
        Method(typeof(DoorTransition), "ResetStaticState", PrivateStatic).Invoke(null, null);
        Check(!DoorTransition.TryConsumePendingArrival(Destination, DestinationSpawn, out lesson), "A new play session must reset pending arrival.");
        foreach (string[] invalid in new[]
        {
            new[] { "", DestinationSpawn, ArrivalLesson },
            new[] { Destination, "", ArrivalLesson },
            new[] { Destination, DestinationSpawn, "   " }
        })
        {
            DoorTransition.QueuePendingArrival(Destination, DestinationSpawn, ArrivalLesson);
            DoorTransition.QueuePendingArrival(invalid[0], invalid[1], invalid[2]);
            Check(!DoorTransition.TryConsumePendingArrival(Destination, DestinationSpawn, out lesson),
                "An incomplete replacement route must clear stale arrival state.");
        }
        report.AppendLine("PASS: Arrival resolves only at the matching scene/spawn, once; queue has no completion side effect, newer/invalid routes replace stale state, clear/session reset disarm arrival.");
        report.AppendLine("PASS: First exit keeps the follow instruction; recorded exit and legacy bridge progress use Go Outside; house completion alone, absent data and unconfigured doors do not change the prompt.");
    }

    private static bool ConfigureHouseDoor(StringBuilder report)
    {
        SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup();
        bool wasInSetup = Array.Exists(setup, entry => entry.path == HousePath);
        Scene previousActive = SceneManager.GetActiveScene();
        Scene house = SceneManager.GetSceneByPath(HousePath);
        bool opened = !house.IsValid() || !house.isLoaded;
        try
        {
            if (opened) house = EditorSceneManager.OpenScene(HousePath, OpenSceneMode.Additive);
            if (house.isDirty)
            {
                WriteWaiting("BHAN HOUSE became dirty while opening; no authored changes were applied.");
                return false;
            }
            var matching = new List<DoorTransition>();
            foreach (GameObject root in house.GetRootGameObjects())
                foreach (DoorTransition door in root.GetComponentsInChildren<DoorTransition>(true))
                    if (door.sceneToLoad == Destination && door.spawnPointNameInNextScene == DestinationSpawn)
                        matching.Add(door);
            Check(matching.Count == 1, "Expected exactly one BHAN HOUSE door to CanyonCrossing/BhanOutsideHouse.");
            DoorTransition selected = matching[0];
            InteractionEvent interaction = selected.GetComponent<InteractionEvent>();
            Check(interaction != null && interaction.OnInteract != null, "The house exit is missing its interaction event.");
            var doorObject = new SerializedObject(selected);
            var eventObject = new SerializedObject(interaction);
            SerializedProperty arrival = RequiredProperty(doorObject, "arrivalLessonId");
            SerializedProperty caption = RequiredProperty(doorObject, "completedPromptMessage");
            SerializedProperty legacy = RequiredProperty(doorObject, "legacyCompletedLessonId");
            SerializedProperty calls = RequiredProperty(eventObject, "OnInteract.m_PersistentCalls.m_Calls");
            var preserved = new List<string>();
            var obsolete = new List<int>();
            bool enterDoorFound = false;
            for (int i = 0; i < calls.arraySize; i++)
            {
                SerializedProperty call = calls.GetArrayElementAtIndex(i);
                Object target = call.FindPropertyRelative("m_Target").objectReferenceValue;
                string method = call.FindPropertyRelative("m_MethodName").stringValue;
                if (target is TutorialManager && method == nameof(TutorialManager.ShowNextStep)) obsolete.Add(i);
                else
                {
                    preserved.Add(ListenerFingerprint(call));
                    if (target == selected && method == nameof(DoorTransition.EnterDoor)) enterDoorFound = true;
                }
            }
            Check(enterDoorFound, "The existing exact DoorTransition.EnterDoor callback must be preserved.");
            bool changed = arrival.stringValue != ArrivalLesson || caption.stringValue != CompletedPrompt ||
                legacy.stringValue != LegacyLesson || obsolete.Count > 0;
            arrival.stringValue = ArrivalLesson;
            caption.stringValue = CompletedPrompt;
            legacy.stringValue = LegacyLesson;
            for (int i = obsolete.Count - 1; i >= 0; i--) calls.DeleteArrayElementAtIndex(obsolete[i]);
            if (changed)
            {
                doorObject.ApplyModifiedPropertiesWithoutUndo();
                eventObject.ApplyModifiedPropertiesWithoutUndo();
            }
            eventObject.Update();
            calls = RequiredProperty(eventObject, "OnInteract.m_PersistentCalls.m_Calls");
            Check(calls.arraySize == preserved.Count, "The door repair changed unrelated listener count.");
            for (int i = 0; i < preserved.Count; i++)
                Check(ListenerFingerprint(calls.GetArrayElementAtIndex(i)) == preserved[i],
                    "The door repair changed an unrelated callback, arguments, order or call state.");
            Check(selected.arrivalLessonId == ArrivalLesson && selected.completedPromptMessage == CompletedPrompt &&
                selected.legacyCompletedLessonId == LegacyLesson, "The configured house exit fields were not applied.");
            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(house);
                Check(EditorSceneManager.SaveScene(house), "The scoped BHAN HOUSE authoring repair could not be saved.");
            }
            report.AppendLine("PASS: BHAN HOUSE exit configured with BhanHouseExited / Go Outside / Sequence_Bridge; " +
                obsolete.Count + " obsolete TutorialManager.ShowNextStep callback(s) removed. EnterDoor and all other callback arguments/order/states preserved.");
            report.AppendLine(changed ? "SAVED: Only Assets/Scenes/BHAN HOUSE.unity." : "UNCHANGED: The house exit already has the required authoring configuration.");
            return true;
        }
        finally
        {
            if (opened && house.IsValid() && house.isLoaded) EditorSceneManager.CloseScene(house, !wasInSetup);
            if (previousActive.IsValid() && previousActive.isLoaded) SceneManager.SetActiveScene(previousActive);
            SceneSetup[] after = EditorSceneManager.GetSceneManagerSetup();
            Check(setup.Length == after.Length, "The house repair changed the original scene setup count.");
            for (int i = 0; i < setup.Length; i++)
                Check(setup[i].path == after[i].path && setup[i].isLoaded == after[i].isLoaded &&
                    setup[i].isActive == after[i].isActive, "The house repair changed an original scene's open/loaded/active state.");
            report.AppendLine("PASS: Original open, loaded and active scene setup preserved after house-door authoring.");
        }
    }

    private static string ListenerFingerprint(SerializedProperty call)
    {
        SerializedProperty arguments = call.FindPropertyRelative("m_Arguments");
        Object target = call.FindPropertyRelative("m_Target").objectReferenceValue;
        Object argument = arguments.FindPropertyRelative("m_ObjectArgument").objectReferenceValue;
        return JsonUtility.ToJson(new ListenerDescription
        {
            targetId = target != null ? target.GetInstanceID() : 0,
            targetType = call.FindPropertyRelative("m_TargetAssemblyTypeName").stringValue,
            method = call.FindPropertyRelative("m_MethodName").stringValue,
            mode = call.FindPropertyRelative("m_Mode").intValue,
            state = call.FindPropertyRelative("m_CallState").intValue,
            objectId = argument != null ? argument.GetInstanceID() : 0,
            objectType = arguments.FindPropertyRelative("m_ObjectArgumentAssemblyTypeName").stringValue,
            intArgument = arguments.FindPropertyRelative("m_IntArgument").intValue,
            floatArgument = arguments.FindPropertyRelative("m_FloatArgument").floatValue,
            stringArgument = arguments.FindPropertyRelative("m_StringArgument").stringValue,
            boolArgument = arguments.FindPropertyRelative("m_BoolArgument").boolValue
        });
    }

    [Serializable]
    private sealed class ListenerDescription
    {
        public int targetId, mode, state, objectId, intArgument;
        public string targetType, method, objectType, stringArgument;
        public float floatArgument;
        public bool boolArgument;
    }

    private static string Prompt(PlayerData data, string original) => DoorTransition.ResolvePromptMessage(
        data, original, ArrivalLesson, CompletedPrompt, LegacyLesson);
    private static string SaveStamp(string path)
    {
        if (!File.Exists(path)) return "ABSENT";
        var file = new FileInfo(path);
        return file.Length + ":" + file.LastWriteTimeUtc.Ticks + ":" + file.CreationTimeUtc.Ticks;
    }
    private static PlayerData ReviewData(bool complete)
    {
        var data = new PlayerData { playerName = "Fixture Engineer", hasAlmanac = true };
        if (complete) data.completedLessons.Add("Sequence_Alamnac");
        return data;
    }
    private static GameObject Fixture(string name, Scene preview)
    {
        // Hidden unsaved roots avoid adding authoring objects to the user's active scene.
        GameObject owner = EditorUtility.CreateGameObjectWithHideFlags(name, HideFlags.HideAndDontSave);
        owner.SetActive(false);
        SceneManager.MoveGameObjectToScene(owner, preview);
        return owner;
    }
    private static void Arm(TutorialNameNPC npc) => Set(npc, "isReviewingWithBhan", true);
    private static bool Consume(TutorialNameNPC npc, MethodInfo consume)
    {
        bool eligible = (bool)consume.Invoke(npc, null);
        Check(!(bool)Get(npc, "isReviewingWithBhan"), "Eligibility evaluation must consume its pending flag.");
        return eligible;
    }
    private static object Get(object target, string name) => Field(target.GetType(), name, PrivateInstance).GetValue(target);
    private static void Set(object target, string name, object value) => Field(target.GetType(), name, PrivateInstance).SetValue(target, value);
    private static FieldInfo Field(Type type, string name, BindingFlags flags)
    {
        FieldInfo field = type.GetField(name, flags);
        Check(field != null, "Validation field not found: " + type.Name + "." + name);
        return field;
    }
    private static MethodInfo Method(Type type, string name, BindingFlags flags)
    {
        MethodInfo method = type.GetMethod(name, flags);
        Check(method != null, "Validation method not found: " + type.Name + "." + name);
        return method;
    }
    private static SerializedProperty RequiredProperty(SerializedObject owner, string name)
    {
        SerializedProperty property = owner.FindProperty(name);
        Check(property != null, "Authoring property not found: " + name);
        return property;
    }
    private static void Check(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
    }
}
