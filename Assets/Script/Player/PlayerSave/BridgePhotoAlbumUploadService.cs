using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using PlayFab;
using PlayFab.DataModels;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>One private, bounded album per player. A failed upload never changes game progression.</summary>
[DisallowMultipleComponent]
public sealed class BridgePhotoAlbumUploadService : MonoBehaviour
{
    [Serializable] private sealed class QueueState
    {
        public int schemaVersion = 1;
        public string accountId;
        public List<BridgePhotoAlbumEntry> photos = new List<BridgePhotoAlbumEntry>();
        // Only a transaction whose successful initiation was durably recorded
        // can be retried/finalized/aborted. Unknown pending transactions are not ours to abort.
        public int pendingProfileVersion = -1;
        public string candidateHash;
    }

    private PlayerDataManager dataManager;
    private CloudSaveManager cloudSave;
    private CloudSaveManager.AuxiliaryFileOperation reservation;
    private QueueState queue;
    private string directory;
    private int observedGeneration = int.MinValue;
    private string observedAccount;
    private bool inFlight;
    private float nextAttempt;
    public bool IsUploading => inFlight;
    public string LastUploadError { get; private set; }

    public static BridgePhotoAlbumUploadService For(PlayerDataManager manager)
    {
        if (manager == null) return null;
        return manager.GetComponent<BridgePhotoAlbumUploadService>() ??
            manager.gameObject.AddComponent<BridgePhotoAlbumUploadService>();
    }

    private void Awake()
    {
        dataManager = GetComponent<PlayerDataManager>();
        cloudSave = GetComponent<CloudSaveManager>();
    }
    private void OnDestroy() { Release(); }

    /// <summary>Called only for the original successful-result capture, never reconstructed legacy caches.</summary>
    public bool QueuePhoto(string contractId, string resultId, string regionId, string capturedAt, byte[] originalPng)
    {
        ObserveSession();
        if (queue == null || !BridgePhotoAlbum.IsResultId(resultId) || !Selected(contractId, resultId)) return false;
        try
        {
            byte[] image = Optimize(originalPng);
            var entry = new BridgePhotoAlbumEntry { contractId = contractId, regionId = regionId,
                resultId = resultId, entryName = BridgePhotoAlbum.EntryName(resultId),
                sha256 = BridgePhotoAlbum.Sha256(image), capturedAt = capturedAt };
            BridgePhotoAlbum.ValidateEntry(entry);
            Directory.CreateDirectory(directory);
            AtomicWrite(Path.Combine(directory, entry.entryName), image);
            var before = new List<BridgePhotoAlbumEntry>(queue.photos);
            queue.photos.RemoveAll(photo => photo.contractId == contractId && photo.regionId == regionId);
            if (queue.photos.Count >= BridgePhotoAlbum.MaximumPhotos)
            { queue.photos = before; throw new InvalidDataException("Website photo limit reached."); }
            queue.photos.Add(entry);
            try { Persist(); }
            catch { queue.photos = before; throw; }
            nextAttempt = Time.realtimeSinceStartup + 1f;
            LastUploadError = null;
            return true;
        }
        catch (Exception)
        {
            LastUploadError = "Bridge saved, but its website photo could not be queued. The original local photo is unchanged.";
            Debug.LogWarning("[BridgeAlbum] " + LastUploadError, this);
            return false;
        }
    }

    private void ObserveSession()
    {
        if (cloudSave == null || dataManager == null || (observedGeneration == cloudSave.SessionGeneration &&
            string.Equals(observedAccount, cloudSave.ActiveAccountId, StringComparison.Ordinal))) return;
        Release(); StopAllCoroutines(); inFlight = false;
        observedGeneration = cloudSave.SessionGeneration;
        observedAccount = cloudSave.ActiveAccountId;
        queue = null; directory = null; LastUploadError = null;
        if (string.IsNullOrEmpty(observedAccount) || observedAccount != dataManager.ActiveAccountId) return;
        directory = Path.Combine(Path.GetDirectoryName(dataManager.CurrentSavePath), "WebsiteBridgePhotos");
        try
        {
            string path = Path.Combine(directory, "queue.json");
            if (File.Exists(path))
            {
                var info = new FileInfo(path);
                if (info.Length > BridgePhotoAlbum.MaximumManifestBytes) throw new InvalidDataException();
                queue = JsonUtility.FromJson<QueueState>(File.ReadAllText(path));
                if (queue == null || queue.schemaVersion != 1 || queue.accountId != observedAccount || queue.photos == null ||
                    queue.photos.Count > BridgePhotoAlbum.MaximumPhotos) throw new InvalidDataException();
                var keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var photo in queue.photos)
                {
                    BridgePhotoAlbum.ValidateEntry(photo);
                    if (!keys.Add(photo.regionId + "\n" + photo.contractId)) throw new InvalidDataException();
                }
            }
            else queue = new QueueState { accountId = observedAccount };
        }
        catch (Exception)
        {
            queue = null;
            LastUploadError = "The local website-photo queue could not be verified. Existing photos were preserved.";
            Debug.LogWarning("[BridgeAlbum] " + LastUploadError, this);
        }
        nextAttempt = Time.realtimeSinceStartup + 1f;
    }

    private void Update()
    {
        ObserveSession();
        if (inFlight || queue == null || (queue.photos.Count == 0 && queue.pendingProfileVersion < 0) || cloudSave == null ||
            !cloudSave.CanPublishDashboard || Time.realtimeSinceStartup < nextAttempt ||
            !PlayFabClientAPI.IsClientLoggedIn()) return;
        // Retire obsolete queue references only after account save selection is settled.
        queue.photos.RemoveAll(photo => !Selected(photo.contractId, photo.resultId));
        if (queue.photos.Count == 0 && queue.pendingProfileVersion < 0)
        {
            try { Persist(); CleanupOptimizedCopies(); } catch (Exception) { LastUploadError = "The website-photo queue could not be saved."; }
            return;
        }
        if (!cloudSave.TryBeginAuxiliaryFileOperation(out EntityKey entity, out reservation))
        { nextAttempt = Time.realtimeSinceStartup + 1f; return; }
        inFlight = true;
        var operation = reservation;
        PlayFabDataAPI.GetFiles(new GetFilesRequest { Entity = entity }, files =>
        {
            if (!Current(operation)) return;
            if (files == null) { Fail(operation, "Website photo lookup returned no metadata. It will retry."); return; }
            if (!cloudSave.VerifyAuxiliaryFileProfile(operation, files.ProfileVersion))
            {
                reservation = null; inFlight = false;
                nextAttempt = Time.realtimeSinceStartup + 1f;
                return;
            }
            StartCoroutine(ReadAndMerge(files, operation));
        }, error => Fail(operation, "Website photo lookup failed. It will retry."));
    }

    private bool Selected(string contractId, string resultId) => dataManager != null &&
        observedAccount == dataManager.ActiveAccountId &&
        string.Equals(dataManager.GetSavedBridge(contractId)?.resultId, resultId, StringComparison.Ordinal);

    private bool Current(CloudSaveManager.AuxiliaryFileOperation operation) => this != null && operation != null &&
        ReferenceEquals(operation, reservation) && cloudSave != null && cloudSave.IsAuxiliaryFileOperationCurrent(operation) &&
        operation.AccountId == dataManager.ActiveAccountId && operation.AccountId == queue?.accountId;

    private IEnumerator ReadAndMerge(GetFilesResponse files, CloudSaveManager.AuxiliaryFileOperation operation)
    {
        BridgePhotoAlbum album = new BridgePhotoAlbum();
        if (files?.Metadata != null && files.Metadata.TryGetValue(BridgePhotoAlbum.FileName, out GetFileMetadata metadata))
        {
            if (metadata == null || metadata.Size > BridgePhotoAlbum.MaximumBytes || !StorageUrl(metadata.DownloadUrl))
            { Fail(operation, "The online bridge album could not be verified; no photo was replaced."); yield break; }
            var handler = new BoundedDownload(BridgePhotoAlbum.MaximumBytes);
            using (var request = new UnityWebRequest(metadata.DownloadUrl, "GET", handler, null))
            {
                request.timeout = 15; request.redirectLimit = 0;
                yield return request.SendWebRequest();
                if (!Current(operation)) yield break;
                if (request.result != UnityWebRequest.Result.Success)
                { Fail(operation, "The bridge album download failed. It will retry."); yield break; }
                try { album = BridgePhotoAlbum.Decode(handler.Bytes()); }
                catch (Exception) { Fail(operation, "The online bridge album is invalid; existing files were preserved."); yield break; }
            }
        }
        if (!Current(operation)) yield break;
        // A new local result may have been saved while the remote album was
        // downloading. Settle that save before merging a different selection.
        if (!cloudSave.VerifyAuxiliaryFileProfile(operation, files.ProfileVersion))
        {
            reservation = null; inFlight = false;
            nextAttempt = Time.realtimeSinceStartup + 1f;
            yield break;
        }
        var submitted = queue.photos.FindAll(photo => Selected(photo.contractId, photo.resultId));
        if (submitted.Count > 0 && submitted.TrueForAll(album.Contains))
        { Finish(operation, submitted); yield break; }
        // First reconcile our own acknowledged initiation after a crash/uncertain
        // finalization. Never abort a transaction inferred from timestamps or a generic pending error.
        if (queue.pendingProfileVersion >= 0 && files.ProfileVersion == queue.pendingProfileVersion)
        { RecoverOwnedTransaction(operation, files.ProfileVersion); yield break; }
        queue.pendingProfileVersion = -1; queue.candidateHash = null;
        if (submitted.Count == 0) { Finish(operation, submitted); yield break; }
        byte[] candidate;
        try
        {
            foreach (var photo in submitted)
            {
                string path = Path.Combine(directory, photo.entryName);
                if (!File.Exists(path) || new FileInfo(path).Length > BridgePhotoAlbum.MaximumImageBytes)
                    throw new InvalidDataException();
                album.Replace(photo, File.ReadAllBytes(path));
            }
            candidate = album.Encode();
            queue.candidateHash = BridgePhotoAlbum.Sha256(candidate);
            AtomicWrite(Path.Combine(directory, "candidate.zip"), candidate);
            Persist();
        }
        catch (Exception)
        { Fail(operation, "The bridge album cannot fit or be verified. Photos remain pending; none were removed."); yield break; }
        // This path belongs to the submitting account even if the user switches
        // accounts before PlayFab acknowledges initiation. Retaining that exact
        // receipt lets the old account recover its own slot on its next login.
        string submittedSavePath = dataManager.CurrentSavePath;
        PlayFabDataAPI.InitiateFileUploads(new InitiateFileUploadsRequest
        {
            Entity = operation.Entity, FileNames = new List<string> { BridgePhotoAlbum.FileName }, ProfileVersion = files.ProfileVersion
        }, initiated =>
        {
            if (initiated?.UploadDetails == null || initiated.UploadDetails.Count != 1 ||
                initiated.UploadDetails[0].FileName != BridgePhotoAlbum.FileName || !StorageUrl(initiated.UploadDetails[0].UploadUrl))
            { Fail(operation, "PlayFab did not provide the bridge album upload URL."); return; }
            try
            {
                EntityMediaUploadRecovery.Record(submittedSavePath, operation.AccountId, operation.Entity,
                    BridgePhotoAlbum.FileName, initiated.ProfileVersion, candidate);
                if (!Current(operation)) return;
                queue.pendingProfileVersion = initiated.ProfileVersion;
                Persist();
            }
            catch (Exception) { Fail(operation, "The album upload transaction could not be recorded. No PNG bytes were uploaded."); return; }
            StartCoroutine(PutCandidate(initiated.UploadDetails[0].UploadUrl, candidate, submitted, operation, initiated.ProfileVersion));
        }, error => Fail(operation, "The album could not start uploading. A retry will reread and merge the latest online album."));
    }

    private IEnumerator PutCandidate(string url, byte[] bytes, List<BridgePhotoAlbumEntry> submitted,
        CloudSaveManager.AuxiliaryFileOperation operation, int version)
    {
        if (!Current(operation)) yield break;
        using (var request = UnityWebRequest.Put(url, bytes))
        {
            request.timeout = 20; request.redirectLimit = 0;
            request.SetRequestHeader("Content-Type", "application/zip");
            yield return request.SendWebRequest();
            if (!Current(operation)) yield break;
            if (request.result != UnityWebRequest.Result.Success)
            { Fail(operation, "The album upload was interrupted. Its recorded transaction will be reconciled before retry."); yield break; }
        }
        PlayFabDataAPI.FinalizeFileUploads(new FinalizeFileUploadsRequest
        {
            Entity = operation.Entity, FileNames = new List<string> { BridgePhotoAlbum.FileName }, ProfileVersion = version
        }, finalized => { if (Current(operation)) Finish(operation, submitted); },
            error => Fail(operation, "Album finalization was not confirmed. Its receipt will be checked before retry."));
    }

    private void RecoverOwnedTransaction(CloudSaveManager.AuxiliaryFileOperation operation, int version)
    {
        // A candidate is durable evidence of exactly what this client initiated.
        string candidatePath = Path.Combine(directory, "candidate.zip");
        try
        {
            if (!File.Exists(candidatePath) || new FileInfo(candidatePath).Length > BridgePhotoAlbum.MaximumBytes ||
                BridgePhotoAlbum.Sha256(File.ReadAllBytes(candidatePath)) != queue.candidateHash)
                throw new InvalidDataException();
        }
        catch (Exception) { Fail(operation, "The pending album transaction needs review; its candidate could not be verified."); return; }
        PlayFabDataAPI.FinalizeFileUploads(new FinalizeFileUploadsRequest
        { Entity = operation.Entity, FileNames = new List<string> { BridgePhotoAlbum.FileName }, ProfileVersion = version },
        finalized =>
        {
            if (!Current(operation)) return;
            queue.pendingProfileVersion = -1;
            try { Persist(); } catch (Exception) { }
            // Reread the final file before recording any result, including after restart.
            Fail(operation, "The prior album upload finalized. Verifying its images before publication.", 1f);
        }, error =>
        {
            if (!Current(operation)) return;
            // Only this exact persisted profile generation can be aborted. If a
            // different device changed it, the CAS rejection preserves its operation.
            PlayFabDataAPI.AbortFileUploads(new AbortFileUploadsRequest
            { Entity = operation.Entity, FileNames = new List<string> { BridgePhotoAlbum.FileName }, ProfileVersion = version },
            aborted =>
            {
                if (!Current(operation)) return;
                queue.pendingProfileVersion = -1; queue.candidateHash = null;
                try { Persist(); } catch (Exception) { }
                Fail(operation, "The interrupted owned album upload was cleared. It will be rebuilt from the latest online album.", 1f);
            }, abortError => Fail(operation, "The pending upload changed or remains uncertain; no other transaction was aborted."));
        });
    }

    private void Finish(CloudSaveManager.AuxiliaryFileOperation operation, List<BridgePhotoAlbumEntry> submitted)
    {
        if (!Current(operation)) return;
        bool retry = false;
        foreach (var photo in submitted)
        {
            if (!Selected(photo.contractId, photo.resultId)) continue;
            var snapshot = new BridgePhotoSnapshot { storage = "archive-v1", fileName = BridgePhotoAlbum.FileName,
                contractId = photo.contractId, regionId = photo.regionId, resultId = photo.resultId,
                entryName = photo.entryName, sha256 = photo.sha256, capturedAt = photo.capturedAt };
            if (!dataManager.TryRecordBridgePhotoSnapshot(photo.contractId, photo.resultId, snapshot))
            { retry = true; continue; }
            queue.photos.RemoveAll(p => p.resultId == photo.resultId && p.sha256 == photo.sha256);
        }
        queue.pendingProfileVersion = -1; queue.candidateHash = null;
        try
        {
            Persist();
            if (!retry) EntityMediaUploadRecovery.MarkPublished(dataManager.CurrentSavePath, operation.AccountId, BridgePhotoAlbum.FileName);
            CleanupOptimizedCopies();
        }
        catch (Exception) { retry = true; }
        LastUploadError = retry ? "Album uploaded; local publication metadata is pending a safe retry." : null;
        Release(); inFlight = false; nextAttempt = Time.realtimeSinceStartup + (retry ? 15f : 1f);
    }
    private void Fail(CloudSaveManager.AuxiliaryFileOperation operation, string message, float delay = 15f)
    {
        if (!Current(operation)) return;
        LastUploadError = message;
        Release(); inFlight = false; nextAttempt = Time.realtimeSinceStartup + delay;
        Debug.LogWarning("[BridgeAlbum] " + message, this);
    }
    private void Release()
    {
        if (reservation != null && cloudSave != null) cloudSave.EndAuxiliaryFileOperation(reservation);
        reservation = null;
    }
    private void Persist()
    {
        if (queue == null || directory == null || queue.accountId != dataManager.ActiveAccountId)
            throw new InvalidDataException("Photo queue account changed.");
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(queue));
        if (bytes.Length > BridgePhotoAlbum.MaximumManifestBytes) throw new InvalidDataException("Photo queue exceeds its budget.");
        AtomicWrite(Path.Combine(directory, "queue.json"), bytes);
    }
    private void CleanupOptimizedCopies()
    {
        // These are generated website copies only, never the original gameplay
        // photo cache. Keep every pending copy until its queue is durably saved.
        if (queue == null || directory == null || queue.accountId != dataManager.ActiveAccountId ||
            !Directory.Exists(directory)) return;
        var retained = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in queue.photos) retained.Add(entry.entryName);
        foreach (string path in Directory.GetFiles(directory, "bridge-*.png", SearchOption.TopDirectoryOnly))
        {
            string name = Path.GetFileName(path);
            if (name.Length == 43 && BridgePhotoAlbum.IsResultId(name.Substring(7, 32)) &&
                !retained.Contains(name)) File.Delete(path);
        }
    }
    private static void AtomicWrite(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temporary = path + ".tmp";
        File.WriteAllBytes(temporary, bytes);
        if (File.Exists(path)) File.Replace(temporary, path, null);
        else File.Move(temporary, path);
    }
    private static bool StorageUrl(string text) => Uri.TryCreate(text, UriKind.Absolute, out Uri uri) &&
        uri.Scheme == Uri.UriSchemeHttps && uri.Host.EndsWith(".blob.core.windows.net", StringComparison.OrdinalIgnoreCase) &&
        string.IsNullOrEmpty(uri.UserInfo) && uri.IsDefaultPort;

    private sealed class BoundedDownload : DownloadHandlerScript
    {
        private readonly int maximum;
        private readonly MemoryStream buffer = new MemoryStream();
        public BoundedDownload(int limit) : base(new byte[64 * 1024]) { maximum = limit; }
        protected override bool ReceiveData(byte[] data, int length)
        {
            if (data == null || length < 0 || buffer.Length + length > maximum) return false;
            buffer.Write(data, 0, length); return true;
        }
        public byte[] Bytes() => buffer.ToArray();
    }

    private static byte[] Optimize(byte[] bytes)
    {
        if (bytes == null || bytes.Length < 33 || bytes.Length > 8 * 1024 * 1024)
            throw new InvalidDataException("Invalid original bridge photo.");
        // Avoid decoding a hostile tiny compressed image into an enormous texture.
        uint width = ReadBig(bytes, 16), height = ReadBig(bytes, 20);
        if (width == 0 || height == 0 || width > 8192 || height > 8192 || (long)width * height > 16777216)
            throw new InvalidDataException("Original bridge dimensions exceed the capture budget.");
        Texture2D source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!ImageConversion.LoadImage(source, bytes, false)) throw new InvalidDataException("Invalid PNG.");
            float ratio = Mathf.Min(1f, Mathf.Min((float)BridgePhotoAlbum.MaximumWidth / source.width,
                (float)BridgePhotoAlbum.MaximumHeight / source.height));
            for (int attempt = 0; attempt < 8; attempt++, ratio *= 0.8f)
            {
                int w = Mathf.Max(1, Mathf.FloorToInt(source.width * ratio));
                int h = Mathf.Max(1, Mathf.FloorToInt(source.height * ratio));
                RenderTexture previous = RenderTexture.active;
                RenderTexture target = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
                Texture2D output = new Texture2D(w, h, TextureFormat.RGBA32, false);
                try
                {
                    Graphics.Blit(source, target); RenderTexture.active = target;
                    output.ReadPixels(new Rect(0, 0, w, h), 0, 0); output.Apply(false, false);
                    byte[] png = output.EncodeToPNG();
                    if (png != null && png.Length <= BridgePhotoAlbum.MaximumImageBytes)
                    { BridgePhotoAlbum.ValidatePng(png); return png; }
                }
                finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); Destroy(output); }
            }
            throw new InvalidDataException("Bridge photo could not be optimized within budget.");
        }
        finally { Destroy(source); }
    }
    private static uint ReadBig(byte[] b, int p) => (uint)b[p] << 24 | (uint)b[p + 1] << 16 | (uint)b[p + 2] << 8 | b[p + 3];
}
