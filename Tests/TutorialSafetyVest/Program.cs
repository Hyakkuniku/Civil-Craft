using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

string root = Directory.GetCurrentDirectory();
while (!File.Exists(Path.Combine(root, "Assets/Script/UI/Shop/ShopManager.cs")))
    root = Directory.GetParent(root)?.FullName ?? throw new InvalidOperationException("Run inside the Unity repository.");
string player = Path.Combine(root, "Assets/Script/Player/PlayerSave/PlayerDataManager.cs");
string shop = Path.Combine(root, "Assets/Script/UI/Shop/ShopManager.cs");
string card = Path.Combine(root, "Assets/Script/UI/Shop/ShopItemUI.cs");
SyntaxNode Tree(string path) => CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot();
string Methods(string path, params string[] names) => string.Join("\n", names.Select(name => Tree(path)
    .DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.ValueText == name)
    .NormalizeWhitespace().ToFullString()));
string Property(string path, string name) => Tree(path).DescendantNodes().OfType<PropertyDeclarationSyntax>()
    .Single(p => p.Identifier.ValueText == name).NormalizeWhitespace().ToFullString();
string Constants(string path, params string[] names) => string.Join("\n", Tree(path).DescendantNodes()
    .OfType<FieldDeclarationSyntax>().Where(f => f.Declaration.Variables.Any(v => names.Contains(v.Identifier.ValueText)))
    .Select(f => f.NormalizeWhitespace().ToFullString()));

// Extract current methods, rather than copy their implementation into tests.
// Test boundaries capture writes and calls; no real save, account or API is used.
string harness = """
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
public class FusionConnectionManager { public static FusionConnectionManager Instance; public bool IsGuestSaveProtected; }
public class PlayerCosmetics { public static PlayerCosmetics Instance; public void RefreshCosmetics() { } }
public class Event<T> { public int Count; public void Invoke(T _) => Count++; }
public class PlayerDataManager {
 public static PlayerDataManager Instance;
 public PlayerData CurrentData = new PlayerData();
 public bool UsesServerCoins, CanApplyWalletData = true, SaveSucceeds = true;
 public int SaveAttempts, SaveCommits, OwnershipEvents, PurchaseEvents, CurrencyEvents, AchievementChecks, UnlockCalls;
 public string CommittedJson;
 public Action OnItemOwnershipChanged, OnCurrencyChanged, OnCosmeticLookSaved;
 public Action<string> OnShopItemPurchased;
 public PlayerDataManager() {
  Instance = this;
  OnItemOwnershipChanged = () => OwnershipEvents++;
  OnShopItemPurchased = _ => PurchaseEvents++;
  OnCurrencyChanged = () => CurrencyEvents++;
 }
 public bool TrySaveGame() {
  SaveAttempts++;
  if (!SaveSucceeds || CurrentData == null || FusionConnectionManager.Instance?.IsGuestSaveProtected == true) return false;
  SaveCommits++; CommittedJson = JsonUtility.ToJson(CurrentData); return true;
 }
 public void CheckAllAchievements() => AchievementChecks++;
 public void UnlockCosmeticReward(string id, bool equip) { UnlockCalls++; CurrentData.unlockedCosmeticIDs.Add(id); }
""" + Constants(player, "TutorialSafetyVestItemId", "TutorialSafetyVestCosmeticId") + "\n" +
    Property(player, "OwnsTutorialSafetyVest") + "\n" + Methods(player, "OwnsShopItem", "IsCosmeticUnlocked",
        "TryClaimTutorialSafetyVest", "TryPurchaseShopItem", "TryMergeWalletEntitlements", "SaveCosmeticLoadout") + """
}
public class GameWalletService {
 public static GameWalletService Instance;
 public bool UsesSharedCoins, CanSpendOnline;
 public int Purchases, Refreshes;
 public bool PurchaseCosmetic(string id, Action<bool,string> callback) { Purchases++; callback(true, null); return true; }
 public void RefreshWallet() => Refreshes++;
}
public class TutorialStep { }
public class TutorialSequence { public TutorialStep[] tutorialSteps; }
public class TutorialManager {
 public static TutorialManager Instance;
 public TutorialStep CurrentStep; public int CurrentStepIndex, Advances, Cancellations;
 public bool Playing = true;
 public bool IsPlayingSequence(TutorialSequence _) => Playing;
 public void ShowNextStep() { Advances++; CurrentStepIndex++; }
 public void CancelTutorialWithoutCompletion(TutorialSequence _) { Cancellations++; Playing = false; }
}
public class UIPanelCoordinator {
 public static UIPanelCoordinator Instance;
 public void OpenPanel(GameObject panel) => panel.SetActive(true);
 public void ClosePanel(GameObject panel) => panel.SetActive(false);
}
public class ShopManager {
 public PlayerDataManager boundPlayerData;
 public bool walletPurchasePending, IsVestShopTutorialLocked, IsGuidingVestEquip;
 public PlayerData failedSafetyVestClaimData;
 public string currencyPrefix = "", Error;
 public int Presentations, Captures, CurrencyUpdates;
 public GameObject shopPanel = new GameObject(), purchaseConfirmationPanel;
 public TMP_Text confirmationTitleText = new TMP_Text(), confirmationDescriptionText = new TMP_Text(), confirmationPriceText = new TMP_Text();
 public Image confirmationIconImage = new Image();
 public List<ShopItemData> allItems = new List<ShopItemData>();
 public List<ShopItemUI> cardPool = new List<ShopItemUI>();
 public TutorialStep safetyVestPurchaseStep = new TutorialStep(), shopCloseStep = new TutorialStep();
 public TutorialSequence onOpenTutorial;
 public bool blockClosingUntilFinalTutorialStep = true;
 public object ownedVestAdvanceCoroutine, openTutorialCoroutine;
 public ShopItemData pendingPurchase;
 public ShopCategory currentCategory;
 public Event<ShopItemData> onItemPurchased = new Event<ShopItemData>();
 public void BindPlayerData() => boundPlayerData = PlayerDataManager.Instance;
 public bool RejectPurchase(string rejection) { Error = rejection; return false; }
 public void CaptureWalletRequestScope() => Captures++;
 public object StartCoroutine(IEnumerator routine) => routine;
 public void StopCoroutine(object _) { }
 public void RefreshVisibleCards() { foreach (var card in cardPool) card.RefreshPurchaseState(); }
 public void UpdateCurrencyDisplay() => CurrencyUpdates++;
 public void InitializeIfNeeded() { }
 public void HideRetiredCurrencyControls() { }
 public void HidePurchaseFeedback() { }
 public void CloseCurrencyShopChoice() { }
 public void ShowCategory(ShopCategory _) { }
 public void UpdateTutorialPresentationForModal() { }
 public IEnumerator StartOpenTutorialWhenReady() { yield break; }
""" + Constants(shop, "SafetyVestItemId", "SafetyVestCosmeticId") + "\n" + Methods(shop,
    "IsFreeTutorialItem", "FormatPrice", "FormatItemPrice", "IsOwned", "CanInteractWithItem",
    "CanPurchase", "CompletePurchase", "FinishPurchasePresentation", "PopulateConfirmation",
    "CanBuySafetyVestNow", "IsOpenTutorialBlockingClose", "CloseShop", "AdvanceOwnedVestStep",
    "OpenShop", "CancelPendingPurchase", "TryPurchase", "ConfirmPendingPurchase", "IsSafetyVestSavedEquipped") + """
 public bool Buy(ShopItemData item) => CompletePurchase(item);
 public bool Allowed(ShopItemData item) => CanPurchase(item, out _);
 public bool CanClaimNow() => CanBuySafetyVestNow();
 public bool BlocksClose() => IsOpenTutorialBlockingClose();
 public void Preview(ShopItemData item) => PopulateConfirmation(item);
 public bool SavedEquipped() => IsSafetyVestSavedEquipped();
 public void DrainAdvance() { var routine = (IEnumerator)ownedVestAdvanceCoroutine; while (routine != null && routine.MoveNext()) { } }
}
public class ShopItemUI {
 public ShopManager owner; public ShopItemData item;
 public ShopItemData Item => item;
 public GameObject gameObject = new GameObject();
 public RectTransform PurchaseButtonTarget = new RectTransform();
 public GameObject ownedBadge = new GameObject();
 public TMP_Text priceText = new TMP_Text();
 public Image priceCoinIcon = new Image();
 public Button buyButton = new Button();
""" + Methods(card, "RefreshPurchaseState") + """
}
public static class Scenarios {
 static int checks;
 static void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
 static readonly string VestId = PlayerDataManager.TutorialSafetyVestItemId, CosmeticId = PlayerDataManager.TutorialSafetyVestCosmeticId;
 static ShopItemData Item(string id, string cosmetic = "Accessory_SafetyVest") {
  var item = new ShopItemData { itemName = "Safety Vest", price = 2500, cosmeticDefinition = new CosmeticDefinition() };
  typeof(ShopItemData).GetField("itemId", BindingFlags.Instance|BindingFlags.NonPublic).SetValue(item, id);
  typeof(CosmeticDefinition).GetField("permanentID", BindingFlags.Instance|BindingFlags.NonPublic).SetValue(item.cosmeticDefinition, cosmetic);
  return item;
 }
 static PlayerDataManager Manager(int coins, bool signedIn = true) {
  FusionConnectionManager.Instance = null; TutorialManager.Instance = null;
  var data = new PlayerDataManager { UsesServerCoins = signedIn };
  data.CurrentData.gold = coins; data.CurrentData.lifetimeGoldSpent = 10;
  data.CurrentData.walletAuthorityVersion = signedIn ? 1 : 0; data.CurrentData.walletCoinsVersion = 73;
  data.CurrentData.walletRewardOutbox.Add(new GameWalletRewardEvent { eventId = "existing_reward" });
  GameWalletService.Instance = new GameWalletService { UsesSharedCoins = signedIn };
  return data;
 }
 static ShopManager Shop(PlayerDataManager data, ShopItemData vest) {
  var shop = new ShopManager { boundPlayerData = data };
  shop.allItems.Add(vest); shop.cardPool.Add(new ShopItemUI { item = vest, owner = shop });
  return shop;
 }
 static void Tutorial(ShopManager shop) {
  shop.IsVestShopTutorialLocked = true;
  shop.onOpenTutorial = new TutorialSequence { tutorialSteps = new[] { shop.safetyVestPurchaseStep, shop.shopCloseStep } };
  TutorialManager.Instance = new TutorialManager { CurrentStep = shop.safetyVestPurchaseStep };
 }
 public static int Run() {
  foreach (bool signedIn in new[] { false, true }) foreach (bool online in new[] { false, true })
  foreach (int coins in new[] { 0, 2499, 2500, 9999 }) {
   var data = Manager(coins, signedIn); GameWalletService.Instance.CanSpendOnline = online;
   var vest = Item(VestId); var shop = Shop(data, vest); Tutorial(shop);
   Check(shop.IsFreeTutorialItem(vest) && shop.FormatItemPrice(vest) == "FREE", "Tutorial vest must display FREE irrespective of asset cost.");
   shop.cardPool[0].RefreshPurchaseState();
   Check(shop.cardPool[0].priceText.text == "FREE" && !shop.cardPool[0].priceCoinIcon.gameObject.activeSelf &&
     shop.cardPool[0].buyButton.interactable, "Offline/zero-Coin player can tap free vest without a price Coin icon.");
   Check(shop.Allowed(vest) && shop.CanClaimNow() && shop.BlocksClose(), "Claim step works without funds or wallet readiness.");
   shop.purchaseConfirmationPanel = new GameObject();
   var coinIcon = new Transform { name = "ConfirmationCoinIcon", gameObject = new GameObject() };
   shop.purchaseConfirmationPanel.Children.Add(coinIcon);
   Check(shop.TryPurchase(vest) && !data.OwnsTutorialSafetyVest && data.SaveAttempts == 0, "Opening confirmation must not grant equipment.");
   Check(shop.confirmationTitleText.text == "CLAIM SAFETY VEST?" && shop.confirmationPriceText.text == "FREE" &&
     shop.confirmationDescriptionText.text.Contains("Works offline") && !coinIcon.gameObject.activeSelf, "Confirmation explains claim and no debit.");
   shop.ConfirmPendingPurchase();
   Check(data.OwnsTutorialSafetyVest && data.SaveCommits == 1 && data.OwnershipEvents == 1 && data.PurchaseEvents == 1,
     "Claim commits shop ownership and wardrobe unlock together, once.");
   Check(data.CurrentData.gold == coins && data.CurrentData.lifetimeGoldSpent == 10 && data.CurrentData.walletCoinsVersion == 73 &&
     data.CurrentData.walletRewardOutbox.Count == 1 && data.AchievementChecks == 0 && data.CurrencyEvents == 0,
     "Free equipment must not debit/award Coins, mutate wallet version, queue rewards or trigger monetary achievements.");
   Check(GameWalletService.Instance.Purchases == 0 && shop.Captures == 0 && !shop.walletPurchasePending && vest.price == 2500,
     "No server purchase, journal operation or asset price mutation.");
   Check(!shop.SavedEquipped() && data.CurrentData.equippedHatID == "", "Claim must not auto-equip or skip SAVE LOOK.");
   Check(!shop.purchaseConfirmationPanel.activeSelf && shop.onItemPurchased.Count == 1, "Successful confirmation closes and publishes once.");
   shop.DrainAdvance(); Check(TutorialManager.Instance.Advances == 1, "Tutorial advances only after saved ownership.");
   Check(!shop.Buy(vest) && data.TryClaimTutorialSafetyVest() && data.SaveAttempts == 1 && data.OwnershipEvents == 1,
     "Repeat taps and recovery calls are idempotent.");
   shop.cardPool[0].RefreshPurchaseState();
   Check(shop.cardPool[0].priceText.text == "OWNED" && !shop.cardPool[0].buyButton.interactable, "Owned state disables further claims.");
   var saved = JsonUtility.FromJson<PlayerData>(data.CommittedJson);
   Check(saved.purchasedShopItemIds.Contains(VestId) && saved.unlockedCosmeticIDs.Contains(CosmeticId) && saved.gold == coins,
     "Ownership and unchanged Coin cache survive serialized reload.");
  }
  foreach (bool shopOwned in new[] { false, true }) foreach (bool cosmeticOwned in new[] { false, true })
  foreach (bool nullableLists in new[] { false, true }) {
   var data = Manager(0); if (nullableLists) { data.CurrentData.purchasedShopItemIds = null; data.CurrentData.unlockedCosmeticIDs = null; }
   if (shopOwned) (data.CurrentData.purchasedShopItemIds ??= new List<string>()).Add(VestId);
   if (cosmeticOwned) (data.CurrentData.unlockedCosmeticIDs ??= new List<string>()).Add(CosmeticId);
   var vest = Item(VestId); var shop = Shop(data, vest);
   Check(shop.IsOwned(vest) == (shopOwned && cosmeticOwned), "Partial old ownership must still offer repair claim.");
   Check(data.TryClaimTutorialSafetyVest() && data.OwnsTutorialSafetyVest, "Legacy missing unlock/ownership is repaired.");
   Check(data.CurrentData.purchasedShopItemIds.Count(id => id == VestId) == 1 && data.CurrentData.unlockedCosmeticIDs.Count(id => id == CosmeticId) == 1,
     "Repair never duplicates IDs.");
   Check(data.SaveCommits == (shopOwned && cosmeticOwned ? 0 : 1) && data.PurchaseEvents == (shopOwned ? 0 : 1), "Already-owned saves are neither rewritten nor refunded.");
  }
  foreach (bool nullableLists in new[] { false, true }) {
   var data = Manager(10000); data.SaveSucceeds = false;
   if (nullableLists) { data.CurrentData.purchasedShopItemIds = null; data.CurrentData.unlockedCosmeticIDs = null; }
   var shops = data.CurrentData.purchasedShopItemIds; var cosmetics = data.CurrentData.unlockedCosmeticIDs;
   var vest = Item(VestId); var shop = Shop(data, vest); Tutorial(shop);
   Check(!shop.Buy(vest) && ReferenceEquals(data.CurrentData.purchasedShopItemIds, shops) &&
     ReferenceEquals(data.CurrentData.unlockedCosmeticIDs, cosmetics), "Failed persistence rolls both collections back exactly.");
   Check(!data.OwnsTutorialSafetyVest && data.SaveCommits == 0 && data.OwnershipEvents == 0 && data.PurchaseEvents == 0 &&
     shop.onItemPurchased.Count == 0 && shop.ownedVestAdvanceCoroutine == null, "Failed claim publishes no ownership or tutorial advancement.");
   Check(!shop.CanClaimNow() && !shop.BlocksClose() && shop.Error.Contains("No Coins were charged"), "Failed save permits leaving instead of softlocking.");
   shop.CloseShop(); Check(!shop.shopPanel.activeSelf && TutorialManager.Instance.Cancellations == 1 &&
     data.CurrentData.completedLessons.Count == 0, "Exit cancels incomplete lesson, without recording completion.");
   data.SaveSucceeds = true; shop.OpenShop(); Tutorial(shop);
   Check(shop.failedSafetyVestClaimData == null && shop.CanClaimNow() && shop.Buy(vest), "Reopening allows retry after storage recovers.");
  }
  {
   var data = Manager(0); var vest = Item(VestId); var shop = Shop(data, vest); Tutorial(shop);
   FusionConnectionManager.Instance = new FusionConnectionManager { IsGuestSaveProtected = true };
   Check(!shop.CanInteractWithItem(vest) && !shop.Allowed(vest) && !data.TryClaimTutorialSafetyVest() && data.SaveAttempts == 0,
     "Visiting a multiplayer host cannot change guest ownership or write guest save.");
   Check(!shop.BlocksClose(), "Protected guest save does not trap the player.");
   FusionConnectionManager.Instance = null;
   shop.walletPurchasePending = true;
   Check(!shop.Buy(vest) && data.SaveAttempts == 0 && GameWalletService.Instance.Purchases == 0, "Do not overlap an unresolved existing paid purchase.");
   shop.walletPurchasePending = false; Check(shop.Buy(vest), "Claim works when previous purchase is resolved.");
  }
  foreach (string invalid in new[] { "otherId", "wrongCosmetic", "repeat", "wrongCategory", "wrongCosmeticCategory", "missingCosmetic" }) {
   var data = Manager(10000); var item = Item(VestId);
   if (invalid == "otherId") item = Item("other_shop_item");
   if (invalid == "wrongCosmetic") item = Item(VestId, "Accessory_SmallCap");
   if (invalid == "repeat") item.canPurchaseMultipleTimes = true;
   if (invalid == "wrongCategory") item.category = ShopCategory.Shirt;
   if (invalid == "wrongCosmeticCategory") item.cosmeticDefinition.category = CosmeticCategory.Shirt;
   if (invalid == "missingCosmetic") item.cosmeticDefinition = null;
   var shop = Shop(data, item);
   Check(!shop.IsFreeTutorialItem(item) && shop.FormatItemPrice(item) == "2,500", "No free-item bypass for " + invalid);
   Check(!shop.CanInteractWithItem(item) && !shop.Buy(item) && data.SaveAttempts == 0, "Other/misconfigured products stay online-only: " + invalid);
  }
  foreach (bool signedIn in new[] { false, true }) {
   var data = Manager(10000, signedIn); var paid = Item("other_shop_item", "Accessory_SmallCap");
   var shop = Shop(data, paid); GameWalletService.Instance.CanSpendOnline = true;
   Check(shop.Buy(paid), "Regular authorized purchases remain available.");
   Check(signedIn ? GameWalletService.Instance.Purchases == 1 && data.SaveAttempts == 0 :
     GameWalletService.Instance.Purchases == 0 && data.CurrentData.gold == 7500 && data.CurrentData.lifetimeGoldSpent == 2510,
     "Signed-in purchases still go to server; guest paid purchases still debit locally.");
   shop.Preview(paid); Check(shop.confirmationTitleText.text.StartsWith("BUY ") && shop.confirmationPriceText.text == "2,500", "Paid confirmation unchanged.");
  }
  {
   var data = Manager(0); var vest = Item(VestId); var shop = Shop(data, vest); Check(shop.Buy(vest), "Offline claim before reconnect.");
   Check(data.TryMergeWalletEntitlements(new List<GameWalletShopEntitlement> { new GameWalletShopEntitlement {
     itemId = "server_other", cosmeticId = "server_cosmetic" } }, new List<GameWalletMaterialEntitlement>()) && data.OwnsTutorialSafetyVest,
     "Verified wallet reconciliation merges and retains locally granted tutorial equipment.");
   var look = data.CurrentData.cosmeticLoadout.Clone(); look.SetAccessoryEquipped(CosmeticId, true);
   Check(data.SaveCosmeticLoadout(look) && shop.SavedEquipped() && data.CurrentData.gold == 0,
     "Real wardrobe/save-look methods can finish vest tutorial without Coins.");
   var other = Manager(0); shop.boundPlayerData = other; shop.failedSafetyVestClaimData = data.CurrentData;
   Check(!shop.IsOwned(vest) && other.CurrentData.purchasedShopItemIds.Count == 0, "Account switch cannot inherit another player's tutorial claim.");
   Check(shop.Buy(vest) && other.OwnsTutorialSafetyVest && data.OwnsTutorialSafetyVest, "Each active save claims independently.");
   other.CurrentData = null; Check(!other.TryClaimTutorialSafetyVest() && !shop.Buy(vest), "No claim without active player data.");
  }
  return checks;
 }
}
""";
var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
    .Select(path => MetadataReference.CreateFromFile(path)).ToList();
references.Add(MetadataReference.CreateFromFile(typeof(PlayerData).Assembly.Location));
var compilation = CSharpCompilation.Create("TutorialSafetyVestActualMethods_" + Guid.NewGuid().ToString("N"),
    new[] { CSharpSyntaxTree.ParseText(harness) }, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
using var compiled = new MemoryStream();
var emission = compilation.Emit(compiled);
if (!emission.Success) throw new InvalidOperationException(string.Join("\n", emission.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
Assembly methods = Assembly.Load(compiled.ToArray());
int checks;
try { checks = (int)methods.GetType("Scenarios", true).GetMethod("Run").Invoke(null, null); }
catch (TargetInvocationException exception) { throw exception.InnerException ?? exception; }

void SourceCheck(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
SourceCheck(File.ReadAllText(Path.Combine(root, "Assets/BridgeBuilder/Data/SHOP/Accessory/Accessory_SafetyVest.asset")).Contains("price: 2500"),
    "Keep user's 2500 catalog price; game tutorial override must not rewrite the asset.");
string tutorial = Methods(shop, "ConfigureSafetyVestTutorial", "PrepareSafetyVestPurchaseStep");
SourceCheck(tutorial.Contains("OwnsTutorialSafetyVest") && tutorial.Contains("FREE SAFETY VEST") && !tutorial.Contains("Short on coins"),
    "Tutorial checks wardrobe ownership and explains free claim, not insufficient funds.");
SourceCheck(Methods(player, "TryPurchaseShopItem").Contains("if (UsesServerCoins)"), "Never relax general signed-in Coin spending restriction.");
Console.WriteLine($"PASS: {checks} tutorial Safety Vest checks. No real player saves, wallet calls or purchases were used.");
