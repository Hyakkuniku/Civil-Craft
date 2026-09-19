using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public enum CurrencyIconKind
{
    Coin,
    Experience,
    Diamond
}

/// <summary>
/// Scene-level source of truth for the game's currency artwork. UI systems use
/// this instead of keeping separate sprite references in every panel/prefab.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-1000)]
public sealed class CurrencyIconCatalog : MonoBehaviour
{
    public Sprite coinIcon;
    public Sprite experienceIcon;
    public Sprite diamondIcon;

    private static CurrencyIconCatalog cached;

    public static Sprite Get(CurrencyIconKind kind)
    {
        CurrencyIconCatalog catalog = Resolve();
        if (catalog == null) return null;

        switch (kind)
        {
            case CurrencyIconKind.Experience:
                return catalog.experienceIcon;
            case CurrencyIconKind.Diamond:
                return catalog.diamondIcon;
            default:
                return catalog.coinIcon;
        }
    }

    public static Image EnsureIcon(
        Transform parent,
        string objectName,
        CurrencyIconKind kind,
        Vector2 anchorMin,
        Vector2 anchorMax)
    {
        if (parent == null) return null;

        Transform existing = parent.Find(objectName);
        GameObject iconObject;
        if (existing != null)
        {
            iconObject = existing.gameObject;
        }
        else
        {
            iconObject = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            iconObject.transform.SetParent(parent, false);
        }
        iconObject.layer = parent.gameObject.layer;

        RectTransform rect = iconObject.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;

        Image image = iconObject.GetComponent<Image>();
        image.sprite = Get(kind);
        image.color = Color.white;
        image.preserveAspect = true;
        image.raycastTarget = false;
        image.enabled = image.sprite != null;
        return image;
    }

    private void OnEnable()
    {
        if (gameObject.scene.IsValid() && gameObject.scene.isLoaded)
            cached = this;
    }

    private void OnDisable()
    {
        if (cached == this) cached = null;
    }

    private static CurrencyIconCatalog Resolve()
    {
        if (IsUsable(cached)) return cached;

        CurrencyIconCatalog[] catalogs = Resources.FindObjectsOfTypeAll<CurrencyIconCatalog>();
        foreach (CurrencyIconCatalog catalog in catalogs)
        {
            if (!IsUsable(catalog)) continue;
            cached = catalog;
            return cached;
        }

        cached = null;
        return null;
    }

    private static bool IsUsable(CurrencyIconCatalog catalog)
    {
        if (catalog == null) return false;
        Scene scene = catalog.gameObject.scene;
        return scene.IsValid() && scene.isLoaded;
    }
}
