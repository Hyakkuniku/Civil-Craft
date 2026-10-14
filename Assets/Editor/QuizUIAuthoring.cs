#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

/// <summary>Opt-in new assets and detached UI checks; no live scene, NPC, input or save mutations.</summary>
public static class QuizUIAuthoring
{
    private const string Request = "Temp/npc-quiz-authoring-v4.request";
    private const string Report = "Temp/NPCQuizAuthoringValidation.txt";
    private const string PrefabPath = "Assets/Resources/UI/QuizUI.prefab";
    private const string ExamplePath = "Assets/Data/Quizzes/NPC Quiz Example.asset";
    private static double nextCheck;
    private static bool running;
    private static TMP_FontAsset font;
    private static Sprite rounded;
    private static bool Busy => running || EditorApplication.isCompiling || EditorApplication.isUpdating ||
        EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer || PrefabStageUtility.GetCurrentPrefabStage() != null;

    [InitializeOnLoadMethod]
    private static void Watch()
    {
        EditorApplication.update -= CheckRequest;
        EditorApplication.update += CheckRequest;
    }

    private static void CheckRequest()
    {
        if (Busy || EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(Request)) return;
        File.Delete(Request);
        CreateAndValidate();
    }

    [MenuItem("Tools/Civil Craft/Quizzes/Create and Validate Quiz UI")]
    public static void CreateAndValidate()
    {
        Directory.CreateDirectory("Temp");
        try
        {
            Check(!Busy, "Leave Play/Prefab Mode and finish imports before creating Quiz UI.");
            running = true;
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/Bekind Sans SDF.asset");
            rounded = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Elements/UI/LevelResultRounded.png");
            Check(font != null && rounded != null, "Existing gameplay font or rounded UI sprite is missing.");
            var sceneStates = new Dictionary<Scene, Tuple<bool, GameObject[]>>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                sceneStates[scene] = Tuple.Create(scene.isDirty, scene.GetRootGameObjects());
            }
            Scene active = SceneManager.GetActiveScene();
            Object[] selection = Selection.objects;
            Object activeSelection = Selection.activeObject;
            var report = new StringBuilder();
            try
            {
                QuizData example = CreateExample();
                CreatePrefab();
                Validate(example, report);
                foreach (var pair in sceneStates)
                    Check(pair.Key.isLoaded && pair.Key.isDirty == pair.Value.Item1 &&
                        pair.Key.GetRootGameObjects().SequenceEqual(pair.Value.Item2), "A source scene changed during detached quiz authoring.");
                Check(SceneManager.GetActiveScene() == active, "The source active scene changed.");
                report.Insert(0, "PASS: reusable QuizUI prefab and three-question example are ready. No released story or scene changed.\n");
                report.AppendLine("NOTE: Detached presentation only. No network, preferences, player saves, live input or NPC callbacks ran.");
                File.WriteAllText(Report, report.ToString());
                Debug.Log("[NPC Quiz] Created/validated prefab and example; phone/tablet/desktop layouts and click blockade passed.");
            }
            finally { Selection.objects = selection; Selection.activeObject = activeSelection; }
        }
        catch (Exception error) { File.WriteAllText(Report, "FAIL: " + error); Debug.LogException(error); }
        finally { running = false; }
    }

    private static QuizData CreateExample()
    {
        QuizData existing = AssetDatabase.LoadAssetAtPath<QuizData>(ExamplePath);
        if (existing != null) { Check(existing.TryValidate(out string error), error); return existing; }
        EnsureFolder("Assets/Data"); EnsureFolder("Assets/Data/Quizzes");
        Sprite beam = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Elements/UI/bm_ui/WoodBeam_mat_icon.png");
        Sprite road = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Elements/UI/bm_ui/WoodRoad_mat_icon.png");
        Check(beam != null && road != null, "Existing beam/road icons are missing.");
        QuizData quiz = ScriptableObject.CreateInstance<QuizData>();
        quiz.quizId = "civil-craft-npc-quiz-example-v1";
        quiz.title = "Bridge Builder Practice";
        quiz.questions = new List<QuizQuestion>
        {
            new QuizQuestion { questionId = "stable-shape", prompt = "Which shape is commonly used to keep a bridge frame stable?", correctChoiceIndex = 0,
                feedback = "Triangles help a frame keep its shape.", choices = new List<QuizChoice> { TextChoice("Triangle"), TextChoice("Square"), TextChoice("Circle") } },
            new QuizQuestion { questionId = "identify-beam", prompt = "Which picture shows a wood beam?", correctChoiceIndex = 0,
                feedback = "A beam supports the bridge structure.", choices = new List<QuizChoice> { ImageChoice(beam), ImageChoice(road) } },
            new QuizQuestion { questionId = "driving-surface", prompt = "Which choice represents the part that vehicles drive across?", correctChoiceIndex = 1,
                feedback = "The road deck provides a continuous driving surface.", choices = new List<QuizChoice> { TextChoice("A support beam"), ImageChoice(road, "Wood road deck"), ImageChoice(beam) } }
        };
        Check(quiz.TryValidate(out string validation), validation);
        AssetDatabase.CreateAsset(quiz, ExamplePath);
        EditorUtility.SetDirty(quiz); AssetDatabase.SaveAssetIfDirty(quiz);
        return quiz;
    }

    private static QuizChoice TextChoice(string text) => new QuizChoice { text = text };
    private static QuizChoice ImageChoice(Sprite image, string text = "")
    {
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(image, out string guid, out long localId);
        return new QuizChoice { image = image, text = text, imageFingerprint = guid + ":" + localId + ":" + AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(image)) };
    }

    private static void CreatePrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null) return; // Preserve authored customizations on repeat checks.
        Scene preview = EditorSceneManager.NewPreviewScene();
        try
        {
            GameObject root = Build(preview);
            root.SetActive(true); root.GetComponent<QuizView>().Panel.SetActive(false);
            ResetCanvasRoot(root.transform as RectTransform, new Vector2(1600, 900));
            GameObject asset = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Check(asset != null, "Could not save the new QuizUI prefab.");
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
    }

    private static GameObject Build(Scene scene)
    {
        GameObject root = new GameObject("QuizUI", typeof(RectTransform));
        root.SetActive(false); root.layer = 5; SceneManager.MoveGameObjectToScene(root, scene);
        Canvas canvas = root.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.overrideSorting = true; canvas.sortingOrder = 100;
        CanvasScaler scaler = root.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1600, 900); scaler.matchWidthOrHeight = .5f;
        root.AddComponent<GraphicRaycaster>();
        QuizView view = root.AddComponent<QuizView>();
        QuizUILayout layout = root.AddComponent<QuizUILayout>();
        QuizUIManager manager = root.AddComponent<QuizUIManager>(); manager.view = view;
        view.layout = layout; layout.view = view;
        Image panel = Surface(root.transform, "NPCQuizPanel", QuizUILayout.Cream, true, false);
        QuizUILayout.Stretch(panel.rectTransform, 0, 0, 0, 0); view.panel = panel.gameObject;
        RectTransform safe = Child(panel.transform, "Safe Content").transform as RectTransform;
        QuizUILayout.Stretch(safe, 0, 0, 0, 0); layout.safeContent = safe;
        RectTransform header = Child(safe, "Header").transform as RectTransform; layout.header = header;
        view.title = Label(header, "Quiz Title", "Bridge Builder Practice", 38);
        view.progress = Label(header, "Question Progress", "Question 1 of 3", 26);
        view.pauseButton = ActionButton(header, "Pause", "Pause", QuizUILayout.Sand);
        view.questionScroll = Scroll(safe, "Question Scroll", out RectTransform questionViewport, out RectTransform questionContent);
        layout.questionViewport = questionViewport; layout.questionContent = questionContent;
        view.question = Label(questionContent, "Question", "Choose the best answer.", 31);
        view.choiceScroll = Scroll(safe, "Choice Scroll", out RectTransform choiceViewport, out RectTransform choiceContent);
        layout.choiceViewport = choiceViewport; view.choiceContent = choiceContent;
        view.choiceTemplate = Choice(choiceContent); view.choiceTemplate.gameObject.SetActive(false);
        RectTransform footer = Child(safe, "Footer").transform as RectTransform; layout.footer = footer;
        layout.feedbackScroll = Scroll(footer, "Feedback Scroll", out RectTransform feedbackViewport, out RectTransform feedbackContent);
        layout.feedbackViewport = feedbackViewport; layout.feedbackContent = feedbackContent;
        view.feedback = Label(feedbackContent, "Feedback", "Choose one answer, then press Submit.", 25);
        view.leaveButton = ActionButton(footer, "Leave Quiz", "Leave Quiz", QuizUILayout.Sand);
        view.submitButton = ActionButton(footer, "Submit", "Submit", QuizUILayout.Gold);
        view.nextButton = ActionButton(footer, "Next", "Next", QuizUILayout.Gold);
        view.finishButton = ActionButton(footer, "Finish", "Finish", QuizUILayout.Gold);
        view.retrySaveButton = ActionButton(footer, "Retry Save", "Retry Save", QuizUILayout.Gold);
        view.nextButton.gameObject.SetActive(false); view.finishButton.gameObject.SetActive(false); view.retrySaveButton.gameObject.SetActive(false);
        return root;
    }

    private static QuizChoiceView Choice(Transform parent)
    {
        Button button = ActionButton(parent, "Choice Template", "", QuizUILayout.Cream);
        Object.DestroyImmediate(button.GetComponentInChildren<TMP_Text>(true).gameObject);
        QuizChoiceView choice = button.gameObject.AddComponent<QuizChoiceView>();
        choice.button = button; choice.face = button.targetGraphic as Image;
        choice.number = Label(button.transform, "Choice Number", "A", 25);
        choice.label = Label(button.transform, "Choice Text", "Answer", 29);
        choice.artwork = Surface(button.transform, "Choice Image", Color.white, false, false);
        choice.artwork.preserveAspect = true;
        return choice;
    }

    private static ScrollRect Scroll(Transform parent, string name, out RectTransform viewport, out RectTransform content)
    {
        Image image = Surface(parent, name, QuizUILayout.Sand, true, false);
        viewport = image.rectTransform; image.gameObject.AddComponent<RectMask2D>();
        ScrollRect scroll = image.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport;
        content = Child(viewport, "Content").transform as RectTransform;
        content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(.5f, 1);
        content.anchoredPosition = Vector2.zero; content.sizeDelta = Vector2.zero;
        scroll.content = content; scroll.horizontal = false; scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 40; scroll.decelerationRate = .12f;
        return scroll;
    }

    private static void Validate(QuizData example, StringBuilder report)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Check(prefab != null, "Quiz UI prefab is missing.");
        QuizView authored = prefab.GetComponent<QuizView>();
        Check(authored != null && authored.IsReady && prefab.GetComponent<QuizUIManager>()?.view == authored, "Quiz view/manager references are incomplete.");
        Check(!authored.Panel.activeSelf && prefab.activeSelf, "Quiz panel must start hidden with its session owner active.");
        Check(!prefab.GetComponent<QuizUIManager>().transform.IsChildOf(authored.Panel.transform), "Session owner must stay outside the hideable panel.");
        Check(authored.Panel.GetComponent<Image>().raycastTarget && authored.Panel.GetComponent<Image>().color.a == 1f, "Quiz backdrop must be opaque and block clicks.");
        Check(prefab.transform.localScale == Vector3.one && (prefab.transform as RectTransform).rect.width > 0f,
            "The authored quiz Canvas has invalid root scale/size.");
        Scene preview = EditorSceneManager.NewPreviewScene();
        RenderTexture target = null;
        QuizData longQuiz = null;
        try
        {
            GameObject root = Object.Instantiate(prefab); root.SetActive(false); SceneManager.MoveGameObjectToScene(root, preview);
            root.GetComponent<QuizUIManager>().enabled = false;
            root.GetComponent<CanvasScaler>().enabled = false;
            QuizView view = root.GetComponent<QuizView>(); QuizUILayout layout = root.GetComponent<QuizUILayout>();
            Canvas canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 30;
            GameObject cameraObject = new GameObject("Detached Quiz Camera", typeof(Camera)); SceneManager.MoveGameObjectToScene(cameraObject, preview);
            Camera camera = cameraObject.GetComponent<Camera>(); camera.scene = preview; camera.enabled = false;
            camera.orthographic = true; camera.transform.position = new Vector3(0, 0, -1000); camera.nearClipPlane = .1f; camera.farClipPlane = 2000;
            camera.cullingMask = 1 << 30; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; canvas.worldCamera = camera;
            longQuiz = Object.Instantiate(example);
            longQuiz.questions[0].prompt = string.Join("\n\n", Enumerable.Repeat("Read this bridge-building question carefully. Longer prompts stay readable and scroll independently above the choices.", 18));
            longQuiz.questions[0].choices = Enumerable.Range(0, 23).Select(i => i == 1 ? ImageChoice(example.questions[2].choices[1].image,
                "Image caption: " + string.Join(" ", Enumerable.Repeat("A long mixed-choice explanation stays inside its card.", 6))) :
                TextChoice("Choice " + (i + 1) + ": " + string.Join(" ", Enumerable.Repeat("Bridge-building answer", 8)))).ToList();
            Vector2Int[] sizes = { new Vector2Int(1280, 720), new Vector2Int(1024, 768), new Vector2Int(1920, 1080), new Vector2Int(720, 1280) };
            string[] names = { "phone", "tablet", "desktop", "portrait" };
            root.SetActive(true); view.Panel.SetActive(true);
            for (int i = 0; i < sizes.Length; i++)
            {
                Vector2Int size = sizes[i]; target = new RenderTexture(size.x, size.y, 24); target.Create();
                // Match the actual prefab CanvasScaler instead of pretending
                // every device uses one logical unit per physical pixel.
                float physicalScale = Mathf.Sqrt((size.x / 1600f) * (size.y / 900f));
                Vector2 logicalSize = new Vector2(size.x, size.y) / physicalScale;
                canvas.scaleFactor = physicalScale;
                camera.targetTexture = target; camera.aspect = (float)size.x / size.y; camera.orthographicSize = size.y * .5f;
                camera.orthographicSize = logicalSize.y * .5f;
                ResetCanvasRoot(root.transform as RectTransform, logicalSize);
                Canvas.ForceUpdateCanvases(); view.ShowQuestion(example, 2, new string[0]); layout.ApplyLayout(); Canvas.ForceUpdateCanvases();
                foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>()) text.ForceMeshUpdate();
                Check(layout.ColumnCount == (size.x > size.y ? 2 : 1), "Wrong choice column count for " + names[i]);
                foreach (RectTransform rect in new[] { layout.header, layout.footer, layout.questionViewport, layout.choiceViewport })
                    Inside(rect, layout.safeContent, "Quiz region leaves safe content: " + rect.name);
                Check(!Bounds(layout.questionViewport, layout.safeContent).Overlaps(Bounds(layout.choiceViewport, layout.safeContent)), "Question/choice regions overlap.");
                Check(!Bounds(layout.choiceViewport, layout.safeContent).Overlaps(Bounds(layout.footer, layout.safeContent)), "Choices cover footer controls.");
                Check(!Bounds(layout.feedbackViewport, layout.footer).Overlaps(Bounds(view.leaveButton.transform as RectTransform, layout.footer)),
                    "Feedback overlaps the physical-size Leave Quiz button.");
                Check(!Bounds(layout.feedbackViewport, layout.footer).Overlaps(Bounds(view.submitButton.transform as RectTransform, layout.footer)),
                    "Feedback overlaps the physical-size Submit button.");
                Check(view.VisibleChoices.Count == example.questions[2].choices.Count, "Choices were hardcoded or lost.");
                foreach (Button button in new[] { view.leaveButton, view.submitButton, view.pauseButton })
                    Check((button.transform as RectTransform).rect.height * physicalScale >= 47.9f, "Quiz touch target is shorter than 48 physical pixels: " + button.name);
                Check(view.VisibleChoices[1].artwork.preserveAspect && view.VisibleChoices[2].artwork.preserveAspect, "Choice images must preserve aspect ratio.");
                ValidateControls(view);
                PrepareGraphics(root, camera, target);
                ValidateClickBlockade(root, view, camera, preview);
                Capture(camera, target, "Temp/npc-quiz-" + names[i] + ".png");
                view.ShowQuestion(longQuiz, 0, new string[0]); layout.ApplyLayout(); Canvas.ForceUpdateCanvases();
                Check(layout.questionContent.rect.height > layout.questionViewport.rect.height, "Long prompt is not scrollable.");
                Check(view.choiceContent.rect.height > layout.choiceViewport.rect.height, "Long/dynamic choices are not scrollable on " + names[i]);
                Check(view.VisibleChoices.Count == 23 && !view.question.richText, "Dynamic/plain-text content regression.");
                Check(view.question.fontSize * physicalScale >= 19.9f && view.VisibleChoices[0].label.fontSize * physicalScale >= 19.9f,
                    "Question or choice text is too small in physical pixels.");
                report.AppendLine("PASS: " + names[i] + " " + size.x + "x" + size.y + ": safe regions, adaptive columns, text/image/mixed choices, Submit/retry/Next/Finish, independent long-content scrolling and opaque click blockade.");
                camera.targetTexture = null; Release(target); target = null;
            }
        }
        finally { if (longQuiz != null) Object.DestroyImmediate(longQuiz); Release(target); EditorSceneManager.ClosePreviewScene(preview); }
    }

    private static void ResetCanvasRoot(RectTransform rect, Vector2 size)
    {
        // A new overlay Canvas in a preview scene has no display and Unity can
        // serialize zero scale/size. Author usable root values explicitly.
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        rect.anchorMin = rect.anchorMax = Vector2.one * .5f;
        rect.pivot = Vector2.one * .5f;
        rect.anchoredPosition3D = Vector3.zero;
        rect.sizeDelta = size;
    }

    private static void ValidateControls(QuizView view)
    {
        Check(!view.submitButton.interactable, "Submit must be disabled before selection.");
        int submitted = 0, selected = -1, next = 0, finished = 0;
        Action submitAction = () => submitted++;
        Action<int> choiceAction = index => selected = index;
        Action nextAction = () => next++;
        Action finishAction = () => finished++;
        view.SubmitRequested += submitAction; view.ChoiceSelected += choiceAction;
        view.NextRequested += nextAction; view.FinishRequested += finishAction;
        var click = new PointerEventData(null) { button = PointerEventData.InputButton.Left };
        try
        {
        ExecuteEvents.Execute(view.submitButton.gameObject, click, ExecuteEvents.pointerClickHandler);
        Check(submitted == 0, "Disabled Submit accepted a native UI click.");
        ExecuteEvents.Execute(view.VisibleChoices[1].button.gameObject, click, ExecuteEvents.pointerClickHandler);
        Check(selected == 1 && view.submitButton.interactable, "Choice click did not select and enable Submit.");
        ExecuteEvents.Execute(view.submitButton.gameObject, click, ExecuteEvents.pointerClickHandler);
        Check(submitted == 1, "Submit click did not dispatch its event exactly once.");
        view.ShowFeedback(false, "Must not reveal the correct choice.");
        Check(view.feedback.text == "Try again." && view.VisibleChoices.All(card => card.button.interactable), "Wrong answer revealed content or locked retry.");
        view.SetSelection(0); Check(view.feedback.text != "Try again.", "New selection retained stale wrong feedback.");
        view.ShowFeedback(true, "An explanation.");
        Check(view.nextButton.gameObject.activeSelf && view.VisibleChoices.All(card => !card.button.interactable), "Correct answer should lock choices until Next.");
        ExecuteEvents.Execute(view.VisibleChoices[1].button.gameObject, click, ExecuteEvents.pointerClickHandler);
        Check(selected == 1, "Locked correct-question choices still dispatched input.");
        ExecuteEvents.Execute(view.nextButton.gameObject, click, ExecuteEvents.pointerClickHandler);
        Check(next == 1, "Next click did not dispatch exactly once.");
        view.ShowSaveError("Save test error");
        Check(view.retrySaveButton.gameObject.activeSelf && view.leaveButton.interactable && !view.nextButton.gameObject.activeSelf, "Save failure must allow retry/exit without advancing.");
        // Return to the mixed question before the screenshot/click checks.
        QuizData example = AssetDatabase.LoadAssetAtPath<QuizData>(ExamplePath);
        view.ShowCompletion(example); Check(view.finishButton.gameObject.activeSelf, "Completion should offer Finish.");
        ExecuteEvents.Execute(view.finishButton.gameObject, click, ExecuteEvents.pointerClickHandler);
        Check(finished == 1, "Finish click did not dispatch exactly once.");
        view.ShowQuestion(example, 2, new string[0]);
        }
        finally
        {
            view.SubmitRequested -= submitAction; view.ChoiceSelected -= choiceAction;
            view.NextRequested -= nextAction; view.FinishRequested -= finishAction;
        }
    }

    private static void ValidateClickBlockade(GameObject root, QuizView view, Camera camera, Scene scene)
    {
        GameObject eventObject = new GameObject("Detached Quiz Event System"); eventObject.SetActive(false); SceneManager.MoveGameObjectToScene(eventObject, scene);
        EventSystem events = eventObject.AddComponent<EventSystem>();
        try
        {
            var pointer = new PointerEventData(events) { position = new Vector2(camera.pixelWidth * .01f, camera.pixelHeight * .01f) };
            var hits = new List<RaycastResult>(); root.GetComponent<GraphicRaycaster>().Raycast(pointer, hits);
            Check(hits.Any(hit => hit.gameObject == view.Panel), "Opaque quiz backdrop does not receive empty-space clicks. " +
                NativeGeometry(view.Panel, camera, pointer.position, hits));
            pointer.position = camera.WorldToScreenPoint(view.leaveButton.transform.position); hits.Clear();
            root.GetComponent<GraphicRaycaster>().Raycast(pointer, hits);
            Check(hits.Count > 0 && hits.OrderByDescending(hit => hit.depth).First().gameObject.transform.IsChildOf(view.leaveButton.transform), "Leave Quiz hit target is blocked.");
            foreach (Button button in new[] { view.submitButton, view.pauseButton, view.VisibleChoices[0].button })
            {
                pointer.position = camera.WorldToScreenPoint(button.transform.position); hits.Clear();
                root.GetComponent<GraphicRaycaster>().Raycast(pointer, hits);
                Check(hits.Count > 0 && (hits.OrderByDescending(hit => hit.depth).First().gameObject.transform == button.transform ||
                    hits.OrderByDescending(hit => hit.depth).First().gameObject.transform.IsChildOf(button.transform)),
                    "Quiz action/choice hit target is blocked: " + button.name);
            }
        }
        finally { Object.DestroyImmediate(eventObject); }
    }

    private static string NativeGeometry(GameObject panel, Camera camera, Vector2 point, IEnumerable<RaycastResult> hits)
    {
        Image image = panel.GetComponent<Image>();
        Vector3[] corners = new Vector3[4]; image.rectTransform.GetWorldCorners(corners);
        return "camera=" + camera.pixelRect + "; point=" + point + "; panel=" + image.rectTransform.rect +
            "; corners=" + corners[0] + "/" + corners[2] + "; active=" + image.isActiveAndEnabled +
            "; depth=" + image.depth + "; cull=" + image.canvasRenderer.cull + "; contains=" +
            RectTransformUtility.RectangleContainsScreenPoint(image.rectTransform, point, camera) +
            "; registered=" + GraphicRegistry.GetRaycastableGraphicsForCanvas(image.canvas).Contains(image) +
            "; hits=" + string.Join(",", hits.Select(hit => hit.gameObject.name + "/" + hit.depth));
    }

    private static void PrepareGraphics(GameObject root, Camera camera, RenderTexture target)
    {
        Canvas.ForceUpdateCanvases();
        foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>()) { graphic.SetAllDirty(); graphic.Rebuild(CanvasUpdate.PreRender); }
        Canvas.ForceUpdateCanvases(); Render(camera, target); Canvas.ForceUpdateCanvases();
    }
    private static void Capture(Camera camera, RenderTexture target, string path)
    {
        Render(camera, target); RenderTexture previous = RenderTexture.active;
        Texture2D image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        try { RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply(); File.WriteAllBytes(path, image.EncodeToPNG()); }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
    }
    private static void Render(Camera camera, RenderTexture target)
    {
        if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset)
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        else camera.Render();
    }
    private static void Release(RenderTexture target) { if (target == null) return; target.Release(); Object.DestroyImmediate(target); }
    private static Rect Bounds(RectTransform target, RectTransform parent)
    {
        Vector3[] corners = new Vector3[4]; target.GetWorldCorners(corners); Vector2 min = Vector2.positiveInfinity, max = Vector2.negativeInfinity;
        foreach (Vector3 corner in corners) { Vector2 point = parent.InverseTransformPoint(corner); min = Vector2.Min(min, point); max = Vector2.Max(max, point); }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }
    private static void Inside(RectTransform target, RectTransform parent, string message)
    {
        Rect bounds = Bounds(target, parent), area = parent.rect;
        Check(bounds.xMin >= area.xMin - .5f && bounds.yMin >= area.yMin - .5f && bounds.xMax <= area.xMax + .5f && bounds.yMax <= area.yMax + .5f, message);
    }
    private static GameObject Child(Transform parent, string name)
    {
        GameObject child = new GameObject(name, typeof(RectTransform)); child.layer = parent.gameObject.layer;
        SceneManager.MoveGameObjectToScene(child, parent.gameObject.scene); child.transform.SetParent(parent, false); return child;
    }
    private static Image Surface(Transform parent, string name, Color color, bool raycast, bool roundedShape = true)
    {
        Image image = Child(parent, name).AddComponent<Image>(); image.color = color; image.raycastTarget = raycast;
        if (roundedShape) { image.sprite = rounded; image.type = Image.Type.Sliced; }
        return image;
    }
    private static TMP_Text Label(Transform parent, string name, string value, float size)
    {
        TMP_Text text = Child(parent, name).AddComponent<TextMeshProUGUI>(); text.font = font; text.fontSharedMaterial = font.material;
        text.text = value; text.fontSize = size; text.color = QuizUILayout.Ink; text.richText = false; text.raycastTarget = false;
        text.enableAutoSizing = false; text.enableWordWrapping = true; text.alignment = TextAlignmentOptions.TopLeft;
        text.overflowMode = TextOverflowModes.Overflow; text.margin = Vector4.zero; return text;
    }
    private static Button ActionButton(Transform parent, string name, string value, Color fill)
    {
        Image frame = Surface(parent, name, QuizUILayout.Ink, true); Image face = Surface(frame.transform, "Face", fill, false);
        QuizUILayout.Stretch(face.rectTransform, 3, 3, 3, 3);
        Button button = frame.gameObject.AddComponent<Button>(); button.targetGraphic = face;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        ColorBlock colors = button.colors; colors.highlightedColor = colors.selectedColor = new Color(1, .95f, .82f);
        colors.pressedColor = new Color(.85f, .76f, .61f); colors.disabledColor = new Color(.65f, .62f, .58f); button.colors = colors;
        TMP_Text text = Label(frame.transform, "Label", value, 27); text.alignment = TextAlignmentOptions.Center;
        QuizUILayout.Stretch(text.rectTransform, 12, 6, 12, 6); return button;
    }
    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/'); EnsureFolder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}

[CustomEditor(typeof(QuizData))]
public sealed class QuizDataEditor : Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox("Questions run in this list's order. Each choice may use text, an image, or both. Correct Choice Index is zero-based (0 = first choice). IDs remain stable for saved progress; editing quiz content resets only this quiz's saved answers.", MessageType.Info);
        DrawDefaultInspector();
        QuizData quiz = (QuizData)target;
        if (!quiz.TryValidate(out string error)) EditorGUILayout.HelpBox(error, MessageType.Error);
        else EditorGUILayout.HelpBox("Ready: " + quiz.questions.Count + " questions. This asset is not added to any NPC until you assign it to a Quiz sequence step.", MessageType.Info);
    }
}
#endif
