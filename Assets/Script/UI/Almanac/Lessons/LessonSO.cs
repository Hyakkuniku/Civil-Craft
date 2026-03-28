using UnityEngine;

[CreateAssetMenu(fileName = "New Lesson", menuName = "Almanac/Lesson Data")]
public class LessonSO : ScriptableObject
{
    [Header("Lesson Details")]
    public string lessonTitle;
    
    [TextArea(10, 20)] 
    public string lessonContent;

    [Header("Quiz Settings")]
    public string quizQuestion;
    
    [Tooltip("Type exactly 4 options here.")]
    public string[] quizOptions = new string[4];
    
    [Tooltip("0 = First option, 1 = Second option, 2 = Third, 3 = Fourth")]
    public int correctOptionIndex;

    [Header("Rewards")]
    public int expReward = 50;
}