using System;
using System.Collections.Generic;

[Serializable]
public sealed class GameWalletRewardEvidence
{
    public double finalCost;
    public int failureCount;
    public int quotedAmount;
    public bool tutorial;
}

[Serializable]
public sealed class GameWalletRewardEvent
{
    public string kind;
    public string sourceId;
    public string eventId;
    public GameWalletRewardEvidence evidence;
    // This is a local recovery record, never a server grant instruction.
    public bool held;
    public string holdReason;
    public int legacyQuotedAmount;
    public int legacyExpAmount;
}

[Serializable]
public sealed class GameWalletContractEvidence
{
    public string contractId;
    public GameWalletRewardEvidence evidence;
    // Explicit presence separates an actual zero EXP payout from older saves
    // written before the persisted payout included EXP.
    public bool hasExpQuote;
    public int quotedExp;
}

public struct GameWalletPayoutQuote
{
    public int gold;
    public int exp;
    public bool fromCapture;
    public string source;
}

[Serializable]
public sealed class GameWalletPendingPurchase
{
    public string operationId;
    public string targetKind;
    public string targetId;
    public string contractId;
    public bool completed;
    public string status;
}

[Serializable]
public sealed class GameWalletJournal
{
    public int schemaVersion = 1;
    public string titleId;
    public string accountId;
    public List<GameWalletRewardEvent> rewards = new List<GameWalletRewardEvent>();
    public List<string> deliveredRewardIds = new List<string>();
    public List<GameWalletPendingPurchase> purchases = new List<GameWalletPendingPurchase>();
}

[Serializable]
public sealed class GameWalletShopEntitlement
{
    public string itemId;
    public string cosmeticId;
}

[Serializable]
public sealed class GameWalletMaterialEntitlement
{
    public string contractId;
    public string materialId;
    public string saveKey;
}

/// <summary>Pure account, URL, count and replay rules. No wallet authority is stored here.</summary>
public static class GameWalletPolicy
{
    public const string WebsiteOrigin = "https://civil-craft.vercel.app";
    public const long MaximumJsonInteger = 9007199254740991L;

    public static bool IsAccountId(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 32) return false;
        foreach (char c in value)
            if (!(c >= '0' && c <= '9' || c >= 'a' && c <= 'f' || c >= 'A' && c <= 'F')) return false;
        return true;
    }

    public static bool IsTitleId(string value) => IsAccountId(value) && value.Length <= 16;

    public static bool UsesServerAuthority(bool accountActive, bool guest) => accountActive && !guest;
    public static bool RequiresSignInForResponse(long responseCode) => responseCode == 401;

    public static bool IsSourceId(string value) => !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 128 && value == value.Trim() && value.IndexOfAny(new[] { '<', '>', '\r', '\n', '\0' }) < 0;

    public static bool IsReward(GameWalletRewardEvent reward)
    {
        if (reward == null || !IsSourceId(reward.sourceId) ||
            reward.kind != "contract" && reward.kind != "achievement" ||
            reward.eventId != reward.kind + ":" + reward.sourceId) return false;
        if (reward.held)
            return reward.kind == "contract" && reward.evidence == null &&
                reward.holdReason == "legacy-evidence-missing" &&
                reward.legacyQuotedAmount >= 0 && reward.legacyExpAmount >= 0;
        if (!string.IsNullOrEmpty(reward.holdReason)) return false;
        if (reward.kind == "achievement") return reward.evidence == null;
        GameWalletRewardEvidence e = reward.evidence;
        return e != null && !double.IsNaN(e.finalCost) && !double.IsInfinity(e.finalCost) &&
            e.finalCost >= 0 && e.failureCount >= 0 && e.quotedAmount >= 0;
    }

    public static bool IsSendableReward(GameWalletRewardEvent reward) => IsReward(reward) && !reward.held;

    public static bool HasCapturedPayout(GameWalletContractEvidence captured)
    {
        if (captured == null || !IsSourceId(captured.contractId)) return false;
        return IsReward(new GameWalletRewardEvent {
            kind = "contract", sourceId = captured.contractId, eventId = "contract:" + captured.contractId,
            evidence = captured.evidence
        }) && (!captured.hasExpQuote || captured.quotedExp >= 0);
    }

    /// <summary>Presence, never positivity, decides which saved payout wins.</summary>
    public static GameWalletPayoutQuote ResolvePayout(GameWalletContractEvidence captured,
        bool hasPending, int pendingGold, int pendingExp, bool hasMemory, int memoryGold, int memoryExp,
        int baseGold, int baseExp, bool tutorial = false)
    {
        if (tutorial) return new GameWalletPayoutQuote { gold = 0, exp = 0, source = "tutorial" };
        if (HasCapturedPayout(captured))
            return new GameWalletPayoutQuote {
                gold = captured.evidence.quotedAmount,
                exp = captured.hasExpQuote ? captured.quotedExp : Math.Max(0, hasPending ? pendingExp : hasMemory ? memoryExp : baseExp),
                fromCapture = true, source = "capture"
            };
        if (hasPending) return new GameWalletPayoutQuote { gold = Math.Max(0, pendingGold), exp = Math.Max(0, pendingExp), source = "pending" };
        if (hasMemory) return new GameWalletPayoutQuote { gold = Math.Max(0, memoryGold), exp = Math.Max(0, memoryExp), source = "memory" };
        return new GameWalletPayoutQuote { gold = Math.Max(0, baseGold), exp = Math.Max(0, baseExp), source = "base" };
    }

    public static GameWalletRewardEvent ContractReward(string canonicalId, GameWalletContractEvidence captured,
        GameWalletPayoutQuote quote, double savedBridgeCost, bool tutorial)
    {
        if (!IsSourceId(canonicalId)) return null;
        var reward = new GameWalletRewardEvent {
            kind = "contract", sourceId = canonicalId, eventId = "contract:" + canonicalId
        };
        bool hasCapture = HasCapturedPayout(captured) && captured.contractId == canonicalId;
        if (hasCapture || tutorial)
            reward.evidence = new GameWalletRewardEvidence {
                finalCost = hasCapture ? captured.evidence.finalCost : savedBridgeCost,
                failureCount = hasCapture ? captured.evidence.failureCount : 0,
                // Explicit tutorial payouts are always zero. Otherwise the
                // quote comes from the same captured attempt as its evidence.
                quotedAmount = tutorial ? 0 : captured.evidence.quotedAmount,
                tutorial = tutorial
            };
        else
        {
            reward.held = true;
            reward.holdReason = "legacy-evidence-missing";
            reward.legacyQuotedAmount = Math.Max(0, quote.gold);
            reward.legacyExpAmount = Math.Max(0, quote.exp);
        }
        return IsReward(reward) ? reward : null;
    }

    public static GameWalletRewardEvent RepairCapturedRewardQuote(GameWalletRewardEvent reward,
        GameWalletContractEvidence captured)
    {
        if (!IsSendableReward(reward) || reward.kind != "contract" || reward.evidence.tutorial ||
            !HasCapturedPayout(captured) || captured.contractId != reward.sourceId ||
            captured.evidence.finalCost != reward.evidence.finalCost ||
            captured.evidence.failureCount != reward.evidence.failureCount ||
            captured.evidence.tutorial != reward.evidence.tutorial ||
            captured.evidence.quotedAmount == reward.evidence.quotedAmount) return reward;
        return new GameWalletRewardEvent {
            kind = reward.kind, sourceId = reward.sourceId, eventId = reward.eventId,
            held = reward.held, holdReason = reward.holdReason,
            legacyQuotedAmount = reward.legacyQuotedAmount, legacyExpAmount = reward.legacyExpAmount,
            evidence = new GameWalletRewardEvidence {
                finalCost = reward.evidence.finalCost,
                failureCount = reward.evidence.failureCount,
                tutorial = reward.evidence.tutorial,
                quotedAmount = captured.evidence.quotedAmount
            }
        };
    }

    public static bool IsPurchase(GameWalletPendingPurchase purchase) => purchase != null &&
        Guid.TryParseExact(purchase.operationId, "D", out _) && IsSourceId(purchase.targetId) &&
        (purchase.targetKind == "cosmetic" || purchase.targetKind == "material" && IsSourceId(purchase.contractId));

    public static bool IsAllowedShopUrl(string rawUrl, string rawExpiry, DateTime nowUtc)
    {
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out Uri url) ||
            url.Scheme != Uri.UriSchemeHttps || url.Host != "civil-craft.vercel.app" ||
            !url.IsDefaultPort || !string.IsNullOrEmpty(url.UserInfo) || !string.IsNullOrEmpty(url.Fragment) ||
            url.AbsolutePath != "/login" && url.AbsolutePath != "/shop" && url.AbsolutePath != "/dashboard/shop") return false;
        return DateTime.TryParse(rawExpiry, System.Globalization.CultureInfo.InvariantCulture,
                   System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                   out DateTime expiry) && expiry > nowUtc && expiry <= nowUtc.AddMinutes(60).AddSeconds(5);
    }

    public static bool SameAccount(string first, string second) => IsAccountId(first) &&
        IsAccountId(second) && string.Equals(first, second, StringComparison.OrdinalIgnoreCase);

    public static bool CanUseAccountData(string selectedAccount, string dataAccount, string sdkAccount,
        string expectedAccount, int sessionGeneration, int expectedGeneration, bool approved,
        bool loggedIn, bool guest, bool visitingHost) => approved && loggedIn && !guest && !visitingHost &&
        sessionGeneration == expectedGeneration && SameAccount(selectedAccount, expectedAccount) &&
        SameAccount(selectedAccount, dataAccount) && SameAccount(selectedAccount, sdkAccount);

    public static bool CanMirrorVersion(long knownVersion, long nextVersion) => knownVersion >= 0 &&
        nextVersion >= knownVersion && nextVersion <= MaximumJsonInteger;

    public static bool CanImportLegacy(int savedAuthorityVersion, bool serverReady) =>
        savedAuthorityVersion == 0 && !serverReady;

    public static bool IsTerminalPurchaseRejection(long responseCode, string expectedId, string returnedId, bool terminal) =>
        responseCode == 400 && terminal && Guid.TryParseExact(expectedId, "D", out _) && expectedId == returnedId;

    public static bool TryCount(object raw, long maximum, out long value)
    {
        value = 0;
        if (maximum < 0) return false;
        // PlayFab's production SimpleJson parser uses ulong for every
        // nonnegative integer token. Bound it before converting to long so
        // an overflowing response cannot wrap into accepted currency.
        if (raw is ulong unsigned)
        {
            if (unsigned > (ulong)maximum || unsigned > (ulong)MaximumJsonInteger) return false;
            value = (long)unsigned;
        }
        else if (raw is uint unsignedSmall) value = unsignedSmall;
        else if (raw is long integer) value = integer;
        else if (raw is int small) value = small;
        else if (raw is double number)
        {
            if (double.IsNaN(number) || double.IsInfinity(number) || number < 0 ||
                number > maximum || number > MaximumJsonInteger || Math.Truncate(number) != number) return false;
            value = (long)number;
        }
        else if (raw is float single)
        {
            if (float.IsNaN(single) || float.IsInfinity(single) || single < 0 ||
                single > maximum || single > MaximumJsonInteger || Math.Truncate(single) != single) return false;
            value = (long)single;
        }
        else if (raw is decimal precise)
        {
            if (precise < 0 || precise > maximum || precise > MaximumJsonInteger || decimal.Truncate(precise) != precise) return false;
            value = (long)precise;
        }
        else return false;
        return value >= 0 && value <= maximum && value <= MaximumJsonInteger;
    }
}
