using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using PlayFab;
using PlayFab.DataModels;
using UnityEngine;

/// <summary>
/// Account-bound receipts for acknowledged media-file initiation. Recovery runs
/// before startup save selection, so an interrupted media transaction cannot
/// deadlock the encrypted save upload that makes media services ready.
/// </summary>
public static class EntityMediaUploadRecovery
{
    [Serializable] private sealed class Receipt
    {
        public int schemaVersion = 1;
        public string accountId;
        public string entityId;
        public string fileName;
        public int profileVersion;
        public string sha256;
        public bool resolved;
        public bool published;
    }
    private const int MaximumBytes = 8 * 1024 * 1024;
    private const string Album = "bridge-photos-v1.zip";
    private const string Portrait = "characterPortrait.png";

    public static void Record(string savePath, string accountId, EntityKey entity,
        string fileName, int profileVersion, byte[] candidate)
    {
        if (string.IsNullOrEmpty(accountId) || entity == null || entity.Type != "title_player_account" ||
            string.IsNullOrEmpty(entity.Id) || !Allowed(fileName) || profileVersion < 0 ||
            candidate == null || candidate.Length == 0 || candidate.Length > MaximumBytes)
            throw new InvalidDataException("Invalid media upload receipt.");
        Receipt prior = Load(savePath);
        if (prior != null && (prior.accountId != accountId || prior.entityId != entity.Id ||
            prior.profileVersion > profileVersion || (prior.profileVersion == profileVersion &&
                (prior.fileName != fileName || prior.sha256 != Hash(candidate)))))
            throw new InvalidDataException("A newer or different media transaction owns this receipt.");
        string directory = DirectoryPath(savePath);
        AtomicWrite(Path.Combine(directory, fileName + ".candidate"), candidate);
        Save(savePath, new Receipt { accountId = accountId, entityId = entity.Id,
            fileName = fileName, profileVersion = profileVersion, sha256 = Hash(candidate) });
    }

    public static bool HasUnpublished(string savePath, string accountId, string fileName)
    {
        try { Receipt receipt = Load(savePath); return receipt != null && receipt.accountId == accountId &&
            receipt.fileName == fileName && !receipt.published; }
        catch (Exception) { return false; }
    }

    public static void MarkPublished(string savePath, string accountId, string fileName)
    {
        Receipt receipt = Load(savePath);
        if (receipt == null || receipt.accountId != accountId || receipt.fileName != fileName) return;
        receipt.resolved = true; receipt.published = true; Save(savePath, receipt);
    }

    /// <returns>True when asynchronous recovery/failure owns this lookup; false to continue normal save selection.</returns>
    public static bool TryRecover(string savePath, string accountId, EntityKey entity,
        GetFilesResponse files, Func<bool> current, Action requery, Action<string> fail)
    {
        Receipt receipt;
        try
        {
            receipt = Load(savePath);
            if (receipt == null || receipt.resolved) return false;
            if (receipt.accountId != accountId || entity == null || receipt.entityId != entity.Id || entity.Type != "title_player_account")
                throw new InvalidDataException();
            string candidate = Path.Combine(DirectoryPath(savePath), receipt.fileName + ".candidate");
            if (!File.Exists(candidate) || new FileInfo(candidate).Length > MaximumBytes ||
                Hash(File.ReadAllBytes(candidate)) != receipt.sha256) throw new InvalidDataException();
        }
        catch (Exception)
        {
            fail("A pending media upload receipt could not be verified. Existing files remain unchanged.");
            return true;
        }
        // Another entity operation advanced this profile. It is not safe to
        // infer who owns any current pending transaction, or to abort it. Normal
        // save reading may proceed; media publication will verify exact content.
        if (files == null || receipt.profileVersion != files.ProfileVersion) return false;
        var fileNames = new List<string> { receipt.fileName };
        PlayFabDataAPI.FinalizeFileUploads(new FinalizeFileUploadsRequest
        { Entity = entity, FileNames = fileNames, ProfileVersion = receipt.profileVersion }, finalized =>
        {
            if (!current()) return;
            try { receipt.resolved = true; Save(savePath, receipt); }
            catch (Exception) { fail("Media upload finalized, but its recovery receipt could not be saved."); return; }
            requery();
        }, error =>
        {
            if (!current()) return;
            // Recheck after uncertain finalization. Abort only while the exact
            // durably acknowledged profile version still owns the pending slot.
            PlayFabDataAPI.GetFiles(new GetFilesRequest { Entity = entity }, refreshed =>
            {
                if (!current()) return;
                if (refreshed.ProfileVersion != receipt.profileVersion) { requery(); return; }
                PlayFabDataAPI.AbortFileUploads(new AbortFileUploadsRequest
                { Entity = entity, FileNames = fileNames, ProfileVersion = receipt.profileVersion }, aborted =>
                {
                    if (!current()) return;
                    try { receipt.resolved = true; Save(savePath, receipt); }
                    catch (Exception) { fail("Interrupted media upload cleared, but its receipt could not be saved."); return; }
                    requery();
                }, abortError => { if (current()) fail("A media upload remains uncertain; no other transaction was aborted."); });
            }, lookupError => { if (current()) fail("Could not reconcile the pending media upload. It will retry."); });
        });
        return true;
    }
    private static bool Allowed(string name) => name == Album || name == Portrait;
    private static string DirectoryPath(string savePath) => Path.Combine(Path.GetDirectoryName(savePath), "WebsiteMediaTransactions");
    private static Receipt Load(string savePath)
    {
        string path = Path.Combine(DirectoryPath(savePath), "pending.json");
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 4096) throw new InvalidDataException();
        var receipt = JsonUtility.FromJson<Receipt>(File.ReadAllText(path));
        if (receipt == null || receipt.schemaVersion != 1 || !Allowed(receipt.fileName) ||
            receipt.profileVersion < 0 || receipt.sha256 == null || receipt.sha256.Length != 64)
            throw new InvalidDataException();
        return receipt;
    }
    private static void Save(string savePath, Receipt receipt) => AtomicWrite(
        Path.Combine(DirectoryPath(savePath), "pending.json"), Encoding.UTF8.GetBytes(JsonUtility.ToJson(receipt)));
    private static void AtomicWrite(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temporary = path + ".tmp"; File.WriteAllBytes(temporary, bytes);
        if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
    }
    private static string Hash(byte[] bytes)
    {
        using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }
}
