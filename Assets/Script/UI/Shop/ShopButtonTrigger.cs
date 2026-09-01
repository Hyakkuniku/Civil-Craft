using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class ShopButtonTrigger : MonoBehaviour
{
    [SerializeField] private ShopManager shopManager;
    [SerializeField] private Button button;
    [SerializeField] private bool wireButtonAutomatically = true;

    private void Reset()
    {
        button = GetComponent<Button>();
    }

    private void Awake()
    {
        if (button == null)
            button = GetComponent<Button>();

        ResolveShopManager();

        if (wireButtonAutomatically && button != null)
            button.onClick.AddListener(OpenShop);
    }

    private void OnDestroy()
    {
        if (wireButtonAutomatically && button != null)
            button.onClick.RemoveListener(OpenShop);
    }

    public void OpenShop()
    {
        ResolveShopManager();
        if (shopManager != null)
            shopManager.OpenShop();
        else
            Debug.LogError("[ShopButtonTrigger] No ShopManager exists in this scene.", this);
    }

    private void ResolveShopManager()
    {
        if (shopManager == null)
            shopManager = FindObjectOfType<ShopManager>(true);
    }
}
