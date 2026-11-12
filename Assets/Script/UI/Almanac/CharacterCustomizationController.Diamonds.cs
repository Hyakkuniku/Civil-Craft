using UnityEngine;
using UnityEngine.SceneManagement;

public sealed partial class CharacterCustomizationController
{
    private AuthoredSaveChoiceDialog colorPurchaseDialog;
    private int colorEditorGeneration;

    private bool OwnsSelectedColor(Color color) => color.a <= .001f ||
        selectedDefinition != null && PlayerDataManager.Instance != null &&
        PlayerDataManager.Instance.OwnsCosmeticColor(CosmeticDefinition.ColorPurchaseId(selectedDefinition.PermanentID, color));

    private void OfferColorPurchase(CosmeticDefinition definition, string slot, Color color)
    {
        if (colorPurchaseDialog != null && colorPurchaseDialog.IsVisible) return;
        GameWalletService wallet = GameWalletService.Instance;
        const int price = CosmeticDefinition.DiamondColorPrice;
        if (wallet == null)
        {
            SetColorPurchaseHint("Sign in to spend Diamonds.");
            return;
        }
        if (!wallet.CanBuyWithDiamonds(price, out string reason))
        {
            SetColorPurchaseHint(reason);
            return;
        }
        if (colorPurchaseDialog == null)
        {
            var template = Resources.Load<AuthoredSaveChoiceDialog>("UI/ColorPurchaseDialog");
            if (template == null)
            {
                SetColorPurchaseHint("The color purchase panel is unavailable.");
                return;
            }
            colorPurchaseDialog = Instantiate(template);
            Scene scene = gameObject.scene;
            if (scene.IsValid() && scene.isLoaded) SceneManager.MoveGameObjectToScene(colorPurchaseDialog.gameObject, scene);
        }

        int expectedGeneration = colorEditorGeneration;
        string colorId = CosmeticDefinition.ColorPurchaseId(definition.PermanentID, color);
        colorPurchaseDialog.Show("Unlock this color?",
            $"Spend {price} Diamonds to use this color on any part of {definition.displayName}. You have {wallet.DiamondBalance:N0} Diamonds.",
            "CANCEL", $"BUY FOR {price}", colorPurchaseDialog.Hide,
            () =>
            {
                colorPurchaseDialog.Hide();
                if (!CanShowColorPurchaseResult(definition, expectedGeneration)) return;
                SetColorPurchaseHint("Confirming purchase…");
                wallet.PurchaseWithDiamonds("cosmetic_color", colorId, price, (success, error) =>
                {
                    if (!CanShowColorPurchaseResult(definition, expectedGeneration)) return;
                    if (success)
                    {
                        ApplyMaterialColor(definition.PermanentID, slot, color);
                        SetColorPurchaseHint("Color unlocked. Choose Save Look to wear it.");
                    }
                    else SetColorPurchaseHint(error);
                });
            });
    }

    private bool CanShowColorPurchaseResult(CosmeticDefinition definition, int expectedGeneration) =>
        this != null && expectedGeneration == colorEditorGeneration && selectedDefinition == definition &&
        previewLoadout != null && selectedItemPanel != null && selectedItemPanel.activeInHierarchy;

    private void SetColorPurchaseHint(string message)
    {
        if (materialHint != null) materialHint.text = message;
    }

    private void CloseColorPurchase()
    {
        colorEditorGeneration++;
        if (colorPurchaseDialog != null) colorPurchaseDialog.Hide();
    }
}
