# Cloud save / first-wallet-import readiness

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' run --project Tests/CloudSaveWalletReadiness/CloudSaveWalletReadiness.csproj
```

This project compiles the **actual** `CloudSaveManager.cs` and `GameWalletModels.cs`. It replaces only Unity scheduling/dialog/JSON boundaries, the player-save owner, encryption, and PlayFab file transports. Queued callbacks exercise real startup, failure, retry, conflict, upload-finalization, source-readiness and reservation code; the tests are not copies of method excerpts.

The fixtures use validated, unique temporary directories and synthetic account/save data. They do not start Unity, connect to PlayFab/the website/database, read credentials, alter player saves, or grant currency. The encryption double tests account-purpose binding and controlled failures, not production cryptography.

Coverage includes offline Resume without first-import approval, strict selection intent after a cleared callback, later retry approval, unexpected or missing online files, local/guest/fresh finalization, legacy-encryption upgrade, conflict choices and backup failures, in-flight/portrait exclusion during source reservation, account/generation reset, and stale download/finalization/release callbacks. Actual Android storage, Unity presentation, browser identity, and deployed monetary acceptance remain separate checks.
