using System;
using System.Collections.Generic;
using System.Globalization;

public enum QuizOutcome
{
    Completed = 0,
    Cancelled = 1,
    Unavailable = 2
}

[Serializable]
public sealed class QuizContext
{
    public string progressionId;
    public string phaseId;
    public string stepId;

    public QuizContext() { }
    public QuizContext(string progressionId, string phaseId, string stepId)
    {
        this.progressionId = progressionId?.Trim();
        this.phaseId = phaseId?.Trim();
        this.stepId = stepId?.Trim();
    }

    public bool IsValid => !string.IsNullOrWhiteSpace(progressionId) &&
                           !string.IsNullOrWhiteSpace(phaseId) && !string.IsNullOrWhiteSpace(stepId);
    // Length prefixes avoid collisions when an authored ID contains separators.
    public string ProgressKey => IsValid ? Encode(progressionId.Trim()) + Encode(phaseId.Trim()) + Encode(stepId.Trim()) : string.Empty;
    public string progressKey => ProgressKey;
    private static string Encode(string value) => value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value + ";";
}

public static class QuizProgressUtility
{
    public static int FirstUnresolvedQuestionIndex(QuizData quiz, IEnumerable<string> correctQuestionIds)
    {
        if (quiz == null || quiz.questions == null) return 0;
        HashSet<string> correct = CreateAnswerSet(correctQuestionIds);
        for (int i = 0; i < quiz.questions.Count; i++)
            if (quiz.questions[i] == null || string.IsNullOrWhiteSpace(quiz.questions[i].questionId) ||
                !correct.Contains(quiz.questions[i].questionId.Trim())) return i;
        return quiz.questions.Count;
    }

    public static bool AreAllQuestionsCorrect(QuizData quiz, IEnumerable<string> correctQuestionIds)
    {
        return quiz != null && quiz.QuestionCount > 0 && FirstUnresolvedQuestionIndex(quiz, correctQuestionIds) == quiz.QuestionCount;
    }

    public static HashSet<string> CreateAnswerSet(IEnumerable<string> ids)
    {
        HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
        if (ids != null)
            foreach (string id in ids)
                if (!string.IsNullOrWhiteSpace(id)) result.Add(id.Trim());
        return result;
    }
}
