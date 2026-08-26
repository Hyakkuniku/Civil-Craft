using System.Collections.Generic;

/// <summary>
/// Small facade for LessonData archive persistence. PlayerDataManager remains the
/// single owner of the JSON save file, so lesson progress cannot desynchronize.
/// </summary>
public static class LessonSaveManager
{
    public static bool Unlock(LessonData lesson)
    {
        return lesson != null && PlayerDataManager.Instance != null &&
               PlayerDataManager.Instance.UnlockLesson(lesson.Id);
    }

    public static bool IsUnlocked(LessonData lesson)
    {
        return lesson != null && PlayerDataManager.Instance != null &&
               PlayerDataManager.Instance.IsLessonUnlocked(lesson.Id);
    }

    public static IReadOnlyList<string> GetUnlockedIds()
    {
        PlayerData data = PlayerDataManager.Instance != null
            ? PlayerDataManager.Instance.CurrentData
            : null;

        return data != null && data.unlockedLessonIds != null
            ? data.unlockedLessonIds
            : (IReadOnlyList<string>)System.Array.Empty<string>();
    }
}
