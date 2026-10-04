using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Fusion;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Opt-in authored guest exit controls. Does not join/leave a room or touch player saves.</summary>
public static class WorldMultiplayerGuestLeaveAuthoring
{
    private const string ScenePath = "Assets/Scenes/CanyonCrossing.unity";
    private const string Request = "Temp/world-guest-leave-ui-v1.request";
    private const string Report = "Temp/WorldGuestLeaveUIValidation.txt";
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private static double nextCheck;

    [InitializeOnLoadMethod]
    private static void Watch() { EditorApplication.update -= CheckRequest; EditorApplication.update += CheckRequest; }
    private static void CheckRequest()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (SceneManager.GetSceneByPath(ScenePath).isDirty)
        { File.WriteAllText(Report, "WAIT: Save CanyonCrossing scene edits first. Nothing overwritten."); return; }
        File.Delete(Request);
        try { Author(); }
        catch (Exception error) { File.WriteAllText(Report, "FAIL: " + error); Debug.LogException(error); }
    }

    [MenuItem("Tools/Civil Craft/Add Guest Leave Multiplayer Controls")]
    public static void Author()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetSceneByPath(ScenePath).isDirty)
            throw new InvalidOperationException("Stop Play Mode and save CanyonCrossing edits first.");
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        var report = new StringBuilder();
        try
        {
            WorldMultiplayerPanelUI ui = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<WorldMultiplayerPanelUI>(true)).Single();
            GameObject panel = Get<GameObject>(ui, "panel");
            var existingActions = panel.GetComponentsInChildren<Button>(true).ToDictionary(button => button, button => JsonUtility.ToJson(button.onClick));
            StyleControls(ui);
            foreach (var action in existingActions)
                if (action.Key.name != "Leave World Multiplayer" && !action.Key.transform.IsChildOf(Get<GameObject>(ui, "leaveConfirmation").transform))
                    Check(JsonUtility.ToJson(action.Key.onClick) == action.Value, "An existing host/close button action changed.");
            CheckAction(Get<Button>(ui, "leaveButton"), ui, "RequestLeave");
            CheckAction(Get<Button>(ui, "confirmLeaveButton"), ui, "ConfirmLeave");
            CheckAction(Get<GameObject>(ui, "leaveConfirmation").GetComponentsInChildren<Button>(true).Single(button => button.name == "Stay In Multiplayer"), ui, "CancelLeave");
            ValidatePreview(ui, report);
            EditorUtility.SetDirty(ui); EditorSceneManager.MarkSceneDirty(scene);
            Check(EditorSceneManager.SaveScene(scene), "Could not save authored guest exit controls.");
            Scene fixtureScene = EditorSceneManager.NewPreviewScene();
            try
            {
                var fixture = new GameObject("Guest Leave Checks (Temporary)"); fixture.SetActive(false);
                SceneManager.MoveGameObjectToScene(fixture, fixtureScene);
                WorldMultiplayerPanelValidation.Run(fixture.transform, report);
            }
            finally { EditorSceneManager.ClosePreviewScene(fixtureScene); }
            report.AppendLine("PASS: Saved guest-only Leave Multiplayer and Leave/Stay confirmation using existing red action, cream panel, wood frame and Bekind font. Host controls/callbacks unchanged.");
            report.AppendLine("NOTE: No Photon session or save was modified. Live host/guest departure still needs a two-player check.");
            File.WriteAllText(Report, report.ToString());
            Debug.Log("[Guest multiplayer leave] Authored controls and isolated role/layout checks passed.");
        }
        finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
    }

    private static void StyleControls(WorldMultiplayerPanelUI ui)
    {
        Button leave = Get<Button>(ui, "leaveButton");
        if (leave == null)
        {
            Button original = Get<Button>(ui, "turnOffButton");
            leave = Object.Instantiate(original, original.transform.parent, false);
        }
        leave.name = "Leave World Multiplayer";
        TMP_Text label = leave.GetComponentInChildren<TMP_Text>(true); label.name = "Guest Leave Label"; label.text = "LEAVE MULTIPLAYER";
        Wire(leave, ui.RequestLeave); leave.gameObject.SetActive(false);

        GameObject confirmation = Get<GameObject>(ui, "leaveConfirmation");
        if (confirmation == null)
        {
            GameObject original = Get<GameObject>(ui, "turnOffConfirmation");
            confirmation = Object.Instantiate(original, original.transform.parent, false);
            foreach (Transform child in confirmation.GetComponentsInChildren<Transform>(true))
                child.name = child.name.Replace("Turn Off", "Guest Leave");
        }
        confirmation.name = "WorldLeaveConfirmation"; confirmation.transform.SetAsLastSibling();
        foreach (TMP_Text text in confirmation.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text.name == "Guest Leave Confirmation Title") text.text = "LEAVE MULTIPLAYER?";
            if (text.name == "Guest Leave Confirmation Message")
                text.text = "Return to <b>Mode Selection</b>?\n\nThe host's room <b>stays open</b>.\nYour own progress stays unchanged.";
        }
        Button confirm = confirmation.GetComponentsInChildren<Button>(true).Single(button => button.name == "Confirm Guest Leave Multiplayer" || button.name == "Confirm Leave Multiplayer");
        Button stay = confirmation.GetComponentsInChildren<Button>(true).Single(button => button.name == "Cancel Guest Leave Multiplayer" || button.name == "Stay In Multiplayer");
        confirm.name = "Confirm Leave Multiplayer"; stay.name = "Stay In Multiplayer";
        TMP_Text confirmLabel = confirm.GetComponentInChildren<TMP_Text>(true); confirmLabel.name = "Guest Confirm Leave Label"; confirmLabel.text = "LEAVE";
        TMP_Text stayLabel = stay.GetComponentInChildren<TMP_Text>(true); stayLabel.name = "Guest Stay Label"; stayLabel.text = "STAY";
        Wire(confirm, ui.ConfirmLeave); Wire(stay, ui.CancelLeave); confirmation.SetActive(false);
        var serialized = new SerializedObject(ui);
        serialized.FindProperty("leaveButton").objectReferenceValue = leave;
        serialized.FindProperty("leaveLabel").objectReferenceValue = label;
        serialized.FindProperty("leaveConfirmation").objectReferenceValue = confirmation;
        serialized.FindProperty("confirmLeaveButton").objectReferenceValue = confirm;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void Wire(Button button, UnityAction action)
    {
        for (int index = button.onClick.GetPersistentEventCount() - 1; index >= 0; index--)
            UnityEventTools.RemovePersistentListener(button.onClick, index);
        UnityEventTools.AddPersistentListener(button.onClick, action);
    }
    private static void CheckAction(Button button, WorldMultiplayerPanelUI ui, string method) =>
        Check(button.onClick.GetPersistentEventCount() == 1 && button.onClick.GetPersistentTarget(0) == ui && button.onClick.GetPersistentMethodName(0) == method,
            "Guest exit action has a stale host callback: " + method);
    private static T Get<T>(WorldMultiplayerPanelUI ui, string field) where T : Object => (T)typeof(WorldMultiplayerPanelUI).GetField(field, Fields).GetValue(ui);

    private static void ValidatePreview(WorldMultiplayerPanelUI source, StringBuilder report)
    {
        Scene scene = EditorSceneManager.NewPreviewScene(); RenderTexture target = null;
        try
        {
            var canvasObject = new GameObject("Guest Multiplayer Preview (Temporary)", typeof(RectTransform), typeof(Canvas));
            SceneManager.MoveGameObjectToScene(canvasObject, scene);
            Canvas canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
            var controllerObject = new GameObject("Guest Multiplayer Preview Controller (Temporary)"); controllerObject.SetActive(false);
            SceneManager.MoveGameObjectToScene(controllerObject, scene);
            WorldMultiplayerPanelUI controller = controllerObject.AddComponent<WorldMultiplayerPanelUI>();
            GameObject sourcePanel = Get<GameObject>(source, "panel");
            GameObject panel = Object.Instantiate(sourcePanel, canvasObject.transform, false);
            foreach (FieldInfo field in typeof(WorldMultiplayerPanelUI).GetFields(Fields))
            {
                if (!field.IsDefined(typeof(SerializeField), false) || field.Name == "openButton") continue;
                Object original = (Object)field.GetValue(source);
                if (field.Name == "panel") { field.SetValue(controller, panel); continue; }
                Transform originalTransform = original is GameObject obj ? obj.transform : ((Component)original).transform;
                Transform copy = panel.transform.Find(AnimationUtility.CalculateTransformPath(originalTransform, sourcePanel.transform));
                field.SetValue(controller, original is GameObject ? (Object)copy.gameObject : copy.GetComponent(field.FieldType));
            }
            foreach (Transform node in canvasObject.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = 30;
            var cameraObject = new GameObject("Guest Multiplayer Preview Camera (Temporary)", typeof(Camera)); SceneManager.MoveGameObjectToScene(cameraObject, scene);
            Camera camera = cameraObject.GetComponent<Camera>(); camera.scene = scene; camera.enabled = false;
            camera.orthographic = true; camera.transform.position = new Vector3(0, 0, -1000);
            camera.nearClipPlane = .1f; camera.farClipPlane = 2000; camera.cullingMask = 1 << 30;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(148, 113, 85, 255); canvas.worldCamera = camera;
            MethodInfo apply = typeof(WorldMultiplayerPanelUI).GetMethod("ApplySessionView", Fields);
            foreach (Vector2Int size in new[] { new Vector2Int(1920, 1080), new Vector2Int(1920, 1200), new Vector2Int(2340, 1080) })
            {
                canvasRect.sizeDelta = size; camera.orthographicSize = size.y / 2f; camera.aspect = (float)size.x / size.y;
                target = new RenderTexture(size.x * 2 / 3, size.y * 2 / 3, 24); target.Create(); camera.targetTexture = target;
                panel.SetActive(true);
                apply.Invoke(controller, new object[] { false, true, false, false, PlayerRef.None, null, null, "" });
                Fit(panel); Capture(camera, target, "Temp/WorldGuestPanel_" + size.x + "x" + size.y + ".png");
                Get<GameObject>(controller, "leaveConfirmation").SetActive(true);
                Fit(panel); Capture(camera, target, "Temp/WorldGuestLeaveConfirmation_" + size.x + "x" + size.y + ".png");
                controller.CancelLeave(); Release(target); target = null;
                report.AppendLine("PASS: Guest panel and Leave/Stay confirmation fit " + size + ".");
            }
        }
        finally { Release(target); EditorSceneManager.ClosePreviewScene(scene); }
    }
    private static void Fit(GameObject root)
    {
        Canvas.ForceUpdateCanvases();
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>())
        { text.ForceMeshUpdate(); Check(!text.isTextOverflowing || text.overflowMode == TextOverflowModes.Ellipsis, "Guest session text overflows: " + text.name); }
    }
    private static void Capture(Camera camera, RenderTexture target, string path)
    {
        Canvas.ForceUpdateCanvases();
        if (GraphicsSettings.currentRenderPipeline == null) camera.Render();
        else RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        RenderTexture previous = RenderTexture.active; var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        try { RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply(); File.WriteAllBytes(path, image.EncodeToPNG()); }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
    }
    private static void Release(RenderTexture target) { if (target != null) { target.Release(); Object.DestroyImmediate(target); } }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
