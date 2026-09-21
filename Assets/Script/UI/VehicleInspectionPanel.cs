using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>A stable scene-owned close target for a panel shared by many vehicles.</summary>
[DisallowMultipleComponent]
public sealed class VehicleInspectionPanel : MonoBehaviour
{
    public const int CurrentLayoutVersion = 1;
    private static readonly Vector2 Center = new Vector2(0.5f, 0.5f);
    [SerializeField, HideInInspector] private int layoutVersion;
    public int LayoutVersion => layoutVersion;

    /// <summary>
    /// Restyles the existing scene-authored inspection panel. No UI objects are
    /// spawned here; this only arranges and formats the references already wired
    /// to the active LiveLoadVehicle.
    /// </summary>
    public void ApplyLayout(TMP_Text nameText, TMP_Text weightText, TMP_Text speedText)
    {
        RectTransform panel = transform as RectTransform;
        if (panel == null) return;

        panel.anchorMin = Center;
        panel.anchorMax = Center;
        panel.pivot = Center;
        panel.anchoredPosition = Vector2.zero;
        panel.sizeDelta = new Vector2(820f, 420f);

        RectTransform body = transform.Find("Container") as RectTransform;
        if (body != null)
        {
            body.anchorMin = Vector2.zero;
            body.anchorMax = Vector2.one;
            body.pivot = Center;
            body.anchoredPosition = new Vector2(0f, -8f);
            body.sizeDelta = new Vector2(-34f, -34f);
        }

        if (nameText != null)
        {
            RectTransform title = nameText.rectTransform.parent as RectTransform;
            if (title != null)
            {
                title.anchorMin = new Vector2(0.5f, 1f);
                title.anchorMax = new Vector2(0.5f, 1f);
                title.pivot = Center;
                title.anchoredPosition = new Vector2(0f, 4f);
                title.sizeDelta = new Vector2(520f, 112f);
            }

            Stretch(nameText.rectTransform, 30f, 14f);
            nameText.fontSize = 38f;
            nameText.fontStyle = FontStyles.Bold;
            nameText.alignment = TextAlignmentOptions.Center;
            nameText.enableWordWrapping = false;
        }

        StyleStat(weightText, new Vector2(-175f, -28f), new Vector2(350f, 180f));
        StyleStat(speedText, new Vector2(185f, -28f), new Vector2(310f, 180f));

        Button closeButton = GetComponentInChildren<Button>(true);
        if (closeButton != null)
        {
            RectTransform close = closeButton.transform as RectTransform;
            while (close != null && close.parent != transform && close.parent is RectTransform parent)
                close = parent;
            if (close != null)
            {
                close.anchorMin = Vector2.one;
                close.anchorMax = Vector2.one;
                close.pivot = Center;
                close.anchoredPosition = new Vector2(-25f, -25f);
                close.sizeDelta = new Vector2(76f, 76f);
            }
        }

        layoutVersion = CurrentLayoutVersion;
    }

    private static void StyleStat(TMP_Text text, Vector2 position, Vector2 size)
    {
        if (text == null) return;
        RectTransform rect = text.rectTransform;
        rect.anchorMin = Center;
        rect.anchorMax = Center;
        rect.pivot = Center;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        text.fontSize = 30f;
        text.fontStyle = FontStyles.Normal;
        text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = true;
        text.richText = true;
        text.lineSpacing = 4f;
    }

    private static void Stretch(RectTransform rect, float horizontalInset, float verticalInset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = Center;
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(-horizontalInset * 2f, -verticalInset * 2f);
    }

    public void Close()
    {
        LiveLoadVehicle.CloseActiveInspectionPanel(gameObject);
    }
}
