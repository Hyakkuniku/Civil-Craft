using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

[Serializable]
public sealed class QuizChoice
{
    [TextArea(2, 5)] public string text;
    public Sprite image;
    // Authored by the Editor; never use a runtime InstanceID as persistent identity.
    [HideInInspector] public string imageFingerprint;
}

[Serializable]
public sealed class QuizQuestion
{
    [Tooltip("Stable save identity. Do not change after publishing unless this is a new question.")]
    public string questionId;
    [TextArea(3, 8)] public string prompt;
    public List<QuizChoice> choices = new List<QuizChoice>();
    [Tooltip("Zero-based index of the one correct choice.")]
    public int correctChoiceIndex;
    [Tooltip("Optional explanation shown only after a correct answer.")]
    [TextArea(2, 5)] public string feedback;
}

[CreateAssetMenu(fileName = "Quiz", menuName = "Civil Craft/NPC Quiz")]
public sealed class QuizData : ScriptableObject
{
    [Tooltip("Stable save identity. Assigned once, not regenerated when content changes.")]
    public string quizId;
    public string title;
    public List<QuizQuestion> questions = new List<QuizQuestion>();

    public int QuestionCount => questions != null ? questions.Count : 0;

    public bool TryValidate(out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(quizId)) { error = "Quiz needs a stable ID."; return false; }
        if (string.IsNullOrWhiteSpace(title)) { error = "Quiz needs a title."; return false; }
        if (questions == null || questions.Count == 0) { error = "Quiz needs at least one question."; return false; }
        HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < questions.Count; i++)
        {
            QuizQuestion question = questions[i];
            string prefix = "Question " + (i + 1) + ": ";
            if (question == null) { error = prefix + "is missing."; return false; }
            if (string.IsNullOrWhiteSpace(question.questionId) || !ids.Add(question.questionId.Trim()))
            { error = prefix + "needs a unique stable ID."; return false; }
            if (string.IsNullOrWhiteSpace(question.prompt)) { error = prefix + "needs question text."; return false; }
            if (question.choices == null || question.choices.Count < 2)
            { error = prefix + "needs at least two choices."; return false; }
            if (question.correctChoiceIndex < 0 || question.correctChoiceIndex >= question.choices.Count)
            { error = prefix + "correct choice index is outside the choice list."; return false; }
            for (int choiceIndex = 0; choiceIndex < question.choices.Count; choiceIndex++)
            {
                QuizChoice choice = question.choices[choiceIndex];
                if (choice == null || (string.IsNullOrWhiteSpace(choice.text) && choice.image == null))
                { error = prefix + "choice " + (choiceIndex + 1) + " needs text or an image."; return false; }
                if (choice.image != null && string.IsNullOrWhiteSpace(choice.imageFingerprint))
                { error = prefix + "image identity is not imported yet; reselect or reimport this quiz in the Editor."; return false; }
            }
        }
        return true;
    }

    public string ContentFingerprint
    {
        get
        {
            StringBuilder content = new StringBuilder("npc-quiz-v1;");
            Append(content, quizId?.Trim());
            Append(content, title);
            Append(content, QuestionCount.ToString(CultureInfo.InvariantCulture));
            if (questions != null)
                foreach (QuizQuestion question in questions)
                {
                    Append(content, question?.questionId?.Trim());
                    Append(content, question?.prompt);
                    Append(content, question?.feedback);
                    Append(content, question == null ? "missing" : question.correctChoiceIndex.ToString(CultureInfo.InvariantCulture));
                    Append(content, question?.choices == null ? "missing" : question.choices.Count.ToString(CultureInfo.InvariantCulture));
                    if (question?.choices == null) continue;
                    foreach (QuizChoice choice in question.choices)
                    {
                        Append(content, choice == null ? "missing" : "choice");
                        Append(content, choice?.text);
                        Append(content, choice?.image == null ? string.Empty : choice.imageFingerprint);
                    }
                }
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(content.ToString())))
                    .Replace("-", string.Empty).ToLowerInvariant();
        }
    }

    private static void Append(StringBuilder target, string value)
    {
        value = value ?? string.Empty;
        target.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append(';');
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(quizId)) quizId = Guid.NewGuid().ToString("N");
        if (questions == null) return;
        foreach (QuizQuestion question in questions)
        {
            if (question == null) continue;
            if (string.IsNullOrWhiteSpace(question.questionId)) question.questionId = Guid.NewGuid().ToString("N");
        }
        RefreshImageFingerprints();
    }

    /// <summary>Refreshes cached image content identity before Play/Build without regenerating stable IDs.</summary>
    public bool RefreshImageFingerprints()
    {
        bool changed = false;
        if (questions == null) return false;
        foreach (QuizQuestion question in questions)
        {
            if (question == null) continue;
            if (question.choices == null) continue;
            foreach (QuizChoice choice in question.choices)
            {
                if (choice == null) continue;
                string identity = string.Empty;
                if (choice.image != null && UnityEditor.AssetDatabase.TryGetGUIDAndLocalFileIdentifier(choice.image, out string guid, out long localId))
                {
                    string path = UnityEditor.AssetDatabase.GetAssetPath(choice.image);
                    identity = guid + ":" + localId.ToString(CultureInfo.InvariantCulture) + ":" +
                        UnityEditor.AssetDatabase.GetAssetDependencyHash(path).ToString();
                }
                if (string.Equals(choice.imageFingerprint, identity, StringComparison.Ordinal)) continue;
                choice.imageFingerprint = identity;
                changed = true;
            }
        }
        return changed;
    }
#endif
}
