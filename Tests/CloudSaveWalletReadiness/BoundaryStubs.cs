using System.Reflection;
using System.Text;
using System.Text.Json;

// The real CloudSaveManager source is linked into this executable. Only its
// Unity, storage-owner, encryption and remote transport boundaries are fake.
// No Unity application, PlayFab title, wallet, credential or player save is used.
namespace UnityEngine
{
    public sealed class DisallowMultipleComponent : Attribute { }
    public sealed class SerializeField : Attribute { }
    public sealed class HeaderAttribute : Attribute { public HeaderAttribute(string _) { } }
    public class MonoBehaviour
    {
        private readonly Dictionary<Type, object> components = new();
        public void SetComponent<T>(T value) => components[typeof(T)] = value;
        protected T GetComponent<T>() where T : class => components.GetValueOrDefault(typeof(T)) as T;
        protected T GetComponentInChildren<T>(bool _) where T : class => GetComponent<T>();
    }
    public static class Debug
    {
        public static readonly List<string> Messages = new();
        public static void LogWarning(object text, object context = null) => Messages.Add(Convert.ToString(text));
        public static void LogError(object text, object context = null) => Messages.Add(Convert.ToString(text));
    }
    public static class Time { public static float realtimeSinceStartup; public static float timeScale = 1; }
    public static class Mathf { public static float Min(float first, float second) => Math.Min(first, second); }
    public static class JsonUtility
    {
        private static readonly JsonSerializerOptions Options = new() { IncludeFields = true };
        public static string ToJson(object value) => JsonSerializer.Serialize(value, value.GetType(), Options);
        public static T FromJson<T>(string text)
        {
            // Unity supports private serializable envelope classes. Instantiate
            // them without changing the production class's accessibility.
            using JsonDocument document = JsonDocument.Parse(text);
            object value = Activator.CreateInstance(typeof(T), nonPublic: true);
            foreach (FieldInfo field in typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance))
                if (document.RootElement.TryGetProperty(field.Name, out JsonElement element))
                    field.SetValue(value, JsonSerializer.Deserialize(element.GetRawText(), field.FieldType, Options));
            return (T)value;
        }
    }
}

public sealed class PlayerData { public int gold; }
public sealed class PlayerDataManager
{
    public event Action OnSaveCommitted;
    public readonly Dictionary<string, string> Accounts = new(StringComparer.Ordinal);
    public string GuestJson = "{\"gold\":700}";
    public string Json = "{\"gold\":0}";
    public string Root;
    public string CurrentSavePath;
    public bool ApplySucceeds = true;
    public bool SelectSucceeds = true;
    public int Applied;
    public int IgnoredLegacy;
    public bool HasAccountSave(string account) => Accounts.ContainsKey(account);
    public bool UseAccountSave(string account, bool guest, out bool existed)
    {
        existed = Accounts.TryGetValue(account, out string previous);
        if (!SelectSucceeds) return false;
        Json = existed ? previous : guest ? GuestJson : "{\"gold\":0}";
        Accounts[account] = Json;
        CurrentSavePath = Path.Combine(Root, account, "playerSaveData.json");
        Directory.CreateDirectory(Path.GetDirectoryName(CurrentSavePath));
        File.WriteAllText(CurrentSavePath, Json);
        return true;
    }
    public string GetCurrentDataJson() => Json;
    public bool TryApplyCloudData(string json, out string error)
    {
        error = ApplySucceeds ? null : "Simulated account-save write failure";
        if (!ApplySucceeds) return false;
        Json = json;
        Applied++;
        File.WriteAllText(CurrentSavePath, json);
        OnSaveCommitted?.Invoke();
        return true;
    }
    public void IgnoreLegacyProgressPrefsForCurrentAccount() => IgnoredLegacy++;
    public void CommitLocal(string json)
    {
        Json = json;
        File.WriteAllText(CurrentSavePath, Json);
        OnSaveCommitted?.Invoke();
    }
}

public sealed class AuthoredSaveChoiceDialog
{
    public bool IsVisible;
    public bool ShowSucceeds = true;
    private Action left, right;
    public bool Show(string title, string message, string leftLabel, string rightLabel, Action first, Action second)
    {
        if (!ShowSucceeds) return false;
        IsVisible = true; left = first; right = second; return true;
    }
    public void Hide() { IsVisible = false; left = right = null; }
    public void ChooseDevice() { Action action = left; action?.Invoke(); }
    public void ChooseOnline() { Action action = right; action?.Invoke(); }
}

public static class SaveEncryption
{
    public static bool FailEncrypt;
    public static string Encrypt(string text, string purpose)
    {
        if (FailEncrypt) throw new IOException("Simulated backup/encryption failure");
        return "stub-encrypted:" + purpose + ":" + Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
    }
    public static bool TryDecryptOrReadLegacy(string text, string purpose, out string plain, out bool legacy, out string error)
    {
        legacy = false; error = null; plain = null;
        string prefix = "stub-encrypted:" + purpose + ":";
        if (text == null || !text.StartsWith(prefix, StringComparison.Ordinal)) { error = "Invalid simulated account encryption"; return false; }
        try { plain = Encoding.UTF8.GetString(Convert.FromBase64String(text[prefix.Length..])); return true; }
        catch { error = "Invalid simulated payload"; return false; }
    }
}

namespace PlayFab
{
    public sealed class PlayFabError { public string ErrorMessage; }
    public static class PlayFabClientAPI
    {
        public static bool LoggedIn = true;
        public static bool IsClientLoggedIn() => LoggedIn;
    }
    public sealed class QueuedCall<TRequest, TResult>
    {
        public TRequest Request;
        public Action<TResult> Success;
        public Action<PlayFabError> Error;
        public void Fail(string message = "Simulated service failure") => Error(new PlayFabError { ErrorMessage = message });
    }
    public static class PlayFabDataAPI
    {
        public static readonly List<QueuedCall<DataModels.GetFilesRequest, DataModels.GetFilesResponse>> Gets = new();
        public static readonly List<QueuedCall<DataModels.InitiateFileUploadsRequest, DataModels.InitiateFileUploadsResponse>> Starts = new();
        public static readonly List<QueuedCall<DataModels.FinalizeFileUploadsRequest, DataModels.FinalizeFileUploadsResponse>> Finishes = new();
        public static void Reset() { Gets.Clear(); Starts.Clear(); Finishes.Clear(); }
        public static void GetFiles(DataModels.GetFilesRequest request, Action<DataModels.GetFilesResponse> success, Action<PlayFabError> error) =>
            Gets.Add(new() { Request = request, Success = success, Error = error });
        public static void InitiateFileUploads(DataModels.InitiateFileUploadsRequest request, Action<DataModels.InitiateFileUploadsResponse> success, Action<PlayFabError> error) =>
            Starts.Add(new() { Request = request, Success = success, Error = error });
        public static void FinalizeFileUploads(DataModels.FinalizeFileUploadsRequest request, Action<DataModels.FinalizeFileUploadsResponse> success, Action<PlayFabError> error) =>
            Finishes.Add(new() { Request = request, Success = success, Error = error });
    }
}
namespace PlayFab.ClientModels
{
    public sealed class LoginResult { public string PlayFabId; public EntityTokenResponse EntityToken; }
    public sealed class EntityTokenResponse { public EntityKey Entity; }
    public sealed class EntityKey { public string Id; public string Type; }
}
namespace PlayFab.DataModels
{
    public sealed class EntityKey { public string Id; public string Type; }
    public sealed class GetFilesRequest { public EntityKey Entity; }
    public sealed class GetFileMetadata { public string DownloadUrl; }
    public sealed class GetFilesResponse { public Dictionary<string, GetFileMetadata> Metadata; public int ProfileVersion; }
    public sealed class InitiateFileUploadsRequest { public EntityKey Entity; public List<string> FileNames; public int ProfileVersion; }
    public sealed class UploadFileMetadata { public string UploadUrl; }
    public sealed class InitiateFileUploadsResponse { public List<UploadFileMetadata> UploadDetails; public int ProfileVersion; }
    public sealed class FinalizeFileUploadsRequest { public EntityKey Entity; public List<string> FileNames; public int ProfileVersion; }
    public sealed class FinalizeFileUploadsResponse { }
}
namespace PlayFab.Internal
{
    public sealed class DownloadCall { public string Url; public Action<byte[]> Success; public Action<string> Error; }
    public sealed class UploadCall { public string Url; public byte[] Bytes; public Action<byte[]> Success; public Action<string> Error; }
    public static class PlayFabHttp
    {
        public static readonly List<DownloadCall> Downloads = new();
        public static readonly List<UploadCall> Uploads = new();
        public static void Reset() { Downloads.Clear(); Uploads.Clear(); }
        public static void SimpleGetCall(string url, Action<byte[]> success, Action<string> error) => Downloads.Add(new() { Url = url, Success = success, Error = error });
        public static void SimplePutCall(string url, byte[] bytes, Action<byte[]> success, Action<string> error) => Uploads.Add(new() { Url = url, Bytes = bytes, Success = success, Error = error });
    }
}
