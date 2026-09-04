using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class ObjectiveTabButton : MonoBehaviour
{
    public TextMeshProUGUI questTitleText;
    public GameObject readyToTurnInIcon; 

    // --- THE FIX: We use the global TrackedTask now! ---
    private TrackedTask myTask;

    public void Setup(TrackedTask task)
    {
        myTask = task;

        Image background = GetComponent<Image>();
        Button button = GetComponent<Button>();
        LayoutElement layout = GetComponent<LayoutElement>();
        Color32 accentColor;
        Color32 backgroundColor;
        string category = task.isTutorial ? "GUIDE" : "CONTRACT";
        string status;

        if (task.isCompleted)
        {
            status = "COMPLETED";
            accentColor = new Color32(105, 99, 90, 255);
            backgroundColor = new Color32(216, 207, 191, 255);
        }
        else if (task.isReadyToTurnIn)
        {
            status = "READY TO TURN IN";
            accentColor = new Color32(126, 77, 8, 255);
            backgroundColor = new Color32(248, 210, 104, 255);
        }
        else
        {
            status = "ACTIVE";
            accentColor = task.isTutorial
                ? new Color32(45, 111, 128, 255)
                : new Color32(162, 88, 39, 255);
            backgroundColor = new Color32(255, 247, 225, 255);
        }

        if (background != null)
        {
            background.color = backgroundColor;
            background.type = Image.Type.Sliced;
        }

        if (button != null)
        {
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color32(255, 248, 222, 255);
            colors.pressedColor = new Color32(220, 207, 181, 255);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color32(170, 164, 151, 180);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
        }

        if (layout != null)
        {
            layout.minHeight = 120f;
            layout.preferredHeight = 126f;
            layout.flexibleWidth = 1f;
        }

        RectTransform cardRect = transform as RectTransform;
        if (cardRect != null)
            cardRect.sizeDelta = new Vector2(cardRect.sizeDelta.x, 126f);

        if (questTitleText != null)
        {
            string accentHex = ColorUtility.ToHtmlStringRGB(accentColor);
            questTitleText.text =
                $"<size=20><b><color=#{accentHex}>{category}  •  {status}</color></b></size>\n" +
                task.title;
            questTitleText.color = new Color32(73, 45, 29, 255);
            questTitleText.fontSize = 31f;
            questTitleText.enableAutoSizing = true;
            questTitleText.fontSizeMin = 22f;
            questTitleText.fontSizeMax = 31f;
            questTitleText.fontStyle = task.isCompleted ? FontStyles.Strikethrough : FontStyles.Normal;
            questTitleText.alignment = TextAlignmentOptions.MidlineLeft;
            questTitleText.margin = new Vector4(32f, 12f, 32f, 12f);
            questTitleText.characterSpacing = 0.5f;
            questTitleText.lineSpacing = 3f;
            questTitleText.overflowMode = TextOverflowModes.Ellipsis;
            questTitleText.raycastTarget = false;
        }

        // Status is now written directly on the card, so the old alert artwork is
        // intentionally unused.
        if (readyToTurnInIcon != null) readyToTurnInIcon.SetActive(false);
    }

    public void OnClicked()
    {
        if (ObjectiveTrackerUI.Instance != null && myTask != null)
        {
            ObjectiveTrackerUI.Instance.SelectTask(myTask);
        }
    }
}
