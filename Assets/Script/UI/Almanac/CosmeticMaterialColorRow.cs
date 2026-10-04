using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>One scene-authored picker for one distinct item material. No runtime UI creation.</summary>
public sealed class CosmeticMaterialColorRow : MonoBehaviour
{
    [SerializeField] private TMP_Text label;
    [SerializeField] private List<CosmeticColorButtonBinding> swatches = new List<CosmeticColorButtonBinding>();
    private readonly Dictionary<Button, UnityAction> listeners = new Dictionary<Button, UnityAction>();
    private Action<int, int> onSelected;
    private int materialIndex;

    public int Capacity => swatches.Count;

    public void Bind(int index, Color[] palette, Color selected, Action<int, int> callback)
    {
        materialIndex = index;
        onSelected = callback;
        if (label != null) label.text = "COLOR " + (index + 1);
        for (int i = 0; i < swatches.Count; i++)
        {
            var swatch = swatches[i];
            if (swatch == null || swatch.button == null) continue;
            if (!listeners.ContainsKey(swatch.button))
            {
                int colorIndex = i;
                UnityAction action = () => onSelected?.Invoke(materialIndex, colorIndex);
                swatch.button.onClick.AddListener(action);
                listeners.Add(swatch.button, action);
            }
            bool visible = palette != null && i < palette.Length;
            swatch.button.gameObject.SetActive(visible);
            if (!visible) continue;
            if (swatch.colorImage != null)
                swatch.colorImage.color = palette[i].a <= .001f ? Color.white : palette[i];
            if (swatch.selectedVisual != null)
                swatch.selectedVisual.SetActive(Distance(palette[i], selected) < .02f);
        }
    }

    private void OnDestroy()
    {
        foreach (var listener in listeners)
            if (listener.Key != null) listener.Key.onClick.RemoveListener(listener.Value);
    }

    private static float Distance(Color a, Color b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) +
        Mathf.Abs(a.b - b.b) + Mathf.Abs(a.a - b.a);
}
