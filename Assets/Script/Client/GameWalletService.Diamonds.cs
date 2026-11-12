using System;
using System.Collections;
using System.Collections.Generic;

public sealed partial class GameWalletService
{
    private const string DiamondShopPath = "/api/game/diamond-shop";
    private bool diamondShopReady;
    private long diamondShopBalance;

    public bool DiamondShopAvailable => diamondShopReady && !RequiresSignIn && IsCurrentSession(accountId, generation);
    public bool HasPendingDiamondPurchase => journal != null && journal.purchases.Exists(
        purchase => GameWalletPolicy.IsDiamondPurchase(purchase) && !purchase.completed);

    public bool CanBuyWithDiamonds(int price, out string reason)
    {
        if (!HasSignedInAccount || RequiresSignIn)
            reason = "Sign in to spend Diamonds.";
        else if (HasPendingDiamondPurchase)
            reason = "A Diamond purchase is awaiting confirmation.";
        else if (inFlight)
            reason = "Checking your Diamonds. Please wait.";
        else if (!DiamondShopAvailable || !journalAvailable || journal == null)
            reason = "Diamond purchases are unavailable. Reconnect and try again.";
        else if (price <= 0)
            reason = "This Diamond price is invalid.";
        else if (diamondShopBalance < price)
            reason = $"You have {diamondShopBalance:N0} Diamonds. This costs {price:N0}.";
        else
        {
            reason = null;
            return true;
        }
        return false;
    }

    public bool PurchaseWithDiamonds(string kind, string id, int price, Action<bool, string> callback)
    {
        if (!CanBuyWithDiamonds(price, out string reason))
        {
            callback?.Invoke(false, reason);
            return false;
        }
        var purchase = new GameWalletPendingPurchase
        {
            operationId = Guid.NewGuid().ToString("D"),
            targetKind = kind,
            targetId = id,
            quotedPrice = price
        };
        if (!GameWalletPolicy.IsDiamondPurchase(purchase)) return false;
        return PersistPurchase(purchase, callback);
    }

    private IEnumerator SynchronizeDiamondShop(string expectedAccount, int expectedGeneration)
    {
        diamondShopReady = false;
        GameWalletPendingPurchase pending = journal.purchases.Find(
            purchase => GameWalletPolicy.IsDiamondPurchase(purchase) && !purchase.completed);
        string status = null;
        if (pending != null)
        {
            var reply = new Reply();
            var payload = new Dictionary<string, object>
            {
                { "operationId", pending.operationId }, { "targetKind", pending.targetKind },
                { "targetId", pending.targetId }, { "quotedPrice", pending.quotedPrice }
            };
            // Resend the same operation. The server returns its permanent
            // receipt even if the original response or local save was lost.
            yield return Request("POST", DiamondShopPath, payload, expectedAccount, expectedGeneration, reply);
            if (!IsCurrentSession(expectedAccount, expectedGeneration) || RequiresSignIn) yield break;
            status = Text(reply.body, "status");
            if (reply.code != 200 || Text(reply.body, "operationId") != pending.operationId ||
                Text(reply.body, "targetKind") != pending.targetKind || Text(reply.body, "targetId") != pending.targetId ||
                (status != "completed" && status != "already_owned" && status != "insufficient_funds" && status != "unavailable"))
                yield break;
        }

        bool success = status == "completed" || status == "already_owned";
        var state = new Reply();
        yield return Request("GET", DiamondShopPath, null, expectedAccount, expectedGeneration, state);
        if (!IsCurrentSession(expectedAccount, expectedGeneration) || RequiresSignIn || state.code != 200 ||
            !ApplyDiamondShopState(state.body, success ? pending : null)) yield break;
        if (pending == null) yield break;

        pending.completed = true;
        pending.status = status;
        if (!SaveJournal())
        {
            pending.completed = false;
            yield break;
        }
        CompletePurchaseCallback(pending.operationId, success, success ? null :
            status == "insufficient_funds" ? "You do not have enough Diamonds for this purchase." :
            "This price or item has changed. No Diamonds were spent.");
    }

    private bool ApplyDiamondShopState(IDictionary<string, object> body, GameWalletPendingPurchase required)
    {
        if (!Integer(body, "diamonds", GameWalletPolicy.MaximumJsonInteger, out long balance) ||
            !body.TryGetValue("shopItems", out object shopRaw) || !(shopRaw is IList shops) || shops.Count > 1000 ||
            !body.TryGetValue("colors", out object colorRaw) || !(colorRaw is IList colors) || colors.Count > 5000) return false;
        var shopItems = new List<GameWalletShopEntitlement>(shops.Count);
        var shopIds = new HashSet<string>(StringComparer.Ordinal);
        var colorIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (object raw in shops)
        {
            var row = raw as IDictionary<string, object>;
            string item = Text(row, "itemId"), cosmetic = Text(row, "cosmeticId");
            if (!GameWalletPolicy.IsSourceId(item) || !GameWalletPolicy.IsSourceId(cosmetic) || !shopIds.Add(item)) return false;
            shopItems.Add(new GameWalletShopEntitlement { itemId = item, cosmeticId = cosmetic });
        }
        foreach (object raw in colors)
            if (!(raw is string id) || !GameWalletPolicy.IsCosmeticColorId(id) || !colorIds.Add(id)) return false;
        if (required != null)
        {
            if (required.targetKind == "cosmetic_color" && !colorIds.Contains(required.targetId)) return false;
            if (required.targetKind == "diamond_cosmetic" && !shopItems.Exists(entry => entry.itemId == required.targetId &&
                entry.cosmeticId == playerData.WalletCosmeticIdForShopItem(required.targetId))) return false;
        }
        if (!playerData.TryMergeDiamondShopOwnership(shopItems, new List<string>(colorIds)) ||
            required != null && !playerData.HasWalletPurchaseEntitlement(required)) return false;
        diamondShopBalance = balance;
        diamondShopReady = DiamondsAvailable = true;
        DiamondBalance = balance;
        return true;
    }
}
