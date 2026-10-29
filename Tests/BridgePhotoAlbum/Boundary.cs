using System.Collections;
using System.Text.Json;

namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class DisallowMultipleComponent : Attribute { }
    public class Object { public static void Destroy(object value) { } }
    public class MonoBehaviour : Object
    {
        private readonly Dictionary<Type, object> components = new();
        public GameObject gameObject = new();
        public void SetComponent<T>(T instance) => components[typeof(T)] = instance;
        public T GetComponent<T>() where T : class => components.TryGetValue(typeof(T), out object instance) ? (T)instance : null;
        public void StopAllCoroutines() { }
        public void StartCoroutine(IEnumerator routine) { while (routine.MoveNext()) { } }
    }
    public sealed class GameObject { public T AddComponent<T>() where T : new() => new(); }
    public static class Time { public static float realtimeSinceStartup; }
    public static class Debug { public static void LogWarning(string message, object context = null) { } }
    public static class JsonUtility
    {
        private static readonly JsonSerializerOptions Options = new() { IncludeFields = true };
        public static string ToJson(object value) => JsonSerializer.Serialize(value, value.GetType(), Options);
        public static T FromJson<T>(string value) => JsonSerializer.Deserialize<T>(value, Options);
    }
    public enum TextureFormat { RGBA32 }
    public enum RenderTextureFormat { ARGB32 }
    public sealed class Texture2D : Object
    {
        public int width, height;
        public Texture2D(int w, int h, TextureFormat format, bool mip) { width = w; height = h; }
        public void ReadPixels(Rect rect, int x, int y) { }
        public void Apply(bool mip, bool immutable) { }
        public byte[] EncodeToPNG() => throw new NotSupportedException("GPU capture is not part of managed state-machine tests");
    }
    public readonly struct Rect { public Rect(int x, int y, int w, int h) { } }
    public static class ImageConversion { public static bool LoadImage(Texture2D texture, byte[] bytes, bool immutable) => false; }
    public sealed class RenderTexture
    {
        public static RenderTexture active;
        public static RenderTexture GetTemporary(int w, int h, int depth, RenderTextureFormat format) => new();
        public static void ReleaseTemporary(RenderTexture value) { }
    }
    public static class Graphics { public static void Blit(Texture2D input, RenderTexture output) { } }
    public static class Mathf
    {
        public static float Min(float a, float b) => Math.Min(a, b);
        public static int Max(int a, int b) => Math.Max(a, b);
        public static int FloorToInt(float value) => (int)Math.Floor(value);
    }
}

public sealed class BridgePhotoSnapshot
{
    public string storage, fileName, contractId, regionId, resultId, entryName, sha256, capturedAt;
}
public sealed class SavedBridgeData { public string resultId; }
public sealed class PlayerDataManager : UnityEngine.MonoBehaviour
{
    public string ActiveAccountId;
    public string CurrentSavePath;
    public SavedBridgeData Bridge;
    public int Recorded;
    public BridgePhotoSnapshot LastSnapshot;
    public bool SaveSucceeds = true;
    public SavedBridgeData GetSavedBridge(string contractId) => Bridge;
    public bool TryRecordBridgePhotoSnapshot(string contractId, string resultId, BridgePhotoSnapshot snapshot)
    { if (Bridge.resultId != resultId || !SaveSucceeds) return false; Recorded++; LastSnapshot = snapshot; return true; }
}
public sealed class CloudSaveManager
{
    public string ActiveAccountId = "ACCOUNT_A";
    public int SessionGeneration;
    public bool CanPublishDashboard = true;
    public AuxiliaryFileOperation Current;
    public sealed class AuxiliaryFileOperation { public string AccountId; public int Generation; public PlayFab.DataModels.EntityKey Entity; }
    public bool TryBeginAuxiliaryFileOperation(out PlayFab.DataModels.EntityKey entity, out AuxiliaryFileOperation token)
    {
        entity = new() { Id = "entity-" + ActiveAccountId, Type = "title_player_account" };
        token = null; if (Current != null) return false;
        token = Current = new() { AccountId = ActiveAccountId, Generation = SessionGeneration, Entity = entity }; return true;
    }
    public bool IsAuxiliaryFileOperationCurrent(AuxiliaryFileOperation token) => ReferenceEquals(token, Current) &&
        token != null && token.AccountId == ActiveAccountId && token.Generation == SessionGeneration;
    public void EndAuxiliaryFileOperation(AuxiliaryFileOperation token) { if (IsAuxiliaryFileOperationCurrent(token)) Current = null; }
    public bool VerifyAuxiliaryFileProfile(AuxiliaryFileOperation token, int version) => IsAuxiliaryFileOperationCurrent(token);
}

namespace PlayFab.DataModels
{
    public sealed class EntityKey { public string Id, Type; }
    public sealed class GetFilesRequest { public EntityKey Entity; }
    public sealed class GetFileMetadata { public int Size; public string DownloadUrl; }
    public sealed class GetFilesResponse { public int ProfileVersion; public Dictionary<string, GetFileMetadata> Metadata; }
    public sealed class InitiateFileUploadsRequest { public EntityKey Entity; public List<string> FileNames; public int? ProfileVersion; }
    public sealed class InitiateFileUploadMetadata { public string FileName, UploadUrl; }
    public sealed class InitiateFileUploadsResponse { public int ProfileVersion; public List<InitiateFileUploadMetadata> UploadDetails; }
    public sealed class FinalizeFileUploadsRequest { public EntityKey Entity; public List<string> FileNames; public int? ProfileVersion; }
    public sealed class FinalizeFileUploadsResponse { }
    public sealed class AbortFileUploadsRequest { public EntityKey Entity; public List<string> FileNames; public int? ProfileVersion; }
    public sealed class AbortFileUploadsResponse { }
}
namespace PlayFab
{
    public static class PlayFabClientAPI { public static bool IsClientLoggedIn() => true; }
    public static class PlayFabDataAPI
    {
        public sealed class Call<TRequest, TResponse>
        {
            public TRequest Request; public Action<TResponse> Success; public Action<object> Error;
            public void Fail() => Error(new object());
        }
        public static List<Call<DataModels.GetFilesRequest, DataModels.GetFilesResponse>> Gets = new();
        public static List<Call<DataModels.InitiateFileUploadsRequest, DataModels.InitiateFileUploadsResponse>> Starts = new();
        public static List<Call<DataModels.FinalizeFileUploadsRequest, DataModels.FinalizeFileUploadsResponse>> Finishes = new();
        public static List<Call<DataModels.AbortFileUploadsRequest, DataModels.AbortFileUploadsResponse>> Aborts = new();
        public static void Reset() { Gets.Clear(); Starts.Clear(); Finishes.Clear(); Aborts.Clear(); }
        public static void GetFiles(DataModels.GetFilesRequest request, Action<DataModels.GetFilesResponse> success, Action<object> error) =>
            Gets.Add(new() { Request = request, Success = success, Error = error });
        public static void InitiateFileUploads(DataModels.InitiateFileUploadsRequest request, Action<DataModels.InitiateFileUploadsResponse> success, Action<object> error) =>
            Starts.Add(new() { Request = request, Success = success, Error = error });
        public static void FinalizeFileUploads(DataModels.FinalizeFileUploadsRequest request, Action<DataModels.FinalizeFileUploadsResponse> success, Action<object> error) =>
            Finishes.Add(new() { Request = request, Success = success, Error = error });
        public static void AbortFileUploads(DataModels.AbortFileUploadsRequest request, Action<DataModels.AbortFileUploadsResponse> success, Action<object> error) =>
            Aborts.Add(new() { Request = request, Success = success, Error = error });
    }
}
namespace UnityEngine.Networking
{
    public class DownloadHandler { }
    public class DownloadHandlerScript : DownloadHandler
    {
        public DownloadHandlerScript(byte[] buffer) { }
        protected virtual bool ReceiveData(byte[] data, int length) => true;
        public bool Feed(byte[] bytes) => ReceiveData(bytes, bytes.Length);
    }
    public sealed class UploadHandler { }
    public sealed class UnityWebRequest : IDisposable
    {
        public enum Result { Success, ConnectionError }
        public static Dictionary<string, byte[]> Downloads = new();
        public int timeout, redirectLimit;
        public Result result = Result.Success;
        private readonly string url, method;
        private readonly DownloadHandler handler;
        public UnityWebRequest(string address, string verb, DownloadHandler download, UploadHandler upload)
        { url = address; method = verb; handler = download; }
        public static UnityWebRequest Put(string address, byte[] bytes) => new(address, "PUT", null, null);
        public object SendWebRequest()
        {
            if (method == "GET")
            {
                if (!Downloads.TryGetValue(url, out byte[] bytes) || !(handler is DownloadHandlerScript script) || !script.Feed(bytes)) result = Result.ConnectionError;
            }
            return null;
        }
        public void SetRequestHeader(string key, string value) { }
        public void Dispose() { }
    }
}
