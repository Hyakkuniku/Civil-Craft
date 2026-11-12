#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Runs real popup/dialogue methods in a disposable batch-only scene, without player data.</summary>
public static class CollectibleDialogueValidation
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
    private static bool Hidden(DialogueManager dialogue) => (bool)Get(dialogue, "collectiblePresentationHidden");
    private static object Get(object target, string field) => target.GetType().GetField(field, Flags).GetValue(target);
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Flags).SetValue(target, value);
    private static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Flags).Invoke(target, args);

    public static void RunBatch()
    {
        if (!Application.isBatchMode || !ParrelSync.ClonesManager.IsClone())
            throw new InvalidOperationException("Run this disposable fixture in the batch validation clone only.");
        Directory.CreateDirectory("Logs");
        string report = "Logs/CollectibleDialogueValidation.txt";
        try
        {
            // Retain the existing native Pause/input regression before discarding
            // its clean, read-only source scene for the isolated popup fixture.
            EditorSceneManager.OpenScene("Assets/Scenes/CanyonCrossing.unity", OpenSceneMode.Single);
            DialoguePauseResumeValidation.ValidateFromCommandLine();
            Check(!File.ReadAllText("Temp/DialoguePauseControlsValidation.txt").StartsWith("FAIL"), "Existing dialogue/Pause/input regression failed.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Check(PlayerDataManager.Instance == null && ItemUnlockUI.Instance == null && PauseManager.Instance == null,
                "The isolated fixture must not have a live save, reward or Pause owner.");

            var canvasObject = new GameObject("Disposable popup Canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            ((RectTransform)canvasObject.transform).sizeDelta = new Vector2(1920, 1080);
            var owner = new GameObject("Disposable dialogue owner");
            var dialogue = owner.AddComponent<DialogueManager>();
            var box = new GameObject("Disposable DialogueBox", typeof(RectTransform));
            box.transform.SetParent(canvasObject.transform, false);
            Set(dialogue, "dialogueBox", box);
            dialogue.nameText = Text(box.transform, "Speaker"); dialogue.dialogueText = Text(box.transform, "Sentence");
            var skipObject = new GameObject("Disposable Skip", typeof(RectTransform), typeof(Button));
            skipObject.transform.SetParent(canvasObject.transform, false);
            Set(dialogue, "skipDialogueButton", skipObject.GetComponent<Button>());
            Set(dialogue, "sentences", new Queue<string>(new[] { "Second sentence", "Third sentence" }));
            Set(dialogue, "cachedTypingWait", new WaitForSeconds(.03f)); Set(dialogue, "isDialogueActive", true);
            int dialogueCompletions = 0;
            Set(dialogue, "onDialogueEndCallback", (Action)(() => dialogueCompletions++));
            Call(dialogue, "OnEnable");
            IEnumerator typing = (IEnumerator)Call(dialogue, "TypeSentence", "The conversation waits for the collectible.");
            for (int frame = 0; frame < 5; frame++) typing.MoveNext();
            int character = dialogue.dialogueText.maxVisibleCharacters;

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/CollectRewardSystem.prefab");
            GameObject popupRoot = Object.Instantiate(prefab, canvasObject.transform, false);
            ItemUnlockUI popup = popupRoot.GetComponent<ItemUnlockUI>();
            if (ItemUnlockUI.Instance != popup) Call(popup, "Awake");
            popup.uiElementsToHide.Add(box); popup.uiElementsToHide.Add(skipObject);
            Check(box.GetComponent<CanvasGroup>() == null, "The absent-CanvasGroup regression must start without a group.");
            int almanacClaims = 0;
            popup.ShowReward("Engineering Almanac", null, string.Empty, () => almanacClaims++);
            CanvasGroup authoredGroup = box.GetComponent<CanvasGroup>();
            Check(authoredGroup != null && box.GetComponents<CanvasGroup>().Length == 1 && Hidden(dialogue) &&
                popup.popupPanel.activeInHierarchy, "Missing dialogue CanvasGroup interrupted the Almanac collectible before opening.");
            ClickCollect(canvasObject, popup.collectButton);
            Check(almanacClaims == 1 && !Hidden(dialogue) && !DialogueManager.IsCollectibleOverlayActive &&
                !popup.popupPanel.activeSelf, "Pointer-clicking Collect did not claim the Almanac exactly once and resume dialogue.");
            // Reuse the generated group as an authored non-default fade/lock.
            authoredGroup.alpha = .7f; authoredGroup.interactable = false;
            int shop = 0, map = 0, almanac = 0;
            popup.ShowReward("Shop", null, string.Empty, () => { shop++; popup.ShowReward("Almanac", null, string.Empty, () => almanac++); });
            popup.ShowReward("Map", null, string.Empty, () => map++);
            CanvasGroup mask = (CanvasGroup)Get(dialogue, "collectiblePresentationGroup");
            Check(DialogueManager.IsCollectibleOverlayActive && mask != null && Hidden(dialogue) && mask.alpha == 0f &&
                !mask.interactable && !mask.blocksRaycasts, "Collectible must hide and block the conversation immediately.");
            Check(box.activeInHierarchy && dialogue.isActiveAndEnabled && DialogueManager.IsAnyDialogueActive && !skipObject.activeSelf,
                "Hiding collectible dialogue must retain its owner/box and hide the separate Skip button.");
            for (int frame = 0; frame < 5; frame++)
                Check(typing.MoveNext() && typing.Current == null && dialogue.dialogueText.maxVisibleCharacters == character,
                    "Collectible advanced the suspended typewriter.");
            dialogue.DisplayNextSentence(); dialogue.SkipDialogue();
            Check(((Queue<string>)Get(dialogue, "sentences")).Count == 2 && dialogueCompletions == 0,
                "Next/Skip consumed hidden dialogue or advanced the story.");
            popup.collectButton.onClick.Invoke();
            Check(shop == 1 && map == 0 && popup.itemNameText.text.Contains("Map") && Hidden(dialogue) && !skipObject.activeSelf,
                "Callback-enqueued reward skipped/overwrote Map or exposed dialogue between collectibles.");
            popup.collectButton.onClick.Invoke();
            Check(map == 1 && almanac == 0 && popup.itemNameText.text.Contains("Almanac") && Hidden(dialogue),
                "Ordered collectible queue did not retain the next reward and dialogue mask.");
            popup.collectButton.onClick.Invoke();
            Check(almanac == 1 && !DialogueManager.IsCollectibleOverlayActive && !Hidden(dialogue) && skipObject.activeSelf,
                "Last Collect did not restore the active conversation and Skip.");
            Check(authoredGroup.alpha == .7f && !authoredGroup.interactable && dialogueCompletions == 0 &&
                ((Queue<string>)Get(dialogue, "sentences")).Count == 2, "Restore changed an authored fade/lock or story state.");
            Check(typing.MoveNext() && dialogue.dialogueText.maxVisibleCharacters == character + 1,
                "Conversation did not resume at its next character.");

            popup.ShowReward("Map", null, string.Empty, null);
            var secondOwner = new GameObject("Second collectible owner");
            DialogueManager.SetCollectibleOverlay(secondOwner, true);
            popup.collectButton.onClick.Invoke();
            Check(Hidden(dialogue) && !skipObject.activeSelf, "One owner released another collectible's mask.");
            DialogueManager.SetCollectibleOverlay(secondOwner, false);
            Check(!Hidden(dialogue) && skipObject.activeSelf, "Last collectible owner did not release its mask.");

            popup.ShowReward("Shop", null, string.Empty, null);
            Call(popup, "OnDisable");
            Check(!Hidden(dialogue) && !DialogueManager.IsCollectibleOverlayActive, "Disabled popup stranded a hidden dialogue.");
            Call(popup, "OnEnable");
            Check(Hidden(dialogue) && DialogueManager.IsCollectibleOverlayActive, "Re-enabled pending collectible did not resume its mask.");
            popup.collectButton.onClick.Invoke();

            Set(dialogue, "isDialogueActive", false); box.SetActive(true);
            popup.ShowReward("Map", null, string.Empty, null);
            Check(!box.activeSelf, "Collectible left a completed dialogue's closing animation visible.");
            popup.collectButton.onClick.Invoke();
            Check(!box.activeSelf && !skipObject.activeSelf && dialogueCompletions == 0, "Collect re-opened or re-completed a finished dialogue.");

            popup.ShowReward("Shop", null, string.Empty, () =>
            {
                dialogue.StartDialogue(new Dialogue { name = "Bhan", sentences = new[] { "The next conversation.", "Continue." } }, () => dialogueCompletions++);
                Check(Hidden(dialogue) && !skipObject.activeSelf, "Continuation briefly exposed dialogue before Collect returned.");
            });
            popup.collectButton.onClick.Invoke();
            Check(box.activeSelf && !Hidden(dialogue) && skipObject.activeSelf && DialogueManager.IsAnyDialogueActive,
                "Collect continuation did not show its new conversation safely.");

            popup.ShowReward("Map", null, string.Empty, () => { throw new InvalidOperationException("Fixture callback failure"); });
            bool callbackFailed = false;
            try { Call(popup, "OnCollectClicked"); } catch (TargetInvocationException error) { callbackFailed = error.InnerException?.Message == "Fixture callback failure"; }
            Check(callbackFailed && !Hidden(dialogue) && !DialogueManager.IsCollectibleOverlayActive,
                "Throwing continuation stranded the dialogue mask.");

            var pauseOwner = new GameObject("Disposable Pause owner"); var pause = pauseOwner.AddComponent<PauseManager>();
            typeof(PauseManager).GetField("<Instance>k__BackingField", Flags).SetValue(null, pause);
            popup.ShowReward("Shop", null, string.Empty, null);
            pause.isPaused = true; box.SetActive(false);
            popup.collectButton.onClick.Invoke();
            Check(!box.activeSelf && !skipObject.activeSelf && !Hidden(dialogue), "Collect re-opened a conversation still owned by Pause.");
            pause.isPaused = false; box.SetActive(true); Call(dialogue, "LateUpdate");
            Check(skipObject.activeSelf, "Skip was not restored after Pause released the collectible-covered conversation.");

            BridgeMaterialSO material = AssetDatabase.LoadAssetAtPath<BridgeMaterialSO>("Assets/BridgeBuilder/Data/Resources/Concrete Pier.asset");
            popup.ShowMaterialIntroduction(material);
            Check(popup.materialPopup.gameObject.activeSelf && !popup.popupPanel.activeSelf && Hidden(dialogue),
                "Material introduction did not use the same dialogue suppression.");
            Call(popup, "OnDestroy"); Object.DestroyImmediate(popupRoot);
            Check(!Hidden(dialogue) && !DialogueManager.IsCollectibleOverlayActive && skipObject.activeSelf,
                "Destroyed collectible owner stranded the conversation.");
            Check(dialogueCompletions == 0, "Presentation checks invoked a story completion callback.");
            Check(box.GetComponents<CanvasGroup>().Length == 1, "Repeated collectibles created duplicate dialogue masks.");
            File.WriteAllText(report, "PASS: " + checks + " native assertions. Missing/reused CanvasGroup and real GraphicRaycaster/pointer-click Almanac claim, Shop/Map/Almanac queue, callback re-entry, typewriter, Next/Skip, independent owners, disable/destroy, completed/new dialogue, nested Pause and authored material reader verified. No player data, discovery, account, network, scene or asset save was invoked.\n");
            Debug.Log("[Collectible Dialogue] " + File.ReadAllText(report));
        }
        catch (Exception error) { File.WriteAllText(report, "FAIL: " + error); throw; }
    }

    private static void ClickCollect(GameObject canvas, Button button)
    {
        // Render a disposable world-space Canvas so Unity assigns real graphic
        // depths before the batch-mode raycaster runs (not -1/unrendered).
        Canvas surface = canvas.GetComponent<Canvas>();
        var cameraObject = new GameObject("Disposable collectible pointer camera", typeof(Camera));
        Camera camera = cameraObject.GetComponent<Camera>();
        camera.orthographic = true; camera.orthographicSize = 540f; camera.transform.position = new Vector3(0, 0, -100);
        camera.nearClipPlane = .1f; camera.farClipPlane = 200f; camera.useOcclusionCulling = false;
        var target = new RenderTexture(1920, 1080, 24); target.Create(); camera.targetTexture = target;
        surface.renderMode = RenderMode.WorldSpace; surface.worldCamera = camera;
        ((RectTransform)surface.transform).sizeDelta = new Vector2(1920, 1080);
        Canvas.ForceUpdateCanvases();
        var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
        if (RenderPipeline.SupportsRenderRequest(camera, request)) RenderPipeline.SubmitRenderRequest(camera, request); else camera.Render();
        var events = new GameObject("Disposable pointer events", typeof(EventSystem)).GetComponent<EventSystem>();
        var pointer = new PointerEventData(events)
        {
            button = PointerEventData.InputButton.Left,
            position = RectTransformUtility.WorldToScreenPoint(camera, button.transform.position)
        };
        var hits = new List<RaycastResult>();
        canvas.GetComponent<GraphicRaycaster>().Raycast(pointer, hits);
        Check(button.IsActive() && button.IsInteractable() && hits.Count > 0 &&
            hits[0].gameObject.GetComponentInParent<Button>() == button, "The visible Almanac Collect button is blocked from UI pointer raycasts: active=" +
            button.IsActive() + "; interactable=" + button.IsInteractable() + "; hits=" + hits.Count + "; top=" +
            (hits.Count > 0 ? hits[0].gameObject.name : "none") + "; graphic depth=" + button.targetGraphic.depth + "; pointer=" + pointer.position);
        Check(ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerClickHandler) != null,
            "The real Collect button did not receive its pointer click.");
        Object.DestroyImmediate(events.gameObject);
        camera.targetTexture = null; target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(cameraObject);
        surface.worldCamera = null; surface.renderMode = RenderMode.ScreenSpaceOverlay;
    }

    private static TextMeshProUGUI Text(Transform parent, string name)
    {
        var obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        obj.transform.SetParent(parent, false);
        var text = obj.GetComponent<TextMeshProUGUI>();
        text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/Bekind Sans SDF.asset");
        return text;
    }
}
#endif
