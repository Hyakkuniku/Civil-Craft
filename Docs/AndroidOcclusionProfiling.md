# Android occlusion performance test

Use this guide to compare CanyonCrossing with occlusion culling OFF and ON on the same phone. The purpose is to find out whether culling saves more rendering work than it costs—not to assume that ON is faster.

## 1. Prepare the phone and game

1. Enable **Developer options → USB debugging** on your Android phone. The exact location varies by phone.
2. Connect it to the PC using a USB data cable.
3. Unlock the phone and accept **Allow USB debugging** for your own PC.
4. Open the installed development version of Civil Craft.

If you already installed our Development Build, try connecting it before rebuilding.

If a new APK is needed, open Unity **File → Build Settings → Android**:

- Keep the existing Scenes In Build list unchanged.
- Enable **Development Build**.
- Leave **Deep Profiling Support** and **Script Debugging** off for this comparison.
- Leave **Export Project** and **Build App Bundle (Google Play)** off to build an APK.
- For automatic connection, enable **Autoconnect Profiler** before building. It is not required for a manual connection to an existing development player.
- Build and install the APK, or select the connected phone and use **Build And Run**.

Do not publish this test APK as the public installer. Do not uninstall an existing game just to resolve an installation error: uninstalling can remove local saves. Record the error first.

## 2. Connect the Unity Profiler

1. Keep Unity out of Editor Play Mode; the game should run on the phone.
2. Open **Window → Analysis → Profiler**.
3. Open the target dropdown at the top, which may say **Playmode** or **Editor**.
4. Choose the Android player/device. Depending on the connection, it may appear as **AndroidPlayer** or **AndroidProfiler (ADB…)**.
5. Confirm that the graphs update while the phone runs the game.

If the phone does not appear:

- Check the USB debugging permission prompt and try a known data-capable cable.
- Confirm the installed APK was built with Development Build enabled and is running.
- Reopen the target dropdown after reconnecting the phone.
- If using Wi-Fi, put the PC and phone on the same local network. Do not disable the firewall entirely to troubleshoot a connection.
- If still missing, capture the target dropdown and any Unity error before changing project settings.

Unity documents target-device and Android connections in its [profiling guide](https://docs.unity3d.com/2022.3/Manual/profiler-profiling-applications.html).

## 3. Prepare a fair comparison

Enable these Profiler modules through the modules dropdown:

- **CPU Usage**
- **Rendering**
- **Memory** (ordinary counters; no memory snapshot is needed)
- **GPU Usage**, if supported by the phone and graphics API

Keep **Deep Profile off**. Profiling adds overhead, so use the same connected configuration for every capture.

On the phone:

1. Enter CanyonCrossing and let loading and initial activity settle.
2. Stand near the covered city buildings or cliff, with scenery behind the obstruction.
3. Open **Settings → Graphics** and enable **Show FPS Counter (Dev)**.
4. Keep the same quality, frame-rate cap, camera direction, minimap state and phone power mode throughout the comparison.
5. Avoid other apps, downloads and screen recording during captures. If the phone becomes hot, allow it to cool before repeating the full set.

The FPS counter's milliseconds are average frame intervals, not separate CPU or GPU timings. A frame-rate cap can hide improvements; record which cap you use and do not change it between OFF and ON.

## 4. Capture OFF → ON → OFF

Use the same stationary camera view for all three recordings. Do not include the Settings panel or the act of switching modes in the measured segment.

### A. First OFF capture

1. Set **Test Occlusion (Dev)** to OFF.
2. Close Settings and wait a few seconds for the view to settle.
3. In the Profiler, clear the previous samples and turn recording on.
4. Record around 10 seconds, then stop recording.
5. Use the Profiler's Save control to save `City-OFF-1.data`.

### B. ON capture

1. Set **Test Occlusion (Dev)** to ON, then close Settings.
2. Confirm the counter says **Occlusion: ACTIVE**.
3. Wait a few seconds, clear the previous samples, and record the same view for around 10 seconds.
4. Stop and save `City-ON.data`.

### C. Second OFF capture

1. Turn the toggle OFF and close Settings again.
2. Repeat the same recording and save `City-OFF-2.data`.
3. Compare the two OFF captures. A large difference suggests changing conditions, such as heat or background activity, so repeat the set before drawing conclusions.

No game restart is needed between these modes. The toggle applies immediately and its choice is session-only.

**Recording buffer:** the Profiler retains a limited number of frames. If 10 seconds exceeds the configured buffer, only the latest frames may remain. Use equal retained sample lengths or increase the Profiler frame-history setting before all runs.

Repeat the set at the cliff using `Cliff-…` filenames. An open-water capture is useful as a baseline, but it is not an active-culling comparison if that camera position is outside coverage.

## 5. Understand the occlusion status

| Counter status | Meaning |
| --- | --- |
| ACTIVE | The gameplay camera is currently using occlusion. |
| OFF | Occlusion is not requested. |
| Outside baked area | Move into the configured city/cliff coverage to test active culling. |
| Suspended | A gameplay/camera condition prevents activation; return to ordinary gameplay and wait for transitions to finish. |
| Bake unavailable | The build lacks a verified, linked bake; resolve that before comparing. |
| CanyonCrossing only | No registered CanyonCrossing profile is available in this scene. |

The minimap is intentionally excluded from baked occlusion. It continues rendering its own view in both runs.

## 6. What to inspect

In **CPU Usage → Timeline**, inspect several representative frames, including the main thread and render thread. In **Rendering**, compare batches, draw calls and triangles where available. Use Memory counters to note memory usage without taking expensive snapshots during the timed run.

- More CPU work with little reduction in rendering supports the possibility that occlusion costs more than it saves.
- Fewer batches or triangles alone do not prove an improvement; compare frame timing as well.
- CPU timeline duration can include waits for the GPU, presentation or a frame-rate limit. Do not interpret every long CPU frame as expensive scripts.
- GPU Usage may be unsupported on your Android device/API. Missing GPU samples do not mean zero GPU cost. Do not change graphics APIs merely to make this panel work during the same comparison.

See [Unity's GPU Profiler documentation](https://docs.unity3d.com/2022.3/Manual/ProfilerGPU.html) for platform limitations. Unity also explains that [occlusion queries add CPU work](https://docs.unity3d.com/2022.3/Manual/OcclusionCulling.html), which can offset rendering savings.

## 7. Share the results

Provide the three `.data` files for each tested view. If those cannot be shared, provide screenshots showing **CPU Usage → Timeline** and **Rendering**, with the target device and relevant frame selected.

Include:

- Phone model and Android version.
- APK/build version and graphics API, if known.
- Quality preset and frame-rate cap.
- Location and camera direction, preferably a gameplay screenshot.
- Whether ON showed **ACTIVE**.
- Approximate FPS/frame interval for each run and whether the phone was warm.

## Decision

If OFF is consistently faster under comparable conditions, leave occlusion off. Normal release builds currently force it off regardless of the saved Inspector test switch. A separate production-rollout change should happen only after repeatable device profiling and visual checks demonstrate a benefit.

These development captures diagnose the cost; they do not establish final release-build performance. Do not change player saves, map-camera behavior, bake settings, or public APK downloads as part of this measurement.
