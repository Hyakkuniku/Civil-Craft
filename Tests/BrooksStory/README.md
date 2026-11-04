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

Strict mode additionally checks the exact serialized NPC, contract and build-site references, six-phase learning and assessment flow, cinematic and follow-guide wiring, existing Ford visibility gate, and preservation of earlier Marsh and Ford progression. It checks Ronan's original prefab identity and visibility rule. Native cinematic framing, animation and input restoration, Android behavior and end-to-end persistence remain separate tests.

Compile only the three new Brooks scripts against the installed Unity/project/URP managed references, without opening/importing Unity:

```powershell
dotnet build Tests/BrooksStory/EditorCompile.csproj --nologo
```

Output stays inside this test directory and never overwrites production assemblies. If Unity has already imported the helper, this standalone compilation may warn about its intentional source/imported duplicate type; native Unity import remains a separate check.

Following Ronan's own authorized story update, Brooks's checks allow only that manager's new `phases.*` and `authoredSpawnLocation` overrides; the twelve original Ronan identity/pose overrides and shared empty NPC template remain protected. Ronan's complete phase content is verified separately by `Tests/RonanStory`.

The saved-scene checks verify the taxi-first Sequential roster, the workbench bindings, both Brooks finish assignments and the original Marsh taxi assignment. The existing truck's two persistent cargo-slot IDs remain protected while the positioning stays editable. Use `--vehicles-only` to run these checks separately. The Unity validation menu also checks the loaded scene's physical vehicle envelopes and fixed waypoints.

These checks are read-only. They do not import/open/save Unity scenes, invoke real gameplay/reward callbacks, access player saves/accounts, contact the website, or use the network. Test build outputs remain ignored locally; project/source files are versionable.
