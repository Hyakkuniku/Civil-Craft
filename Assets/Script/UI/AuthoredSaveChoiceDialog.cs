using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A scene-authored modal. Only its copy and callbacks change at runtime;
/// the Canvas, layout, graphics, and buttons live in the scene/prefab.
/// </summary>
public sealed class AuthoredSaveChoiceDialog : MonoBehaviour
{
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text messageText;
    [SerializeField] private TMP_Text leftLabel;
    [SerializeField] private TMP_Text rightLabel;
    [SerializeField] private Button leftButton;
    [SerializeField] private Button rightButton;

    private Action leftAction;
    private Action rightAction;

    public bool IsVisible => gameObject.activeSelf;

    private void OnEnable()
    {
        UIReservedRegionLayout.Register(transform as RectTransform);
    }

    private void OnDisable()
    {
        UIReservedRegionLayout.Unregister(transform as RectTransform);
    }

    public bool Show(string title, string message, string leftText, string rightText,
        Action onLeft, Action onRight)
    {
        if (titleText == null || messageText == null || leftLabel == null || rightLabel == null ||
            leftButton == null || rightButton == null)
        {
            Debug.LogError("The authored save-choice dialog has missing UI references.", this);
            return false;
        }

        titleText.text = title;
        messageText.text = message;
        leftLabel.text = leftText;
        rightLabel.text = rightText;
        leftAction = onLeft;
        rightAction = onRight;
        leftButton.onClick.RemoveListener(ChooseLeft);
        rightButton.onClick.RemoveListener(ChooseRight);
        leftButton.onClick.AddListener(ChooseLeft);
        rightButton.onClick.AddListener(ChooseRight);
        gameObject.SetActive(true);
        return true;
    }

    public void Hide()
    {
        if (leftButton != null) leftButton.onClick.RemoveListener(ChooseLeft);
        if (rightButton != null) rightButton.onClick.RemoveListener(ChooseRight);
        leftAction = null;
        rightAction = null;
        gameObject.SetActive(false);
    }

    private void ChooseLeft() => leftAction?.Invoke();
    private void ChooseRight() => rightAction?.Invoke();

#if UNITY_EDITOR
    public void SetAuthoringReferences(TMP_Text title, TMP_Text message, TMP_Text left,
        TMP_Text right, Button leftControl, Button rightControl)
    {
        titleText = title;
        messageText = message;
        leftLabel = left;
        rightLabel = right;
        leftButton = leftControl;
        rightButton = rightControl;
    }
#endif
}
