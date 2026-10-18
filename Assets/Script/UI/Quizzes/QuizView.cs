using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Presentation only. Session/progression ownership lives outside the hidden panel.</summary>
[DisallowMultipleComponent]
public sealed class QuizView : MonoBehaviour
{
    public GameObject panel;
    public TMP_Text title;
    public TMP_Text progress;
    public TMP_Text question;
    public TMP_Text feedback;
    public Button submitButton;
    public Button nextButton;
    public Button finishButton;
    public Button leaveButton;
    public Button retrySaveButton;
    [HideInInspector] public Button pauseButton; // Optional legacy reference; never a visible quiz control.
    public Button previousButton;
    public ScrollRect questionScroll;
    public ScrollRect choiceScroll;
    public RectTransform choiceContent;
    public QuizChoiceView choiceTemplate;
    public QuizUILayout layout;
    public GameObject Panel => panel;
    public bool IsReady => panel != null && title != null && progress != null && question != null && feedback != null &&
        submitButton != null && nextButton != null && finishButton != null && leaveButton != null && retrySaveButton != null &&
        choiceTemplate != null && choiceContent != null && layout != null;
    public event Action<int> ChoiceSelected;
    public event Action SubmitRequested;
    public event Action NextRequested;
    public event Action PreviousRequested;
    public event Action FinishRequested;
    public event Action LeaveRequested;
    public event Action RetrySaveRequested;
    public event Action PauseRequested;

    private readonly List<QuizChoiceView> choices = new List<QuizChoiceView>();
    public IReadOnlyList<QuizChoiceView> VisibleChoices => choices;
    private readonly List<QuizResultCard> resultCards = new List<QuizResultCard>();
    public IReadOnlyList<QuizResultCard> VisibleResultCards => resultCards;
    private bool wired;
    private int selection = -1;
    private bool answered;
    private bool complete;
    private bool busy;
    private bool saveError;
    private bool assessment;
    private bool allAssessmentAnswers;
    private bool hasPrevious, hasNext;
    public bool HasNpcPresentation { get; private set; }

    /// <summary>Uses the real asking NPC when available; otherwise keeps a usable full-screen quiz.</summary>
    public void SetNpcPresentation(bool active, string npcName)
    {
        HasNpcPresentation = active;
        if (layout != null) layout.SetNpcPresentation(active, npcName);
    }

    private void Awake() { Wire(); }
    private void Wire()
    {
        if (wired) return;
        wired = true;
        if (submitButton != null) submitButton.onClick.AddListener(() => { if (CanRequest()) SubmitRequested?.Invoke(); });
        if (nextButton != null) nextButton.onClick.AddListener(() => { if (CanRequest()) NextRequested?.Invoke(); });
        if (finishButton != null) finishButton.onClick.AddListener(() => { if (CanRequest()) FinishRequested?.Invoke(); });
        if (leaveButton != null) leaveButton.onClick.AddListener(() => { if (CanRequest()) LeaveRequested?.Invoke(); });
        if (retrySaveButton != null) retrySaveButton.onClick.AddListener(() => { if (CanRequest()) RetrySaveRequested?.Invoke(); });
        HideLegacyPauseControl();
        BindPreviousControl();
    }

    public void HideLegacyPauseControl()
    {
        if (pauseButton == null) return;
        pauseButton.interactable = false;
        pauseButton.gameObject.SetActive(false);
    }

    /// <summary>Optional integration hook for the normal outer Pause flow; no on-screen quiz button invokes it.</summary>
    public void RequestPause() { if (CanRequest()) PauseRequested?.Invoke(); }

    private bool CanRequest() => Panel != null && Panel.activeInHierarchy && !busy;

    public void ShowQuestion(QuizData data, int index, IReadOnlyCollection<string> correctIds)
    {
        Wire();
        ClearChoices();
        ClearResultCards();
        assessment = false;
        selection = -1; answered = false; complete = false; busy = false; saveError = false;
        title.text = data.title;
        progress.text = "Question " + (index + 1) + " of " + data.questions.Count;
        QuizQuestion item = data.questions[index];
        question.text = item.prompt;
        question.richText = false;
        feedback.text = "Choose one answer, then press Submit.";
        feedback.color = QuizUILayout.Ink;
        if (choiceTemplate != null)
        {
            choiceTemplate.gameObject.SetActive(false);
            for (int i = 0; i < item.choices.Count; i++)
            {
                QuizChoiceView card = Instantiate(choiceTemplate, choiceContent);
                card.name = "Choice " + (i + 1);
                card.gameObject.SetActive(true);
                card.Bind(item.choices[i], i, Choose);
                choices.Add(card);
            }
        }
        RefreshButtons();
        ResetScroll();
        if (layout != null) layout.ApplyLayout();
    }

    private void Choose(int index)
    {
        if (busy || answered || complete || saveError) return;
        SetSelection(index);
        ChoiceSelected?.Invoke(index);
    }

    public void SetSelection(int index)
    {
        if (answered || complete || busy || saveError) return;
        selection = index >= 0 && index < choices.Count ? index : -1;
        feedback.text = assessment ? "Choose one answer. You may review your answers before submitting." : "Choose one answer, then press Submit.";
        feedback.color = QuizUILayout.Ink;
        for (int i = 0; i < choices.Count; i++) choices[i].SetSelected(i == selection, answered || busy || saveError);
        RefreshButtons();
    }

    public void ShowFeedback(bool correct, string explanation)
    {
        busy = false; saveError = false; answered = correct;
        feedback.text = correct ? "Correct!" + (string.IsNullOrWhiteSpace(explanation) ? string.Empty : " " + explanation) : "Try again.";
        feedback.color = correct ? new Color32(47, 105, 63, 255) : new Color32(162, 65, 37, 255);
        for (int i = 0; i < choices.Count; i++) choices[i].SetSelected(i == selection, answered);
        RefreshButtons();
        if (layout != null) layout.ApplyLayout();
    }

    public void ShowCompletion(QuizData data)
    {
        Wire(); ClearChoices(); ClearResultCards(); busy = false; saveError = false; complete = true; answered = true; selection = -1;
        title.text = data.title; progress.text = "Quiz complete";
        question.text = "Well done! You answered every question correctly.";
        feedback.text = "Press Finish to continue your conversation.";
        feedback.color = QuizUILayout.Ink;
        RefreshButtons(); ResetScroll();
        if (layout != null) layout.ApplyLayout();
    }

    public void ShowAssessmentQuestion(QuizData data, int index, int selected, int answeredCount)
    {
        ShowQuestion(data, index, null);
        EnsureAssessmentControls();
        assessment = true;
        hasPrevious = index > 0;
        hasNext = index + 1 < data.QuestionCount;
        allAssessmentAnswers = answeredCount == data.QuestionCount;
        SetSelection(selected);
        progress.text += "  •  " + answeredCount + "/" + data.QuestionCount + " answered";
        RefreshButtons();
        layout?.ApplyLayout();
    }

    public void ShowAssessmentResult(QuizData data, int score, IReadOnlyDictionary<string, int> answers = null)
    {
        ShowCompletion(data);
        assessment = true;
        question.text = "Your score: " + score + " / " + data.QuestionCount;
        question.richText = false;
        feedback.text = "Review your answers, then press Continue.";
        Sprite cardSprite = choiceTemplate != null && choiceTemplate.face != null ? choiceTemplate.face.sprite : null;
        if (cardSprite == null && layout != null && layout.paperFrame != null) cardSprite = layout.paperFrame.sprite;
        for (int i = 0; i < data.QuestionCount; i++)
        {
            QuizQuestion item = data.questions[i];
            int selected = -1;
            if (answers != null && item != null && !string.IsNullOrWhiteSpace(item.questionId) &&
                answers.TryGetValue(item.questionId.Trim(), out int savedAnswer)) selected = savedAnswer;
            resultCards.Add(QuizResultCard.Create(choiceContent, title.font, cardSprite, item, i + 1, selected));
        }
        SetButtonText(finishButton, "Continue");
        RefreshButtons();
        layout?.ApplyLayout();
        ResetScroll();
    }

    private void EnsureAssessmentControls()
    {
        if (previousButton == null && leaveButton != null)
        {
            previousButton = Instantiate(leaveButton, leaveButton.transform.parent);
            previousButton.name = "Previous Question";
            previousButton.onClick = new Button.ButtonClickedEvent();
            SetButtonText(previousButton, "Previous");
        }
        BindPreviousControl();
    }

    private void BindPreviousControl()
    {
        if (previousButton == null) return;
        previousButton.onClick.RemoveListener(HandlePrevious);
        previousButton.onClick.AddListener(HandlePrevious);
    }

    private void HandlePrevious() { if (CanRequest()) PreviousRequested?.Invoke(); }

    private static void SetButtonText(Button button, string value)
    {
        TMP_Text label = button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;
        if (label != null) label.text = value;
    }

    public void ShowSaveError(string message)
    {
        busy = false; saveError = true;
        feedback.text = string.IsNullOrWhiteSpace(message) ? "Progress could not be saved. Retry, or leave safely and return later." : message;
        feedback.color = new Color32(162, 65, 37, 255);
        RefreshButtons();
        for (int i = 0; i < choices.Count; i++) choices[i].SetSelected(i == selection, true);
        if (layout != null) layout.ApplyLayout();
    }

    public void SetBusy(bool value)
    {
        busy = value;
        RefreshButtons();
        for (int i = 0; i < choices.Count; i++) choices[i].SetSelected(i == selection, busy || answered || saveError);
    }

    public void Reset()
    {
        ClearChoices(); ClearResultCards(); selection = -1; assessment = false; answered = complete = busy = saveError = false;
        SetNpcPresentation(false, string.Empty);
        SetButtonText(finishButton, "Finish");
        if (feedback != null) feedback.text = string.Empty;
        RefreshButtons(); ResetScroll();
    }

    private void RefreshButtons()
    {
        HideLegacyPauseControl();
        if (assessment)
        {
            if (leaveButton != null) leaveButton.gameObject.SetActive(false);
            if (previousButton != null) { previousButton.gameObject.SetActive(!complete && !saveError); previousButton.interactable = hasPrevious && !busy; }
            if (nextButton != null) { nextButton.gameObject.SetActive(!complete && !saveError); nextButton.interactable = hasNext && !busy; }
            if (submitButton != null) { submitButton.gameObject.SetActive(!complete && !saveError); submitButton.interactable = allAssessmentAnswers && !busy; }
            if (finishButton != null) { finishButton.gameObject.SetActive(complete && !saveError); finishButton.interactable = !busy; }
            if (retrySaveButton != null) { retrySaveButton.gameObject.SetActive(saveError); retrySaveButton.interactable = !busy; }
            return;
        }
        if (leaveButton != null) leaveButton.gameObject.SetActive(true);
        if (previousButton != null) previousButton.gameObject.SetActive(false);
        if (submitButton != null) { submitButton.gameObject.SetActive(!answered && !complete && !saveError); submitButton.interactable = selection >= 0 && !busy; }
        if (nextButton != null) { nextButton.gameObject.SetActive(answered && !complete && !saveError); nextButton.interactable = !busy; }
        if (finishButton != null) { finishButton.gameObject.SetActive(complete && !saveError); finishButton.interactable = !busy; }
        if (retrySaveButton != null) { retrySaveButton.gameObject.SetActive(saveError); retrySaveButton.interactable = !busy; }
        if (leaveButton != null) leaveButton.interactable = !busy;
    }

    public bool IsAssessment => assessment;
    public bool IsAssessmentResult => assessment && complete;

    private void ClearResultCards()
    {
        foreach (QuizResultCard card in resultCards)
        {
            if (card == null) continue;
            card.gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(card.gameObject); else DestroyImmediate(card.gameObject);
        }
        resultCards.Clear();
    }

    private void ClearChoices()
    {
        foreach (QuizChoiceView card in choices)
        {
            if (card == null) continue;
            card.gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(card.gameObject); else DestroyImmediate(card.gameObject);
        }
        choices.Clear();
    }

    private void ResetScroll()
    {
        if (questionScroll != null) { questionScroll.StopMovement(); questionScroll.verticalNormalizedPosition = 1f; }
        if (choiceScroll != null) { choiceScroll.StopMovement(); choiceScroll.verticalNormalizedPosition = 1f; }
        if (layout != null) layout.ResetFeedbackScroll();
    }
}
