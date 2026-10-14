// Test doubles at Unity lifecycle/HTTP boundaries only. Production release
// parsing, eligibility and service implementations are linked by the project.
using System.Collections;
using System.Text;

namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class DisallowMultipleComponent : Attribute { }
    [AttributeUsage(AttributeTargets.Class)] public sealed class RequireComponent : Attribute { public RequireComponent(Type _) { } }
    [AttributeUsage(AttributeTargets.Field)] public sealed class SerializeField : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType _) { }
    }
    public enum RuntimeInitializeLoadType { SubsystemRegistration, BeforeSceneLoad }
    public class Object
    {
        public static void Destroy(Object _) { }
        public static void DontDestroyOnLoad(Object _) { }
    }
    public sealed class Coroutine { }
    public class MonoBehaviour : Object
    {
        public GameObject gameObject { get; set; } = new();
        public bool isActiveAndEnabled = true;
        public readonly List<IEnumerator> StartedCoroutines = new();
        public Coroutine StartCoroutine(IEnumerator coroutine)
        {
            StartedCoroutines.Add(coroutine);
            return new Coroutine();
        }
        public T GetComponent<T>() where T : class => gameObject.GetComponent<T>();
        public void StopCoroutine(Coroutine _) => StartedCoroutines.Clear();
        public void StopAllCoroutines() => StartedCoroutines.Clear();
    }
    public sealed class GameObject : Object
    {
        private readonly Dictionary<Type, object> components = new();
        public bool activeSelf, activeInHierarchy;
        public GameObject(string _ = null) { }
        public T AddComponent<T>() where T : class, new() { T component = new(); Attach(component); return component; }
        public void Attach(object component)
        {
            components[component.GetType()] = component;
            if (component is MonoBehaviour behaviour) behaviour.gameObject = this;
        }
        public T GetComponent<T>() where T : class => components.Values.OfType<T>().FirstOrDefault();
        public void SetActive(bool value) { activeSelf = activeInHierarchy = value; }
    }
    public static class Application
    {
        public static string persistentDataPath;
        public static string version = "1.2.3";
        public static readonly List<string> OpenedUrls = new();
        public static void OpenURL(string url) => OpenedUrls.Add(url);
    }
    public static class Time { public static float realtimeSinceStartup, unscaledTime; }
    public static class PlayerPrefs { public static int Choice; public static int GetInt(string key, int fallback) => Choice; }
    public static class JsonUtility
    {
        public static T FromJson<T>(string json) => System.Text.Json.JsonSerializer.Deserialize<T>(json, new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
    }
    public static class Debug
    {
        public static readonly List<string> Warnings = new();
        public static void LogWarning(object message) => Warnings.Add(message?.ToString());
    }
}

namespace PlayFab
{
    public static class PlayFabClientAPI { public static bool LoggedIn; public static bool IsClientLoggedIn() => LoggedIn; }
    public sealed class MessageTestPlayer { public string ClientSessionTicket, PlayFabId; }
    public static class PlayFabSettings { public static MessageTestPlayer staticPlayer = new(); }
}

namespace UnityEditor
{
    public static class PlayerSettings
    {
        public static class Android { public static int bundleVersionCode = 10; }
    }
}

namespace UnityEngine.Networking
{
    public class DownloadHandler : IDisposable { public string text; public void Dispose() { } }
    public sealed class DownloadHandlerBuffer : DownloadHandler { }
    public abstract class DownloadHandlerScript : DownloadHandler
    {
        protected DownloadHandlerScript(byte[] _) { }
        protected virtual bool ReceiveData(byte[] data, int length) => true;
        public bool Deliver(byte[] data, int length) => ReceiveData(data, length);
    }
    public sealed class UnityWebRequestAsyncOperation : IEnumerator
    {
        private readonly IEnumerator scripted;
        public UnityWebRequestAsyncOperation(IEnumerator scripted) { this.scripted = scripted; }
        public object Current => scripted.Current;
        public bool MoveNext() => scripted.MoveNext();
        public void Reset() => throw new NotSupportedException();
    }
    public sealed class UnityWebRequest : IDisposable
    {
        public const string kHttpVerbGET = "GET";
        public enum Result { InProgress, Success, ConnectionError, ProtocolError, DataProcessingError }
        public sealed record Recorded(string Method, string Url, IReadOnlyDictionary<string, string> Headers, int Timeout, int RedirectLimit);
        public sealed record Response(long Code, string Body, Result Outcome = Result.Success, Action OnReceive = null);
        public static readonly Queue<Response> Script = new();
        public static readonly List<Recorded> Requests = new();
        public readonly string url, method;
        public DownloadHandler downloadHandler;
        public int timeout, redirectLimit = 32;
        public long responseCode;
        public Result result = Result.InProgress;
        public string error;
        private readonly Dictionary<string, string> headers = new();
        public UnityWebRequest(string url, string method)
        {
            this.url = url;
            this.method = method;
        }
        public static UnityWebRequest Get(string url) => new(url, "GET") { downloadHandler = new DownloadHandlerBuffer() };
        public void SetRequestHeader(string key, string value) => headers[key] = value;
        public UnityWebRequestAsyncOperation SendWebRequest()
        {
            Requests.Add(new(method, url, new Dictionary<string, string>(headers), timeout, redirectLimit));
            return new UnityWebRequestAsyncOperation(new ScriptedOperation(this));
        }
        public void Abort() { result = Result.ConnectionError; error = "Aborted"; }
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
                if (Script.Count == 0) throw new InvalidOperationException("A real network request is forbidden; no scripted response was supplied.");
                Response response = Script.Dequeue();
                request.responseCode = response.Code;
                request.downloadHandler.text = response.Body;
                request.result = response.Outcome;
                if (request.downloadHandler is DownloadHandlerScript receiver && response.Body != null)
                {
                    byte[] data = Encoding.UTF8.GetBytes(response.Body);
                    for (int offset = 0; offset < data.Length; offset += 4096)
                    {
                        byte[] chunk = data.Skip(offset).Take(4096).ToArray();
                        if (!receiver.Deliver(chunk, chunk.Length)) { request.result = Result.DataProcessingError; break; }
                    }
                }
                request.error = response.Outcome == Result.Success ? null : "Scripted offline/HTTP failure";
                response.OnReceive?.Invoke();
                return false;
            }
            public void Reset() => throw new NotSupportedException();
        }
    }
}

namespace UnityEngine.Events
{
    public delegate void UnityAction();
    public sealed class UnityEvent
    {
        private readonly List<UnityAction> listeners = new();
        public int ListenerCount => listeners.Count;
        public void AddListener(UnityAction listener) => listeners.Add(listener);
        public void RemoveListener(UnityAction listener) => listeners.RemoveAll(item => item == listener);
        public void Invoke() { foreach (UnityAction listener in listeners.ToArray()) listener(); }
    }
}
namespace UnityEngine.UI
{
    public sealed class Button
    {
        public UnityEngine.GameObject gameObject = new();
        public bool interactable = true;
        public readonly UnityEngine.Events.UnityEvent onClick = new();
    }
    public sealed class ScrollRect { public float verticalNormalizedPosition; }
}
namespace UnityEngine.InputSystem
{
    public sealed class Keyboard
    {
        public static Keyboard current;
        public readonly KeyControl escapeKey = new();
    }
    public sealed class KeyControl { public bool wasPressedThisFrame; }
}
namespace TMPro { public sealed class TMP_Text { public string text; public bool richText = true; } }

public sealed class MainMenuUpdatesLayout
{
    public int ApplyCount, RefreshNotesCount;
    public void ApplyLayout() => ApplyCount++;
    public void RefreshNotesLayout() => RefreshNotesCount++;
}
public sealed class UIPanelCoordinator
{
    public static UIPanelCoordinator Instance;
    public UnityEngine.GameObject CurrentPanel;
    public int Opens, Closes;
    public void OpenPanel(UnityEngine.GameObject panel) { Opens++; CurrentPanel = panel; panel.SetActive(true); }
    public void ClosePanel(UnityEngine.GameObject panel) { Closes++; CurrentPanel = null; panel.SetActive(false); }
    public bool IsOpen(UnityEngine.GameObject panel) => panel == CurrentPanel;
}
