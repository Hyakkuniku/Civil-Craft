using UnityEngine;
using UnityEngine.UI;

/// <summary>Center the portrait without stretching its render texture or fighting wardrobe transitions.</summary>
[DisallowMultipleComponent]
public sealed class AlmanacPortraitFit : MonoBehaviour
{
    [SerializeField] private RectTransform homeArea;
    private bool applying;
    private void OnEnable() => FitNow();
    private void LateUpdate() => FitNow();
    public void FitNow()
    {
        var portrait = transform as RectTransform;
        if (applying || portrait == null || homeArea == null || portrait.parent != homeArea.parent) return;
        var parent = portrait.parent as RectTransform;
        var image = GetComponent<RawImage>();
        if (parent == null || image == null || image.texture == null) return;
        Vector2 available = homeArea.rect.size;
        float aspect = (float)image.texture.width / Mathf.Max(1, image.texture.height);
        Vector2 fitted = available.x / Mathf.Max(.01f, available.y) > aspect
            ? new Vector2(available.y * aspect, available.y) : new Vector2(available.x, available.x / aspect);
        Vector2 center = parent.InverseTransformPoint(homeArea.TransformPoint(homeArea.rect.center));
        center -= parent.rect.center;
        if (portrait.anchorMin == Vector2.one * .5f && portrait.anchorMax == Vector2.one * .5f &&
            (portrait.sizeDelta - fitted).sqrMagnitude < .01f && (portrait.anchoredPosition - center).sqrMagnitude < .01f) return;
        applying = true;
        portrait.anchorMin = portrait.anchorMax = portrait.pivot = Vector2.one * .5f;
        portrait.sizeDelta = fitted; portrait.anchoredPosition = center;
        portrait.localScale = Vector3.one;
        applying = false;
    }
}
