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
using Object = UnityEngine.Object;

/// <summary>Checks popup audio using disposable controls and callbacks without player data.</summary>
[InitializeOnLoad]
internal static class CollectibleAudioValidation
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly string[] AudioIds = { "CollectibleReveal", "CollectibleCollect", "MaterialIntroduction", "Interact" };
    private const string Request = "Temp/collectible-audio.request";
    private const string Report = "Logs/collectible-audio.txt";

    static CollectibleAudioValidation() => EditorApplication.update += Poll;

    private static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer) return;
        File.Delete(Request);
        var report = new StringBuilder("Collectible and material introduction audio\n");
        try
        {
            ValidateSceneLibraries(report);
            ValidatePopups(report);
            report.AppendLine("PASS collectible and material audio ready in all game scenes.");
        }
        catch (Exception error) { report.Insert(0, "FAIL\n"); report.AppendLine(error.ToString()); Debug.LogException(error); }
        File.WriteAllText(Report, report.ToString());
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void ValidateSceneLibraries(StringBuilder report)
    {
        foreach (EditorBuildSettingsScene definition in EditorBuildSettings.scenes.Where(scene => scene.enabled))
        {
            Scene scene = SceneManager.GetSceneByPath(definition.path);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            try
            {
                if (opened) scene = EditorSceneManager.OpenScene(definition.path, OpenSceneMode.Additive);
                var managers = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<AudioManager>(true)).ToArray();
                Require(managers.Length > 0, "Missing audio service in " + scene.name);
                foreach (AudioManager manager in managers)
                {
                    var library = new SerializedObject(manager).FindProperty("soundEffects");
                    foreach (string id in AudioIds)
                    {
                        var entries = Enumerable.Range(0, library.arraySize).Select(library.GetArrayElementAtIndex)
                            .Where(entry => entry.FindPropertyRelative("id").stringValue == id).ToArray();
                        Require(entries.Length == 1 && entries[0].FindPropertyRelative("clip").objectReferenceValue != null &&
                            entries[0].FindPropertyRelative("volume").floatValue > 0f, scene.name + " has invalid popup SFX " + id);
                    }
                }
                report.AppendLine("PASS audio library " + scene.name);
            }
            finally { if (opened && scene.IsValid()) EditorSceneManager.CloseScene(scene, true); }
        }
    }

    private static void Invoke(object owner, string method) => owner.GetType().GetMethod(method, Flags).Invoke(owner, null);

    private static void ValidatePopups(StringBuilder report)
    {
        Require(AudioManager.Instance == null && ItemUnlockUI.Instance == null && PlayerDataManager.Instance == null,
            "Run detached popup checks in Edit Mode without a live player/save owner.");
        var prompts = Object.FindObjectsOfType<Transform>(true).Where(node => node.name == "InteractionButtonContainer")
            .ToDictionary(node => node.gameObject, node => node.gameObject.activeSelf);
        Scene active = SceneManager.GetActiveScene();
        bool dirty = active.isDirty;
        float timeScale = Time.timeScale;
        bool listenerPause = AudioListener.pause;
        Scene preview = EditorSceneManager.NewPreviewScene();
        GameObject root = null;
        AudioManager audio = null;
        ItemUnlockUI popup = null;
        BridgeMaterialSO material = null;
        try
        {
            root = new GameObject("Detached collectible audio checks");
            SceneManager.MoveGameObjectToScene(root, preview);
            audio = root.AddComponent<AudioManager>();
            EditorUtility.CopySerialized(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/AudioManager.prefab").GetComponent<AudioManager>(), audio);
            var settings = new SerializedObject(audio);
            settings.FindProperty("persistAcrossScenes").boolValue = false;
            settings.ApplyModifiedPropertiesWithoutUndo();
            Invoke(audio, "Awake");

            GameObject popupObject = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/CollectRewardSystem.prefab"));
            SceneManager.MoveGameObjectToScene(popupObject, preview);
            popupObject.transform.SetParent(root.transform, false);
            popup = popupObject.GetComponent<ItemUnlockUI>();
            popup.uiElementsToHide.Clear();
            var readerObject = new GameObject("Detached material reader", typeof(RectTransform));
            SceneManager.MoveGameObjectToScene(readerObject, preview);
            readerObject.transform.SetParent(root.transform, false);
            var reader = readerObject.AddComponent<LearningPopupLayout>();
            var dismiss = new GameObject("GOT IT", typeof(RectTransform), typeof(Button));
            dismiss.transform.SetParent(readerObject.transform, false);
            reader.dismissButton = dismiss.GetComponent<Button>();
            popup.materialPopup = reader;
            Invoke(popup, "Awake");
            material = ScriptableObject.CreateInstance<BridgeMaterialSO>();
            material.displayName = "Detached wood beam";
            material.introductionDescription = "Material description for the isolated audio fixture.";
            int checks = 0;
            Action<bool, string> check = (condition, message) => { Require(condition, message); checks++; };
            int first = 0, second = 0, acknowledged = 0;
            ulong before = audio.SfxPlaybackRevision;
            popup.ShowReward("Almanac", null, "", () => first++);
            check(audio.SfxPlaybackRevision == before + 1 && popup.popupPanel.activeInHierarchy, "Collectible reveal must play once on display.");
            check(root.GetComponentsInChildren<AudioSource>(true).Any(source => source.clip != null && source.clip.name == "collectible_reveal"),
                "Collectible reveal uses the wrong clip.");
            before = audio.SfxPlaybackRevision;
            popup.ShowReward("Hat", null, "", () => second++);
            popup.ShowMaterialIntroduction(material, () => acknowledged++);
            check(audio.SfxPlaybackRevision == before, "Queued hidden entries played reveal sounds early.");
            popup.collectButton.onClick.Invoke();
            check(first == 1 && second == 0 && audio.SfxPlaybackRevision == before + 2, "Collect and next visible reward must each play once.");
            before = audio.SfxPlaybackRevision;
            popup.collectButton.onClick.Invoke();
            check(second == 1 && acknowledged == 0 && reader.gameObject.activeInHierarchy && audio.SfxPlaybackRevision == before + 2,
                "Queued material introduction did not play at its actual reveal.");
            check(root.GetComponentsInChildren<AudioSource>(true).Any(source => source.clip != null && source.clip.name == "material_introduction"),
                "Material reveal uses the wrong clip.");
            before = audio.SfxPlaybackRevision;
            reader.dismissButton.onClick.Invoke();
            check(acknowledged == 1 && audio.SfxPlaybackRevision == before + 1 && !reader.gameObject.activeSelf,
                "Material acknowledgement must click once and retain its continuation.");
            before = audio.SfxPlaybackRevision;
            reader.dismissButton.onClick.Invoke();
            check(audio.SfxPlaybackRevision == before && acknowledged == 1, "A hidden/finished material replayed audio or callbacks.");
            material.equipSfxId = "Equip_WoodBeam";
            before = audio.SfxPlaybackRevision;
            popup.ShowMaterialIntroduction(material);
            check(audio.SfxPlaybackRevision == before + 2, "Material reveal lost its material-specific accent.");
            reader.dismissButton.onClick.Invoke();
            popup.materialPopup = null;
            material.equipSfxId = "";
            before = audio.SfxPlaybackRevision;
            popup.ShowMaterialIntroduction(material);
            check(audio.SfxPlaybackRevision == before + 1 && popup.popupPanel.activeSelf, "Legacy material layout has no reveal audio.");
            popup.collectButton.onClick.Invoke();
            before = audio.SfxPlaybackRevision;
            popup.ShowReward("Reentrant first", null, "", () => popup.ShowReward("Reentrant next", null, "", null));
            popup.collectButton.onClick.Invoke();
            check(audio.SfxPlaybackRevision == before + 3 && popup.popupPanel.activeSelf, "Callback-enqueued reveal was skipped or played twice.");
            popup.collectButton.onClick.Invoke();
            AudioListener.pause = true;
            Time.timeScale = 0f;
            before = audio.SfxPlaybackRevision;
            popup.ShowReward("Paused reveal", null, "", null);
            check(audio.SfxPlaybackRevision == before + 1 && root.GetComponentsInChildren<AudioSource>().Any(source =>
                source.clip != null && source.ignoreListenerPause && source.outputAudioMixerGroup == audio.SfxOutput && !source.loop),
                "Popup audio must route to SFX and remain available during a paused reader.");
            popup.collectButton.onClick.Invoke();
            AudioListener.pause = listenerPause;
            Time.timeScale = timeScale;
            popup.ShowReward("Pointer collect", null, "", null);
            UIActionAudio.EnsureFeedback(popup.collectButton);
            var pointer = new PointerEventData(null) { button = PointerEventData.InputButton.Left, pointerId = 3 };
            ExecuteEvents.Execute(popup.collectButton.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(popup.collectButton.gameObject, pointer, ExecuteEvents.pointerUpHandler);
            before = audio.SfxPlaybackRevision;
            ExecuteEvents.Execute(popup.collectButton.gameObject, pointer, ExecuteEvents.pointerClickHandler);
            check(audio.SfxPlaybackRevision == before + 1, "Generic button fallback doubled the collection sound.");
            before = audio.SfxPlaybackRevision;
            popup.collectButton.onClick.Invoke();
            check(audio.SfxPlaybackRevision == before && !DialogueManager.IsCollectibleOverlayActive, "Empty queue replayed collection audio or retained its overlay.");
            report.AppendLine($"PASS {checks} detached popup/audio checks covering reveal timing, queue order, material identity, legacy layout, callbacks, pause, and click deduplication.");
        }
        finally
        {
            AudioListener.pause = listenerPause;
            Time.timeScale = timeScale;
            if (popup != null) Invoke(popup, "OnDestroy");
            if (audio != null) { audio.StopAllSFX(); Invoke(audio, "OnDestroy"); }
            if (root != null) Object.DestroyImmediate(root);
            if (material != null) Object.DestroyImmediate(material);
            EditorSceneManager.ClosePreviewScene(preview);
            foreach (var prompt in prompts) if (prompt.Key != null) prompt.Key.SetActive(prompt.Value);
            Require(SceneManager.GetActiveScene() == active && active.isDirty == dirty, "Popup checks changed the open scene.");
        }
    }
}
#endif
