# Ronan story regression checks

Run from the Unity project root:

```powershell
dotnet run --project Tests/RonanStory/RonanStory.csproj -- --source-only
```

Source-only checks cover the Shear lesson and five-question assessment (answers **B, C, A, D, B**), existing contract identity, user-owned budget/load/rewards, safe Editor authoring and actual continuation-helper control flow against isolated Unity boundary doubles. The assessment accepts any submitted score and grants no extra rewards.

After explicit authoring and manual CanyonCrossing save, run without `--source-only` for serialized-scene checks. These preserve the earlier Marsh, Ford and Brooks stories and Ronan's existing Brooks gate. Ronan is an inherited prefab: the runner resolves the exact source component's durable scene overrides without assuming a complete local NPC record or unpacking the prefab. Unity omits zero/empty/null values for newly expanded prefab-array elements; the runner accepts only those known defaults after proving the source array is empty and checking that authoring explicitly clears feature rewards. Required IDs, nonzero references, array sizes and enabled callbacks remain mandatory.

The whole current Ronan contract is a task baseline: its future **100-metre, different-elevation** brief and **2000 budget/50 load** placeholders remain unchanged. The physical site deliberately remains **70 metres at equal height** until the author redesigns it. Intentional future contract/site balancing requires updating the corresponding baseline checks; this story migration does not perform that work. The existing vehicle's one corrected contract reference is normalized narrowly in its fingerprint; its finish line, route, cargo and physics overrides must otherwise remain identical.

`--helper-only` deliberately skips authoring/saved-scene checks. `dotnet build Tests/RonanStory/EditorCompile.csproj --nologo` compiles only the new scripts against installed Unity/project references with isolated test outputs.

No region-unlock system or shear-specific physics/visualizer is assumed. The existing truss stress test remains unchanged; Shear is educational lesson/Almanac/quiz content. No live gameplay/reward callbacks, scene editing/import/saving, player data/accounts, website, or network are accessed by this runner. Native camera framing, animation/input restoration, Android behavior and durable end-to-end testing remain separate checks.
