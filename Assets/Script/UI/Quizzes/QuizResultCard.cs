using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>A read-only, measured review of one submitted answer. It never changes quiz progress.</summary>
[DisallowMultipleComponent]
public sealed class QuizResultCard : MonoBehaviour
{
    public static readonly Color CorrectPaper = new Color32(229, 240, 211, 255);
    public static readonly Color IncorrectPaper = new Color32(252, 226, 219, 255);
    public static readonly Color CorrectInk = new Color32(53, 94, 44, 255);
    public static readonly Color IncorrectInk = new Color32(148, 51, 43, 255);

    public bool IsCorrect { get; private set; }
    public Image Face { get; private set; }
    public TMP_Text StatusLabel { get; private set; }
    public TMP_Text QuestionLabel { get; private set; }
    public TMP_Text YourAnswerLabel => yourAnswer != null ? yourAnswer.label : null;
    public TMP_Text CorrectAnswerLabel => correctAnswer != null ? correctAnswer.label : null;
    public TMP_Text FeedbackLabel { get; private set; }
    public Image YourAnswerImage => yourAnswer != null ? yourAnswer.artwork : null;
    public Image CorrectAnswerImage => correctAnswer != null ? correctAnswer.artwork : null;

    private ChoiceBlock yourAnswer;
    private ChoiceBlock correctAnswer;

    private sealed class ChoiceBlock
    {
        public RectTransform rect;
        public TMP_Text heading;
        public TMP_Text label;
        public Image artwork;
    }

    /// <summary>Builds plain-text review content from the submitted selection, not from the live choice controls.</summary>
    public static QuizResultCard Create(Transform parent, TMP_FontAsset font, Sprite cardSprite,
        QuizQuestion question, int questionNumber, int selectedIndex, IReadOnlyList<int> choiceOrder = null)
    {
        GameObject item = new GameObject("Question " + questionNumber + " Result", typeof(RectTransform), typeof(Image), typeof(QuizResultCard));
        item.layer = parent.gameObject.layer;
        item.transform.SetParent(parent, false);
        QuizResultCard card = item.GetComponent<QuizResultCard>();
        QuizChoice selected = GetChoice(question, selectedIndex);
        QuizChoice correct = GetChoice(question, question != null ? question.correctChoiceIndex : -1);
        card.IsCorrect = selected != null && correct != null && selectedIndex == question.correctChoiceIndex;

        Image border = item.GetComponent<Image>();
        ConfigureImage(border, cardSprite, QuizUILayout.Ink);
        card.Face = NewImage(item.transform, "Result Paper", cardSprite,
            card.IsCorrect ? CorrectPaper : IncorrectPaper);
        QuizUILayout.Stretch(card.Face.rectTransform, 2f, 2f, 2f, 2f);

        card.StatusLabel = NewText(item.transform, "Result Status", font,
            "Question " + questionNumber + (card.IsCorrect ? "  -  Correct" : selected != null ? "  -  Incorrect" : "  -  Not answered"));
        card.StatusLabel.color = card.IsCorrect ? CorrectInk : IncorrectInk;
        card.StatusLabel.fontStyle = FontStyles.Bold;
        card.QuestionLabel = NewText(item.transform, "Question", font, question != null ? question.prompt ?? string.Empty : string.Empty);
        card.yourAnswer = NewChoice(item.transform, "Your answer", font, selected,
            QuizProgressUtility.ChoiceDisplayIndex(question, choiceOrder, selectedIndex), "No answer");
        card.correctAnswer = NewChoice(item.transform, "Correct answer", font, correct,
            QuizProgressUtility.ChoiceDisplayIndex(question, choiceOrder,
                question != null ? question.correctChoiceIndex : -1), "Answer unavailable");
        card.correctAnswer.rect.gameObject.SetActive(!card.IsCorrect);
        card.FeedbackLabel = NewText(item.transform, "Explanation", font,
            !string.IsNullOrWhiteSpace(question != null ? question.feedback : null) ? "Explanation: " + question.feedback : string.Empty);
        card.FeedbackLabel.gameObject.SetActive(!string.IsNullOrWhiteSpace(card.FeedbackLabel.text));
        return card;
    }

    /// <summary>Returns the complete card height, including wrapped text and image answers.</summary>
    public float RequiredHeight(float width, float uiScale, float physicalScale)
    {
        return MeasureAndArrange(width, uiScale, physicalScale, false);
    }

    /// <summary>Uses the same metrics as RequiredHeight; the parent owns the card's position and size.</summary>
    public void Layout(float width, float height, float uiScale, float physicalScale)
    {
        MeasureAndArrange(width, uiScale, physicalScale, true);
    }

    private float MeasureAndArrange(float width, float uiScale, float physicalScale, bool arrange)
    {
        float unit = Mathf.Max(.01f, uiScale);
        float scale = Mathf.Max(.01f, physicalScale);
        float padding = Mathf.Min(20f * unit, Mathf.Max(0f, width) * .12f);
        float available = Mathf.Max(1f, width - padding * 2f);
        float gap = 12f * unit;
        SetFont(StatusLabel, 24f * unit, 18f, scale);
        SetFont(QuestionLabel, 28f * unit, 20f, scale);
        SetFont(FeedbackLabel, 24f * unit, 18f, scale);
        float y = padding;
        PlaceText(StatusLabel, available, padding, ref y, unit, arrange);
        y += 8f * unit;
        PlaceText(QuestionLabel, available, padding, ref y, unit, arrange);
        y += gap;
        PlaceChoice(yourAnswer, available, padding, ref y, unit, scale, arrange);
        if (correctAnswer != null && correctAnswer.rect.gameObject.activeSelf)
        {
            y += gap;
            PlaceChoice(correctAnswer, available, padding, ref y, unit, scale, arrange);
        }
        if (FeedbackLabel != null && FeedbackLabel.gameObject.activeSelf)
        {
            y += gap;
            PlaceText(FeedbackLabel, available, padding, ref y, unit, arrange);
        }
        if (arrange && Face != null) QuizUILayout.Stretch(Face.rectTransform, 2f * unit, 2f * unit, 2f * unit, 2f * unit);
        return y + padding;
    }

    private static void PlaceChoice(ChoiceBlock block, float width, float left, ref float y,
        float unit, float physicalScale, bool arrange)
    {
        if (block == null) return;
        SetFont(block.heading, 22f * unit, 18f, physicalScale);
        SetFont(block.label, 26f * unit, 20f, physicalScale);
        float innerY = 0f;
        PlaceText(block.heading, width, 0f, ref innerY, unit, arrange);
        innerY += 5f * unit;
        PlaceText(block.label, width, 0f, ref innerY, unit, arrange);
        if (block.artwork != null && block.artwork.gameObject.activeSelf)
        {
            innerY += 8f * unit;
            float imageHeight = Mathf.Min(180f * unit, Mathf.Max(80f * unit, width * .35f));
            if (arrange) Place(block.artwork.rectTransform, 0f, innerY, width, imageHeight);
            innerY += imageHeight;
        }
        if (arrange) Place(block.rect, left, y, width, innerY);
        y += innerY;
    }

    private static void PlaceText(TMP_Text text, float width, float left, ref float y, float unit, bool arrange)
    {
        if (text == null) return;
        // Bekind's capital glyphs extend above its nominal ascent. Give the
        // actual glyphs space on every edge rather than shrinking or clipping.
        float inset = Mathf.Max(4f * unit, text.fontSize * .12f);
        text.margin = new Vector4(inset, inset, inset, inset);
        // Preferred width may exceed a long unbroken word; only measured height controls this column.
        float height = Mathf.Max(text.fontSize * 1.1f + inset * 2f,
            text.GetPreferredValues(text.text, Mathf.Max(1f, width - inset * 2f), 0f).y) + 4f * unit;
        if (arrange) Place(text.rectTransform, left, y, width, height);
        y += height;
    }

    private static void Place(RectTransform rect, float left, float top, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(left, -top);
        rect.sizeDelta = new Vector2(width, height);
        rect.localScale = Vector3.one;
    }

    private static void SetFont(TMP_Text text, float desired, float minimumPixels, float physicalScale)
    {
        if (text != null) text.fontSize = Mathf.Max(desired, minimumPixels / physicalScale);
    }

    private static ChoiceBlock NewChoice(Transform parent, string heading, TMP_FontAsset font,
        QuizChoice choice, int index, string missing)
    {
        GameObject item = new GameObject(heading, typeof(RectTransform));
        item.layer = parent.gameObject.layer;
        item.transform.SetParent(parent, false);
        string letter = index >= 0 && index < 26 ? ((char)('A' + index)).ToString() : (index + 1).ToString();
        string answer = choice == null ? missing : letter + ". " + (choice.text ?? string.Empty);
        ChoiceBlock block = new ChoiceBlock
        {
            rect = item.GetComponent<RectTransform>(),
            heading = NewText(item.transform, "Label", font, heading),
            label = NewText(item.transform, "Answer", font, answer),
            artwork = NewImage(item.transform, "Answer Image", choice != null ? choice.image : null, Color.white)
        };
        block.heading.fontStyle = FontStyles.Bold;
        block.artwork.type = Image.Type.Simple;
        block.artwork.preserveAspect = true;
        block.artwork.gameObject.SetActive(choice != null && choice.image != null);
        return block;
    }

    private static QuizChoice GetChoice(QuizQuestion question, int index)
    {
        return question != null && question.choices != null && index >= 0 && index < question.choices.Count
            ? question.choices[index] : null;
    }

    private static TMP_Text NewText(Transform parent, string name, TMP_FontAsset font, string text)
    {
        GameObject item = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        item.layer = parent.gameObject.layer;
        item.transform.SetParent(parent, false);
        TMP_Text label = item.GetComponent<TMP_Text>();
        if (font != null) label.font = font;
        label.text = text;
        label.color = QuizUILayout.Ink;
        label.richText = false;
        label.raycastTarget = false;
        label.enableAutoSizing = false;
        label.enableWordWrapping = true;
        label.alignment = TextAlignmentOptions.TopLeft;
        label.overflowMode = TextOverflowModes.Overflow;
        label.margin = Vector4.zero;
        return label;
    }

    private static Image NewImage(Transform parent, string name, Sprite sprite, Color color)
    {
        GameObject item = new GameObject(name, typeof(RectTransform), typeof(Image));
        item.layer = parent.gameObject.layer;
        item.transform.SetParent(parent, false);
        Image image = item.GetComponent<Image>();
        ConfigureImage(image, sprite, color);
        return image;
    }

    private static void ConfigureImage(Image image, Sprite sprite, Color color)
    {
        image.sprite = sprite;
        image.type = sprite != null ? Image.Type.Sliced : Image.Type.Simple;
        image.color = color;
        image.raycastTarget = false;
    }
}
