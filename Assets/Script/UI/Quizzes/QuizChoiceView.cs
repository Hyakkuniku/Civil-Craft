using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>A single reusable choice card. It never knows which answer is correct.</summary>
[DisallowMultipleComponent]
public sealed class QuizChoiceView : MonoBehaviour
{
    public Button button;
    public Image face;
    public Image artwork;
    public TMP_Text label;
    public TMP_Text number;
    private int choiceIndex;
    private Action<int> selected;
    private bool hasImage;
    private bool hasText;

    public void Bind(QuizChoice choice, int index, Action<int> onSelected)
    {
        choiceIndex = index;
        selected = onSelected;
        hasImage = choice != null && choice.image != null;
        hasText = choice != null && !string.IsNullOrWhiteSpace(choice.text);
        if (number != null) number.text = index < 26 ? ((char)('A' + index)).ToString() : (index + 1).ToString();
        if (label != null) { label.text = choice != null ? choice.text ?? string.Empty : string.Empty; label.richText = false; label.gameObject.SetActive(hasText); }
        if (artwork != null) { artwork.sprite = choice != null ? choice.image : null; artwork.preserveAspect = true; artwork.gameObject.SetActive(hasImage); }
        if (button != null)
        {
            button.onClick.RemoveListener(Click);
            button.onClick.AddListener(Click);
            button.transition = Selectable.Transition.None;
        }
        SetSelected(false, false);
    }

    private void Click() { if (button != null && button.interactable) selected?.Invoke(choiceIndex); }

    public void SetSelected(bool value, bool locked)
    {
        if (face != null) face.color = value ? QuizUILayout.Gold : QuizUILayout.Cream;
        if (number != null) number.color = QuizUILayout.Ink;
        if (button != null) button.interactable = !locked;
    }

    public float RequiredHeight(float width, float unit)
    {
        float textHeight = hasText && label != null ? label.GetPreferredValues(label.text, Mathf.Max(20f, width - 48f * unit), 0f).y : 0f;
        return Mathf.Max(112f * unit, 72f * unit + textHeight + (hasImage ? 190f * unit : 0f));
    }

    public void Layout(float width, float height, float unit, float physicalScale = 1f)
    {
        if (number != null)
        {
            QuizUILayout.Top(number.rectTransform, 20f * unit, 20f * unit, 12f * unit, 36f * unit);
            number.fontSize = Mathf.Max(25f * unit, 18f / Mathf.Max(.01f, physicalScale));
        }
        float textHeight = hasText && label != null ? label.GetPreferredValues(label.text, Mathf.Max(20f, width - 48f * unit), 0f).y : 0f;
        if (label != null)
        {
            label.fontSize = Mathf.Max(29f * unit, 20f / Mathf.Max(.01f, physicalScale));
            label.alignment = hasImage ? TextAlignmentOptions.Center : TextAlignmentOptions.TopLeft;
            label.enableWordWrapping = true;
            label.overflowMode = TextOverflowModes.Overflow;
            textHeight = hasText ? label.GetPreferredValues(label.text, Mathf.Max(20f, width - 48f * unit), 0f).y : 0f;
            if (hasImage) QuizUILayout.Bottom(label.rectTransform, 24f * unit, 24f * unit, 16f * unit, textHeight);
            else QuizUILayout.Stretch(label.rectTransform, 24f * unit, 18f * unit, 24f * unit, 54f * unit);
        }
        if (artwork != null)
            QuizUILayout.Stretch(artwork.rectTransform, 26f * unit, (hasText ? textHeight + 32f * unit : 18f * unit), 26f * unit, 56f * unit);
    }
}
