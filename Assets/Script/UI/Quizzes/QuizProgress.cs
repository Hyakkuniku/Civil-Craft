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
    public static bool IsValidChoiceOrders(QuizData quiz, IReadOnlyList<QuizChoiceOrderSaveData> orders)
    {
        if (quiz == null || quiz.questions == null || quiz.QuestionCount == 0 ||
            orders == null || orders.Count != quiz.QuestionCount) return false;
        Dictionary<string, QuizQuestion> remaining = new Dictionary<string, QuizQuestion>(StringComparer.Ordinal);
        foreach (QuizQuestion question in quiz.questions)
        {
            if (question == null || string.IsNullOrWhiteSpace(question.questionId) ||
                remaining.ContainsKey(question.questionId.Trim())) return false;
            remaining.Add(question.questionId.Trim(), question);
        }
        foreach (QuizChoiceOrderSaveData order in orders)
        {
            if (order == null || string.IsNullOrWhiteSpace(order.questionId) ||
                !remaining.TryGetValue(order.questionId.Trim(), out QuizQuestion question) ||
                !IsValidChoiceOrder(question, order.choiceIndices)) return false;
            remaining.Remove(order.questionId.Trim());
        }
        return remaining.Count == 0;
    }

    /// <summary>
    /// Source indices preserve the meaning of every saved answer. Old attempts
    /// without choice metadata keep their original choice lettering/positions.
    /// </summary>
    public static List<QuizChoiceOrderSaveData> CreateChoiceOrders(QuizData quiz, QuizProgressSaveData progress,
        Random random = null)
    {
        List<QuizChoiceOrderSaveData> result = new List<QuizChoiceOrderSaveData>();
        if (quiz == null || quiz.questions == null) return result;
        if (progress != null && IsValidChoiceOrders(quiz, progress.choiceOrders))
        {
            foreach (QuizChoiceOrderSaveData order in progress.choiceOrders) result.Add(order.Clone());
            return result;
        }
        foreach (QuizQuestion question in quiz.questions)
        {
            if (question == null || string.IsNullOrWhiteSpace(question.questionId) || question.choices == null)
                return new List<QuizChoiceOrderSaveData>();
            QuizChoiceOrderSaveData order = new QuizChoiceOrderSaveData { questionId = question.questionId.Trim() };
            for (int index = 0; index < question.choices.Count; index++) order.choiceIndices.Add(index);
            result.Add(order);
        }
        if (!IsValidChoiceOrders(quiz, result)) return new List<QuizChoiceOrderSaveData>();
        bool legacyProgress = progress != null &&
            ((progress.questionOrderIds != null && progress.questionOrderIds.Count > 0) ||
             (progress.correctQuestionIds != null && progress.correctQuestionIds.Count > 0) ||
             (progress.answers != null && progress.answers.Count > 0) || progress.submitted || progress.completed);
        if (legacyProgress) return result;
        random = random ?? new Random(Guid.NewGuid().GetHashCode());
        foreach (QuizChoiceOrderSaveData order in result)
            for (int index = order.choiceIndices.Count - 1; index > 0; index--)
            {
                int other = random.Next(index + 1);
                int choice = order.choiceIndices[index];
                order.choiceIndices[index] = order.choiceIndices[other];
                order.choiceIndices[other] = choice;
            }
        return result;
    }

    public static IReadOnlyList<int> ChoiceOrderFor(string questionId, IReadOnlyList<QuizChoiceOrderSaveData> orders)
    {
        if (string.IsNullOrWhiteSpace(questionId) || orders == null) return null;
        foreach (QuizChoiceOrderSaveData order in orders)
            if (order != null && string.Equals(order.questionId?.Trim(), questionId.Trim(), StringComparison.Ordinal))
                return order.choiceIndices;
        return null;
    }

    public static int ChoiceIndexAt(QuizQuestion question, IReadOnlyList<int> order, int displayIndex)
    {
        if (question == null || question.choices == null || displayIndex < 0 || displayIndex >= question.choices.Count) return -1;
        if (order == null) return displayIndex;
        return IsValidChoiceOrder(question, order) ? order[displayIndex] : -1;
    }

    public static int ChoiceDisplayIndex(QuizQuestion question, IReadOnlyList<int> order, int sourceIndex)
    {
        if (question == null || question.choices == null || sourceIndex < 0 || sourceIndex >= question.choices.Count) return -1;
        if (order == null) return sourceIndex;
        if (!IsValidChoiceOrder(question, order)) return -1;
        for (int index = 0; index < order.Count; index++) if (order[index] == sourceIndex) return index;
        return -1;
    }

    private static bool IsValidChoiceOrder(QuizQuestion question, IReadOnlyList<int> order)
    {
        if (question == null || question.choices == null || question.choices.Count < 2 ||
            order == null || order.Count != question.choices.Count) return false;
        HashSet<int> remaining = new HashSet<int>();
        foreach (int index in order)
            if (index < 0 || index >= question.choices.Count || !remaining.Add(index)) return false;
        return remaining.Count == question.choices.Count;
    }

    /// <summary>A saved order contains each current stable question ID exactly once.</summary>
    public static bool IsValidQuestionOrder(QuizData quiz, IReadOnlyList<string> order)
    {
        if (quiz == null || quiz.questions == null || quiz.QuestionCount == 0 ||
            order == null || order.Count != quiz.QuestionCount) return false;
        HashSet<string> remaining = new HashSet<string>(StringComparer.Ordinal);
        foreach (QuizQuestion question in quiz.questions)
            if (question == null || string.IsNullOrWhiteSpace(question.questionId) ||
                !remaining.Add(question.questionId.Trim())) return false;
        foreach (string id in order)
            if (string.IsNullOrWhiteSpace(id) || !remaining.Remove(id.Trim())) return false;
        return remaining.Count == 0;
    }

    /// <summary>
    /// Reuses a valid saved attempt order. Legacy answered/submitted records keep
    /// their original authored numbering; only genuinely fresh attempts shuffle.
    /// Neither the asset nor the supplied record is modified; random state is local.
    /// </summary>
    public static List<string> CreateQuestionOrder(QuizData quiz, QuizProgressSaveData progress, Random random = null)
    {
        List<string> result = new List<string>();
        if (quiz == null || quiz.questions == null) return result;
        foreach (QuizQuestion question in quiz.questions)
        {
            if (question == null || string.IsNullOrWhiteSpace(question.questionId)) return new List<string>();
            result.Add(question.questionId.Trim());
        }
        if (!IsValidQuestionOrder(quiz, result)) return new List<string>();
        if (progress != null && IsValidQuestionOrder(quiz, progress.questionOrderIds))
        {
            result.Clear();
            foreach (string id in progress.questionOrderIds) result.Add(id.Trim());
            return result;
        }
        bool legacyProgress = progress != null &&
            ((progress.correctQuestionIds != null && progress.correctQuestionIds.Count > 0) ||
             (progress.answers != null && progress.answers.Count > 0) || progress.submitted || progress.completed);
        if (legacyProgress) return result;
        random = random ?? new Random(Guid.NewGuid().GetHashCode());
        for (int index = result.Count - 1; index > 0; index--)
        {
            int other = random.Next(index + 1);
            string id = result[index];
            result[index] = result[other];
            result[other] = id;
        }
        return result;
    }

    /// <summary>Resolves a display position without changing question or choice order in the asset.</summary>
    public static int QuestionIndexAt(QuizData quiz, IReadOnlyList<string> order, int displayIndex)
    {
        if (quiz == null || quiz.questions == null || displayIndex < 0 || displayIndex >= quiz.QuestionCount) return -1;
        if (order == null) return displayIndex; // Legacy direct presentation calls use authored order.
        if (!IsValidQuestionOrder(quiz, order)) return -1;
        string id = order[displayIndex].Trim();
        for (int index = 0; index < quiz.questions.Count; index++)
            if (string.Equals(quiz.questions[index].questionId.Trim(), id, StringComparison.Ordinal)) return index;
        return -1;
    }

    public static int FirstUnresolvedDisplayIndex(QuizData quiz, IEnumerable<string> correctQuestionIds,
        IReadOnlyList<string> order)
    {
        if (quiz == null || quiz.questions == null) return 0;
        HashSet<string> correct = CreateAnswerSet(correctQuestionIds);
        for (int displayIndex = 0; displayIndex < quiz.QuestionCount; displayIndex++)
        {
            int sourceIndex = QuestionIndexAt(quiz, order, displayIndex);
            if (sourceIndex < 0 || quiz.questions[sourceIndex] == null ||
                !correct.Contains(quiz.questions[sourceIndex].questionId.Trim())) return displayIndex;
        }
        return quiz.QuestionCount;
    }

    public static bool HasAllAssessmentAnswers(QuizData quiz, IReadOnlyDictionary<string, int> answers)
    {
        if (quiz == null || quiz.QuestionCount == 0 || answers == null) return false;
        foreach (QuizQuestion question in quiz.questions)
            if (question == null || !answers.TryGetValue(question.questionId.Trim(), out int choice) ||
                choice < 0 || choice >= question.choices.Count) return false;
        return true;
    }

    public static int AssessmentScore(QuizData quiz, IReadOnlyDictionary<string, int> answers)
    {
        int score = 0;
        if (quiz?.questions == null || answers == null) return score;
        foreach (QuizQuestion question in quiz.questions)
            if (question != null && answers.TryGetValue(question.questionId.Trim(), out int choice) &&
                choice == question.correctChoiceIndex) score++;
        return score;
    }

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
