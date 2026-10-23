# Concrete Pier regression checks

Run from the Unity project root:

```powershell
dotnet run --project Tests/ConcretePier/ConcretePier.csproj -- --source-only
dotnet run --project Tests/ConcretePier/ConcretePier.csproj
dotnet run --project Tests/PierStressRegression/PierStressRegression.csproj
dotnet build Tests/ConcretePier/EditorCompile.csproj --nologo
```

The source-only mode works before the new asset has been wired in Unity. The default mode additionally checks the configured Concrete Pier SO, original save identity, icon, additive model wrappers, and the shared prefab's inactive, full-cell, aspect-preserving entry. Loaded scene rendering and authoring remain native checks. The separate editor project compiles both authoring scripts and the icon fitter against the installed Unity/project libraries; it never imports its output into Unity.

The runner compiles the actual `BridgeMaterialSO` source, the Roslyn-extracted production `ContractSO.IsMaterialHidden`, and production material-selection/purchase methods. Only engine, scene-singleton, visual-panel and wallet boundaries are doubled. Scenarios cover exact asset identity, empty/null/unrelated allowances, explicit hiding, legacy purchase/quantity behavior, unavailable purchases, and delayed confirmation after a contract change. AST checks enforce manual-draw, auto-draw and atomic clipboard gates before placement/deletion/replacement.

`Baselines.json` captures the supplied model/FBX/sprite bytes and their metadata, existing material/contract assets, and restoration/network catalog sources before Concrete Pier wiring. Existing dirty NPC story work is deliberately included in that baseline. The later explicit authorization to replace Marsh's Wood Pier normalizes only his exact third allowance GUID back to the original before checking that same original hash; the baseline hashes are not replaced. Every other contract remains byte-identical. These checks never restore or rewrite a file.

Unity's explicit Save Project also removed quotes/folded lines in Marsh's asset. The original, native-before, native-after and successful native-proof fixtures retain their exact hashes. Tests compare all original/native-before scalar tokens, prove that native before/after differs by exactly one GUID, and require the protected-field report before accepting the one exact native-after hash. This is a narrow documented serialization exception, not a new baseline for other edits.

The suite does not claim native Unity rendering, physical-model bounds, actual mouse/touch clicks, or simulation stability. Those require the separate native preview/asset checks. It does not log in, send a request, buy anything, alter player data, or open/save a Unity scene.

The later cropped-sprite assignment uses the new `pier concrete not selected.png` and `concrete pier2 selected (1).png` assets. `CroppedSprites.json` captures these additional files separately; the original image/model/contract baseline remains intact. Source checks preserve both new PNGs and their GUIDs/metadata byte-for-byte, and strict checks require the new pair on Concrete Pier and its shared toolbar entry. The two supplied canvases intentionally remain 515×596 and 529×621: any visual registration is a UI presentation concern, not permission to stretch or rewrite the artwork.

The pair is opt-in registered in one 560×658 virtual pixel frame. The selected source body is uniformly corrected by 565/534, and measured center offsets align both pier bodies while reserving the existing selection lift. Tests inspect the actual component's registration branch, its unchanged legacy fallback, and the serialized shared-entry configuration; the native previews separately verify body height/center, aspect, clipping and click targets.

## Authoring notes

Marsh alone now replaces his third Wood Pier allowance/introduction with Concrete Pier, as explicitly requested; all other contracts are unchanged. Concrete Pier's **Requires Explicit Contract Allowance** flag stays checked, so it remains hidden unless a chosen contract lists that exact SO under **Allowed Materials**. If another contract previously used an empty/unrestricted list, also list the other materials you still want available: a nonempty list already has restrictive legacy semantics. **Hidden Materials** wins even over an explicit allowance; purchased unlocks cannot bypass this opt-in rule. Marsh's hidden list is not changed by this replacement.

Keep the stable material ID `Concrete Pier`. The original draft numbers for price, mass, strength, maximum length and pier settings were preserved; this task does not claim engineering balancing. The shared prefab and loaded-scene material dock entries are additive. Review and manually save any scene marked dirty by the UI addition; the authoring tool never auto-saves those scenes.
