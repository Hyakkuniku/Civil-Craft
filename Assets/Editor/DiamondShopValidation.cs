#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class DiamondShopValidation
{
    private const string Output = "Logs/DiamondShop";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Civil Craft/Shop/Validate Diamond Items and Colors")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play Mode before running the detached previews.");
        Directory.CreateDirectory(Output);
        Scene preview = EditorSceneManager.NewPreviewScene();
        FieldInfo iconCache = typeof(CurrencyIconCatalog).GetField("cached", BindingFlags.Static | BindingFlags.NonPublic);
        object originalIconCache = iconCache.GetValue(null);
        try
        {
            ValidateColors();
            var root = new GameObject("Diamond shop preview", typeof(RectTransform), typeof(Canvas));
            SceneManager.MoveGameObjectToScene(root, preview);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            root.transform.localScale = Vector3.one * .01f;
            var cameraObject = new GameObject("Diamond preview camera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, preview);
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.enabled = false;
            camera.scene = preview;
            camera.orthographic = true;
            camera.orthographicSize = 5.4f;
            camera.transform.position = new Vector3(0f, 0f, -20f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(139, 111, 88, 255);
            cameraObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;

            var cardPrefab = AssetDatabase.LoadAssetAtPath<ShopItemUI>("Assets/Prefabs/UI/ShopItemCard.prefab");
            var wardrobePrefab = AssetDatabase.LoadAssetAtPath<CharacterCustomizationController>("Assets/Prefabs/UI/CharacterCustomizationPanel.prefab");
            var dialogPrefab = Resources.Load<AuthoredSaveChoiceDialog>("UI/ColorPurchaseDialog");
            Require(cardPrefab != null && wardrobePrefab != null && dialogPrefab != null, "Authored shop, wardrobe or purchase dialog is missing.");
            var catalog = root.AddComponent<CurrencyIconCatalog>();
            foreach (CurrencyIconCatalog source in Resources.FindObjectsOfTypeAll<CurrencyIconCatalog>())
            {
                if (source == catalog || source.diamondIcon == null) continue;
                catalog.coinIcon = source.coinIcon;
                catalog.diamondIcon = source.diamondIcon;
                break;
            }
            if (catalog.diamondIcon == null)
            {
                string[] sprites = AssetDatabase.FindAssets("diamond t:Sprite", new[] { "Assets/Elements" });
                foreach (string guid in sprites)
                {
                    Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(guid));
                    if (sprite == null) continue;
                    catalog.diamondIcon = sprite;
                    break;
                }
            }
            Require(catalog.diamondIcon != null, "The Diamond currency artwork is missing.");
            iconCache.SetValue(null, catalog);
            var shop = root.AddComponent<ShopManager>();
            var cards = new List<ShopItemUI>();
            string[] products = { "Accessory/Accessory_LargeCap", "Hair/Hair_8", "Shirt/Shirt_Polo", "Pants/Pants_Cargo", "Shoes/Shoes_Boots2" };
            for (int i = 0; i < products.Length; i++)
            {
                var item = AssetDatabase.LoadAssetAtPath<ShopItemData>("Assets/BridgeBuilder/Data/SHOP/" + products[i] + ".asset");
                Require(item != null && item.diamondPrice == 25 && item.cosmeticDefinition != null, "Diamond product configuration is incomplete.");
                var card = Object.Instantiate(cardPrefab, root.transform, false);
                card.gameObject.SetActive(true);
                card.Bind(item, shop);
                TMP_Text price = Get<TMP_Text>(card, "priceText");
                Require(price.text == "25", "The Diamond card price is not numeric beside its currency icon.");
                Require(Get<Image>(card, "priceCoinIcon").sprite == catalog.diamondIcon, "The card displays the wrong currency.");
                cards.Add(card);
            }

            var wardrobe = Object.Instantiate(wardrobePrefab, root.transform, false);
            wardrobe.gameObject.SetActive(false);
            var rowTemplate = wardrobe.GetComponentsInChildren<CosmeticMaterialColorRow>(true).First();
            var row = Object.Instantiate(rowTemplate, root.transform, false);
            row.gameObject.SetActive(true);
            var definition = AssetDatabase.LoadAssetAtPath<CosmeticDefinition>("Assets/BridgeBuilder/Data/Cosmetics/Shirt_Tee1.asset");
            Require(definition != null && definition.availableColors.Length >= 2, "The production color palette is missing.");
            var swatches = Get<List<CosmeticColorButtonBinding>>(row, "swatches");
            int selected = -1;
            row.Bind(0, definition.availableColors, Color.clear, (_, color) => selected = color, color => color.a <= .001f);
            Require(swatches[0].selectedVisual.activeSelf, "The free default color is not selected.");
            for (int i = 1; i < definition.availableColors.Length; i++)
            {
                Image badge = swatches[i].button.transform.Find("Locked color diamond").GetComponent<Image>();
                Require(badge.sprite == catalog.diamondIcon && badge.gameObject.activeSelf && !badge.raycastTarget,
                    "A locked color is missing its visible Diamond badge.");
            }
            swatches[1].button.onClick.Invoke();
            Require(selected == 1, "A locked color cannot open its purchase flow.");
            row.Bind(0, definition.availableColors, definition.availableColors[1], (_, color) => selected = color, _ => true);
            Require(swatches[1].selectedVisual.activeSelf &&
                !swatches[1].button.transform.Find("Locked color diamond").gameObject.activeSelf,
                "An owned color remains marked as locked.");
            row.Bind(0, definition.availableColors, Color.clear, (_, color) => selected = color, color => color.a <= .001f);

            foreach (Vector2 screen in new[] { new Vector2(1920, 1080), new Vector2(1600, 1000), new Vector2(1440, 1080) })
            {
                float width = 1080f * screen.x / screen.y;
                ((RectTransform)root.transform).sizeDelta = new Vector2(width, 1080f);
                for (int i = 0; i < cards.Count; i++)
                {
                    RectTransform rect = (RectTransform)cards[i].transform;
                    rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
                    rect.sizeDelta = new Vector2(220f, 425f);
                    rect.anchoredPosition = new Vector2((i - 2) * 238f, 130f);
                    foreach (TMP_Text text in cards[i].GetComponentsInChildren<TMP_Text>(false))
                    {
                        text.ForceMeshUpdate();
                        Require(!text.isTextOverflowing, "A shop card label overflows at " + screen);
                    }
                }
                RectTransform rowRect = (RectTransform)row.transform;
                rowRect.anchorMin = rowRect.anchorMax = rowRect.pivot = Vector2.one * .5f;
                rowRect.anchoredPosition = new Vector2(0f, -235f);
                rowRect.sizeDelta = new Vector2(900f, 140f);
                Canvas.ForceUpdateCanvases();
                Capture(camera, (int)screen.x, (int)screen.y, Output + "/Shop-" + (int)screen.x + ".png");
            }

            foreach (ShopItemUI card in cards) card.gameObject.SetActive(false);
            row.gameObject.SetActive(false);
            var dialog = Object.Instantiate(dialogPrefab, root.transform, false);
            var dialogCanvas = dialog.GetComponent<Canvas>();
            if (dialogCanvas != null)
            {
                dialogCanvas.renderMode = RenderMode.WorldSpace;
                dialogCanvas.worldCamera = camera;
                dialogCanvas.overrideSorting = false;
            }
            var scaler = dialog.GetComponent<CanvasScaler>();
            if (scaler != null) scaler.enabled = false;
            RectTransform dialogRect = (RectTransform)dialog.transform;
            dialogRect.anchorMin = Vector2.zero;
            dialogRect.anchorMax = Vector2.one;
            dialogRect.sizeDelta = dialogRect.anchoredPosition = Vector2.zero;
            dialogRect.localScale = Vector3.one;
            bool confirmed = false;
            Require(dialog.Show("Unlock this color?", "Spend 5 Diamonds to use this color on any part of Engineering Hard Hat. You have 1,000 Diamonds.",
                "CANCEL", "BUY FOR 5", dialog.Hide, () => confirmed = true), "The color purchase confirmation could not open.");
            Get<Button>(dialog, "rightButton").onClick.Invoke();
            Require(confirmed, "The confirmation button is not connected.");
            Capture(camera, 1440, 1080, Output + "/Color-Confirmation.png");
            foreach (TMP_Text text in dialog.GetComponentsInChildren<TMP_Text>(false))
            {
                text.ForceMeshUpdate();
                Require(!text.isTextOverflowing, "The color purchase dialog clips " + text.name);
            }
            File.WriteAllText(Output + "/Validation.txt",
                "PASS: five authored Diamond products, locked and owned swatches, default colors, preview callbacks, purchase confirmation and phone/tablet text bounds.");
            Debug.Log("[Diamond Shop] PASS");
        }
        finally
        {
            iconCache.SetValue(null, originalIconCache);
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    private static void ValidateColors()
    {
        var look = new CosmeticLoadoutData { shirtColor = Color.red };
        Color paid = new Color32(48, 73, 107, 255);
        string owned = CosmeticDefinition.ColorPurchaseId("Shirt_Tee1", paid);
        look.SetMaterialColor("Shirt_Tee1", "first", paid);
        look.SetMaterialColor("Shirt_Tee1", "second", Color.red);
        look.SetMaterialColor("Shirt_Polo", "first", paid);
        look.KeepPurchasedColors(id => id == owned);
        Require(look.materialColors.Count == 1 && look.shirtColor == Color.clear &&
            look.GetMaterialColor("Shirt_Tee1", "first", CosmeticCategory.Shirt) == paid,
            "Default-only and item-specific color ownership were not enforced.");
    }

    private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Private).GetValue(target);
    private static void Require(bool passed, string reason)
    {
        if (!passed) throw new InvalidOperationException(reason);
    }

    private static void Capture(Camera camera, int width, int height, string path)
    {
        RenderTexture previous = RenderTexture.active;
        var target = new RenderTexture(width, height, 24);
        target.Create();
        var image = new Texture2D(width, height, TextureFormat.RGBA32, false);
        try
        {
            camera.targetTexture = target;
            Canvas.ForceUpdateCanvases();
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = null;
            RenderTexture.active = previous;
            target.Release();
            Object.DestroyImmediate(image);
            Object.DestroyImmediate(target);
        }
    }
}
#endif
