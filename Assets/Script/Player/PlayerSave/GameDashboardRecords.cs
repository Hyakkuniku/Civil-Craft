using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

/// <summary>Optional, backward-compatible records. These never grant progress or rewards.</summary>
[Serializable]
public sealed class ContractCompletionRecord
{
    public string contractId;
    public string completedAtUtc;
}

[Serializable]
public sealed class BridgePhotoSnapshot
{
    public string storage = "archive-v1";
    public string fileName = "bridge-photos-v1.zip";
    public string contractId;
    public string regionId;
    public string resultId;
    public string entryName;
    public string sha256;
    public string capturedAt;
}

[Serializable]
public sealed class QuizAggregateResult
{
    public string quizId;
    public string attemptId;
    public string progressKey;
    public string contentFingerprint;
    public string title;
    public string status = "completed";
    public int score;
    public int totalQuestions;
    public string completedAt;
    // Canonical QuizData question/choice indices, never shuffled UI positions.
    // Null in older saves; a missing review does not invalidate its aggregate.
    public List<int> selectedIndices;
}

/// <summary>Pure validation shared by save-time capture, projection and regression checks.</summary>
public static class GameDashboardRecords
{
    public static bool IsLowerHex(string value, int length)
    {
        if (value == null || value.Length != length) return false;
        foreach (char c in value)
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
        return true;
    }

    public static bool ValidSnapshot(BridgePhotoSnapshot snapshot, string contractId, string resultId)
    {
        return snapshot != null && snapshot.storage == "archive-v1" &&
            snapshot.fileName == "bridge-photos-v1.zip" && IsLowerHex(resultId, 32) &&
            snapshot.resultId == resultId && snapshot.contractId == contractId &&
            !string.IsNullOrWhiteSpace(snapshot.regionId) && snapshot.regionId.Length <= 200 &&
            snapshot.entryName == "bridge-" + resultId + ".png" && IsLowerHex(snapshot.sha256, 64);
    }

    public static bool Acknowledged(IDictionary<string, object> response)
    {
        if (response == null || !response.TryGetValue("accepted", out object accepted) ||
            !(accepted is bool yes) || !yes || !response.TryGetValue("schemaVersion", out object version))
            return false;
        return VersionOne(version);
    }

    public static bool AcknowledgedQuizReview(IDictionary<string, object> response)
    {
        return response != null && response.TryGetValue("quizReviewSchemaVersion", out object version) &&
            VersionOne(version);
    }

    private static bool VersionOne(object version)
    {
        // PlayFab's JSON serializer can return either integral or floating numeric types.
        return version is byte b && b == 1 || version is short s && s == 1 ||
            version is int i && i == 1 || version is long l && l == 1L ||
            version is float f && f == 1f || version is double d && d == 1d ||
            version is decimal m && m == 1m;
    }

    public static bool ValidQuizResult(QuizAggregateResult result)
    {
        return result != null && !string.IsNullOrWhiteSpace(result.quizId) && result.quizId.Length <= 200 &&
            !string.IsNullOrWhiteSpace(result.attemptId) && result.attemptId.Length <= 200 &&
            !string.IsNullOrWhiteSpace(result.title) && result.title.Length <= 4000 &&
            result.status == "completed" && result.totalQuestions >= 1 && result.totalQuestions <= 500 &&
            result.score >= 0 && result.score <= result.totalQuestions;
    }

    public static bool ValidQuizReview(QuizAggregateResult result)
    {
        if (!ValidQuizResult(result) || !IsLowerHex(result.contentFingerprint, 64) ||
            result.selectedIndices == null || result.selectedIndices.Count != result.totalQuestions)
            return false;
        foreach (int selected in result.selectedIndices)
            if (selected < 0 || selected >= 20) return false;
        return true;
    }

    public static bool TryQuizResult(QuizData quiz, QuizProgressSaveData progress,
        string attemptId, string submittedAt, out QuizAggregateResult result)
    {
        result = null;
        if (quiz == null || !quiz.assessmentMode || !quiz.TryValidate(out _) ||
            quiz.QuestionCount < 1 || quiz.QuestionCount > 500 || progress == null || !progress.submitted ||
            progress.quizId != quiz.quizId.Trim() || progress.contentFingerprint != quiz.ContentFingerprint ||
            progress.answers == null || progress.answers.Count != quiz.QuestionCount ||
            string.IsNullOrWhiteSpace(progress.progressKey)) return false;
        var answers = new Dictionary<string, int>(StringComparer.Ordinal);
        var validIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (QuizQuestion question in quiz.questions) validIds.Add(question.questionId.Trim());
        foreach (QuizAnswerSaveData answer in progress.answers)
            if (answer == null || string.IsNullOrWhiteSpace(answer.questionId) ||
                !validIds.Contains(answer.questionId) || answers.ContainsKey(answer.questionId) || answer.choiceIndex < 0)
                return false;
            else answers.Add(answer.questionId, answer.choiceIndex);
        if (!QuizProgressUtility.HasAllAssessmentAnswers(quiz, answers)) return false;
        var selectedIndices = new List<int>();
        foreach (QuizQuestion question in quiz.questions)
            selectedIndices.Add(answers[question.questionId.Trim()]);
        result = new QuizAggregateResult {
            quizId = quiz.quizId.Trim(), attemptId = attemptId, progressKey = progress.progressKey,
            contentFingerprint = progress.contentFingerprint, title = quiz.title.Trim(),
            score = QuizProgressUtility.AssessmentScore(quiz, answers), totalQuestions = quiz.QuestionCount,
            completedAt = submittedAt ?? string.Empty, selectedIndices = selectedIndices
        };
        return ValidQuizResult(result);
    }

    public static string LegacyQuizResultId(QuizProgressSaveData progress)
    {
        if (progress == null) return string.Empty;
        var parts = new List<string>();
        if (progress.answers != null)
            foreach (QuizAnswerSaveData answer in progress.answers)
                if (answer != null) parts.Add((answer.questionId ?? string.Empty) + ":" +
                    answer.choiceIndex.ToString(CultureInfo.InvariantCulture));
        parts.Sort(StringComparer.Ordinal);
        string source = (progress.progressKey ?? string.Empty) + "\n" + (progress.quizId ?? string.Empty) +
            "\n" + (progress.contentFingerprint ?? string.Empty) + "\n" + string.Join("\n", parts);
        using (SHA256 hash = SHA256.Create())
            return "legacy-" + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(source)))
                .Replace("-", string.Empty).ToLowerInvariant();
    }
}
