using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using PlayFab;
using PlayFab.Json;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

/// <summary>
/// Account-bound client of the authoritative website wallet. The game save is
/// a balance/entitlement cache, never a source of signed-in debits or credits.
/// Its encrypted operation journal survives replacing a story/cloud save.
/// </summary>
[DisallowMultipleComponent]
public sealed class GameWalletService : MonoBehaviour
{
    private sealed class Reply {
        public long code;
        public IDictionary<string, object> body;
        public bool saveSelectionBlocked;
    }
    private PlayerDataManager playerData;
    private CloudSaveManager cloud;
    private GameWalletJournal journal;
    private string journalPath;
    private string accountId;
    private string titleId;
    private int generation = -1;
    private bool journalAvailable;
    private bool journalDirty;
    private bool inFlight;
    private bool refreshRequested;
    private bool observedLegacySourceReady;
    private float nextAttempt;
    private float nextJournalAttempt;
    private string shopCurrency;
    private Action<bool, string> shopCallback;
    private int? lifetimeEarned;
    private int? lifetimeSpent;
    private readonly Dictionary<string, Action<bool, string>> purchaseCallbacks =
        new Dictionary<string, Action<bool, string>>(StringComparer.Ordinal);

    public static GameWalletService Instance => PlayerDataManager.Instance != null
        ? PlayerDataManager.Instance.GetComponent<GameWalletService>() : null;
    // A selected signed-in account must not fall back to offline local spending
    // when its HTTP/session/wallet becomes unavailable.
    public bool HasSignedInAccount => GameWalletPolicy.UsesServerAuthority(cloud != null && cloud.IsAccountActive,
        PlayerPrefs.GetInt("LoginChoice", 0) == 1);
    public bool RequiresSignIn { get; private set; }
    public bool CoinsAvailable { get; private set; }
    public bool DiamondsAvailable { get; private set; }
    public long DiamondBalance { get; private set; }
    public int CoinBalance { get; private set; }
    public long WalletVersion { get; private set; }
    public string WalletStatus { get; private set; } = "Sign in to use the online wallet.";
    public bool IsRefreshing => inFlight;
    public bool CanSpendOnline => !RequiresSignIn && !inFlight && CoinsAvailable && journalAvailable && IsCurrentSession(accountId, generation) &&
        journal != null && !journal.purchases.Exists(p => p != null && !p.completed);

    public static GameWalletService EnsureAttached(PlayerDataManager data)
    {
        if (data == null) return null;
        GameWalletService service = data.GetComponent<GameWalletService>();
        return service != null ? service : data.gameObject.AddComponent<GameWalletService>();
    }

    private void Awake()
    {
        playerData = GetComponent<PlayerDataManager>();
        cloud = GetComponent<CloudSaveManager>();
        if (playerData != null) playerData.OnSaveCommitted += OnSaveCommitted;
    }

    private void OnDestroy()
    {
        if (playerData != null) playerData.OnSaveCommitted -= OnSaveCommitted;
    }

    public void RefreshWallet() { refreshRequested = true; nextAttempt = 0f; }
    private void OnApplicationFocus(bool focused) { if (focused) RefreshWallet(); }
    private void OnApplicationPause(bool paused) { if (!paused) RefreshWallet(); }

    private void Update()
    {
        if (cloud == null) cloud = GetComponent<CloudSaveManager>();
        string selected = HasSignedInAccount ? cloud.ActiveAccountId : null;
        int selectedGeneration = cloud != null ? cloud.SessionGeneration : -1;
        if (!string.Equals(selected, accountId, StringComparison.OrdinalIgnoreCase) || generation != selectedGeneration)
            SelectSession(selected, selectedGeneration);
        bool sourceReady = cloud != null && cloud.CanImportLegacyWallet;
        if (sourceReady && !observedLegacySourceReady && !CoinsAvailable && playerData?.CurrentData != null &&
            playerData.CurrentData.walletAuthorityVersion == 0) RefreshWallet();
        observedLegacySourceReady = sourceReady;
        if (HasSignedInAccount && (!PlayFabClientAPI.IsClientLoggedIn() ||
            !GameWalletPolicy.SameAccount(PlayFabSettings.staticPlayer.PlayFabId, accountId) ||
            !string.Equals(PlayFabSettings.staticSettings.TitleId, titleId, StringComparison.OrdinalIgnoreCase))) RequireSignIn();
        // A player-approved device copy can continue earning offline even when
        // cloud reconciliation/publishing is suspended. Archive only this
        // approved account's outbox, without authorizing any network request.
        if (journalAvailable && Time.unscaledTime >= nextJournalAttempt && CanArchiveJournal(accountId, generation))
            MergeOutbox();
        if (inFlight || RequiresSignIn || !IsCurrentSession(accountId, generation) || !journalAvailable ||
            Time.unscaledTime < nextAttempt) return;
        if (refreshRequested || HasSendableRewards() || journal.purchases.Exists(p => !p.completed))
        {
            refreshRequested = false;
            StartCoroutine(Synchronize(accountId, generation));
        }
    }

    private bool IsCurrentSession(string expectedAccount, int expectedGeneration)
    {
        return cloud != null && playerData != null &&
            string.Equals(PlayFabSettings.staticSettings.TitleId, titleId, StringComparison.OrdinalIgnoreCase) && GameWalletPolicy.CanUseAccountData(
            cloud.ActiveAccountId, playerData.ActiveAccountId, PlayFabSettings.staticPlayer.PlayFabId,
            expectedAccount, cloud.SessionGeneration, expectedGeneration, cloud.CanPublishDashboard,
            PlayFabClientAPI.IsClientLoggedIn(), PlayerPrefs.GetInt("LoginChoice", 0) == 1,
            FusionConnectionManager.Instance != null && FusionConnectionManager.Instance.IsGuestSaveProtected);
    }

    private bool CanArchiveJournal(string expectedAccount, int expectedGeneration)
    {
        return cloud != null && playerData != null &&
            string.Equals(PlayFabSettings.staticSettings.TitleId, titleId, StringComparison.OrdinalIgnoreCase) && GameWalletPolicy.CanUseAccountData(
            cloud.ActiveAccountId, playerData.ActiveAccountId, PlayFabSettings.staticPlayer.PlayFabId,
            expectedAccount, cloud.SessionGeneration, expectedGeneration, cloud.IsSessionReady && !cloud.HasConflict,
            PlayFabClientAPI.IsClientLoggedIn(), PlayerPrefs.GetInt("LoginChoice", 0) == 1,
            FusionConnectionManager.Instance != null && FusionConnectionManager.Instance.IsGuestSaveProtected);
    }

    private void SelectSession(string selectedAccount, int selectedGeneration)
    {
        accountId = selectedAccount;
        generation = selectedGeneration;
        titleId = PlayFabSettings.staticSettings.TitleId;
        journal = null;
        journalAvailable = false;
        journalDirty = false;
        CoinsAvailable = DiamondsAvailable = false;
        RequiresSignIn = false;
        observedLegacySourceReady = false;
        lifetimeEarned = lifetimeSpent = null;
        WalletVersion = GameWalletPolicy.IsAccountId(selectedAccount) && playerData?.CurrentData != null
            ? Math.Max(0, playerData.CurrentData.walletCoinsVersion) : 0;
        shopCurrency = null;
        shopCallback = null;
        purchaseCallbacks.Clear();
        refreshRequested = true;
        nextAttempt = 0f;
        nextJournalAttempt = 0f;
        WalletStatus = "Online wallet unavailable. Signed-in spending requires a connection.";
        if (!GameWalletPolicy.IsAccountId(accountId) || !GameWalletPolicy.IsTitleId(titleId)) return;
        journalPath = Path.Combine(Application.persistentDataPath, "WalletJournals", titleId, accountId.ToUpperInvariant() + ".json");
        try
        {
            if (File.Exists(journalPath))
            {
                if (!SaveEncryption.TryDecryptOrReadLegacy(File.ReadAllText(journalPath), JournalPurpose(),
                        out string serialized, out bool wasLegacy, out _) || wasLegacy)
                    throw new InvalidDataException();
                journal = JsonUtility.FromJson<GameWalletJournal>(serialized);
                if (journal == null || journal.schemaVersion != 1 ||
                    !GameWalletPolicy.SameAccount(journal.accountId, accountId) || journal.titleId != titleId ||
                    journal.rewards == null || journal.purchases == null || journal.deliveredRewardIds == null ||
                    journal.rewards.Exists(r => !GameWalletPolicy.IsReward(r)) ||
                    journal.purchases.Exists(p => !GameWalletPolicy.IsPurchase(p))) throw new InvalidDataException();
            }
            else { journal = new GameWalletJournal { titleId = titleId, accountId = accountId }; journalDirty = true; }
            journalAvailable = true;
            MergeOutbox();
        }
        catch (Exception)
        {
            // Never silently reset a receipt/operation journal: an uncertain
            // operation must retain its ID and original account for recovery.
            WalletStatus = "The account wallet journal needs recovery. Online spending is disabled.";
            Debug.LogWarning("[GameWallet] Account operation journal could not be opened; it was preserved.");
        }
        playerData?.NotifyWalletChanged();
    }

    private string JournalPurpose() => "game-wallet-journal:" + titleId + ":" + accountId.ToUpperInvariant();

    private bool SaveJournal()
    {
        if (!journalAvailable || journal == null || string.IsNullOrEmpty(journalPath)) return false;
        journalDirty = true;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(journalPath));
            string temporary = journalPath + ".tmp";
            File.WriteAllText(temporary, SaveEncryption.Encrypt(JsonUtility.ToJson(journal), JournalPurpose()));
            if (File.Exists(journalPath)) File.Replace(temporary, journalPath, journalPath + ".bak");
            else File.Move(temporary, journalPath);
            journalDirty = false;
            return true;
        }
        catch (Exception)
        {
            WalletStatus = "Wallet operations could not be saved. Please retry when device storage is available.";
            Debug.LogWarning("[GameWallet] Could not persist the account operation journal.");
            return false;
        }
    }

    private void OnSaveCommitted()
    {
        if (journalAvailable && CanArchiveJournal(accountId, generation) && Time.unscaledTime >= nextJournalAttempt) MergeOutbox();
    }

    private bool MergeOutbox()
    {
        if (!CanArchiveJournal(accountId, generation)) return false;
        List<GameWalletRewardEvent> source = playerData?.CurrentData?.walletRewardOutbox;
        if (journal == null) return false;
        if (source == null) source = new List<GameWalletRewardEvent>();
        if (source.Exists(reward => reward != null && journal.deliveredRewardIds.Contains(reward.eventId)) &&
            !playerData.AcknowledgeWalletRewards(journal.deliveredRewardIds)) nextJournalAttempt = Time.unscaledTime + 30f;
        bool changed = false;
        for (int index = 0; index < journal.rewards.Count; index++)
        {
            GameWalletRewardEvent reward = journal.rewards[index];
            GameWalletRewardEvent repaired = playerData.RepairCapturedWalletRewardQuote(reward);
            if (ReferenceEquals(repaired, reward)) continue;
            journal.rewards[index] = repaired;
            changed = true;
        }
        foreach (GameWalletRewardEvent reward in source)
        {
            if (!GameWalletPolicy.IsReward(reward) || journal.deliveredRewardIds.Contains(reward.eventId) ||
                journal.rewards.Exists(r => r.eventId == reward.eventId)) continue;
            journal.rewards.Add(playerData.RepairCapturedWalletRewardQuote(reward));
            changed = true;
        }
        if (changed)
        {
            journalDirty = true;
            RefreshWallet();
        }
        if (journalDirty && !SaveJournal())
        {
            nextJournalAttempt = Time.unscaledTime + 30f;
            CoinsAvailable = false;
            return false;
        }
        return true;
    }

    private IEnumerator Request(string method, string path, object payload, string expectedAccount, int expectedGeneration, Reply reply)
    {
        if (RequiresSignIn || !IsCurrentSession(expectedAccount, expectedGeneration))
        { InvalidateChangedAccount(expectedAccount, expectedGeneration); yield break; }
        // Recheck at dispatch as well as at the caller: Unity can resume a
        // nested coroutine after a save choice/account changed.
        if (path == "/api/game/wallet/import" && !cloud.CanImportLegacyWallet)
        { reply.saveSelectionBlocked = true; yield break; }
        string ticket = PlayFabSettings.staticPlayer.ClientSessionTicket;
        if (string.IsNullOrEmpty(ticket)) yield break;
        using (var request = new UnityWebRequest(GameWalletPolicy.WebsiteOrigin + path, method))
        {
            request.downloadHandler = new DownloadHandlerBuffer();
            if (payload != null)
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(PlayFabSimpleJson.SerializeObject(payload)));
                request.SetRequestHeader("Content-Type", "application/json");
            }
            request.SetRequestHeader("Authorization", "Bearer " + ticket);
            request.timeout = 15;
            request.redirectLimit = 0;
            yield return request.SendWebRequest();
            if (!IsCurrentSession(expectedAccount, expectedGeneration))
            { InvalidateChangedAccount(expectedAccount, expectedGeneration); yield break; }
            reply.code = request.responseCode;
            if (GameWalletPolicy.RequiresSignInForResponse(reply.code)) RequireSignIn();
            else if (reply.code == 200) RequiresSignIn = false;
            if (request.downloadedBytes > 1024 * 1024) yield break;
            try { reply.body = PlayFabSimpleJson.DeserializeObject(request.downloadHandler.text) as IDictionary<string, object>; }
            catch (Exception) { reply.body = null; }
        }
    }

    private void Fail(string message = "Online wallet unavailable. Your queued rewards and operations are preserved.")
    {
        CoinsAvailable = DiamondsAvailable = false;
        WalletStatus = RequiresSignIn ? "Your session has expired. Sign in again to use the shop." : message;
        nextAttempt = Time.unscaledTime + 30f;
        playerData?.NotifyWalletChanged();
    }

    private void InvalidateChangedAccount(string expectedAccount, int expectedGeneration)
    {
        if (cloud != null && playerData != null && cloud.SessionGeneration == expectedGeneration &&
            string.Equals(PlayFabSettings.staticSettings.TitleId, titleId, StringComparison.OrdinalIgnoreCase) &&
            GameWalletPolicy.SameAccount(cloud.ActiveAccountId, expectedAccount) &&
            GameWalletPolicy.SameAccount(playerData.ActiveAccountId, expectedAccount) &&
            GameWalletPolicy.SameAccount(PlayFabSettings.staticPlayer.PlayFabId, expectedAccount) &&
            PlayFabClientAPI.IsClientLoggedIn() && PlayerPrefs.GetInt("LoginChoice", 0) != 1) return;
        CoinsAvailable = DiamondsAvailable = false;
        WalletStatus = "Your account session changed. Refresh balances after signing in.";
        playerData?.NotifyWalletChanged();
    }

    private static bool IsVerifiedCoinResponse(Reply reply) => reply != null && reply.body != null &&
        (reply.code == 200 || !string.IsNullOrWhiteSpace(Text(reply.body, "error")) && Text(reply.body, "error").Length <= 2000);

    private void FailCoins(string message, long responseCode = 200, bool coinResponseVerified = true)
    {
        // Only a Coin-specific failure may retain the independent Diamond
        // value already verified for this account. Transport/auth failures
        // and invalid whole-wallet responses still invalidate both currencies.
        if (!coinResponseVerified || RequiresSignIn || !IsCurrentSession(accountId, generation) || responseCode == 0 ||
            responseCode == 401 || responseCode == 403 || responseCode == 408 ||
            responseCode == 502 || responseCode == 504)
        { Fail(message); return; }
        CoinsAvailable = false;
        WalletStatus = message + (DiamondsAvailable ? " Your synchronized Diamonds balance remains available." : "");
        nextAttempt = Time.unscaledTime + 30f;
        playerData?.NotifyWalletChanged();
    }

    private void RequireSignIn()
    {
        if (RequiresSignIn) return;
        RequiresSignIn = true;
        CoinsAvailable = DiamondsAvailable = false;
        WalletStatus = "Your session has expired. Sign in again to use the shop.";
        playerData?.NotifyWalletChanged();
    }

    private IEnumerator Synchronize(string expectedAccount, int expectedGeneration)
    {
        inFlight = true;
        try
        {
            // Startup may have selected an account before its save choice was
            // approved. Copy the approved outbox before building import's
            // historical-consumption baseline, never during that selection.
            if (!IsCurrentSession(expectedAccount, expectedGeneration) || !MergeOutbox())
            { Fail(); yield break; }
            if (shopCurrency != null)
            {
                string currency = shopCurrency;
                Action<bool, string> callback = shopCallback;
                shopCurrency = null;
                shopCallback = null;
                var link = new Reply();
                yield return Request("POST", "/api/game/shop-link", new Dictionary<string, object> { { "currency", currency } }, expectedAccount, expectedGeneration, link);
                if (!IsCurrentSession(expectedAccount, expectedGeneration)) yield break;
                bool valid = link.code == 200 && link.body != null &&
                    GameWalletPolicy.IsAllowedShopUrl(Text(link.body, "url"), Text(link.body, "expiresAt"), DateTime.UtcNow);
                if (valid) Application.OpenURL(Text(link.body, "url"));
                callback?.Invoke(valid, valid ? null : RequiresSignIn ? WalletStatus : "The website shop link is unavailable. Please try again.");
            }
            var wallet = new Reply();
            yield return Request("GET", "/api/game/wallet", null, expectedAccount, expectedGeneration, wallet);
            if (!IsCurrentSession(expectedAccount, expectedGeneration)) yield break;
            if (wallet.code != 200 || !TryWallet(wallet.body, out bool ready)) { Fail(); yield break; }
            if (!ready)
            {
                if (!GameWalletPolicy.CanImportLegacy(playerData.CurrentData.walletAuthorityVersion, ready))
                {
                    FailCoins("The server Coin wallet needs recovery. An existing cached balance will not be imported again.");
                    yield break;
                }
                if (!cloud.CanImportLegacyWallet)
                {
                    FailCoins("Coins are awaiting your account-save selection. Reconnect to finish checking your online save, or sign in and choose a save.");
                    yield break;
                }
                if (HasUnreviewedLegacyRewardOverlap())
                {
                    // A preserved device journal is not proof that the newly
                    // selected legacy save did not already pay this reward.
                    // Guessing could duplicate its opening credit or lose it.
                    FailCoins("Coins need a legacy-save review: a preserved queued reward is also completed in the selected save without its pending record. No import was sent.");
                    yield break;
                }
                if (!cloud.TryBeginLegacyWalletImport(expectedAccount, expectedGeneration))
                {
                    FailCoins("Coins are waiting for your selected account save to finish synchronizing. Please retry.");
                    yield break;
                }
                var imported = new Reply();
                try
                {
                    // Freeze a deep-copied selected-save snapshot while cloud
                    // reconciliation is reserved for this account/generation.
                    yield return Request("POST", "/api/game/wallet/import", ImportSnapshot(), expectedAccount, expectedGeneration, imported);
                }
                finally { cloud.EndLegacyWalletImport(expectedAccount, expectedGeneration); }
                if (!IsCurrentSession(expectedAccount, expectedGeneration)) yield break;
                if (imported.saveSelectionBlocked)
                { FailCoins("Coins are awaiting confirmation of your selected account save. Please reconnect and retry."); yield break; }
                if (imported.code != 200)
                { FailCoins("Coin migration is unavailable. Your selected save is unchanged.", imported.code, IsVerifiedCoinResponse(imported)); yield break; }
                if (!TryWallet(imported.body, out ready))
                { Fail("The wallet response could not be verified. Your selected save is unchanged."); yield break; }
                if (!ready)
                { FailCoins("Coin migration is awaiting confirmation. Your selected save is unchanged."); yield break; }
            }
            if (!MirrorWallet()) { FailCoins("Coins are online, but their local cache could not be saved. Please retry."); yield break; }
            var entitlements = new Reply();
            yield return Request("GET", "/api/game/entitlements", null, expectedAccount, expectedGeneration, entitlements);
            if (!IsCurrentSession(expectedAccount, expectedGeneration)) yield break;
            if (entitlements.code != 200 || !ApplyEntitlements(entitlements.body)) { FailCoins("Online Coin ownership could not be synchronized. Please retry.", entitlements.code, IsVerifiedCoinResponse(entitlements)); yield break; }
            if (!MergeOutbox())
            { FailCoins("Coin operations must be saved before synchronization. Please retry when device storage is available."); yield break; }
            // Recover a possibly debited operation before submitting earnings.
            // A rejected reward must not strand already-purchased ownership.
            GameWalletPendingPurchase pending = journal.purchases.Find(p => !p.completed);
            if (pending != null)
            {
                var purchase = new Reply();
                yield return Request("GET", "/api/game/purchases/" + pending.operationId, null, expectedAccount, expectedGeneration, purchase);
                if (!IsCurrentSession(expectedAccount, expectedGeneration)) yield break;
                if (purchase.code == 404)
                {
                    purchase = new Reply();
                    var payload = new Dictionary<string, object> { { "operationId", pending.operationId }, { "targetKind", pending.targetKind }, { "targetId", pending.targetId } };
                    if (pending.targetKind == "material") payload.Add("contractId", pending.contractId);
                    yield return Request("POST", "/api/game/purchases", payload, expectedAccount, expectedGeneration, purchase);
                    if (!IsCurrentSession(expectedAccount, expectedGeneration)) yield break;
                }
                if (purchase.body != null && purchase.body.TryGetValue("terminal", out object terminal) &&
                    terminal is bool rejected && GameWalletPolicy.IsTerminalPurchaseRejection(purchase.code,
                        pending.operationId, Text(purchase.body, "operationId"), rejected))
                {
                    // Only a durable server tombstone proves that this UUID
                    // can never later debit. An ordinary 400 stays pending.
                    pending.completed = true;
                    pending.status = "invalid_target";
                    if (!SaveJournal()) { pending.completed = false; FailCoins("The Coin operation journal could not be saved. Please retry."); yield break; }
                    CompletePurchaseCallback(pending.operationId, false, "This shop item is unavailable. No Coins were spent.");
                    WalletStatus = "Online Coins synchronized. The unavailable purchase was not charged.";
                    nextAttempt = Time.unscaledTime + 15f;
                    yield break;
                }
                string status = Text(purchase.body, "status");
                if (purchase.code != 200 || status != "completed" && status != "already_owned" && status != "insufficient_funds" ||
                    Text(purchase.body, "operationId") != pending.operationId ||
                    !Integer(purchase.body, "coins", int.MaxValue, out long coins) ||
                    !Integer(purchase.body, "version", GameWalletPolicy.MaximumJsonInteger, out long version))
                { FailCoins("A Coin purchase is awaiting confirmation. It will retry using the same operation ID.", purchase.code, IsVerifiedCoinResponse(purchase)); yield break; }
                // The operation receipt may predate another accepted reward or
                // purchase. Refresh the current wallet instead of applying an
                // older operation's historical balance as a fresh snapshot.
                WalletVersion = Math.Max(WalletVersion, version);
                var currentWallet = new Reply();
                yield return Request("GET", "/api/game/wallet", null, expectedAccount, expectedGeneration, currentWallet);
                if (!IsCurrentSession(expectedAccount, expectedGeneration)) yield break;
                if (currentWallet.code != 200 || !TryWallet(currentWallet.body, out ready))
                { Fail("The purchase receipt is preserved. Reconnect to refresh the current wallet."); yield break; }
                if (!ready || !MirrorWallet())
                { FailCoins("The purchase receipt is preserved. Reconnect to recover the Coin wallet cache."); yield break; }
                bool success = status != "insufficient_funds";
                if (success)
                {
                    var owned = new Reply();
                    yield return Request("GET", "/api/game/entitlements", null, expectedAccount, expectedGeneration, owned);
                    if (!IsCurrentSession(expectedAccount, expectedGeneration)) yield break;
                    if (owned.code != 200 || !ApplyEntitlements(owned.body, pending))
                    { FailCoins("The Coin purchase is confirmed online. Ownership will be recovered without another debit.", owned.code, IsVerifiedCoinResponse(owned)); yield break; }
                }
                pending.completed = true; pending.status = status;
                if (!SaveJournal()) { pending.completed = false; FailCoins("The Coin operation journal could not be saved. Please retry."); yield break; }
                CompletePurchaseCallback(pending.operationId, success, success ? null : "You do not have enough Coins for this item.");
            }
            bool rewardSyncFailed = false;
            if (HasSendableRewards())
            {
                var batch = journal.rewards.FindAll(GameWalletPolicy.IsSendableReward);
                if (batch.Count > 20) batch = batch.GetRange(0, 20);
                var rewards = new Reply();
                var events = new List<Dictionary<string, object>>();
                foreach (GameWalletRewardEvent reward in batch)
                {
                    var item = new Dictionary<string, object> { { "kind", reward.kind }, { "sourceId", reward.sourceId }, { "eventId", reward.eventId } };
                    if (reward.evidence != null) item.Add("evidence", reward.evidence);
                    events.Add(item);
                }
                yield return Request("POST", "/api/game/rewards", new Dictionary<string, object> { { "events", events } }, expectedAccount, expectedGeneration, rewards);
                if (!IsCurrentSession(expectedAccount, expectedGeneration)) yield break;
                if (RequiresSignIn) { Fail(); yield break; }
                if (rewards.code != 200)
                {
                    if (rewards.code != 400)
                    { FailCoins("Earned Coins remain queued. Reconnect to refresh your Coin wallet.", rewards.code, IsVerifiedCoinResponse(rewards)); yield break; }
                    // Preserve the event IDs/evidence for retry or review, but
                    // retain the verified wallet and recovered ownership.
                    rewardSyncFailed = true;
                }
                else
                {
                    if (!TryWallet(rewards.body, out ready))
                    { Fail("The earned-reward wallet response could not be verified. Your rewards remain queued."); yield break; }
                    if (!ready || !MirrorWallet())
                    { FailCoins("Earned Coins remain queued. Please reconnect to synchronize them."); yield break; }
                    var delivered = new List<string>();
                    foreach (GameWalletRewardEvent reward in batch)
                    {
                        delivered.Add(reward.eventId);
                        if (!journal.deliveredRewardIds.Contains(reward.eventId)) journal.deliveredRewardIds.Add(reward.eventId);
                        journal.rewards.RemoveAll(r => r.eventId == reward.eventId);
                    }
                    if (!SaveJournal()) { FailCoins("The Coin reward journal could not be saved. Please retry."); yield break; }
                    playerData.AcknowledgeWalletRewards(delivered);
                }
            }
            CoinsAvailable = true;
            WalletStatus = rewardSyncFailed ? "Online wallet synchronized. Some earned Coins remain queued for retry or review." :
                journal.rewards.Exists(r => r.held) ? "Online wallet synchronized. Older reward evidence needs review; those Coins remain held." :
                "Online Coins synchronized.";
            nextAttempt = Time.unscaledTime + (rewardSyncFailed ? 30f :
                refreshRequested || shopCurrency != null || HasSendableRewards() ? 0f : 15f);
            playerData.NotifyWalletChanged();
            playerData.CheckAllAchievements();
        }
        finally
        {
            inFlight = false;
            playerData?.NotifyWalletChanged();
        }
    }

    private bool MirrorWallet() => playerData != null && playerData.TryApplyWalletSnapshot(CoinBalance, WalletVersion, lifetimeEarned, lifetimeSpent);

    private bool HasSendableRewards() => journal != null && journal.rewards.Exists(GameWalletPolicy.IsSendableReward);

    private void CompletePurchaseCallback(string operationId, bool success, string error)
    {
        if (!purchaseCallbacks.TryGetValue(operationId, out Action<bool, string> callback)) return;
        purchaseCallbacks.Remove(operationId);
        callback(success, error);
    }

    private string CanonicalContractSource(string id) => playerData.WalletCanonicalContractId(id) ?? id;

    private string CanonicalRewardKey(GameWalletRewardEvent reward) => reward.kind + ":" +
        (reward.kind == "contract" ? CanonicalContractSource(reward.sourceId) : reward.sourceId);

    private bool HasUnreviewedLegacyRewardOverlap()
    {
        PlayerData data = playerData.CurrentData;
        var selectedPending = new HashSet<string>(StringComparer.Ordinal);
        if (data.walletRewardOutbox != null)
            foreach (GameWalletRewardEvent reward in data.walletRewardOutbox)
                if (GameWalletPolicy.IsReward(reward)) selectedPending.Add(CanonicalRewardKey(reward));
        var selectedCompleted = new HashSet<string>(StringComparer.Ordinal);
        if (data.completedContracts != null)
            foreach (string id in data.completedContracts) selectedCompleted.Add("contract:" + CanonicalContractSource(id));
        if (data.unlockedAchievements != null)
            foreach (string id in data.unlockedAchievements) selectedCompleted.Add("achievement:" + id);
        return journal.rewards.Exists(reward => GameWalletPolicy.IsReward(reward) &&
            selectedCompleted.Contains(CanonicalRewardKey(reward)) && !selectedPending.Contains(CanonicalRewardKey(reward)));
    }

    private object ImportSnapshot()
    {
        PlayerData data = playerData.CurrentData;
        var pendingContracts = new HashSet<string>(StringComparer.Ordinal);
        var pendingAchievements = new HashSet<string>(StringComparer.Ordinal);
        foreach (GameWalletRewardEvent reward in journal.rewards)
            if (reward.kind == "contract") pendingContracts.Add(CanonicalContractSource(reward.sourceId)); else pendingAchievements.Add(reward.sourceId);
        List<string> contracts = data.completedContracts != null ? data.completedContracts.ConvertAll(CanonicalContractSource) : new List<string>();
        List<string> achievements = data.unlockedAchievements != null ? new List<string>(data.unlockedAchievements) : new List<string>();
        contracts.RemoveAll(pendingContracts.Contains);
        achievements.RemoveAll(pendingAchievements.Contains);
        string json = playerData.GetCurrentDataJson();
        string hash;
        using (SHA256 sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(json))).Replace("-", "").ToLowerInvariant();
        return new Dictionary<string, object> {
            { "gold", Math.Max(0, data.gold) }, { "saveHash", hash },
            { "lifetimeGoldEarned", Math.Max(0, data.lifetimeGoldEarned) },
            { "lifetimeGoldSpent", Math.Max(0, data.lifetimeGoldSpent) },
            { "completedContracts", contracts }, { "unlockedAchievements", achievements },
            { "purchasedShopItemIds", data.purchasedShopItemIds != null ? new List<string>(data.purchasedShopItemIds) : new List<string>() },
            { "unlockedCosmeticIDs", data.unlockedCosmeticIDs != null ? new List<string>(data.unlockedCosmeticIDs) : new List<string>() },
            { "unlockedContractMaterials", data.unlockedContractMaterials != null ? new List<string>(data.unlockedContractMaterials) : new List<string>() }
        };
    }

    public bool PurchaseCosmetic(string itemId, Action<bool, string> callback) => EnqueuePurchase("cosmetic", itemId, null, callback);
    public bool PurchaseMaterial(string contractId, string materialId, Action<bool, string> callback) => EnqueuePurchase("material", materialId, contractId, callback);

    private bool EnqueuePurchase(string kind, string target, string contract, Action<bool, string> callback)
    {
        if (!CanSpendOnline) { callback?.Invoke(false, "Signed-in purchases require a synchronized online wallet."); return false; }
        var purchase = new GameWalletPendingPurchase {
            operationId = Guid.NewGuid().ToString("D"), targetKind = kind, targetId = target, contractId = contract
        };
        if (!GameWalletPolicy.IsPurchase(purchase)) { callback?.Invoke(false, "The shop item is not configured correctly."); return false; }
        journal.purchases.Add(purchase);
        if (!SaveJournal()) { journal.purchases.Remove(purchase); callback?.Invoke(false, WalletStatus); return false; }
        if (callback != null) purchaseCallbacks[purchase.operationId] = callback;
        RefreshWallet();
        return true;
    }

    public bool RequestShopLink(string currency, Action<bool, string> callback)
    {
        if ((currency != "coins" && currency != "diamonds") || RequiresSignIn || !IsCurrentSession(accountId, generation) || shopCurrency != null)
        { callback?.Invoke(false, "Sign in and finish opening your account save before visiting the shop."); return false; }
        shopCurrency = currency;
        shopCallback = callback;
        RefreshWallet();
        return true;
    }

    private bool TryWallet(IDictionary<string, object> body, out bool ready)
    {
        ready = false;
        if (body == null || !body.TryGetValue("ready", out object value) || !(value is bool flag) ||
            !Integer(body, "version", GameWalletPolicy.MaximumJsonInteger, out long version)) return false;
        ready = flag;
        if (!NullableInteger(body, "coins", int.MaxValue, out long? coins) ||
            !NullableInteger(body, "diamonds", GameWalletPolicy.MaximumJsonInteger, out long? diamonds)) return false;
        if (ready && !coins.HasValue) return false;
        if (ready && !GameWalletPolicy.CanMirrorVersion(WalletVersion, version)) return false;
        if (!OptionalCounter(body, "lifetimeGoldEarned", out lifetimeEarned) ||
            !OptionalCounter(body, "lifetimeGoldSpent", out lifetimeSpent)) return false;
        CoinBalance = coins.HasValue ? (int)coins.Value : 0;
        DiamondsAvailable = diamonds.HasValue;
        DiamondBalance = diamonds ?? 0;
        WalletVersion = version;
        CoinsAvailable = ready && coins.HasValue;
        return true;
    }

    private bool ApplyEntitlements(IDictionary<string, object> body, GameWalletPendingPurchase requiredPurchase = null)
    {
        if (body == null || !Integer(body, "version", GameWalletPolicy.MaximumJsonInteger, out long version) ||
            !GameWalletPolicy.CanMirrorVersion(WalletVersion, version) ||
            !body.TryGetValue("shopItems", out object shopsRaw) || !(shopsRaw is IList shops) || shops.Count > 1000 ||
            !body.TryGetValue("materials", out object materialsRaw) || !(materialsRaw is IList materials) || materials.Count > 2000) return false;
        var shopItems = new List<GameWalletShopEntitlement>();
        var materialItems = new List<GameWalletMaterialEntitlement>();
        var shopIds = new HashSet<string>(StringComparer.Ordinal);
        var materialKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (object raw in shops)
        {
            var row = raw as IDictionary<string, object>;
            string itemId = Text(row, "itemId"), cosmeticId = Text(row, "cosmeticId");
            if (!GameWalletPolicy.IsSourceId(itemId) || !GameWalletPolicy.IsSourceId(cosmeticId) || !shopIds.Add(itemId)) return false;
            shopItems.Add(new GameWalletShopEntitlement { itemId = itemId, cosmeticId = cosmeticId });
        }
        foreach (object raw in materials)
        {
            var row = raw as IDictionary<string, object>;
            string contract = Text(row, "contractId"), material = Text(row, "materialId"), key = Text(row, "saveKey");
            if (!GameWalletPolicy.IsSourceId(contract) || !GameWalletPolicy.IsSourceId(material) ||
                key != contract + "_" + material || !materialKeys.Add(key)) return false;
            materialItems.Add(new GameWalletMaterialEntitlement { contractId = contract, materialId = material, saveKey = key });
        }
        if (requiredPurchase != null)
        {
            // A successful receipt is not proof that this response includes
            // its target. Validate the permanent asset mapping before saving
            // or acknowledging the operation, even if local ownership exists.
            if (requiredPurchase.targetKind == "cosmetic")
            {
                string cosmetic = playerData.WalletCosmeticIdForShopItem(requiredPurchase.targetId);
                if (!GameWalletPolicy.IsSourceId(cosmetic) || !shopItems.Exists(row =>
                    row.itemId == requiredPurchase.targetId && row.cosmeticId == cosmetic)) return false;
            }
            else
            {
                string contract = playerData.WalletCanonicalContractId(requiredPurchase.contractId);
                string material = playerData.WalletCanonicalMaterialId(requiredPurchase.targetId);
                if (!GameWalletPolicy.IsSourceId(contract) || !GameWalletPolicy.IsSourceId(material) ||
                    !materialItems.Exists(row => row.contractId == contract && row.materialId == material &&
                        row.saveKey == contract + "_" + material)) return false;
            }
        }
        return playerData.TryMergeWalletEntitlements(shopItems, materialItems) &&
            (requiredPurchase == null || playerData.HasWalletPurchaseEntitlement(requiredPurchase));
    }

    private static string Text(IDictionary<string, object> body, string key) => body != null &&
        body.TryGetValue(key, out object value) && value is string text ? text : null;

    private static bool NullableInteger(IDictionary<string, object> body, string key, long max, out long? value)
    {
        value = null;
        if (body == null || !body.TryGetValue(key, out object raw)) return false;
        if (raw == null) return true;
        if (!Integer(body, key, max, out long count)) return false;
        value = count; return true;
    }

    private static bool OptionalCounter(IDictionary<string, object> body, string key, out int? value)
    {
        value = null;
        if (!body.ContainsKey(key)) return true;
        if (!NullableInteger(body, key, int.MaxValue, out long? count)) return false;
        if (count.HasValue) value = (int)count.Value;
        return true;
    }

    private static bool Integer(IDictionary<string, object> body, string key, long max, out long value)
    {
        value = 0;
        return body != null && body.TryGetValue(key, out object raw) && GameWalletPolicy.TryCount(raw, max, out value);
    }

    public static void OfferMainMenuSignIn()
    {
        PlayFabAuthManager auth = PlayFabAuthManager.Instance;
        if (auth != null) { auth.OpenAuthCanvasForMainMenu(); return; }
        GameWalletService service = Instance;
        if (service != null) service.StartCoroutine(service.ReturnForSignIn());
    }

    private IEnumerator ReturnForSignIn()
    {
        FusionConnectionManager fusion = FusionConnectionManager.Instance;
        bool visiting = fusion != null && fusion.IsGuestSaveProtected;
        if (!visiting && playerData != null && !playerData.TrySaveGame()) yield break;
        if (fusion != null)
        {
            fusion.StopSession();
            while (fusion != null && fusion.IsSessionStopping) yield return null;
        }
        LoadingScreenManager.LoadScene("Main Menu");
        while (LoadingScreenManager.IsLoading || PlayFabAuthManager.Instance == null) yield return null;
        PlayFabAuthManager.Instance.OpenAuthCanvasForMainMenu();
    }
}
