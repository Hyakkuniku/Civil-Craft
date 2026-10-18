# Startup result/tutorial visibility

Run `dotnet run --project Tests/StartupPanelVisibility/StartupPanelVisibility.csproj` from the Unity project root.

This harness extracts the actual `Awake` methods from `LevelCompleteManager`, `LevelFailedManager`, and `TutorialManager`, plus the tutorial's startup-hiding helper. It executes these bodies against minimal engine boundaries, checking primary acquisition, additive duplicate rejection, active and already-inactive linked panels, absent UI references, safe hiding before injected layout/style failures, and normal subsequent panel activation. Duplicate cleanup must not replace or destroy the primary manager or delete separately authored panel GameObjects.

Structural guards prohibit calling normal close/complete/skip/save/input-restoration actions for startup cleanup and confirm real completion/failure/tutorial presentation paths still open their UI. Serialized checks resolve each manager's exact panel references in Bhan House and CanyonCrossing and require those references to start inactive. Other scenes and prefabs are outside this saved-default check's scope.

While waiting for a user-reviewed scene save, `dotnet run --project Tests/StartupPanelVisibility/StartupPanelVisibility.csproj -- --runtime-only` runs the production startup bodies and safety guards without requiring the pending serialized defaults. The final verification should use the full command after saving both scenes.

These are deterministic boundary tests, not native Unity lifecycle/rendering tests. They do not promise support for additive loading of two complete gameplay scenes or change Enter Play Mode reload settings. Test ordinary gameplay with CanyonCrossing alone. No scene, player save, reward, account or network mutations occur.
