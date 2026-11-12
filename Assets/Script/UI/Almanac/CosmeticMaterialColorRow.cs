using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>One authored picker for a distinct wardrobe material.</summary>
public sealed class CosmeticMaterialColorRow : MonoBehaviour
{
    [SerializeField] private TMP_Text label;
    [SerializeField] private List<CosmeticColorButtonBinding> swatches = new List<CosmeticColorButtonBinding>();
    private readonly Dictionary<Button, UnityAction> listeners = new Dictionary<Button, UnityAction>();
    private readonly Dictionary<Button, Image> diamondBadges = new Dictionary<Button, Image>();
    private Action<int, int> onSelected;
    private int materialIndex;

    public int Capacity => swatches.Count;

    public void Bind(int index, Color[] palette, Color selected, Action<int, int> callback, Func<Color, bool> isOwned = null)
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
            bool locked = palette[i].a > .001f && isOwned != null && !isOwned(palette[i]);
            if (swatch.colorImage != null)
                swatch.colorImage.color = palette[i].a <= .001f ? Color.white :
                    locked ? Color.Lerp(palette[i], new Color(.23f, .18f, .14f), .32f) : palette[i];
            if (swatch.selectedVisual != null)
                swatch.selectedVisual.SetActive(!locked && Distance(palette[i], selected) < .02f);
            if (locked && !diamondBadges.ContainsKey(swatch.button))
            {
                Image badge = CurrencyIconCatalog.EnsureIcon(swatch.button.transform, "Locked color diamond",
                    CurrencyIconKind.Diamond, new Vector2(.48f, .02f), new Vector2(.98f, .52f));
                diamondBadges.Add(swatch.button, badge);
            }
            if (diamondBadges.TryGetValue(swatch.button, out Image existingBadge) && existingBadge != null)
                existingBadge.gameObject.SetActive(locked && existingBadge.sprite != null);
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
