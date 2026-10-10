# Saved wallet scene shipping

Run from the Unity project root:

```powershell
dotnet run --project Tests/GameWalletSceneShipping/GameWalletSceneShipping.csproj
```

This executes the same saved-asset policy used by the Unity pre-build check. It
requires the authored dialog and both plus buttons plus manual balance refresh in
all four shipping shop scenes. Wrong ShopManager callback targets, inactive or
disabled controls, disconnected prefab references and missing dialog fields fail.
It makes no network request and does not open, save or modify Unity scenes.

For the first scene publication only, when the committed baseline has no wallet
controls, this command prints a wallet-only candidate on stdout:

```powershell
dotnet run --project Tests/GameWalletSceneShipping/GameWalletSceneShipping.csproj -- `
  '<absolute Unity project path>' --export-wallet-scene 'Assets/Scenes/CanyonCrossing.unity'
```

The candidate retains every existing baseline YAML document. It copies only the
new dialog instance, Diamond plus/refresh object subtrees and five allowlisted
existing-document changes: the ShopManager dialog field, the two currency row
child references, the Diamond amount layout and the new dialog root reference.
The candidate must pass the same scene policy. Existing object ID reuse fails.
Nothing is written, staged or committed. Review the candidate diff before placing
it in Git's index; never stage a mixed scene wholesale. Existing unrelated working
scene changes must remain on disk after publication.

These checks certify serialized wiring, not rendered appearance or device/browser
acceptance. Build a new APK after the source and scene publication. Test the game
guest/login/account mismatch/cancel/refresh/offline/multiplayer flows and a verified
test payment before enabling the production wallet. Do not upload or replace an
installer merely because the compile or scene checks pass.

## Explicit local Android acceptance build

`WalletAcceptanceBuild.BuildAndroid` is an opt-in batch-mode entry point. It never
switches targets or changes player settings. The checkout must already be Android
IL2CPP/ARM64 and its saved scenes must pass validation. Use a clean release checkout
containing only reviewed source and serialized scene changes; do not package
unrelated dirty working scenes. Read-only Git checks enforce a verified HEAD, no
changed tracked Assets/Packages/ProjectSettings and no untracked files (including
ignored compiled assets) in those directories, both before and after the build.
Git errors fail closed; ignored
Library/Builds/log output does not make the checkout dirty. Example after
publication and review:

```powershell
& 'D:\UNITY\2022.3.62f3\Editor\Unity.exe' -batchmode -quit -buildTarget Android `
  -projectPath '<absolute clean release checkout>' `
  -executeMethod WalletAcceptanceBuild.BuildAndroid `
  -civilCraftWalletApk '<absolute clean release checkout>\Builds\WalletAcceptance\CivilCraft-wallet-acceptance-20261010T120000.apk' `
  -logFile '<absolute NEW build log path>'
```

The output must be a new `CivilCraft-wallet-acceptance-*.apk` under that checkout's
`Builds/WalletAcceptance` directory. Existing outputs, sibling-prefix escapes,
path traversal and junction/symlink output directories are rejected. A failed
build may leave a partial APK; choose a new filename for a retry. Choose a new log
path too: Unity manages `-logFile` before the build entry point can run.

The APK uses Development/AllowDebugging for device acceptance. It is not a signed
production release. This helper does not install, upload, publish, open a browser,
enable the wallet or request any payment/account action.

## Reproducible TMP fallback baseline

TMP3.0.7's build preprocessor clears Dynamic fonts with Clear Dynamic Data On Build
enabled. The reviewed LiberationSans SDF fallback is committed with its generated
glyph/character/rectangle cache and atlas pixels already cleared. Its source font,
material, subasset IDs, Dynamic mode and Clear On Build setting remain unchanged.
TMP therefore finds an already-empty atlas instead of modifying tracked inputs.

Acceptance preflight checks ONLY that exact fallback path and protected-byte
fingerprint. It is read-only: it never edits a font, switches off cache clearing,
touches Internet Friends or exempts any file from the general Git clean guard.
Changed source/config/material or regenerated cache requires review before building.
The original-versus-Unity-generated proof can be checked without writing anything:

```powershell
dotnet run --project Tests/GameWalletSceneShipping/GameWalletSceneShipping.csproj -- `
  '<absolute original checkout>' --verify-font-clear '<absolute Unity-cleared fallback asset>'
```
