using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class CosmeticOptionButton : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text label;
    [SerializeField] private GameObject selectedVisual;
    [SerializeField] private GameObject lockedVisual;

    private CosmeticDefinition definition;
    private Action<CosmeticDefinition> onClicked;

    public string CosmeticId => definition != null ? definition.PermanentID : string.Empty;
    public RectTransform ButtonTarget => button != null
        ? button.GetComponent<RectTransform>() : null;

    private void Awake()
    {
        if (button == null) button = GetComponent<Button>();
        if (button != null) button.onClick.AddListener(HandleClick);
    }

    private void OnDestroy()
    {
        if (button != null) button.onClick.RemoveListener(HandleClick);
    }

    public void Bind(CosmeticDefinition value, bool unlocked, bool selected,
        Action<CosmeticDefinition> callback)
    {
        definition = value;
        onClicked = callback;
        gameObject.SetActive(value != null);
        if (value == null) return;

        if (label != null) label.text = value.displayName;
        if (icon != null)
        {
            icon.sprite = value.icon;
            icon.enabled = value.icon != null;
            icon.preserveAspect = true;
        }
        if (selectedVisual != null) selectedVisual.SetActive(selected);
        if (lockedVisual != null) lockedVisual.SetActive(!unlocked);
        if (button != null) button.interactable = unlocked;
    }

    private void HandleClick()
    {
        if (definition != null) onClicked?.Invoke(definition);
    }
}
