using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class EducationalAlmanacTab : MonoBehaviour
{
    [Header("Lesson Data")]
    public List<LessonSO> allLessons;
    
    [Header("Left Page (The Stack)")]
    public Transform buttonContainer; 
    [Tooltip("A UI Button Prefab with a TextMeshProUGUI child.")]
    public GameObject lessonButtonPrefab;

    [Header("Right Page (Readabouts)")]
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI contentText;

    [Header("Right Page (Quiz UI)")]
    public GameObject quizPanel; 
    public TextMeshProUGUI questionText;
    public Button[] optionButtons; // Array of exactly 4 buttons
    public TextMeshProUGUI[] optionTexts; // The text on those 4 buttons
    public TextMeshProUGUI feedbackText;

    private LessonSO currentLesson;

    private void OnEnable()
    {
        RefreshLessonList();
    }

    public void RefreshLessonList()
    {
        // Clear old buttons
        foreach (Transform child in buttonContainer) 
        {
            Destroy(child.gameObject);
        }

        // Spawn a button for every lesson
        foreach (LessonSO lesson in allLessons)
        {
            GameObject btnObj = Instantiate(lessonButtonPrefab, buttonContainer);
            
            TextMeshProUGUI btnText = btnObj.GetComponentInChildren<TextMeshProUGUI>();
            if (btnText != null) btnText.text = lesson.lessonTitle;

            Button btn = btnObj.GetComponent<Button>();
            btn.onClick.AddListener(() => DisplayLesson(lesson));
        }

        ClearDetails();
    }

    private void DisplayLesson(LessonSO lesson)
    {
        currentLesson = lesson;
        
        if (titleText != null) titleText.text = lesson.lessonTitle;
        if (contentText != null) contentText.text = lesson.lessonContent;

        SetupQuiz(lesson);
    }

    private void SetupQuiz(LessonSO lesson)
    {
        if (quizPanel != null) quizPanel.SetActive(true);
        if (questionText != null) questionText.text = lesson.quizQuestion;

        for (int i = 0; i < optionButtons.Length; i++)
        {
            if (i < lesson.quizOptions.Length)
            {
                optionButtons[i].gameObject.SetActive(true);
                optionTexts[i].text = lesson.quizOptions[i];
                
                int indexToCheck = i; // Prevents closure issues in loops
                optionButtons[i].onClick.RemoveAllListeners();
                optionButtons[i].onClick.AddListener(() => CheckAnswer(indexToCheck));
            }
            else
            {
                optionButtons[i].gameObject.SetActive(false); // Hide unused buttons
            }
        }

        // Check if player already finished this quiz
        if (PlayerDataManager.Instance != null && PlayerDataManager.Instance.CurrentData.completedLessons.Contains(lesson.name))
        {
            SetQuizInteractable(false);
            feedbackText.text = "<color=green>Quiz Completed!</color>";
        }
        else
        {
            SetQuizInteractable(true);
            feedbackText.text = "";
        }
    }

    private void CheckAnswer(int selectedIndex)
    {
        if (currentLesson == null) return;

        if (selectedIndex == currentLesson.correctOptionIndex)
        {
            feedbackText.text = "<color=green>Correct! +" + currentLesson.expReward + " EXP</color>";
            SetQuizInteractable(false);

            if (PlayerDataManager.Instance != null)
            {
                PlayerDataManager.Instance.CompleteLesson(currentLesson.name);
                PlayerDataManager.Instance.AddExp(currentLesson.expReward);
            }
        }
        else
        {
            feedbackText.text = "<color=red>Incorrect. Re-read the lesson and try again!</color>";
        }
    }

    private void SetQuizInteractable(bool state)
    {
        foreach (Button btn in optionButtons)
        {
            btn.interactable = state;
        }
    }

    private void ClearDetails()
    {
        if (titleText != null) titleText.text = "Select a Lesson";
        if (contentText != null) contentText.text = "Expand your engineering knowledge on the left!";
        if (quizPanel != null) quizPanel.SetActive(false);
    }
}