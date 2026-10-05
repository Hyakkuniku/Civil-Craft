#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Opt-in, isolated native checks. Never authors UI, changes saves or opens a network session.</summary>
public static class RecordingHUDShortcutValidation
{
    private const string Request = "Temp/RecordingHUDShortcutValidation.request";
    private const string Report = "Temp/RecordingHUDShortcutValidation.txt";
    private const string ManagerPrefab = "Assets/Prefabs/BuildingMode/MANAGERS AND CANVASES.prefab";
    private const string Images = "Temp/RecordingHUDShortcutPreviews";
    private const BindingFlags InstanceMethods = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags StaticMethods = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static double nextCheck;

    [InitializeOnLoadMethod]
    private static void Watch()
    {
        EditorApplication.update -= CheckRequest;
        EditorApplication.update += CheckRequest;
    }

    private static void CheckRequest()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(Request)) return;
        if (UnsafeReason() != null)
        {
            File.WriteAllText(Report, "WAIT: " + UnsafeReason() + ". No scenes, UI or saves changed.");
            return;
        }
        File.Delete(Request);
        Validate();
    }

    private static string UnsafeReason()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "Stop Play Mode";
        if (BuildPipeline.isBuildingPlayer) return "Wait for the player build";
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return "Wait for Unity compilation/import";
        return null;
    }

    [MenuItem("Tools/Civil Craft/Validate Recording HUD Shortcut")]
    public static void Validate()
    {
        if (UnsafeReason() != null) return;
        var report = new StringBuilder("RUNNING: Isolated native Ctrl+H HUD validation.\n");
        File.WriteAllText(Report, report.ToString());
        Scene preview = default;
        Scene activeScene = SceneManager.GetActiveScene();
        FieldInfo eventSystemsField = typeof(EventSystem).GetField("m_EventSystems", StaticMethods);
        IList eventSystems = eventSystemsField != null ? eventSystemsField.GetValue(null) as IList : null;
        object[] originalEventSystems = eventSystems != null ? eventSystems.Cast<object>().ToArray() : null;
        FieldInfo coordinatorInstance = typeof(UIPanelCoordinator).GetField("<Instance>k__BackingField", StaticMethods);
        object originalCoordinator = coordinatorInstance != null ? coordinatorInstance.GetValue(null) : null;
        float originalTimeScale = Time.timeScale;
        try
        {
            Type runtimeType = typeof(UIPanelCoordinator).Assembly.GetType("RecordingHUDShortcut");
            Check(runtimeType != null, "The recording HUD shortcut has not been compiled yet.");
            Check(coordinatorInstance != null, "The coordinator Instance fixture hook is unavailable.");
            Check(eventSystems != null, "The EventSystem isolation fixture hook is unavailable.");
            preview = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("Recording HUD validation (temporary)");
            SceneManager.MoveGameObjectToScene(root, preview);
            var serviceOwner = Child(root.transform, "Recording service fixture (inactive)");
            serviceOwner.SetActive(false);
            Component service = serviceOwner.AddComponent(runtimeType);
            var coordinatorOwner = Child(root.transform, "Coordinator fixture (inactive)");
            coordinatorOwner.SetActive(false);
            UIPanelCoordinator coordinator = coordinatorOwner.AddComponent<UIPanelCoordinator>();
            coordinatorInstance.SetValue(null, coordinator);

            ValidateShortcut(runtimeType, report);
            ValidateMasks(service, coordinator, root, report);
            ValidateTextFocus(runtimeType, root, report);
            ValidateActualPrefab(service, report);
            Check(Mathf.Approximately(Time.timeScale, originalTimeScale), "Validation changed the gameplay time scale.");
            report.AppendLine("PASS: No scene authoring, save, purchase, authentication or multiplayer APIs were used; source prefab file unchanged.");
            Debug.Log("[Recording HUD shortcut] Shortcut, input focus, native alpha/raycast, modal suspension, rescan and actual prefab checks passed.");
        }
        catch (Exception error)
        {
            report.AppendLine("FAIL: " + Unwrap(error));
            Debug.LogException(Unwrap(error));
        }
        finally
        {
            // Restore globals before temporary components are destroyed, even on a failed assertion.
            if (coordinatorInstance != null) coordinatorInstance.SetValue(null, originalCoordinator);
            if (eventSystems != null && originalEventSystems != null)
            {
                eventSystems.Clear();
                foreach (object original in originalEventSystems) eventSystems.Add(original);
            }
            Time.timeScale = originalTimeScale;
            if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
            if (activeScene.IsValid() && activeScene.isLoaded) SceneManager.SetActiveScene(activeScene);
            File.WriteAllText(Report, report.ToString());
        }
    }

    private static void ValidateShortcut(Type runtime, StringBuilder report)
    {
        MethodInfo gate = runtime.GetMethod("ShouldToggleShortcut", StaticMethods);
        Check(gate != null, "Shortcut gate is missing.");
        foreach (bool control in new[] { false, true })
            foreach (bool pressed in new[] { false, true })
                foreach (bool typing in new[] { false, true })
                    Check((bool)gate.Invoke(null, new object[] { control, pressed, typing }) == (control && pressed && !typing),
                        "The Ctrl+H gate toggles on a wrong combination or while typing.");
        report.AppendLine("PASS: All 8 Ctrl/H/text-entry combinations; Ctrl+H toggles only on an H press with Control held and no text input.");
    }

    private static void ValidateMasks(Component service, UIPanelCoordinator coordinator, GameObject fixture, StringBuilder report)
    {
        const int width = 512, height = 256;
        var cameraOwner = Child(fixture.transform, "Recording HUD preview camera (temporary)");
        Camera camera = cameraOwner.AddComponent<Camera>();
        camera.enabled = false; camera.scene = fixture.scene;
        camera.transform.position = new Vector3(0f, 0f, -100f);
        camera.orthographic = true; camera.orthographicSize = 128f; camera.aspect = 2f;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
        camera.nearClipPlane = .1f; camera.farClipPlane = 1000f; camera.cullingMask = 1 << 5;
        camera.allowHDR = false; camera.allowMSAA = false;
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
        var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
        target.Create(); camera.targetTexture = target;
        Canvas main = CanvasFixture(fixture.transform, "MainCanvas", camera);
        Canvas build = null;
        GameObject modal = null;
        bool subscribed = false;
        Canvas.WillRenderCanvases observePreRender = null;
        try
        {
        Invoke(service, "OnEnable");
        subscribed = true;
        Image controls = ImageFixture(main.transform, "MoveButtons");
        controls.rectTransform.anchoredPosition = new Vector2(-120f, 0f);
        CanvasGroup first = controls.gameObject.AddComponent<CanvasGroup>();
        first.alpha = .37f; first.interactable = true; first.blocksRaycasts = true;
        Image escaped = ImageFixture(controls.transform, "Ignore-parent HUD child");
        CanvasGroup escapeGroup = escaped.gameObject.AddComponent<CanvasGroup>();
        escapeGroup.alpha = .61f; escapeGroup.ignoreParentGroups = true;
        Image inactive = ImageFixture(main.transform, "AccessButtons");
        inactive.gameObject.SetActive(false);
        Image dialogue = ImageFixture(main.transform, "DialogueBox");
        Image registration = ImageFixture(main.transform, "NameRegistrationUI");
        Image registrationConfirmation = ImageFixture(main.transform, "Conf");
        Image mobileSettings = ImageFixture(main.transform, "MobileSettingsPanel");
        foreach (Image exclusion in new[] { dialogue, registration, registrationConfirmation, mobileSettings })
            exclusion.rectTransform.anchoredPosition = new Vector2(180f, 0f);
        GameObject utility = Child(main.transform, "UtilityPanel");
        Image welcome = ImageFixture(utility.transform, "txt_welcomeArea");
        Image utilityModal = ImageFixture(utility.transform, "Authored confirmation");
        utilityModal.rectTransform.anchoredPosition = new Vector2(180f, 0f);
        build = CanvasFixture(fixture.transform, "BuildCanvas", camera);
        Image simulation = ImageFixture(build.transform, "MaterialsScrollPanel");
        CanvasGroup simulationGroup = simulation.gameObject.AddComponent<CanvasGroup>();
        simulationGroup.alpha = 0f; simulationGroup.interactable = false; simulationGroup.blocksRaycasts = false;
        Image introduction = ImageFixture(build.transform, "ChallengeTestIntroduction");
        introduction.rectTransform.anchoredPosition = new Vector2(180f, 0f);
        modal = Child(fixture.transform, "Pause modal fixture (not a HUD target)");

        Refresh(service, new[] { main, build });
        Canvas.ForceUpdateCanvases();
        Color32 shownPixel = CapturePixel(camera, target, "HUDShown", 136, 128);
        float rootInheritedAlpha = controls.canvasRenderer.GetInheritedAlpha();
        float childInheritedAlpha = escaped.canvasRenderer.GetInheritedAlpha();
        report.AppendLine($"DIAGNOSTIC: shown root alpha={rootInheritedAlpha:F4} depth={controls.depth} cull={controls.canvasRenderer.cull} raycast={RaycastHits(main, controls)}; ignore-parent alpha={childInheritedAlpha:F4} depth={escaped.depth} cull={escaped.canvasRenderer.cull} raycast={RaycastHits(main, escaped)}; sample=({shownPixel.r},{shownPixel.g},{shownPixel.b},{shownPixel.a}).");
        Check(Mathf.Abs(rootInheritedAlpha - .37f) < .001f &&
            (Mathf.Abs(childInheritedAlpha - .61f) < .001f || Mathf.Abs(childInheritedAlpha - .37f * .61f) < .001f) &&
            RaycastHits(main, controls) && RaycastHits(main, escaped) && shownPixel.a > 20,
            "The native unmasked alpha/raycast control fixture is not usable.");
        SetHidden(service, true);
        Color32 hiddenPixel = CapturePixel(camera, target, "HUDHidden", 136, 128);
        Check(hiddenPixel.a < 3, "The hidden HUD still produced pixels in its isolated render region.");
        SetHidden(service, false);
        Color32 restoredPixel = CapturePixel(camera, target, "HUDRestored", 136, 128);
        Check(restoredPixel.a > 20 && RaycastHits(main, controls) && RaycastHits(main, escaped),
            "Showing HUD failed to restore native pixels and raycasts.");
        report.AppendLine($"PASS: Actual isolated URP rendering: shown alpha {shownPixel.a}, hidden {hiddenPixel.a}, restored {restoredPixel.a}; screenshots saved to {Images}.");
        SetHidden(service, true);
        Check(Hidden(service), "The hide preference was not stored.");
        Check(controls.gameObject.GetComponents<CanvasGroup>().Length == 1 && escaped.gameObject.GetComponents<CanvasGroup>().Length == 1,
            "Hiding added or replaced an authored CanvasGroup.");
        Check(first.alpha == .37f && escapeGroup.alpha == .61f && first.interactable && first.blocksRaycasts && escapeGroup.ignoreParentGroups,
            "Hiding changed an existing CanvasGroup.");
        Check(controls.gameObject.activeSelf && controls.enabled && !inactive.gameObject.activeSelf, "Hiding changed activeSelf or Graphic.enabled.");
        Check(controls.canvasRenderer.cull && escaped.canvasRenderer.cull, "Native CanvasRenderer culling did not hide authored-group HUD or its ignoreParentGroups child.");
        Check(Mathf.Abs(controls.canvasRenderer.GetInheritedAlpha() - rootInheritedAlpha) < .001f &&
            Mathf.Abs(escaped.canvasRenderer.GetInheritedAlpha() - childInheritedAlpha) < .001f, "Recording culling modified inherited native alpha.");
        Check(!RaycastHits(main, controls) && !RaycastHits(main, escaped), "Hidden HUD still receives GraphicRaycaster clicks.");
        Check(!dialogue.canvasRenderer.cull && !utilityModal.canvasRenderer.cull,
            "Dialogue or an authored confirmation was treated as ordinary HUD.");
        Check(!registration.canvasRenderer.cull && !registrationConfirmation.canvasRenderer.cull && !mobileSettings.canvasRenderer.cull,
            "Bhan House's name registration, its confirmation or mobile settings was treated as ordinary HUD.");
        Check(welcome.canvasRenderer.cull, "Utility welcome HUD was not hidden.");
        Check(!introduction.canvasRenderer.cull, "The bridge-test introduction was hidden.");

        // The game is free to update these authored values while the recording mask is active.
        first.alpha = .53f; first.interactable = false; first.blocksRaycasts = false;
        escapeGroup.alpha = .42f; escapeGroup.interactable = false;
        controls.enabled = false;
        controls.gameObject.SetActive(false);
        inactive.gameObject.SetActive(true);
        GameObject clipOwner = Child(main.transform, "Late HUD with native RectMask2D");
        clipOwner.GetComponent<RectTransform>().sizeDelta = new Vector2(200f, 100f);
        clipOwner.AddComponent<RectMask2D>();
        Image late = ImageFixture(clipOwner.transform, "EmoteButton");
        late.rectTransform.sizeDelta = new Vector2(100f, 60f);
        Refresh(service, new[] { main, build });
        Canvas.ForceUpdateCanvases();
        Check(late.canvasRenderer.cull, "New HUD targets were not hidden during a rescan.");

        PushPanel(coordinator, modal);
        Invoke(service, "ApplyVisibility");
        Check(Hidden(service), "Opening a modal discarded the hide preference.");
        Check(!late.canvasRenderer.cull,
            "Recording masks did not suspend for a modal/expanded minimap.");
        ClearPanels(coordinator);
        Invoke(service, "ApplyVisibility");
        Check(late.canvasRenderer.cull, "Closing a modal did not restore recording HUD hiding.");

        // Exercise the actual registered canvas callbacks and native RectMask2D:
        // the child moves outside clipping while the recording preference stays hidden.
        int observedCycles = 0;
        bool firstRestoredCull = true;
        observePreRender = () =>
        {
            if (observedCycles++ == 0) firstRestoredCull = late.canvasRenderer.cull;
        };
        Canvas.preWillRenderCanvases += observePreRender; // Registered after service PrepareRendering.
        late.rectTransform.anchoredPosition = new Vector2(1000f, 0f);
        late.SetVerticesDirty();
        Canvas.ForceUpdateCanvases();
        Check(observedCycles > 0 && !firstRestoredCull && late.canvasRenderer.cull,
            "Native canvas pre/will callbacks did not restore natural visibility, apply current RectMask2D clipping and suppress the final draw.");
        Invoke(service, "ApplyVisibility"); // Reapplying must not capture our own forced cull.
        Canvas.preWillRenderCanvases -= observePreRender;
        observePreRender = null;

        float previousTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        Invoke(service, "ToggleHud");
        Check(!Hidden(service) && Time.timeScale == 0f, "Paused shortcut helpers failed or changed time scale.");
        Time.timeScale = previousTimeScale;
        Check(first.alpha == .53f && escapeGroup.alpha == .42f && !first.interactable && !escapeGroup.interactable && !first.blocksRaycasts && escapeGroup.blocksRaycasts,
            "Showing HUD overwrote authored changes made while hidden.");
        Check(!controls.gameObject.activeSelf && !controls.enabled && inactive.gameObject.activeSelf,
            "Showing HUD resurrected a control or undid an active/enabled change.");
        Check(simulationGroup.alpha == 0f && !simulationGroup.interactable && !simulationGroup.blocksRaycasts,
            "Showing recording HUD undid the simulation's separate hide state.");
        Check(controls.gameObject.GetComponents<CanvasGroup>().Length == 1 && escaped.gameObject.GetComponents<CanvasGroup>().Length == 1,
            "Recording added CanvasGroup components.");
        Check(late.canvasRenderer.cull, "Showing HUD resurrected naturally culled content after a render-cycle clipping update.");
        Check(!escaped.canvasRenderer.cull, "Showing HUD failed to restore naturally unculled content.");
        SetHidden(service, true);
        Invoke(service, "OnDisable");
        Check(first.alpha == .53f && escapeGroup.alpha == .42f && !escaped.canvasRenderer.cull && late.canvasRenderer.cull,
            "Disabling the shortcut service did not restore each renderer's latest natural cull state.");
        report.AppendLine("PASS: Native CanvasRenderer culling and GraphicRaycaster verify hidden controls without changing authored groups or inherited alpha, including ignoreParentGroups children; dialogue, Bhan House name registration/confirmation, mobile settings and bridge-test introduction are excluded. Exact active/enabled, authored-group and native cull values survive hide/show, simulation hiding, rescan, modal suspension, paused helper calls, pre/post-render clipping updates, repeated suppression and cleanup.");
        }
        finally
        {
            if (observePreRender != null) Canvas.preWillRenderCanvases -= observePreRender;
            if (subscribed) Invoke(service, "OnDisable");
            if (camera != null) camera.targetTexture = null;
            target.Release(); Object.DestroyImmediate(target);
            Object.DestroyImmediate(cameraOwner);
            if (main != null) Object.DestroyImmediate(main.gameObject);
            if (build != null) Object.DestroyImmediate(build.gameObject);
            if (modal != null) Object.DestroyImmediate(modal);
        }
    }

    private static void ValidateTextFocus(Type runtime, GameObject fixture, StringBuilder report)
    {
        var eventOwner = Child(fixture.transform, "Text focus EventSystem (temporary)");
        EventSystem events = eventOwner.AddComponent<EventSystem>();
        events.enabled = false;
        IList eventSystems = (IList)typeof(EventSystem).GetField("m_EventSystems", StaticMethods).GetValue(null);
        if (eventSystems.Contains(events)) eventSystems.Remove(events);
        eventSystems.Insert(0, events);
        var tmpOwner = Child(fixture.transform, "TMP chat/account input");
        TMP_InputField tmp = tmpOwner.AddComponent<TMP_InputField>();
        var tmpLabel = Child(tmpOwner.transform, "TMP field label");
        var legacyOwner = Child(fixture.transform, "Legacy account input");
        InputField legacy = legacyOwner.AddComponent<InputField>();
        var ordinary = Child(fixture.transform, "Ordinary HUD button");
        MethodInfo focused = runtime.GetMethod("IsTextInputFocused", StaticMethods);
        Check(focused != null, "Text input focus gate is missing.");
        events.SetSelectedGameObject(tmpOwner);
        SetInputFocus(tmp, true);
        Check((bool)focused.Invoke(null, null), "Selected TMP chat/name input did not suppress the shortcut.");
        events.SetSelectedGameObject(tmpLabel);
        SetInputFocus(tmp, true);
        Check((bool)focused.Invoke(null, null), "A selected child of a TMP input did not suppress the shortcut.");
        SetInputFocus(tmp, false);
        events.SetSelectedGameObject(legacyOwner);
        SetInputFocus(legacy, true);
        Check((bool)focused.Invoke(null, null), "Selected legacy input did not suppress the shortcut.");
        SetInputFocus(legacy, false);
        events.SetSelectedGameObject(ordinary);
        Check(!(bool)focused.Invoke(null, null), "A normal non-input selection incorrectly suppressed the shortcut.");
        events.SetSelectedGameObject(null);
        Check(!(bool)focused.Invoke(null, null), "No selected input incorrectly suppressed the shortcut.");
        report.AppendLine("PASS: TMP chat/account fields (including selected descendants) and legacy input fields suppress the shortcut; ordinary/no selection permits it.");
        eventSystems.Remove(events);
        Object.DestroyImmediate(eventOwner);
        Object.DestroyImmediate(tmpOwner);
        Object.DestroyImmediate(legacyOwner);
        Object.DestroyImmediate(ordinary);
    }

    private static void ValidateActualPrefab(Component service, StringBuilder report)
    {
        Check(File.Exists(ManagerPrefab), "The actual managers/UI prefab is missing.");
        Hash128 beforeFile = Hash128.Compute(File.ReadAllText(ManagerPrefab));
        GameObject contents = PrefabUtility.LoadPrefabContents(ManagerPrefab);
        try
        {
            var objects = contents.GetComponentsInChildren<Transform>(true).ToDictionary(node => node.gameObject, node => node.gameObject.activeSelf);
            var groups = contents.GetComponentsInChildren<CanvasGroup>(true).ToDictionary(group => group,
                group => new[] { group.alpha, group.interactable ? 1f : 0f, group.blocksRaycasts ? 1f : 0f, group.ignoreParentGroups ? 1f : 0f });
            var canvases = contents.GetComponentsInChildren<Canvas>(true);
            var enabled = canvases.ToDictionary(canvas => canvas, canvas => canvas.enabled);
            var renderers = contents.GetComponentsInChildren<CanvasRenderer>(true).ToDictionary(renderer => renderer, renderer => renderer.cull);
            int componentCount = contents.GetComponentsInChildren<Component>(true).Length;
            Refresh(service, canvases);
            SetHidden(service, true);
            var hudRoots = (IEnumerable<GameObject>)service.GetType().GetField("hudTargets", InstanceMethods).GetValue(service);
            int coveredBranches = 0, coveredRenderers = 0;
            foreach (GameObject hud in hudRoots)
            {
                CanvasRenderer[] branchRenderers = hud.GetComponentsInChildren<CanvasRenderer>(true);
                if (branchRenderers.Length == 0) continue;
                Check(branchRenderers.All(renderer => renderer.cull), "An actual HUD branch did not receive native renderer suppression: " + PathOf(hud.transform));
                coveredBranches++;
                coveredRenderers += branchRenderers.Length;
            }
            Check(coveredBranches >= 5, "The actual gameplay manager prefab has insufficient named-HUD coverage.");
            foreach (var snapshot in objects) Check(snapshot.Key.activeSelf == snapshot.Value, "Actual authored activeSelf changed: " + snapshot.Key.name);
            foreach (var snapshot in enabled) Check(snapshot.Key.enabled == snapshot.Value, "Actual Canvas enabled state changed: " + snapshot.Key.name);
            foreach (var snapshot in groups) Check(EqualGroup(snapshot.Key, snapshot.Value), "Actual authored CanvasGroup changed: " + snapshot.Key.name);
            Check(contents.GetComponentsInChildren<Component>(true).Length == componentCount, "Recording added components to actual authored UI.");
            SetHidden(service, false);
            foreach (var snapshot in renderers) Check(snapshot.Key.cull == snapshot.Value, "Actual renderer's natural cull state was not restored: " + snapshot.Key.name);
            foreach (var snapshot in groups) Check(EqualGroup(snapshot.Key, snapshot.Value), "Actual authored CanvasGroup was not preserved on show: " + snapshot.Key.name);
            report.AppendLine("PASS: Actual managers/UI prefab: " + coveredBranches + " named HUD branches / " + coveredRenderers + " CanvasRenderers checked; original activeSelf, Canvas.enabled, CanvasGroup and natural cull flags unchanged on restore; no component or hierarchy authoring and no prefab save.");
        }
        finally { PrefabUtility.UnloadPrefabContents(contents); }
        Check(Hash128.Compute(File.ReadAllText(ManagerPrefab)) == beforeFile, "The source UI prefab file was changed.");
    }

    private static bool EqualGroup(CanvasGroup group, float[] snapshot) => group.alpha == snapshot[0] &&
        group.interactable == (snapshot[1] > 0f) && group.blocksRaycasts == (snapshot[2] > 0f) && group.ignoreParentGroups == (snapshot[3] > 0f);
    private static void SetInputFocus(Component field, bool focused)
    {
        // These isolated blank fields deliberately have no shared TMP font/text
        // asset. Use the same native focus flag read by isFocused without doing
        // text generation, opening a keyboard or changing a player's input.
        FieldInfo flag = field.GetType().GetField("m_AllowInput", InstanceMethods);
        Check(flag != null, "An input field's focus fixture flag is unavailable.");
        flag.SetValue(field, focused);
    }
    private static void PushPanel(UIPanelCoordinator coordinator, GameObject panel)
    {
        Type frameType = typeof(UIPanelCoordinator).GetNestedType("PanelFrame", BindingFlags.NonPublic);
        object frame = Activator.CreateInstance(frameType, true);
        frameType.GetField("panel", InstanceMethods).SetValue(frame, panel);
        object stack = typeof(UIPanelCoordinator).GetField("panelStack", InstanceMethods).GetValue(coordinator);
        stack.GetType().GetMethod("Push").Invoke(stack, new[] { frame });
    }
    private static void ClearPanels(UIPanelCoordinator coordinator)
    {
        object stack = typeof(UIPanelCoordinator).GetField("panelStack", InstanceMethods).GetValue(coordinator);
        stack.GetType().GetMethod("Clear").Invoke(stack, null);
    }
    private static bool RaycastHits(Canvas canvas, Image target)
    {
        var raycaster = canvas.GetComponent<GraphicRaycaster>();
        Vector3[] corners = new Vector3[4];
        target.rectTransform.GetWorldCorners(corners);
        Vector2 point = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, (corners[0] + corners[2]) * .5f);
        var hits = new List<RaycastResult>();
        var pointer = new PointerEventData(EventSystem.current) { position = point };
        raycaster.Raycast(pointer, hits);
        return hits.Any(hit => hit.gameObject == target.gameObject);
    }
    private static Canvas CanvasFixture(Transform parent, string name, Camera camera)
    {
        GameObject owner = Child(parent, name);
        Canvas canvas = owner.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = camera;
        owner.GetComponent<RectTransform>().sizeDelta = new Vector2(512f, 256f);
        owner.AddComponent<GraphicRaycaster>();
        return canvas;
    }
    private static Image ImageFixture(Transform parent, string name)
    {
        var owner = Child(parent, name);
        RectTransform rect = owner.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.sizeDelta = new Vector2(300f, 100f);
        rect.anchoredPosition = Vector2.zero;
        Image image = owner.AddComponent<Image>();
        image.color = Color.white;
        image.raycastTarget = true;
        return image;
    }
    private static GameObject Child(Transform parent, string name)
    {
        var child = new GameObject(name, typeof(RectTransform));
        child.layer = 5;
        SceneManager.MoveGameObjectToScene(child, parent.gameObject.scene);
        child.transform.SetParent(parent, false);
        return child;
    }
    private static Color32 CapturePixel(Camera camera, RenderTexture target, string label, int x, int y)
    {
        Canvas.ForceUpdateCanvases();
        var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
        if (RenderPipeline.SupportsRenderRequest(camera, request)) RenderPipeline.SubmitRenderRequest(camera, request);
        else camera.Render();
        RenderTexture previous = RenderTexture.active;
        Texture2D pixels = null;
        try
        {
            RenderTexture.active = target;
            pixels = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
            Directory.CreateDirectory(Images);
            File.WriteAllBytes(Images + "/" + label + ".png", pixels.EncodeToPNG());
            return pixels.GetPixels32()[y * target.width + x];
        }
        finally
        {
            RenderTexture.active = previous;
            if (pixels != null) Object.DestroyImmediate(pixels);
        }
    }
    private static void Refresh(Component service, Canvas[] canvases)
    {
        MethodInfo method = service.GetType().GetMethod("RefreshHudTargets", InstanceMethods, null, new[] { typeof(Canvas[]) }, null);
        Check(method != null, "The isolated Canvas discovery overload is missing.");
        method.Invoke(service, new object[] { canvases });
    }
    private static void SetHidden(Component service, bool value) => Invoke(service, "SetHudHidden", value);
    private static bool Hidden(Component service) => (bool)service.GetType().GetProperty("HudHidden", InstanceMethods).GetValue(service);
    private static object Invoke(Component target, string name, params object[] arguments)
    {
        MethodInfo method = target.GetType().GetMethod(name, InstanceMethods);
        Check(method != null, "Required method is missing: " + name);
        return method.Invoke(target, arguments.Length == 0 ? null : arguments);
    }
    private static string PathOf(Transform node) => node.parent != null ? PathOf(node.parent) + "/" + node.name : node.name;
    private static Exception Unwrap(Exception error) => error is TargetInvocationException wrapped && wrapped.InnerException != null ? wrapped.InnerException : error;
    private static void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
}
#endif
