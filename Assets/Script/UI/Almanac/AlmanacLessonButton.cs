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

    private Action<LessonData> clickHandler;
    private LessonData configuredLesson;

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
        bool canOpen = isUnlocked || allowLockedOpen;

        if (button == null) button = GetComponent<Button>();
        if (button != null)
        {
            button.onClick.RemoveListener(HandleClicked);
            button.interactable = canOpen;
            if (canOpen) button.onClick.AddListener(HandleClicked);
        }

        if (titleText != null)
            titleText.text = canOpen && lesson != null ? lesson.Title : "???";

        if (thumbnailImage != null)
        {
            thumbnailImage.sprite = canOpen && lesson != null ? lesson.Image : null;
            thumbnailImage.enabled = thumbnailImage.sprite != null;
            thumbnailImage.color = canOpen ? Color.white : lockedColor;
        }

        if (backgroundImage != null)
            backgroundImage.color = canOpen ? unlockedColor : lockedColor;

        if (lockedOverlay != null) lockedOverlay.SetActive(!canOpen);
        if (lockedLabel != null) lockedLabel.text = canOpen ? string.Empty : "Locked";
    }

    private void HandleClicked()
    {
        if (configuredLesson != null)
            clickHandler?.Invoke(configuredLesson);
    }
}
