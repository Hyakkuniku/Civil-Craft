using System;
using System.Collections.Generic;

/// <summary>Plain save data; independent of Unity assets and quiz presentation.</summary>
[Serializable]
public sealed class QuizProgressSaveData
{
    public string progressKey;
    public string quizId;
    public string contentFingerprint;
    public List<string> correctQuestionIds = new List<string>();
    public bool completed;

    public QuizProgressSaveData Clone()
    {
        return new QuizProgressSaveData
        {
            progressKey = progressKey,
            quizId = quizId,
            contentFingerprint = contentFingerprint,
            correctQuestionIds = correctQuestionIds != null ? new List<string>(correctQuestionIds) : new List<string>(),
            completed = completed
        };
    }
}
