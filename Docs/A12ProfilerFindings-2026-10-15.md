# Galaxy A12 capture findings — 15 October 2026

## Inputs and method

- `C:/Users/Hyakk/Downloads/A12-Performant-OFF-1.data` (36,424,468 bytes).
- `C:/Users/Hyakk/Downloads/A12-Performant-ON.data` (36,198,556 bytes).
- Read using Unity 2022.3.62f3 `ProfilerDriver.LoadProfile` and `RawFrameDataView` in an isolated batch-mode analysis project. The open game project, scene, source recordings and live Profiler session were not modified.
- All retained frames were included: 175 OFF, 173 ON. Raw thread frame durations, marker timings and named counters were exported under ignored `Temp/A12Profiler/`. Means below are per recorded frame; FPS is 1000 divided by mean frame duration, not an average of instantaneous FPS.
- Named CPU/GPU frame-time counters were interpreted as nanoseconds and converted to milliseconds. The separate raw `frameGpuTimeMs` property returned zero; GPU numbers below come from the populated `GPU Frame Time` counter, not a detailed GPU pass breakdown.

## Comparison

| Measurement | OFF | ON |
| --- | ---: | ---: |
| Mean raw frame duration | 95.72 ms | 95.79 ms |
| Effective average frame rate | 10.45 FPS | 10.44 FPS |
| Median raw frame duration | 95.25 ms | 91.98 ms |
| 95th-percentile raw frame duration | 115.26 ms | 151.85 ms |
| Mean CPU total frame-time counter | 95.81 ms | 96.38 ms |
| Mean GPU frame-time counter | 64.98 ms | 64.59 ms |
| Batches / draw calls per frame | 738 / 738 | 738 / 738 |
| SetPass calls per frame | 41 | 41 |
| Triangles per frame | 527,994 | 528,058 |
| Mean CargoItem.LateUpdate total | 44.83 ms | 43.54 ms |
| Mean physics fixed-update total | 10.35 ms | 11.33 ms |
| Mean InputManager.FixedUpdate total | 4.96 ms | 5.19 ms |
| Mean ExpandedMinimapController.LateUpdate total | 2.74 ms | 2.71 ms |

CPU marker totals are inclusive and can overlap their descendants; do not add parent and child rows. CPU and GPU also overlap in time and must not be added together.

## Findings

### 1. Average culling ON/OFF performance is essentially tied in these recordings

Mean frame duration differs by only 0.07 ms. The ON recording has worse upper-tail stalls, but one capture each, with no second OFF control, cannot establish that culling caused them. The OFF run itself drifts from an approximately 81.5 ms mean in its first 25 frames to 105.5 ms in its final 25 frames. Heat, scheduling, or other changing conditions need controlling; this is not proof of thermal throttling.

Rendering counts do not show a meaningful saving: 738 batches and 41 SetPass calls in every retained frame of both captures. The small 64-triangle difference does not demonstrate a culling gain.

### 2. Active baked occlusion was not demonstrated by the ON file

Both files contain `CullObjectsWithoutUmbra` samples. Neither contains timed `CullQueryPortalVisibilityUmbra` or `CullDynamicObjectsWithUmbra` samples. Those names exist in the marker registry, but registered names alone do not prove execution.

This suggests that the ON checkbox may not have resulted in active native occlusion for this view. The files do not capture our overlay text or all policy gates, so they cannot establish which gate was responsible. Check the actual **Occlusion: ACTIVE** indicator after closing Settings. If it instead reports outside coverage, suspended, or bake unavailable, address that condition before repeating the A/B test. A screenshot of the ON view/status would resolve this uncertainty.

### 3. Cargo updates are the clearest CPU optimization lead

`CargoItem.LateUpdate` runs four times per frame and takes a combined 43.5–44.8 ms—roughly 45–47% of the measured main-thread frame. This is the total across four calls, not 44 ms per item. The render thread spends about 76.5 ms per frame under `Gfx.WaitForGfxCommandsFromMainThread`, consistent with a substantial main-thread bottleneck.

Current source path:

`CargoItem.LateUpdate → UpdatePickupOutline → IsInteractionAvailable → ContractSO.IsCargoInteractionUnlocked → NPCProgressionManager.FindByProgressionSaveId`

The progression lookup performs `Resources.FindObjectsOfTypeAll<NPCProgressionManager>()`. Cargo availability also checks its parent vehicle and saved delivery/loading state. These are candidates for cached/event-driven evaluation; they are not individually proven to account for the full 44 ms. The current capture exposes the enclosing behaviour and approximately 0.63 ms of named `FindObjectsOfType` samples, leaving much of the cost without a finer marker. Add narrow markers around availability and outline work before attributing the remaining cost to a specific operation. Avoid Deep Profile on the A12 for the first follow-up.

### 4. Physics catch-up contributes additional work

The current project's fixed timestep is 0.01 seconds (100 Hz). Captures show approximately 9.64–9.65 physics/InputManager fixed updates per rendered frame. Physics accounts for roughly 10–11 ms and InputManager fixed updates for another 5 ms. Do not casually change the timestep: bridge, cargo, vehicle and player behavior require regression testing if it is adjusted.

### 5. Graphics still require attention after the CPU issue

The GPU frame-time counter averages about 65 ms, well above the 33.3 ms budget for 30 FPS. Removing a CPU cost alone therefore does not justify promising 30 FPS. Minimap, player and UI cameras all render in the recordings; camera/render-pass CPU samples cannot be used as their individual GPU costs. Profile render scale, opaque geometry, shadows and post-processing separately after addressing the main-thread hotspot.

## Recommended next work (not implemented by this analysis)

1. Keep release occlusion disabled.
2. Confirm the ON overlay status and obtain OFF → ON → OFF captures from a fixed view within baked coverage.
3. Instrument the cargo availability/outline path, then optimize the demonstrated repeated work while preserving story gating, pickup, loading and delivery behavior.
4. Repeat the A12 capture after that change before changing physics or graphics settings.
5. Only then prioritize GPU/rendering reductions using further measurements.

No gameplay scripts, quality settings, camera policy or player saves were changed during this analysis.

## Authorized cargo follow-up

The subsequent implementation changes only repeated lookup/visual-hint work and the development culling default:

- `ContractSO` caches the resolved NPC identity, not its unlock result. Dialogue completion remains queried live. Destroyed/unloaded references, changed IDs, and reload-disabled session changes invalidate the reference. Missing NPCs are not negatively cached; inactive story NPCs retain the previous lookup semantics. Edit Mode checks continue resolving their current scene directly.
- `CargoItem` refreshes cosmetic availability at most once every 0.2 seconds. Held, restoring, loaded, disabled, or visitor-inaccessible cargo hides its hint immediately. Contract replacement and disable cleanup reset the schedule. The outline is still submitted each frame, retaining its renderer registration behavior.
- Pickup, vehicle loading, delivery, save operations, tutorial progression, carried-object hand positioning, and cargo physics retain their existing logic. Actual interaction never uses the cached visual state. An ordinary unlock/re-lock appears on the next 0.2-second visual poll (quantized to a rendered frame), but the interaction query itself is immediate.
- Editor/development sessions now start with occlusion explicitly off, even when an older Inspector test flag was saved on. The developer can still opt into a controlled comparison through Settings. Release builds remain off. No scene edits are required.

Standalone verification: `dotnet run --project Tests/CargoPerformance/CargoPerformance.csproj` passes **1,052** actual-source/boundary and structural checks. This covers reference reuse, live unlock/re-lock, respawn, scene/session invalidation, cosmetic scheduling, immediate hide conditions, disposal, and uncached interaction. Camera-policy regression checks pass **846** behavior assertions and **54** structural guards. These are isolated tests, not a live pickup/delivery playthrough or an Android performance result.

### Next device check

1. Refresh Unity scripts out of Play Mode and build/install a new **Development Build**; the old APK does not contain these changes. Keep Deep Profile off and quality set to Performant.
2. Verify cargo before/after its tutorial dialogue: locked before completion, pickable afterward. Pick up, carry, place at its allowed destination, load into a vehicle where applicable, and reload the save. Check delivery/loaded state persists and no tutorial step is bypassed. In multiplayer, verify a visitor cannot alter host cargo.
3. In the same A12 position/view as the original OFF capture, keep occlusion off, minimap visible, and all graphics settings unchanged. Close Settings and record another 10–20 seconds with CPU, Rendering and Memory modules. Include a short held-cargo segment separately if practical. Control device temperature and avoid mixing menu/loading frames into the stationary comparison.
4. Save as `A12-Performant-CargoFix-OFF.data`. Compare total frame time and `CargoItem.LateUpdate` against the original OFF recording, plus these new child markers:
   - `Cargo.Outline.Refresh`: visual availability polling.
   - `Cargo.Availability`: full live interaction predicate.
   - `Cargo.Progression.ResolveNPC`: cached identity validation or fallback lookup.
   - `Cargo.Progression.CheckDialogue`: current dialogue/save gate.
   - `Cargo.Outline.Submit`: outline creation/visibility submission.
   - `Cargo.HeldPose`: hand-follow transform work while carrying.
5. Do not sum nested marker totals. If `Cargo.Outline.Submit` or another component still dominates, optimize that measured work next rather than assuming the lookup explained the original 44 ms.

The phone result and full gameplay regression remain pending. GPU time was approximately 65 ms in the supplied recordings; this CPU change alone does not establish a 30 FPS result. Physics timestep, quality settings, other cameras, and player save formats are unchanged.
