# Solo simulation playback

The build UI has `<< 0x >>` controls beside its existing simulation Start/Stop group. They appear only during a solo test. The left arrow slows playback; the right arrow speeds it up. A new attempt starts at its original speed. Nothing is stored in a player save or contract asset.

`0x` is the requested label for ordinary **running** speed (Unity time scale 1), not a frozen simulation. Other labels show the composed speed, such as `0.5x` or `3x`. The six player settings are 0.25, 0.5, 1, 1.5, 2 and 3 times the attempt's entry speed. An authored simulation lesson's temporary slowdown composes with that setting; closing the lesson removes only its own slowdown.

## Scope and timing

- The build-view control appears during single-player bridge tests, including settling. Drawing and overworld exploration do not show it. The existing player-carried cargo view keeps its own interface.
- Fusion hosts, guests, legacy network sessions and bridge competitions cannot change playback through these controls. An active network session counts as multiplayer even if only the host is present.
- Modal panels, dialogue, pause, loading and result screens suppress interaction. The ordinary overworld Pause menu retains its current build-mode restrictions.
- Fixed physics timestep and solver settings stay unchanged. Vehicle motion, timed tests, damage and multiple-load departure delays therefore advance in simulation time at the selected pace. Higher speeds need more physics work per real second; reaching the requested 3x pace depends on the device keeping up.
- Stop, retry, save/finalization, success, failure, disabling the manager and leaving Build Mode release the speed. Pause and cloud-save conflict dialogs restore the appropriate scale without unpausing the game during teardown.
- If a developer tool takes over the clock, playback releases ownership until the next attempt rather than repeatedly overriding that tool.

## Authored UI and checks

`Tools → Civil Craft → Simulation Speed` provides scoped UI authoring, source validation, phone/tablet previews and native lifecycle checks. The shared BuildUI prefab and both gameplay scenes contain inactive authored controls. At runtime, older scene copies can repair missing controls through `SimulationSpeedHUD.Ensure`.

The authoring command validates detached copies before changing scene sources and preserves the existing Start/Stop and Undo/Redo buttons. It backs up sources outside Assets, saves only initially clean scenes, and leaves existing unsaved edits unsaved. On narrow layouts the speed group moves below the same right-hand control cluster to keep the budget display clear.

The control uses the existing simulation toolbar's rounded translucent background, material, tint and opacity, with white arrows and speed text. The arrows have the same 76-unit visible size and press/disabled tint as the existing icons. Transparent hit surfaces retain the larger mobile tap areas; there are no cream button tiles. A separate authored background keeps the visible toolbar height aligned while the reserved touch area can grow for Android density. Style version 2 is applied to both existing authored controls and runtime repairs.

Run the independent state checks with:

```powershell
dotnet run --project Tests/SimulationPlaybackSpeed/SimulationPlaybackSpeed.csproj
```

Native reports are written to `Logs/simulation-playback-lifecycle.txt` and `Logs/simulation-speed-ui.txt`; detached images go to `Logs/SimulationSpeedUI`.

Verification on November 8, 2026: 56 independent state assertions and 51 native clock/lifecycle assertions passed. Both scenes passed 12 combined previews across phone, tablet and short-screen layouts, including actual UI raycasts, safe-area and budget clearance. Runtime and Editor sources compiled in Unity 2022.3.62f3.

Device follow-up: on the Samsung A12, test one ordinary crossing and a 2–4 vehicle crossing at every speed, open/close a simulation lesson, stop/retry, and return to the overworld. Check motion and touch placement. No Android device profiling or two-device network session is claimed by the desktop checks.
