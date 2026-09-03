using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class AlmanacMaterialButton : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private Image thumbnailImage;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private GameObject lockedOverlay;
    [SerializeField] private TMP_Text lockedLabel;
    [SerializeField] private Color unlockedColor = Color.white;
    [SerializeField] private Color lockedColor = new Color(0.45f, 0.45f, 0.45f, 1f);

    private Action<BridgeMaterialSO> clickHandler;
    private BridgeMaterialSO configuredMaterial;

    private void Reset()
    {
        button = GetComponent<Button>();
        backgroundImage = GetComponent<Image>();
        titleText = GetComponentInChildren<TMP_Text>(true);
    }

    private void OnDestroy()
    {
        if (button != null) button.onClick.RemoveListener(HandleClicked);
    }

    public void Configure(
        BridgeMaterialSO material,
        bool isDiscovered,
        Action<BridgeMaterialSO> onClicked)
    {
        configuredMaterial = material;
        clickHandler = onClicked;

        if (button == null) button = GetComponent<Button>();
        if (button != null)
        {
            button.onClick.RemoveListener(HandleClicked);
            button.interactable = isDiscovered;
            if (isDiscovered) button.onClick.AddListener(HandleClicked);
        }

        if (titleText != null)
            titleText.text = isDiscovered && material != null ? material.GetDisplayName() : "???";

        if (thumbnailImage != null)
        {
            thumbnailImage.sprite = isDiscovered && material != null ? material.materialIcon : null;
            thumbnailImage.enabled = thumbnailImage.sprite != null;
            thumbnailImage.preserveAspect = true;
            thumbnailImage.color = isDiscovered ? Color.white : lockedColor;
        }

        if (backgroundImage != null)
            backgroundImage.color = isDiscovered ? unlockedColor : lockedColor;

        if (lockedOverlay != null) lockedOverlay.SetActive(!isDiscovered);
        if (lockedLabel != null) lockedLabel.text = "???";
    }

    private void HandleClicked()
    {
        if (configuredMaterial != null) clickHandler?.Invoke(configuredMaterial);
    }
}
