using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class LeaderboardRowUI : MonoBehaviour
{
    [SerializeField] private TMP_Text rankText;
    [SerializeField] private TMP_Text builderText;
    [SerializeField] private TMP_Text costText;
    [SerializeField] private TMP_Text stressText;
    [SerializeField] private Image background;

    public void Bind(int rank, string builder, int cost, float peakStress)
    {
        if (rankText != null) rankText.text = rank.ToString();
        if (builderText != null) builderText.text = string.IsNullOrWhiteSpace(builder) ? "Builder" : builder;
        if (costText != null) costText.text = cost.ToString("N0");
        if (stressText != null) stressText.text = peakStress.ToString("0.0") + "%";
        if (background != null)
            background.color = rank <= 3
                ? new Color32(255, 231, 176, 255)
                : new Color32(255, 250, 242, 255);
    }
}
