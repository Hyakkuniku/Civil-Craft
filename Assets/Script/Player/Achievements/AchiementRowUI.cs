using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class AchievementRowUI : MonoBehaviour
{
    [Header("UI Elements")]
    public Image iconImage;
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI descriptionText;
    public TextMeshProUGUI rewardText;
    
    [Header("Progress Bar")]
    public Image progressFill;
    public TextMeshProUGUI progressText;
    
    [Header("Completion Visuals")]
    public GameObject completedCheckmark; 
    public CanvasGroup canvasGroup; // Optional: To slightly fade out completed achievements

    public void Setup(AchievementSO achievement, int currentProgress, bool isCompleted)
    {
        titleText.text = achievement.achievementName;
        descriptionText.text = achievement.description;
        
        if (rewardText != null)
        {
            rewardText.text = $" {achievement.bonusGold} Gold | {achievement.bonusExp} EXP";
        }

        if (isCompleted)
        {
            // Completed State
            if (iconImage != null && achievement.unlockedIcon != null) iconImage.sprite = achievement.unlockedIcon;
            if (progressText != null) progressText.text = "Completed!";
            if (progressFill != null) progressFill.fillAmount = 1f;
            if (completedCheckmark != null) completedCheckmark.SetActive(true);
            if (canvasGroup != null) canvasGroup.alpha = 0.7f; // Dim it slightly
        }
        else
        {
            // In-Progress State
            if (iconImage != null) iconImage.sprite = achievement.lockedIcon != null ? achievement.lockedIcon : achievement.unlockedIcon;
            if (completedCheckmark != null) completedCheckmark.SetActive(false);
            if (canvasGroup != null) canvasGroup.alpha = 1f;
            
            // Clamp progress so it doesn't show "55/50" if they overshot it before opening the menu
            int clampedProgress = Mathf.Min(currentProgress, achievement.targetAmount);
            
            if (progressText != null) progressText.text = $"{clampedProgress} / {achievement.targetAmount}";
            if (progressFill != null) progressFill.fillAmount = (float)clampedProgress / achievement.targetAmount;
        }
    }
}