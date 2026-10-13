# Wallet read-only check policy tests

Run from the Unity project root:

```powershell
dotnet run --project Tests/WalletReadOnlyCheck/WalletReadOnlyCheck.csproj
```

This standalone .NET executable tests the pure policy linked from
`Assets/Editor/WalletReadOnlyCheckPolicy.cs`. It does not start Unity, inspect a
real player session, read credentials or saves, contact providers, or change
accounts, balances, journals or wallet settings. Its fixtures are synthetic.

A passing local policy test is not a player migration approval or permission to
enable the wallet. Live diagnostic output remains a time-specific read-only
projection requiring separate review.

## Optional Editor compiler smoke check

```powershell
dotnet build Tests/WalletReadOnlyCheck/EditorCompile.csproj
```

This compiles the actual Editor checker against the locally installed Unity and
already-generated game/PlayFab library assemblies. It does not launch Unity or
run the diagnostic. The smoke project uses a separate intermediate output path
so it does not contaminate the standalone policy executable. Installed assembly
paths must match this machine; a successful compile is not a live-state check.

## Capture the existing account without an APK build

1. In Unity Play Mode, sign in to `testBetaOmaygosh` and load its existing save.
   Do not select Start Fresh or import a guest save again.
2. Wait for online save synchronization and any account-save choice to finish.
3. Open **Civil Craft > Wallet > Read-Only Check**.
4. Select **Capture live read-only check**, then **Copy safe report** and share
   that report for review.

The capture does not refresh balances, change accounts, save player data, modify
the journal, contact the website or PlayFab, import currency or activate Coins.
Do not change Inspector values to force a passing result. A NOT VERIFIED result
requires reviewing the listed reason, not resetting or deleting account data.
