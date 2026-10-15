# Camera-safe occlusion culling — CanyonCrossing

This setup is limited to **CanyonCrossing**. It uses native baked visibility for the ordinary player camera without hiding shared world objects. Compact and expanded maps, build/simulation, magnifier, bridge captures, UI, portrait and loading cameras stay excluded. Existing mobile scenery distance culling is unchanged.

The developer master switch initially starts **off**. Normal release builds now explicitly force gameplay occlusion **off**, even if that Inspector switch was saved on during testing. A successful bake is not proof of a performance improvement: production rollout requires visual checks and an Android on/off comparison showing a net frame-time improvement.

## Development Settings and FPS counter

In Editor Play Mode, or an Android build made with **Build Settings → Development Build** checked, open **Settings → Graphics**:

- **Test Occlusion (Dev)** overrides the scene's test switch for this app session. Turn it on/off at the same camera position, then close Settings before comparing gameplay. All existing camera, coverage and suspension gates still apply. Maps and other excluded cameras remain excluded.
- **Show FPS Counter (Dev)** displays average FPS, average frame interval in milliseconds, and the actual occlusion state at the top-center safe area. The overlay does not intercept input. It is off at session start.

These controls are added at runtime using the existing Settings style; no scene save is needed. Their choices are not written to player saves or preferences. Following the A12 baseline, every fresh Editor/development session explicitly starts occlusion **off**, even if the authored scene test switch was saved on. Use **Test Occlusion (Dev)** to opt in for a controlled comparison. Normal release builds create neither control nor overlay and ignore an authored occlusion-on switch.

**ACTIVE** means the gameplay camera is currently using occlusion. **Outside baked area**, **suspended**, or **bake unavailable** means turning the test checkbox on has not enabled it for that view. The player starting position can be outside coverage. FPS and milliseconds measure frame cadence, not separate CPU/GPU work; continue using the Unity Profiler for device acceptance. Keep quality and frame-rate cap identical between runs; a cap can mask improvements.

Native UI check: **Tools → Civil Craft → Performance → Validate Development Settings** uses a detached preview and preserves the source scene. Reports/previews are saved under `Temp/development-performance-settings-validation.txt` and `Temp/development-settings-*.png`.

## Prepare, validate and bake

1. Exit Play Mode, refresh scripts, and open **CanyonCrossing alone**. Save any existing edits yourself; the tool refuses setup/bake on an already-dirty scene and never closes other scenes.
2. Choose **Tools → Civil Craft → Performance → Canyon Occlusion → 1. Setup Saved Canyon Scene**. This creates the scene-local **Camera-Safe Occlusion** root, exact approved renderer references, two view volumes, and camera roles. Setup is undoable and does not save automatically.
3. Save CanyonCrossing, then choose **2. Validate Open Canyon Scene**. Resolve any reported invalid/missing/replaced candidates; the tool does not substitute similarly named objects.
4. With **Enable Gameplay Occlusion** unchecked, choose **3. Bake Saved Canyon Scene**. Keep Unity in Edit Mode and do not edit or save during the asynchronous bake. **Cancel Owned Bake** cancels only this tool's bake.
5. Check `Temp/CanyonOcclusionSetup.txt` for **BAKE COMPLETE**, then validate again. The tool waits up to 30 seconds after background generation for the imported asset/link. It verifies a fresh output write, no reported native failure, the linked asset's nonempty PVS and the scene/profile/asset link before saving the initially saved, unchanged CanyonCrossing scene. Unity's current-renderer `umbraDataSize` can remain zero until a view renders, so it is diagnostic only. Failure, cancellation or scene edits leave the result unverified and the master switch off.
6. Use **4. Render Fixed-Pose Comparisons** for temporary native on/off camera captures around the approved house and cliff, with a separate map camera rendering alongside. Inspect the PNG pairs and pixel/noise counts in `Temp/CanyonOcclusionRender/Report.txt`. The test preserves source cameras and scene state; it does not replace live gameplay checks or Android profiling.

The thirteen initial occluders are the twelve audited House/medieval renderers directly under `Environment/MAIN CITY`, plus `Environment/CanyonCliff_8(Clone)`. Their serialized object identities, opaque material slots, mesh geometry and movement/animation ancestry are checked. Only the Occluder/Occludee static bits change; doors/children and gameplay objects are not recursively marked static. Generated props keep their existing flags. City and cliff view volumes add 30 metres on every side; starting bake settings are **5 / 0.25 / 100**.

Before generating, any existing linked/local bake, its metadata, and the saved scene are copied to a timestamped `LocalBackups/Occlusion` folder outside `Assets`. Keep that folder until the new result is accepted. No old source bake is explicitly deleted. Review current scene changes before restoring any backup.

## Runtime behavior and integration

**CanyonCameraOcclusionPolicy** has `Gameplay` and `Excluded` roles. Only the profile's explicitly owned perspective player camera can enable native occlusion, and only with a verified bake, active profile, enabled master switch, Normal gameplay state, no transition/loading, no active suspension, and camera position inside a configured active view volume. Missing configuration/bake, leaving coverage, build/simulation, or removing/disabling the policy renders normally with occlusion off. It never changes camera activation, masks, distances, textures, or world-renderer visibility.

`Configure(profile, role)` assigns a scene camera. `RefreshPolicy()` recomputes its native switch. `SetSuspended(owner, true/false)` is owner-based and idempotent; two simultaneous owners must both release. The static `SetSuspended(camera, owner, suspended)` helper does nothing for a camera without its own policy. New systems moving the gameplay camera must suspend **before** movement and release on success, cancellation, disable and teardown. Never use global renderer disabling as a substitute.

Recording and cinematics already hold/release their own suspensions. Disabling a cinematic director stops its camera coroutines before releasing suspension. A destroyed owner is pruned automatically. Built-in and render-pipeline callbacks reevaluate immediately before each owned camera renders. Reload-disabled Play sessions clear stale owner state and avoid duplicate subscriptions. Replacement minimap and portrait cameras explicitly opt out of baked occlusion.

Changing static geometry, materials, approved references or view-volume coverage requires a fresh validation/bake. Do not hand-edit the hidden bake-verification fields to enable an old result.

## Acceptance and Android A/B check

- At identical positions behind city buildings and the cliff, toggle **Test Occlusion (Dev)** in Settings and compare screenshots after closing Settings. The session override takes precedence over the profile's Inspector switch. Visible scenery must not disappear; the map must remain complete. Test compact/expanded map pan and zoom, markers, water contact foam, ghost bridge references, build/simulation/magnifier and bridge captures.
- Test moving out of coverage, entering/leaving build mode, interrupted transitions, recording/cinematic start/cancel/finish, scene reload, missing bake data, and disabled domain/scene reload. Confirm ordinary camera settings and controls remain unchanged.
- Use the same Android device, quality settings, resolution, scenery-distance settings, route and camera poses for on/off runs. Warm up both runs, keep the minimap rendering, and record **CPU and GPU frame times, batches, triangles, and memory** in the Unity Profiler. Include city obstruction, cliff obstruction and open-water views. FPS alone is insufficient; reduced batches do not necessarily mean a faster frame.
- Authorize a separate production-rollout change only after the visual checks pass and the repeated device comparison shows a net frame-time improvement. The release guard intentionally prevents a saved Inspector test switch from enabling shipped occlusion. If CPU culling cost or memory outweighs the render savings, leave it off. Open water is expected to offer little obstruction-based benefit. This workflow does not publish an APK.

Standalone checks: `dotnet run --project Tests/CameraOcclusion/CameraOcclusion.csproj`. They exercise actual source policy against isolated boundary doubles and validate integration safety; they do not replace native Unity rendering or device profiling.

## Verification recorded 15 October 2026

- CanyonCrossing setup and bake saved: 13 approved opaque occluders, 35 camera policies (one Gameplay), two expanded coverage volumes. The linked asset contains 103,312 PVS bytes; its serialized file is 207,798 bytes. Developer master remains off.
- Runtime and Editor sources compiled against this project's Unity 2022.3 dependencies. Standalone regression checks passed 831 source-behavior assertions and 52 integration guards.
- Detached native checks passed coverage, missing-bake/default-off behavior, nested/destroyed suspension owners, component removal, camera-setting preservation, and repeated cinematic cancellation with captured camera-parent/pose restoration. No player/account saves were exercised.
- Five fixed-pose comparisons at 960×540 (two cliff sides, two house sides, and a zoomed map) showed 0–20 changed gameplay pixels out of 518,400, with no obvious missing scenery on visual review. Repeated-off noise was zero. Every corresponding map comparison was pixel-identical. Source cameras, scene roots, dirty state and saved scene file were preserved. The authored player start is outside these coverage volumes and was correctly skipped.
- These are sampled native rendering checks, not full gameplay acceptance. Live build/simulation/magnifier/capture, ghost bridges, map foam/markers and interactive pan/zoom still need a Play/device pass. Android CPU/GPU frame times, batches, triangles and memory were not measured because no device was connected. Do not enable the saved default until that acceptance work passes.

Latest local reports and PNG pairs are under `Temp/CanyonOcclusionSetup.txt`, `Temp/CanyonOcclusionValidation.txt` and `Temp/CanyonOcclusionRender/`. Recoverable pre-bake copies are under `LocalBackups/Occlusion/`; they are intentionally ignored by Git. The bake asset and its metadata must travel with the scene when committing this feature.

### Development-controls follow-up

- Runtime and Editor compilation passed. Updated standalone checks passed **846 source-behavior assertions and 54 structural guards**, including development overrides, forced-off release behavior, missing-bake/coverage guards and time-scale-independent frame sampling.
- Native detached Settings checks passed at **1280×720** and **1024×768**: five Graphics rows fit, new text does not overflow, repeated initialization creates no duplicate controls, and toggles do not invoke the Shadows callback. Source scene roots, dirtiness and diagnostic session state were preserved; no scene/player saves were performed.
- No Android device profiling or APK publishing was performed. The saved scene's Inspector switch may be on from user testing; the release-build guard still keeps published occlusion off.
