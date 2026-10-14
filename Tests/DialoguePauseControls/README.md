# Dialogue pause controls regression

Run from the Unity project root:

```powershell
dotnet run --project Tests/DialoguePauseControls/DialoguePauseControls.csproj --verbosity quiet
```

The .NET 8 harness parses the checked-out production C# with Roslyn, extracts the real method/property bodies, and compiles them into a disposable in-memory assembly. It does not copy or reimplement the input-ownership decisions being tested.

Production members exercised:

- `DialogueManager.StartDialogue`, `DisplayNextSentence`, `TypeSentence`, `EndDialogue`, `SkipDialogue`, `ResolvePlayerInput`, `ResolvePlayerTransform`, `IsAnyDialogueActive`, and `IsPaused`.
- `PauseManager.PauseGame`, `ResumeGame`, and `FinishResumeGame`.
- `InputManager.EnsureInputInitialized`, `SetPlayerInputEnable`, `SetLookEnabled`, `ReadMovementInput`, `ReadLookInput`, and the movement/look/mobile enablement properties.

The current implementation must pass completion and skip after three real pause/resume cycles, retain the conversation lock after resume, reject Skip/Next while paused, restore movement/look/interaction, and invoke each callback once. Binding cases include speaker-less conversations, a cached player transform, no player at start followed by a late same-scene spawn, an explicit authored binding, and fallback exclusion of other scenes, disabled remote input, and inactive players. A completion callback starts a second conversation and must retain its own callback and control lock.

A second compilation removes **only the three added `ResolvePlayerInput();` call statements**. The same pause/resume flow must then reproduce completed dialogue with movement/look still disabled. This is a narrowly controlled prior-variant experiment, not a claim to compile a historical commit.

## Boundaries

The scene/object registry, input actions, coroutine scheduler, text/UI helpers, cursor/audio, and panel animation are boundary doubles. Coroutines are not scheduled; this does **not** verify real typewriter timing, rendering, camera rotation, cursor ownership, Unity's object equality, or the native Input System. Native Edit Mode fixture checks remain separate. The tests intentionally do not introduce new rules for forced private End calls, unrelated modal/build/transition ownership, or restoring initially disabled controls.

No Unity Play session, production singleton, account, authentication, network, player save, or scene file is accessed or changed. Both compiled variants have separate in-memory static state. `bin/` and `obj/` are ignored; the test project itself is explicitly unignored for shipping.

Verified result: **249 current-source assertions + 38 prior-variant reproduction assertions**.
