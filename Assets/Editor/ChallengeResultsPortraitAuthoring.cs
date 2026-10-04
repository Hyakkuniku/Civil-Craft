using System;
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

/// <summary>Opt-in result portrait authoring and isolated previews. Never starts networking or changes saves.</summary>
public static class ChallengeResultsPortraitAuthoring
{
    private const string ScenePath = "Assets/Scenes/CanyonCrossing.unity";
    private const string Request = "Temp/challenge-results-portraits-v1.request";
    private const string Report = "Temp/ChallengeResultsPortraitValidation.txt";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
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

    [MenuItem("Tools/Civil Craft/Author Animated Challenge Result Portraits")]
    public static void Author()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetSceneByPath(ScenePath).isDirty)
            throw new InvalidOperationException("Stop Play Mode and save CanyonCrossing scene edits first.");
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        var report = new StringBuilder();
        try
        {
            var ui = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MultiplayerChallengeLobbyUI>(true)).Single();
            var data = new SerializedObject(ui);
            var panel = (GameObject)data.FindProperty("resultsPanel").objectReferenceValue;
            var callbacks = panel.GetComponentsInChildren<Button>(true).ToDictionary(button => button, button => JsonUtility.ToJson(button.onClick));
            foreach (string player in new[] { "Host", "Guest" })
            {
                var name = (TMP_Text)data.FindProperty("results" + player + "Name").objectReferenceValue;
                var score = (TMP_Text)data.FindProperty("results" + player + "Score").objectReferenceValue;
                var details = (TMP_Text)data.FindProperty("results" + player + "Details").objectReferenceValue;
                Place(name.rectTransform, .035f, .81f, .965f, .965f);
                Place(score.rectTransform, .46f, .64f, .965f, .80f);
                Place(details.rectTransform, .46f, .07f, .965f, .61f);
                Style(name, 34, 20, TextAlignmentOptions.Center);
                Style(score, 48, 32, TextAlignmentOptions.MidlineLeft);
                Style(details, 25, 18, TextAlignmentOptions.TopLeft);
                Transform existing = name.transform.parent.Find(player + " Result Character");
                var portrait = existing != null ? existing.GetComponent<RawImage>() : null;
                if (portrait == null)
                {
                    var obj = new GameObject(player + " Result Character", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
                    obj.transform.SetParent(name.transform.parent, false); obj.layer = name.gameObject.layer;
                    portrait = obj.GetComponent<RawImage>();
                }
                Place(portrait.rectTransform, .035f, .055f, .425f, .79f);
                portrait.color = Color.white; portrait.texture = null; portrait.raycastTarget = false;
                portrait.uvRect = new Rect(player == "Host" ? 0f : .5f, 0f, .5f, 1f);
                data.FindProperty("results" + player + "Portrait").objectReferenceValue = portrait;
            }
            data.ApplyModifiedPropertiesWithoutUndo();
            foreach (var callback in callbacks)
                Check(JsonUtility.ToJson(callback.Key.onClick) == callback.Value, "A result button handler changed.");
            ValidateAndCapture(ui, scene, report);
            EditorSceneManager.MarkSceneDirty(scene);
            Check(EditorSceneManager.SaveScene(scene), "Could not save the authored result portraits.");
            report.AppendLine("PASS: Saved two scene-authored transparent character spaces; names/metrics/return button handlers preserved. No bridge, player save, outcome, purchase or network state changed.");
            report.AppendLine("NOTE: Live host/guest results and world-return playback still need a two-player play test.");
            File.WriteAllText(Report, report.ToString());
            Debug.Log("[Challenge results portraits] Authored animation, framing, text-fit and cleanup checks passed.");
        }
        finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
    }

    private static void Place(RectTransform rect, float x0, float y0, float x1, float y1)
    {
        rect.anchorMin = new Vector2(x0, y0); rect.anchorMax = new Vector2(x1, y1);
        rect.pivot = Vector2.one * .5f; rect.anchoredPosition = rect.sizeDelta = Vector2.zero;
        rect.localScale = Vector3.one; rect.localRotation = Quaternion.identity;
    }
    private static void Style(TMP_Text text, float maximum, float minimum, TextAlignmentOptions alignment)
    {
        text.fontSize = text.fontSizeMax = maximum; text.fontSizeMin = minimum;
        text.enableAutoSizing = true; text.alignment = alignment; text.raycastTarget = false;
    }
    private static object Field(MultiplayerChallengeLobbyUI ui, string name) => typeof(MultiplayerChallengeLobbyUI).GetField(name, Private).GetValue(ui);

    private static void ValidateAndCapture(MultiplayerChallengeLobbyUI ui, Scene sourceScene, StringBuilder report)
    {
        Scene previewScene = EditorSceneManager.NewPreviewScene();
        RenderTexture portraits = null, canvasTarget = null;
        var hostReaction = new ChallengeResultPortraitReaction();
        var guestReaction = new ChallengeResultPortraitReaction();
        MultiplayerChallengeLobbyUI lighting = null;
        try
        {
            var canvasRoot = new GameObject("Result UI Check (Temporary)", typeof(RectTransform), typeof(Canvas));
            SceneManager.MoveGameObjectToScene(canvasRoot, previewScene); canvasRoot.layer = 30;
            var canvas = canvasRoot.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            var canvasRect = canvasRoot.GetComponent<RectTransform>(); canvasRect.sizeDelta = new Vector2(1920, 1080);
            var panel = Object.Instantiate((GameObject)Field(ui, "resultsPanel"), canvasRoot.transform, false);
            panel.SetActive(true);
            foreach (Transform node in panel.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = 30;
            var hostImage = panel.GetComponentsInChildren<RawImage>().Single(image => image.name == "Host Result Character");
            var guestImage = panel.GetComponentsInChildren<RawImage>().Single(image => image.name == "Guest Result Character");
            Check(hostImage.uvRect == new Rect(0, 0, .5f, 1) && guestImage.uvRect == new Rect(.5f, 0, .5f, 1) &&
                !hostImage.raycastTarget && !guestImage.raycastTarget, "Portrait halves are swapped or block button input.");
            var cameraRoot = new GameObject("Result UI Camera (Temporary)", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraRoot, previewScene);
            var camera = cameraRoot.GetComponent<Camera>(); camera.scene = previewScene; camera.enabled = false;
            camera.orthographic = true; camera.orthographicSize = 540; camera.aspect = 16f / 9;
            camera.transform.position = new Vector3(0, 0, -1000); camera.nearClipPlane = .1f; camera.farClipPlane = 2000;
            camera.cullingMask = 1 << 30; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f, .19f, .27f);
            canvas.worldCamera = camera;
            Camera sourceCamera = (Camera)Field(ui, "portraitCamera");
            var stage = Object.Instantiate(sourceCamera.transform.parent.gameObject);
            stage.SetActive(false); SceneManager.MoveGameObjectToScene(stage, previewScene); stage.transform.position = new Vector3(0, -10000, 0);
            Camera portraitCamera = stage.GetComponentInChildren<Camera>(true); portraitCamera.scene = previewScene; portraitCamera.enabled = false;
            Transform hostAnchor = stage.GetComponentsInChildren<Transform>(true).Single(t => t.name == ((Transform)Field(ui, "hostPortraitAnchor")).name);
            Transform guestAnchor = stage.GetComponentsInChildren<Transform>(true).Single(t => t.name == ((Transform)Field(ui, "guestPortraitAnchor")).name);
            var motor = sourceScene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<PlayerMotor>(true))
                .First(player => player.transform.Find("NewCharacterModel") != null);
            GameObject a = CloneModel(motor.transform.Find("NewCharacterModel"), hostAnchor);
            GameObject b = CloneModel(motor.transform.Find("NewCharacterModel"), guestAnchor);
            stage.SetActive(true);
            Animator hostAnimator = a.GetComponentInChildren<Animator>(true), guestAnimator = b.GetComponentInChildren<Animator>(true);
            PrepareAnimator(hostAnimator); PrepareAnimator(guestAnimator);
            var scope = new GameObject("Results Lighting Check (Temporary)"); scope.SetActive(false); SceneManager.MoveGameObjectToScene(scope, previewScene);
            lighting = scope.AddComponent<MultiplayerChallengeLobbyUI>();
            typeof(MultiplayerChallengeLobbyUI).GetField("portraitCamera", Private).SetValue(lighting, portraitCamera);
            typeof(MultiplayerChallengeLobbyUI).GetField("portraitLight", Private).SetValue(lighting, stage.GetComponentInChildren<Light>(true));
            var framing = typeof(MultiplayerChallengeLobbyUI).GetMethod("FramePortraitColumns", BindingFlags.Static | BindingFlags.NonPublic);

            foreach (int screenHeight in new[] { 1080, 1200 })
            {
                canvasRect.sizeDelta = new Vector2(1920, screenHeight); camera.orthographicSize = screenHeight / 2f; camera.aspect = 1920f / screenHeight;
                Canvas.ForceUpdateCanvases();
                float aspect = hostImage.rectTransform.rect.width / hostImage.rectTransform.rect.height * 2;
                portraits = new RenderTexture(768, Mathf.RoundToInt(768 / aspect), 24, RenderTextureFormat.ARGB32);
                portraits.Create(); portraitCamera.targetTexture = portraits; hostImage.texture = guestImage.texture = portraits;
                framing.Invoke(null, new object[] { portraitCamera, hostAnchor, guestAnchor, aspect });
                canvasTarget = new RenderTexture(1280, Mathf.RoundToInt(1280f * screenHeight / 1920), 24); canvasTarget.Create(); camera.targetTexture = canvasTarget;
                Populate(panel, false);
                float elapsed = 0;
                hostReaction.Reset(); guestReaction.Reset();
                Vector3 initialA = a.transform.position, initialB = b.transform.position;
                for (int sample = 0; sample < 3; sample++)
                {
                    float sampleAt = new[] { .25f, 2.2f, 3.6f }[sample];
                    while (elapsed < sampleAt)
                    {
                        hostReaction.Tick(a, ChallengeReaction.Crying, 1, elapsed);
                        guestReaction.Tick(b, ChallengeReaction.Winning, 1, elapsed);
                        hostAnimator.Update(.05f); guestAnimator.Update(.05f); elapsed += .05f;
                    }
                    RenderPortraits(lighting, portraitCamera, portraits);
                    ValidateSilhouettes(portraits);
                    ValidateLayout(panel);
                    if (sample == 1) Capture(camera, canvasTarget, "Temp/ChallengeResultsCharacters_" + screenHeight + ".png");
                }
                Check(a.transform.position == initialA && b.transform.position == initialB, "Result clips move their portrait anchors.");
                // A second cycle must actually restart, not return to idle forever
                // or restart from frame zero on every results refresh.
                while (elapsed < 8.4f)
                {
                    hostReaction.Tick(a, ChallengeReaction.Crying, 1, elapsed);
                    guestReaction.Tick(b, ChallengeReaction.Winning, 1, elapsed);
                    hostAnimator.Update(.05f); guestAnimator.Update(.05f); elapsed += .05f;
                }
                Check(hostReaction.IsPlaying && guestReaction.IsPlaying, "Result portraits stop replaying while results remain open.");
                Check(hostAnimator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.crying") &&
                    guestAnimator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Winning"), "Host/guest result animations are wrong.");
                Populate(panel, true); ValidateLayout(panel);
                hostReaction.Tick(a, ChallengeReaction.None, 2, elapsed); guestReaction.Tick(b, ChallengeReaction.None, 2, elapsed);
                hostAnimator.Update(.2f); guestAnimator.Update(.2f);
                Check(!hostReaction.IsPlaying && !guestReaction.IsPlaying && hostAnimator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.idle"),
                    "Draw/changed-round portraits retain a previous defeat animation.");
                hostReaction.Reset(); guestReaction.Reset();
                Release(portraits); portraits = null; Release(canvasTarget); canvasTarget = null;
                report.AppendLine("PASS: " + screenHeight + "px layout: full animated silhouettes in separate transparent halves, stable anchor poses, long-name/metric fit, two clip cycles and draw reset.");
            }
            Check(RenderSettings.sun != stage.GetComponentInChildren<Light>(true), "Result rendering leaked the portrait sun into the world.");
            report.AppendLine("PASS: Real authored character rig and Winning/crying clips, URP portrait-light isolation, unscaled animation and preview-only cleanup. World-return reactions remain independent.");
        }
        finally
        {
            hostReaction.Reset(); guestReaction.Reset();
            if (lighting != null) typeof(MultiplayerChallengeLobbyUI).GetMethod("RestorePortraitLighting", Private).Invoke(lighting, null);
            Release(portraits); Release(canvasTarget); EditorSceneManager.ClosePreviewScene(previewScene);
        }
    }

    private static GameObject CloneModel(Transform source, Transform anchor)
    {
        var model = Object.Instantiate(source.gameObject, anchor, false); model.SetActive(false);
        model.transform.localPosition = Vector3.zero; model.transform.localRotation = Quaternion.identity; model.transform.localScale = source.lossyScale;
        foreach (Transform node in model.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = 31;
        foreach (MonoBehaviour behaviour in model.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
        foreach (Collider collider in model.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        foreach (Rigidbody body in model.GetComponentsInChildren<Rigidbody>(true)) { body.isKinematic = true; body.detectCollisions = false; }
        foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            typeof(MultiplayerChallengeLobbyUI).GetMethod("ConfigurePortraitRenderer", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { renderer });
            renderer.forceRenderingOff = false; renderer.shadowCastingMode = ShadowCastingMode.Off;
            if (renderer.gameObject.activeSelf) renderer.enabled = true;
            if (renderer is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = true;
        }
        model.SetActive(true); return model;
    }
    private static void PrepareAnimator(Animator animator)
    {
        animator.enabled = true; animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.updateMode = AnimatorUpdateMode.UnscaledTime; animator.speed = 1;
        animator.SetFloat("Speed", 0); animator.SetBool("IsGrounded", true); animator.SetBool("Wave", false);
        int carry = animator.GetLayerIndex("Carry Pose"); if (carry >= 0) animator.SetLayerWeight(carry, 0);
        animator.Play("Base Layer.idle", 0, 0); animator.Update(0);
    }
    private static void Populate(GameObject panel, bool longNames)
    {
        TMP_Text Text(string name) => panel.GetComponentsInChildren<TMP_Text>(true).First(t => t.name == name);
        Text("Winner").text = "<b><color=#79500F>WINNER: " + (longNames ? new string('W', 32) : "Guest Engineer") + "</color></b>";
        Text("Scoring Rules").text = "<b>40% COST / 60% STRENGTH</b>  •  Budget <b>₱200,000</b>";
        foreach (string player in new[] { "Host", "Guest" })
        {
            Text(player + " Results Name").text = (longNames ? new string('W', 32) : player + " Engineer") + " <size=70%>(" + player.ToUpperInvariant() + ")</size>";
            var outcome = player == "Host" ? ChallengeTestOutcome.Collapsed : ChallengeTestOutcome.Crossed;
            float cost = player == "Host" ? 64955f : 92221f, peak = player == "Host" ? 1f : .767f;
            var grade = ChallengeCompetitionScoring.Grade(outcome, cost, 200000f, peak);
            Text(player + " Score").text = $"<b>{grade.Hundredths / 100.0:0.00}</b><size=45%> / 100</size>";
            Text(player + " Test Details").text = (string)typeof(MultiplayerChallengeLobbyUI).GetMethod("ResultsDetails", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { outcome, cost, 200000f, peak });
        }
    }
    private static void ValidateLayout(GameObject panel)
    {
        Canvas.ForceUpdateCanvases();
        foreach (TMP_Text text in panel.GetComponentsInChildren<TMP_Text>())
        { text.ForceMeshUpdate(); Check(!text.isTextOverflowing, "Result text overflows: " + text.name); }
        foreach (RawImage image in panel.GetComponentsInChildren<RawImage>())
        {
            Rect r = image.rectTransform.rect;
            foreach (TMP_Text text in image.transform.parent.GetComponentsInChildren<TMP_Text>())
            {
                Vector3[] corners = new Vector3[4]; text.rectTransform.GetWorldCorners(corners);
                var min = (Vector2)image.transform.InverseTransformPoint(corners[0]);
                var max = (Vector2)image.transform.InverseTransformPoint(corners[2]);
                Check(!r.Overlaps(Rect.MinMaxRect(min.x, min.y, max.x, max.y)), "Portrait overlaps " + text.name);
            }
        }
    }
    private static void RenderPortraits(MultiplayerChallengeLobbyUI scope, Camera camera, RenderTexture target)
    {
        var args = new object[] { default(ScriptableRenderContext), camera };
        try
        {
            typeof(MultiplayerChallengeLobbyUI).GetMethod("BeginPortraitLighting", Private).Invoke(scope, args);
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            Check(RenderPipeline.SupportsRenderRequest(camera, request), "URP portrait rendering unavailable.");
            RenderPipeline.SubmitRenderRequest(camera, request);
        }
        finally { typeof(MultiplayerChallengeLobbyUI).GetMethod("EndPortraitLighting", Private).Invoke(scope, args); }
    }
    private static void ValidateSilhouettes(RenderTexture target)
    {
        var pixels = Read(target);
        try
        {
            int left = 0, right = 0; Color32[] colors = pixels.GetPixels32();
            for (int y = 0; y < target.height; y++) for (int x = 0; x < target.width; x++)
            {
                if (colors[y * target.width + x].a <= 12) continue;
                Check(y > 0 && y < target.height - 1 && x > 0 && x < target.width - 1 && x != target.width / 2 && x != target.width / 2 - 1,
                    "Animated head/feet/arms are cropped or portraits cross their separate columns.");
                if (x < target.width / 2) left++; else right++;
            }
            Check(left > 100 && right > 100, "A result character is not rendered.");
            Check(pixels.GetPixel(0, 0).a < .01f, "The result portrait has an opaque background.");
        }
        finally { Object.DestroyImmediate(pixels); }
    }
    private static Texture2D Read(RenderTexture target)
    {
        RenderTexture previous = RenderTexture.active;
        var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        try { RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply(); return image; }
        finally { RenderTexture.active = previous; }
    }
    private static void Capture(Camera camera, RenderTexture target, string path)
    {
        Canvas.ForceUpdateCanvases(); camera.Render();
        var image = Read(target); try { File.WriteAllBytes(path, image.EncodeToPNG()); } finally { Object.DestroyImmediate(image); }
    }
    private static void Release(RenderTexture target) { if (target != null) { target.Release(); Object.DestroyImmediate(target); } }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
