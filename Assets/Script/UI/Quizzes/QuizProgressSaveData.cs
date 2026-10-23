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
    public List<QuizAnswerSaveData> answers = new List<QuizAnswerSaveData>();
    public bool submitted;
    public List<string> questionOrderIds = new List<string>();
    public List<QuizChoiceOrderSaveData> choiceOrders = new List<QuizChoiceOrderSaveData>();

    public QuizProgressSaveData Clone()
    {
        return new QuizProgressSaveData
        {
            progressKey = progressKey,
            quizId = quizId,
            contentFingerprint = contentFingerprint,
            correctQuestionIds = correctQuestionIds != null ? new List<string>(correctQuestionIds) : new List<string>(),
            completed = completed,
            answers = answers != null ? answers.ConvertAll(answer => answer == null ? null : answer.Clone()) : new List<QuizAnswerSaveData>(),
            submitted = submitted,
            questionOrderIds = questionOrderIds != null ? new List<string>(questionOrderIds) : new List<string>(),
            choiceOrders = choiceOrders != null ? choiceOrders.ConvertAll(order => order == null ? null : order.Clone()) : new List<QuizChoiceOrderSaveData>()
        };
    }
}

[Serializable]
public sealed class QuizChoiceOrderSaveData
{
    public string questionId;
    public List<int> choiceIndices = new List<int>();
    public QuizChoiceOrderSaveData Clone() => new QuizChoiceOrderSaveData
    {
        questionId = questionId,
        choiceIndices = choiceIndices != null ? new List<int>(choiceIndices) : new List<int>()
    };
}

[Serializable]
public sealed class QuizAnswerSaveData
{
    public string questionId;
    public int choiceIndex;
    public QuizAnswerSaveData Clone() => new QuizAnswerSaveData { questionId = questionId, choiceIndex = choiceIndex };
}
