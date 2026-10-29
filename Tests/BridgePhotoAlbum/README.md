# Private bridge album regression checks

From the Unity project root:

```powershell
dotnet build Tests/BridgePhotoAlbum/BridgePhotoAlbum.csproj --artifacts-path Temp/BridgePhotoAlbum --verbosity quiet
dotnet Temp/BridgePhotoAlbum/bin/BridgePhotoAlbum/debug/BridgePhotoAlbum.dll
```

The harness links the actual bounded ZIP codec and upload service. Engine,
rendering and provider boundaries are doubles; it makes no network requests and
does not access player saves. Cases cover malformed ZIPs, identity/hash bounds,
durable delivery, profile conflicts, upload finalization, account changes,
redesign callbacks and recovery of a known interrupted transaction.
The cloud-save harness separately executes startup media recovery and verifies
that cached offline/changed-profile saves cannot overwrite online photos.

`--write-fixture <absolute Temp path>` emits an archive from the production C#
writer for the website's cross-runtime test. `--read-fixture <path>` validates a
website-written archive using the production C# reader. Real capture quality,
Android storage behavior and actual provider round trips still require device
acceptance.
