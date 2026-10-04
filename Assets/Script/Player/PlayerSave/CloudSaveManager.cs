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
using EntityKey = PlayFab.DataModels.EntityKey;

/// <summary>
/// Synchronizes one account's encrypted gameplay save with PlayFab. Local commits
/// remain authoritative while offline; divergent copies require a choice.
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
        public int schemaVersion = 2;
        public string accountId;
        public string encryptedPlayerData;
        // Retained only so version-1 cloud saves can be migrated.
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
    private int conflictingCloudProfileVersion;
    private int conflictingCloudGeneration;
    private bool conflictingCloudNeedsEncryptionUpgrade;
    private bool hadLocalSaveAtLogin;
    private bool inFlight;
    private bool retryRequired;
    private bool showingConflict;
    private bool conflictBackedUp;
    private float nextSyncTime;
    private int generation;
    private Action<bool> startupComplete;
    [Header("Authored Conflict Dialog")]
    [SerializeField] private AuthoredSaveChoiceDialog conflictDialog;
    private bool pausedForConflict;
    private float previousTimeScale;
    private StartMode startMode;
    private bool strictStartup;
    private bool suspendSyncForConflict;
    private bool auxiliaryFileOperationInProgress;

    public bool IsSyncing => inFlight;
    public bool HasConflict => showingConflict;
    public bool IsAccountActive => !string.IsNullOrEmpty(accountId);
    public string ActiveAccountId => accountId;
    public int SessionGeneration => generation;
    // A selected account is not ready until its startup save choice has been
    // approved. Resume may approve the cached save after a network failure;
    // strict save choices must succeed before dependent publishers can run.
    public bool IsSessionReady { get; private set; }
    public bool CanPublishDashboard => IsAccountActive && IsSessionReady &&
        !showingConflict && !suspendSyncForConflict;
    public string LastSyncError { get; private set; }

    private void Awake()
    {
        saveManager = GetComponent<PlayerDataManager>();
        if (saveManager != null) saveManager.OnSaveCommitted += OnLocalSaveCommitted;
        if (conflictDialog == null)
            conflictDialog = GetComponentInChildren<AuthoredSaveChoiceDialog>(true);
        if (conflictDialog != null) conflictDialog.Hide();
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
        auxiliaryFileOperationInProgress = false;
        strictStartup = false;
        showingConflict = false;
        conflictBackedUp = false;
        CloseConflictOverlay();
        startupComplete = null;
        IsSessionReady = false;
        accountId = null;
        entity = null;
        conflictingCloudJson = null;
        conflictingCloudHash = null;
        conflictingCloudProfileVersion = 0;
        conflictingCloudGeneration = 0;
        conflictingCloudNeedsEncryptionUpgrade = false;
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
        if (!IsAccountActive || inFlight || auxiliaryFileOperationInProgress ||
            showingConflict || suspendSyncForConflict ||
            Time.realtimeSinceStartup < nextSyncTime ||
            !PlayFabClientAPI.IsClientLoggedIn()) return;

        if (retryRequired || Hash(saveManager.GetCurrentDataJson()) != lastSyncedHash)
            SyncNow();
        else
            nextSyncTime = Time.realtimeSinceStartup + RetrySeconds;
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused && IsAccountActive && !inFlight && !auxiliaryFileOperationInProgress &&
            !showingConflict && !suspendSyncForConflict &&
            PlayFabClientAPI.IsClientLoggedIn())
        {
            if (Hash(saveManager.GetCurrentDataJson()) != lastSyncedHash)
                SyncNow();
        }
    }

    private void SyncNow()
    {
        if (inFlight || auxiliaryFileOperationInProgress || showingConflict || entity == null) return;
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

    /// <summary>
    /// Reserves the entity-file upload slot for a secondary file such as the
    /// wardrobe portrait. PlayFab permits only one pending file transaction on
    /// an entity, so this prevents the encrypted save and portrait from racing.
    /// </summary>
    public bool TryBeginAuxiliaryFileOperation(out EntityKey entityKey)
    {
        entityKey = null;
        if (!IsAccountActive || entity == null || inFlight || auxiliaryFileOperationInProgress ||
            showingConflict || suspendSyncForConflict || !PlayFabClientAPI.IsClientLoggedIn())
            return false;

        auxiliaryFileOperationInProgress = true;
        entityKey = new EntityKey { Id = entity.Id, Type = entity.Type };
        return true;
    }

    public void EndAuxiliaryFileOperation()
    {
        auxiliaryFileOperationInProgress = false;
        nextSyncTime = Mathf.Min(nextSyncTime, Time.realtimeSinceStartup + 0.25f);
    }

    private void ReceiveCloud(byte[] bytes, int profileVersion, int requestGeneration)
    {
        CloudEnvelope cloud;
        bool needsEncryptionUpgrade;
        try
        {
            cloud = JsonUtility.FromJson<CloudEnvelope>(Encoding.UTF8.GetString(bytes));
            if (cloud == null || cloud.accountId != accountId)
                throw new InvalidDataException("Invalid or unsupported cloud-save file.");

            needsEncryptionUpgrade = cloud.schemaVersion == 1;
            if (cloud.schemaVersion == 2)
            {
                if (!SaveEncryption.TryDecryptOrReadLegacy(cloud.encryptedPlayerData,
                        GetCloudEncryptionPurpose(), out string decryptedJson,
                        out bool wasLegacy, out string encryptionError) || wasLegacy)
                    throw new InvalidDataException("Cloud payload authentication failed: " + encryptionError);
                cloud.playerDataJson = decryptedJson;
            }
            else if (cloud.schemaVersion != 1)
            {
                throw new InvalidDataException("Invalid or unsupported cloud-save file.");
            }

            if (string.IsNullOrWhiteSpace(cloud.playerDataJson) ||
                cloud.sha256 != Hash(cloud.playerDataJson) ||
                JsonUtility.FromJson<PlayerData>(cloud.playerDataJson) == null)
                throw new InvalidDataException("Invalid or unsupported cloud-save data.");
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
            if (needsEncryptionUpgrade) UploadLocal(profileVersion, requestGeneration);
            else
            {
                MarkSynced(cloud.sha256);
                FinishOperation();
            }
            return;
        }

        if (localHash == cloud.sha256)
        {
            if (needsEncryptionUpgrade) UploadLocal(profileVersion, requestGeneration);
            else
            {
                MarkSynced(cloud.sha256);
                FinishOperation();
            }
        }
        else if (!hadLocalSaveAtLogin || localHash == lastSyncedHash)
        {
            if (!saveManager.TryApplyCloudData(cloud.playerDataJson, out string error))
            {
                Fail("Cloud save could not be applied: " + error);
                return;
            }
            saveManager.IgnoreLegacyProgressPrefsForCurrentAccount();
            if (needsEncryptionUpgrade) UploadLocal(profileVersion, requestGeneration);
            else
            {
                MarkSynced(cloud.sha256);
                FinishOperation();
            }
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
            conflictingCloudProfileVersion = profileVersion;
            conflictingCloudGeneration = requestGeneration;
            conflictingCloudNeedsEncryptionUpgrade = needsEncryptionUpgrade;
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
            encryptedPlayerData = SaveEncryption.Encrypt(playerJson, GetCloudEncryptionPurpose()),
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
        IsSessionReady = true;
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
        if (ready != null) IsSessionReady = !strictStartup;
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
            string purpose = "conflict-backup:" + accountId;
            File.WriteAllText(Path.Combine(directory, id + "-device.json"),
                SaveEncryption.Encrypt(localJson, purpose));
            File.WriteAllText(Path.Combine(directory, id + "-online.json"),
                SaveEncryption.Encrypt(onlineJson, purpose));
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
        if (conflictDialog == null)
        {
            showingConflict = false;
            Fail("The authored cloud-save conflict dialog is missing.");
            return;
        }

        previousTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        pausedForConflict = true;
        string explanation =
            "This device and your online account have different progress. Neither will replace the other. " +
            (conflictBackedUp ? "Both copies are backed up on this device." :
                "Your online save will not be changed.");
        if (!conflictDialog.Show("CHOOSE A SAVE", explanation, "Keep Device Copy",
                "Use Online Save", () => ResolveConflict(true), () => ResolveConflict(false)))
        {
            CloseConflictOverlay();
            showingConflict = false;
            Fail("The authored cloud-save conflict dialog is incomplete.");
        }
    }

    private void CloseConflictOverlay()
    {
        if (conflictDialog != null) conflictDialog.Hide();
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
            IsSessionReady = true;
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
            if (conflictingCloudNeedsEncryptionUpgrade)
            {
                inFlight = true;
                UploadLocal(conflictingCloudProfileVersion, conflictingCloudGeneration);
            }
            else
            {
                MarkSynced(conflictingCloudHash);
                FinishOperation();
            }
        }
        conflictingCloudJson = null;
        conflictingCloudHash = null;
        conflictingCloudProfileVersion = 0;
        conflictingCloudGeneration = 0;
        conflictingCloudNeedsEncryptionUpgrade = false;
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

    private string GetCloudEncryptionPurpose()
    {
        return "cloud-account:" + accountId;
    }
}
