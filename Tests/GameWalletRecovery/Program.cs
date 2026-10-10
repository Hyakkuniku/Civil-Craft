using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.Networking;

internal static class Program
{
    private const string OperationId = "11111111-2222-4333-8444-555555555555";
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int passed;

    private static int Main()
    {
        try {
            Run("the production PlayFab JSON parser accepts bounded unsigned balances", ProductionJsonNumericBalances);
            Run("the production parser mirrors full wallet balances, versions and lifetime counters", ProductionJsonWalletAndCounters);
            foreach (string invalid in new[] { "coins-overflow", "diamonds-overflow", "version-overflow", "earned-overflow", "spent-overflow", "unsigned-overflow", "negative", "fraction", "string" }) {
                string kind = invalid;
                Run($"runtime-parser {kind} whole-wallet data cannot update balances or ownership", () => ProductionJsonInvalidWallet(kind));
            }
            foreach (string invalid in new[] { "overflow", "unsigned-overflow", "negative", "fraction", "string" }) {
                string kind = invalid;
                Run($"runtime-parser {kind} entitlement version cannot complete a pending purchase", () => ProductionJsonInvalidEntitlementVersion(kind));
            }
            Run("paid ownership recovers before a rejected reward without losing the reward", PaidPurchaseBeforeRejectedReward);
            Run("held rewards are persisted but never sent or scheduled repeatedly", HeldRewardsDoNotSendOrSpin);
            foreach (string invalid in new[] { "empty", "stale", "wrong-target", "wrong-cosmetic", "missing-local-unlock" }) {
                string scenario = invalid;
                Run($"{scenario} entitlements retain the original pending operation", () => InvalidRecoveryEntitlements(scenario));
            }
            Run("valid cosmetic ownership is saved before its operation completes", ValidCosmeticRecovery);
            Run("canonical material ownership is saved before its operation completes", ValidMaterialRecovery);
            Run("failed local ownership save retains the stable operation ID", LocalOwnershipSaveFailure);
            Run("failed journal completion write recovers with the same operation ID", JournalSaveFailure);
            Run("not-found operation resubmits only its original UUID", NotFoundResubmitsStableId);
            Run("session generation changes discard a response before cache or ownership writes", SessionChangeAborts);
            Run("wrong operation status cannot complete a different pending operation", WrongOperationCannotComplete);
            Run("persisted old reward quote repairs only from matching captured evidence", MatchingCapturedQuoteRepairs);
            Run("mismatched captured evidence leaves the original reward queued for review", MismatchedCaptureDoesNotInventEvidence);
            Run("quote repair must persist before its reward can be sent", QuoteRepairWriteFailureBlocksHttp);
            Run("a null legacy outbox cannot skip repairing an existing journal quote", () => MatchingCapturedQuoteRepairs(true));
            Run("network loss after paid recovery keeps ownership but disables cached spending", () => OfflineRewardAfterPaidRecovery(0));
            Run("reward service outage after paid recovery preserves ownership and queued earnings", () => OfflineRewardAfterPaidRecovery(503));
            Run("new mid-pump rewards must persist before any purchase or reward submission", MidPumpJournalFailureBlocksMonetaryHttp);
            Run("Coin reward timeout preserves paid ownership but invalidates both availability flags", () => OfflineRewardAfterPaidRecovery(408));
            foreach (long failure in new[] { 400L, 503L }) {
                long code = failure;
                Run($"Coin import HTTP {code} preserves the independently validated Diamond balance", () => ImportFailurePreservesDiamonds(code));
            }
            Run("unresolved legacy save source permits wallet GET but never imports", UnresolvedLegacySourceBlocksOnlyImport);
            Run("already-ready server wallet does not require a legacy import source", AlreadyReadyWalletWithoutLegacySource);
            Run("legacy source approval revoked during wallet request blocks import", SourceRevokedDuringWalletRequest);
            Run("an existing cached Coin authority never reimports but retains healthy Diamonds", CachedAuthorityNeverReimports);
            Run("Coin cache save failure preserves independently verified Diamonds", CacheFailurePreservesDiamonds);
            foreach (string failure in new[] { "401", "malformed", "network" }) {
                string kind = failure;
                Run($"whole-wallet {kind} failure clears both currency availability flags", () => WholeWalletFailureClearsCurrencies(kind));
            }
            Run("session generation change invalidates both currencies after a prior successful sync", GenerationChangeClearsCurrencies);
            Run("account switching invalidates both currencies before any new request", AccountSwitchClearsCurrencies);
            foreach (string outcome in new[] { "success", "400", "503", "network", "401", "switch" }) {
                string scenario = outcome;
                Run($"legacy import reservation releases after {scenario}", () => ImportReservationAlwaysReleases(scenario));
            }
            Run("a rejected save-selection reservation blocks import while retaining Diamonds", ImportReservationRejected);
            Run("malformed import wallet response invalidates both currencies", MalformedImportInvalidatesCurrencies);
            Run("source approval is rechecked at nested import dispatch and its reservation releases", SourceRevokedBeforeImportDispatch);
            Run("legacy import snapshots own their selected ownership and progression arrays", ImportSnapshotDoesNotBorrowMutableLists);
            foreach (string overlap in new[] { "contract", "contract-alias", "achievement" }) {
                string kind = overlap;
                Run($"retained journal {kind} overlapping the selected historical baseline blocks import", () => HistoricalJournalOverlapBlocksImport(kind));
            }
            Run("selected-source-backed unpaid alias completion is excluded from opening tombstones", SourceBackedAliasRemainsUnpaid);
            Run("non-overlapping retained journal reward survives and grants after a valid opening", NonOverlappingJournalRewardSurvivesImport);
            foreach (string invalidBody in new[] { "html", "numeric-error", "empty-error" }) {
                string kind = invalidBody;
                Run($"unverified scoped HTTP503 {kind} clears both currencies", () => UnverifiedCoinErrorInvalidatesBoth(kind));
            }
            Run("SDK title changes during a wallet request invalidate both currencies", TitleChangedDuringAwait);
            Run("SDK title casing changes preserve the same account scope", EquivalentTitleCasingIsAllowed);
            Run("approved legacy source readiness wakes a held opening without manual refresh", SourceReadinessAutomaticallyWakesImport);
            Run("startup readiness recovery wakes a held opening without repeated HTTP spin", StartupReadinessAutomaticallyWakesImport);
            foreach (string selected in new[] { "coins", "diamonds" }) {
                string currency = selected;
                Run($"{currency} opens only one verified main-site link with header-only authentication", () => VerifiedShopLinkOpensMainSite(currency));
                foreach (string state in new[] { "coded-disabled", "legacy-disabled", "not-ready" }) {
                    string availability = state;
                    Run($"{currency} {availability} preserves Coin operations and explains paused setup", () => KnownUnavailableShopAndWallet(currency, availability));
                }
                foreach (string invalid in new[] { "fork", "evil", "http", "expired" }) {
                    string kind = invalid;
                    Run($"{currency} {kind} link cannot open a browser", () => UnsafeShopLinkCannotOpen(currency, kind));
                }
                Run($"{currency} link recovers after a disabled response without a URL fallback", () => ShopLinkRecoversAfterSetup(currency));
            }
            foreach (string invalid in new[] { "unknown", "html", "wrong-code", "wrong-status", "numeric-code" }) {
                string kind = invalid;
                Run($"shop and wallet {kind} errors are not reflected or treated as setup evidence", () => UntrustedUnavailableResponse(kind));
            }
            Run("shop authentication failure takes precedence over maintenance wording", ShopAuthenticationFailureRequiresSignIn);
            foreach (string changed in new[] { "generation", "account", "title" }) {
                string scope = changed;
                Run($"shop {scope} change discards links before browser opening or callbacks", () => ShopScopeChangeAborts(scope));
            }
            Run("known disabled wallet can show independent Diamonds without applying legacy Coins", () => IndependentDiamondsForDisabledWallet("valid"));
            Run("independent verified zero Diamonds is a real balance, not a failure fallback", () => IndependentDiamondsForDisabledWallet("zero"));
            foreach (string invalid in new[] { "null", "string", "overflow", "negative", "missing", "network", "401", "503", "malformed" }) {
                string kind = invalid;
                Run($"independent Diamond {kind} failure leaves both unavailable and Coins untouched", () => IndependentDiamondsForDisabledWallet(kind));
            }
            foreach (string changed in new[] { "generation", "account", "title" }) {
                string scope = changed;
                Run($"independent Diamond {scope} change discards balance proof", () => IndependentDiamondScopeChangeAborts(scope));
            }
            Console.WriteLine($"GameWalletRecovery: {passed} actual-service scenarios passed.");
            return 0;
        } catch (Exception error) {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void Run(string title, Action test) { test(); passed++; Console.WriteLine("PASS " + title); }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    private static void ProductionJsonNumericBalances()
    {
        // Link the actual checked-in SDK parser: a signed-number test double
        // would miss its ulong representation of nonnegative JSON integers.
        var body = (IDictionary<string, object>)PlayFab.Json.PlayFabSimpleJson.DeserializeObject(
            "{\"zero\":0,\"positive\":25,\"coinMax\":2147483647,\"jsonMax\":9007199254740991}");
        foreach (string key in new[] { "zero", "positive", "coinMax", "jsonMax" })
            Check(body[key] is ulong, "The runtime SDK parses nonnegative integers as ulong");
        Check(GameWalletPolicy.TryCount(body["zero"], int.MaxValue, out long zero) && zero == 0,
            "Runtime-parser zero must be accepted as a real balance");
        Check(GameWalletPolicy.TryCount(body["positive"], GameWalletPolicy.MaximumJsonInteger, out long positive) && positive == 25,
            "Runtime-parser positive Diamonds must be accepted");
        Check(GameWalletPolicy.TryCount(body["coinMax"], int.MaxValue, out long coins) && coins == int.MaxValue,
            "Runtime-parser Coin maximum remains bounded to the save's int range");
        Check(GameWalletPolicy.TryCount(body["jsonMax"], GameWalletPolicy.MaximumJsonInteger, out long maximum) && maximum == GameWalletPolicy.MaximumJsonInteger,
            "Runtime-parser maximum exact JSON integer preserves all digits");
        var invalid = (IDictionary<string, object>)PlayFab.Json.PlayFabSimpleJson.DeserializeObject(
            "{\"coinOverflow\":2147483648,\"jsonOverflow\":9007199254740992,\"unsignedOverflow\":18446744073709551615," +
            "\"negative\":-1,\"fraction\":0.5,\"string\":\"25\",\"boolean\":true,\"null\":null}");
        Check(invalid["unsignedOverflow"] is ulong maximumUnsigned && maximumUnsigned == ulong.MaxValue,
            "Exercise the SDK's actual unsigned overflow representation");
        Check(!GameWalletPolicy.TryCount(invalid["coinOverflow"], int.MaxValue, out _), "Runtime-parser Coins cannot exceed the int cache range");
        foreach (string key in new[] { "jsonOverflow", "unsignedOverflow", "negative", "fraction", "string", "boolean", "null" })
            Check(!GameWalletPolicy.TryCount(invalid[key], GameWalletPolicy.MaximumJsonInteger, out _), "Malformed runtime-parser counts remain rejected: " + key);
    }

    private static void ProductionJsonWalletAndCounters()
    {
        using var fixture = new Fixture();
        var body = Wallet(int.MaxValue, GameWalletPolicy.MaximumJsonInteger);
        body["diamonds"] = GameWalletPolicy.MaximumJsonInteger;
        body["lifetimeGoldEarned"] = int.MaxValue;
        body["lifetimeGoldSpent"] = int.MaxValue;
        Reply("GET", "/api/game/wallet", body);
        Reply("GET", "/api/game/entitlements", Entitlements(GameWalletPolicy.MaximumJsonInteger));
        fixture.Sync();
        Check(fixture.Service.CoinsAvailable && fixture.Service.CoinBalance == int.MaxValue && fixture.Data.CurrentData.gold == int.MaxValue,
            "The actual runtime parser accepts bounded Coins and mirrors the verified cache");
        Check(fixture.Service.DiamondsAvailable && fixture.Service.DiamondBalance == GameWalletPolicy.MaximumJsonInteger &&
            fixture.Service.WalletVersion == GameWalletPolicy.MaximumJsonInteger && fixture.Data.CurrentData.walletCoinsVersion == GameWalletPolicy.MaximumJsonInteger,
            "Unsigned wallet and entitlement versions preserve their exact count");
        Check(fixture.Data.CurrentData.lifetimeGoldEarned == int.MaxValue && fixture.Data.CurrentData.lifetimeGoldSpent == int.MaxValue,
            "Actual-parser lifetime statistics are validated and mirrored, never replaced by zero");
        Check(UnityWebRequest.Requests.All(request => request.Method == "GET"), "Parser correction alone cannot request currency credits or spending");
    }

    private static void ProductionJsonInvalidWallet(string scenario)
    {
        using var fixture = new Fixture();
        var body = Wallet();
        switch (scenario) {
            case "coins-overflow": body["coins"] = (ulong)int.MaxValue + 1UL; break;
            case "diamonds-overflow": body["diamonds"] = (ulong)GameWalletPolicy.MaximumJsonInteger + 1UL; break;
            case "version-overflow": body["version"] = (ulong)GameWalletPolicy.MaximumJsonInteger + 1UL; break;
            case "earned-overflow": body["lifetimeGoldEarned"] = (ulong)int.MaxValue + 1UL; break;
            case "spent-overflow": body["lifetimeGoldSpent"] = (ulong)int.MaxValue + 1UL; break;
            case "unsigned-overflow": body["coins"] = ulong.MaxValue; break;
            case "negative": body["coins"] = -1L; break;
            case "fraction": body["coins"] = 0.5d; break;
            case "string": body["coins"] = "900"; break;
            default: throw new InvalidOperationException("Unexpected fixture");
        }
        Reply("GET", "/api/game/wallet", body);
        fixture.Sync();
        Check(!fixture.Service.CoinsAvailable && !fixture.Service.DiamondsAvailable && !fixture.Service.CanSpendOnline,
            "An invalid runtime numeric field cannot make a whole wallet available");
        Check(fixture.Data.CurrentData.gold == 123 && fixture.Data.MirrorCount == 0 && fixture.Data.MergeCount == 0 && UnityWebRequest.Requests.Count == 1,
            "Reject malformed unsigned data before any save, entitlement or monetary request");
    }

    private static void ProductionJsonInvalidEntitlementVersion(string scenario)
    {
        using var fixture = new Fixture();
        fixture.AddPending();
        var body = Entitlements(6, new object[] { Shop() });
        body["version"] = scenario switch {
            "overflow" => (object)((ulong)GameWalletPolicy.MaximumJsonInteger + 1UL),
            "unsigned-overflow" => ulong.MaxValue,
            "negative" => -1L,
            "fraction" => 6.5d,
            "string" => "6",
            _ => throw new InvalidOperationException("Unexpected fixture"),
        };
        Recovery(fixture, body);
        fixture.Sync();
        AssertPending(fixture);
        Check(fixture.Callbacks.Count == 0 && fixture.Data.CurrentData.purchasedShopItemIds.Count == 0,
            "An invalid runtime-parser version cannot create ownership or confirm the operation");
        AssertNoDebitHttp();
    }
    private static T Get<T>(GameWalletService service, string field) => (T)typeof(GameWalletService).GetField(field, PrivateInstance).GetValue(service);
    private static void Set(GameWalletService service, string field, object value) => typeof(GameWalletService).GetField(field, PrivateInstance).SetValue(service, value);
    private static object Invoke(GameWalletService service, string method, params object[] args) => typeof(GameWalletService).GetMethod(method, PrivateInstance).Invoke(service, args);

    // Unity yields nested IEnumerator and AsyncOperation objects. The transport
    // double is itself an IEnumerator, so the real Request coroutine executes
    // its post-yield checks exactly as it does in the game.
    private static void Drive(IEnumerator routine, Action<IEnumerator> beforeNested = null)
    {
        var stack = new Stack<IEnumerator>();
        stack.Push(routine);
        int steps = 0;
        while (stack.Count > 0) {
            if (++steps > 5000) throw new Exception("Coroutine did not terminate");
            IEnumerator top = stack.Peek();
            if (!top.MoveNext()) { (top as IDisposable)?.Dispose(); stack.Pop(); continue; }
            if (top.Current is IEnumerator nested) { beforeNested?.Invoke(nested); stack.Push(nested); }
        }
    }

    private static Dictionary<string, object> Wallet(int coins = 900, long version = 5) => new() {
        ["coins"] = coins, ["diamonds"] = 25, ["ready"] = true, ["version"] = version,
        ["lifetimeGoldEarned"] = 300, ["lifetimeGoldSpent"] = 100,
    };
    private static Dictionary<string, object> AbsentCoinWallet() => new() {
        ["coins"] = null, ["diamonds"] = 25, ["ready"] = false, ["version"] = 0,
        ["lifetimeGoldEarned"] = 0, ["lifetimeGoldSpent"] = 0,
    };
    private static Dictionary<string, object> Entitlements(long version = 5, object[] shops = null, object[] materials = null) => new() {
        ["version"] = version, ["shopItems"] = shops ?? Array.Empty<object>(), ["materials"] = materials ?? Array.Empty<object>(),
    };
    private static Dictionary<string, object> Shop(string id = "shop_hat", string cosmetic = "Hat") => new() { ["itemId"] = id, ["cosmeticId"] = cosmetic };
    private static Dictionary<string, object> Material(string contract = "ShopKeeper", string material = "Beam") => new() {
        ["contractId"] = contract, ["materialId"] = material, ["saveKey"] = contract + "_" + material,
    };
    private static Dictionary<string, object> Purchase(string id = OperationId, long version = 6) => new() {
        ["operationId"] = id, ["status"] = "completed", ["coins"] = 800, ["version"] = version,
    };
    private static void Reply(string method, string path, object body, long code = 200, Action after = null) =>
        UnityWebRequest.Script.Enqueue(new(method, path, code, body, after));

    private static void Startup(Fixture fixture)
    {
        Reply("GET", "/api/game/wallet", Wallet());
        Reply("GET", "/api/game/entitlements", Entitlements());
    }
    private static void Recovery(Fixture fixture, object entitlements, Action afterEntitlements = null)
    {
        Startup(fixture);
        Reply("GET", "/api/game/purchases/" + OperationId, Purchase());
        Reply("GET", "/api/game/wallet", Wallet(800, 6));
        Reply("GET", "/api/game/entitlements", entitlements, after: afterEntitlements);
    }
    private static void AssertNoDebitHttp() => Check(!UnityWebRequest.Requests.Any(request => request.Method == "POST" && request.Path == "/api/game/purchases"), "Recovery must not issue another debit");
    private static void AssertPending(Fixture fixture)
    {
        Check(!fixture.Pending.completed, "Purchase must remain pending");
        Check(fixture.Pending.operationId == OperationId, "Do not replace a pending operation UUID");
        Check(!fixture.Persisted().purchases.Single().completed, "Disk journal must preserve the pending purchase");
    }

    private static void PaidPurchaseBeforeRejectedReward()
    {
        using var fixture = new Fixture();
        fixture.AddPending();
        fixture.Data.CurrentData.walletRewardOutbox.Add(new GameWalletRewardEvent { kind = "achievement", sourceId = "ACH_001", eventId = "achievement:ACH_001" });
        Recovery(fixture, Entitlements(6, new object[] { Shop() }));
        Reply("POST", "/api/game/rewards", new { error = "Reward quote needs review" }, 400);
        fixture.Sync();
        Check(fixture.Pending.completed, "A rejected earned reward cannot strand paid ownership");
        Check(fixture.Data.CurrentData.purchasedShopItemIds.Contains("shop_hat"), "Recovered item missing");
        Check(fixture.Data.CurrentData.unlockedCosmeticIDs.Contains("Hat"), "Recovered cosmetic missing");
        Check(fixture.Service.CoinsAvailable && fixture.Data.CurrentData.gold == 800, "Preserve the verified wallet when a reward is rejected");
        Check(fixture.Journal.rewards.Count == 1 && fixture.Data.CurrentData.walletRewardOutbox.Count == 1, "Failed reward must stay queued");
        Check(fixture.Journal.deliveredRewardIds.Count == 0 && fixture.Data.AckCount == 0, "Rejected reward must not be acknowledged");
        Check(Get<float>(fixture.Service, "nextAttempt") >= Time.unscaledTime + 30f, "Rejected rewards need bounded retry delay");
        Check(UnityWebRequest.Requests[^1].Path == "/api/game/rewards", "Purchase recovery must run before reward submission");
        AssertNoDebitHttp();
    }

    private static void OfflineRewardAfterPaidRecovery(long code)
    {
        using var fixture = new Fixture();
        fixture.AddPending();
        fixture.Data.CurrentData.walletRewardOutbox.Add(new GameWalletRewardEvent { kind = "achievement", sourceId = "ACH_001", eventId = "achievement:ACH_001" });
        Recovery(fixture, Entitlements(6, new object[] { Shop() }));
        Reply("POST", "/api/game/rewards", new { error = "Unavailable" }, code);
        fixture.Sync();
        Check(fixture.Pending.completed && fixture.Persisted().purchases.Single().completed, "The paid purchase remains recovered despite a later outage");
        Check(fixture.Data.HasWalletPurchaseEntitlement(fixture.Pending), "Do not revoke paid ownership on a reward connection failure");
        Check(!fixture.Service.CoinsAvailable && !fixture.Service.CanSpendOnline, "Offline cached Coins cannot authorize spending");
        Check(fixture.Service.DiamondsAvailable == (code == 503), "A scoped Coin HTTP failure retains validated Diamonds; network loss invalidates both");
        Check(fixture.Service.CoinBalance == 800 && fixture.Data.CurrentData.gold == 800, "Keep the last verified balance, never replace it with zero");
        Check(fixture.Journal.rewards.Single().eventId == "achievement:ACH_001" && fixture.Data.CurrentData.walletRewardOutbox.Count == 1, "Keep unconfirmed earnings queued");
        Check(fixture.Data.AckCount == 0 && fixture.Journal.deliveredRewardIds.Count == 0, "Failed reward submission cannot acknowledge delivery");
        AssertNoDebitHttp();
    }

    private static void HeldRewardsDoNotSendOrSpin()
    {
        using var fixture = new Fixture();
        fixture.Data.CurrentData.walletRewardOutbox.Add(new GameWalletRewardEvent {
            kind = "contract", sourceId = "ShopKeeper", eventId = "contract:ShopKeeper", held = true,
            holdReason = "legacy-evidence-missing", legacyQuotedAmount = 500, legacyExpAmount = 20,
        });
        Startup(fixture);
        fixture.UpdateAndDrive();
        Check(fixture.Journal.rewards.Count == 1 && fixture.Journal.rewards[0].held, "Held reward must remain recoverable");
        Check(fixture.Persisted().rewards.Single().held, "Held evidence must persist to disk");
        Check(!UnityWebRequest.Requests.Any(request => request.Path == "/api/game/rewards"), "Never POST held rewards");
        int startupRequests = UnityWebRequest.Requests.Count;
        Time.unscaledTime += 1000f;
        for (int i = 0; i < 10; i++) Invoke(fixture.Service, "Update");
        Check(fixture.Service.StartedCoroutines.Count == 1, "Held-only outboxes must not schedule repeated HTTP synchronizations after startup");
        Check(UnityWebRequest.Requests.Count == startupRequests, "A held-only outbox cannot trigger additional HTTP");
        Check(fixture.Service.CoinsAvailable, "Held historical evidence must not disable an otherwise verified wallet");
    }

    private static void InvalidRecoveryEntitlements(string scenario)
    {
        using var fixture = new Fixture();
        fixture.AddPending();
        object body = scenario switch {
            "empty" => Entitlements(6),
            "stale" => Entitlements(5, new object[] { Shop() }),
            "wrong-target" => Entitlements(6, new object[] { Shop("shop_boots", "Boots") }),
            "wrong-cosmetic" => Entitlements(6, new object[] { Shop("shop_hat", "Boots") }),
            _ => Entitlements(6, new object[] { Shop() }),
        };
        Recovery(fixture, body, scenario == "missing-local-unlock" ? () => fixture.Data.SkipOwnershipMerge = true : null);
        fixture.Sync();
        AssertPending(fixture);
        Check(!fixture.Service.CanSpendOnline, "Unconfirmed ownership must disable new spending");
        Check(fixture.Service.DiamondsAvailable && fixture.Service.DiamondBalance == 25, "Coin ownership validation must not erase an independent Diamond balance");
        AssertNoDebitHttp();
    }

    private static void ValidCosmeticRecovery()
    {
        using var fixture = new Fixture();
        fixture.AddPending();
        Recovery(fixture, Entitlements(6, new object[] { Shop() }));
        fixture.Sync();
        Check(fixture.Pending.completed && fixture.Persisted().purchases.Single().completed, "Completion must be durably journaled");
        Check(fixture.Data.HasWalletPurchaseEntitlement(fixture.Pending), "Completion requires local ownership");
        Check(fixture.Data.CurrentData.gold == 800 && fixture.Service.CanSpendOnline, "Verified current wallet should be spendable");
        Check(fixture.Callbacks.Count == 1 && fixture.Callbacks[0], "Success callback follows durable ownership");
        Check(UnityWebRequest.Requests.All(request => request.Headers.TryGetValue("Authorization", out string header) && header == "Bearer fixture-session-ticket"), "Authenticate only using the request header");
        Check(UnityWebRequest.Requests.All(request => !request.Path.Contains("fixture-session-ticket") && request.RedirectLimit == 0), "Never disclose a session ticket in a URL or follow redirects");
        AssertNoDebitHttp();
    }

    private static void ValidMaterialRecovery()
    {
        using var fixture = new Fixture();
        fixture.AddPending("material", "BeamAlias", "ShopKeeperContract");
        Recovery(fixture, Entitlements(6, materials: new object[] { Material() }));
        fixture.Sync();
        Check(fixture.Pending.completed && fixture.Data.CurrentData.unlockedContractMaterials.Contains("ShopKeeper_Beam"), "Recover canonical material ownership");
        Check(fixture.Persisted().purchases.Single().completed, "Material completion must persist");
        AssertNoDebitHttp();
    }

    private static void LocalOwnershipSaveFailure()
    {
        using var fixture = new Fixture();
        fixture.AddPending();
        Recovery(fixture, Entitlements(6, new object[] { Shop() }), () => fixture.Data.FailMerge = true);
        fixture.Sync();
        AssertPending(fixture);
        Check(fixture.Callbacks.Count == 0, "Do not report purchase completion after local save failure");
        Check(fixture.Service.DiamondsAvailable && fixture.Service.DiamondBalance == 25, "A Coin ownership save failure must not invalidate verified Diamonds");
        Check(fixture.Data.CurrentData.purchasedShopItemIds.Count == 0, "Failed ownership save must not unlock the item");
        AssertNoDebitHttp();
    }

    private static void JournalSaveFailure()
    {
        using var fixture = new Fixture();
        fixture.AddPending();
        Recovery(fixture, Entitlements(6, new object[] { Shop() }), () => SaveEncryption.FailNextEncrypt = true);
        fixture.Sync();
        AssertPending(fixture);
        Check(fixture.Callbacks.Count == 0, "Completion cannot precede a successful journal save");
        fixture.Data.CurrentData.walletCoinsVersion = 6;
        Reply("GET", "/api/game/wallet", Wallet(800, 6));
        Reply("GET", "/api/game/entitlements", Entitlements(6, new object[] { Shop() }));
        Reply("GET", "/api/game/purchases/" + OperationId, Purchase());
        Reply("GET", "/api/game/wallet", Wallet(800, 6));
        Reply("GET", "/api/game/entitlements", Entitlements(6, new object[] { Shop() }));
        fixture.Sync();
        Check(fixture.Pending.completed && fixture.Persisted().purchases.Single().completed, "A retry should recover using the permanent receipt");
        Check(fixture.Journal.purchases.Count == 1 && fixture.Pending.operationId == OperationId, "No replacement operation during recovery");
        AssertNoDebitHttp();
    }

    private static void NotFoundResubmitsStableId()
    {
        using var fixture = new Fixture();
        fixture.AddPending();
        Startup(fixture);
        Reply("GET", "/api/game/purchases/" + OperationId, new { error = "Not found" }, 404);
        Reply("POST", "/api/game/purchases", Purchase());
        Reply("GET", "/api/game/wallet", Wallet(800, 6));
        Reply("GET", "/api/game/entitlements", Entitlements(6, new object[] { Shop() }));
        fixture.Sync();
        var request = UnityWebRequest.Requests.Single(request => request.Method == "POST");
        var body = (IDictionary<string, object>)TestJson.Parse(request.Body);
        Check((string)body["operationId"] == OperationId && (string)body["targetId"] == "shop_hat", "Retry the same operation and target");
        Check(!body.ContainsKey("price") && !body.ContainsKey("playFabId"), "The client supplies no price or account override");
        Check(fixture.Pending.completed && fixture.Journal.purchases.Count == 1, "Persist a single completed operation");
    }

    private static void SessionChangeAborts()
    {
        using var fixture = new Fixture();
        fixture.AddPending();
        int previousGold = fixture.Data.CurrentData.gold;
        Reply("GET", "/api/game/wallet", Wallet(), after: () => {
            fixture.Cloud.SessionGeneration++;
            fixture.Cloud.ActiveAccountId = "DEF456";
            fixture.Data.ActiveAccountId = "DEF456";
            PlayFab.PlayFabSettings.staticPlayer.PlayFabId = "DEF456";
        });
        fixture.Sync();
        AssertPending(fixture);
        Check(fixture.Data.MirrorCount == 0 && fixture.Data.MergeCount == 0, "A stale session response cannot update another account");
        Check(fixture.Data.CurrentData.gold == previousGold && UnityWebRequest.Requests.Count == 1, "Abort before additional requests or balance writes");
        Check(!fixture.Service.IsRefreshing, "Always finish the coroutine cleanup");
    }

    private static void WrongOperationCannotComplete()
    {
        using var fixture = new Fixture();
        fixture.AddPending();
        Startup(fixture);
        Reply("GET", "/api/game/purchases/" + OperationId, Purchase("aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee"));
        fixture.Sync();
        AssertPending(fixture);
        Check(UnityWebRequest.Requests.Count == 3 && fixture.Callbacks.Count == 0, "Reject a mismatched operation response");
    }

    private static void MatchingCapturedQuoteRepairs() => MatchingCapturedQuoteRepairs(false);

    private static void MatchingCapturedQuoteRepairs(bool nullLegacyOutbox)
    {
        using var fixture = new Fixture();
        SeedOldQuote(fixture, 25000);
        if (nullLegacyOutbox) fixture.Data.CurrentData.walletRewardOutbox = null;
        Startup(fixture);
        Reply("POST", "/api/game/rewards", Wallet(11400, 6));
        fixture.Sync();
        var posted = (IDictionary<string, object>)TestJson.Parse(UnityWebRequest.Requests.Single(request => request.Path == "/api/game/rewards").Body);
        var reward = (IDictionary<string, object>)((IList)posted["events"])[0];
        var evidence = (IDictionary<string, object>)reward["evidence"];
        Check((long)evidence["quotedAmount"] == 10500 && (long)evidence["finalCost"] == 25000 && (long)evidence["failureCount"] == 0, "Repair only the captured quote, not cost or failures");
        Check((string)reward["eventId"] == "contract:ShopKeeper", "Preserve the canonical reward identity");
        Check(fixture.Journal.rewards.Count == 0 && fixture.Persisted().deliveredRewardIds.Contains("contract:ShopKeeper"), "Persist confirmed delivery once");
        Check(fixture.Data.AckCount == 1 && fixture.Service.CoinBalance == 11400, "Apply only the server-confirmed reward balance");
    }

    private static void MismatchedCaptureDoesNotInventEvidence()
    {
        using var fixture = new Fixture();
        SeedOldQuote(fixture, 26000);
        Startup(fixture);
        Reply("POST", "/api/game/rewards", new { error = "Quote requires review" }, 400);
        fixture.Sync();
        var posted = (IDictionary<string, object>)TestJson.Parse(UnityWebRequest.Requests.Single(request => request.Path == "/api/game/rewards").Body);
        var evidence = (IDictionary<string, object>)((IDictionary<string, object>)((IList)posted["events"])[0])["evidence"];
        Check((long)evidence["quotedAmount"] == 500 && (long)evidence["finalCost"] == 25000 && (long)evidence["failureCount"] == 0, "Different attempts cannot authorize rewriting old evidence");
        Check(fixture.Journal.rewards.Single().eventId == "contract:ShopKeeper" && fixture.Persisted().rewards.Single().evidence.quotedAmount == 500, "Preserve old quote/identity for review");
        Check(fixture.Data.AckCount == 0 && fixture.Journal.deliveredRewardIds.Count == 0, "Unconfirmed reward is never acknowledged");
        Check(fixture.Service.CoinsAvailable, "Keep independently verified account funds available");
    }

    private static void SeedOldQuote(Fixture fixture, double capturedCost)
    {
        fixture.Journal.rewards.Add(new GameWalletRewardEvent {
            kind = "contract", sourceId = "ShopKeeper", eventId = "contract:ShopKeeper",
            evidence = new GameWalletRewardEvidence { finalCost = 25000, failureCount = 0, quotedAmount = 500 },
        });
        fixture.Data.CurrentData.walletContractEvidence.Add(new GameWalletContractEvidence {
            contractId = "ShopKeeper", hasExpQuote = true, quotedExp = 50,
            evidence = new GameWalletRewardEvidence { finalCost = capturedCost, failureCount = 0, quotedAmount = 10500 },
        });
        Check(Invoke(fixture.Service, "SaveJournal") is true, "Persist the old queued reward before repair");
    }

    private static void QuoteRepairWriteFailureBlocksHttp()
    {
        using var fixture = new Fixture();
        SeedOldQuote(fixture, 25000);
        SaveEncryption.FailNextEncrypt = true;
        fixture.Sync();
        Check(UnityWebRequest.Requests.Count == 0, "Do not send requests when repaired journal persistence fails");
        Check(fixture.Persisted().rewards.Single().evidence.quotedAmount == 500, "Failed write must preserve the original disk recovery record");
        Check(fixture.Journal.rewards.Single().eventId == "contract:ShopKeeper" && fixture.Data.AckCount == 0, "Keep the same unacknowledged reward");
        Check(Get<bool>(fixture.Service, "journalDirty"), "Keep the corrected journal dirty for safe retry");
        Startup(fixture);
        Reply("POST", "/api/game/rewards", Wallet(11400, 6), after: () => {
            Check(fixture.Persisted().rewards.Single().evidence.quotedAmount == 10500, "Persist the corrected evidence before sending its HTTP request");
        });
        fixture.Sync();
        Check(fixture.Journal.rewards.Count == 0 && fixture.Persisted().deliveredRewardIds.Contains("contract:ShopKeeper"), "Retry can confirm the same reward without a replacement ID");
    }

    private static void MidPumpJournalFailureBlocksMonetaryHttp()
    {
        using var fixture = new Fixture();
        fixture.AddPending();
        Reply("GET", "/api/game/wallet", Wallet(), after: () => {
            fixture.Data.CurrentData.walletRewardOutbox.Add(new GameWalletRewardEvent { kind = "achievement", sourceId = "ACH_002", eventId = "achievement:ACH_002" });
            SaveEncryption.FailAllEncrypt = true;
        });
        Reply("GET", "/api/game/entitlements", Entitlements());
        fixture.Sync();
        Check(UnityWebRequest.Requests.Count == 2 && UnityWebRequest.Requests.All(request => request.Method == "GET"), "No purchase/reward submission before a changed journal is persisted");
        AssertPending(fixture);
        Check(fixture.Journal.rewards.Single().eventId == "achievement:ACH_002" && fixture.Persisted().rewards.Count == 0, "Retain unsaved incoming reward and the original disk journal");
        Check(!fixture.Service.CanSpendOnline && fixture.Data.AckCount == 0, "Storage failure disables new spending and cannot acknowledge rewards");
        SaveEncryption.FailAllEncrypt = false;
        Recovery(fixture, Entitlements(6, new object[] { Shop() }));
        Reply("POST", "/api/game/rewards", Wallet(950, 7));
        fixture.Sync();
        Check(fixture.Pending.completed && fixture.Journal.purchases.Count == 1, "After persistence recovers, complete only the original purchase");
        Check(fixture.Persisted().deliveredRewardIds.Contains("achievement:ACH_002"), "Retry the same persisted incoming reward");
        AssertNoDebitHttp();
    }

    private static void ImportFailurePreservesDiamonds(long code)
    {
        using var fixture = new Fixture();
        fixture.Data.CurrentData.walletAuthorityVersion = 0;
        Reply("GET", "/api/game/wallet", AbsentCoinWallet());
        Reply("POST", "/api/game/wallet/import", new { error = "Coin migration unavailable" }, code);
        fixture.Sync();
        Check(!fixture.Service.CoinsAvailable && !fixture.Service.CanSpendOnline, "Failed Coin migration cannot authorize Coin spending");
        Check(fixture.Service.DiamondsAvailable && fixture.Service.DiamondBalance == 25, "Keep the independent Diamond balance proven by the current wallet response");
        Check(fixture.Data.CurrentData.gold == 123 && fixture.Data.CurrentData.walletAuthorityVersion == 0, "Do not overwrite or mark the selected legacy save as imported");
        Check(fixture.Data.MirrorCount == 0 && UnityWebRequest.Requests.Count == 2, "A rejected import has no mirror or further requests");
    }

    private static void UnresolvedLegacySourceBlocksOnlyImport()
    {
        using var fixture = new Fixture();
        fixture.Cloud.CanImportLegacyWallet = false;
        fixture.Data.CurrentData.walletAuthorityVersion = 0;
        Reply("GET", "/api/game/wallet", AbsentCoinWallet());
        fixture.Sync();
        Check(UnityWebRequest.Requests.Count == 1 && UnityWebRequest.Requests[0].Method == "GET", "Failed/unresolved cloud startup cannot permanently choose the fallback local balance");
        Check(fixture.Service.DiamondsAvailable && fixture.Service.DiamondBalance == 25 && !fixture.Service.CoinsAvailable, "Read-only account balances remain available without approving a Coin import");
        Check(fixture.Data.CurrentData.gold == 123 && fixture.Data.CurrentData.walletAuthorityVersion == 0 && fixture.Data.MirrorCount == 0, "Retain the unresolved local save without importing or mirroring it");
    }

    private static void AlreadyReadyWalletWithoutLegacySource()
    {
        using var fixture = new Fixture();
        fixture.Cloud.CanImportLegacyWallet = false;
        fixture.AddPending();
        Recovery(fixture, Entitlements(6, new object[] { Shop() }));
        fixture.Sync();
        Check(fixture.Pending.completed && fixture.Service.CoinsAvailable && fixture.Service.DiamondsAvailable, "An existing server-authoritative wallet may recover normally");
        Check(!UnityWebRequest.Requests.Any(request => request.Path == "/api/game/wallet/import"), "Never attempt legacy import for a ready server wallet");
        AssertNoDebitHttp();
    }

    private static void SourceRevokedDuringWalletRequest()
    {
        using var fixture = new Fixture();
        fixture.Data.CurrentData.walletAuthorityVersion = 0;
        Reply("GET", "/api/game/wallet", AbsentCoinWallet(), after: () => fixture.Cloud.CanImportLegacyWallet = false);
        fixture.Sync();
        Check(UnityWebRequest.Requests.Count == 1 && UnityWebRequest.Requests.All(request => request.Method == "GET"), "Recheck approval after awaiting the wallet response and before preparing the import");
        Check(fixture.Service.DiamondsAvailable && fixture.Service.DiamondBalance == 25 && !fixture.Service.CoinsAvailable, "Retain the independent read-only Diamond result");
        Check(fixture.Data.CurrentData.walletAuthorityVersion == 0 && fixture.Data.MirrorCount == 0, "A changed startup decision cannot be committed as the account opening balance");
    }

    private static void CachedAuthorityNeverReimports()
    {
        using var fixture = new Fixture();
        Reply("GET", "/api/game/wallet", AbsentCoinWallet());
        fixture.Sync();
        Check(UnityWebRequest.Requests.Count == 1 && UnityWebRequest.Requests.All(request => request.Method == "GET"), "A saved authority marker cannot become a second legacy import");
        Check(fixture.Service.DiamondsAvailable && fixture.Service.DiamondBalance == 25 && !fixture.Service.CoinsAvailable, "Coin recovery readiness must not hide a separately verified Diamond balance");
        Check(fixture.Data.CurrentData.gold == 123 && fixture.Data.CurrentData.walletAuthorityVersion == 1, "Never reset the cached authority or feed cached Coins back into migration");
    }

    private static void CacheFailurePreservesDiamonds()
    {
        using var fixture = new Fixture();
        fixture.Data.FailMirror = true;
        Reply("GET", "/api/game/wallet", Wallet());
        fixture.Sync();
        Check(!fixture.Service.CoinsAvailable && !fixture.Service.CanSpendOnline && fixture.Data.CurrentData.gold == 123, "Failed Coin cache save must not authorize spending");
        Check(fixture.Service.DiamondsAvailable && fixture.Service.DiamondBalance == 25, "Coin cache persistence is independent of the validated Diamond balance");
    }

    private static void WholeWalletFailureClearsCurrencies(string scenario)
    {
        using var fixture = new Fixture();
        Startup(fixture);
        fixture.Sync();
        Check(fixture.Service.CoinsAvailable && fixture.Service.DiamondsAvailable, "Precondition: both currencies were previously verified");
        long code = scenario == "401" ? 401 : scenario == "network" ? 0 : 200;
        object body = scenario == "malformed" ? new { coins = 900, ready = true, version = 6, diamonds = "not-a-balance" } : new { error = "Unavailable" };
        Reply("GET", "/api/game/wallet", body, code);
        fixture.Sync();
        Check(!fixture.Service.CoinsAvailable && !fixture.Service.DiamondsAvailable && !fixture.Service.CanSpendOnline, "Whole-response/auth/network failures must invalidate both availability flags");
        Check(fixture.Service.RequiresSignIn == (scenario == "401"), "Only an authentication failure requires signing in again");
        Check(fixture.Data.CurrentData.gold == 900, "Do not replace the previous verified local cache with fake zero");
    }

    private static void GenerationChangeClearsCurrencies()
    {
        using var fixture = new Fixture();
        Startup(fixture);
        fixture.Sync();
        Reply("GET", "/api/game/wallet", Wallet(700, 6), after: () => fixture.Cloud.SessionGeneration++);
        fixture.Sync();
        Check(!fixture.Service.CoinsAvailable && !fixture.Service.DiamondsAvailable && !fixture.Service.CanSpendOnline, "A request from a revoked generation cannot leave old balances marked available");
        Check(fixture.Data.CurrentData.gold == 900, "Do not mirror the stale response after a generation change");
    }

    private static void AccountSwitchClearsCurrencies()
    {
        using var fixture = new Fixture();
        Startup(fixture);
        fixture.Sync();
        fixture.Cloud.ActiveAccountId = fixture.Data.ActiveAccountId = PlayFab.PlayFabSettings.staticPlayer.PlayFabId = "DEF456";
        fixture.Cloud.SessionGeneration++;
        fixture.Cloud.CanPublishDashboard = false;
        Invoke(fixture.Service, "Update");
        Check(!fixture.Service.CoinsAvailable && !fixture.Service.DiamondsAvailable && !fixture.Service.CanSpendOnline, "Account switching must invalidate availability before any request for the new account");
        Check(UnityWebRequest.Requests.Count == 2, "Do not use the old account's wallet response for the new account");
    }

    private static void ImportReservationAlwaysReleases(string scenario)
    {
        using var fixture = new Fixture();
        fixture.Data.CurrentData.walletAuthorityVersion = 0;
        Reply("GET", "/api/game/wallet", AbsentCoinWallet());
        long code = scenario == "success" || scenario == "switch" ? 200 : scenario == "network" ? 0 : long.Parse(scenario);
        object body = code == 200 ? Wallet(123, 1) : new { error = "Coin import unavailable" };
        Reply("POST", "/api/game/wallet/import", body, code, () => {
            Check(fixture.Cloud.ImportReservationActive, "Hold the approved save-selection reservation during the import request");
            if (scenario == "switch") {
                fixture.Cloud.SessionGeneration++;
                fixture.Cloud.ActiveAccountId = fixture.Data.ActiveAccountId = PlayFab.PlayFabSettings.staticPlayer.PlayFabId = "DEF456";
            }
        });
        if (scenario == "success") Reply("GET", "/api/game/entitlements", Entitlements(1));
        fixture.Sync();
        Check(fixture.Cloud.ImportReservationBegins == 1 && fixture.Cloud.ImportReservationEnds == 1 && !fixture.Cloud.ImportReservationActive,
            "Release the matching reservation exactly once on every import outcome");
        if (scenario == "success") {
            Check(fixture.Data.CurrentData.walletAuthorityVersion == 1 && fixture.Service.CoinsAvailable && fixture.Service.DiamondsAvailable,
                "A successful reserved import may mirror the authoritative wallet");
        } else {
            Check(fixture.Data.MirrorCount == 0 && fixture.Data.CurrentData.walletAuthorityVersion == 0, "Failed or stale imports cannot commit local authority");
            Check(fixture.Service.DiamondsAvailable == (scenario == "400" || scenario == "503"), "Retain Diamonds only for scoped semantic Coin failures in the current account");
            Check(!fixture.Service.CoinsAvailable, "Failed imports cannot authorize Coin spending");
        }
    }

    private static void ImportReservationRejected()
    {
        using var fixture = new Fixture();
        fixture.Data.CurrentData.walletAuthorityVersion = 0;
        fixture.Cloud.DenyImportReservation = true;
        Reply("GET", "/api/game/wallet", AbsentCoinWallet());
        fixture.Sync();
        Check(UnityWebRequest.Requests.Count == 1 && UnityWebRequest.Requests[0].Method == "GET", "A selection race cannot dispatch an unreserved opening balance");
        Check(fixture.Cloud.ImportReservationBegins == 1 && fixture.Cloud.ImportReservationEnds == 0 && !fixture.Cloud.ImportReservationActive,
            "Never release a reservation this attempt did not acquire");
        Check(fixture.Service.DiamondsAvailable && fixture.Service.DiamondBalance == 25 && !fixture.Service.CoinsAvailable, "Preserve independently validated read-only Diamonds");
    }

    private static void MalformedImportInvalidatesCurrencies()
    {
        using var fixture = new Fixture();
        fixture.Data.CurrentData.walletAuthorityVersion = 0;
        Reply("GET", "/api/game/wallet", AbsentCoinWallet());
        Reply("POST", "/api/game/wallet/import", new { coins = 123, ready = true, version = 1, diamonds = "broken" });
        fixture.Sync();
        Check(!fixture.Service.CoinsAvailable && !fixture.Service.DiamondsAvailable, "Malformed whole-wallet data cannot preserve availability flags");
        Check(fixture.Data.CurrentData.gold == 123 && fixture.Data.CurrentData.walletAuthorityVersion == 0 && fixture.Data.MirrorCount == 0,
            "Do not commit an invalid import response");
        Check(!fixture.Cloud.ImportReservationActive && fixture.Cloud.ImportReservationEnds == 1, "Malformed replies cannot strand the local selection reservation");
    }

    private static bool IsImportRequest(IEnumerator routine) => routine.GetType().Name.Contains("<Request>") &&
        routine.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Any(field => field.FieldType == typeof(string) && (string)field.GetValue(routine) == "/api/game/wallet/import");

    private static void SourceRevokedBeforeImportDispatch()
    {
        using var fixture = new Fixture();
        fixture.Data.CurrentData.walletAuthorityVersion = 0;
        Reply("GET", "/api/game/wallet", AbsentCoinWallet());
        bool revoked = false;
        fixture.Sync(routine => {
            if (!IsImportRequest(routine)) return;
            Check(fixture.Cloud.ImportReservationActive, "Acquire the matching selection reservation before preparing its request");
            fixture.Cloud.CanImportLegacyWallet = false;
            revoked = true;
        });
        Check(revoked && UnityWebRequest.Requests.Count == 1 && UnityWebRequest.Requests[0].Method == "GET", "Eligibility revoked between coroutine yields must never dispatch import");
        Check(fixture.Cloud.ImportReservationBegins == 1 && fixture.Cloud.ImportReservationEnds == 1 && !fixture.Cloud.ImportReservationActive,
            "Release the acquired reservation even when no HTTP was dispatched");
        Check(fixture.Service.DiamondsAvailable && fixture.Service.DiamondBalance == 25 && !fixture.Service.CoinsAvailable,
            "A blocked save selection is not a transport failure and must retain the verified Diamond result");
    }

    private static void ImportSnapshotDoesNotBorrowMutableLists()
    {
        using var fixture = new Fixture();
        fixture.Data.CurrentData.walletAuthorityVersion = 0;
        var data = fixture.Data.CurrentData;
        data.completedContracts.Add("OldLegacy");
        data.unlockedAchievements.Add("ACH_001");
        data.purchasedShopItemIds.Add("shop_hat");
        data.unlockedCosmeticIDs.Add("Hat");
        data.unlockedContractMaterials.Add("ShopKeeper_Beam");
        Reply("GET", "/api/game/wallet", AbsentCoinWallet());
        Reply("POST", "/api/game/wallet/import", Wallet(123, 1));
        Reply("GET", "/api/game/entitlements", Entitlements(1));
        bool changed = false;
        fixture.Sync(routine => {
            if (!IsImportRequest(routine)) return;
            data.completedContracts.Add("LaterContract");
            data.unlockedAchievements.Add("ACH_002");
            data.purchasedShopItemIds.Add("shop_boots");
            data.unlockedCosmeticIDs.Add("Boots");
            data.unlockedContractMaterials.Add("ShopKeeper_Rope");
            changed = true;
        });
        var request = UnityWebRequest.Requests.Single(request => request.Path == "/api/game/wallet/import");
        var body = (IDictionary<string, object>)TestJson.Parse(request.Body);
        Check(changed, "Exercise the await boundary after constructing the selected save snapshot");
        foreach (var expected in new Dictionary<string, string> {
            ["completedContracts"] = "OldLegacy", ["unlockedAchievements"] = "ACH_001", ["purchasedShopItemIds"] = "shop_hat",
            ["unlockedCosmeticIDs"] = "Hat", ["unlockedContractMaterials"] = "ShopKeeper_Beam",
        }) {
            var values = (IList)body[expected.Key];
            Check(values.Count == 1 && (string)values[0] == expected.Value, "Snapshot arrays must remain bound to the original selected save: " + expected.Key);
        }
        Check(fixture.Cloud.ImportReservationEnds == 1 && !fixture.Cloud.ImportReservationActive, "Release the successful immutable snapshot reservation");
    }

    private static GameWalletRewardEvent PendingContract() => new() {
        kind = "contract", sourceId = "ShopKeeper", eventId = "contract:ShopKeeper",
        evidence = new GameWalletRewardEvidence { finalCost = 75000, failureCount = 0, quotedAmount = 500 },
    };

    private static void HistoricalJournalOverlapBlocksImport(string kind)
    {
        using var fixture = new Fixture();
        fixture.Data.CurrentData.walletAuthorityVersion = 0;
        fixture.Data.CurrentData.gold = 500;
        GameWalletRewardEvent pending = kind == "achievement" ? new GameWalletRewardEvent {
            kind = "achievement", sourceId = "ACH_001", eventId = "achievement:ACH_001",
        } : PendingContract();
        fixture.Journal.rewards.Add(pending);
        if (kind == "achievement") fixture.Data.CurrentData.unlockedAchievements.Add("ACH_001");
        else fixture.Data.CurrentData.completedContracts.Add(kind == "contract-alias" ? "ShopKeeperContract" : "ShopKeeper");
        Check(Invoke(fixture.Service, "SaveJournal") is true, "Persist the retained account-journal reward independently of the selected story save");
        Reply("GET", "/api/game/wallet", AbsentCoinWallet());
        fixture.Sync();
        Check(UnityWebRequest.Requests.Count == 1 && UnityWebRequest.Requests[0].Method == "GET", "An already-earned historical baseline and ambiguous retained reward cannot be imported and paid twice");
        Check(fixture.Cloud.ImportReservationBegins == 0 && !fixture.Cloud.ImportReservationActive, "Block ambiguous source overlap before reserving or posting an opening");
        Check(fixture.Data.CurrentData.gold == 500 && fixture.Data.CurrentData.walletAuthorityVersion == 0, "Do not reinterpret historical money or change its import marker");
        Check(fixture.Persisted().rewards.Single().eventId == pending.eventId && fixture.Data.AckCount == 0, "Retain the original unacknowledged reward for review");
        Check(fixture.Service.DiamondsAvailable && fixture.Service.DiamondBalance == 25 && !fixture.Service.CoinsAvailable, "Coin baseline ambiguity must not hide verified read-only Diamonds");
    }

    private static void SourceBackedAliasRemainsUnpaid()
    {
        using var fixture = new Fixture();
        fixture.Data.CurrentData.walletAuthorityVersion = 0;
        fixture.Data.CurrentData.gold = 500; // Existing unrelated legacy funds, not this queued reward.
        fixture.Data.CurrentData.completedContracts.Add("ShopKeeperContract");
        GameWalletRewardEvent reward = PendingContract();
        fixture.Journal.rewards.Add(reward);
        fixture.Data.CurrentData.walletRewardOutbox.Add(reward);
        Check(Invoke(fixture.Service, "SaveJournal") is true, "Persist the selected-source-backed unpaid event");
        Reply("GET", "/api/game/wallet", AbsentCoinWallet());
        Reply("POST", "/api/game/wallet/import", Wallet(500, 1));
        Reply("GET", "/api/game/entitlements", Entitlements(1));
        Reply("POST", "/api/game/rewards", Wallet(1000, 2));
        fixture.Sync();
        var body = (IDictionary<string, object>)TestJson.Parse(UnityWebRequest.Requests.Single(request => request.Path == "/api/game/wallet/import").Body);
        Check(((IList)body["completedContracts"]).Count == 0, "Canonicalize the alias before excluding its proven-unpaid completion from opening tombstones");
        Check((long)body["gold"] == 500 && fixture.Service.CoinBalance == 1000, "Import the approved old funds once and apply only the separately confirmed unpaid reward");
        Check(fixture.Persisted().rewards.Count == 0 && fixture.Persisted().deliveredRewardIds.Contains("contract:ShopKeeper"), "Confirm the same source event once");
    }

    private static void NonOverlappingJournalRewardSurvivesImport()
    {
        using var fixture = new Fixture();
        fixture.Data.CurrentData.walletAuthorityVersion = 0;
        fixture.Data.CurrentData.gold = 500;
        fixture.Data.CurrentData.completedContracts.Add("VancesContract");
        fixture.Journal.rewards.Add(PendingContract());
        Check(Invoke(fixture.Service, "SaveJournal") is true, "Persist a different account-earned source without selected-save overlap");
        Reply("GET", "/api/game/wallet", AbsentCoinWallet());
        Reply("POST", "/api/game/wallet/import", Wallet(500, 1));
        Reply("GET", "/api/game/entitlements", Entitlements(1));
        Reply("POST", "/api/game/rewards", Wallet(1000, 2));
        fixture.Sync();
        var body = (IDictionary<string, object>)TestJson.Parse(UnityWebRequest.Requests.Single(request => request.Path == "/api/game/wallet/import").Body);
        Check(((IList)body["completedContracts"]).Count == 1 && (string)((IList)body["completedContracts"])[0] == "VancesContract", "Retain unrelated already-earned history in the selected baseline");
        Check(fixture.Service.CoinBalance == 1000 && fixture.Journal.rewards.Count == 0 && fixture.Persisted().deliveredRewardIds.Contains("contract:ShopKeeper"), "A non-overlapping retained reward may be confirmed after a valid opening");
    }

    private static void UnverifiedCoinErrorInvalidatesBoth(string scenario)
    {
        using var fixture = new Fixture();
        fixture.Data.CurrentData.walletAuthorityVersion = 0;
        Reply("GET", "/api/game/wallet", AbsentCoinWallet());
        object body = scenario == "html" ? "<html>Gateway unavailable</html>" : scenario == "numeric-error" ? new { error = 503 } : new { error = " " };
        Reply("POST", "/api/game/wallet/import", body, 503);
        fixture.Sync();
        Check(!fixture.Service.CoinsAvailable && !fixture.Service.DiamondsAvailable, "An unverified error body is not proof of a scoped Coin service failure");
        Check(fixture.Data.MirrorCount == 0 && fixture.Data.CurrentData.walletAuthorityVersion == 0 && fixture.Data.CurrentData.gold == 123, "Do not commit malformed or proxy responses");
        Check(!fixture.Cloud.ImportReservationActive && fixture.Cloud.ImportReservationEnds == 1, "Release local selection reservation on unverified responses");
    }

    private static void TitleChangedDuringAwait()
    {
        using var fixture = new Fixture();
        Startup(fixture);
        fixture.Sync();
        Reply("GET", "/api/game/wallet", Wallet(700, 6), after: () => PlayFab.PlayFabSettings.staticSettings.TitleId = "31AF02");
        fixture.Sync();
        Check(!fixture.Service.CoinsAvailable && !fixture.Service.DiamondsAvailable && !fixture.Service.CanSpendOnline, "An SDK title change cannot retain another title's currency proof");
        Check(fixture.Data.CurrentData.gold == 900 && fixture.Data.MirrorCount == 1, "Reject the stale title response before any cache write");
    }

    private static void EquivalentTitleCasingIsAllowed()
    {
        using var fixture = new Fixture();
        PlayFab.PlayFabSettings.staticSettings.TitleId = "17fa03";
        Startup(fixture);
        fixture.Sync();
        Check(fixture.Service.CoinsAvailable && fixture.Service.DiamondsAvailable && fixture.Service.CanSpendOnline && !fixture.Service.RequiresSignIn,
            "Case normalization of the same PlayFab title is not an account or title switch");
    }

    private static void SourceReadinessAutomaticallyWakesImport()
    {
        using var fixture = new Fixture();
        fixture.Data.CurrentData.walletAuthorityVersion = 0;
        fixture.Cloud.CanImportLegacyWallet = false;
        Reply("GET", "/api/game/wallet", AbsentCoinWallet());
        fixture.UpdateAndDrive();
        Check(UnityWebRequest.Requests.Count == 1 && !fixture.Service.CoinsAvailable && fixture.Service.DiamondsAvailable,
            "The initial unapproved source permits only a read-only balance request");
        Check(!Get<bool>(fixture.Service, "refreshRequested") && Get<float>(fixture.Service, "nextAttempt") > Time.unscaledTime,
            "Precondition: the initial refresh was consumed and its ordinary retry timer has not elapsed");
        fixture.Cloud.CanImportLegacyWallet = true;
        QueueApprovedOpening();
        fixture.UpdateAndDrive();
        AssertSingleAutomaticallyCompletedOpening(fixture);
    }

    private static void StartupReadinessAutomaticallyWakesImport()
    {
        using var fixture = new Fixture();
        fixture.Data.CurrentData.walletAuthorityVersion = 0;
        fixture.Cloud.CanImportLegacyWallet = false;
        fixture.Cloud.CanPublishDashboard = fixture.Cloud.IsSessionReady = false;
        Invoke(fixture.Service, "Update");
        Check(fixture.Service.StartedCoroutines.Count == 0 && UnityWebRequest.Requests.Count == 0,
            "Do not authorize HTTP before the account startup state is ready");
        fixture.Cloud.CanPublishDashboard = fixture.Cloud.IsSessionReady = true;
        Reply("GET", "/api/game/wallet", AbsentCoinWallet());
        fixture.UpdateAndDrive();
        Check(UnityWebRequest.Requests.Count == 1 && !fixture.Service.CoinsAvailable && fixture.Service.DiamondsAvailable,
            "Ready account data with an unapproved legacy source still cannot post an opening");
        fixture.Cloud.CanImportLegacyWallet = true;
        QueueApprovedOpening();
        fixture.UpdateAndDrive();
        AssertSingleAutomaticallyCompletedOpening(fixture);
    }

    private static void QueueApprovedOpening()
    {
        Reply("GET", "/api/game/wallet", AbsentCoinWallet());
        Reply("POST", "/api/game/wallet/import", Wallet(123, 1));
        Reply("GET", "/api/game/entitlements", Entitlements(1));
    }

    private static void AssertSingleAutomaticallyCompletedOpening(Fixture fixture)
    {
        Check(UnityWebRequest.Requests.Count(request => request.Method == "POST" && request.Path == "/api/game/wallet/import") == 1,
            "A readiness transition must perform exactly one opening import");
        Check(fixture.Data.CurrentData.walletAuthorityVersion == 1 && fixture.Data.CurrentData.gold == 123 &&
            fixture.Service.CoinsAvailable && fixture.Service.CoinBalance == 123 && fixture.Service.DiamondsAvailable && fixture.Service.DiamondBalance == 25,
            "Automatic recovery must mirror the approved opening and keep both verified balances coherent");
        Check(fixture.Cloud.ImportReservationBegins == 1 && fixture.Cloud.ImportReservationEnds == 1 && !fixture.Cloud.ImportReservationActive,
            "The automatic opening uses and releases the same source reservation");
        int started = fixture.Service.StartedCoroutines.Count, requests = UnityWebRequest.Requests.Count;
        Time.unscaledTime += 1000f;
        for (int i = 0; i < 10; i++) Invoke(fixture.Service, "Update");
        Check(fixture.Service.StartedCoroutines.Count == started && UnityWebRequest.Requests.Count == requests,
            "Stable source readiness after completion must not schedule repeat GETs or imports");
    }

    private static object AvailabilityError(string state) => state switch {
        "legacy-disabled" => new { error = "Game wallet is not enabled." },
        "not-ready" => new { code = "GAME_WALLET_NOT_READY", error = "private-provider-details-must-not-be-displayed" },
        _ => new { code = "GAME_WALLET_DISABLED", error = "private-provider-details-must-not-be-displayed" },
    };

    private static object ShopLink(string currency, string invalid = null)
    {
        string target = "/dashboard/shop?currency=" + currency + "&gameLink=opaque-fixture-link";
        string url = GameWalletPolicy.WebsiteOrigin + "/login?redirect=" + Uri.EscapeDataString(target);
        if (invalid == "fork") url = "https://civil-craft-website.vercel.app/shop?currency=" + currency;
        if (invalid == "evil") url = "https://civil-craft.vercel.app.evil.test/shop";
        if (invalid == "http") url = "http://civil-craft.vercel.app/shop";
        return new { url, expiresAt = DateTime.UtcNow.AddMinutes(invalid == "expired" ? -1 : 15).ToString("O") };
    }

    private static void AssertReadOnlyUnavailableRequests(int start = 0)
    {
        Check(UnityWebRequest.Requests.Skip(start).All(request =>
            request.Method == "POST" && request.Path == "/api/game/shop-link" ||
            request.Method == "GET" && (request.Path == "/api/game/wallet" || request.Path == "/api/player/currencies")),
            "Setup unavailability cannot import, submit rewards, repair operations or debit currency");
        Check(UnityWebRequest.Requests.All(request => request.Headers.TryGetValue("Authorization", out string header) &&
            header == "Bearer fixture-session-ticket" && request.RedirectLimit == 0 && request.Timeout == 15 &&
            !request.Url.Contains("fixture-session-ticket", StringComparison.Ordinal)),
            "Every request keeps bounded transport and session tickets in the HTTPS header, never a URL");
    }

    private static void VerifiedShopLinkOpensMainSite(string currency)
    {
        using var fixture = new Fixture();
        var callbacks = new List<(bool success, string error)>();
        Check(fixture.Service.RequestShopLink(currency, (success, error) => callbacks.Add((success, error))), "Queue the selected storefront");
        Check(!fixture.Service.RequestShopLink(currency, null), "Repeated clicks must not queue another browser opening");
        Reply("POST", "/api/game/shop-link", ShopLink(currency));
        Startup(fixture);
        fixture.Sync();
        Check(callbacks.Count == 1 && callbacks[0].success && callbacks[0].error == null && Application.OpenedUrls.Count == 1,
            "The confirmed request opens exactly one verified URL and completes exactly one callback");
        string opened = Application.OpenedUrls.Single();
        Check(new Uri(opened).Host == "civil-craft.vercel.app" && Uri.UnescapeDataString(opened).Contains("currency=" + currency) &&
            !opened.Contains("fixture-session-ticket", StringComparison.Ordinal), "Open the selected main-site storefront without exposing a login ticket");
        var link = UnityWebRequest.Requests.Single(request => request.Path == "/api/game/shop-link");
        Check((string)((IDictionary)TestJson.Parse(link.Body))["currency"] == currency && link.Headers["Authorization"] == "Bearer fixture-session-ticket",
            "The account-bound link request carries only the selected currency in its body");
    }

    private static void KnownUnavailableShopAndWallet(string currency, string availability)
    {
        using var fixture = new Fixture();
        Startup(fixture); fixture.Sync();
        int first = UnityWebRequest.Requests.Count;
        fixture.AddPending();
        fixture.Data.CurrentData.walletRewardOutbox.Add(PendingContract());
        var callbacks = new List<(bool success, string error)>();
        Check(fixture.Service.RequestShopLink(currency, (success, error) => callbacks.Add((success, error))), "Queue a confirmed shop request");
        Reply("POST", "/api/game/shop-link", AvailabilityError(availability), 503);
        Reply("GET", "/api/game/wallet", AvailabilityError(availability), 503);
        Reply("GET", "/api/player/currencies", new { coins = 100500, coinsAvailable = true, diamonds = (object)null });
        fixture.Sync();
        Check(callbacks.Count == 1 && !callbacks[0].success && callbacks[0].error.Contains("paused") &&
            !callbacks[0].error.Contains("try again", StringComparison.OrdinalIgnoreCase) &&
            !callbacks[0].error.Contains("private-provider", StringComparison.Ordinal), "Explain setup without generic retry or raw error reflection");
        Check(Application.OpenedUrls.Count == 0 && fixture.Service.WalletStatus.Contains("paused", StringComparison.OrdinalIgnoreCase) &&
            !fixture.Service.WalletStatus.Contains("private-provider", StringComparison.Ordinal), "No unbound URL fallback or provider details");
        Check(!fixture.Service.CoinsAvailable && !fixture.Service.DiamondsAvailable && !fixture.Service.CanSpendOnline &&
            fixture.Data.CurrentData.gold == 900 && fixture.Data.CurrentData.walletAuthorityVersion == 1 && fixture.Service.WalletVersion == 5 &&
            fixture.Data.CurrentData.lifetimeGoldEarned == 300 && fixture.Data.CurrentData.lifetimeGoldSpent == 100,
            "Retain the previous Coin cache/version/counters, never substitute zero or adopt classic Coins");
        AssertPending(fixture);
        Check(fixture.Persisted().rewards.Single().eventId == "contract:ShopKeeper", "Queued reward identity is preserved on disk");
        AssertReadOnlyUnavailableRequests(first);
    }

    private static void UnsafeShopLinkCannotOpen(string currency, string invalid)
    {
        using var fixture = new Fixture();
        var callbacks = new List<bool>();
        fixture.Service.RequestShopLink(currency, (success, _) => callbacks.Add(success));
        Reply("POST", "/api/game/shop-link", ShopLink(currency, invalid));
        Startup(fixture); fixture.Sync();
        Check(callbacks.Count == 1 && !callbacks[0] && Application.OpenedUrls.Count == 0, "Unsafe or expired links never open a browser");
        Check(fixture.Service.CoinsAvailable && fixture.Service.DiamondsAvailable, "A rejected link does not replace a valid independently refreshed wallet");
    }

    private static void UntrustedUnavailableResponse(string scenario)
    {
        using var fixture = new Fixture();
        object body = scenario switch {
            "html" => "<html>private-provider-details-must-not-be-displayed</html>",
            "wrong-code" => new { code = "UNKNOWN_STATE", error = "Game wallet is not enabled." },
            "numeric-code" => new { code = 503, error = "Game wallet is not enabled." },
            "wrong-status" => AvailabilityError("coded-disabled"),
            _ => new { error = "private-provider-details-must-not-be-displayed" },
        };
        long code = scenario == "wrong-status" ? 502 : 503;
        string callbackError = null;
        fixture.Service.RequestShopLink("coins", (_, error) => callbackError = error);
        Reply("POST", "/api/game/shop-link", body, code);
        Reply("GET", "/api/game/wallet", body, code);
        fixture.Sync();
        Check(callbackError == "The website shop link is unavailable. Please try again." &&
            !fixture.Service.WalletStatus.Contains("private-provider", StringComparison.Ordinal) &&
            !fixture.Service.WalletStatus.Contains("not enabled", StringComparison.Ordinal), "Only allowlisted HTTP503 evidence receives setup wording");
        Check(UnityWebRequest.Requests.Count == 2 && Application.OpenedUrls.Count == 0 && !fixture.Service.CoinsAvailable && !fixture.Service.DiamondsAvailable,
            "Unknown/malformed errors cannot authorize the independent Diamond request or browser opening");
        AssertReadOnlyUnavailableRequests();
    }

    private static void ShopAuthenticationFailureRequiresSignIn()
    {
        using var fixture = new Fixture();
        string callbackError = null;
        fixture.Service.RequestShopLink("coins", (_, error) => callbackError = error);
        Reply("POST", "/api/game/shop-link", AvailabilityError("coded-disabled"), 401);
        fixture.Sync();
        Check(fixture.Service.RequiresSignIn && callbackError.Contains("Sign in again") && !callbackError.Contains("paused") &&
            !fixture.Service.CoinsAvailable && !fixture.Service.DiamondsAvailable && Application.OpenedUrls.Count == 0 && UnityWebRequest.Requests.Count == 1,
            "Authentication takes precedence and forbids follow-up requests or browser opening");
    }

    private static void ChangeWalletScope(Fixture fixture, string scope)
    {
        if (scope == "generation") fixture.Cloud.SessionGeneration++;
        else if (scope == "account") fixture.Cloud.ActiveAccountId = "DEF456";
        else PlayFab.PlayFabSettings.staticSettings.TitleId = "ABCDEF";
    }

    private static void ShopScopeChangeAborts(string scope)
    {
        using var fixture = new Fixture();
        int callbacks = 0;
        fixture.Service.RequestShopLink("diamonds", (_, _) => callbacks++);
        Reply("POST", "/api/game/shop-link", ShopLink("diamonds"), after: () => ChangeWalletScope(fixture, scope));
        fixture.Sync();
        Check(callbacks == 0 && Application.OpenedUrls.Count == 0 && UnityWebRequest.Requests.Count == 1 &&
            fixture.Data.MirrorCount == 0 && !fixture.Service.CoinsAvailable && !fixture.Service.DiamondsAvailable,
            "A changed generation/account/title cannot consume the outgoing account's URL or wallet response");
    }

    private static void ShopLinkRecoversAfterSetup(string currency)
    {
        using var fixture = new Fixture();
        var callbacks = new List<bool>();
        fixture.Service.RequestShopLink(currency, (success, _) => callbacks.Add(success));
        Reply("POST", "/api/game/shop-link", AvailabilityError("coded-disabled"), 503);
        Reply("GET", "/api/game/wallet", AvailabilityError("coded-disabled"), 503);
        Reply("GET", "/api/player/currencies", new { coins = 100500, diamonds = 25 });
        fixture.Sync();
        Check(Application.OpenedUrls.Count == 0 && !fixture.Service.CoinsAvailable && fixture.Service.DiamondsAvailable, "Disabled setup does not open a fallback URL");
        fixture.Service.RequestShopLink(currency, (success, _) => callbacks.Add(success));
        Reply("POST", "/api/game/shop-link", ShopLink(currency));
        Startup(fixture); fixture.Sync();
        Check(callbacks.SequenceEqual(new[] { false, true }) && Application.OpenedUrls.Count == 1 && fixture.Service.CoinsAvailable &&
            fixture.Service.DiamondsAvailable && fixture.Service.CoinBalance == 900 && !fixture.Service.WalletStatus.Contains("paused"),
            "An explicitly retried verified main-site link and ready wallet recover without sticky setup state");
    }

    private static void IndependentDiamondsForDisabledWallet(string scenario)
    {
        using var fixture = new Fixture();
        Startup(fixture); fixture.Sync();
        int first = UnityWebRequest.Requests.Count, mirrors = fixture.Data.MirrorCount, merges = fixture.Data.MergeCount;
        Reply("GET", "/api/game/wallet", AvailabilityError("coded-disabled"), 503);
        long responseCode = scenario == "network" ? 0 : scenario == "401" ? 401 : scenario == "503" ? 503 : 200;
        object diamond = scenario switch {
            "zero" => 0L, "null" => null, "string" => "25", "overflow" => GameWalletPolicy.MaximumJsonInteger + 1,
            "negative" => -1L, _ => 25L,
        };
        object body = scenario == "missing" ? new { coins = 100500 } : scenario == "malformed" ? new object[] { 25 } :
            new { coins = 100500, coinsAvailable = true, coinReadiness = "ready", diamonds = diamond };
        Reply("GET", "/api/player/currencies", body, responseCode);
        fixture.Sync();
        bool valid = scenario == "valid" || scenario == "zero";
        Check(fixture.Service.DiamondsAvailable == valid && !fixture.Service.CoinsAvailable && !fixture.Service.CanSpendOnline,
            "Only a current, valid independent Diamond count may become available; signed-in Coin spending stays disabled");
        Check(fixture.Data.CurrentData.gold == 900 && fixture.Service.CoinBalance == 900 && fixture.Service.WalletVersion == 5 &&
            fixture.Data.CurrentData.walletCoinsVersion == 5 && fixture.Data.CurrentData.walletAuthorityVersion == 1 &&
            fixture.Data.CurrentData.lifetimeGoldEarned == 300 && fixture.Data.CurrentData.lifetimeGoldSpent == 100 &&
            fixture.Data.MirrorCount == mirrors && fixture.Data.MergeCount == merges, "Legacy endpoint Coin values and readiness cannot alter Coin authority, cache or counters");
        if (valid) Check(fixture.Service.DiamondBalance == (scenario == "zero" ? 0 : 25) &&
            fixture.Service.WalletStatus.Contains("synchronized Diamonds"), "A verified zero or positive count is displayed distinctly from unavailable");
        Check(fixture.Service.RequiresSignIn == (scenario == "401"), "Only Diamond authentication failure demands sign-in and clears both flags");
        if (scenario == "401") Check(fixture.Service.WalletStatus.Contains("Sign in again") && !fixture.Service.WalletStatus.Contains("paused"), "Sign-in status takes precedence over setup wording");
        AssertReadOnlyUnavailableRequests(first);
    }

    private static void IndependentDiamondScopeChangeAborts(string scope)
    {
        using var fixture = new Fixture();
        Reply("GET", "/api/game/wallet", AvailabilityError("coded-disabled"), 503);
        Reply("GET", "/api/player/currencies", new { coins = 100500, diamonds = 999 }, after: () => ChangeWalletScope(fixture, scope));
        fixture.Sync();
        Check(!fixture.Service.CoinsAvailable && !fixture.Service.DiamondsAvailable && fixture.Service.DiamondBalance != 999 &&
            fixture.Data.CurrentData.gold == 123 && fixture.Data.CurrentData.walletAuthorityVersion == 1 && fixture.Service.WalletVersion == 0 &&
            fixture.Data.MirrorCount == 0 && fixture.Data.MergeCount == 0,
            "Changed account/generation/title cannot display another session's Diamond count or apply classic Coins");
        AssertReadOnlyUnavailableRequests();
    }

    private sealed class Fixture : IDisposable
    {
        public readonly GameWalletService Service;
        public readonly PlayerDataManager Data;
        public readonly CloudSaveManager Cloud;
        public readonly List<bool> Callbacks = new();
        private readonly string directory;
        public GameWalletJournal Journal => Get<GameWalletJournal>(Service, "journal");
        public GameWalletPendingPurchase Pending => Journal.purchases.Single();
        public Fixture()
        {
            UnityWebRequest.Reset(); SaveEncryption.FailNextEncrypt = SaveEncryption.FailAllEncrypt = false;
            Time.unscaledTime = 100f; PlayerPrefs.Values.Clear(); Application.OpenedUrls.Clear();
            FusionConnectionManager.Instance = null;
            PlayFab.PlayFabClientAPI.LoggedIn = true;
            PlayFab.PlayFabSettings.staticPlayer.PlayFabId = "ABC123";
            PlayFab.PlayFabSettings.staticPlayer.ClientSessionTicket = "fixture-session-ticket";
            PlayFab.PlayFabSettings.staticSettings.TitleId = "17FA03";
            directory = Path.Combine(Path.GetTempPath(), "CivilCraftGameWalletRecovery-" + Guid.NewGuid().ToString("N"));
            Application.persistentDataPath = directory;
            var gameObject = new GameObject();
            Data = new PlayerDataManager { ActiveAccountId = "ABC123", CurrentData = new PlayerData { gold = 123, walletAuthorityVersion = 1 } };
            Cloud = new CloudSaveManager { ActiveAccountId = "ABC123", SessionGeneration = 42 };
            Service = new GameWalletService();
            PlayerDataManager.Instance = Data;
            gameObject.Attach(Data); gameObject.Attach(Cloud); gameObject.Attach(Service);
            Invoke(Service, "Awake");
            Invoke(Service, "SelectSession", "ABC123", 42);
            Check(Invoke(Service, "SaveJournal") is true, "Create the fixture's durable journal");
        }
        public void AddPending(string kind = "cosmetic", string target = "shop_hat", string contract = null)
        {
            Journal.purchases.Add(new GameWalletPendingPurchase { operationId = OperationId, targetKind = kind, targetId = target, contractId = contract });
            Get<Dictionary<string, Action<bool, string>>>(Service, "purchaseCallbacks")[OperationId] = (success, _) => Callbacks.Add(success);
            Check(Invoke(Service, "SaveJournal") is true, "Persist the original operation before networking");
        }
        public void Sync(Action<IEnumerator> beforeNested = null)
        {
            Drive((IEnumerator)Invoke(Service, "Synchronize", "ABC123", 42), beforeNested);
            Check(UnityWebRequest.Script.Count == 0, "Every scripted response should be consumed");
        }
        public void UpdateAndDrive()
        {
            int previous = Service.StartedCoroutines.Count;
            Invoke(Service, "Update");
            for (int i = previous; i < Service.StartedCoroutines.Count; i++) Drive(Service.StartedCoroutines[i]);
            Check(UnityWebRequest.Script.Count == 0, "Consume every scripted startup response");
        }
        public GameWalletJournal Persisted() => UnityEngine.JsonUtility.FromJson<GameWalletJournal>(File.ReadAllText(Get<string>(Service, "journalPath")));
        public void Dispose()
        {
            Invoke(Service, "OnDestroy");
            string path = Path.GetFullPath(directory), parent = Path.GetFullPath(Path.GetTempPath());
            // Delete only this fixture's explicitly named, generated temp folder.
            Check(path.StartsWith(parent, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(path).StartsWith("CivilCraftGameWalletRecovery-", StringComparison.Ordinal), "Refuse cleanup outside the fixture temp directory");
            if (Directory.Exists(path)) Directory.Delete(path, true);
            UnityWebRequest.Reset();
        }
    }
}
