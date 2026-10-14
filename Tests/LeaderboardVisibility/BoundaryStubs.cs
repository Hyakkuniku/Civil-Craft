// Doubles for engine/UI/network and PlayFab boundaries. Actual controller and
// strict public parser are linked into the harness unchanged.
using System.Collections;
using System.Text;

namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Field)] public sealed class SerializeField : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public sealed class HideInInspector : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public sealed class Header : Attribute { public Header(string _) { } }
    [AttributeUsage(AttributeTargets.Field)] public sealed class Tooltip : Attribute { public Tooltip(string _) { } }
    public class Object { }
    public class Component : Object
    {
        public GameObject gameObject { get; set; } = new();
        public Transform transform => gameObject.transform;
        public T GetComponent<T>() where T : class => gameObject.GetComponent<T>();
    }
    public sealed class Coroutine { public IEnumerator Iterator; }
    public class MonoBehaviour : Component
    {
        public bool isActiveAndEnabled = true;
        public readonly List<Coroutine> Coroutines = new();
        public Coroutine StartCoroutine(IEnumerator iterator)
        {
            var result = new Coroutine { Iterator = iterator };
            Coroutines.Add(result);
            iterator.MoveNext(); // Unity immediately advances to the first yield.
            return result;
        }
        public void StopCoroutine(Coroutine routine) => Coroutines.Remove(routine);
    }
    public class Transform { }
    public sealed class RectTransform : Transform { }
    public sealed class GameObject : Object
    {
        public bool activeSelf, activeInHierarchy;
        public readonly Transform transform = new RectTransform();
        private readonly Dictionary<Type, object> components = new();
        public T GetComponent<T>() where T : class => components.Values.OfType<T>().FirstOrDefault();
        public void SetActive(bool value) { activeSelf = activeInHierarchy = value; }
    }
    public readonly struct Color { public Color(float r, float g, float b, float a) { } }
    public readonly struct Color32 { public Color32(byte r, byte g, byte b, byte a) { } public static implicit operator Color(Color32 _) => new(); }
    public static class Mathf { public static int RoundToInt(float value) => (int)MathF.Round(value); }
    public static class JsonUtility
    {
        public static T FromJson<T>(string json) => System.Text.Json.JsonSerializer.Deserialize<T>(json,
            new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
    }
    public static class Debug { public static void LogWarning(object message, object context = null) { } }
}
namespace UnityEngine.Events
{
    public sealed class UnityEvent
    {
        private readonly List<Action> listeners = new();
        public void AddListener(Action action) => listeners.Add(action);
        public void RemoveListener(Action action) => listeners.Remove(action);
        public void Invoke() { foreach (var action in listeners.ToArray()) action(); }
    }
    public sealed class UnityEvent<T>
    {
        private readonly List<Action<T>> listeners = new();
        public void AddListener(Action<T> action) => listeners.Add(action);
        public void RemoveListener(Action<T> action) => listeners.Remove(action);
        public void Invoke(T value) { foreach (var action in listeners.ToArray()) action(value); }
    }
}
namespace UnityEngine.UI
{
    public class Graphic { public UnityEngine.Color color; }
    public sealed class Button : UnityEngine.Component
    {
        public bool interactable = true;
        public Graphic targetGraphic = new();
        public readonly UnityEngine.Events.UnityEvent onClick = new();
    }
    public sealed class Toggle : UnityEngine.Component
    {
        public bool isOn;
        public readonly UnityEngine.Events.UnityEvent<bool> onValueChanged = new();
        public void SetIsOnWithoutNotify(bool value) => isOn = value;
    }
    public sealed class Scrollbar : UnityEngine.Component { }
    public sealed class ScrollRect : UnityEngine.Component
    { public float verticalNormalizedPosition; public Scrollbar verticalScrollbar; }
}
namespace TMPro
{
    public sealed class TMP_Text : UnityEngine.Component { public string text; }
    public sealed class TMP_Dropdown : UnityEngine.Component
    {
        public sealed class OptionData { public string text; public OptionData(string value) { text = value; } }
        public List<OptionData> options = new();
        public int value;
        public bool interactable;
        public readonly UnityEngine.Events.UnityEvent<int> onValueChanged = new();
        public void ClearOptions() { options.Clear(); value = 0; }
        public void AddOptions(List<OptionData> supplied) => options.AddRange(supplied);
        public void SetValueWithoutNotify(int selected) => value = selected;
        public void RefreshShownValue() { }
    }
}
namespace UnityEngine.Networking
{
    public class DownloadHandler : IDisposable { public void Dispose() { } }
    public abstract class DownloadHandlerScript : DownloadHandler
    {
        protected DownloadHandlerScript(byte[] _) { }
        protected virtual bool ReceiveData(byte[] data, int count) => true;
        public bool Deliver(byte[] data, int count) => ReceiveData(data, count);
    }
    public sealed class UnityWebRequestAsyncOperation
    { public readonly UnityWebRequest Request; public UnityWebRequestAsyncOperation(UnityWebRequest request) { Request = request; } }
    public sealed class UnityWebRequest : IDisposable
    {
        public const string kHttpVerbGET = "GET";
        public enum Result { InProgress, Success, ConnectionError, ProtocolError, DataProcessingError }
        public static readonly List<UnityWebRequest> Requests = new();
        public readonly string url, method;
        public readonly Dictionary<string, string> Headers = new();
        public DownloadHandler downloadHandler;
        public int timeout, redirectLimit;
        public long responseCode;
        public Result result;
        public bool Aborted, Disposed;
        public UnityWebRequest(string url, string method) { this.url = url; this.method = method; }
        public void SetRequestHeader(string key, string value) => Headers[key] = value;
        public UnityWebRequestAsyncOperation SendWebRequest() { Requests.Add(this); return new(this); }
        public void Respond(string body, long status = 200, Result outcome = Result.Success)
        {
            responseCode = status; result = outcome;
            byte[] bytes = Encoding.UTF8.GetBytes(body ?? "");
            if (!((DownloadHandlerScript)downloadHandler).Deliver(bytes, bytes.Length)) result = Result.DataProcessingError;
        }
        public void Abort() { Aborted = true; }
        public void Dispose() { Disposed = true; }
    }
}
namespace PlayFab
{
    public enum PlayFabErrorCode { StatisticNotFound, Unknown }
    public sealed class PlayFabError { public PlayFabErrorCode Error; public string ErrorMessage; }
    public sealed class PlayFabAuthenticationContext { public void CopyFrom(PlayFabAuthenticationContext context) { } }
    public static class PlayFabSettings { public static PlayFabAuthenticationContext staticPlayer = new(); }
    public static class PlayFabClientAPI
    {
        public static bool LoggedIn = true;
        public static readonly List<ClientModels.GetLeaderboardRequest> Leaderboards = new();
        public static readonly List<Action<ClientModels.GetLeaderboardResult>> LeaderboardSuccess = new();
        public static readonly List<ClientModels.ExecuteCloudScriptRequest> Multiplayer = new();
        public static bool IsClientLoggedIn() => LoggedIn;
        public static void GetLeaderboard(ClientModels.GetLeaderboardRequest request,
            Action<ClientModels.GetLeaderboardResult> success, Action<PlayFabError> error)
        { Leaderboards.Add(request); LeaderboardSuccess.Add(success); }
        public static void ExecuteCloudScript(ClientModels.ExecuteCloudScriptRequest request,
            Action<ClientModels.ExecuteCloudScriptResult> success, Action<PlayFabError> error) => Multiplayer.Add(request);
    }
}
namespace PlayFab.ClientModels
{
    public sealed class GetLeaderboardRequest { public string StatisticName; public int StartPosition, MaxResultsCount; }
    public sealed class PlayerLeaderboardEntry { public int StatValue, Position; public string DisplayName; }
    public sealed class GetLeaderboardResult { public List<PlayerLeaderboardEntry> Leaderboard; }
    public sealed class ExecuteCloudScriptRequest
    { public string FunctionName; public bool GeneratePlayStreamEvent; public PlayFab.PlayFabAuthenticationContext AuthenticationContext; }
    public sealed class ExecuteCloudScriptResult { public object Error, FunctionResult; }
}
public sealed class ContractSO
{ public string name, contractID; public bool hideFromLeaderboard; public string ContractID => contractID; }
public sealed class PlayerDataManager
{
    public static PlayerDataManager Instance;
    public List<ContractSO> allGameContracts;
    public ContractLeaderboardData GetLeaderboardRecord(string id) => null;
}
public sealed class ContractLeaderboardData { public LeaderboardRunData strongest, mostEfficient; }
public sealed class LeaderboardRunData { public float cost, peakStress; }
public static class ChallengeBuildWorkspace { public const string ContractPrefix = "CHALLENGE_"; }
public sealed class LeaderboardPanelMotion : UnityEngine.MonoBehaviour { public void Close() { } }
public sealed class LeaderboardToggleStyle { public void RefreshState() { } }
public sealed class LeaderboardRowUI : UnityEngine.Component { public void Bind(int rank, string name, int cost, float stress) { } }
public sealed class MultiplayerLeaderboardRowUI : UnityEngine.Component
{
    public void Bind(int rank, string name, int wins, int losses, bool own) { }
    public static string FormatWinRate(int wins, int losses) => "50%";
}
public static class LeaderboardScoreCodec
{
    public static string StatisticName(string id, bool strongest) => id + (strongest ? "_STR" : "_COST");
    public static bool TryDecode(int value, bool strongest, out int cost, out float stress) { cost = 1; stress = 2; return true; }
}
public static class UIReservedRegionLayout { public static void Register(UnityEngine.RectTransform rect) { } public static void Unregister(UnityEngine.RectTransform rect) { } }
public static class MultiplayerResultReportingService { public static string AuthenticatedAccountId = "test-player"; }
