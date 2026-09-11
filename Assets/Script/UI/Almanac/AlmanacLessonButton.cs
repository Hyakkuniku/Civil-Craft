using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class AlmanacLessonButton : MonoBehaviour
{
    [Header("Required")]
    [SerializeField] private Button button;
    [SerializeField] private TMP_Text titleText;

    [Header("Optional Visuals")]
    [SerializeField] private Image thumbnailImage;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private GameObject lockedOverlay;
    [SerializeField] private TMP_Text lockedLabel;
    [SerializeField] private Color unlockedColor = Color.white;
    [SerializeField] private Color lockedColor = new Color(0.45f, 0.45f, 0.45f, 1f);
    [SerializeField] private Color selectedColor = new Color(0.82f, 0.68f, 0.48f, 1f);

    private Action<LessonData> clickHandler;
    private LessonData configuredLesson;
    private bool configuredAsUnlocked;
    private bool configuredAsSelectable;
    private bool isSelected;
    private int displayNumber;

    private void Reset()
    {
        button = GetComponent<Button>();
        backgroundImage = GetComponent<Image>();
        titleText = GetComponentInChildren<TMP_Text>(true);
    }

    private void OnDestroy()
    {
        if (button != null)
            button.onClick.RemoveListener(HandleClicked);
    }

    public void Configure(
        LessonData lesson,
        bool isUnlocked,
        Action<LessonData> onClicked,
        bool allowLockedOpen = false)
    {
        configuredLesson = lesson;
        clickHandler = onClicked;
        configuredAsUnlocked = isUnlocked;
        displayNumber = 0;
        isSelected = false;
        bool canOpen = isUnlocked || allowLockedOpen;
        configuredAsSelectable = canOpen;

        if (button == null) button = GetComponent<Button>();
        if (button != null)
        {
            button.onClick.RemoveListener(HandleClicked);
            button.interactable = canOpen;
            if (canOpen) button.onClick.AddListener(HandleClicked);
        }

        RefreshTitle(canOpen);

        if (thumbnailImage != null)
        {
            thumbnailImage.sprite = canOpen && lesson != null ? lesson.Image : null;
            thumbnailImage.enabled = thumbnailImage.sprite != null;
            thumbnailImage.color = canOpen ? Color.white : lockedColor;
        }

        RefreshBackground();

        if (lockedOverlay != null) lockedOverlay.SetActive(!canOpen);
        if (lockedLabel != null) lockedLabel.text = canOpen ? string.Empty : "Locked";
    }

    public void SetDisplayNumber(int number)
    {
        displayNumber = Mathf.Max(0, number);
        RefreshTitle(configuredAsUnlocked || (button != null && button.interactable));
    }

    public void SetSelected(bool selected)
    {
        isSelected = selected && configuredAsSelectable;
        RefreshBackground();
    }

    private void HandleClicked()
    {
        if (configuredLesson != null)
            clickHandler?.Invoke(configuredLesson);
    }

    private void RefreshTitle(bool canOpen)
    {
        if (titleText == null) return;

        string number = displayNumber > 0 ? displayNumber.ToString("00") + "   " : string.Empty;
        string title = canOpen && configuredLesson != null
            ? configuredLesson.Title
            : "Undiscovered lesson";
        titleText.text = number + title;
        titleText.alignment = TextAlignmentOptions.MidlineLeft;
        titleText.margin = new Vector4(24f, 0f, 18f, 0f);
    }

    private void RefreshBackground()
    {
        if (backgroundImage == null) return;
        backgroundImage.color = isSelected ? selectedColor :
            (configuredAsUnlocked ? unlockedColor : lockedColor);
    }
}
