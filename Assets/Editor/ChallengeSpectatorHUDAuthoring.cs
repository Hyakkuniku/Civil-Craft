using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Opt-in, scene-authored spectator readouts and isolated native checks. Never starts a session.</summary>
public static class ChallengeSpectatorHUDAuthoring
{
    private const string ScenePath = "Assets/Scenes/CanyonCrossing.unity";
    private const string Request = "Temp/ChallengeSpectatorHUDAuthoring.request";
    private const string Report = "Temp/ChallengeSpectatorHUDValidation.txt";
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static double nextCheck;
    private static string lastWait;

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
        string wait = WaitReason();
        if (wait != null)
        {
            if (lastWait != wait) File.WriteAllText(Report, "WAIT: " + wait + " Request retained; no scene was changed.");
            lastWait = wait;
            return;
        }
        lastWait = null;
        File.Delete(Request);
        try { Author(); }
        catch (Exception error)
        {
            File.WriteAllText(Report, "FAIL: " + error);
            Debug.LogException(error);
        }
    }

    private static string WaitReason()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "Stop Play Mode first.";
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer)
            return "Waiting for compilation, import or player build to finish.";
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        if (scene.IsValid() && scene.isLoaded && scene.isDirty) return "Save CanyonCrossing scene edits first.";
        return null;
    }

    [MenuItem("Tools/Civil Craft/Author Challenge Spectator HUD")]
    public static void Author()
    {
        string wait = WaitReason();
        if (wait != null) throw new InvalidOperationException(wait);
        Scene active = SceneManager.GetActiveScene();
        SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup();
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool alreadyRegistered = scene.IsValid();
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        var report = new StringBuilder();
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Author Challenge Spectator HUD");
        bool saved = false;
        try
        {
            Transform build = All<Canvas>(scene).Single(canvas => canvas.name == "BuildCanvas").transform;
            MultiplayerChallengeLobbyUI ui = All<MultiplayerChallengeLobbyUI>(scene).Single();
            MultiplayerHUDVisibility visibility = All<MultiplayerHUDVisibility>(scene).Single();
            var callbacks = All<Button>(scene).ToDictionary(button => button, button => JsonUtility.ToJson(button.onClick));
            var data = new SerializedObject(ui);
            GameObject status = (GameObject)Property(data, "submissionStatusPanel").objectReferenceValue;
            Check(status != null && status.transform.parent == build, "The authored challenge card is missing from BuildCanvas.");
            Image frameStyle = status.GetComponent<Image>();
            Image creamStyle = status.GetComponentsInChildren<Image>(true)
                .First(image => image.transform != status.transform && image.color.r > .9f);
            TMP_Text fontStyle = (TMP_Text)Property(data, "submissionStatusHeader").objectReferenceValue;
            Check(frameStyle != null && frameStyle.sprite != null && fontStyle != null && fontStyle.font != null,
                "Challenge card frame, rounded sprite or font is missing.");

            // The loaded target is known to be saved. Keep synchronous snapshots
            // so failed native validation cannot strand a partial scene edit.
            Undo.RegisterFullObjectHierarchyUndo(build.gameObject, "Author Challenge Spectator HUD");
            Undo.RegisterCompleteObjectUndo(ui, "Wire Challenge Owner HUD");
            Undo.RegisterCompleteObjectUndo(visibility, "Wire Challenge Stress HUD");
            GameObject owner = AuthoredChild(build, "ChallengeTestOwnerHUD", typeof(Image));
            var ownerRect = (RectTransform)owner.transform;
            ownerRect.anchorMin = ownerRect.anchorMax = new Vector2(1f, .75f);
            ownerRect.pivot = new Vector2(1f, .5f);
            ownerRect.anchoredPosition = new Vector2(-28f, 0f);
            ownerRect.sizeDelta = new Vector2(320f, 106f);
            ownerRect.localScale = Vector3.one;
            ownerRect.localRotation = Quaternion.identity;
            CopyImage(frameStyle, owner.GetComponent<Image>());
            GameObject body = AuthoredChild(owner.transform, "Cream Inset", typeof(Image));
            Stretch((RectTransform)body.transform, new Vector2(6f, 6f), new Vector2(-6f, -6f));
            CopyImage(creamStyle, body.GetComponent<Image>());
            TMP_Text label = Text(body.transform, "Now Testing", fontStyle);
            Place(label.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(.5f, 1f),
                new Vector2(0f, -10f), new Vector2(-24f, 22f));
            label.text = "NOW TESTING";
            label.color = frameStyle.color;
            label.fontStyle = FontStyles.Bold;
            Fit(label, 18f, 16f);
            TMP_Text name = Text(body.transform, "Engineer Name", fontStyle);
            Place(name.rectTransform, Vector2.zero, Vector2.one, Vector2.one * .5f,
                new Vector2(0f, -15f), new Vector2(-24f, -36f));
            name.text = "<b><color=#79500F>Engineer</color></b>";
            name.color = fontStyle.color;
            Fit(name, 30f, 12f);
            owner.SetActive(false);
            Property(data, "testOwnerPanel").objectReferenceValue = owner;
            Property(data, "testOwnerName").objectReferenceValue = name;
            data.ApplyModifiedPropertiesWithoutUndo();

            Transform stress = build.Find("StressLevelVisualizer");
            Check(stress != null, "The authored bridge stress visualizer is missing.");
            CanvasGroup stressGroup = stress.GetComponent<CanvasGroup>();
            Check(stressGroup != null, "The stress visualizer must use its existing authored CanvasGroup.");
            var gates = new SerializedObject(visibility);
            Property(gates, "testStressGroup").objectReferenceValue = stressGroup;
            SerializedProperty hidden = Property(gates, "testHiddenGroups");
            Check(Enumerable.Range(0, hidden.arraySize).Any(index =>
                hidden.GetArrayElementAtIndex(index).objectReferenceValue == stressGroup),
                "Keep the stress CanvasGroup in the test mask so it stays hidden during the introduction.");
            gates.ApplyModifiedPropertiesWithoutUndo();

            ValidateAuthored(build, ui, visibility, owner, name, stressGroup, frameStyle, creamStyle, fontStyle, report);
            ValidateNative(owner, stress.gameObject, report);
            foreach (var callback in callbacks)
                Check(JsonUtility.ToJson(callback.Key.onClick) == callback.Value, "An existing button handler changed.");
            report.AppendLine("PASS: Existing scene button handlers preserved; runtime refresh changes only text and visibility on authored UI.");
            EditorSceneManager.MarkSceneDirty(scene);
            Check(EditorSceneManager.SaveScene(scene), "Could not save the authored CanyonCrossing readouts.");
            saved = true;
            Undo.CollapseUndoOperations(undoGroup);
            report.AppendLine("PASS: Saved CanyonCrossing only. The owner card is a BuildCanvas sibling and the existing stress readout is wired for every simulation viewer.");
            report.AppendLine("NOTE: Live host/guest playback still requires a two-player play test; isolated checks do not create a Photon room.");
        }
        catch
        {
            if (!saved) Undo.RevertAllDownToGroup(undoGroup);
            throw;
        }
        finally
        {
            if (opened) EditorSceneManager.CloseScene(scene, !alreadyRegistered);
            if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
        }
        SceneSetup[] after = EditorSceneManager.GetSceneManagerSetup();
        Check(setup.Length == after.Length && setup.Zip(after, (a, b) =>
            a.path == b.path && a.isLoaded == b.isLoaded && a.isActive == b.isActive).All(equal => equal),
            "Authoring changed the open scene setup.");
        report.AppendLine("PASS: Original open scenes and active scene restored; unrelated scenes were not saved.");
        File.WriteAllText(Report, report.ToString());
        Debug.Log("[Challenge spectator HUD] Authored references, owner state, stress visibility and layout passed. Report: " + Report);
    }

    private static IEnumerable<T> All<T>(Scene scene) where T : Component =>
        scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true));

    private static SerializedProperty Property(SerializedObject data, string name)
    {
        SerializedProperty property = data.FindProperty(name);
        Check(property != null, "The runtime field is not compiled yet: " + name);
        return property;
    }

    private static GameObject Child(Transform parent, string name, params Type[] types)
    {
        Transform existing = parent.Find(name);
        var child = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        if (child.scene != parent.gameObject.scene) SceneManager.MoveGameObjectToScene(child, parent.gameObject.scene);
        child.transform.SetParent(parent, false);
        child.layer = parent.gameObject.layer;
        foreach (Type type in types) if (child.GetComponent(type) == null) child.AddComponent(type);
        return child;
    }

    private static GameObject AuthoredChild(Transform parent, string name, params Type[] types)
    {
        Transform existing = parent.Find(name);
        GameObject child = existing != null ? existing.gameObject : Child(parent, name);
        if (existing == null) Undo.RegisterCreatedObjectUndo(child, "Create Challenge Owner HUD");
        foreach (Type type in types) if (child.GetComponent(type) == null) Undo.AddComponent(child, type);
        return child;
    }

    private static TMP_Text Text(Transform parent, string name, TMP_Text style)
    {
        TMP_Text text = AuthoredChild(parent, name, typeof(TextMeshProUGUI)).GetComponent<TMP_Text>();
        text.font = style.font;
        text.fontSharedMaterial = style.fontSharedMaterial;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        text.richText = true;
        text.enableWordWrapping = true;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.margin = Vector4.zero;
        return text;
    }

    private static void CopyImage(Image style, Image image)
    {
        image.sprite = style.sprite;
        image.type = style.type;
        image.color = style.color;
        image.material = style.material;
        image.pixelsPerUnitMultiplier = style.pixelsPerUnitMultiplier;
        image.raycastTarget = false;
    }

    private static void Fit(TMP_Text text, float maximum, float minimum)
    {
        text.fontSize = text.fontSizeMax = maximum;
        text.fontSizeMin = minimum;
        text.enableAutoSizing = true;
    }

    private static void Stretch(RectTransform rect, Vector2 minimum, Vector2 maximum)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.pivot = Vector2.one * .5f;
        rect.offsetMin = minimum; rect.offsetMax = maximum;
        rect.localScale = Vector3.one; rect.localRotation = Quaternion.identity;
    }

    private static void Place(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position, Vector2 size)
    {
        rect.anchorMin = anchorMin; rect.anchorMax = anchorMax; rect.pivot = pivot;
        rect.anchoredPosition = position; rect.sizeDelta = size;
        rect.localScale = Vector3.one; rect.localRotation = Quaternion.identity;
    }

    private static void ValidateAuthored(Transform build, MultiplayerChallengeLobbyUI ui, MultiplayerHUDVisibility visibility,
        GameObject owner, TMP_Text name, CanvasGroup stress, Image frameStyle, Image creamStyle, TMP_Text fontStyle, StringBuilder report)
    {
        var data = new SerializedObject(ui);
        Check(Property(data, "testOwnerPanel").objectReferenceValue == owner && Property(data, "testOwnerName").objectReferenceValue == name,
            "The owner HUD references are not authored.");
        Check(owner.transform.parent == build && !owner.activeSelf && build.Find("ChallengeTestIntroduction") != null,
            "The owner HUD must start hidden beside the existing introduction.");
        Image frame = owner.GetComponent<Image>(), body = owner.transform.Find("Cream Inset").GetComponent<Image>();
        Check(frame.sprite == frameStyle.sprite && frame.color == frameStyle.color &&
            body.sprite == creamStyle.sprite && body.color == creamStyle.color && name.font == fontStyle.font,
            "The owner HUD differs from the authored rounded cream/brown challenge theme.");
        Check(owner.GetComponentsInChildren<Graphic>(true).All(graphic => !graphic.raycastTarget) &&
            owner.GetComponentsInChildren<Button>(true).Length == 0, "The read-only owner card blocks gameplay input.");
        var gates = new SerializedObject(visibility);
        Check(Property(gates, "testStressGroup").objectReferenceValue == stress, "The live stress readout is not wired.");
        SerializedProperty hidden = Property(gates, "testHiddenGroups");
        for (int index = 0; index < hidden.arraySize; index++)
        {
            var group = hidden.GetArrayElementAtIndex(index).objectReferenceValue as CanvasGroup;
            Check(group == null || (group.transform != owner.transform && !owner.transform.IsChildOf(group.transform)),
                "The test mask hides the owner HUD through an ancestor.");
        }
        report.AppendLine("PASS: Authored right-side owner references, rounded cream/brown card, matching font, no raycast interception and stress CanvasGroup exception.");
    }

    private static void ValidateNative(GameObject authoredOwner, GameObject authoredStress, StringBuilder report)
    {
        Scene preview = EditorSceneManager.NewPreviewScene();
        RenderTexture target = null;
        try
        {
            var canvasObject = new GameObject("BuildCanvas", typeof(RectTransform), typeof(Canvas));
            SceneManager.MoveGameObjectToScene(canvasObject, preview);
            var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            var owner = Object.Instantiate(authoredOwner, canvasObject.transform, false);
            var stress = Object.Instantiate(authoredStress, canvasObject.transform, false);
            owner.name = "ChallengeTestOwnerHUD"; stress.name = "StressLevelVisualizer";
            stress.SetActive(true);
            foreach (Transform node in canvasObject.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = 30;
            var serviceObject = new GameObject("Owner Presentation Check (Temporary)");
            serviceObject.SetActive(false); SceneManager.MoveGameObjectToScene(serviceObject, preview);
            var ui = serviceObject.AddComponent<MultiplayerChallengeLobbyUI>();
            TMP_Text name = owner.GetComponentsInChildren<TMP_Text>(true).Single(text => text.name == "Engineer Name");
            Set(ui, "testOwnerPanel", owner); Set(ui, "testOwnerName", name);
            ValidateOwnerStates(ui, owner, name, report);
            ValidateStressVisibility(serviceObject.transform, report);
            Type stressValidation = typeof(ChallengeSpectatorHUDAuthoring).Assembly.GetType("ChallengeSpectatorStressValidation");
            Check(stressValidation != null, "The spectator stress validation is not compiled yet.");
            stressValidation.GetMethod("Run", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { serviceObject.transform, report });

            var cameraObject = new GameObject("Spectator HUD Camera (Temporary)", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, preview);
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.scene = preview; camera.enabled = false; camera.orthographic = true;
            camera.transform.position = new Vector3(0f, 0f, -1000f);
            camera.nearClipPlane = .1f; camera.farClipPlane = 2000f;
            camera.cullingMask = 1 << 30; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.12f, .19f, .27f);
            camera.allowHDR = false; camera.allowMSAA = false; canvas.worldCamera = camera;
            var state = new MultiplayerChallengeState { Phase = MultiplayerChallengePhase.Testing, TestIndex = 1 };
            foreach (int height in new[] { 1080, 1200 })
            {
                var rect = canvasObject.GetComponent<RectTransform>(); rect.sizeDelta = new Vector2(1920f, height);
                camera.orthographicSize = height * .5f; camera.aspect = 1920f / height;
                Refresh(ui, state, new string('W', 32));
                Canvas.ForceUpdateCanvases();
                foreach (TMP_Text text in owner.GetComponentsInChildren<TMP_Text>())
                {
                    text.ForceMeshUpdate();
                    Check(!text.isTextOverflowing && text.textInfo.characterCount > 0, "Owner name or label overflows at " + height + "px.");
                }
                Bounds card = RectTransformUtility.CalculateRelativeRectTransformBounds(rect, (RectTransform)owner.transform);
                Bounds gauge = RectTransformUtility.CalculateRelativeRectTransformBounds(rect, (RectTransform)stress.transform);
                Check(card.max.x <= rect.rect.xMax && card.min.x > rect.rect.xMin && card.min.y > rect.rect.yMin && card.max.y < rect.rect.yMax,
                    "Owner card falls outside the screen at " + height + "px.");
                Check(Mathf.Abs(rect.rect.xMax - card.max.x - 28f) < .1f && Mathf.Abs(card.center.y - height * .25f) < .1f,
                    "Owner card is not at the authored right-side margin and vertical position.");
                Check(!card.Intersects(gauge) && gauge.min.x >= rect.rect.xMin && gauge.max.x <= rect.rect.xMax &&
                    gauge.min.y >= rect.rect.yMin && gauge.max.y <= rect.rect.yMax, "Owner card obscures the stress readout.");
                Refresh(ui, state, "Alexandra Engineer");
                target = new RenderTexture(1280, Mathf.RoundToInt(1280f * height / 1920f), 24, RenderTextureFormat.ARGB32);
                target.Create(); camera.targetTexture = target;
                Capture(camera, target, "Temp/ChallengeSpectatorHUD_" + height + ".png");
                camera.targetTexture = null; target.Release(); Object.DestroyImmediate(target); target = null;
                report.AppendLine("PASS: Native " + (height == 1080 ? "16:9" : "16:10") + " layout, 32-character name fit, right margin, stress separation; preview captured.");
            }
        }
        finally
        {
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    private static void ValidateOwnerStates(MultiplayerChallengeLobbyUI ui, GameObject owner, TMP_Text name, StringBuilder report)
    {
        int objects = owner.GetComponentsInChildren<Transform>(true).Length;
        int components = owner.GetComponentsInChildren<Component>(true).Length;
        var state = new MultiplayerChallengeState { HostSubmissionOrder = 2, GuestSubmissionOrder = 1 };
        foreach (MultiplayerChallengePhase phase in Enum.GetValues(typeof(MultiplayerChallengePhase)))
        {
            state.Phase = phase; state.TestIndex = 1; Refresh(ui, state, "Guest Engineer");
            Check(owner.activeSelf == (phase == MultiplayerChallengePhase.Testing), "Owner card remains visible in " + phase + ".");
        }
        state.Phase = MultiplayerChallengePhase.Testing;
        foreach (int index in new[] { 1, 2 })
        {
            state.TestIndex = index;
            string expected = MultiplayerChallengeRules.TestsHostBridge(state) ? "Host Engineer" : "Guest Engineer";
            Check(expected == (index == 1 ? "Guest Engineer" : "Host Engineer"), "Guest-first submission mapping changed.");
            Refresh(ui, state, expected);
            Check(owner.activeSelf && name.text.Contains(expected) && name.text.Contains("#79500F"), "Owner name or gold emphasis is wrong.");
        }
        state.TestIndex = 1;
        Refresh(ui, state, "<size=200%>Engineer</size>");
        Check(!name.text.Contains("<size=") && name.text.Contains("‹size=200%›"), "A player name injects TMP markup.");
        Refresh(ui, state, null); Check(name.text.Contains("Engineer"), "Empty names lose the readable fallback.");
        foreach (int index in new[] { 0, 3 })
        {
            state.TestIndex = index; Refresh(ui, state, "Invalid");
            Check(!owner.activeSelf && name.text == string.Empty, "Invalid test indices show a stale owner.");
        }
        Check(owner.GetComponentsInChildren<Transform>(true).Length == objects && owner.GetComponentsInChildren<Component>(true).Length == components,
            "Refreshing the owner card creates runtime UI.");
        report.AppendLine("PASS: Production owner refresh hides outside Testing, accepts test indices 1/2, follows guest-first order, escapes markup, clears stale names and creates no UI.");
    }

    private static void ValidateStressVisibility(Transform fixture, StringBuilder report)
    {
        GameObject service = Child(fixture, "Stress Visibility Check (Temporary)");
        var visibility = service.AddComponent<MultiplayerHUDVisibility>();
        CanvasGroup stress = Child(fixture, "Authored Stress Check", typeof(CanvasGroup)).GetComponent<CanvasGroup>();
        CanvasGroup controls = Child(fixture, "Authored Controls Check", typeof(CanvasGroup)).GetComponent<CanvasGroup>();
        stress.alpha = .73f; stress.interactable = false; stress.blocksRaycasts = true;
        controls.alpha = .61f; controls.interactable = true; controls.blocksRaycasts = false;
        Set(visibility, "testStressGroup", stress); Set(visibility, "testHiddenGroups", new[] { stress, controls, stress });
        Set(visibility, "testReadoutsVisible", false); Apply(visibility, true, true);
        Check(stress.alpha == 0f && !stress.interactable && !stress.blocksRaycasts, "Stress shows during the introduction.");
        Set(visibility, "testReadoutsVisible", true); Apply(visibility, true, true);
        Check(stress.alpha == .73f && !stress.interactable && stress.blocksRaycasts && controls.alpha == 0f,
            "Testing does not restore the stress readout's exact flags while hiding controls.");
        stress.alpha = 1f; stress.interactable = true; stress.blocksRaycasts = false;
        Apply(visibility, true, true);
        Check(stress.alpha == .73f && !stress.interactable && stress.blocksRaycasts, "A refresh escapes the stress visibility policy.");
        Set(visibility, "testReadoutsVisible", false); Apply(visibility, true, true);
        Check(stress.alpha == 0f && !stress.blocksRaycasts, "The next introduction or results retain the stress readout.");
        Apply(visibility, true, false);
        Check(stress.alpha == .73f && !stress.interactable && stress.blocksRaycasts &&
            controls.alpha == .61f && controls.interactable && !controls.blocksRaycasts, "Finishing tests fails exact flag restoration.");
        Set(visibility, "testReadoutsVisible", true); Apply(visibility, true, true);
        Apply(visibility, false, false);
        Check(stress.alpha == .73f && !stress.interactable && stress.blocksRaycasts, "Disconnect fails stress flag restoration.");
        Set(visibility, "testReadoutsVisible", true); Apply(visibility, true, true);
        typeof(MultiplayerHUDVisibility).GetMethod("OnDisable", Instance).Invoke(visibility, null);
        Check(stress.alpha == .73f && !stress.interactable && stress.blocksRaycasts &&
            !(bool)typeof(MultiplayerHUDVisibility).GetField("testReadoutsVisible", Instance).GetValue(visibility),
            "Disabling leaves a stale stress mask or visibility flag.");
        report.AppendLine("PASS: Actual test mask hides stress in PreparingTest/results, restores it in Testing, keeps controls masked, and restores exact alpha/input flags on completion/disconnect/disable.");
    }

    private static void Refresh(MultiplayerChallengeLobbyUI ui, MultiplayerChallengeState state, string playerName) =>
        typeof(MultiplayerChallengeLobbyUI).GetMethod("RefreshTestOwnerHUD", Instance).Invoke(ui, new object[] { state, playerName });
    private static void Apply(MultiplayerHUDVisibility visibility, bool online, bool testing) =>
        typeof(MultiplayerHUDVisibility).GetMethod("ApplyVisibility", Instance).Invoke(visibility, new object[] { online, testing });
    private static void Set(object value, string field, object content) => value.GetType().GetField(field, Instance).SetValue(value, content);

    private static void Capture(Camera camera, RenderTexture target, string path)
    {
        RenderTexture previous = RenderTexture.active;
        var texture = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        try
        {
            Canvas.ForceUpdateCanvases();
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            if (RenderPipeline.SupportsRenderRequest(camera, request)) RenderPipeline.SubmitRenderRequest(camera, request);
            else camera.Render();
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0); texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
        }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(texture); }
    }

    private static void Check(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
    }
}
