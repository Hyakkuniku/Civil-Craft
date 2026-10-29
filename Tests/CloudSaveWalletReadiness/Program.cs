using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using PlayFab;
using PlayFab.ClientModels;
using PlayFab.DataModels;
using PlayFab.Internal;
using UnityEngine;

int scenarios = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
void Run(string name, Action<Fixture> test, bool cached = true, string synced = null)
{
    using var fixture = new Fixture(cached, synced);
    test(fixture);
    scenarios++;
    Console.WriteLine("PASS " + name);
}

Run("failed Resume permits offline play/journaling and existing wallet reads, not a first import", f => {
    f.Begin(CloudSaveManager.StartMode.Resume);
    PlayFabDataAPI.Gets[0].Fail();
    Check(f.Callbacks.SequenceEqual(new[] { true }), "Offline Resume must retain its one-shot gameplay callback");
    Check(f.Cloud.IsSessionReady && f.Cloud.CanPublishDashboard, "Existing migrated wallet reads must not be disabled by the new import-only gate");
    Check(!f.Cloud.IsLegacyWalletImportSourceReady && !f.Cloud.CanImportLegacyWallet, "A failed lookup cannot approve a irreversible legacy import");
    Check(GameWalletPolicy.CanUseAccountData(Fixture.A, Fixture.A, Fixture.A, Fixture.A,
        f.Cloud.SessionGeneration, f.Cloud.SessionGeneration, f.Cloud.IsSessionReady && !f.Cloud.HasConflict,
        true, false, false), "The approved offline account can archive its earned-reward journal");
    Check(!f.Cloud.TryBeginLegacyWalletImport(Fixture.A, f.Cloud.SessionGeneration), "The consumer reservation must also reject an unresolved source");
});

Run("Resume download failure never approves its cached balance for import", f => {
    f.Begin(CloudSaveManager.StartMode.Resume);
    f.StartDownload();
    PlayFabHttp.Downloads[0].Error("Simulated download outage");
    Check(f.Cloud.IsSessionReady && !f.Cloud.CanImportLegacyWallet, "Download failure is offline-ready only");
});

Run("Resume retries the real cloud flow and approves the reconciled newer online copy", f => {
    f.Begin(CloudSaveManager.StartMode.Resume);
    PlayFabDataAPI.Gets[0].Fail();
    f.Tick();
    f.DeliverCloud(Fixture.Online);
    Check(f.Manager.Json == Fixture.Online && f.Manager.Applied == 1, "Retry must select the actual newer online snapshot");
    Check(f.Cloud.CanImportLegacyWallet && f.Cloud.IsLegacyWalletImportSourceReady, "Only successful reconciliation approves import");
    Check(f.Callbacks.Count == 1 && f.Callbacks[0], "The cleared offline callback is not invoked twice");
}, synced: Fixture.Local);

Run("a failed strict LoadOnline retains online-selection intent after its callback is cleared", f => {
    f.Begin(CloudSaveManager.StartMode.LoadOnline);
    PlayFabDataAPI.Gets[0].Fail();
    Check(f.Callbacks.SequenceEqual(new[] { false }) && !f.Cloud.IsSessionReady, "Strict startup must not approve the local fallback");
    f.Tick();
    f.DeliverCloud(Fixture.Online);
    Check(f.Manager.Json == Fixture.Online && PlayFabDataAPI.Starts.Count == 0,
        "Without the persistent selection flag this branch would upload the changed local copy instead");
    Check(f.Backups().Length == 2 && f.Cloud.CanImportLegacyWallet, "The selected online copy must be backed up/reconciled before approval");
    Check(f.Callbacks.Count == 1, "Retry success changes source readiness, not the old failed callback");
}, synced: Fixture.Online);

Run("strict LoadOnline retry cannot upload a local fallback when the cloud file disappears", f => {
    f.Begin(CloudSaveManager.StartMode.LoadOnline);
    PlayFabDataAPI.Gets[0].Fail();
    f.Tick();
    f.DeliverMissing();
    Check(PlayFabDataAPI.Starts.Count == 0 && !f.Cloud.IsSessionReady && !f.Cloud.CanImportLegacyWallet,
        "An absent online file after a failed choice must remain unresolved");
});

foreach (CloudSaveManager.StartMode mode in new[] {
    CloudSaveManager.StartMode.StartFresh, CloudSaveManager.StartMode.UseGuestSave, CloudSaveManager.StartMode.LoadLocalAccount })
{
    bool cached = mode == CloudSaveManager.StartMode.LoadLocalAccount;
    Run("strict " + mode + " rejects an unexpected online copy even after its callback failed", f => {
        f.Begin(mode);
        string selected = f.Manager.Json;
        PlayFabDataAPI.Gets[0].Fail();
        f.Tick();
        f.DeliverCloud(Fixture.Online);
        Check(!f.Cloud.IsSessionReady && !f.Cloud.CanImportLegacyWallet && f.Manager.Json == selected,
            "A failed strict selection must not silently become generic Resume");
        Check(f.Manager.Applied == 0 && PlayFabDataAPI.Starts.Count == 0, "Neither copy may be overwritten without another choice");
    }, cached);
}

foreach (CloudSaveManager.StartMode mode in new[] {
    CloudSaveManager.StartMode.StartFresh, CloudSaveManager.StartMode.UseGuestSave, CloudSaveManager.StartMode.LoadLocalAccount })
{
    bool cached = mode == CloudSaveManager.StartMode.LoadLocalAccount;
    Run("explicit " + mode + " source is approved only after confirmed file finalization", f => {
        f.Begin(mode);
        f.DeliverMissing();
        Check(!f.Cloud.CanImportLegacyWallet && f.Cloud.IsSyncing, "File lookup/upload initiation is not source approval");
        f.StartUpload();
        Check(!f.Cloud.CanImportLegacyWallet, "An unfinished storage PUT is not source approval");
        f.FinishPut();
        Check(!f.Cloud.CanImportLegacyWallet, "An unfinalized uploaded file is not source approval");
        f.FinishFinalize();
        Check(f.Cloud.IsSessionReady && f.Cloud.CanImportLegacyWallet, "Only acknowledged finalization approves this explicit source");
        Check(f.Manager.Json == (mode == CloudSaveManager.StartMode.StartFresh ? "{\"gold\":0}" :
            mode == CloudSaveManager.StartMode.UseGuestSave ? f.Manager.GuestJson : Fixture.Local), "The explicit choice must retain its intended source");
    }, cached);
}

Run("failed upload finalization does not approve a source", f => {
    f.Begin(CloudSaveManager.StartMode.LoadLocalAccount);
    f.DeliverMissing(); f.StartUpload(); f.FinishPut();
    PlayFabDataAPI.Finishes[0].Fail();
    Check(!f.Cloud.IsSessionReady && !f.Cloud.IsLegacyWalletImportSourceReady && !f.Cloud.CanImportLegacyWallet,
        "A successful PUT alone cannot approve the source");
});

Run("legacy online encryption upgrade waits for acknowledged finalization", f => {
    f.Begin(CloudSaveManager.StartMode.LoadOnline);
    f.DeliverCloud(Fixture.Online, schema: 1);
    Check(f.Manager.Json == Fixture.Online && !f.Cloud.CanImportLegacyWallet, "Applying legacy JSON does not bypass its required upgrade");
    f.StartUpload(); f.FinishPut(); f.FinishFinalize();
    Check(f.Cloud.CanImportLegacyWallet, "Successful upgrade finalization approves the selected online source");
});

Run("failed strict online backup preserves local data and blocks import", f => {
    f.Begin(CloudSaveManager.StartMode.LoadOnline);
    f.DeliverCloud(Fixture.Online, beforeDownload: () => SaveEncryption.FailEncrypt = true);
    Check(f.Manager.Json == Fixture.Local && f.Manager.Applied == 0 && !f.Cloud.CanImportLegacyWallet,
        "A backup failure must not replace the selected local data or approve import");
    SaveEncryption.FailEncrypt = false;
    f.Tick(); f.DeliverCloud(Fixture.Online);
    Check(f.Cloud.CanImportLegacyWallet && f.Backups().Length == 2, "A later successful strict retry retains the original online intent");
});

Run("failed local application cannot approve the online import source", f => {
    f.Manager.ApplySucceeds = false;
    f.Begin(CloudSaveManager.StartMode.LoadOnline); f.DeliverCloud(Fixture.Online);
    Check(!f.Cloud.IsSessionReady && !f.Cloud.CanImportLegacyWallet && f.Manager.Json == Fixture.Local,
        "The approved source must actually be committed by its owner");
});

Run("unresolved conflict and Keep Device remain import-blocked while offline journaling continues", f => {
    f.Begin(CloudSaveManager.StartMode.Resume); f.DeliverCloud(Fixture.Online);
    Check(f.Cloud.HasConflict && f.Dialog.IsVisible && !f.Cloud.CanImportLegacyWallet && f.Backups().Length == 2,
        "Divergent snapshots must require the real authored choice callback");
    f.Dialog.ChooseDevice();
    Check(f.Cloud.IsSessionReady && !f.Cloud.HasConflict && !f.Cloud.CanPublishDashboard && !f.Cloud.CanImportLegacyWallet,
        "Keeping device data is not permission to import while reconciliation is suspended");
    Check(GameWalletPolicy.CanUseAccountData(Fixture.A, Fixture.A, Fixture.A, Fixture.A,
        f.Cloud.SessionGeneration, f.Cloud.SessionGeneration, f.Cloud.IsSessionReady && !f.Cloud.HasConflict,
        true, false, false), "Device-only progress must retain its archival gate");
}, synced: "{\"gold\":400}");

Run("Use Online conflict callback approves only the chosen and saved source", f => {
    f.Begin(CloudSaveManager.StartMode.Resume); f.DeliverCloud(Fixture.Online);
    f.Dialog.ChooseOnline();
    Check(f.Manager.Json == Fixture.Online && f.Cloud.CanImportLegacyWallet && !f.Cloud.HasConflict,
        "The chosen online source must be applied and confirmed");
}, synced: "{\"gold\":400}");

Run("confirmed source reservation excludes cloud sync and portrait operations", f => {
    f.Begin(CloudSaveManager.StartMode.Resume); f.DeliverCloud(Fixture.Local);
    int generation = f.Cloud.SessionGeneration;
    Check(!f.Cloud.TryBeginLegacyWalletImport(Fixture.B, generation) &&
        !f.Cloud.TryBeginLegacyWalletImport(Fixture.A, generation - 1), "Import locks are bound to account and generation");
    Check(f.Cloud.TryBeginLegacyWalletImport(Fixture.A, generation), "Confirmed idle source can be reserved");
    Check(f.Cloud.CanImportLegacyWallet && !f.Cloud.TryBeginLegacyWalletImport(Fixture.A, generation),
        "Own reservation preserves dispatch rechecks but rejects a second owner");
    Check(!f.Cloud.TryBeginAuxiliaryFileOperation(out _, out _), "Portrait/entity file upload cannot race the import");
    int lookups = PlayFabDataAPI.Gets.Count;
    f.Manager.CommitLocal("{\"gold\":500,\"marker\":1}");
    f.Tick(); f.Call("OnApplicationPause", true); f.Call("SyncNow");
    Check(PlayFabDataAPI.Gets.Count == lookups, "Update, pause and direct synchronization must all honor the reservation");
    f.Cloud.EndLegacyWalletImport(Fixture.A, generation);
    f.Tick();
    Check(PlayFabDataAPI.Gets.Count == lookups + 1 && !f.Cloud.CanImportLegacyWallet,
        "Releasing the reservation restores cloud sync, whose in-flight state blocks another import");
});

Run("active auxiliary operations block first import until released", f => {
    f.Begin(CloudSaveManager.StartMode.Resume); f.DeliverCloud(Fixture.Local);
    Check(f.Cloud.TryBeginAuxiliaryFileOperation(out _, out var reservation) && !f.Cloud.CanImportLegacyWallet &&
        !f.Cloud.TryBeginLegacyWalletImport(Fixture.A, f.Cloud.SessionGeneration), "A portrait transaction must finish first");
    f.Cloud.EndAuxiliaryFileOperation(reservation);
    Check(f.Cloud.CanImportLegacyWallet, "Releasing auxiliary storage restores the confirmed source gate");
});

Run("stale auxiliary callbacks cannot release a new account's media reservation", f => {
    f.Begin(CloudSaveManager.StartMode.Resume); f.DeliverCloud(Fixture.Local);
    Check(f.Cloud.TryBeginAuxiliaryFileOperation(out _, out var oldReservation), "First account acquires media slot");
    Check(f.Cloud.IsAuxiliaryFileOperationCurrent(oldReservation), "Owner token is current");
    f.Manager.Accounts[Fixture.B] = Fixture.Online;
    f.Begin(CloudSaveManager.StartMode.Resume, Fixture.B); f.DeliverCloud(Fixture.Online, account: Fixture.B);
    Check(f.Cloud.TryBeginAuxiliaryFileOperation(out _, out var newReservation), "Second account acquires its own media slot");
    Check(!f.Cloud.IsAuxiliaryFileOperationCurrent(oldReservation), "Old token is invalidated by login generation");
    f.Cloud.EndAuxiliaryFileOperation(oldReservation);
    Check(f.Cloud.IsAuxiliaryFileOperationCurrent(newReservation) && !f.Cloud.CanImportLegacyWallet,
        "Stale callback cannot unlock the new account's media transaction");
    f.Cloud.EndAuxiliaryFileOperation(newReservation);
    Check(f.Cloud.CanImportLegacyWallet, "Only the new owner's token can release its reservation");
});

Run("EndSession resets source approval, reservation and stale response authority", f => {
    f.Begin(CloudSaveManager.StartMode.Resume); f.DeliverCloud(Fixture.Local);
    int previousGeneration = f.Cloud.SessionGeneration;
    Check(f.Cloud.TryBeginLegacyWalletImport(Fixture.A, previousGeneration), "Test source must be reserved");
    f.Cloud.EndSession();
    Check(!f.Cloud.IsAccountActive && !f.Cloud.IsSessionReady && !f.Cloud.IsLegacyWalletImportSourceReady &&
        !f.Cloud.CanImportLegacyWallet, "Sign-out cannot carry approval or a lock forward");
    f.Manager.Accounts[Fixture.B] = Fixture.Online;
    f.Begin(CloudSaveManager.StartMode.Resume, Fixture.B); f.DeliverCloud(Fixture.Online, account: Fixture.B);
    Check(f.Cloud.TryBeginLegacyWalletImport(Fixture.B, f.Cloud.SessionGeneration), "New account must acquire its own source reservation");
    f.Cloud.EndLegacyWalletImport(Fixture.A, previousGeneration);
    Check(!f.Cloud.TryBeginLegacyWalletImport(Fixture.B, f.Cloud.SessionGeneration), "Outgoing coroutine cannot release the incoming reservation");
    f.Cloud.EndLegacyWalletImport(Fixture.B, f.Cloud.SessionGeneration);
});

Run("stale download callback cannot approve or replace another account source", f => {
    f.Begin(CloudSaveManager.StartMode.Resume); f.StartDownload();
    var oldDownload = PlayFabHttp.Downloads[0];
    byte[] oldBytes = f.Envelope(Fixture.Local, Fixture.A);
    f.Manager.Accounts[Fixture.B] = Fixture.Online;
    f.Begin(CloudSaveManager.StartMode.Resume, Fixture.B); f.DeliverCloud(Fixture.Online, account: Fixture.B);
    oldDownload.Success(oldBytes);
    PlayFabDataAPI.Gets[0].Fail("Stale outgoing error");
    Check(f.Cloud.ActiveAccountId == Fixture.B && f.Manager.Json == Fixture.Online && f.Cloud.CanImportLegacyWallet &&
        f.Cloud.LastSyncError == null, "Neither stale success nor failure may affect the new account");
});

Run("stale upload finalization cannot mark another account source ready", f => {
    f.Begin(CloudSaveManager.StartMode.Resume); f.DeliverMissing(); f.StartUpload(); f.FinishPut();
    var oldFinalize = PlayFabDataAPI.Finishes[0];
    f.Manager.Accounts[Fixture.B] = Fixture.Online;
    f.Begin(CloudSaveManager.StartMode.LoadOnline, Fixture.B);
    oldFinalize.Success(new FinalizeFileUploadsResponse());
    Check(!f.Cloud.IsSessionReady && !f.Cloud.IsLegacyWalletImportSourceReady, "The new account must await its own reconciliation");
    f.DeliverCloud(Fixture.Online, account: Fixture.B);
    Check(f.Cloud.CanImportLegacyWallet && f.Manager.Json == Fixture.Online, "Only the new account's confirmed response approves it");
});

Run("owned album recovery precedes strict startup save selection", f => {
    string save = Path.Combine(f.Root, Fixture.A, "playerSaveData.json");
    var entity = new PlayFab.DataModels.EntityKey { Id = "stub-" + Fixture.A, Type = "title_player_account" };
    EntityMediaUploadRecovery.Record(save, Fixture.A, entity, "bridge-photos-v1.zip", 8, new byte[] { 1, 2, 3 });
    f.Begin(CloudSaveManager.StartMode.LoadOnline);
    PlayFabDataAPI.Gets[0].Success(new GetFilesResponse { ProfileVersion = 8, Metadata = new() });
    Check(PlayFabDataAPI.Finishes.Count == 1 && PlayFabHttp.Downloads.Count == 0 &&
        PlayFabDataAPI.Starts.Count == 0 && !f.Cloud.IsSessionReady,
        "Known media operation is reconciled before reading/uploading the selected gameplay save");
    Check(PlayFabDataAPI.Finishes[0].Request.FileNames.SequenceEqual(new[] { "bridge-photos-v1.zip" }) &&
        PlayFabDataAPI.Finishes[0].Request.ProfileVersion == 8, "Recovery stays bound to the acknowledged file/profile");
    PlayFabDataAPI.Finishes[0].Success(new FinalizeFileUploadsResponse());
    Check(PlayFabDataAPI.Gets.Count == 2 && !f.Cloud.IsSessionReady, "Finalization requires a fresh lookup, not readiness or progress credit");
    f.DeliverCloud(Fixture.Local);
    Check(f.Cloud.IsSessionReady && f.Manager.Json == Fixture.Local,
        "Startup can finish after media recovery without depending on already-ready media services");
});

Run("interrupted portrait recovery aborts only the exact owned profile after rechecking", f => {
    string save = Path.Combine(f.Root, Fixture.A, "playerSaveData.json");
    var entity = new PlayFab.DataModels.EntityKey { Id = "stub-" + Fixture.A, Type = "title_player_account" };
    EntityMediaUploadRecovery.Record(save, Fixture.A, entity, "characterPortrait.png", 8, new byte[] { 4, 5 });
    f.Begin(CloudSaveManager.StartMode.LoadOnline);
    PlayFabDataAPI.Gets[0].Success(new GetFilesResponse { ProfileVersion = 8, Metadata = new() });
    PlayFabDataAPI.Finishes[0].Fail();
    Check(PlayFabDataAPI.Aborts.Count == 0 && PlayFabDataAPI.Gets.Count == 2, "Uncertain finalization is rechecked before any abort");
    PlayFabDataAPI.Gets[1].Success(new GetFilesResponse { ProfileVersion = 8, Metadata = new() });
    Check(PlayFabDataAPI.Aborts.Count == 1 && PlayFabDataAPI.Aborts[0].Request.ProfileVersion == 8 &&
        PlayFabDataAPI.Aborts[0].Request.FileNames.Single() == "characterPortrait.png", "Only the recorded portrait transaction is eligible");
    PlayFabDataAPI.Aborts[0].Success(new AbortFileUploadsResponse());
    Check(EntityMediaUploadRecovery.HasUnpublished(save, Fixture.A, "characterPortrait.png"),
        "Clearing a pending transaction does not claim the portrait metadata was published");
    f.DeliverCloud(Fixture.Local);
    Check(f.Cloud.IsSessionReady, "Known interrupted portrait no longer deadlocks cloud startup");
});

Run("changed remote profile is never aborted during uncertain media finalization", f => {
    string save = Path.Combine(f.Root, Fixture.A, "playerSaveData.json");
    EntityMediaUploadRecovery.Record(save, Fixture.A, new() { Id = "stub-" + Fixture.A, Type = "title_player_account" },
        "bridge-photos-v1.zip", 8, new byte[] { 1 });
    f.Begin(CloudSaveManager.StartMode.LoadOnline);
    PlayFabDataAPI.Gets[0].Success(new GetFilesResponse { ProfileVersion = 8, Metadata = new() });
    PlayFabDataAPI.Finishes[0].Fail();
    PlayFabDataAPI.Gets[1].Success(new GetFilesResponse { ProfileVersion = 9, Metadata = new() });
    Check(PlayFabDataAPI.Aborts.Count == 0 && PlayFabDataAPI.Gets.Count == 3,
        "A newer device operation is not inferred to belong to the old receipt");
    PlayFabDataAPI.Gets[2].Success(new GetFilesResponse { ProfileVersion = 9,
        Metadata = new() { ["playerSaveData.json"] = new() { DownloadUrl = "mock://cloud-save" } } });
    PlayFabHttp.Downloads[^1].Success(f.Envelope(Fixture.Local, Fixture.A));
    Check(f.Cloud.IsSessionReady, "A changed profile still permits ordinary save verification");
});

Run("old-account recovery callback cannot affect a later account", f => {
    string save = Path.Combine(f.Root, Fixture.A, "playerSaveData.json");
    EntityMediaUploadRecovery.Record(save, Fixture.A, new() { Id = "stub-" + Fixture.A, Type = "title_player_account" },
        "bridge-photos-v1.zip", 8, new byte[] { 1 });
    f.Begin(CloudSaveManager.StartMode.LoadOnline);
    PlayFabDataAPI.Gets[0].Success(new GetFilesResponse { ProfileVersion = 8, Metadata = new() });
    var old = PlayFabDataAPI.Finishes[0];
    f.Manager.Accounts[Fixture.B] = Fixture.Online;
    f.Begin(CloudSaveManager.StartMode.LoadOnline, Fixture.B);
    int lookups = PlayFabDataAPI.Gets.Count;
    old.Fail();
    Check(PlayFabDataAPI.Aborts.Count == 0 && PlayFabDataAPI.Gets.Count == lookups && !f.Cloud.IsSessionReady,
        "An outgoing recovery callback cannot abort, requery or approve the incoming account");
    f.DeliverCloud(Fixture.Online, account: Fixture.B);
    Check(f.Cloud.IsSessionReady && f.Manager.Json == Fixture.Online, "Only B's verified save becomes ready");
});

Run("tampered local media candidate blocks automated recovery without changing files", f => {
    string save = Path.Combine(f.Root, Fixture.A, "playerSaveData.json");
    EntityMediaUploadRecovery.Record(save, Fixture.A, new() { Id = "stub-" + Fixture.A, Type = "title_player_account" },
        "bridge-photos-v1.zip", 8, new byte[] { 1 });
    File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(save), "WebsiteMediaTransactions", "bridge-photos-v1.zip.candidate"), new byte[] { 9 });
    f.Begin(CloudSaveManager.StartMode.LoadOnline);
    PlayFabDataAPI.Gets[0].Success(new GetFilesResponse { ProfileVersion = 8, Metadata = new() });
    Check(PlayFabDataAPI.Finishes.Count == 0 && PlayFabDataAPI.Aborts.Count == 0 && !f.Cloud.IsSessionReady,
        "Unverified receipt never authorizes a mutation or a first import");
});

Run("a late older initiation receipt cannot overwrite a newer known transaction", f => {
    string save = Path.Combine(f.Root, Fixture.A, "playerSaveData.json");
    var entity = new PlayFab.DataModels.EntityKey { Id = "stub-" + Fixture.A, Type = "title_player_account" };
    EntityMediaUploadRecovery.Record(save, Fixture.A, entity, "characterPortrait.png", 10, new byte[] { 1 });
    bool rejected = false;
    try { EntityMediaUploadRecovery.Record(save, Fixture.A, entity, "bridge-photos-v1.zip", 8, new byte[] { 2 }); }
    catch (InvalidDataException) { rejected = true; }
    Check(rejected && EntityMediaUploadRecovery.HasUnpublished(save, Fixture.A, "characterPortrait.png"),
        "Stale receipt cannot replace the newer profile owner's recovery evidence");
});

Run("cached offline Resume cannot upload media before its selected save is reconciled", f => {
    f.Begin(CloudSaveManager.StartMode.Resume); PlayFabDataAPI.Gets[0].Fail();
    Check(f.Cloud.IsSessionReady && !f.Cloud.TryBeginAuxiliaryFileOperation(out _, out _),
        "Offline gameplay readiness is not permission to replace online photo files");
    f.Tick(); f.DeliverCloud(Fixture.Local);
    Check(f.Cloud.TryBeginAuxiliaryFileOperation(out _, out var slot), "Verified selected save can reserve media");
    f.Cloud.EndAuxiliaryFileOperation(slot);
    f.Manager.CommitLocal("{\"gold\":501}");
    Check(!f.Cloud.TryBeginAuxiliaryFileOperation(out _, out _), "A newly committed result must synchronize before media work starts");
});

Run("changed entity profile makes media recheck game save before rebuilding album", f => {
    f.Begin(CloudSaveManager.StartMode.Resume); f.DeliverCloud(Fixture.Local);
    Check(f.Cloud.TryBeginAuxiliaryFileOperation(out _, out var slot) && slot.ProfileVersion == 7,
        "Media reservation records the last verified entity profile version");
    Check(!f.Cloud.VerifyAuxiliaryFileProfile(slot, 8) && !f.Cloud.IsAuxiliaryFileOperationCurrent(slot),
        "Another device's entity update invalidates the media reservation before merging");
    int count = PlayFabDataAPI.Gets.Count; f.Tick();
    Check(PlayFabDataAPI.Gets.Count == count + 1 && PlayFabDataAPI.Starts.Count == 0,
        "Next operation is gameplay save reconciliation, not an album overwrite");
});

Run("a result saved during album lookup invalidates the media source", f => {
    f.Begin(CloudSaveManager.StartMode.Resume); f.DeliverCloud(Fixture.Local);
    Check(f.Cloud.TryBeginAuxiliaryFileOperation(out _, out var slot), "Confirmed source reserves media");
    f.Manager.CommitLocal("{\"gold\":502}");
    Check(!f.Cloud.VerifyAuxiliaryFileProfile(slot, 7), "Same remote profile cannot authorize an unsynchronized newer local selection");
});

Console.WriteLine($"CloudSaveWalletReadiness: {scenarios} actual-CloudSaveManager scenarios passed.");

sealed class Fixture : IDisposable
{
    public const string A = "ABC123", B = "DEF456";
    public const string Local = "{\"gold\":500}", Online = "{\"gold\":20000}";
    private const string Prefix = "CivilCraftCloudSaveSource-";
    public readonly string Root;
    public readonly PlayerDataManager Manager;
    public readonly CloudSaveManager Cloud = new();
    public readonly AuthoredSaveChoiceDialog Dialog = new();
    public readonly List<bool> Callbacks = new();
    public Fixture(bool cached, string synced)
    {
        PlayFabDataAPI.Reset(); PlayFabHttp.Reset(); Debug.Messages.Clear();
        SaveEncryption.FailEncrypt = false; PlayFabClientAPI.LoggedIn = true;
        Time.realtimeSinceStartup = 0; Time.timeScale = 1;
        Root = Path.Combine(Path.GetTempPath(), Prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        Manager = new PlayerDataManager { Root = Root };
        if (cached) Manager.Accounts[A] = Local;
        if (synced != null)
        {
            string accountPath = Path.Combine(Root, A);
            Directory.CreateDirectory(accountPath);
            File.WriteAllText(Path.Combine(accountPath, "playerSaveData.json.cloudstate"), "{\"lastSyncedHash\":\"" + Hash(synced) + "\"}");
        }
        Cloud.SetComponent(Manager); Cloud.SetComponent(Dialog);
        Call("Awake");
    }
    public void Begin(CloudSaveManager.StartMode mode, string account = A) => Cloud.BeginSession(new LoginResult {
        PlayFabId = account, EntityToken = new EntityTokenResponse { Entity = new PlayFab.ClientModels.EntityKey { Id = "stub-" + account, Type = "title_player_account" } }
    }, mode, result => Callbacks.Add(result));
    public void Tick() { Time.realtimeSinceStartup += 31; Call("Update"); }
    public void Call(string method, params object[] args) => typeof(CloudSaveManager).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Cloud, args);
    public void StartDownload()
    {
        PlayFabDataAPI.Gets[^1].Success(new GetFilesResponse {
            ProfileVersion = 7,
            Metadata = new() { ["playerSaveData.json"] = new GetFileMetadata { DownloadUrl = "mock://cloud-save" } }
        });
    }
    public void DeliverCloud(string json, int schema = 2, string account = A, Action beforeDownload = null)
    {
        byte[] bytes = Envelope(json, account, schema);
        StartDownload(); beforeDownload?.Invoke(); PlayFabHttp.Downloads[^1].Success(bytes);
    }
    public byte[] Envelope(string json, string account, int schema = 2) => Encoding.UTF8.GetBytes(JsonUtility.ToJson(new {
        schemaVersion = schema, accountId = account,
        encryptedPlayerData = schema == 2 ? SaveEncryption.Encrypt(json, "cloud-account:" + account) : null,
        playerDataJson = schema == 1 ? json : null, sha256 = Hash(json)
    }));
    public void DeliverMissing() => PlayFabDataAPI.Gets[^1].Success(new GetFilesResponse { ProfileVersion = 7, Metadata = new() });
    public void StartUpload() => PlayFabDataAPI.Starts[^1].Success(new InitiateFileUploadsResponse {
        ProfileVersion = 8, UploadDetails = new() { new UploadFileMetadata { UploadUrl = "mock://cloud-upload" } }
    });
    public void FinishPut() => PlayFabHttp.Uploads[^1].Success(Array.Empty<byte>());
    public void FinishFinalize() => PlayFabDataAPI.Finishes[^1].Success(new FinalizeFileUploadsResponse());
    public string[] Backups() => Directory.GetFiles(Root, "*-device.json", SearchOption.AllDirectories)
        .Concat(Directory.GetFiles(Root, "*-online.json", SearchOption.AllDirectories)).ToArray();
    private static string Hash(string json) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    public void Dispose()
    {
        Call("OnDestroy"); SaveEncryption.FailEncrypt = false;
        string resolved = Path.GetFullPath(Root);
        string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
        if (Path.GetDirectoryName(resolved) != temp || !Path.GetFileName(resolved).StartsWith(Prefix, StringComparison.Ordinal))
            throw new InvalidOperationException("Refusing to remove a path outside this isolated test fixture");
        Directory.Delete(resolved, recursive: true);
    }
}
