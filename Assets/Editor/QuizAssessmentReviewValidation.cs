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
            report.AppendLine("PASS: source assets, loaded scenes, active scene and player data unchanged. No live session, progression, camera presentation, save or Continue callbacks ran; only detached choice-selection UI callbacks were exercised.");
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
                answers[data.questions[i].questionId.Trim()] = i < 2 ? data.questions[i].correctChoiceIndex :
                    (data.questions[i].correctChoiceIndex + 1) % data.questions[i].choices.Count;
            List<string> order = ShuffledOrder(data);
            List<QuizChoiceOrderSaveData> choiceOrders = ShuffledChoiceOrders(data);
            int score = QuizProgressUtility.AssessmentScore(data, answers);
            Require(score == Math.Min(2, data.QuestionCount), "The ID-keyed fixture score changed when display order changed.");
            string authoredJson = JsonUtility.ToJson(data);
            holder.SetActive(true); root.SetActive(true); view.Panel.SetActive(true);
            foreach (Vector2Int screen in new[] { new Vector2Int(1600, 720), new Vector2Int(2048, 1280), new Vector2Int(900, 1600) })
            foreach (bool npc in new[] { true, false })
            {
                currentFixture = data.quizId + " / " + screen + (npc ? " / NPC" : " / fallback");
                target = new RenderTexture(screen.x, screen.y, 24); target.Create(); camera.targetTexture = target;
                float scale = Mathf.Pow(screen.x / reference.x, 1 - match) * Mathf.Pow(screen.y / reference.y, match);
                canvas.scaleFactor = scale; canvasRect.sizeDelta = new Vector2(screen.x, screen.y) / scale;
                camera.orthographicSize = canvasRect.sizeDelta.y * .005f; camera.aspect = (float)screen.x / screen.y;
                // Preview scenes do not get the normal gameplay LateUpdate.
                // Resolve stretched panel rectangles after the Canvas resize
                // before asking the layout to arrange new result cards.
                Canvas.ForceUpdateCanvases();
                view.SetNpcPresentation(npc, "Professor Bhan");
                view.ShowAssessmentResult(data, score, answers, order, choiceOrders);
                layout.ApplyLayout(); Prepare(root, camera, target);
                Validate(view, data, answers, score, order, choiceOrders);
                string file = "Temp/QuizAssessmentReview_" + (data == null ? "Unknown" : data.quizId) +
                    (npc ? "_NPC_" : "_Fallback_") + screen.x + "x" + screen.y;
                CapturePng(camera, target, file + "_Top.png");
                ValidateInput(root, view, camera);
                // UI-only drag: does not invoke Continue or any gameplay callback.
                ValidateScroll(root, view, camera);
                view.choiceScroll.verticalNormalizedPosition = 0f; Prepare(root, camera, target);
                CapturePng(camera, target, file + "_Bottom.png");
                ValidateContinue(root, view, camera);
                view.ShowAssessmentResult(data, score, answers, order, choiceOrders); // Repeated show must not duplicate old cards.
                Require(view.VisibleResultCards.Count == data.QuestionCount, "Result cards duplicated on repeated render.");
                // Presentation-only traversal stands in for Next/Previous without
                // clicking a real session callback or changing saved answers.
                foreach (int displayIndex in Enumerable.Range(0, data.QuestionCount)
                    .Concat(Enumerable.Range(0, data.QuestionCount).Reverse()))
                {
                    QuizQuestion expected = data.questions[QuizProgressUtility.QuestionIndexAt(data, order, displayIndex)];
                    int selectedAnswer = answers[expected.questionId.Trim()];
                    view.ShowAssessmentQuestion(data, displayIndex, selectedAnswer, data.QuestionCount, order, choiceOrders);
                    ValidateQuestion(view, expected, displayIndex, data.QuestionCount, selectedAnswer, true, choiceOrders);
                    ValidateChoiceClicks(view, expected, choiceOrders);
                }
                view.ShowQuestion(data, 0, Array.Empty<string>(), order, choiceOrders);
                ValidateQuestion(view, data.questions[QuizProgressUtility.QuestionIndexAt(data, order, 0)],
                    0, data.QuestionCount, -1, false, choiceOrders);
                ValidateChoiceClicks(view, data.questions[QuizProgressUtility.QuestionIndexAt(data, order, 0)], choiceOrders);
                Require(JsonUtility.ToJson(data) == authoredJson, "Shuffled presentation mutated the quiz or its authored choice ordering.");
                Require(QuizProgressUtility.AssessmentScore(data, answers) == score, "Detached choice clicks changed the submitted source-index score.");
                report.AppendLine("PASS: " + file + "; shuffled ID order [" + string.Join(", ", order) + "]; " + data.QuestionCount +
                    " cards, shuffled choice letters, ID/source-keyed selected/correct answers, answer colors/text, full text/images, scroll and Continue hit target; forward/back prompts, per-question display permutations and saved selection remain aligned; every detached displayed choice click reports its original source index.");
                camera.targetTexture = null; Release(target); target = null;
            }
            view.Reset(); Require(view.VisibleResultCards.Count == 0 && !view.IsAssessmentResult, "Reset retained a stale result review.");
        }
        finally { RenderTexture.active = oldActive; Release(target); EditorSceneManager.ClosePreviewScene(preview); }
    }

    private static List<string> ShuffledOrder(QuizData data)
    {
        // Fixed per-asset seed keeps previews reproducible without reading or
        // changing UnityEngine.Random's process-global gameplay state.
        int seed = 27449;
        foreach (char character in data.quizId ?? string.Empty) seed = unchecked(seed * 31 + character);
        UnityEngine.Random.State before = UnityEngine.Random.state;
        List<string> order = QuizProgressUtility.CreateQuestionOrder(data, new QuizProgressSaveData(), new System.Random(seed));
        Require(UnityEngine.Random.state.Equals(before), "Question-order creation changed Unity's global random state.");
        string[] authored = data.questions.Select(question => question.questionId.Trim()).ToArray();
        if (order.SequenceEqual(authored) && order.Count > 1) order.Reverse(); // Guarantee that the preview exercises reordering.
        Require(QuizProgressUtility.IsValidQuestionOrder(data, order) &&
            (order.Count < 2 || !order.SequenceEqual(authored)), "The detached order fixture must be a non-authored complete permutation.");
        for (int index = 0; index < order.Count; index++)
        {
            int sourceIndex = QuizProgressUtility.QuestionIndexAt(data, order, index);
            Require(sourceIndex >= 0 && data.questions[sourceIndex].questionId.Trim() == order[index],
                "Question-order lookup crossed stable IDs.");
        }
        return order;
    }

    private static List<QuizChoiceOrderSaveData> ShuffledChoiceOrders(QuizData data)
    {
        int seed = 11939;
        foreach (char character in data.quizId ?? string.Empty) seed = unchecked(seed * 31 + character);
        UnityEngine.Random.State before = UnityEngine.Random.state;
        // The question-order fixture is intentionally not stored in this empty
        // progress record: an old saved question order with no saved choice
        // orders is a legacy identity-choice attempt, not a fresh shuffle.
        List<QuizChoiceOrderSaveData> orders = QuizProgressUtility.CreateChoiceOrders(data,
            new QuizProgressSaveData(), new System.Random(seed));
        Require(UnityEngine.Random.state.Equals(before), "Choice-order creation changed Unity's global random state.");
        Require(orders != null && orders.Count == data.QuestionCount, "Fresh choice orders are missing a stable question ID.");
        foreach (QuizQuestion question in data.questions)
        {
            QuizChoiceOrderSaveData saved = orders.Single(item => item != null &&
                string.Equals(item.questionId, question.questionId.Trim(), StringComparison.Ordinal));
            int[] authored = Enumerable.Range(0, question.choices.Count).ToArray();
            if (saved.choiceIndices.SequenceEqual(authored) && saved.choiceIndices.Count > 1) saved.choiceIndices.Reverse();
            var choiceOrder = QuizProgressUtility.ChoiceOrderFor(question.questionId, orders);
            Require(choiceOrder != null && choiceOrder.Count == authored.Length && choiceOrder.OrderBy(index => index).SequenceEqual(authored) &&
                (authored.Length < 2 || !choiceOrder.SequenceEqual(authored)), "The detached choice fixture must be a complete non-authored permutation.");
            for (int displayIndex = 0; displayIndex < choiceOrder.Count; displayIndex++)
            {
                int sourceIndex = QuizProgressUtility.ChoiceIndexAt(question, choiceOrder, displayIndex);
                Require(sourceIndex == choiceOrder[displayIndex] &&
                    QuizProgressUtility.ChoiceDisplayIndex(question, choiceOrder, sourceIndex) == displayIndex,
                    "Choice source/display mapping crossed a per-question permutation.");
            }
        }
        return orders;
    }

    private static void Validate(QuizView view, QuizData data, Dictionary<string, int> answers, int score,
        IReadOnlyList<string> order, IReadOnlyList<QuizChoiceOrderSaveData> choiceOrders)
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
            QuizResultCard card = view.VisibleResultCards[i];
            QuizQuestion question = data.questions[QuizProgressUtility.QuestionIndexAt(data, order, i)];
            int selected = answers[question.questionId.Trim()]; bool correct = selected == question.correctChoiceIndex;
            Require(card.IsCorrect == correct, "Result card correctness does not match the submitted answer.");
            Require(card.StatusLabel.text.StartsWith("Question " + (i + 1) + "  -  ", StringComparison.Ordinal) &&
                card.QuestionLabel.text == question.prompt, "Review numbering/prompt does not match shuffled display order.");
            QuizChoice selectedChoice = question.choices[selected], correctChoice = question.choices[question.correctChoiceIndex];
            var choiceOrder = QuizProgressUtility.ChoiceOrderFor(question.questionId, choiceOrders);
            int selectedDisplayIndex = QuizProgressUtility.ChoiceDisplayIndex(question, choiceOrder, selected);
            int correctDisplayIndex = QuizProgressUtility.ChoiceDisplayIndex(question, choiceOrder, question.correctChoiceIndex);
            Require(card.YourAnswerLabel.transform.parent.GetComponentsInChildren<TMP_Text>(false).Any(text => text.text == "Your answer") &&
                card.YourAnswerLabel.text == AnswerLabel(selectedDisplayIndex, selectedChoice),
                "Review does not identify the player's submitted answer.");
            if (!correct)
                Require(card.CorrectAnswerLabel.gameObject.activeInHierarchy &&
                    card.CorrectAnswerLabel.transform.parent.GetComponentsInChildren<TMP_Text>(false).Any(text => text.text == "Correct answer") &&
                    card.CorrectAnswerLabel.text == AnswerLabel(correctDisplayIndex, correctChoice),
                    "Wrong answer must show the correct choice.");
            else Require(!card.CorrectAnswerLabel.gameObject.activeInHierarchy, "A correctly answered card unexpectedly displays a correction.");
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

    private static string AnswerLabel(int index, QuizChoice choice) =>
        (index < 26 ? ((char)('A' + index)).ToString() : (index + 1).ToString()) + ". " + (choice.text ?? string.Empty);

    private static void ValidateQuestion(QuizView view, QuizQuestion expected, int displayIndex, int total,
        int selected, bool assessment, IReadOnlyList<QuizChoiceOrderSaveData> choiceOrders)
    {
        Require(!view.IsAssessmentResult && view.VisibleResultCards.Count == 0 && view.VisibleChoices.Count == expected.choices.Count,
            "Returning to ordered question presentation retained result cards or another question's choice count.");
        Require(view.question.text == expected.prompt && view.progress.text.StartsWith("Question " + (displayIndex + 1) + " of " + total,
            StringComparison.Ordinal), "Next/Previous presentation shows another stable question ID's prompt or display number.");
        for (int index = 0; index < expected.choices.Count; index++)
        {
            int sourceIndex = QuizProgressUtility.ChoiceIndexAt(expected,
                QuizProgressUtility.ChoiceOrderFor(expected.questionId, choiceOrders), index);
            QuizChoice choice = expected.choices[sourceIndex]; QuizChoiceView card = view.VisibleChoices[index];
            string letter = index < 26 ? ((char)('A' + index)).ToString() : (index + 1).ToString();
            Require(card.number != null && card.number.text == letter && card.label != null &&
                card.label.text == (choice.text ?? string.Empty), "Displayed choice letter/content does not match its per-question permutation.");
            Require(card.face != null && card.face.color == (sourceIndex == selected ? QuizUILayout.Gold : QuizUILayout.Cream),
                "The selected choice no longer belongs to this stable question ID.");
            CheckImage(card.artwork, choice.image);
        }
        if (assessment)
            Require(view.previousButton != null && view.previousButton.interactable == (displayIndex > 0) &&
                view.nextButton.interactable == (displayIndex + 1 < total) && view.submitButton.interactable,
                "Ordered assessment Previous/Next/Submit availability changed.");
        else Require(!view.submitButton.interactable, "Retry-mode question should start without a transient selection.");
    }

    private static void ValidateChoiceClicks(QuizView view, QuizQuestion question, IReadOnlyList<QuizChoiceOrderSaveData> choiceOrders)
    {
        int received = -1, callbackCount = 0;
        Action<int> observer = sourceIndex => { received = sourceIndex; callbackCount++; };
        view.ChoiceSelected += observer;
        try
        {
            var choiceOrder = QuizProgressUtility.ChoiceOrderFor(question.questionId, choiceOrders);
            for (int displayIndex = 0; displayIndex < view.VisibleChoices.Count; displayIndex++)
            {
                QuizChoiceView card = view.VisibleChoices[displayIndex];
                Require(card.button != null && card.button.interactable && card.button.onClick.GetPersistentEventCount() == 0,
                    "Detached choice click must have only its UI binding, with no authored gameplay callback.");
                int before = callbackCount;
                // QuizUIManager was removed while the fixture root was inactive.
                // This native button click reaches only QuizView's UI event and
                // our observer; no live quiz session or save callback exists.
                ExecuteEvents.Execute(card.button.gameObject,
                    new PointerEventData(null) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
                int expectedSource = QuizProgressUtility.ChoiceIndexAt(question, choiceOrder, displayIndex);
                Require(callbackCount == before + 1 && received == expectedSource,
                    "Displayed choice click reported a display index instead of its original source answer.");
                for (int index = 0; index < view.VisibleChoices.Count; index++)
                    Require(view.VisibleChoices[index].face.color == (index == displayIndex ? QuizUILayout.Gold : QuizUILayout.Cream),
                        "Clicking a shuffled choice highlighted another source answer.");
            }
        }
        finally { view.ChoiceSelected -= observer; }
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
        if (sprite == null)
        { Require(image == null || !image.gameObject.activeInHierarchy, "A different question's image leaked into a text-only answer."); return; }
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
        QuizUILayout layout = root.GetComponent<QuizUILayout>();
        if (layout != null) layout.ApplyLayout();
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
