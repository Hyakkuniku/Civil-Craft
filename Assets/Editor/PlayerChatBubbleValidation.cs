#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class PlayerChatBubbleValidation
{
    private const string Request = "Temp/player-chat-bubble-validation.request";
    private static double next;
    [InitializeOnLoadMethod]
    private static void Watch() { EditorApplication.update -= Poll; EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < next || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode) return;
        next = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(Request)) return;
        File.Delete(Request); Validate();
    }
    [MenuItem("Tools/Civil Craft/Multiplayer/Validate Chat Bubbles")]
    public static void Validate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var report = new StringBuilder();
        Scene source = SceneManager.GetActiveScene(); bool dirty = source.isDirty;
        Scene preview = default;
        RenderTexture target = null, previous = RenderTexture.active;
        try
        {
            preview = EditorSceneManager.NewPreviewScene();
            var go = new GameObject("Detached bubble test"); SceneManager.MoveGameObjectToScene(go, preview);
            PlayerChatBubble owner = go.AddComponent<PlayerChatBubble>();
            Invoke(owner, "EnsureUI", preview);
            Canvas canvas = Field<Canvas>(owner, "canvas");
            CanvasGroup group = Field<CanvasGroup>(owner, "group");
            TextMeshProUGUI text = Field<TextMeshProUGUI>(owner, "label");
            RectTransform rect = Field<RectTransform>(owner, "bubble");
            Check(!group.blocksRaycasts && !group.interactable && !text.raycastTarget &&
                canvas.GetComponent<GraphicRaycaster>() == null, "Bubble intercepts input");
            Check(!text.richText && !text.parseCtrlCharacters, "Markup enabled");
            var cameraGo = new GameObject("Detached preview camera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraGo, preview);
            Camera camera = cameraGo.GetComponent<Camera>(); camera.enabled = false; camera.scene = preview;
            camera.orthographic = true; camera.transform.position = new Vector3(0, 0, -1000);
            camera.farClipPlane = 2000; camera.cullingMask = 1 << 30; camera.useOcclusionCulling = false;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(112, 132, 108, 255);
            foreach (Transform child in canvas.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 30;
            canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = camera;
            RectTransform canvasRect = (RectTransform)canvas.transform;
            // Switching away from Overlay retains its screen-centred world pose.
            // Reset that pose for the detached orthographic preview camera.
            canvasRect.position = Vector3.zero;
            canvasRect.rotation = Quaternion.identity;
            canvasRect.localScale = Vector3.one;
            canvasRect.pivot = new Vector2(.5f, .5f);
            foreach (Vector2Int size in new[] { new Vector2Int(1280, 720), new Vector2Int(1024, 768), new Vector2Int(720, 1280) })
            {
                target = new RenderTexture(size.x, size.y, 24); target.Create(); camera.targetTexture = target;
                camera.aspect = (float)size.x / size.y; camera.orthographicSize = size.y / 2f;
                canvasRect.sizeDelta = size;
                canvasRect.position = Vector3.zero;
                float scale = Mathf.Clamp(size.y / 1080f, .75f, 1.5f);
                float width = Mathf.Min(380f * scale, size.x * .65f);
                foreach (string message in new[] { "Hello! Ready to build the bridge?", "<size=999><color=red>This is literal text</color></size>", new string('W', 280) })
                {
                    owner.Show(null, message); Invoke(owner, "LayoutMessage", width, scale);
                    canvas.gameObject.SetActive(true); Canvas.ForceUpdateCanvases(); text.ForceMeshUpdate(true, true);
                    Check(text.GetParsedText() == message, "Literal text altered");
                    Check(!text.isTextOverflowing && rect.rect.height < size.y * .85f, "Long text does not fit");
                    Check(rect.rect.width <= size.x * .65f + 1, "Width overflow");
                }
                rect.anchoredPosition = new Vector2(0, -rect.rect.height / 2);
                Canvas.ForceUpdateCanvases();
                var corners = new Vector3[4]; rect.GetWorldCorners(corners);
                foreach (Vector3 corner in corners)
                {
                    Vector3 viewport = camera.WorldToViewportPoint(corner);
                    Check(viewport.z > 0 && viewport.x > 0 && viewport.x < 1 && viewport.y > 0 && viewport.y < 1,
                        "Preview camera does not contain the full bubble");
                }
                camera.Render(); RenderTexture.active = target;
                var image = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
                try { image.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0); image.Apply(); File.WriteAllBytes("Temp/chat-bubble-" + size.x + "x" + size.y + ".png", image.EncodeToPNG()); }
                finally { UnityEngine.Object.DestroyImmediate(image); }
                RenderTexture.active = previous; camera.targetTexture = null;
                target.Release(); UnityEngine.Object.DestroyImmediate(target); target = null;
                report.AppendLine("PASS " + size + ": short, literal-markup and maximum-length text wrap without overflow; readable fixed font size.");
            }
            owner.Clear(); Check(!canvas.gameObject.activeSelf, "Clear fails to hide");
            report.AppendLine("PASS: click-through canvas, plain text, cleanup; no network or player saves touched. Two-device RPC testing is still required.");
        }
        catch (Exception error) { report.AppendLine("FAIL: " + error); Debug.LogException(error); }
        finally
        {
            RenderTexture.active = previous;
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
            report.AppendLine(source.isDirty == dirty ? "PASS: source scene dirty state preserved; no scene saved." : "FAIL: source dirty state changed.");
            File.WriteAllText("Temp/player-chat-bubble-validation.txt", report.ToString());
        }
    }
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
    private static void Invoke(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(owner, args);
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
#endif
