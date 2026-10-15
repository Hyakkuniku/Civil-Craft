# Samsung A12 — cargo fix capture comparison

## Result

The new capture supports a substantial improvement in the tested view. Cargo updates are no longer the dominant measured main-thread cost. Effective average frame rate rose from **10.45 FPS to 26.18 FPS**, but the recording does **not** demonstrate sustained 30 FPS or confirm pickup/delivery/tutorial correctness.

## Recordings and method

- Baseline: `C:/Users/Hyakk/Downloads/A12-Performant-OFF-1.data`, 175 complete frames, approximately 16.75 seconds.
- After fix: `C:/Users/Hyakk/Downloads/A12-Performant-CargoFix-OFF.data`, 50,347,464 bytes. Retained frames 0–297: 298 complete frames, approximately 11.38 seconds. Frame 298 has missing rendering counters; frame 299 has zero duration and incomplete samples. Both trailing frames were excluded consistently from comparison averages.
- Read using Unity 2022.3.62f3 in the separate `Temp/A12Profiler` analysis project, without loading the capture into the user's open game project. Source captures were not modified.
- FPS is 1000 divided by mean raw frame duration. Marker costs are aggregate per rendered frame, including all instances. Inclusive/nested markers and CPU/GPU timings must not be added together.
- GPU numbers use the named `GPU Frame Time` counter, converted from nanoseconds. One of the 298 complete new frames reports zero GPU time; it is treated as unavailable, not free rendering. GPU mean uses 297 positive observations (baseline: 175).

## Measurements

| Measurement | Original OFF | Cargo fix OFF |
| --- | ---: | ---: |
| Effective average FPS | 10.45 | 26.18 |
| Mean frame duration | 95.72 ms | 38.19 ms |
| Median frame duration | 95.18 ms | 36.59 ms |
| 95th-percentile frame duration | 115.26 ms | 49.47 ms |
| Mean CPU total frame-time counter | 95.81 ms | 38.21 ms |
| Mean CPU main-thread counter | 95.17 ms | 36.12 ms |
| Mean valid GPU frame-time counter | 64.98 ms | 38.19 ms |
| CargoItem.LateUpdate, all instances | 44.83 ms | 0.0358 ms |
| CargoItem.LateUpdate calls per frame | 4 | 4 |
| Physics fixed-update total | 10.35 ms | 3.54 ms |
| InputManager.FixedUpdate total | 4.96 ms | 1.38 ms |
| ExpandedMinimapController.LateUpdate | 2.74 ms | 1.37 ms |
| Render-thread wait for main-thread commands | 76.57 ms | 6.33 ms |
| Batches / draw calls | 738 / 738 | 666 / 666 |
| SetPass calls | 41 | 41 |
| Triangles | 527,994 | 520,737 |

Frame time is about 60% lower and effective FPS is about 2.5 times the baseline. Cargo's measured late-update total is over 99.9% lower. Four cargo late-update calls still execute each frame, so the drop is not explained simply by those components disappearing from the recording.

## New cargo markers

| Marker | Mean inclusive time per frame | Calls per frame |
| --- | ---: | ---: |
| Cargo.Outline.Refresh | 0.01836 ms | 0.698 |
| Cargo.Availability | 0.01776 ms | 0.698 |
| Cargo.Progression.ResolveNPC | 0.00319 ms | 0.698 |
| Cargo.Progression.CheckDialogue | 0.00267 ms | 0.698 |
| Cargo.Outline.Submit | 0.00124 ms | 4 |

These markers overlap. Do not sum them as independent costs. `Cargo.HeldPose` has no timed samples, so this recording does not exercise carrying hand-follow work. The new markers confirm the updated code was present, and the measured lookup/availability/outline path is now small in this view.

The old capture did not have these child markers, so it still cannot prove exactly which old sub-operation accounted for all 44.83 ms. The overall improvement is consistent with removing the cargo bottleneck, not a controlled attribution of every saved millisecond to one line of code.

## What remains expensive

- Main-thread URP rendering totals approximately **16.68 ms**. This includes culling, draw scheduling and submission, not just GPU rendering.
- The GPU frame-time counter is approximately **38.19 ms**, above the **33.33 ms** budget for 30 FPS. Only 2 of the 298 retained raw frame durations meet that budget.
- Render-thread `Gfx.PresentFrame` averages **20.58 ms**. This can include GPU completion and presentation/VSync waits; it is not itself a pure GPU-pass measurement. See [Unity's profiler marker reference](https://docs.unity.com/en-us/engine/7000.0/manual/analysis/profiler/markers).
- Player, minimap and UI cameras still render. Minimap camera samples average about **2.90 ms main-thread** and **1.67 ms render-thread**. These are not the minimap's isolated GPU cost and should not be added to estimate its total wall-clock impact.
- Fixed steps fell to approximately **3.82 per rendered frame**, consistent with faster frames at the unchanged 0.01-second timestep. This is not evidence that physics was disabled or that the timestep was changed.

Inference: rendering/GPU work and presentation now deserve the next controlled investigation; cargo no longer justifies being the primary target in this view. Keep occlusion off for the next baseline. The retained capture contains `CullObjectsWithoutUmbra` samples and no timed Umbra query/dynamic-occlusion samples.

## Comparison limits and next step

The render workload is not identical: batches fell by about 9.8%, triangles by about 1.4%, and memory counters differ. Camera pose, visible objects, distance culling, build state and phone temperature were not independently recorded. The captures therefore support the observed improvement but cannot assign the entire gain exclusively to the cargo change or predict all areas of the map.

Before making further changes, confirm the manual gameplay checks: tutorial lock/unlock, pickup, carry, valid delivery, vehicle loading where applicable, restart persistence, and multiplayer visitor restrictions. The profile alone cannot verify those behaviors.

For the next optimization, inspect the existing Performant rendering configuration and measure one rendering change at a time at a fixed pose. Separate player-camera and minimap costs; do not globally hide world objects or compromise map completeness. Further graphics changes require their own implementation request. No gameplay scripts, scenes, quality settings, or saves were changed during this capture analysis.
