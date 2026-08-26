using UnityEngine;

[CreateAssetMenu(fileName = "New Lesson", menuName = "Civil Craft/Lesson Data")]
public sealed class LessonData : ScriptableObject
{
    [Tooltip("Permanent save ID. Keep this unchanged after releasing a lesson.")]
    [SerializeField] private string lessonId;
    [SerializeField] private string lessonTitle = "Lesson Title";
    [SerializeField] private Sprite lessonImage;
    [TextArea(8, 30)]
    [SerializeField] private string lessonDescription;

    public string Id => string.IsNullOrWhiteSpace(lessonId) ? name : lessonId.Trim();
    public string Title => lessonTitle;
    public Sprite Image => lessonImage;
    public string Description => lessonDescription;
}
