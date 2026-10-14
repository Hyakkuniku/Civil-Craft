using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Opt-in native input checks using only a disposable player, never live saves.</summary>
public static class NPCQuizControlsValidation
{
    private const string Request = "Temp/npc-quiz-controls-validation-v1.request";
    private const string Report = "Temp/NPCQuizControlsValidation.txt";
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static bool running;
    private static double nextCheck;

    [InitializeOnLoadMethod]
    private static void Watch() { EditorApplication.update -= CheckRequest; EditorApplication.update += CheckRequest; }

    private static bool Busy => running || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
        EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer || PrefabStageUtility.GetCurrentPrefabStage() != null;

    private static void CheckRequest()
    {
        if (Busy || EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(Request)) return;
        File.Delete(Request);
        ValidateFromMenu();
    }

    [MenuItem("Tools/Civil Craft/Quizzes/Validate Native Quiz Controls")]
    public static void ValidateFromMenu()
    {
        Directory.CreateDirectory("Temp");
        try { File.WriteAllText(Report, Validate()); Debug.Log("[NPC Quiz Controls] PASS: " + Report); }
        catch (Exception error) { File.WriteAllText(Report, "FAIL: " + error); Debug.LogException(error); }
    }

    private static string Validate()
    {
        Check(!Busy, "Run in idle Edit Mode, outside Prefab Mode.");
        running = true;
        Scene originalActive = SceneManager.GetActiveScene();
        Object[] selection = Selection.objects;
        Object selected = Selection.activeObject;
        CursorLockMode cursor = Cursor.lockState;
        bool cursorVisible = Cursor.visible;
        float timeScale = Time.timeScale;
        var scenes = new Dictionary<Scene, Tuple<bool, GameObject[]>>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            scenes.Add(scene, Tuple.Create(scene.isDirty, scene.GetRootGameObjects()));
        }
        FieldInfo gameSingleton = Field(typeof(GameManager), "<Instance>k__BackingField");
        FieldInfo pauseSingleton = Field(typeof(PauseManager), "<Instance>k__BackingField");
        FieldInfo coordinatorSingleton = Field(typeof(UIPanelCoordinator), "<Instance>k__BackingField");
        FieldInfo activeQuiz = Field(typeof(QuizUIManager), "activeManager");
        object gameBefore = gameSingleton.GetValue(null), pauseBefore = pauseSingleton.GetValue(null),
            coordinatorBefore = coordinatorSingleton.GetValue(null), quizBefore = activeQuiz.GetValue(null);
        var dialogues = (HashSet<DialogueManager>)Field(typeof(DialogueManager), "knownManagers").GetValue(null);
        DialogueManager[] dialoguesBefore = dialogues.ToArray();
        InputManager[] liveInputs = Object.FindObjectsOfType<InputManager>(true);
        bool[] movement = liveInputs.Select(input => input.IsPlayerInputEnabled).ToArray();
        bool[] lookFlags = liveInputs.Select(input => input.IsLookInputEnabled).ToArray();
        Scene temporary = default;
        GameObject root = null;
        InputManager fixtureInput = null;
        QuizUIManager manager = null;
        var report = new StringBuilder();
        try
        {
            gameSingleton.SetValue(null, null);
            pauseSingleton.SetValue(null, null);
            coordinatorSingleton.SetValue(null, null);
            activeQuiz.SetValue(null, null);
            dialogues.Clear();
            temporary = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(temporary);
            root = new GameObject("NPC Quiz Native Controls (Disposable)");
            SceneManager.MoveGameObjectToScene(root, temporary);
            GameObject player = Child(root.transform, "Synthetic local player");
            PlayerLook look = player.AddComponent<PlayerLook>();
            PlayerInteract interaction = player.AddComponent<PlayerInteract>();
            fixtureInput = player.AddComponent<InputManager>();
            Call(fixtureInput, "EnsureInputInitialized");
            manager = Child(root.transform, "Synthetic quiz owner").AddComponent<QuizUIManager>();
            Set(manager, "ownerScene", temporary);
            for (int cycle = 0; cycle < 3; cycle++)
            {
                fixtureInput.SetPlayerInputEnable(true);
                fixtureInput.SetLookEnabled(true);
                interaction.enabled = true;
                Call(manager, "LockPlayerControls");
                Check(ReferenceEquals(Get(manager, "lockedInput"), fixtureInput), "Quiz resolved a different scene's player.");
                Check(!fixtureInput.IsPlayerInputEnabled && !fixtureInput.onFoot.enabled && !look.canLook && !interaction.enabled,
                    "Quiz failed to lock the generated movement map, camera look, or interaction.");
                Call(manager, "RestorePlayerControls");
                Check(fixtureInput.IsPlayerInputEnabled && fixtureInput.onFoot.enabled && look.canLook && interaction.enabled,
                    "Quiz failed to restore native controls after closing.");
            }
            report.AppendLine("PASS: Three native lock/restore cycles use the same-scene synthetic player and restore generated onFoot actions, PlayerLook.canLook and PlayerInteract.enabled.");

            fixtureInput.SetPlayerInputEnable(false);
            fixtureInput.SetLookEnabled(false);
            interaction.enabled = false;
            Call(manager, "LockPlayerControls");
            Call(manager, "RestorePlayerControls");
            Check(!fixtureInput.IsPlayerInputEnabled && !look.canLook && !interaction.enabled,
                "Quiz force-enabled controls that were disabled before opening.");
            report.AppendLine("PASS: Previously disabled movement/look/interaction remain disabled; PlayerMotor is never forced on.");

            PauseManager pause = Child(root.transform, "Synthetic pause owner").AddComponent<PauseManager>();
            pauseSingleton.SetValue(null, pause);
            fixtureInput.SetPlayerInputEnable(true);
            fixtureInput.SetLookEnabled(true);
            interaction.enabled = true;
            Call(manager, "LockPlayerControls");
            Set(manager, "sessionActive", true);
            activeQuiz.SetValue(null, manager);
            pause.isPaused = true;
            bool resumeAllowed = !DialogueManager.IsAnyDialogueActive && !QuizUIManager.IsAnyQuizActive;
            Check(!resumeAllowed, "Pause's quiz gate released an active quiz lock.");
            pause.isPaused = false;
            fixtureInput.SetPlayerInputEnable(resumeAllowed);
            fixtureInput.SetLookEnabled(resumeAllowed);
            Check(!fixtureInput.onFoot.enabled && !look.canLook, "Resume enabled controls behind quiz.");
            Set(manager, "sessionActive", false);
            activeQuiz.SetValue(null, null);
            Call(manager, "RestorePlayerControls");
            Check(fixtureInput.onFoot.enabled && look.canLook && interaction.enabled, "Controls stayed locked after resumed quiz closed.");
            report.AppendLine("PASS: Active quiz participates in Pause's restore gate; native action map/look stay off on Resume and restore when the quiz closes.");
        }
        finally
        {
            if (manager != null) Set(manager, "sessionActive", false);
            activeQuiz.SetValue(null, null);
            if (fixtureInput != null)
            {
                PlayerInput actions = (PlayerInput)Get(fixtureInput, "playerInput");
                if (actions != null)
                {
                    actions.Disable();
                    var asset = actions.asset;
                    Set(fixtureInput, "playerInput", null);
                    fixtureInput.onFoot = default;
                    if (asset != null && !EditorUtility.IsPersistent(asset)) Object.DestroyImmediate(asset);
                }
                Call(fixtureInput, "OnDestroy");
            }
            if (root != null) Object.DestroyImmediate(root);
            if (originalActive.IsValid() && originalActive.isLoaded) SceneManager.SetActiveScene(originalActive);
            if (temporary.IsValid() && temporary.isLoaded) EditorSceneManager.CloseScene(temporary, true);
            gameSingleton.SetValue(null, gameBefore);
            pauseSingleton.SetValue(null, pauseBefore);
            coordinatorSingleton.SetValue(null, coordinatorBefore);
            activeQuiz.SetValue(null, quizBefore);
            dialogues.Clear();
            foreach (DialogueManager dialogue in dialoguesBefore) dialogues.Add(dialogue);
            Time.timeScale = timeScale;
            Cursor.lockState = cursor;
            Cursor.visible = cursorVisible;
            Selection.objects = selection;
            Selection.activeObject = selected;
            running = false;
        }
        foreach (var pair in scenes)
            Check(pair.Key.isLoaded && pair.Key.isDirty == pair.Value.Item1 && pair.Key.GetRootGameObjects().SequenceEqual(pair.Value.Item2),
                "Source scene roots/dirty state changed.");
        for (int i = 0; i < liveInputs.Length; i++)
            Check(liveInputs[i] != null && liveInputs[i].IsPlayerInputEnabled == movement[i] && liveInputs[i].IsLookInputEnabled == lookFlags[i],
                "Live player input state changed.");
        Check(SceneManager.GetActiveScene() == originalActive && Mathf.Approximately(Time.timeScale, timeScale) &&
            Selection.objects.SequenceEqual(selection) && Selection.activeObject == selected && Cursor.lockState == cursor && Cursor.visible == cursorVisible,
            "Editor scene/selection/time/cursor state was not restored.");
        Check(ReferenceEquals(gameSingleton.GetValue(null), gameBefore) && ReferenceEquals(pauseSingleton.GetValue(null), pauseBefore) &&
            ReferenceEquals(coordinatorSingleton.GetValue(null), coordinatorBefore) && ReferenceEquals(activeQuiz.GetValue(null), quizBefore) && dialogues.SetEquals(dialoguesBefore),
            "Singleton/dialogue registry was not restored.");
        report.AppendLine("PASS: Existing scene roots/dirty states, active scene, live input flags, registries, selection, cursor and time restored.");
        report.AppendLine("Scope: actual quiz lock/restore helpers and generated Input System map; isolated Pause gate. Full modal/session/save callbacks are covered separately by source-extracted tests. No live save, account, network, scene save, or automatic Play Mode.");
        report.AppendLine("Completed UTC: " + DateTime.UtcNow.ToString("O"));
        return report.ToString();
    }

    private static GameObject Child(Transform parent, string name)
    {
        var child = new GameObject(name);
        SceneManager.MoveGameObjectToScene(child, parent.gameObject.scene);
        child.transform.SetParent(parent, false);
        return child;
    }
    private static FieldInfo Field(Type type, string name) => type.GetField(name, Flags) ?? throw new MissingFieldException(type.FullName, name);
    private static object Get(object target, string name) => Field(target.GetType(), name).GetValue(target);
    private static void Set(object target, string name, object value) => Field(target.GetType(), name).SetValue(target, value);
    private static object Call(object target, string name) => target.GetType().GetMethod(name, Flags).Invoke(target, null);
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
