# Brooks story regression checks

Run from the Unity project root:

```powershell
dotnet run --project Tests/BrooksStory/BrooksStory.csproj -- --source-only
```

The source-only mode validates the Moving Load lesson/assessment (answers **B, C, A, D, B**), existing contract identity and user-editable balancing, scoped continuation callbacks, and safe Editor-authoring boundaries. The complete production continuation helper is compiled with Roslyn against isolated boundary doubles and its coroutine/public cinematic callback are executed for completed/incomplete saves, unavailable data, visitors, stale or invalid phases, paused/busy UI/travel, exactly-once continuation and normal progression winning the race. It does not claim that a loaded or saved scene has changed. `--helper-only` explicitly omits authoring and saved-scene checks.

After applying the explicit Brooks migration and manually saving CanyonCrossing:

```powershell
dotnet run --project Tests/BrooksStory/BrooksStory.csproj
```

Strict mode additionally checks the exact serialized NPC/contract/build-site references, six-phase learning/contract/assessment flow, three-shot cinematic and follow-guide wiring, existing Ford visibility gate, and preservation of earlier Marsh/Ford progression. It verifies the existing inherited single-vehicle/finish prefab unchanged and handles Ronan's stripped prefab records without assuming local complete components: prior name/ID/pose overrides and empty inherited phases are preserved, while the new visibility rule is bound to his original prefab root. Native cinematic framing, animation/input restoration, Android behavior and end-to-end persistence remain separate tests.

Compile only the three new Brooks scripts against the installed Unity/project/URP managed references, without opening/importing Unity:

```powershell
dotnet build Tests/BrooksStory/EditorCompile.csproj --nologo
```

Output stays inside this test directory and never overwrites production assemblies. If Unity has already imported the helper, this standalone compilation may warn about its intentional source/imported duplicate type; native Unity import remains a separate check.

No new Taxi → Cargo sequential test is assumed: current single-player completion is triggered by the first matching live-load vehicle reaching its finish line. Story updates must not claim two independently passed vehicle tests unless a separate supported system is explicitly implemented and tested.

Following Ronan's own authorized story update, Brooks's checks allow only that manager's new `phases.*` and `authoredSpawnLocation` overrides; the twelve original Ronan identity/pose overrides and shared empty NPC template remain protected. Ronan's complete phase content is verified separately by `Tests/RonanStory`.

The truck fingerprint is based on the scene backup taken **before** Ronan's migration. Compared with the original Brooks baseline, only cargo-slot IDs on source components `4463698897250604490` and `8805695919974729455` had already changed, to `9c1eaa0d49b24024a798eb9eea0d6228` and `313c81b8ae3f4aa0b6aec57846cc21e7`. Those existing save identities are preserved—not restored, regenerated, or ignored—and the entire latest Brooks truck/finish/route block remains fingerprinted.

These checks are read-only. They do not import/open/save Unity scenes, invoke real gameplay/reward callbacks, access player saves/accounts, contact the website, or use the network. Test build outputs remain ignored locally; project/source files are versionable.
