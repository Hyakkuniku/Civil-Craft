using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using PlayFab.Json;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>Unauthenticated main-menu release feed; never accesses game/account saves.</summary>
[DisallowMultipleComponent]
public sealed class GameReleaseService : MonoBehaviour
{
    public const string CacheFileName = "public-release-cache.json";
    public GameReleaseSnapshot Snapshot { get; private set; }
    public GameReleaseFetchState State { get; private set; }
    public string StatusMessage { get; private set; } = "Update status has not been checked.";
    public DateTime? LastVerifiedUtc { get; private set; }
    public DateTime? CacheFetchedAtUtc { get; private set; }
    public long? InstalledVersionCode { get; private set; }
    public bool IsRequestInFlight { get; private set; }
    public bool HasVerifiedUpdate => GameReleasePolicy.IsVerifiedUpdate(Snapshot, InstalledVersionCode, State == GameReleaseFetchState.Ready);
    public event Action Changed;
    private DateTime? lastAttemptUtc;
    private bool initialized, menuSessionStarted;
    private Coroutine fetchRoutine;
    private UnityWebRequest activeRequest;

    public void BeginMenuSession()
    {
        Initialize();
        if (menuSessionStarted) return;
        menuSessionStarted = true;
        RequestRefresh(true);
    }

    public void RefreshIfStale() { Initialize(); RequestRefresh(false); }
    public void Retry() { Initialize(); RequestRefresh(true); }

    private void Initialize()
    {
        if (initialized) return;
        initialized = true;
        InstalledVersionCode = GameInstalledVersionReader.ReadAndroidCode();
        ReadCache();
    }

    private void RequestRefresh(bool force)
    {
        if (!isActiveAndEnabled || IsRequestInFlight || !GameReleasePolicy.NeedsRefresh(lastAttemptUtc, DateTime.UtcNow, force)) return;
        lastAttemptUtc = DateTime.UtcNow;
        State = GameReleaseFetchState.Checking;
        StatusMessage = "Checking for updates...";
        IsRequestInFlight = true;
        NotifyChanged();
        fetchRoutine = StartCoroutine(Fetch());
    }

    private IEnumerator Fetch()
    {
        UnityWebRequest request = null;
        BoundedReleaseDownload handler = null;
        UnityWebRequestAsyncOperation operation = null;
        try
        {
            handler = new BoundedReleaseDownload();
            request = new UnityWebRequest(GameReleasePolicy.WebsiteOrigin + GameReleasePolicy.FeedPath, UnityWebRequest.kHttpVerbGET);
            request.downloadHandler = handler;
            request.timeout = GameReleasePolicy.FetchTimeoutSeconds;
            request.redirectLimit = 0;
            request.SetRequestHeader("Accept", "application/json");
            activeRequest = request;
            operation = request.SendWebRequest();
        }
        catch (Exception)
        {
            if (request != null) request.Dispose();
            else if (handler != null) handler.Dispose();
            activeRequest = null;
            IsRequestInFlight = false;
            SetFailure("Unable to check updates.");
            yield break;
        }
        try
        {
            yield return operation;
            if (request.result != UnityWebRequest.Result.Success || request.responseCode != 200 || handler.Overflowed)
                SetFailure("Unable to check updates. Check your connection and try again.");
            else
                AcceptResponse(handler.Body);
        }
        finally
        {
            if (activeRequest == request) activeRequest = null;
            request.Dispose();
            IsRequestInFlight = false;
            fetchRoutine = null;
            NotifyChanged();
        }
    }

    private void AcceptResponse(string body)
    {
        GameReleaseSnapshot parsed;
        try
        {
            if (!GameReleasePolicy.IsJsonObjectDocument(body) ||
                !GameReleasePolicy.TryParse(PlayFabSimpleJson.DeserializeObject(body) as IDictionary<string, object>, out parsed))
            { SetFailure("Update information could not be read."); return; }
        }
        catch (Exception) { SetFailure("Update information could not be read."); return; }
        if (!parsed.Initialized)
        {
            State = GameReleaseFetchState.Unconfigured;
            StatusMessage = "Updates are not configured on the website yet." + SavedNotesSuffix();
            return;
        }
        Snapshot = parsed;
        State = GameReleaseFetchState.Ready;
        LastVerifiedUtc = CacheFetchedAtUtc = DateTime.UtcNow;
        StatusMessage = HasVerifiedUpdate ? "New game update available." :
            !GameReleasePolicy.HasDownload(parsed) ? "No downloadable build is currently published." :
            !InstalledVersionCode.HasValue || !parsed.Current.AndroidVersionCode.HasValue ? "Build version information is unavailable." :
            "No newer update available.";
        if (!WriteCache()) StatusMessage += " Notes could not be saved for offline reading.";
    }

    private void SetFailure(string message)
    {
        State = GameReleaseFetchState.Failed;
        StatusMessage = message + SavedNotesSuffix();
        NotifyChanged();
    }

    private string SavedNotesSuffix() => Snapshot != null ? " Showing last saved information." : " No saved release notes are available.";

    private string CachePath => Path.Combine(Application.persistentDataPath, CacheFileName);

    private void ReadCache()
    {
        try
        {
            string path = CachePath;
            if (!File.Exists(path) || new FileInfo(path).Length > GameReleasePolicy.MaxResponseBytes * 2L) return;
            string content = File.ReadAllText(path, Encoding.UTF8);
            if (!GameReleasePolicy.IsJsonObjectDocument(content)) return;
            var envelope = PlayFabSimpleJson.DeserializeObject(content) as IDictionary<string, object>;
            if (envelope == null || !envelope.TryGetValue("schema", out object schema) ||
                !(schema is ulong unsignedSchema && unsignedSchema == 1UL || schema is long signedSchema && signedSchema == 1L) ||
                !envelope.TryGetValue("fetchedAtUtc", out object stamp) || !(stamp is string timestamp) ||
                !DateTime.TryParseExact(timestamp, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime fetched) ||
                fetched.Kind != DateTimeKind.Utc || fetched > DateTime.UtcNow.AddMinutes(5) ||
                !envelope.TryGetValue("feed", out object feed) ||
                !GameReleasePolicy.TryParse(feed as IDictionary<string, object>, out GameReleaseSnapshot cached) || !cached.Initialized) return;
            Snapshot = cached;
            CacheFetchedAtUtc = fetched;
            StatusMessage = "Showing last saved information. Connect to check for updates.";
        }
        catch (Exception) { /* A public cache can be discarded without affecting player saves. */ }
    }

    private bool WriteCache()
    {
        try
        {
            var envelope = new Dictionary<string, object>
            {
                ["schema"] = 1L, ["fetchedAtUtc"] = CacheFetchedAtUtc.Value.ToString("O", CultureInfo.InvariantCulture),
                ["feed"] = GameReleasePolicy.PublicFeed(Snapshot)
            };
            string text = PlayFabSimpleJson.SerializeObject(envelope);
            if (Encoding.UTF8.GetByteCount(text) > GameReleasePolicy.MaxResponseBytes * 2) return false;
            Directory.CreateDirectory(Application.persistentDataPath);
            File.WriteAllText(CachePath, text, Encoding.UTF8);
            return true;
        }
        catch (Exception) { return false; }
    }

    private void OnDisable()
    {
        if (activeRequest != null) activeRequest.Abort();
        if (fetchRoutine != null) StopCoroutine(fetchRoutine);
        fetchRoutine = null;
        if (activeRequest != null) activeRequest.Dispose();
        activeRequest = null;
        if (IsRequestInFlight)
        {
            IsRequestInFlight = false;
            State = GameReleaseFetchState.Failed;
            StatusMessage = "Update check interrupted." + SavedNotesSuffix();
        }
    }

    private void NotifyChanged()
    {
        if (Changed == null) return;
        foreach (Action callback in Changed.GetInvocationList())
            try { callback(); }
            catch (Exception error) { Debug.LogWarning("[Updates] UI listener failed: " + error.GetType().Name); }
    }
}

public static class GameInstalledVersionReader
{
    public static long? ReadAndroidCode()
    {
#if UNITY_EDITOR
        return UnityEditor.PlayerSettings.Android.bundleVersionCode > 0 ? (long?)UnityEditor.PlayerSettings.Android.bundleVersionCode : null;
#elif UNITY_ANDROID
        try
        {
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var manager = activity.Call<AndroidJavaObject>("getPackageManager"))
            using (var package = manager.Call<AndroidJavaObject>("getPackageInfo", activity.Call<string>("getPackageName"), 0))
            using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
            {
                long code = version.GetStatic<int>("SDK_INT") >= 28 ? package.Call<long>("getLongVersionCode") : package.Get<int>("versionCode");
                return code > 0 ? (long?)code : null;
            }
        }
        catch (Exception) { return null; }
#else
        return null;
#endif
    }
}

/// <summary>Limits both the receive buffer and complete public JSON body.</summary>
public sealed class BoundedReleaseDownload : DownloadHandlerScript
{
    private readonly MemoryStream received = new MemoryStream();
    public bool Overflowed { get; private set; }
    public string Body => Encoding.UTF8.GetString(received.ToArray());
    public BoundedReleaseDownload() : base(new byte[4096]) { }
    protected override bool ReceiveData(byte[] data, int length)
    {
        if (data == null || length < 0 || length > data.Length || received.Length + length > GameReleasePolicy.MaxResponseBytes)
        { Overflowed = true; return false; }
        if (length > 0) received.Write(data, 0, length);
        return true;
    }
}
