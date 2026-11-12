#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>On-demand inventory of authored and generated gameplay controls.</summary>
[InitializeOnLoad]
internal static class SceneInteractionAudioAudit
{
    private const string Request = "Temp/scene-interaction-audio.request";
    private const string Report = "Logs/scene-interaction-audio.txt";
    static SceneInteractionAudioAudit() => EditorApplication.update += Poll;

    private static string PathOf(Transform node) => node.parent == null ? node.name : PathOf(node.parent) + "/" + node.name;
    private static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer) return;
        string command = File.ReadAllText(Request).Trim();
        File.Delete(Request);
        try
        {
            Scene scene = SceneManager.GetActiveScene();
            var report = new StringBuilder("Game scene interaction audio\n");
            if (command == "inspect-all" || command == "validate-all")
            {
                AuditAllScenes(report, command == "inspect-all");
                if (command == "validate-all") ValidateInput(report);
                File.WriteAllText(Report, report.ToString());
                return;
            }
            if (command == "validate")
            {
                Coverage(scene, report);
                ValidateInput(report);
                File.WriteAllText(Report, report.ToString());
                return;
            }
            if (command != "inspect") throw new InvalidOperationException("Unknown interaction audit command.");
            Inspect(scene, report);
            File.WriteAllText(Report, report.ToString());
        }
        catch (Exception error) { File.WriteAllText(Report, "FAIL: " + error); Debug.LogException(error); }
    }

    private static void Inspect(Scene scene, StringBuilder report)
    {
            var objects = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true));
            foreach (Transform node in objects.OrderBy(PathOf, StringComparer.Ordinal))
            {
                var components = node.GetComponents<MonoBehaviour>().Where(component => component != null).ToArray();
                bool control = components.Any(component => component is Selectable || component is IPointerClickHandler ||
                    component is IPointerDownHandler || component is Interactable || component is EventTrigger);
                if (!control) continue;
                report.AppendLine($"CONTROL {PathOf(node)} active={node.gameObject.activeInHierarchy}");
                report.AppendLine("  TYPES " + string.Join(", ", components.Select(component => component.GetType().Name)));
                foreach (MonoBehaviour component in components)
                {
                    var data = new SerializedObject(component);
                    var property = data.GetIterator();
                    while (property.Next(true))
                    {
                        if (property.propertyType != SerializedPropertyType.Generic || !property.isArray ||
                            !property.propertyPath.EndsWith("m_PersistentCalls.m_Calls", StringComparison.Ordinal)) continue;
                        for (int i = 0; i < property.arraySize; i++)
                        {
                            var call = property.GetArrayElementAtIndex(i);
                            var target = call.FindPropertyRelative("m_Target").objectReferenceValue;
                            report.AppendLine($"  EVENT {component.GetType().Name}.{property.propertyPath}: {target?.GetType().Name}:{target?.name}.{call.FindPropertyRelative("m_MethodName").stringValue} ({call.FindPropertyRelative("m_Arguments.m_StringArgument").stringValue}) state={call.FindPropertyRelative("m_CallState").intValue}");
                        }
                    }
                }
            }
    }

    private static void AuditAllScenes(StringBuilder report, bool includeInventory)
    {
        Scene active = SceneManager.GetActiveScene();
        var loaded = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).ToArray();
        var dirty = loaded.Select(scene => scene.isDirty).ToArray();
        var bytes = EditorBuildSettings.scenes.Where(scene => scene.enabled).ToDictionary(scene => scene.path, scene => File.ReadAllBytes(scene.path));
        foreach (string path in bytes.Keys)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            Require(SceneInteractionAudio.SupportsScene(name), "No interaction audio support for build scene: " + name);
            Scene scene = SceneManager.GetSceneByPath(path);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            try
            {
                if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                report.AppendLine("SCENE " + name);
                Coverage(scene, report);
                if (includeInventory) Inspect(scene, report);
            }
            finally { if (opened && scene.IsValid()) EditorSceneManager.CloseScene(scene, true); }
        }
        Require(!SceneInteractionAudio.SupportsScene("Demo with terrain") && !SceneInteractionAudio.SupportsScene(""),
            "Game audio should not attach to unrelated demo scenes.");
        Require(SceneManager.sceneCount == loaded.Length && SceneManager.GetActiveScene() == active, "Loaded scene setup changed.");
        for (int i = 0; i < loaded.Length; i++)
            Require(SceneManager.GetSceneAt(i) == loaded[i] && loaded[i].isDirty == dirty[i], "Loaded scene state changed.");
        foreach (var pair in bytes) Require(pair.Value.SequenceEqual(File.ReadAllBytes(pair.Key)), "Audit changed a scene file: " + pair.Key);
        report.AppendLine("PASS all build scenes covered; demo scenes excluded; saved scenes and open scene setup preserved.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Coverage(Scene scene, StringBuilder report)
    {
        var nodes = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
        AlmanacManager book = nodes.SelectMany(node => node.GetComponents<AlmanacManager>()).FirstOrDefault();
        int fallback = 0, dedicated = 0, continuous = 0, interactions = 0;
        foreach (Transform node in nodes)
        {
            foreach (Selectable control in node.GetComponents<Selectable>())
            {
                if (UIActionAudio.SupportsFallback(control, book))
                {
                    fallback++;
                    report.AppendLine("FALLBACK " + PathOf(node));
                }
                else if (control is Button) dedicated++;
                else continuous++;
            }
            interactions += node.GetComponents<Interactable>().Length;
        }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/AudioManager.prefab");
        var entries = new SerializedObject(prefab.GetComponent<AudioManager>()).FindProperty("soundEffects");
        foreach (string id in new[] { "Interact", "KnockDoor", "MapOpen", "OpenAlamanac", "PageFlip", "PauseOpen" })
        {
            Require(Enumerable.Range(0, entries.arraySize).Any(i =>
                entries.GetArrayElementAtIndex(i).FindPropertyRelative("id").stringValue == id &&
                entries.GetArrayElementAtIndex(i).FindPropertyRelative("clip").objectReferenceValue != null), "Missing SFX: " + id);
        }
        var managers = nodes.SelectMany(node => node.GetComponents<AudioManager>()).ToArray();
        Require(managers.Any(manager => manager.enabled && manager.gameObject.activeInHierarchy), "Missing active scene AudioManager in " + scene.name);
        foreach (AudioManager manager in managers)
        {
            var settings = new SerializedObject(manager);
            var library = settings.FindProperty("soundEffects");
            Require(settings.FindProperty("persistAcrossScenes").boolValue && settings.FindProperty("sfxOutput").objectReferenceValue != null &&
                Enumerable.Range(0, library.arraySize).Any(i =>
                    library.GetArrayElementAtIndex(i).FindPropertyRelative("id").stringValue == "Interact" &&
                    library.GetArrayElementAtIndex(i).FindPropertyRelative("clip").objectReferenceValue != null &&
                    library.GetArrayElementAtIndex(i).FindPropertyRelative("volume").floatValue > 0f),
                "Scene SFX library, volume, persistence or mixer routing is invalid: " + scene.name);
        }
        Require(fallback > 0, "Unexpected empty UI audio coverage in " + scene.name);
        report.AppendLine($"PASS coverage: {fallback} fallback controls, {dedicated} dedicated-sound buttons, {continuous} continuous/input controls; {interactions} world interactables already use BaseInteract audio.");
    }

    // Detached Unity controls exercise real EventSystem dispatch and the real
    // audio service. No gameplay button, purchase, save, or network action runs.
    private static void ValidateInput(StringBuilder report)
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode && AudioManager.Instance == null &&
            SceneInteractionAudio.IsSupportedScene, "Run detached input checks in a supported game scene in Edit Mode.");
        bool dirty = SceneManager.GetActiveScene().isDirty;
        var preview = EditorSceneManager.NewPreviewScene();
        GameObject fixture = null;
        AudioManager audio = null;
        try
        {
            fixture = new GameObject("Detached interaction audio checks");
            SceneManager.MoveGameObjectToScene(fixture, preview);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/AudioManager.prefab");
            audio = fixture.AddComponent<AudioManager>();
            EditorUtility.CopySerialized(prefab.GetComponent<AudioManager>(), audio);
            var settings = new SerializedObject(audio);
            settings.FindProperty("persistAcrossScenes").boolValue = false;
            settings.ApplyModifiedPropertiesWithoutUndo();
            typeof(AudioManager).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(audio, null);
            int checks = 0;
            Action<bool, string> check = (condition, message) => { Require(condition, message); checks++; };
            Func<Type, Selectable> make = type =>
            {
                var node = new GameObject("Detached " + type.Name, typeof(RectTransform));
                SceneManager.MoveGameObjectToScene(node, preview);
                node.transform.SetParent(fixture.transform, false);
                var selectable = (Selectable)node.AddComponent(type);
                UIActionAudio.EnsureFeedback(selectable);
                return selectable;
            };
            var pointer = new PointerEventData(null) { button = PointerEventData.InputButton.Left, pointerId = 2 };
            Action<Selectable> down = value => ExecuteEvents.Execute(value.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            Action<Selectable> up = value => ExecuteEvents.Execute(value.gameObject, pointer, ExecuteEvents.pointerUpHandler);
            Action<Selectable> click = value => ExecuteEvents.Execute(value.gameObject, pointer, ExecuteEvents.pointerClickHandler);
            Action<Selectable> gesture = value => { value.GetComponent<UIActionAudio>().CaptureState(); down(value); up(value); click(value); };
            Button button = (Button)make(typeof(Button));
            int actions = 0;
            button.onClick.AddListener(() => actions++);
            ulong before = audio.SfxPlaybackRevision; gesture(button);
            check(audio.SfxPlaybackRevision == before + 1 && actions == 1, "Silent button was not filled exactly once.");
            button.interactable = false; before = audio.SfxPlaybackRevision; gesture(button);
            check(audio.SfxPlaybackRevision == before && actions == 1, "Disabled button produced feedback.");
            button.interactable = true;
            var group = button.gameObject.AddComponent<CanvasGroup>(); group.interactable = false;
            before = audio.SfxPlaybackRevision; gesture(button);
            check(audio.SfxPlaybackRevision == before, "CanvasGroup-blocked button produced feedback.");
            group.interactable = true;
            pointer.button = PointerEventData.InputButton.Right; before = audio.SfxPlaybackRevision; gesture(button);
            check(audio.SfxPlaybackRevision == before, "Right click produced feedback.");
            pointer.button = PointerEventData.InputButton.Left;
            before = audio.SfxPlaybackRevision; down(button); up(button);
            check(audio.SfxPlaybackRevision == before, "Canceled pointer gesture produced feedback.");
            pointer.dragging = true; before = audio.SfxPlaybackRevision; gesture(button);
            check(audio.SfxPlaybackRevision == before, "Scrolling over a button produced feedback.");
            pointer.dragging = false;
            button.onClick.RemoveAllListeners(); button.onClick.AddListener(() => audio.PlaySFX("MapOpen"));
            before = audio.SfxPlaybackRevision; gesture(button);
            check(audio.SfxPlaybackRevision == before + 1, "Existing action audio was doubled.");
            button.onClick.RemoveAllListeners(); button.onClick.AddListener(() => button.gameObject.SetActive(false));
            before = audio.SfxPlaybackRevision; gesture(button);
            check(audio.SfxPlaybackRevision == before + 1, "Closing its own panel lost the click sound.");
            button.gameObject.SetActive(true); button.onClick.RemoveAllListeners();
            audio.PlaySFX("Jump"); before = audio.SfxPlaybackRevision; gesture(button); gesture(button);
            check(audio.SfxPlaybackRevision == before + 2, "Unrelated SFX or another click suppressed input feedback.");
            button.onClick.AddListener(() => actions++); before = audio.SfxPlaybackRevision; gesture(button);
            check(audio.SfxPlaybackRevision == before + 1 && actions > 1, "Replacing UnityEvent listeners removed audio.");
            var selected = new BaseEventData(null);
            UIActionAudio feedbackButton = button.GetComponent<UIActionAudio>();
            feedbackButton.OnUpdateSelected(selected); before = audio.SfxPlaybackRevision;
            button.onClick.Invoke(); feedbackButton.OnSubmit(selected);
            check(audio.SfxPlaybackRevision == before + 1, "Keyboard/controller submit has no feedback.");
            button.interactable = false; feedbackButton.OnUpdateSelected(selected);
            before = audio.SfxPlaybackRevision; feedbackButton.OnSubmit(selected);
            check(audio.SfxPlaybackRevision == before, "Disabled submit produced feedback.");
            Toggle toggle = (Toggle)make(typeof(Toggle)); toggle.isOn = false;
            before = audio.SfxPlaybackRevision; toggle.isOn = true;
            check(audio.SfxPlaybackRevision == before, "Programmatic toggle changes produced feedback.");
            before = audio.SfxPlaybackRevision; gesture(toggle);
            check(audio.SfxPlaybackRevision == before + 1, "Toggle click has no feedback.");
            var toggleFeedback = toggle.GetComponent<UIActionAudio>();
            toggleFeedback.OnUpdateSelected(selected); before = audio.SfxPlaybackRevision;
            ExecuteEvents.Execute(toggle.gameObject, selected, ExecuteEvents.submitHandler);
            check(audio.SfxPlaybackRevision == before + 1, "Toggle submit has no feedback.");
            Slider slider = (Slider)make(typeof(Slider)); slider.value = .2f;
            slider.GetComponent<UIActionAudio>().CaptureState();
            before = audio.SfxPlaybackRevision; down(slider); slider.value = .4f; slider.value = .7f;
            check(audio.SfxPlaybackRevision == before, "Slider drag spammed sounds.");
            up(slider); click(slider);
            check(audio.SfxPlaybackRevision == before + 1, "Slider release did not play exactly once.");
            before = audio.SfxPlaybackRevision; gesture(slider);
            check(audio.SfxPlaybackRevision == before, "Unchanged slider produced feedback.");
            Navigation nav = slider.navigation; nav.mode = Navigation.Mode.None; slider.navigation = nav;
            slider.GetComponent<UIActionAudio>().OnUpdateSelected(selected); before = audio.SfxPlaybackRevision;
            ExecuteEvents.Execute(slider.gameObject, new AxisEventData(null) { moveDir = MoveDirection.Right, moveVector = Vector2.right }, ExecuteEvents.moveHandler);
            check(audio.SfxPlaybackRevision == before + 1, "Keyboard slider adjustment has no feedback.");
            Button generated = (Button)make(typeof(Button)); before = audio.SfxPlaybackRevision; gesture(generated);
            check(audio.SfxPlaybackRevision == before + 1, "Generated button has no feedback.");
            UIActionAudio.EnsureFeedback(generated); UIActionAudio.EnsureFeedback(generated);
            before = audio.SfxPlaybackRevision; gesture(generated);
            check(generated.GetComponents<UIActionAudio>().Length == 1 && audio.SfxPlaybackRevision == before + 1,
                "Repeated discovery created duplicate handlers.");
            var sources = fixture.GetComponentsInChildren<AudioSource>();
            check(sources.Any(source => source.clip != null && source.clip.name == "interaction_click_pop" &&
                source.outputAudioMixerGroup == audio.SfxOutput && source.ignoreListenerPause && !source.loop),
                "Fallback clip, pause behavior, or SFX mixer routing is wrong.");
            report.AppendLine($"PASS {checks} detached Unity input/audio checks (pointer, controller, disabled controls, own-panel close, dynamic listeners, sliders, and sound priority).");
        }
        finally
        {
            if (audio != null)
            {
                audio.StopAllSFX();
                typeof(AudioManager).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(audio, null);
            }
            if (fixture != null) UnityEngine.Object.DestroyImmediate(fixture);
            EditorSceneManager.ClosePreviewScene(preview);
            Require(SceneManager.GetActiveScene().isDirty == dirty, "Detached checks dirtied the gameplay scene.");
        }
    }
}
#endif
