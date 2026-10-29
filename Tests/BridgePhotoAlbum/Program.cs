using System.Reflection;
using System.Text;
using PlayFab;
using PlayFab.DataModels;
using UnityEngine;

int checks = 0;
void Check(bool result, string message) { checks++; if (!result) throw new Exception(message); }
void Reject(byte[] bytes, string message) { bool rejected = false; try { BridgePhotoAlbum.Decode(bytes); } catch (InvalidDataException) { rejected = true; } Check(rejected, message); }
byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR4nGNgAAIAAAUAAXpeqz8AAAAASUVORK5CYII=");
BridgePhotoAlbumEntry Entry(string id, string contract = "ShopKeeper") => new() { contractId = contract, regionId = "canyon-crossing", resultId = id,
    entryName = BridgePhotoAlbum.EntryName(id), sha256 = BridgePhotoAlbum.Sha256(png), capturedAt = "2026-10-29T01:00:00Z" };
string first = new('a', 32), second = new('b', 32);
var album = new BridgePhotoAlbum(); album.Replace(Entry(first), png);
byte[] encoded = album.Encode(); var decoded = BridgePhotoAlbum.Decode(encoded);
Check(decoded.Contains(Entry(first)), "Actual canonical encoder roundtrips through actual bounded decoder");
decoded.Replace(Entry(second, "ReedsContract"), png);
Check(BridgePhotoAlbum.Decode(decoded.Encode()).Manifest.photos.Count == 2, "Merging preserves another contract's photo");
string replacement = new('c', 32);
decoded.Replace(Entry(replacement), png);
Check(decoded.Manifest.photos.Count == 2 && decoded.Find("ShopKeeper", "canyon-crossing").resultId == replacement,
    "Replacing the selected contract preserves other contract entries");
Reject(encoded.Take(encoded.Length - 1).ToArray(), "Truncated archive rejected");
var bad = (byte[])encoded.Clone(); bad[6] = 1; Reject(bad, "Encrypted local header rejected");
bad = (byte[])encoded.Clone(); bad[8] = 8; Reject(bad, "Compressed entry rejected");
bad = (byte[])encoded.Clone(); bad[14] ^= 1; Reject(bad, "Local-central CRC disagreement rejected");
bad = (byte[])encoded.Clone(); bad[31] ^= 1; Reject(bad, "Entry name mismatch rejected");
bad = (byte[])encoded.Clone(); bad[^2] = 1; Reject(bad, "ZIP comments rejected");
Reject(encoded.Concat(new byte[1]).ToArray(), "Trailing bytes rejected");
Reject(new byte[BridgePhotoAlbum.MaximumBytes + 1], "Album download budget enforced");
bad = (byte[])png.Clone(); bad[16] = 1;
try { BridgePhotoAlbum.ValidatePng(bad); Check(false, "Oversized PNG rejected"); } catch (InvalidDataException) { Check(true, "Oversized PNG rejected"); }
try { album.Replace(new BridgePhotoAlbumEntry { contractId = "x", regionId = "y", resultId = first,
    entryName = "../outside.png", sha256 = Entry(first).sha256 }, png); Check(false, "Traversal rejected"); }
catch (InvalidDataException) { Check(true, "Traversal rejected"); }

if (args.Length > 0 && args[0] == "--write-fixture")
{
    string destination = Path.GetFullPath(args[1]); Directory.CreateDirectory(Path.GetDirectoryName(destination));
    File.WriteAllBytes(destination, encoded); Console.WriteLine("Wrote actual C# album fixture: " + destination);
}
if (args.Length > 0 && args[0] == "--read-fixture")
{
    Check(BridgePhotoAlbum.Decode(File.ReadAllBytes(args[1])).Manifest.photos.Count > 0, "Website-written ZIP accepted by actual C# decoder");
}

// Actual uploader state machine with fake engine/provider boundaries. No real accounts/network/GPU.
using (var fixture = new UploadFixture(Entry(first), png))
{
    fixture.Update(); Check(PlayFabDataAPI.Gets.Count == 1, "Durable queue starts with authenticated remote album lookup");
    PlayFabDataAPI.Gets[0].Success(new GetFilesResponse { ProfileVersion = 7, Metadata = new() });
    Check(PlayFabDataAPI.Starts.Count == 1 && PlayFabDataAPI.Starts[0].Request.ProfileVersion == 7, "Initiation uses observed profile CAS");
    PlayFabDataAPI.Starts[0].Success(new InitiateFileUploadsResponse { ProfileVersion = 8,
        UploadDetails = new() { new InitiateFileUploadMetadata { FileName = BridgePhotoAlbum.FileName, UploadUrl = "https://test.blob.core.windows.net/album" } } });
    Check(PlayFabDataAPI.Finishes.Count == 1 && fixture.Manager.Recorded == 0, "PUT alone never publishes snapshot metadata");
    PlayFabDataAPI.Finishes[0].Success(new FinalizeFileUploadsResponse());
    Check(fixture.Manager.Recorded == 1 && fixture.Manager.LastSnapshot.resultId == first, "Only finalized result publishes exact snapshot");
    fixture.Restart(); fixture.Update(); Check(PlayFabDataAPI.Gets.Count == 1, "Delivered queue does not replay on restart");
}
using (var fixture = new UploadFixture(Entry(first), png))
{
    fixture.Update(); var oldLookup = PlayFabDataAPI.Gets[0];
    fixture.SwitchAccount(); oldLookup.Success(new GetFilesResponse { ProfileVersion = 7, Metadata = new() });
    Check(PlayFabDataAPI.Starts.Count == 0 && fixture.Manager.Recorded == 0, "Old account lookup cannot initiate or publish for new account");
}
using (var fixture = new UploadFixture(Entry(first), png))
{
    fixture.Update(); PlayFabDataAPI.Gets[0].Success(new GetFilesResponse { ProfileVersion = 7, Metadata = new() });
    PlayFabDataAPI.Starts[0].Fail(); Check(fixture.Manager.Recorded == 0 && fixture.Cloud.Current == null, "Profile conflict releases only own slot and retains pending photo");
    Time.realtimeSinceStartup += 16; fixture.Update(); Check(PlayFabDataAPI.Gets.Count == 2, "Conflict retries from fresh remote lookup, not stale archive");
}
using (var fixture = new UploadFixture(Entry(first), png))
{
    fixture.Update(); PlayFabDataAPI.Gets[0].Success(new GetFilesResponse { ProfileVersion = 7, Metadata = new() });
    PlayFabDataAPI.Starts[0].Success(new InitiateFileUploadsResponse { ProfileVersion = 8,
        UploadDetails = new() { new InitiateFileUploadMetadata { FileName = BridgePhotoAlbum.FileName, UploadUrl = "https://test.blob.core.windows.net/album" } } });
    fixture.Manager.Bridge.resultId = second;
    PlayFabDataAPI.Finishes[0].Success(new FinalizeFileUploadsResponse());
    Check(fixture.Manager.Recorded == 0, "Late older upload cannot attach to redesigned result");
}
using (var fixture = new UploadFixture(Entry(first), png))
{
    fixture.Update(); PlayFabDataAPI.Gets[0].Success(new GetFilesResponse { ProfileVersion = 7, Metadata = new() });
    PlayFabDataAPI.Starts[0].Success(new InitiateFileUploadsResponse { ProfileVersion = 8,
        UploadDetails = new() { new InitiateFileUploadMetadata { FileName = BridgePhotoAlbum.FileName, UploadUrl = "https://test.blob.core.windows.net/album" } } });
    PlayFabDataAPI.Finishes[0].Fail(); fixture.Restart(); Time.realtimeSinceStartup += 16; fixture.Update();
    PlayFabDataAPI.Gets[^1].Success(new GetFilesResponse { ProfileVersion = 8, Metadata = new() });
    Check(PlayFabDataAPI.Finishes.Count == 2 && PlayFabDataAPI.Starts.Count == 1,
        "Restart retries acknowledged existing transaction finalization before starting new upload");
    PlayFabDataAPI.Finishes[^1].Fail(); Check(PlayFabDataAPI.Aborts.Count == 1 && PlayFabDataAPI.Aborts[0].Request.ProfileVersion == 8,
        "Interrupted upload abort is limited to exact durably recorded owned profile version");
}
using (var fixture = new UploadFixture(Entry(first), png))
{
    fixture.Update(); PlayFabDataAPI.Gets[0].Success(new GetFilesResponse { ProfileVersion = 7, Metadata = new() });
    PlayFabDataAPI.Starts[0].Success(new InitiateFileUploadsResponse { ProfileVersion = 8,
        UploadDetails = new() { new InitiateFileUploadMetadata { FileName = BridgePhotoAlbum.FileName, UploadUrl = "https://test.blob.core.windows.net/album" } } });
    PlayFabDataAPI.Finishes[0].Fail();
    fixture.Manager.Bridge.resultId = second;
    fixture.Restart(); fixture.Update();
    PlayFabDataAPI.Gets[^1].Success(new GetFilesResponse { ProfileVersion = 8, Metadata = new() });
    Check(PlayFabDataAPI.Finishes.Count == 2 && fixture.Manager.Recorded == 0,
        "Obsolete zero-entry queue still reconciles owned pending upload without publishing old result");
}
using (var fixture = new UploadFixture(Entry(first), png))
{
    var remote = new BridgePhotoAlbum(); remote.Replace(Entry(second, "ReedsContract"), png);
    byte[] remoteBytes = remote.Encode(); string url = "https://test.blob.core.windows.net/existing";
    UnityEngine.Networking.UnityWebRequest.Downloads[url] = remoteBytes;
    fixture.Update(); PlayFabDataAPI.Gets[0].Success(new GetFilesResponse { ProfileVersion = 12,
        Metadata = new() { [BridgePhotoAlbum.FileName] = new() { Size = remoteBytes.Length, DownloadUrl = url } } });
    var candidate = BridgePhotoAlbum.Decode(File.ReadAllBytes(Path.Combine(fixture.Root, "WebsiteBridgePhotos", "candidate.zip")));
    Check(candidate.Contains(Entry(first)) && candidate.Contains(Entry(second, "ReedsContract")),
        "Actual uploader reread/merge preserves other device's distinct contract image");
    Check(PlayFabDataAPI.Starts[0].Request.ProfileVersion == 12, "Merged upload CAS uses exact downloaded profile version");
}
using (var fixture = new UploadFixture(Entry(first), png))
{
    var remote = new BridgePhotoAlbum(); remote.Replace(Entry(first), png);
    byte[] remoteBytes = remote.Encode(); string url = "https://test.blob.core.windows.net/committed";
    UnityEngine.Networking.UnityWebRequest.Downloads[url] = remoteBytes;
    fixture.Manager.SaveSucceeds = false;
    fixture.Update(); PlayFabDataAPI.Gets[0].Success(new GetFilesResponse { ProfileVersion = 9,
        Metadata = new() { [BridgePhotoAlbum.FileName] = new() { Size = remoteBytes.Length, DownloadUrl = url } } });
    Check(PlayFabDataAPI.Starts.Count == 0 && fixture.Manager.Recorded == 0 && fixture.Cloud.Current == null,
        "Already-finalized exact result needs no second upload; failed local metadata save remains retryable");
    fixture.Manager.SaveSucceeds = true; Time.realtimeSinceStartup += 16; fixture.Update();
    PlayFabDataAPI.Gets[^1].Success(new GetFilesResponse { ProfileVersion = 9,
        Metadata = new() { [BridgePhotoAlbum.FileName] = new() { Size = remoteBytes.Length, DownloadUrl = url } } });
    Check(fixture.Manager.Recorded == 1 && PlayFabDataAPI.Starts.Count == 0, "Local save retry recovers committed photo without reupload");
}
using (var fixture = new UploadFixture(Entry(first), png))
{
    fixture.Update(); PlayFabDataAPI.Gets[0].Success(new GetFilesResponse { ProfileVersion = 7, Metadata = new() });
    var oldInitiation = PlayFabDataAPI.Starts[0];
    fixture.SwitchAccount();
    oldInitiation.Success(new InitiateFileUploadsResponse { ProfileVersion = 8,
        UploadDetails = new() { new InitiateFileUploadMetadata { FileName = BridgePhotoAlbum.FileName, UploadUrl = "https://test.blob.core.windows.net/album" } } });
    Check(PlayFabDataAPI.Finishes.Count == 0 && fixture.Manager.Recorded == 0 &&
        EntityMediaUploadRecovery.HasUnpublished(Path.Combine(fixture.Root, "playerSaveData.json"), "ACCOUNT_A", BridgePhotoAlbum.FileName) &&
        !EntityMediaUploadRecovery.HasUnpublished(Path.Combine(fixture.Root, "playerSaveData.json"), "ACCOUNT_B", BridgePhotoAlbum.FileName),
        "Late initiation persists only the old account's exact recovery receipt; never uploads or publishes for new account");
}
Console.WriteLine($"BridgePhotoAlbum: {checks} actual codec/uploader checks passed (no provider requests).");

sealed class UploadFixture : IDisposable
{
    public readonly PlayerDataManager Manager;
    public readonly CloudSaveManager Cloud = new();
    private BridgePhotoAlbumUploadService service;
    private readonly string root = Path.Combine(Path.GetTempPath(), "CivilCraftBridgeAlbumTest-" + Guid.NewGuid().ToString("N"));
    public string Root => root;
    public UploadFixture(BridgePhotoAlbumEntry entry, byte[] png)
    {
        PlayFabDataAPI.Reset(); Time.realtimeSinceStartup = 0;
        Directory.CreateDirectory(root);
        Manager = new PlayerDataManager { ActiveAccountId = "ACCOUNT_A", CurrentSavePath = Path.Combine(root, "playerSaveData.json"), Bridge = new() { resultId = entry.resultId } };
        string dir = Path.Combine(root, "WebsiteBridgePhotos"); Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, entry.entryName), png);
        File.WriteAllText(Path.Combine(dir, "queue.json"), JsonUtility.ToJson(new QueueFixture { accountId = Manager.ActiveAccountId, photos = new() { entry } }));
        service = NewService();
    }
    private BridgePhotoAlbumUploadService NewService()
    {
        var instance = new BridgePhotoAlbumUploadService(); instance.SetComponent(Manager); instance.SetComponent(Cloud);
        Call(instance, "Awake"); Call(instance, "ObserveSession"); return instance;
    }
    public void Update() { Time.realtimeSinceStartup += 2; Call(service, "Update"); }
    public void Restart() { Call(service, "OnDestroy"); service = NewService(); }
    public void SwitchAccount() { Cloud.SessionGeneration++; Cloud.ActiveAccountId = Manager.ActiveAccountId = "ACCOUNT_B"; Cloud.Current = null; Call(service, "Update"); }
    private static void Call(object instance, string name) => instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(instance, null);
    public void Dispose()
    {
        Call(service, "OnDestroy");
        string resolved = Path.GetFullPath(root);
        if (Path.GetDirectoryName(resolved) != Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) ||
            !Path.GetFileName(resolved).StartsWith("CivilCraftBridgeAlbumTest-", StringComparison.Ordinal)) throw new Exception("Unsafe test cleanup path");
        Directory.Delete(resolved, true);
    }
    [Serializable] private sealed class QueueFixture { public int schemaVersion = 1; public string accountId; public List<BridgePhotoAlbumEntry> photos; public int pendingProfileVersion = -1; public string candidateHash = null; }
}
