#if UNITY_EDITOR
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[InitializeOnLoad]
public static class CurrencyIconSceneSetup
{
    private const string SessionKey = "CivilCraft.CurrencyIconSceneSetup.v3";
    private const string CoinPath = "Assets/Elements/UI/peso.png";
    private const string ExperiencePath = "Assets/Elements/UI/exp icon.png";
    private const string DiamondPath = "Assets/Elements/UI/diamond icon.png";
    private const string ShopCardPath = "Assets/Prefabs/UI/ShopItemCard.prefab";

    private static readonly string[] ScenePaths =
    {
        "Assets/Scenes/CanyonCrossing.unity",
        "Assets/Scenes/BHAN HOUSE.unity"
    };

    static CurrencyIconSceneSetup()
    {
        EditorApplication.delayCall += RunOnce;
    }

    [MenuItem("Tools/Civil Craft/Apply Currency Icons")]
    public static void ApplyAll()
    {
        Sprite coin = AssetDatabase.LoadAssetAtPath<Sprite>(CoinPath);
        Sprite experience = AssetDatabase.LoadAssetAtPath<Sprite>(ExperiencePath);
        Sprite diamond = AssetDatabase.LoadAssetAtPath<Sprite>(DiamondPath);
        if (coin == null || experience == null || diamond == null)
        {
            Debug.LogError("[Currency Icons] One or more currency sprites are missing.");
            return;
        }

        UpgradeShopCardPrefab(coin);
        foreach (string scenePath in ScenePaths)
            ApplyToScene(scenePath, coin, experience, diamond);

        AssetDatabase.SaveAssets();
        Debug.Log("[Currency Icons] Coin, EXP, and diamond artwork applied to gameplay UI.");
    }

    private static void RunOnce()
    {
        if (SessionState.GetBool(SessionKey, false)) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += RunOnce;
            return;
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        SessionState.SetBool(SessionKey, true);
        ApplyAll();
    }

    private static void ApplyToScene(
        string scenePath,
        Sprite coin,
        Sprite experience,
        Sprite diamond)
    {
        Scene scene = SceneManager.GetSceneByPath(scenePath);
        bool openedForSetup = !scene.IsValid() || !scene.isLoaded;
        if (openedForSetup)
            scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);

        Canvas mainCanvas = FindSceneComponents<Canvas>(scene)
            .FirstOrDefault(canvas => canvas.name == "MainCanvas") ??
            FindSceneComponents<Canvas>(scene).FirstOrDefault();
        if (mainCanvas != null)
        {
            Transform existing = FindRecursive(mainCanvas.transform, "CurrencyIconCatalog");
            GameObject catalogObject = existing != null
                ? existing.gameObject
                : new GameObject("CurrencyIconCatalog", typeof(CurrencyIconCatalog));
            if (existing == null) catalogObject.transform.SetParent(mainCanvas.transform, false);

            CurrencyIconCatalog catalog = catalogObject.GetComponent<CurrencyIconCatalog>();
            if (catalog == null) catalog = catalogObject.AddComponent<CurrencyIconCatalog>();
            catalog.coinIcon = coin;
            catalog.experienceIcon = experience;
            catalog.diamondIcon = diamond;
            EditorUtility.SetDirty(catalog);
        }

        ShopManager shop = FindSceneComponents<ShopManager>(scene).FirstOrDefault();
        if (shop != null) DecorateShop(shop, coin, diamond);

        AlmanacPlayerStats stats = FindSceneComponents<AlmanacPlayerStats>(scene).FirstOrDefault();
        if (stats != null) DecorateAlmanac(stats, coin, experience, diamond);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        if (openedForSetup) EditorSceneManager.CloseScene(scene, true);
    }

    private static void DecorateShop(ShopManager shop, Sprite coin, Sprite diamond)
    {
        Transform primaryPill = FindRecursive(shop.transform, "CurrencyPill");
        Transform secondaryPill = FindRecursive(shop.transform, "SecondaryCurrencyPill");
        if (primaryPill != null)
        {
            EnsureIcon(primaryPill, "CoinIcon", coin,
                new Vector2(0.06f, 0.15f), new Vector2(0.31f, 0.85f));
            SetTextRect(FindRecursive(primaryPill, "CurrencyText"),
                new Vector2(0.29f, 0f), new Vector2(0.96f, 1f));
        }
        if (secondaryPill != null)
        {
            EnsureIcon(secondaryPill, "DiamondIcon", diamond,
                new Vector2(0.08f, 0.16f), new Vector2(0.43f, 0.84f));
            SetTextRect(FindRecursive(secondaryPill, "SecondaryCurrencyText"),
                new Vector2(0.39f, 0f), new Vector2(0.94f, 1f));
        }

        Transform confirmationPrice = FindRecursive(shop.transform, "ConfirmationPrice");
        if (confirmationPrice != null && confirmationPrice.parent != null)
        {
            EnsureIcon(confirmationPrice.parent, "ConfirmationCoinIcon", coin,
                new Vector2(0.43f, 0.30f), new Vector2(0.50f, 0.43f));
            SetTextRect(confirmationPrice, new Vector2(0.50f, 0.28f), new Vector2(0.92f, 0.44f));
            TMP_Text confirmationText = confirmationPrice.GetComponent<TMP_Text>();
            if (confirmationText != null) confirmationText.text = "0";
        }

        SerializedObject serializedShop = new SerializedObject(shop);
        SerializedProperty prefix = serializedShop.FindProperty("currencyPrefix");
        if (prefix != null) prefix.stringValue = string.Empty;
        serializedShop.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(shop);
    }

    private static void DecorateAlmanac(
        AlmanacPlayerStats stats,
        Sprite coin,
        Sprite experience,
        Sprite diamond)
    {
        Transform root = stats.transform;
        Transform goldCard = FindRecursive(root, "GoldCard");
        Transform diamondCard = FindRecursive(root, "SecondaryCurrencyCard");
        Transform overview = FindRecursive(root, "OverviewCard");

        if (goldCard != null)
        {
            EnsureIcon(goldCard, "CoinIcon", coin,
                new Vector2(0.04f, 0.18f), new Vector2(0.20f, 0.82f));
            SetTextRect(FindRecursive(goldCard, "GoldLabel"),
                new Vector2(0.21f, 0f), new Vector2(0.60f, 1f));
        }
        if (diamondCard != null)
        {
            EnsureIcon(diamondCard, "DiamondIcon", diamond,
                new Vector2(0.04f, 0.18f), new Vector2(0.20f, 0.82f));
            SetTextRect(FindRecursive(diamondCard, "SecondaryCurrencyLabel"),
                new Vector2(0.21f, 0f), new Vector2(0.62f, 1f));
        }
        if (overview != null)
        {
            EnsureIcon(overview, "ExperienceIcon", experience,
                new Vector2(0.04f, 0.345f), new Vector2(0.115f, 0.515f));
            Transform label = FindRecursive(overview, "ExpLabel");
            SetTextRect(label, new Vector2(0.125f, 0.35f), new Vector2(0.40f, 0.51f));
            TMP_Text text = label != null ? label.GetComponent<TMP_Text>() : null;
            if (text != null) text.text = "EXPERIENCE";
        }
    }

    private static void UpgradeShopCardPrefab(Sprite coin)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(ShopCardPath);
        if (root == null) return;
        try
        {
            Transform buy = FindRecursive(root.transform, "BuyButton");
            ShopItemUI card = root.GetComponent<ShopItemUI>();
            if (buy == null || card == null) return;

            Image icon = EnsureIcon(buy, "PriceCoinIcon", coin,
                new Vector2(0.08f, 0.18f), new Vector2(0.27f, 0.82f));
            Transform price = FindRecursive(buy, "Text");
            if (price == null)
                price = buy.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault()?.transform;
            SetTextRect(price, new Vector2(0.25f, 0f), new Vector2(0.94f, 1f));
            TMP_Text priceText = price != null ? price.GetComponent<TMP_Text>() : null;
            if (priceText != null) priceText.text = "0";

            SerializedObject serializedCard = new SerializedObject(card);
            SerializedProperty property = serializedCard.FindProperty("priceCoinIcon");
            if (property != null) property.objectReferenceValue = icon;
            serializedCard.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, ShopCardPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static Image EnsureIcon(
        Transform parent,
        string name,
        Sprite sprite,
        Vector2 anchorMin,
        Vector2 anchorMax)
    {
        Transform child = parent.Find(name);
        GameObject iconObject = child != null
            ? child.gameObject
            : new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        if (child == null) iconObject.transform.SetParent(parent, false);
        iconObject.layer = parent.gameObject.layer;

        RectTransform rect = iconObject.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;

        Image image = iconObject.GetComponent<Image>();
        image.sprite = sprite;
        image.color = Color.white;
        image.preserveAspect = true;
        image.raycastTarget = false;
        return image;
    }

    private static void SetTextRect(Transform target, Vector2 anchorMin, Vector2 anchorMax)
    {
        RectTransform rect = target as RectTransform;
        if (rect == null) return;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static Transform FindRecursive(Transform parent, string name)
    {
        if (parent == null) return null;
        foreach (Transform child in parent)
        {
            if (child.name == name) return child;
            Transform nested = FindRecursive(child, name);
            if (nested != null) return nested;
        }
        return null;
    }

    private static T[] FindSceneComponents<T>(Scene scene) where T : Component
    {
        return Resources.FindObjectsOfTypeAll<T>()
            .Where(component => component != null && component.gameObject.scene == scene)
            .ToArray();
    }
}
#endif
