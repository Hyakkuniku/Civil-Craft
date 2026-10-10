using System;

int checks = 0;
void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new InvalidOperationException(message);
}
DateTime now = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
string expiry = now.AddMinutes(20).ToString("O");
Check(GameWalletPolicy.SameAccount("AbC123", "ABC123"), "Account comparison is case insensitive");
Check(GameWalletPolicy.IsTitleId("17FA03"), "Configured PlayFab title accepted");
Check(GameWalletPolicy.RequiresSignInForResponse(401), "Expired authentication offers Sign In instead of endless wallet retries");
Check(!GameWalletPolicy.RequiresSignInForResponse(503), "Service outages do not erase or misclassify the player session");
Check(GameWalletPolicy.UsesServerAuthority(true, false), "An expired selected account still blocks offline local spending and debug grants");
Check(!GameWalletPolicy.UsesServerAuthority(false, false), "A genuinely signed-out local save retains the guest workflow");
Check(!GameWalletPolicy.UsesServerAuthority(true, true), "An explicit guest session never uses the online wallet authority");
foreach (string badTitle in new[] { null, "", "../other", "ABC/123", "A\\B", new string('A', 17), "17FA03 " })
    Check(!GameWalletPolicy.IsTitleId(badTitle), "Wallet journal title cannot escape its directory or accept invalid configuration");
foreach (string bad in new[] { "", "not an id", "<id>", "ABC/123", new string('A', 33) })
    Check(!GameWalletPolicy.IsAccountId(bad), "Bad account identifiers must fail");
Check(!GameWalletPolicy.SameAccount("ABC123", "DEF456"), "Account switching must not reuse callbacks");
Check(GameWalletPolicy.CanUseAccountData("ABC123", "abc123", "ABC123", "ABC123", 4, 4, true, true, false, false), "Only approved account data can enter its journal");
Check(!GameWalletPolicy.CanUseAccountData("ABC123", "ABC123", "ABC123", "ABC123", 4, 4, false, true, false, false), "Unapproved cloud/save choices cannot copy an outbox");
Check(!GameWalletPolicy.CanUseAccountData("ABC123", "DEF456", "ABC123", "ABC123", 4, 4, true, true, false, false), "An outgoing account save cannot enter the incoming account journal");
Check(!GameWalletPolicy.CanUseAccountData("ABC123", null, "ABC123", "ABC123", 4, 4, true, true, false, false), "A guest save cannot enter a signed-in journal");
Check(!GameWalletPolicy.CanUseAccountData("ABC123", "ABC123", "DEF456", "ABC123", 4, 4, true, true, false, false), "SDK account switches cancel callbacks and journal copies");
Check(!GameWalletPolicy.CanUseAccountData("ABC123", "ABC123", "ABC123", "ABC123", 5, 4, true, true, false, false), "Same-account re-login invalidates old generations");
Check(!GameWalletPolicy.CanUseAccountData("ABC123", "ABC123", "ABC123", "ABC123", 4, 4, true, false, false, false), "Expired session never sends wallet operations");
Check(!GameWalletPolicy.CanUseAccountData("ABC123", "ABC123", "ABC123", "ABC123", 4, 4, true, true, true, false), "Explicit guest choice never copies or spends an account wallet");
Check(!GameWalletPolicy.CanUseAccountData("ABC123", "ABC123", "ABC123", "ABC123", 4, 4, true, true, false, true), "Host-world visitors cannot publish rewards from host progress");
bool approvedDeviceCopy = true, cloudPublishingAllowed = false;
Check(GameWalletPolicy.CanUseAccountData("ABC123", "ABC123", "ABC123", "ABC123", 4, 4, approvedDeviceCopy, true, false, false), "Approved Keep Device Copy can archive an offline reward journal");
Check(!GameWalletPolicy.CanUseAccountData("ABC123", "ABC123", "ABC123", "ABC123", 4, 4, cloudPublishingAllowed, true, false, false), "Archival approval never authorizes HTTP/spending while cloud publishing is suspended");
foreach (string path in new[] { "/login?redirect=%2Fdashboard%2Fshop", "/shop?currency=coins", "/dashboard/shop?currency=diamonds&gameLink=abcdef" })
    Check(GameWalletPolicy.IsAllowedShopUrl(GameWalletPolicy.WebsiteOrigin + path, expiry, now), "Official shop URL must open");
foreach (string url in new[] {
    "http://civil-craft.vercel.app/shop", "https://civil-craft.vercel.app.evil.test/shop",
    "https://evil.test/shop", "https://user:password@civil-craft.vercel.app/shop",
    "https://civil-craft.vercel.app:444/shop", "https://civil-craft.vercel.app/admin",
    "https://civil-craft.vercel.app/shop#secret", "javascript:alert(1)" })
    Check(!GameWalletPolicy.IsAllowedShopUrl(url, expiry, now), "Unsafe shop URL must not open");
Check(!GameWalletPolicy.IsAllowedShopUrl(GameWalletPolicy.WebsiteOrigin + "/shop", now.AddSeconds(-1).ToString("O"), now), "Expired links rejected");
Check(!GameWalletPolicy.IsAllowedShopUrl(GameWalletPolicy.WebsiteOrigin + "/shop", now.AddDays(1).ToString("O"), now), "Unbounded link lifetime rejected");
Check(GameWalletPolicy.IsReward(new GameWalletRewardEvent { kind = "achievement", sourceId = "ACH_001", eventId = "achievement:ACH_001" }), "Stable achievement event accepted");
Check(GameWalletPolicy.IsReward(new GameWalletRewardEvent { kind = "contract", sourceId = "ShopKeeper", eventId = "contract:ShopKeeper", evidence = new GameWalletRewardEvidence { finalCost = 25000, failureCount = 2, quotedAmount = 10400 } }), "Stable contract event accepted");
Check(!GameWalletPolicy.IsReward(new GameWalletRewardEvent { kind = "contract", sourceId = "ShopKeeper", eventId = Guid.NewGuid().ToString(), evidence = new GameWalletRewardEvidence() }), "Random retries cannot become new rewards");
Check(!GameWalletPolicy.IsReward(new GameWalletRewardEvent { kind = "coins", sourceId = "mint", eventId = "coins:mint" }), "Generic mint events rejected");
Check(!GameWalletPolicy.IsReward(new GameWalletRewardEvent { kind = "contract", sourceId = "ShopKeeper", eventId = "contract:ShopKeeper", evidence = new GameWalletRewardEvidence { finalCost = double.NaN } }), "Nonfinite evidence rejected");
Check(GameWalletPolicy.IsPurchase(new GameWalletPendingPurchase { operationId = Guid.NewGuid().ToString("D"), targetKind = "cosmetic", targetId = "shop_cosmetic_Hair_1" }), "Stable cosmetic request accepted");
Check(GameWalletPolicy.IsPurchase(new GameWalletPendingPurchase { operationId = Guid.NewGuid().ToString("D"), targetKind = "material", targetId = "Wood Road", contractId = "ShopKeeper" }), "Material IDs retain spaces");
Check(!GameWalletPolicy.IsPurchase(new GameWalletPendingPurchase { operationId = "bad", targetKind = "cosmetic", targetId = "shop_cosmetic_Hair_1" }), "Bad operation rejected");
Check(!GameWalletPolicy.IsPurchase(new GameWalletPendingPurchase { operationId = Guid.NewGuid().ToString("D"), targetKind = "material", targetId = "Beam" }), "Material request requires contract");
Check(GameWalletPolicy.CanMirrorVersion(10, 10), "Same current wallet version is repeatable");
Check(GameWalletPolicy.CanMirrorVersion(10, 11), "Newer reward or two-device spend snapshot accepted");
Check(!GameWalletPolicy.CanMirrorVersion(10, 2), "An old purchase receipt cannot roll back a current wallet cache");
Check(!GameWalletPolicy.CanMirrorVersion(10, -1), "Malformed wallet versions rejected");
Check(GameWalletPolicy.CanImportLegacy(0, false), "Only never-migrated selected save can request import");
Check(!GameWalletPolicy.CanImportLegacy(1, false), "An unavailable/recovered backend cannot reimport a cached server balance");
Check(!GameWalletPolicy.CanImportLegacy(0, true), "Existing server migration receipt always skips import");
string operationId = Guid.NewGuid().ToString("D");
Check(GameWalletPolicy.IsTerminalPurchaseRejection(400, operationId, operationId, true), "Durable same-operation tombstone safely releases a rejected purchase");
Check(!GameWalletPolicy.IsTerminalPurchaseRejection(400, operationId, operationId, false), "Ordinary validation errors do not abandon a potentially delayed operation");
Check(!GameWalletPolicy.IsTerminalPurchaseRejection(400, operationId, Guid.NewGuid().ToString("D"), true), "Another operation's rejection cannot release this pending purchase");
Check(!GameWalletPolicy.IsTerminalPurchaseRejection(503, operationId, operationId, true), "Service failures cannot discard pending purchases");
Check(GameWalletPolicy.TryCount(9007199254740991d, GameWalletPolicy.MaximumJsonInteger, out long large) && large == 9007199254740991L, "Maximum exact JSON integer retains every digit");
Check(GameWalletPolicy.TryCount(2147483647L, int.MaxValue, out long maxCoins) && maxCoins == int.MaxValue, "Bounded maximum Coins accepted");
Check(GameWalletPolicy.TryCount(0, int.MaxValue, out long zero) && zero == 0, "A real zero is a valid wallet balance");
Check(GameWalletPolicy.TryCount(0UL, int.MaxValue, out long unsignedZero) && unsignedZero == 0, "PlayFab's unsigned zero is a real wallet balance");
Check(GameWalletPolicy.TryCount(25UL, GameWalletPolicy.MaximumJsonInteger, out long unsignedDiamonds) && unsignedDiamonds == 25, "PlayFab's unsigned positive Diamond balance accepted");
Check(GameWalletPolicy.TryCount((ulong)int.MaxValue, int.MaxValue, out long unsignedCoins) && unsignedCoins == int.MaxValue, "Unsigned Coins retain their int save limit");
Check(GameWalletPolicy.TryCount((ulong)GameWalletPolicy.MaximumJsonInteger, GameWalletPolicy.MaximumJsonInteger, out long unsignedMaximum) && unsignedMaximum == GameWalletPolicy.MaximumJsonInteger, "Unsigned exact JSON maximum retains every digit");
Check(GameWalletPolicy.TryCount(25U, int.MaxValue, out long smallUnsigned) && smallUnsigned == 25, "Unsigned int balances retain normal bounds");
foreach (object unsignedOverflow in new object[] { 2147483648UL, uint.MaxValue, ulong.MaxValue })
    Check(!GameWalletPolicy.TryCount(unsignedOverflow, int.MaxValue, out _), "Unsigned overflow must be rejected before a signed conversion");
foreach (object jsonOverflow in new object[] { (ulong)GameWalletPolicy.MaximumJsonInteger + 1UL, (ulong)long.MaxValue, ulong.MaxValue })
    Check(!GameWalletPolicy.TryCount(jsonOverflow, long.MaxValue, out _), "A caller's wider bound cannot permit inexact or overflowing unsigned JSON integers");
foreach (object invalidBoundValue in new object[] { 0UL, 0U, 0L, 0d })
    Check(!GameWalletPolicy.TryCount(invalidBoundValue, -1, out long rejected) && rejected == 0, "Negative count bounds fail closed for every numeric representation");
foreach (object bad in new object[] { null, true, "1", -1, 0.5d, double.NaN, double.PositiveInfinity, 2147483648L, 0.1m })
    Check(!GameWalletPolicy.TryCount(bad, int.MaxValue, out _), "Malformed or overflowing balances cannot become available currency");

var reedCapture = new GameWalletContractEvidence {
    contractId = "ReedsContract", hasExpQuote = true, quotedExp = 500,
    evidence = new GameWalletRewardEvidence { finalCost = 50000, failureCount = 0, quotedAmount = 10500 }
};
var restarted = GameWalletPolicy.ResolvePayout(reedCapture, true, 500, 500, false, 0, 0, 500, 500);
Check(restarted.gold == 10500 && restarted.exp == 500 && restarted.fromCapture, "Reed's under-budget payout survives restart instead of becoming its 500 base fallback");
var reedReward = GameWalletPolicy.ContractReward("ReedsContract", reedCapture, restarted, 50000, false);
Check(GameWalletPolicy.IsSendableReward(reedReward) && reedReward.evidence.quotedAmount == 10500,
    "The submitted quote belongs to the persisted cost/failure attempt, not the NPC's volatile fallback");
Check(reedReward.evidence.finalCost == 50000 && reedReward.evidence.failureCount == 0,
    "Restart preserves the matching authoritative-formula evidence");
var zeroCapture = new GameWalletContractEvidence {
    contractId = "ReedsContract", hasExpQuote = true, quotedExp = 0,
    evidence = new GameWalletRewardEvidence { finalCost = 120000, failureCount = 3, quotedAmount = 0 }
};
var actualZero = GameWalletPolicy.ResolvePayout(zeroCapture, true, 500, 500, false, 0, 0, 500, 500);
Check(actualZero.gold == 0 && actualZero.exp == 0 && actualZero.fromCapture,
    "Captured zero Gold and EXP are genuine values, never replaced with base rewards");
var zeroReward = GameWalletPolicy.ContractReward("ReedsContract", zeroCapture, actualZero, 120000, false);
Check(GameWalletPolicy.IsSendableReward(zeroReward) && zeroReward.evidence.quotedAmount == 0 && zeroReward.evidence.failureCount == 3,
    "Over-budget/failure zero reward retains its original failure count");
var pendingZero = GameWalletPolicy.ResolvePayout(null, true, 0, 0, false, 0, 0, 500, 500);
Check(pendingZero.gold == 0 && pendingZero.exp == 0 && pendingZero.source == "pending",
    "A ready quest's saved zero is distinct from missing payout data");
var memoryZero = GameWalletPolicy.ResolvePayout(null, false, 0, 0, true, 0, 0, 500, 500);
Check(memoryZero.gold == 0 && memoryZero.exp == 0 && memoryZero.source == "memory",
    "Dictionary presence keeps a real in-memory zero instead of treating it as a cache miss");
var missingQuote = GameWalletPolicy.ResolvePayout(null, false, 0, 0, false, 0, 0, 500, 500);
Check(missingQuote.gold == 500 && missingQuote.exp == 500 && missingQuote.source == "base",
    "Only genuinely absent persisted/pending/memory values use the base display fallback");
var savedPending = GameWalletPolicy.ResolvePayout(null, true, 10500, 500, true, 500, 500, 500, 500);
Check(savedPending.gold == 10500 && savedPending.exp == 500 && savedPending.source == "pending",
    "Reopening a Collect dialog cannot overwrite a saved ready payout with NPC fallback values");
var olderCapture = new GameWalletContractEvidence {
    contractId = "ReedsContract", evidence = new GameWalletRewardEvidence { finalCost = 50000, quotedAmount = 10500 }
};
Check(GameWalletPolicy.ResolvePayout(olderCapture, true, 500, 0, false, 0, 0, 500, 500).exp == 0,
    "Old captures without an EXP-presence field preserve a ready task's explicit zero EXP");
var tutorial = GameWalletPolicy.ResolvePayout(reedCapture, true, 10500, 500, true, 10500, 500, 500, 500, true);
Check(tutorial.gold == 0 && tutorial.exp == 0, "Explicit tutorial collection remains reward-free");
var tutorialReward = GameWalletPolicy.ContractReward("ReedsContract", reedCapture, tutorial, 50000, true);
Check(GameWalletPolicy.IsSendableReward(tutorialReward) && tutorialReward.evidence.tutorial && tutorialReward.evidence.quotedAmount == 0,
    "Tutorial zero is explicit evidence, not an accidental base-reward substitution");
var legacy = GameWalletPolicy.ContractReward("ReedsContract", null, savedPending, 50000, false);
Check(GameWalletPolicy.IsReward(legacy) && legacy.held && legacy.holdReason == "legacy-evidence-missing",
    "Legacy unclaimed jobs without reliable capture remain durable, valid recovery records");
Check(!GameWalletPolicy.IsSendableReward(legacy) && legacy.evidence == null,
    "Unknown legacy failures are never invented as zero or posted as an unverified reward");
Check(legacy.eventId == "contract:ReedsContract" && legacy.legacyQuotedAmount == 10500 && legacy.legacyExpAmount == 500,
    "Holding a legacy reward preserves its canonical ID and original displayed value");
legacy.legacyQuotedAmount = -1;
Check(!GameWalletPolicy.IsReward(legacy), "Malformed held reward amounts fail closed");
legacy.legacyQuotedAmount = 10500;
legacy.holdReason = "grant-anything";
Check(!GameWalletPolicy.IsReward(legacy), "Only the exact supported local hold reason is accepted");
legacy.holdReason = "legacy-evidence-missing";
legacy.kind = "achievement"; legacy.sourceId = "ACH_001"; legacy.eventId = "achievement:ACH_001";
Check(!GameWalletPolicy.IsReward(legacy), "The legacy contract recovery marker cannot be applied to another reward kind");
var missingBridgeLegacy = GameWalletPolicy.ContractReward("ReedsContract", null, savedPending, 0, false);
Check(GameWalletPolicy.IsReward(missingBridgeLegacy) && missingBridgeLegacy.held && missingBridgeLegacy.evidence == null,
    "A missing legacy bridge cannot fabricate a financial quote or throw while constructing its recovery record");
var missingBridgeTutorial = GameWalletPolicy.ContractReward("ReedsContract", null, tutorial, 0, true);
Check(GameWalletPolicy.IsSendableReward(missingBridgeTutorial) && missingBridgeTutorial.evidence.quotedAmount == 0,
    "Explicit zero tutorial fallback never invents a nonzero legacy payout");
var prototypeQuote = new GameWalletRewardEvent {
    kind = "contract", sourceId = "ReedsContract", eventId = "contract:ReedsContract",
    evidence = new GameWalletRewardEvidence { finalCost = 50000, failureCount = 0, quotedAmount = 500 }
};
var repairedQuote = GameWalletPolicy.RepairCapturedRewardQuote(prototypeQuote, reedCapture);
Check(!ReferenceEquals(repairedQuote, prototypeQuote) && repairedQuote.evidence.quotedAmount == 10500,
    "An exact captured attempt repairs the old prototype's stale 500 quote to its saved 10500 payout");
Check(prototypeQuote.evidence.quotedAmount == 500 && repairedQuote.eventId == prototypeQuote.eventId,
    "Quote repair does not mutate the replaceable source outbox or create another reward ID");
var changedCostCapture = new GameWalletContractEvidence {
    contractId = "ReedsContract", evidence = new GameWalletRewardEvidence { finalCost = 49000, failureCount = 0, quotedAmount = 10700 }
};
Check(ReferenceEquals(GameWalletPolicy.RepairCapturedRewardQuote(prototypeQuote, changedCostCapture), prototypeQuote),
    "A later redesign's different cost cannot rewrite an earlier pending reward");
var changedFailureCapture = new GameWalletContractEvidence {
    contractId = "ReedsContract", evidence = new GameWalletRewardEvidence { finalCost = 50000, failureCount = 1, quotedAmount = 10450 }
};
Check(ReferenceEquals(GameWalletPolicy.RepairCapturedRewardQuote(prototypeQuote, changedFailureCapture), prototypeQuote),
    "Unknown or different failure history cannot be invented during quote repair");
var changedTutorialCapture = new GameWalletContractEvidence {
    contractId = "ReedsContract", evidence = new GameWalletRewardEvidence { finalCost = 50000, failureCount = 0, quotedAmount = 0, tutorial = true }
};
Check(ReferenceEquals(GameWalletPolicy.RepairCapturedRewardQuote(prototypeQuote, changedTutorialCapture), prototypeQuote),
    "Tutorial mismatch cannot change a financial reward");
Check(ReferenceEquals(GameWalletPolicy.RepairCapturedRewardQuote(missingBridgeLegacy, reedCapture), missingBridgeLegacy),
    "A held unknown-history record is never automatically unheld by quote repair");
var otherCapture = new GameWalletContractEvidence {
    contractId = "VancesContract", evidence = new GameWalletRewardEvidence { finalCost = 50000, failureCount = 0, quotedAmount = 20500 }
};
Check(ReferenceEquals(GameWalletPolicy.RepairCapturedRewardQuote(prototypeQuote, otherCapture), prototypeQuote),
    "Another contract's capture cannot repair this reward");
var fieldJsonOptions = new System.Text.Json.JsonSerializerOptions { IncludeFields = true };
string capturedJson = System.Text.Json.JsonSerializer.Serialize(zeroCapture, fieldJsonOptions);
var restoredZero = System.Text.Json.JsonSerializer.Deserialize<GameWalletContractEvidence>(capturedJson, fieldJsonOptions);
Check(restoredZero.hasExpQuote && GameWalletPolicy.ResolvePayout(restoredZero, false, 0, 0, false, 0, 0, 500, 500).exp == 0,
    "The persisted EXP presence marker survives field-based save roundtrips");
string heldJson = System.Text.Json.JsonSerializer.Serialize(missingBridgeLegacy, fieldJsonOptions);
var restoredHeld = System.Text.Json.JsonSerializer.Deserialize<GameWalletRewardEvent>(heldJson, fieldJsonOptions);
Check(GameWalletPolicy.IsReward(restoredHeld) && !GameWalletPolicy.IsSendableReward(restoredHeld) && restoredHeld.legacyQuotedAmount == 10500,
    "A held legacy recovery record survives journal serialization without becoming an HTTP grant");
string policyProjectRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "../../../../../"));
string payoutManagerSource = System.IO.File.ReadAllText(System.IO.Path.Combine(policyProjectRoot,
    "Assets/Script/Player/PlayerSave/PlayerDataManager.cs"));
int captureStart = payoutManagerSource.IndexOf("public void CaptureWalletContractReward(", StringComparison.Ordinal);
int nextMethod = payoutManagerSource.IndexOf("private GameWalletContractEvidence FindWalletContractEvidence", captureStart, StringComparison.Ordinal);
string captureMethod = payoutManagerSource.Substring(captureStart, nextMethod - captureStart);
int normalizationPosition = captureMethod.IndexOf("contractId = NormalizeContractIdentifier(contractId);", StringComparison.Ordinal);
int paidGuardPosition = captureMethod.IndexOf("if (HasContractCompletionRecord(contractId)) return;", StringComparison.Ordinal);
int capturedMutationPosition = captureMethod.IndexOf("CurrentData.walletContractEvidence.RemoveAll", StringComparison.Ordinal);
Check(normalizationPosition >= 0 && paidGuardPosition > normalizationPosition && capturedMutationPosition > paidGuardPosition,
    "The actual capture entry point guards paid redesigns before replacing the original queued payout evidence");
Console.WriteLine($"PASS: {checks} game wallet policy checks");
