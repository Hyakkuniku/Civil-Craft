using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Five scene-authored cells; never creates UI at runtime.</summary>
public sealed class MultiplayerLeaderboardRowUI : MonoBehaviour
{
    [SerializeField] private TMP_Text rankText;
    [SerializeField] private TMP_Text builderText;
    [SerializeField] private TMP_Text winRateText;
    [SerializeField] private TMP_Text winsText;
    [SerializeField] private TMP_Text lossesText;
    [SerializeField] private Image background;

    public static string FormatWinRate(int wins, int losses)
    {
        long decisive = (long)Math.Max(0, wins) + Math.Max(0, losses);
        return decisive == 0 ? "—" : (100d * Math.Max(0, wins) / decisive).ToString("0.0") + "%";
    }

    public void Bind(int rank, string builder, int wins, int losses, bool isYou = false)
    {
        if (rankText != null) rankText.text = rank.ToString();
        if (builderText != null)
        {
            builderText.richText = false;
            builderText.text = (string.IsNullOrWhiteSpace(builder) ? "Engineer" : builder) + (isYou ? " (YOU)" : "");
            builderText.fontStyle = FontStyles.Bold;
        }
        if (winRateText != null) winRateText.text = FormatWinRate(wins, losses);
        if (winsText != null) winsText.text = Math.Max(0, wins).ToString("N0");
        if (lossesText != null) lossesText.text = Math.Max(0, losses).ToString("N0");
        if (background != null) background.color = isYou ? new Color32(255, 224, 157, 255) :
            rank <= 3 ? new Color32(255, 231, 176, 255) : new Color32(255, 250, 242, 255);
    }
}
