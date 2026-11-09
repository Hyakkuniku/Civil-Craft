using System.IO;
using System.Text;
using UnityEngine;

/// <summary>Batch entry for detached rules, existing typed chat and authored UI checks.</summary>
public static class RandomChallengeFeatureValidation
{
    public static void RunAll()
    {
        RandomChallengeValidation.Run();
        var fixture = new GameObject("Detached Existing Chat Regression");
        fixture.hideFlags = HideFlags.HideAndDontSave;
        fixture.SetActive(false);
        var report = new StringBuilder();
        try
        {
            SessionChatValidation.Run(fixture.transform, report);
            var offline = fixture.AddComponent<SessionChatUI>();
            offline.SendRandomChallenge();
            if (SessionChatUI.IsOpen) throw new System.InvalidOperationException("Offline challenges captured gameplay.");
            report.AppendLine("PASS: Offline Random Challenge cannot invite or capture gameplay.");
        }
        finally
        {
            Object.DestroyImmediate(fixture);
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/random-challenge-chat-regressions.txt", report.ToString());
        }
        RandomChallengeChatUIAuthoring.ApplyAndValidate();
    }
}
