using UnityEngine;

[CreateAssetMenu(fileName = "New Lesson", menuName = "Civil Craft/Lesson Data")]
public sealed class LessonData : ScriptableObject
{
    [Header("Shared Identity")]
    [Tooltip("Permanent save ID. Keep this unchanged after releasing a lesson.")]
    [SerializeField] private string lessonId;
    [SerializeField] private string lessonTitle = "Lesson Title";
    [Header("Popup")]
    [Tooltip("Photo shown in the introduction and, by default, in the Almanac.")]
    [SerializeField] private Sprite lessonImage;
    [TextArea(3, 8)]
    [Tooltip("Short introduction. Leave blank to use the existing Lesson Description.")]
    [SerializeField] private string popupDescription;

    [Header("Almanac")]
    [Tooltip("Optional detailed diagram/photo. Leave empty to reuse the popup image.")]
    [SerializeField] private Sprite almanacImage;
    [TextArea(8, 30)]
    [Tooltip("Full lesson explanation for the Almanac. Existing lesson text is preserved here.")]
    [SerializeField] private string lessonDescription;

    public string Id => string.IsNullOrWhiteSpace(lessonId) ? name : lessonId.Trim();
    public string Title => lessonTitle;
    public Sprite Image => lessonImage;
    public string Description => lessonDescription;
    public string PopupDescription => string.IsNullOrWhiteSpace(popupDescription)
        ? lessonDescription : popupDescription;
    public string AlmanacDescription => string.IsNullOrWhiteSpace(lessonDescription)
        ? popupDescription : lessonDescription;
    public Sprite AlmanacImage => almanacImage != null ? almanacImage : lessonImage;
}
