// Boundary doubles only. Recovery/coroutine/request ordering is exercised from
// the actual linked GameWalletService.cs, never a reimplementation of that flow.
using System.Collections;
using System.Text;
using System.Text.Json;

internal static class TestJson
{
    internal static readonly JsonSerializerOptions Options = new() { IncludeFields = true };
    internal static string Serialize(object value) => JsonSerializer.Serialize(value, Options);
    internal static object Parse(string value)
    {
        using JsonDocument document = JsonDocument.Parse(value);
        return Convert(document.RootElement);
    }
    private static object Convert(JsonElement value) => value.ValueKind switch {
        JsonValueKind.Object => value.EnumerateObject().ToDictionary(p => p.Name, p => Convert(p.Value)),
        JsonValueKind.Array => value.EnumerateArray().Select(Convert).ToList(),
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.TryGetInt64(out long integer) ? (object)integer : value.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => throw new InvalidDataException("Unsupported JSON token"),
    };
}

namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class DisallowMultipleComponent : Attribute { }
    public sealed class Coroutine { }
    public class MonoBehaviour
    {
        public GameObject gameObject { get; set; }
        public readonly List<IEnumerator> StartedCoroutines = new();
        public T GetComponent<T>() where T : class => gameObject?.GetComponent<T>();
        public Coroutine StartCoroutine(IEnumerator coroutine) { StartedCoroutines.Add(coroutine); return new Coroutine(); }
    }
    public sealed class GameObject
    {
        private readonly Dictionary<Type, object> components = new();
        public T AddComponent<T>() where T : class, new() { T item = new(); Attach(item); return item; }
        public void Attach(object item) { components[item.GetType()] = item; if (item is MonoBehaviour component) component.gameObject = this; }
        public T GetComponent<T>() where T : class => components.Values.OfType<T>().FirstOrDefault();
    }
    public static class Application
    {
        public static string persistentDataPath;
        public static readonly List<string> OpenedUrls = new();
        public static void OpenURL(string url) => OpenedUrls.Add(url);
    }
    public static class Time { public static float unscaledTime; }
    public static class PlayerPrefs
    {
        public static readonly Dictionary<string, int> Values = new();
        public static int GetInt(string key, int fallback = 0) => Values.TryGetValue(key, out int value) ? value : fallback;
    }
    public static class Debug
    {
        public static readonly List<string> Warnings = new();
        public static void LogWarning(object message, object context = null) => Warnings.Add(message?.ToString());
    }
    public static class JsonUtility
    {
        public static string ToJson(object value) => TestJson.Serialize(value);
        public static T FromJson<T>(string value) => JsonSerializer.Deserialize<T>(value, TestJson.Options);
    }
}
namespace UnityEngine.SceneManagement { public static class SceneManager { } }

namespace UnityEngine.Networking
{
    public class DownloadHandler { public string text; }
    public sealed class DownloadHandlerBuffer : DownloadHandler { }
    public class UploadHandler { public byte[] data; }
    public sealed class UploadHandlerRaw : UploadHandler { public UploadHandlerRaw(byte[] value) { data = value; } }
    public sealed class UnityWebRequest : IDisposable
    {
        public sealed record Recorded(string Method, string Path, string Body, IReadOnlyDictionary<string, string> Headers, int RedirectLimit, int Timeout, string Url);
        public sealed record Response(string Method, string Path, long Code, object Body, Action AfterResponse = null);
        public static readonly Queue<Response> Script = new();
        public static readonly List<Recorded> Requests = new();
        public readonly string url, method;
        public DownloadHandler downloadHandler;
        public UploadHandler uploadHandler;
        public int timeout, redirectLimit;
        public long responseCode;
        public ulong downloadedBytes;
        private readonly Dictionary<string, string> headers = new();
        public UnityWebRequest(string url, string method) { this.url = url; this.method = method; }
        public void SetRequestHeader(string key, string value) => headers[key] = value;
        public IEnumerator SendWebRequest() => new ScriptedOperation(this);
        public void Dispose() { }
        public static void Reset() { Script.Clear(); Requests.Clear(); }
        private sealed class ScriptedOperation : IEnumerator
        {
            private readonly UnityWebRequest request;
            private bool served;
            public ScriptedOperation(UnityWebRequest request) { this.request = request; }
            public object Current => null;
            public bool MoveNext()
            {
                if (served) return false;
                served = true;
                Uri uri = new(request.url);
                string body = request.uploadHandler == null ? null : Encoding.UTF8.GetString(request.uploadHandler.data);
                Requests.Add(new(request.method, uri.PathAndQuery, body, new Dictionary<string, string>(request.headers), request.redirectLimit, request.timeout, request.url));
                if (uri.Scheme != "https" || uri.Host != "civil-craft.vercel.app") throw new Exception("Unexpected request origin");
                if (Script.Count == 0) throw new Exception($"Unscripted request: {request.method} {uri.PathAndQuery}");
                Response next = Script.Dequeue();
                if (next.Method != request.method || next.Path != uri.PathAndQuery)
                    throw new Exception($"Expected {next.Method} {next.Path}; received {request.method} {uri.PathAndQuery}");
                request.responseCode = next.Code;
                request.downloadHandler.text = TestJson.Serialize(next.Body);
                request.downloadedBytes = (ulong)Encoding.UTF8.GetByteCount(request.downloadHandler.text);
                next.AfterResponse?.Invoke();
                return false;
            }
            public void Reset() => throw new NotSupportedException();
        }
    }
}

namespace PlayFab
{
    public static class PlayFabClientAPI { public static bool LoggedIn = true; public static bool IsClientLoggedIn() => LoggedIn; }
    public static class PlayFabSettings
    {
        public sealed class Player { public string PlayFabId, ClientSessionTicket; }
        public sealed class Settings { public string TitleId; }
        public static readonly Player staticPlayer = new();
        public static readonly Settings staticSettings = new();
    }
}
namespace PlayFab.Json
{
    public static class PlayFabSimpleJson
    {
        public static string SerializeObject(object value) => TestJson.Serialize(value);
        public static object DeserializeObject(string value) => TestJson.Parse(value);
    }
}

public sealed class PlayerData
{
    public int gold, walletAuthorityVersion, lifetimeGoldEarned, lifetimeGoldSpent;
    public long walletCoinsVersion;
    public List<GameWalletRewardEvent> walletRewardOutbox = new();
    public List<GameWalletContractEvidence> walletContractEvidence = new();
    public List<string> completedContracts = new(), unlockedAchievements = new(), purchasedShopItemIds = new(), unlockedCosmeticIDs = new(), unlockedContractMaterials = new();
}
public sealed class PlayerDataManager : UnityEngine.MonoBehaviour
{
    public static PlayerDataManager Instance;
    public event Action OnSaveCommitted;
    public PlayerData CurrentData = new();
    public string ActiveAccountId;
    public bool FailMirror, FailMerge, FailSave, SkipOwnershipMerge;
    public int MirrorCount, MergeCount, SaveCount, NotificationCount, AchievementCheckCount, AckCount;
    public void NotifyWalletChanged() => NotificationCount++;
    public void CheckAllAchievements() => AchievementCheckCount++;
    public bool TrySaveGame() { SaveCount++; if (FailSave) return false; OnSaveCommitted?.Invoke(); return true; }
    public bool TryApplyWalletSnapshot(int coins, long version, int? earned = null, int? spent = null)
    {
        MirrorCount++;
        if (FailMirror || FailSave) return false;
        CurrentData.gold = coins; CurrentData.walletCoinsVersion = version; CurrentData.walletAuthorityVersion = 1;
        if (earned.HasValue) CurrentData.lifetimeGoldEarned = earned.Value;
        if (spent.HasValue) CurrentData.lifetimeGoldSpent = spent.Value;
        return TrySaveGame();
    }
    public bool TryMergeWalletEntitlements(List<GameWalletShopEntitlement> shops, List<GameWalletMaterialEntitlement> materials)
    {
        MergeCount++;
        if (FailMerge || FailSave) return false;
        if (SkipOwnershipMerge) return true;
        foreach (GameWalletShopEntitlement item in shops) {
            if (!CurrentData.purchasedShopItemIds.Contains(item.itemId)) CurrentData.purchasedShopItemIds.Add(item.itemId);
            if (!CurrentData.unlockedCosmeticIDs.Contains(item.cosmeticId)) CurrentData.unlockedCosmeticIDs.Add(item.cosmeticId);
        }
        foreach (GameWalletMaterialEntitlement item in materials)
            if (!CurrentData.unlockedContractMaterials.Contains(item.saveKey)) CurrentData.unlockedContractMaterials.Add(item.saveKey);
        return TrySaveGame();
    }
    public bool AcknowledgeWalletRewards(List<string> ids)
    {
        AckCount++;
        if (FailSave) return false;
        if (CurrentData.walletRewardOutbox == null) return true;
        CurrentData.walletRewardOutbox.RemoveAll(reward => ids.Contains(reward.eventId));
        return TrySaveGame();
    }
    public string GetCurrentDataJson() => TestJson.Serialize(CurrentData);
    public GameWalletRewardEvent RepairCapturedWalletRewardQuote(GameWalletRewardEvent reward) =>
        GameWalletPolicy.RepairCapturedRewardQuote(reward, CurrentData.walletContractEvidence?.Find(capture => capture?.contractId == reward?.sourceId));
    public string WalletCosmeticIdForShopItem(string itemId) => itemId == "shop_hat" ? "Hat" : itemId == "shop_boots" ? "Boots" : null;
    public string WalletCanonicalContractId(string id) => id == "ShopKeeperContract" ? "ShopKeeper" : id;
    public string WalletCanonicalMaterialId(string id) => id == "BeamAlias" ? "Beam" : id;
    public bool HasWalletPurchaseEntitlement(GameWalletPendingPurchase pending)
    {
        if (pending.targetKind == "cosmetic")
            return CurrentData.purchasedShopItemIds.Contains(pending.targetId) && CurrentData.unlockedCosmeticIDs.Contains(WalletCosmeticIdForShopItem(pending.targetId));
        return CurrentData.unlockedContractMaterials.Contains(WalletCanonicalContractId(pending.contractId) + "_" + WalletCanonicalMaterialId(pending.targetId));
    }
}
public sealed class CloudSaveManager : UnityEngine.MonoBehaviour
{
    public string ActiveAccountId;
    public int SessionGeneration;
    public bool CanPublishDashboard = true, CanImportLegacyWallet = true, IsSessionReady = true, HasConflict;
    public bool IsLegacyWalletImportSourceReady => CanImportLegacyWallet;
    public bool IsAccountActive => !string.IsNullOrEmpty(ActiveAccountId);
    public bool DenyImportReservation, ImportReservationActive;
    public int ImportReservationBegins, ImportReservationEnds;
    private string reservationAccount;
    private int reservationGeneration;
    public bool TryBeginLegacyWalletImport(string expectedAccount, int expectedGeneration)
    {
        ImportReservationBegins++;
        if (DenyImportReservation || ImportReservationActive || !CanImportLegacyWallet ||
            !GameWalletPolicy.SameAccount(ActiveAccountId, expectedAccount) || SessionGeneration != expectedGeneration) return false;
        ImportReservationActive = true; reservationAccount = expectedAccount; reservationGeneration = expectedGeneration;
        return true;
    }
    public void EndLegacyWalletImport(string expectedAccount, int expectedGeneration)
    {
        ImportReservationEnds++;
        if (ImportReservationActive && GameWalletPolicy.SameAccount(reservationAccount, expectedAccount) && reservationGeneration == expectedGeneration)
            ImportReservationActive = false;
    }
}
public static class SaveEncryption
{
    public static bool FailNextEncrypt;
    public static bool FailAllEncrypt;
    public static string Encrypt(string value, string purpose)
    {
        if (FailAllEncrypt || FailNextEncrypt) { FailNextEncrypt = false; throw new IOException("Injected journal write failure"); }
        return value;
    }
    public static bool TryDecryptOrReadLegacy(string value, string purpose, out string json, out bool legacy, out string error)
    { json = value; legacy = false; error = null; return true; }
}
public sealed class FusionConnectionManager
{
    public static FusionConnectionManager Instance;
    public bool IsGuestSaveProtected, IsSessionStopping;
    public void StopSession() => IsSessionStopping = false;
}
public sealed class PlayFabAuthManager
{
    public static PlayFabAuthManager Instance;
    public void OpenAuthCanvasForMainMenu() { }
}
public static class LoadingScreenManager
{
    public static bool IsLoading;
    public static void LoadScene(string scene) { }
}
