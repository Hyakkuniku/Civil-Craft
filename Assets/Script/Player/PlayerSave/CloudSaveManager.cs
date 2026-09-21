using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using PlayFab;
using PlayFab.ClientModels;
using PlayFab.DataModels;
using PlayFab.Internal;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using EntityKey = PlayFab.DataModels.EntityKey;

/// <summary>
/// Synchronizes one account's local save with one PlayFab entity file. Local
/// commits remain authoritative while offline; divergent copies require a choice.
/// </summary>
[DisallowMultipleComponent]
public sealed class CloudSaveManager : MonoBehaviour
{
    public enum SaveAvailability { Unavailable, Empty, Online, LocalOnly }
    public enum StartMode { Resume, StartFresh, UseGuestSave, LoadOnline, LoadLocalAccount }

    private const string CloudFileName = "playerSaveData.json";
    private const float RetrySeconds = 30f;
    private const float SaveDebounceSeconds = 8f;

    [Serializable]
    private sealed class CloudEnvelope
    {
        public int schemaVersion = 1;
        public string accountId;
        public string playerDataJson;
        public string sha256;
    }

    [Serializable]
    private sealed class SyncState
    {
        public string lastSyncedHash;
    }

    private PlayerDataManager saveManager;
    private EntityKey entity;
    private string accountId;
    private string syncStatePath;
    private string lastSyncedHash;
    private string conflictingCloudJson;
    private string conflictingCloudHash;
    private bool hadLocalSaveAtLogin;
    private bool inFlight;
    private bool retryRequired;
    private bool showingConflict;
    private bool conflictBackedUp;
    private float nextSyncTime;
    private int generation;
    private Action<bool> startupComplete;
    private GameObject conflictOverlay;
    private GameObject conflictEventSystem;
    private bool pausedForConflict;
    private float previousTimeScale;
    private StartMode startMode;
    private bool strictStartup;
    private bool suspendSyncForConflict;

    public bool IsSyncing => inFlight;
    public bool HasConflict => showingConflict;
    public bool IsAccountActive => !string.IsNullOrEmpty(accountId);
    public string LastSyncError { get; private set; }

    private void Awake()
    {
        saveManager = GetComponent<PlayerDataManager>();
        if (saveManager != null) saveManager.OnSaveCommitted += OnLocalSaveCommitted;
    }

    private void OnDestroy()
    {
        if (saveManager != null) saveManager.OnSaveCommitted -= OnLocalSaveCommitted;
    }

    public void EndSession()
    {
        generation++;
        inFlight = false;
        retryRequired = false;
        suspendSyncForConflict = false;
        strictStartup = false;
        showingConflict = false;
        conflictBackedUp = false;
        CloseConflictOverlay();
        startupComplete = null;
        accountId = null;
        entity = null;
        conflictingCloudJson = null;
        lastSyncedHash = null;
        LastSyncError = null;
    }

    /// <summary>Checks cloud existence without changing the active guest save.</summary>
    public void InspectAccount(LoginResult login, Action<SaveAvailability> onResult)
    {
        if (saveManager == null || login?.EntityToken?.Entity == null ||
            string.IsNullOrWhiteSpace(login.PlayFabId))
        {
            onResult?.Invoke(SaveAvailability.Unavailable);
            return;
        }

        var key = new EntityKey
        {
            Id = login.EntityToken.Entity.Id,
            Type = login.EntityToken.Entity.Type
        };
        PlayFabDataAPI.GetFiles(new GetFilesRequest { Entity = key }, result =>
        {
            if (result.Metadata != null && result.Metadata.ContainsKey(CloudFileName))
                onResult?.Invoke(SaveAvailability.Online);
            else
                onResult?.Invoke(saveManager.HasAccountSave(login.PlayFabId)
                    ? SaveAvailability.LocalOnly : SaveAvailability.Empty);
        }, error =>
        {
            Debug.LogWarning("[CloudSave] Could not inspect account: " + error.ErrorMessage, this);
            onResult?.Invoke(SaveAvailability.Unavailable);
        });
    }

    public void BeginSession(LoginResult login, StartMode mode, Action<bool> onReady)
    {
        EndSession();
        if (saveManager == null || login == null || login.EntityToken?.Entity == null ||
            string.IsNullOrWhiteSpace(login.PlayFabId))
        {
            Debug.LogError("[CloudSave] Login did not include a usable player entity.", this);
            onReady?.Invoke(false);
            return;
        }

        bool alreadyCached = saveManager.HasAccountSave(login.PlayFabId);
        if (((mode == StartMode.StartFresh || mode == StartMode.UseGuestSave) && alreadyCached) ||
            (mode == StartMode.LoadLocalAccount && !alreadyCached))
        {
            Debug.LogWarning("[CloudSave] Account save changed before the choice was applied.", this);
            onReady?.Invoke(false);
            return;
        }

        if (!saveManager.UseAccountSave(login.PlayFabId, mode == StartMode.UseGuestSave,
                out hadLocalSaveAtLogin))
        {
            onReady?.Invoke(false);
            return;
        }

        startMode = mode;
        strictStartup = mode != StartMode.Resume;
        accountId = login.PlayFabId;
        entity = new EntityKey
        {
            Id = login.EntityToken.Entity.Id,
            Type = login.EntityToken.Entity.Type
        };
        syncStatePath = saveManager.CurrentSavePath + ".cloudstate";
        lastSyncedHash = ReadSyncState();
        startupComplete = onReady;
        nextSyncTime = Time.realtimeSinceStartup;
        SyncNow();
    }

    private void OnLocalSaveCommitted()
    {
        if (IsAccountActive)
        {
            hadLocalSaveAtLogin = true;
            nextSyncTime = Time.realtimeSinceStartup + SaveDebounceSeconds;
        }
    }

    private void Update()
    {
        if (!IsAccountActive || inFlight || showingConflict || suspendSyncForConflict ||
            Time.realtimeSinceStartup < nextSyncTime ||
            !PlayFabClientAPI.IsClientLoggedIn()) return;

        if (retryRequired || Hash(saveManager.GetCurrentDataJson()) != lastSyncedHash)
            SyncNow();
        else
            nextSyncTime = Time.realtimeSinceStartup + RetrySeconds;
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused && IsAccountActive && !inFlight && !showingConflict && !suspendSyncForConflict &&
            PlayFabClientAPI.IsClientLoggedIn() &&
            Hash(saveManager.GetCurrentDataJson()) != lastSyncedHash)
            SyncNow();
    }

    private void SyncNow()
    {
        if (inFlight || showingConflict || entity == null) return;
        inFlight = true;
        int requestGeneration = generation;
        PlayFabDataAPI.GetFiles(new GetFilesRequest { Entity = entity },
            result =>
            {
                if (requestGeneration != generation) return;
                if (result.Metadata == null ||
                    !result.Metadata.TryGetValue(CloudFileName, out GetFileMetadata metadata))
                {
                    if (startupComplete != null && startMode == StartMode.LoadOnline)
                    {
                        Fail("The account's online save is no longer available.");
                        return;
                    }
                    if (!string.IsNullOrEmpty(lastSyncedHash))
                    {
                        Fail("Cloud save disappeared; the local copy was preserved.");
                        return;
                    }
                    UploadLocal(result.ProfileVersion, requestGeneration);
                    return;
                }
                if (metadata == null || string.IsNullOrEmpty(metadata.DownloadUrl))
                {
                    Fail("Cloud save has no download URL.");
                    return;
                }
                PlayFabHttp.SimpleGetCall(metadata.DownloadUrl,
                    bytes =>
                    {
                        if (requestGeneration != generation) return;
                        ReceiveCloud(bytes, result.ProfileVersion, requestGeneration);
                    }, error =>
                    {
                        if (requestGeneration == generation) Fail("Download failed: " + error);
                    });
            }, error =>
            {
                if (requestGeneration == generation) Fail("Cloud lookup failed: " + error.ErrorMessage);
            });
    }

    private void ReceiveCloud(byte[] bytes, int profileVersion, int requestGeneration)
    {
        CloudEnvelope cloud;
        try
        {
            cloud = JsonUtility.FromJson<CloudEnvelope>(Encoding.UTF8.GetString(bytes));
            if (cloud == null || cloud.schemaVersion != 1 || cloud.accountId != accountId ||
                string.IsNullOrWhiteSpace(cloud.playerDataJson) ||
                cloud.sha256 != Hash(cloud.playerDataJson) ||
                JsonUtility.FromJson<PlayerData>(cloud.playerDataJson) == null)
                throw new InvalidDataException("Invalid or unsupported cloud-save file.");
        }
        catch (Exception exception)
        {
            Fail("Cloud save could not be validated: " + exception.Message);
            return;
        }

        string localHash = Hash(saveManager.GetCurrentDataJson());
        if (startupComplete != null &&
            (startMode == StartMode.StartFresh || startMode == StartMode.UseGuestSave ||
             startMode == StartMode.LoadLocalAccount))
        {
            Fail("An online save appeared before this choice was completed. Please sign in again.");
            return;
        }

        if (startupComplete != null && startMode == StartMode.LoadOnline)
        {
            if (hadLocalSaveAtLogin && localHash != cloud.sha256 &&
                !PreserveConflictCopies(saveManager.GetCurrentDataJson(), cloud.playerDataJson))
            {
                Fail("The device save could not be backed up before switching.");
                return;
            }
            if (localHash != cloud.sha256 &&
                !saveManager.TryApplyCloudData(cloud.playerDataJson, out string applyError))
            {
                Fail("The online save could not be opened: " + applyError);
                return;
            }
            saveManager.IgnoreLegacyProgressPrefsForCurrentAccount();
            MarkSynced(cloud.sha256);
            FinishOperation();
            return;
        }

        if (localHash == cloud.sha256)
        {
            MarkSynced(cloud.sha256);
            FinishOperation();
        }
        else if (!hadLocalSaveAtLogin || localHash == lastSyncedHash)
        {
            if (!saveManager.TryApplyCloudData(cloud.playerDataJson, out string error))
            {
                Fail("Cloud save could not be applied: " + error);
                return;
            }
            saveManager.IgnoreLegacyProgressPrefsForCurrentAccount();
            MarkSynced(cloud.sha256);
            FinishOperation();
        }
        else if (cloud.sha256 == lastSyncedHash)
        {
            UploadLocal(profileVersion, requestGeneration);
        }
        else
        {
            conflictBackedUp = PreserveConflictCopies(saveManager.GetCurrentDataJson(), cloud.playerDataJson);
            conflictingCloudJson = cloud.playerDataJson;
            conflictingCloudHash = cloud.sha256;
            showingConflict = true;
            inFlight = false;
            ShowConflictOverlay();
        }
        hadLocalSaveAtLogin = true;
    }

    private void UploadLocal(int profileVersion, int requestGeneration)
    {
        string playerJson = saveManager.GetCurrentDataJson();
        string uploadedHash = Hash(playerJson);
        byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(new CloudEnvelope
        {
            accountId = accountId,
            playerDataJson = playerJson,
            sha256 = uploadedHash
        }));

        PlayFabDataAPI.InitiateFileUploads(new InitiateFileUploadsRequest
        {
            Entity = entity,
            FileNames = new List<string> { CloudFileName },
            ProfileVersion = profileVersion
        }, result =>
        {
            if (requestGeneration != generation) return;
            if (result.UploadDetails == null || result.UploadDetails.Count != 1 ||
                string.IsNullOrWhiteSpace(result.UploadDetails[0].UploadUrl))
            {
                Fail("PlayFab did not provide an upload URL.");
                return;
            }
            PlayFabHttp.SimplePutCall(result.UploadDetails[0].UploadUrl, bytes,
                _ =>
                {
                    if (requestGeneration != generation) return;
                    PlayFabDataAPI.FinalizeFileUploads(new FinalizeFileUploadsRequest
                    {
                        Entity = entity,
                        FileNames = new List<string> { CloudFileName },
                        ProfileVersion = result.ProfileVersion
                    }, finalized =>
                    {
                        if (requestGeneration != generation) return;
                        MarkSynced(uploadedHash);
                        FinishOperation();
                    }, error =>
                    {
                        if (requestGeneration == generation) Fail("Finalizing save failed: " + error.ErrorMessage);
                    });
                }, error =>
                {
                    if (requestGeneration == generation) Fail("Upload failed: " + error);
                });
        }, error =>
        {
            if (requestGeneration == generation) Fail("Starting upload failed: " + error.ErrorMessage);
        });
    }

    private void MarkSynced(string hash)
    {
        lastSyncedHash = hash;
        try
        {
            string temporaryPath = syncStatePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonUtility.ToJson(new SyncState { lastSyncedHash = hash }));
            if (File.Exists(syncStatePath)) File.Delete(syncStatePath);
            File.Move(temporaryPath, syncStatePath);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[CloudSave] Could not record sync state: " + exception.Message, this);
        }
    }

    private string ReadSyncState()
    {
        try
        {
            if (File.Exists(syncStatePath))
                return JsonUtility.FromJson<SyncState>(File.ReadAllText(syncStatePath))?.lastSyncedHash;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[CloudSave] Could not read sync state: " + exception.Message, this);
        }
        return null;
    }

    private void FinishOperation()
    {
        inFlight = false;
        retryRequired = false;
        strictStartup = false;
        hadLocalSaveAtLogin = true;
        LastSyncError = null;
        nextSyncTime = Time.realtimeSinceStartup + SaveDebounceSeconds;
        Action<bool> ready = startupComplete;
        startupComplete = null;
        ready?.Invoke(true);
    }

    private void Fail(string message)
    {
        LastSyncError = message;
        Debug.LogWarning("[CloudSave] " + message + " Local progress remains available; sync will retry.", this);
        inFlight = false;
        retryRequired = true;
        nextSyncTime = Time.realtimeSinceStartup + RetrySeconds;
        Action<bool> ready = startupComplete;
        startupComplete = null;
        ready?.Invoke(!strictStartup);
    }

    private bool PreserveConflictCopies(string localJson, string onlineJson)
    {
        try
        {
            string directory = Path.Combine(Path.GetDirectoryName(saveManager.CurrentSavePath), "ConflictBackups");
            Directory.CreateDirectory(directory);
            string id = DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" +
                Guid.NewGuid().ToString("N").Substring(0, 8);
            File.WriteAllText(Path.Combine(directory, id + "-device.json"), localJson);
            File.WriteAllText(Path.Combine(directory, id + "-online.json"), onlineJson);
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[CloudSave] Could not back up conflicting copies: " + exception.Message, this);
            return false;
        }
    }

    private void ShowConflictOverlay()
    {
        CloseConflictOverlay();
        previousTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        pausedForConflict = true;
        conflictOverlay = new GameObject("Cloud Save Conflict", typeof(RectTransform),
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        conflictOverlay.transform.SetParent(transform, false);
        Canvas canvas = conflictOverlay.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 32767;
        CanvasScaler scaler = conflictOverlay.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        Image backdrop = MakeImage("Blocker", conflictOverlay.transform,
            new Color(0f, 0f, 0f, 0.85f));
        SetAnchors(backdrop.rectTransform, Vector2.zero, Vector2.one);

        Image panel = MakeImage("Choice Panel", backdrop.transform,
            new Color(0.11f, 0.16f, 0.22f, 1f));
        RectTransform panelRect = panel.rectTransform;
        panelRect.anchorMin = new Vector2(0.07f, 0.5f);
        panelRect.anchorMax = new Vector2(0.93f, 0.5f);
        panelRect.sizeDelta = new Vector2(0f, 470f);
        panelRect.anchoredPosition = Vector2.zero;

        MakeText("Title", panel.transform, "Choose a save", 46, FontStyle.Bold,
            new Vector2(0.05f, 0.75f), new Vector2(0.95f, 0.95f));
        MakeText("Explanation", panel.transform,
            "This device and your online account have different progress. Neither will replace the other. " +
            (conflictBackedUp ? "Both copies are backed up on this device." :
                "Your online save will not be changed."),
            31, FontStyle.Normal, new Vector2(0.07f, 0.34f), new Vector2(0.93f, 0.75f));

        MakeButton("Keep Device Copy", panel.transform, new Vector2(0.05f, 0.07f),
            new Vector2(0.48f, 0.29f), () => ResolveConflict(true));
        MakeButton("Use Online Save", panel.transform, new Vector2(0.52f, 0.07f),
            new Vector2(0.95f, 0.29f), () => ResolveConflict(false));

        if (EventSystem.current == null)
        {
            conflictEventSystem = new GameObject("Cloud Save Event System",
                typeof(EventSystem), typeof(StandaloneInputModule));
            conflictEventSystem.transform.SetParent(transform, false);
        }
    }

    private static Image MakeImage(string name, Transform parent, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        Image image = go.GetComponent<Image>();
        image.color = color;
        return image;
    }

    private static void SetAnchors(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static Text MakeText(string name, Transform parent, string value, int size,
        FontStyle style, Vector2 min, Vector2 max)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        Text label = go.GetComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        label.text = value;
        label.fontSize = size;
        label.fontStyle = style;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.resizeTextForBestFit = true;
        label.resizeTextMinSize = 18;
        label.resizeTextMaxSize = size;
        SetAnchors(label.rectTransform, min, max);
        return label;
    }

    private static void MakeButton(string label, Transform parent, Vector2 min, Vector2 max,
        UnityEngine.Events.UnityAction onClick)
    {
        Image image = MakeImage(label, parent, new Color(0.16f, 0.44f, 0.7f, 1f));
        SetAnchors(image.rectTransform, min, max);
        Button button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(onClick);
        MakeText("Label", image.transform, label, 30, FontStyle.Bold, Vector2.zero, Vector2.one);
    }

    private void CloseConflictOverlay()
    {
        if (conflictOverlay != null) Destroy(conflictOverlay);
        if (conflictEventSystem != null) Destroy(conflictEventSystem);
        conflictOverlay = null;
        conflictEventSystem = null;
        if (pausedForConflict)
        {
            Time.timeScale = previousTimeScale;
            pausedForConflict = false;
        }
    }

    private void ResolveConflict(bool useDevice)
    {
        showingConflict = false;
        CloseConflictOverlay();
        if (useDevice)
        {
            // Continue with the cached account data without touching the online
            // version. Reconciliation is suspended until the next sign-in.
            suspendSyncForConflict = true;
            inFlight = false;
            LastSyncError = "Device and online saves differ. The online save was not replaced.";
            Action<bool> ready = startupComplete;
            startupComplete = null;
            ready?.Invoke(true);
        }
        else
        {
            if (!saveManager.TryApplyCloudData(conflictingCloudJson, out string error))
            {
                Fail("Chosen online save could not be applied: " + error);
                return;
            }
            saveManager.IgnoreLegacyProgressPrefsForCurrentAccount();
            MarkSynced(conflictingCloudHash);
            FinishOperation();
        }
        conflictingCloudJson = null;
        conflictingCloudHash = null;
    }

    private static string Hash(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        using (SHA256 sha = SHA256.Create())
        {
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }
}
