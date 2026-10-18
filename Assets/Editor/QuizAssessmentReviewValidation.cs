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

/// <summary>Opt-in, disposable result-card previews; no session, NPC, save or scene-authoring actions.</summary>
[InitializeOnLoad]
public static class QuizAssessmentReviewValidation
{
    public const string RequestPath = "Temp/quiz-assessment-review-v1.request";
    public const string ReportPath = "Logs/quiz-assessment-review-v1.txt";
    private const string PrefabPath = "Assets/Resources/UI/QuizUI.prefab";
    private const string BeamPath = "Assets/BridgeBuilder/Data/Quized/BeamBridgeQuiz.asset";
    private const string ExamplePath = "Assets/Data/Quizzes/NPC Quiz Example.asset";
    private static bool running;
    private static double nextCheck;
    private static string currentFixture;

    static QuizAssessmentReviewValidation() { EditorApplication.update += Poll; }
    private static bool Busy => running || EditorApplication.isCompiling || EditorApplication.isUpdating ||
        EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer || PrefabStageUtility.GetCurrentPrefabStage() != null;
    private static void Poll()
    {
        if (Busy || EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 1;
        if (!File.Exists(RequestPath)) return;
        File.Delete(RequestPath);
        Run();
    }

    [MenuItem("Tools/Civil Craft/Quizzes/Validate Assessment Review (Read Only)")]
    public static void Run()
    {
        if (Busy) return;
        running = true;
        var report = new StringBuilder();
        Scene active = SceneManager.GetActiveScene();
        Object[] selected = Selection.objects;
        Object activeSelection = Selection.activeObject;
        var scenes = new Dictionary<Scene, Tuple<bool, GameObject[]>>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            scenes[scene] = Tuple.Create(scene.isDirty, scene.GetRootGameObjects());
        }
        PlayerDataManager saveManager = PlayerDataManager.Instance;
        PlayerData player = saveManager != null ? saveManager.CurrentData : null;
        string playerJson = player != null ? JsonUtility.ToJson(player) : null;
        try
        {
            Directory.CreateDirectory("Temp");
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            QuizData beam = AssetDatabase.LoadAssetAtPath<QuizData>(BeamPath);
            QuizData example = AssetDatabase.LoadAssetAtPath<QuizData>(ExamplePath);
            Require(prefab != null && beam != null && example != null, "Existing quiz prefab/content is missing.");
            var assets = new[] { PrefabPath, BeamPath, ExamplePath }
                .Concat(prefab.GetComponentsInChildren<TMP_Text>(true).Where(text => text.font != null)
                    .Select(text => AssetDatabase.GetAssetPath(text.font))).Distinct().ToArray();
            var hashes = assets.ToDictionary(path => path, path => AssetDatabase.GetAssetDependencyHash(path));
            string prefabJson = EditorJsonUtility.ToJson(prefab.GetComponent<QuizView>());
            QuizData mixed = Object.Instantiate(example), longQuiz = Object.Instantiate(beam);
            try
            {
                mixed.assessmentMode = true;
                mixed.title = "Image and Mixed Answer Review";
                BuildLongFixture(longQuiz);
                foreach (QuizData data in new[] { beam, mixed, longQuiz }) Capture(prefab, data, report);
            }
            finally { Object.DestroyImmediate(mixed); Object.DestroyImmediate(longQuiz); }
            Require(hashes.All(pair => AssetDatabase.GetAssetDependencyHash(pair.Key) == pair.Value) &&
                EditorJsonUtility.ToJson(prefab.GetComponent<QuizView>()) == prefabJson,
                "An authored quiz, prefab or gameplay font changed during the read-only preview.");
            Require(SceneManager.GetActiveScene() == active && scenes.All(pair => pair.Key.isLoaded &&
                pair.Key.isDirty == pair.Value.Item1 && pair.Key.GetRootGameObjects().SequenceEqual(pair.Value.Item2)),
                "Loaded scenes or their dirty flags changed.");
            Require(PlayerDataManager.Instance == saveManager && (player == null ||
                (ReferenceEquals(player, saveManager.CurrentData) && JsonUtility.ToJson(player) == playerJson)),
                "Live player data changed during the presentation-only check.");
            report.AppendLine("PASS: source assets, loaded scenes, active scene and player data unchanged. No live session, progression, camera presentation, save or button callbacks ran.");
            Write("PASS\n" + report);
            Debug.Log("[Quiz assessment review] Detached phone/tablet/portrait and result-scroll checks passed. " + ReportPath);
        }
        catch (Exception error) { Write("FAIL\n" + error + "\n" + report); Debug.LogException(error); }
        finally { Selection.objects = selected; Selection.activeObject = activeSelection; running = false; }
    }

    private static void Capture(GameObject prefab, QuizData data, StringBuilder report)
    {
        Scene preview = EditorSceneManager.NewPreviewScene();
        RenderTexture target = null;
        RenderTexture oldActive = RenderTexture.active;
        try
        {
            // Inactive preview parent prevents any session owner Awake before it is removed.
            GameObject holder = new GameObject("Detached Review Holder"); holder.SetActive(false);
            SceneManager.MoveGameObjectToScene(holder, preview);
            GameObject root = Object.Instantiate(prefab, holder.transform, false); root.SetActive(false);
            Object.DestroyImmediate(root.GetComponent<QuizUIManager>());
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            Vector2 reference = scaler != null ? scaler.referenceResolution : new Vector2(1600, 900);
            float match = scaler != null ? scaler.matchWidthOrHeight : .5f;
            if (scaler != null) scaler.enabled = false;
            Canvas canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true; canvas.sortingOrder = 100;
            RectTransform canvasRect = (RectTransform)root.transform;
            canvasRect.anchorMin = canvasRect.anchorMax = canvasRect.pivot = Vector2.one * .5f;
            canvasRect.anchoredPosition = Vector2.zero; canvasRect.localScale = Vector3.one * .01f;
            foreach (Transform item in root.GetComponentsInChildren<Transform>(true)) item.gameObject.layer = 30;
            foreach (CanvasGroup group in root.GetComponentsInChildren<CanvasGroup>(true))
            { group.alpha = 1f; group.interactable = true; group.blocksRaycasts = true; }
            var cameraObject = new GameObject("Detached Review Camera", typeof(Camera)); SceneManager.MoveGameObjectToScene(cameraObject, preview);
            Camera camera = cameraObject.GetComponent<Camera>(); camera.scene = preview; camera.enabled = false;
            camera.orthographic = true; camera.nearClipPlane = .1f; camera.farClipPlane = 100;
            camera.transform.position = new Vector3(0, 0, -20); camera.cullingMask = 1 << 30;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(131, 179, 189, 255);
            camera.useOcclusionCulling = false; canvas.worldCamera = camera;
            QuizView view = root.GetComponent<QuizView>(); QuizUILayout layout = root.GetComponent<QuizUILayout>();
            Require(view != null && view.IsReady, "Prefab has incomplete view references.");
            var answers = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < data.QuestionCount; i++)
                answers[data.questions[i].questionId] = i < 2 ? data.questions[i].correctChoiceIndex :
                    (data.questions[i].correctChoiceIndex + 1) % data.questions[i].choices.Count;
            int score = Math.Min(2, data.QuestionCount);
            holder.SetActive(true); root.SetActive(true); view.Panel.SetActive(true);
            foreach (Vector2Int screen in new[] { new Vector2Int(1600, 720), new Vector2Int(2048, 1280), new Vector2Int(900, 1600) })
            foreach (bool npc in new[] { true, false })
            {
                currentFixture = data.quizId + " / " + screen + (npc ? " / NPC" : " / fallback");
                target = new RenderTexture(screen.x, screen.y, 24); target.Create(); camera.targetTexture = target;
                float scale = Mathf.Pow(screen.x / reference.x, 1 - match) * Mathf.Pow(screen.y / reference.y, match);
                canvas.scaleFactor = scale; canvasRect.sizeDelta = new Vector2(screen.x, screen.y) / scale;
                camera.orthographicSize = canvasRect.sizeDelta.y * .005f; camera.aspect = (float)screen.x / screen.y;
                view.SetNpcPresentation(npc, "Professor Bhan");
                view.ShowAssessmentResult(data, score, answers);
                layout.ApplyLayout(); Prepare(root, camera, target);
                Validate(view, data, answers, score);
                string file = "Temp/QuizAssessmentReview_" + (data == null ? "Unknown" : data.quizId) +
                    (npc ? "_NPC_" : "_Fallback_") + screen.x + "x" + screen.y;
                CapturePng(camera, target, file + "_Top.png");
                ValidateInput(root, view, camera);
                // UI-only drag: does not invoke Continue or any gameplay callback.
                ValidateScroll(root, view, camera);
                view.choiceScroll.verticalNormalizedPosition = 0f; Prepare(root, camera, target);
                CapturePng(camera, target, file + "_Bottom.png");
                ValidateContinue(root, view, camera);
                view.ShowAssessmentResult(data, score, answers); // Repeated show must not duplicate old cards.
                Require(view.VisibleResultCards.Count == data.QuestionCount, "Result cards duplicated on repeated render.");
                view.ShowAssessmentQuestion(data, 0, answers[data.questions[0].questionId], data.QuestionCount);
                Require(!view.IsAssessmentResult && view.VisibleResultCards.Count == 0 && view.VisibleChoices.Count == data.questions[0].choices.Count,
                    "Returning to question presentation retained result cards.");
                report.AppendLine("PASS: " + file + "; " + data.QuestionCount + " cards, answer colors/text, full text/images, scroll and Continue hit target.");
                camera.targetTexture = null; Release(target); target = null;
            }
            view.Reset(); Require(view.VisibleResultCards.Count == 0 && !view.IsAssessmentResult, "Reset retained a stale result review.");
        }
        finally { RenderTexture.active = oldActive; Release(target); EditorSceneManager.ClosePreviewScene(preview); }
    }

    private static void Validate(QuizView view, QuizData data, Dictionary<string, int> answers, int score)
    {
        Require(view.IsAssessmentResult && view.VisibleResultCards.Count == data.QuestionCount && view.VisibleChoices.Count == 0,
            "Completion review must contain one result card for each question, not playable choices.");
        Require(view.question.text.Contains(score + " / " + data.QuestionCount) && view.finishButton.gameObject.activeSelf &&
            view.finishButton.interactable && !view.submitButton.gameObject.activeSelf && !view.leaveButton.gameObject.activeSelf,
            "Result score or Continue/assessment controls are wrong.");
        Require(view.pauseButton == null || !view.pauseButton.gameObject.activeSelf, "Removed Pause control became visible.");
        Rect previous = default;
        for (int i = 0; i < data.QuestionCount; i++)
        {
            QuizResultCard card = view.VisibleResultCards[i]; QuizQuestion question = data.questions[i];
            int selected = answers[question.questionId]; bool correct = selected == question.correctChoiceIndex;
            Require(card.IsCorrect == correct, "Result card correctness does not match the submitted answer.");
            Require(card.QuestionLabel.text.Contains(question.prompt), "Review lost the full question.");
            QuizChoice selectedChoice = question.choices[selected], correctChoice = question.choices[question.correctChoiceIndex];
            Require(card.YourAnswerLabel.transform.parent.GetComponentsInChildren<TMP_Text>(false).Any(text => text.text == "Your answer") &&
                (string.IsNullOrWhiteSpace(selectedChoice.text) || card.YourAnswerLabel.text.Contains(selectedChoice.text)),
                "Review does not identify the player's submitted answer.");
            if (!correct)
                Require(card.CorrectAnswerLabel.gameObject.activeInHierarchy &&
                    card.CorrectAnswerLabel.transform.parent.GetComponentsInChildren<TMP_Text>(false).Any(text => text.text == "Correct answer") &&
                    (string.IsNullOrWhiteSpace(correctChoice.text) || card.CorrectAnswerLabel.text.Contains(correctChoice.text)),
                    "Wrong answer must show the correct choice.");
            Require(card.Face != null && card.Face.color == (correct ? QuizResultCard.CorrectPaper : QuizResultCard.IncorrectPaper),
                "Answer card lacks its green/red correctness treatment.");
            CheckImage(card.YourAnswerImage, selectedChoice.image);
            if (!correct) CheckImage(card.CorrectAnswerImage, correctChoice.image);
            foreach (TMP_Text text in card.GetComponentsInChildren<TMP_Text>(false))
            { CheckText(text); Inside(text.rectTransform, (RectTransform)card.transform, "Review text leaves its answer card: " + text.name); }
            Rect bounds = Bounds((RectTransform)card.transform, view.choiceContent);
            Require(bounds.xMin >= view.choiceContent.rect.xMin - 1 && bounds.xMax <= view.choiceContent.rect.xMax + 1,
                "Result card extends outside the scroll content width.");
            Require(i == 0 || bounds.yMax <= previous.yMin + .5f, "Stacked result cards overlap.");
            previous = bounds;
        }
        Inside(view.layout.choiceViewport, view.layout.safeContent, "Review viewport leaves the paper.");
        Rect scroll = Bounds(view.layout.choiceViewport, view.layout.safeContent), footer = Bounds(view.layout.footer, view.layout.safeContent);
        Require(!scroll.Overlaps(footer), "Review list overlaps the feedback/Continue footer.");
        foreach (TMP_Text text in new[] { view.title, view.progress, view.question, view.feedback }) CheckText(text);
    }

    private static void CheckText(TMP_Text text)
    {
        Require(text != null && !text.richText, "Remote/authored review strings must render as plain text.");
        text.ForceMeshUpdate(false, true);
        float contentWidth = Mathf.Max(1f, text.rectTransform.rect.width - text.margin.x - text.margin.z);
        Require(!text.isTextTruncated && text.GetPreferredValues(text.text, contentWidth, 0).y <= text.rectTransform.rect.height + 1.5f,
            "Result text clips vertically: " + TextDiagnostics(text));
        Rect rect = text.rectTransform.rect;
        for (int i = 0; i < text.textInfo.characterCount; i++)
        {
            TMP_CharacterInfo character = text.textInfo.characterInfo[i];
            if (!character.isVisible) continue;
            Require(character.bottomLeft.x >= rect.xMin - 2 && character.topRight.x <= rect.xMax + 2 &&
                character.bottomLeft.y >= rect.yMin - 2 && character.topRight.y <= rect.yMax + 2,
                "Visible result glyph leaves its text rectangle: " + TextDiagnostics(text) +
                "; character[" + i + "]=" + character.character + "; bottomLeft=" + character.bottomLeft.ToString("F3") +
                "; topRight=" + character.topRight.ToString("F3") + "; origin=" + character.origin +
                "; baseline=" + character.baseLine + "; ascender=" + character.ascender + "; descender=" + character.descender);
        }
    }

    private static string TextDiagnostics(TMP_Text text)
    {
        QuizResultCard card = text.GetComponentInParent<QuizResultCard>();
        return currentFixture + "; card=" + (card != null ? card.name : "header/footer") + "; field=" + text.name +
            "; text=" + text.text + "; font=" + (text.font != null ? text.font.name : "null") + "; size=" + text.fontSize +
            "; style=" + text.fontStyle + "; rect=" + text.rectTransform.rect + "; preferred=" +
            text.GetPreferredValues(text.text, Mathf.Max(1f, text.rectTransform.rect.width - text.margin.x - text.margin.z), 0) + "; margin=" + text.margin +
            "; bounds=" + text.textBounds + "; scale=" + text.rectTransform.lossyScale;
    }

    private static void CheckImage(Image image, Sprite sprite)
    {
        if (sprite == null) return;
        Require(image != null && image.gameObject.activeInHierarchy && image.sprite == sprite && image.preserveAspect &&
            image.rectTransform.rect.width > 0 && image.rectTransform.rect.height > 0, "Image answer is missing or would distort its aspect ratio.");
    }

    private static void ValidateInput(GameObject root, QuizView view, Camera camera)
    {
        ValidateContinue(root, view, camera);
        Vector2 point = camera.WorldToScreenPoint(view.layout.choiceViewport.TransformPoint(view.layout.choiceViewport.rect.center));
        List<RaycastResult> hits = Raycast(root, point);
        Require(hits.Count > 0 && ExecuteEvents.GetEventHandler<IDragHandler>(hits[0].gameObject) == view.choiceScroll.gameObject,
            "Review scroll area is covered or has no native drag handler.");
    }

    private static void ValidateContinue(GameObject root, QuizView view, Camera camera)
    {
        RectTransform rect = (RectTransform)view.finishButton.transform;
        Vector2 point = camera.WorldToScreenPoint(rect.TransformPoint(rect.rect.center));
        List<RaycastResult> hits = Raycast(root, point);
        Require(hits.Count > 0 && ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject) == view.finishButton.gameObject,
            "Continue cannot receive a native UI click.");
    }

    private static void ValidateScroll(GameObject root, QuizView view, Camera camera)
    {
        ScrollRect scroll = view.choiceScroll;
        Require(scroll.vertical && scroll.viewport == view.layout.choiceViewport && scroll.content == view.choiceContent,
            "Review must reuse the real vertical choice ScrollRect.");
        if (scroll.content.rect.height <= scroll.viewport.rect.height + 1) return;
        Vector2 point = camera.WorldToScreenPoint(scroll.viewport.TransformPoint(scroll.viewport.rect.center));
        List<RaycastResult> hits = Raycast(root, point);
        var pointer = new PointerEventData(null) { button = PointerEventData.InputButton.Left, position = point,
            pressPosition = point, pointerPressRaycast = hits[0], pointerCurrentRaycast = hits[0] };
        scroll.OnInitializePotentialDrag(pointer); scroll.OnBeginDrag(pointer);
        pointer.position = point + Vector2.up * Mathf.Min(220, camera.pixelHeight * .24f);
        scroll.OnDrag(pointer); scroll.OnEndDrag(pointer);
        Require(scroll.verticalNormalizedPosition < .999f, "Dragging the result list did not scroll the submitted answers.");
    }

    private static List<RaycastResult> Raycast(GameObject root, Vector2 position)
    {
        var hits = new List<RaycastResult>();
        root.GetComponent<GraphicRaycaster>().Raycast(new PointerEventData(null) { position = position }, hits);
        return hits.OrderByDescending(hit => hit.depth).ToList();
    }

    private static void BuildLongFixture(QuizData data)
    {
        data.quizId = "detached-long-review"; data.title = "Long Review Layout Test"; data.assessmentMode = true;
        data.questions = Enumerable.Range(0, 12).Select(i => new QuizQuestion {
            questionId = "long-" + i, prompt = "Question " + (i + 1) + ": " + string.Join(" ", Enumerable.Repeat("How does this bridge transfer its loads to the supporting structure?", 5)),
            choices = new List<QuizChoice> { new QuizChoice { text = string.Join(" ", Enumerable.Repeat("The deck transfers forces into beams and then toward the supports.", 5)) },
                new QuizChoice { text = string.Join(" ", Enumerable.Repeat("This is the alternative answer selected to check long review text.", 5)) } },
            correctChoiceIndex = 0, feedback = string.Join(" ", Enumerable.Repeat("This explanation remains readable and scrolls with the reviewed answer.", 5))
        }).ToList();
    }

    private static void Prepare(GameObject root, Camera camera, RenderTexture target)
    {
        Canvas.ForceUpdateCanvases();
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(false)) text.ForceMeshUpdate(false, true);
        foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(false)) { graphic.SetAllDirty(); graphic.Rebuild(CanvasUpdate.PreRender); }
        Canvas.ForceUpdateCanvases(); Render(camera, target); Canvas.ForceUpdateCanvases();
    }
    private static void Render(Camera camera, RenderTexture target)
    {
        if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset)
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        else camera.Render();
    }
    private static void CapturePng(Camera camera, RenderTexture target, string file)
    {
        Render(camera, target); RenderTexture old = RenderTexture.active;
        var pixels = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        try { RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply(); File.WriteAllBytes(file, pixels.EncodeToPNG()); }
        finally { RenderTexture.active = old; Object.DestroyImmediate(pixels); }
    }
    private static Rect Bounds(RectTransform item, RectTransform parent)
    {
        var corners = new Vector3[4]; item.GetWorldCorners(corners);
        Vector2 min = Vector2.positiveInfinity, max = Vector2.negativeInfinity;
        foreach (Vector3 corner in corners) { Vector2 point = parent.InverseTransformPoint(corner); min = Vector2.Min(min, point); max = Vector2.Max(max, point); }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }
    private static void Inside(RectTransform item, RectTransform parent, string message)
    {
        Rect bounds = Bounds(item, parent), area = parent.rect;
        Require(bounds.xMin >= area.xMin - 1 && bounds.xMax <= area.xMax + 1 && bounds.yMin >= area.yMin - 1 && bounds.yMax <= area.yMax + 1, message);
    }
    private static void Write(string report) { Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, report); }
    private static void Release(RenderTexture texture) { if (texture == null) return; texture.Release(); Object.DestroyImmediate(texture); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
#endif
