# Pause startup visibility regression checks

Run from the Unity project root:

`dotnet run --project Tests/PauseStartupVisibility/PauseStartupVisibility.csproj`

This harness extracts the production `PauseManager.Awake`, `HideStartupPresentation`, authored button discovery, normal discovery, `Start`, and availability methods. Deterministic engine boundaries exercise owning and additive-duplicate managers, active/already-inactive presentation, null references, persistent callback discovery with foreign scenes/owners/methods, later initialization failure, and ordinary HUD availability in normal versus blocked states.

Startup must hide each manager's own modal and HUD button before duplicate rejection. It clears the local paused flag without invoking normal Resume, player input restoration, time changes, progression, or runtime UI generation. Normal owning Start is intentionally allowed to show the HUD Pause button again; that is distinct from opening the pause modal. The existing mobile fallback is retained for normal availability, never duplicate cleanup.

Source safety guards retain real pause/open and resume/close paths and the dialogue/quiz control-restoration gate. Run `Tests/DialoguePauseControls/DialoguePauseControls.csproj` separately for the actual pause/resume and dialogue input-boundary scenarios.

Saved-scene checks resolve the exact Pause manager references in Bhan House and CanyonCrossing, require the modal and serialized pause state to start closed, and verify each HUD button's persistent TogglePause callback belongs to its own manager. A normal authored HUD button may be active in the saved scene. While native authoring is pending, append `-- --runtime-only` to defer only saved-scene checks.

These checks use Unity object and event doubles, not a native lifecycle/rendering session. They do not claim support for disabled domain/scene reload or additive loading of two complete gameplay scenes. No live scene, player save, account or network state is changed.
