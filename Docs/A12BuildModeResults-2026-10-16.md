# A12 build-mode capture and hidden minimap fix

## Recording

`C:/Users/Hyakk/Downloads/A12-Performant-CargoFix-OFF_BUILDMODE.data` (56,893,688 bytes), recorded with the cargo fix and occlusion off.

Read with Unity 2022.3.62f3 in a separate analysis project under ignored `Library/A12BuildAnalysis`. The capture and game scenes were not modified. Of 300 exported frames, 298 have complete nonzero durations and rendering counters (indices 0–297); the last two incomplete frames were excluded. Retained duration is approximately 26.01 seconds. GPU values are the populated `GPU Frame Time` counter in nanoseconds, converted to milliseconds.

## Results

| Measurement | Updated normal gameplay | Build mode |
| --- | ---: | ---: |
| Effective average FPS | 26.18 | 11.46 |
| Mean raw frame duration | 38.19 ms | 87.30 ms |
| Median frame duration | 36.59 ms | 87.07 ms |
| 95th-percentile frame duration | 49.47 ms | 92.66 ms |
| Mean CPU total counter | 38.21 ms | 87.44 ms |
| Mean CPU main-thread counter | 36.12 ms | 50.68 ms |
| Mean valid GPU frame-time counter | 38.19 ms | 87.27 ms |
| Batches / draw calls | 666 / 666 | 622 / 622 |
| SetPass calls | 41 | 71 |
| Triangles | 520,737 | 292,484 |
| CargoItem.LateUpdate, all four instances | 0.036 ms | 0.07 ms |
| Physics fixed-update total | 3.54 ms | 12.80 ms |

Normal gameplay and build mode use different views and rendering features; this is a workload comparison, not an on/off test proving one feature's GPU cost. CPU/GPU overlap, and nested profiler sample totals must not be added together.

Cargo remains inexpensive. The build capture spends about 36.4 ms per frame waiting for rendering from the main thread, while the render thread's `Gfx.PresentFrame` averages 64.5 ms. Presentation markers can include GPU/presentation waits; they do not identify an individual expensive shader. Together with the reported GPU frame time, the evidence points toward graphics as a major build-mode limit. Fewer triangles and batches do not guarantee faster rendering, especially with different screen coverage and passes.

## Confirmed hidden minimap rendering

The capture shows all three cameras rendering every retained frame:

| Camera | Main-thread camera sample | Render-thread camera sample |
| --- | ---: | ---: |
| build_camera | 5.81 ms | 14.19 ms |
| MinimapCamera | 1.16 ms | 2.80 ms |
| UICamera | 0.65 ms | 1.05 ms |

The separate enclosing `Inl_UniversalRenderPipeline.RenderSingleCameraInternal: MinimapCamera` sample is approximately 2.87 ms on the main thread. It overlaps inner work; do not add it to the table. These are CPU timings, not isolated per-camera GPU costs.

The saved CanyonCrossing scene hides `MinimapPanel` when building, but its independently rooted `MinimapCamera` remains enabled. The map controller already knows the camera should be off while building; build entry was not calling that visibility refresh. The scene has no persistent build-entry/exit event callbacks that repair the omission.

## Implemented correction

Use the existing `MinimapUnlockController.RefreshAll()` after each relevant HUD change:

- Normal build entry: after capturing and hiding the overworld UI. Capturing first preserves the original return state.
- Cargo-test entry: after restoring the overworld UI, so an unlocked map can render during the playable cargo crossing.
- Cargo-test return to editing: after hiding the overworld UI again.
- Multiplayer challenge entry and exit: after their corresponding UI capture/restore.
- Ordinary build exit already refreshed the map; that call remains.

The camera component's existing visibility rules control rendering. The camera GameObject, map RenderTexture, layers, quality settings, occlusion policy, zoom/pan, markers, bridge outlines, input, simulation, and player save data are unchanged. Locked maps remain locked; visitor maps and lesson suppression retain the existing rules. The refresh runs at transitions, not as a new per-frame scene search.

This fixes a concrete waste of rendering work. It does not establish that the minimap explains the entire 87 ms GPU frame or that build mode will reach 30 FPS. Broad graphics reductions were not applied.

## Verification and next capture

Runtime and Editor compilation passed against the project's Unity 2022.3 dependencies (one existing unused-field warning in ShopManager). `dotnet run --project Tests/BuildMinimapRendering/BuildMinimapRendering.csproj` passed 39 visibility/lifecycle checks and 16 source transition-ordering guards. The tests execute the actual visibility and captured-UI methods plus authored transition statements against isolated boundary doubles, covering unlocked/locked/legacy/guest maps, lessons, cargo and challenge return paths, null references, original hidden UI, and render-texture preservation. No player saves or network access are used. This is automated logic verification; live rendering, input, and Android performance still require the device pass below.

For the device comparison:

1. Exit Play Mode, refresh scripts, and build/install a new Development APK. No scene save is required for this code change.
2. With the map unlocked, verify it works normally, then enter the same build site as the supplied capture. The map should hide. Exit and verify its contents, markers and expanded pan/zoom still work.
3. Where available, start/cancel a player-carried cargo test and enter/exit a multiplayer challenge. Check that the map returns during overworld views and hides again in editing. Check a locked account and an open lesson still suppress it.
4. Record 10–20 seconds at the same build camera pose, quality and bridge state with occlusion off. Save `A12-Performant-BuildMapFix-OFF.data`.
5. In steady build mode, `MinimapCamera` should no longer have timed render samples. Compare frame time, GPU counter, batches, SetPass and memory. Then capture a brief return to normal gameplay to verify that map rendering resumes.

Further investigation should measure the remaining build camera passes at the same site. Magnifier rendering was absent in this stationary capture. The broad per-camera CPU samples cannot establish which material/pass causes the remaining GPU time. Android performance and full interactive regression of this change remain pending.
