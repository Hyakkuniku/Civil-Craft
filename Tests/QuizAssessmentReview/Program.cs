using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

internal static class Program
{
    private static int checks;
    private static readonly TMP_FontAsset Font = new() { name = "Authored gameplay font" };
    private static readonly Sprite Paper = new() { name = "Authored rounded card" };

    private static void Main()
    {
        foreach (int count in new[] { 1, 2, 5, 12 }) VerifyAssessmentReview(count);
        VerifyMissingAnswers();
        VerifyRetryModeUnchanged();
        VerifyCardsAndImages();
        VerifyMeasuredLayout();
        VerifySourceBoundaries();
        Console.WriteLine($"PASS: {checks} production-source assessment review, result-card, and integration assertions. Unity hierarchy/input/TMP measurement are doubles; actual phone/tablet rendering is checked separately. No live saves, scenes, accounts, rewards, or network accessed.");
    }

    private static void VerifyAssessmentReview(int count)
    {
        QuizData data = Quiz(count);
        QuizView view = View();
        var answers = new Dictionary<string, int>();
        // Deliberately reverse insertion order; the review must follow authored question order.
        for (int i = count - 1; i >= 0; i--) answers[data.questions[i].questionId.Trim()] = Selection(data.questions[i], i);
        string fingerprint = data.ContentFingerprint;
        Check(QuizProgressUtility.HasAllAssessmentAnswers(data, answers), "Review fixture is a complete submitted assessment.");
        int expectedScore = Enumerable.Range(0, count).Count(i => i % 3 == 0);
        Check(QuizProgressUtility.AssessmentScore(data, answers) == expectedScore, "The original numeric grading remains in use.");

        int completionEvents = 0, answerEvents = 0;
        view.FinishRequested += () => completionEvents++;
        view.ChoiceSelected += _ => answerEvents++;
        for (int i = 0; i < count; i++)
        {
            view.ShowAssessmentQuestion(data, i, answers[data.questions[i].questionId.Trim()], count);
            Check(!view.IsAssessmentResult && view.VisibleResultCards.Count == 0, "Questions never show an answer key before submit.");
            Check(view.VisibleChoices.Count == data.questions[i].choices.Count, "Question uses the actual authored choice count.");
            Check(view.VisibleChoices.All(c => c.face.color != QuizResultCard.CorrectPaper && c.face.color != QuizResultCard.IncorrectPaper), "Question choices do not reveal correctness colors.");
            Check(!view.finishButton.gameObject.activeSelf && view.submitButton.gameObject.activeSelf, "Assessment controls remain pre-submission controls.");
        }
        var liveChoices = view.VisibleChoices.ToArray();
        view.ShowAssessmentResult(data, expectedScore, answers);
        Check(view.IsAssessment && view.IsAssessmentResult, "Assessment result state is explicit.");
        Check(view.VisibleChoices.Count == 0 && liveChoices.All(c => !c.gameObject.activeSelf && c.gameObject.destroyed), "Submission removes the live selectable answer cards.");
        Check(view.VisibleResultCards.Count == count, "Every authored question receives exactly one review card.");
        Check(view.question.text == "Your score: " + expectedScore + " / " + count, "Existing score summary remains visible.");
        Check(view.finishButton.gameObject.activeSelf && view.finishButton.interactable, "Continue remains available regardless of score.");
        foreach (Button hidden in new[] { view.previousButton, view.nextButton, view.submitButton, view.leaveButton, view.retrySaveButton, view.pauseButton })
            Check(!hidden.gameObject.activeSelf, "Review hides question-only, Leave, and legacy Pause controls.");
        Check(view.questionScroll.verticalNormalizedPosition == 1 && view.choiceScroll.verticalNormalizedPosition == 1, "Submitted review starts scrolled to the top.");
        for (int i = 0; i < count; i++)
        {
            QuizResultCard card = view.VisibleResultCards[i];
            QuizQuestion question = data.questions[i];
            int chosen = answers[question.questionId.Trim()];
            bool correct = chosen == question.correctChoiceIndex;
            Check(card.QuestionLabel.text == question.prompt && card.StatusLabel.text.StartsWith("Question " + (i + 1) + " "), "Cards keep authored order, question numbering, and exact question copy.");
            Check(card.IsCorrect == correct && card.Face.color == (correct ? QuizResultCard.CorrectPaper : QuizResultCard.IncorrectPaper), "Selected answers use green for correct and red for incorrect.");
            Check(card.YourAnswerLabel.text.EndsWith(question.choices[chosen].text ?? ""), "Card displays the actual player answer, not the correct answer in its place.");
            Check(card.YourAnswerImage.sprite == question.choices[chosen].image, "Player-selected image is preserved.");
            Check(card.CorrectAnswerLabel.gameObject.activeInHierarchy == !correct, "Only incorrect answers expose the correction section.");
            if (!correct)
            {
                Check(card.CorrectAnswerLabel.text.EndsWith(question.choices[question.correctChoiceIndex].text ?? ""), "Incorrect cards show the correct text answer.");
                Check(card.CorrectAnswerImage.sprite == question.choices[question.correctChoiceIndex].image, "Incorrect cards show the correct image answer.");
            }
            Check(card.FeedbackLabel.text.Contains(question.feedback), "Optional explanation is retained in the result review.");
            Check(card.GetComponentsInChildren<Button>(true).Length == 0, "Review cards are read-only, not answer controls.");
            Check(card.GetComponentsInChildren<TMP_Text>(true).All(t => !t.richText && !t.raycastTarget && t.font == Font), "Remote/authored copy is plain text in the existing font and does not block scroll input.");
        }
        Check(data.ContentFingerprint == fingerprint && answers.Count == count, "Review never edits quiz content, saved identities, or player answers.");
        Check(answerEvents == 0 && completionEvents == 0, "Rendering a result does not submit new choices or continue NPC progression.");
        var firstCards = view.VisibleResultCards.ToArray();
        string firstAnswer = firstCards[0].YourAnswerLabel.text;
        answers[data.questions[0].questionId.Trim()] = (answers[data.questions[0].questionId.Trim()] + 1) % data.questions[0].choices.Count;
        Check(firstCards[0].YourAnswerLabel.text == firstAnswer, "A materialized result does not follow later dictionary mutation.");
        view.ShowAssessmentResult(data, expectedScore, answers);
        Check(view.VisibleResultCards.Count == count && firstCards.All(c => !c.gameObject.activeSelf && c.gameObject.destroyed), "Repeated results replace old cards without duplication or delayed-frame interception.");
        view.SetBusy(true); view.finishButton.onClick.Invoke();
        Check(completionEvents == 0, "Busy durable-save state still blocks Continue requests.");
        view.SetBusy(false); view.finishButton.onClick.Invoke();
        Check(completionEvents == 1, "The existing Continue event is preserved.");
        var finalCards = view.VisibleResultCards.ToArray();
        view.ShowAssessmentQuestion(data, 0, -1, 0);
        Check(view.VisibleResultCards.Count == 0 && finalCards.All(c => !c.gameObject.activeSelf), "Reopening a question clears submitted-review content.");
        view.ShowAssessmentResult(data, expectedScore, answers);
        finalCards = view.VisibleResultCards.ToArray(); view.Reset();
        Check(!view.IsAssessmentResult && view.VisibleResultCards.Count == 0 && finalCards.All(c => !c.gameObject.activeSelf), "Session reset clears all review state and content.");
    }

    private static void VerifyMissingAnswers()
    {
        QuizData data = Quiz(3); QuizView view = View();
        foreach (IReadOnlyDictionary<string, int> values in new IReadOnlyDictionary<string, int>[]
        { null, new Dictionary<string, int>(), new Dictionary<string, int> { [data.questions[0].questionId.Trim()] = -1, [data.questions[1].questionId.Trim()] = 500 } })
        {
            view.ShowAssessmentResult(data, 0, values);
            Check(view.VisibleResultCards.Count == 3, "Legacy or incomplete records still produce a safe review.");
            foreach (QuizResultCard card in view.VisibleResultCards)
            {
                Check(!card.IsCorrect && card.Face.color == QuizResultCard.IncorrectPaper, "Missing/invalid selection is never mistaken for correct index zero.");
                Check(card.YourAnswerLabel.text == "No answer" && !card.YourAnswerImage.gameObject.activeSelf, "Missing answers are explicit and have no stale artwork.");
                Check(card.CorrectAnswerLabel.gameObject.activeInHierarchy, "A missing answer safely exposes the authored correct answer.");
            }
        }
        view.ShowAssessmentResult(data, 0); // Existing detached tools/two-argument callers remain safe.
        Check(view.VisibleResultCards.All(c => c.YourAnswerLabel.text == "No answer"), "The two-argument compatibility path does not invent player choices.");
    }

    private static void VerifyRetryModeUnchanged()
    {
        QuizData data = Quiz(2); data.assessmentMode = false; QuizView view = View();
        view.ShowQuestion(data, 0, null);
        Check(!view.IsAssessment && !view.IsAssessmentResult && view.VisibleResultCards.Count == 0, "Practice begins without result review state.");
        Check(!view.submitButton.interactable, "Practice still requires selecting before Submit.");
        view.SetSelection(0); Check(view.submitButton.interactable, "Practice selection enables Submit.");
        view.ShowFeedback(false, data.questions[0].feedback);
        Check(view.feedback.text == "Try again." && view.VisibleResultCards.Count == 0, "Wrong practice feedback still permits retries without revealing answer keys.");
        view.ShowFeedback(true, data.questions[0].feedback);
        Check(view.feedback.text.Contains(data.questions[0].feedback) && view.nextButton.gameObject.activeSelf, "Correct practice feedback and Next remain unchanged.");
        view.ShowCompletion(data);
        Check(view.VisibleResultCards.Count == 0 && view.finishButton.gameObject.activeSelf, "Practice completion stays distinct from assessment review.");
    }

    private static void VerifyCardsAndImages()
    {
        var root = new GameObject("Detached card root", typeof(RectTransform));
        foreach (int choices in new[] { 2, 4, 27, 31 })
        {
            QuizQuestion question = Question(0, choices);
            foreach (int selected in Enumerable.Range(0, choices).Concat(new[] { -5, choices + 10 }))
            {
                QuizResultCard card = QuizResultCard.Create(root.transform, Font, Paper, question, 4, selected);
                Check(card.Face.sprite == Paper && card.Face.color == (selected == question.correctChoiceIndex ? QuizResultCard.CorrectPaper : QuizResultCard.IncorrectPaper), "Card reuses paper styling with correctness color.");
                Check(card.YourAnswerImage.preserveAspect && card.CorrectAnswerImage.preserveAspect && card.YourAnswerImage.type == Image.Type.Simple, "Answer sprites retain their aspect ratio and are not sliced as UI backgrounds.");
                Check(card.GetComponentsInChildren<Graphic>(true).All(g => !g.raycastTarget), "All result-card graphics allow the parent scrolling surface to receive touches.");
                Check(card.IsCorrect == (selected == question.correctChoiceIndex), "Only the exact authored correct index is green for arbitrary choice counts.");
                if (selected >= 26 && selected < choices) Check(card.YourAnswerLabel.text.StartsWith((selected + 1) + ". "), "Choices beyond Z use a readable numeric identifier.");
            }
        }
        QuizResultCard missing = QuizResultCard.Create(root.transform, Font, null, null, 1, 0);
        Check(!missing.IsCorrect && missing.YourAnswerLabel.text == "No answer" && missing.CorrectAnswerLabel.text == "Answer unavailable", "A missing record degrades safely without false answer content.");
        var shortQuestion = Question(0, 2); shortQuestion.feedback = null;
        QuizResultCard emptyFeedback = QuizResultCard.Create(root.transform, Font, Paper, shortQuestion, 1, 0);
        Check(!emptyFeedback.FeedbackLabel.gameObject.activeSelf, "An optional blank explanation does not create a blank visible explanation section.");
    }

    private static void VerifyMeasuredLayout()
    {
        var root = new GameObject("Detached layout root", typeof(RectTransform));
        foreach (float width in new[] { 240f, 400f, 760f, 1100f })
        foreach (float unit in new[] { .58f, 1f, 1.15f })
        foreach (float physicalScale in new[] { .45f, 1f, 2f })
        foreach (int selected in new[] { 0, 1, 2 })
        {
            QuizQuestion question = Question(2, 4);
            question.prompt = string.Join(" ", Enumerable.Repeat("A long structural question with mixed image and text choices <b>must stay plain</b>.", 4));
            question.choices[0].text = string.Join(" ", Enumerable.Repeat("Long caption that should wrap and scroll instead of shrinking.", 5));
            QuizResultCard card = QuizResultCard.Create(root.transform, Font, Paper, question, 3, selected);
            string before = card.QuestionLabel.text;
            float height = card.RequiredHeight(width, unit, physicalScale);
            var rect = (RectTransform)card.transform; rect.sizeDelta = new Vector2(width, height);
            card.Layout(width, height, unit, physicalScale);
            Check(float.IsFinite(height) && height > 0, "Variable content always receives a finite positive measured card height.");
            foreach (TMP_Text label in card.GetComponentsInChildren<TMP_Text>())
            {
                Check(label.fontSize * physicalScale >= 18f - .01f, "Result text respects a physical readability floor.");
                float contentWidth = Math.Max(1, label.rectTransform.rect.width - label.margin.x - label.margin.z);
                Vector2 preferred = label.GetPreferredValues(label.text, contentWidth, 0);
                Check(label.rectTransform.rect.height + .01f >= preferred.y, "Every visible result label has enough measured height for its wrapped content.");
                Check(label.margin.x >= label.fontSize * .12f - .01f && label.margin.y >= label.fontSize * .12f - .01f &&
                      label.margin.z >= label.fontSize * .12f - .01f && label.margin.w >= label.fontSize * .12f - .01f,
                    "Measured labels retain a font-relative glyph gutter on every edge.");
                // The authored Bekind cap line (61) exceeds ascent (56.3) at
                // point size 72. Its real capital quads need more than zero
                // margin above the nominal line box, even for short labels.
                float capitalOvershoot = label.fontSize * ((61f - 56.3f) / 72f);
                Check(label.margin.y > capitalOvershoot && label.margin.x >= 4f * unit - .01f && label.margin.w >= 4f * unit - .01f,
                    "Bekind capital-glyph overshoot fits inside the measured inset instead of escaping the card.");
                float bottom = TopOffset(label.transform, card.transform) + label.rectTransform.rect.height;
                Check(bottom <= height + .01f, "No result label spills below its measured card.");
                Check(!label.enableAutoSizing && label.enableWordWrapping, "Review retains readable text and wraps instead of tiny shrink-to-fit.");
            }
            Check(card.QuestionLabel.text == before && Math.Abs(card.RequiredHeight(width, unit, physicalScale) - height) < .01f, "Repeated layout preserves content and has stable dimensions.");
        }
    }

    private static void VerifySourceBoundaries()
    {
        string root = FindRoot();
        string manager = Read("Assets/Script/UI/Quizzes/QuizUIManager.cs");
        string view = Read("Assets/Script/UI/Quizzes/QuizView.cs");
        string layout = Read("Assets/Script/UI/Quizzes/QuizUILayout.cs");
        string card = Read("Assets/Script/UI/Quizzes/QuizResultCard.cs");
        string showResult = Method(manager, "private void ShowAssessmentResult()");
        Check(showResult.Contains("QuizProgressUtility.AssessmentScore(quiz, assessmentAnswers), assessmentAnswers"), "Session passes the real submitted/resumed answer dictionary along with the original score.");
        Check(Method(manager, "private void Submit()").Contains("if (CommitProgress(false)) ShowAssessmentResult();"), "New submission shows answer review only after the durable save succeeds.");
        Check(Method(manager, "private bool Open(").Contains("if (assessmentSubmitted) ShowAssessmentResult();"), "Reopening a submitted session restores its real saved answers as a result review.");
        Check(Method(manager, "private void RetrySave()").Contains("if (assessmentSubmitted) ShowAssessmentResult();"), "Retrying a failed submission resumes the review after the normal save path.");
        foreach (string signature in new[] { "public void ShowQuestion(", "public void ShowCompletion(", "public void Reset()" })
            Check(Method(view, signature).Contains("ClearResultCards()"), "Every replacement/reset path cleans up review cards: " + signature);
        string results = Method(view, "public void ShowAssessmentResult(");
        Check(results.Contains("ShowCompletion(data)") && results.Contains("for (int i = 0; i < data.QuestionCount; i++)"), "Only completed assessment display creates cards in authored order.");
        Check(Method(layout, "public void ApplyLayout()").Contains("!view.IsAssessmentResult") && Method(layout, "public void ApplyLayout()").Contains("if (view.IsAssessmentResult) LayoutResults"), "Review uses a single-column result layout, separate from ordinary choice grids.");
        string resultLayout = Method(layout, "private void LayoutResults(");
        Check(resultLayout.Contains("RequiredHeight") && resultLayout.Contains("Mathf.Max(choiceViewport.rect.height, y)"), "Scrollable review content grows to the full measured result-card height.");
        foreach (string forbidden in new[] { "PlayerDataManager", "TrySave", "QuizOutcome", "OnComplete", "NPCProgression", "Time.timeScale", "correctChoiceIndex =", "assessmentMode =", "Button" })
            Check(!card.Contains(forbidden), "Result cards cannot mutate save, progression, rewards, controls, or quiz data: " + forbidden);
        Check(!Read("Assets/Script/UI/Quizzes/QuizChoiceView.cs").Contains("correctChoiceIndex"), "Pre-submit choice cards remain ignorant of answer correctness.");

        string Read(string path) => File.ReadAllText(Path.Combine(root, path));
    }

    private static QuizView View()
    {
        var obj = new GameObject("Detached Quiz", typeof(RectTransform), typeof(QuizView));
        QuizView view = obj.GetComponent<QuizView>(); view.panel = obj;
        view.title = UnityEngine.Object.Child<TextMeshProUGUI>(obj.transform, "Title"); view.title.font = Font;
        view.progress = UnityEngine.Object.Child<TextMeshProUGUI>(obj.transform, "Progress");
        view.question = UnityEngine.Object.Child<TextMeshProUGUI>(obj.transform, "Question");
        view.feedback = UnityEngine.Object.Child<TextMeshProUGUI>(obj.transform, "Feedback");
        view.submitButton = Button("Submit"); view.nextButton = Button("Next"); view.finishButton = Button("Finish");
        view.leaveButton = Button("Leave"); view.retrySaveButton = Button("Retry"); view.pauseButton = Button("Pause"); view.previousButton = Button("Previous");
        view.questionScroll = UnityEngine.Object.Child<ScrollRect>(obj.transform, "Question Scroll");
        view.choiceScroll = UnityEngine.Object.Child<ScrollRect>(obj.transform, "Choice Scroll");
        view.choiceContent = UnityEngine.Object.Child<RectTransform>(view.choiceScroll.transform, "Choice Content");
        view.layout = obj.AddComponent<QuizUILayout>();
        var template = new GameObject("Choice template", typeof(RectTransform), typeof(QuizChoiceView), typeof(Image), typeof(Button));
        template.transform.SetParent(view.choiceContent, false);
        view.choiceTemplate = template.GetComponent<QuizChoiceView>();
        view.choiceTemplate.face = template.GetComponent<Image>(); view.choiceTemplate.face.sprite = Paper;
        view.choiceTemplate.button = template.GetComponent<Button>();
        view.choiceTemplate.label = UnityEngine.Object.Child<TextMeshProUGUI>(template.transform, "Label"); view.choiceTemplate.label.font = Font;
        view.choiceTemplate.number = UnityEngine.Object.Child<TextMeshProUGUI>(template.transform, "Number");
        view.choiceTemplate.artwork = UnityEngine.Object.Child<Image>(template.transform, "Artwork");
        return view;

        Button Button(string label)
        {
            var button = UnityEngine.Object.Child<Button>(obj.transform, label);
            UnityEngine.Object.Child<TextMeshProUGUI>(button.transform, "Label").text = label; return button;
        }
    }

    private static QuizData Quiz(int count)
    {
        var quiz = new QuizData { quizId = "assessment-review-test", title = "Assessment Test", assessmentMode = true };
        for (int i = 0; i < count; i++) quiz.questions.Add(Question(i, 2 + i % 7));
        Check(quiz.TryValidate(out string error), "Fixture validates: " + error); return quiz;
    }
    private static QuizQuestion Question(int i, int count)
    {
        var question = new QuizQuestion { questionId = "  question-" + i + "  ", prompt = "Question " + i + " <b>plain text</b>", correctChoiceIndex = i % count, feedback = "Explanation " + i + " helps the player learn." };
        for (int choice = 0; choice < count; choice++)
        {
            bool image = choice % 3 != 0, text = choice % 3 != 1;
            question.choices.Add(new QuizChoice { text = text ? "Answer " + i + ":" + choice : null, image = image ? new Sprite { name = "Sprite " + i + ":" + choice } : null, imageFingerprint = image ? "stable-image-" + i + ":" + choice : null });
        }
        return question;
    }
    private static int Selection(QuizQuestion question, int index) => index % 3 == 0 ? question.correctChoiceIndex : (question.correctChoiceIndex + 1) % question.choices.Count;
    private static float TopOffset(Transform child, Transform root)
    {
        float result = 0;
        for (Transform current = child; current != root && current != null; current = current.parent) result -= ((RectTransform)current).anchoredPosition.y;
        return result;
    }
    private static string Method(string text, string signature)
    {
        int start = text.IndexOf(signature, StringComparison.Ordinal);
        if (start < 0) throw new InvalidOperationException("Production method not found: " + signature);
        int opening = text.IndexOf('{', start), depth = 0;
        for (int i = opening; i < text.Length; i++)
        { if (text[i] == '{') depth++; else if (text[i] == '}' && --depth == 0) return text.Substring(start, i - start + 1); }
        throw new InvalidOperationException("Unterminated method: " + signature);
    }
    private static string FindRoot()
    {
        for (DirectoryInfo directory = new(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Assets/Script/UI/Quizzes/QuizView.cs"))) return directory.FullName;
        throw new InvalidOperationException("Unity project not found.");
    }
    private static void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
}
