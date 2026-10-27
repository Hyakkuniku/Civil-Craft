using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Restyles the existing close control without replacing its Button, events or tutorial target.</summary>
public static class AlmanacCloseButtonStyle
{
    public static RectTransform Apply(Button button, TMP_FontAsset font)
    {
        if (button == null || !(button.transform is RectTransform rect)) return null;
        RectTransform control = rect;
        RawImage oval = FindCompactWrapper(rect);
        if (oval != null)
        {
            control = oval.rectTransform;
            oval.enabled = false;
            oval.raycastTarget = false;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one * .5f;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
        // Both authored scenes use a small oval wrapper; the shared prefab has
        // a direct button. Never resize or hide that prefab's full book parent.
        control.anchorMin = control.anchorMax = Vector2.one;
        control.pivot = Vector2.one;
        control.anchoredPosition = new Vector2(-18f, -12f);
        control.sizeDelta = new Vector2(190f, 76f);
        control.localScale = Vector3.one;
        control.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity; // The old X artwork was upside down.

        Image border = button.GetComponent<Image>();
        if (border == null) border = button.gameObject.AddComponent<Image>();
        border.sprite = DialogueManager.GetSkipRoundedSprite();
        border.type = Image.Type.Sliced;
        border.preserveAspect = false;
        border.enabled = true;
        border.color = new Color32(145, 84, 48, 255);
        border.raycastTarget = true;
        button.targetGraphic = border;
        button.transition = Selectable.Transition.ColorTint;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color32(255, 245, 220, 255);
        colors.pressedColor = new Color32(229, 202, 151, 255);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = new Color(1f, 1f, 1f, .45f);
        colors.fadeDuration = .08f;
        button.colors = colors;

        Shadow shadow = button.GetComponent<Shadow>();
        if (shadow == null) shadow = button.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color32(103, 58, 35, 180);
        shadow.effectDistance = new Vector2(0f, -5f);

        RectTransform faceRect = Child(rect, "Cream Face");
        Image face = faceRect.GetComponent<Image>();
        if (face == null) face = faceRect.gameObject.AddComponent<Image>();
        face.sprite = border.sprite;
        face.type = Image.Type.Sliced;
        face.color = new Color32(239, 216, 167, 255);
        face.raycastTarget = false;
        faceRect.offsetMin = new Vector2(5f, 8f);
        faceRect.offsetMax = new Vector2(-5f, -5f);

        RectTransform labelRect = Child(rect, "Label");
        TextMeshProUGUI label = labelRect.GetComponent<TextMeshProUGUI>();
        if (label == null) label = labelRect.gameObject.AddComponent<TextMeshProUGUI>();
        label.text = "CLOSE";
        if (font != null) label.font = font;
        label.fontSize = 32f;
        label.enableAutoSizing = true;
        label.fontSizeMin = 22f;
        label.fontSizeMax = 32f;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Truncate;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color32(132, 74, 43, 255);
        label.raycastTarget = false;
        labelRect.SetAsLastSibling();
        return control;
    }

    private static RawImage FindCompactWrapper(RectTransform button)
    {
        RectTransform parent = button.parent as RectTransform;
        if (parent == null || parent.childCount != 1 || button.anchorMin != Vector2.zero ||
            button.anchorMax != Vector2.one || parent.anchorMin != parent.anchorMax ||
            parent.sizeDelta.x <= 0f || parent.sizeDelta.y <= 0f ||
            parent.sizeDelta.x > 320f || parent.sizeDelta.y > 200f) return null;
        return parent.GetComponent<RawImage>();
    }

    private static RectTransform Child(RectTransform parent, string name)
    {
        RectTransform rect = parent.Find(name) as RectTransform;
        if (rect == null)
        {
            var child = new GameObject(name, typeof(RectTransform));
            child.layer = parent.gameObject.layer;
            rect = (RectTransform)child.transform;
            rect.SetParent(parent, false);
        }
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        return rect;
    }
}
