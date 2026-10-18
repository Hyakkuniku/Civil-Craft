# NPC quiz camera presentation

The quiz can borrow CanyonCrossing's ordinary local gameplay camera to show its requesting NPC on the left, with the paper question panel on the right. It does not activate a second camera, change the minimap, move either character, or change render layers. Only that camera omits the local avatar while drawing the quiz shot.

Framing uses the upper 84% of the NPC's visible bounds for a slightly closer view. It preserves the exact top of the head and the authored shoulder/hand width rather than zooming tightly onto the face. Perspective fitting still checks all eight corners of that focused region, including near-corner depth, so the head stays clear of the question card and screen edges.

## Ownership and fallback

- `QuizNPCPresentation` lives beside `QuizUIManager`, outside the panel hidden by Pause.
- `Begin(owner, npcScreenViewport)` resolves only the owner's `NPCProgressionManager` ancestor. A hidden/unspawned/travelling NPC, missing local player camera, active cinematic, orthographic camera, obstructed close-up, or unusable viewport returns `false`. The quiz then uses its ordinary full-width UI; answering, saving and completion rules do not change.
- `npcScreenViewport` is the normalized screen rectangle reserved by the responsive quiz layout. `SetViewport` updates composition after a screen/safe-area change. The camera's own viewport remains unchanged.
- Only `GameManager.MainCamera` attached to the local enabled `InputManager` / `PlayerLook` pairing can be borrowed. Recording is returned to normal before the camera snapshot is captured.
- The existing orbit component is disabled for this shot and its previous enabled state is captured. The quiz manager continues to own movement/look/interaction permission and restores that separately.
- Baked occlusion is suspended with this presenter as its owner and released on every exit path. No global occlusion switch or map-camera setting is changed.
- End the presentation before restoring gameplay input or invoking an NPC continuation callback. `End` is immediate and idempotent, even if called repeatedly by cancellation, disabling, destruction, or scene unload.
- A started build transition/cinematic, camera replacement/reparenting or NPC phase change cancels this presentation. It does not overwrite a new controller's camera position or projection values. On takeover it removes only projection overrides that still match its own last write. A normal quiz close restores the captured local/world pose, FOV, near clip and orbit-component state.
- The subsystem reset releases a surviving presenter when Unity's domain or scene reload options are disabled.

## Local avatar obstruction

`QuizLocalAvatarVisibility` caches the local player's body/outfit mesh renderers, including inactive cosmetic variants. It resolves the model through that player's `PlayerMotor.playerAnimator` (with legacy-name fallbacks), never searches for other players, and excludes held cargo, particles and world UI.

Only while the borrowed camera renders, each cached renderer's `forceRenderingOff` is temporarily set. Its exact previous value is restored afterward. Nested map/portrait camera renders temporarily restore the avatar, then resume the outer quiz scope. Both scriptable and built-in camera hooks are supported without double-entering both pipelines. Frame recovery, partial teardown, cancellation, disable, scene unload and domain-reset cleanup restore flags and remove callbacks.

No position, collider, object activity, renderer enabled state, material, layer, shadow setting, multiplayer state or player-save value is changed. Other cameras keep their original view of the player. A missing/unknown local visual model safely uses the full-screen quiz instead.

## Animation

`PresentQuestion()` starts a short burst of the NPC's existing `isTalking` animation (three seconds by default). Results and Pause call `SetSpeaking(false)`; the camera stays framed. If a bool/controller is missing, that parameter is skipped without creating a new animation asset. Existing `isWalking` and `isRunning` bools are held off during the presentation. All captured bools are restored before NPC continuation.

`NPCProgressionManager.IsIdleRoamingBlocked` includes `QuizNPCPresentation.IsPresenting(transform)`. This keeps waypoint/NavMesh idle roaming stopped even after a question's short talking gesture finishes. It does not stop a newly started phase journey.

## Native checks

1. In a fresh Play session, reach Bhan's quiz and choose **Start Quiz** in the Almanac. Bhan should be on the left, not the player or a different NPC. The camera should ease into the shot without switching the minimap/build cameras.
2. Move between questions: observe a short talking gesture followed by idle. Multiple choice counts, scrolling and score submission must retain their existing behavior.
3. Open Pause → Settings → Resume. The shot should remain unchanged; the NPC must not resume walking while the quiz is active.
4. Finish/leave where supported, cancel from NPC teardown, and unload the scene. The normal gameplay camera and previously captured control state must return before follow-up dialogue or travel starts.
5. Test an NPC near a wall, a hidden NPC, a standalone non-NPC owner, no local player, and an orthographic camera. These should keep a usable fullscreen quiz instead of a broken NPC shot.
6. Compare landscape phone, tablet, wide desktop, and portrait. The head/body must stay inside the speaker region, and UI must not cover the face. Rotate the device during a question.

An Android device is still needed to verify final touch, Back behavior and scene-specific framing in the real tutorial. Detached maths/Editor checks cannot establish every authored camera obstruction or Animator transition.

## Verified in this revision

- Runtime and Editor scripts compile (the existing unused Shop placeholder warning remains unrelated).
- Detached native Editor checks passed phone, tablet, desktop and portrait in both NPC and fullscreen-fallback modes: readable title/progress, independent long-content scrolling, text/image/mixed choices, 48-pixel touch targets, assessment Previous exactly once, full-screen click blockade, and perspective NPC bounds containment.
- The preview copied Bhan's meshes only; no live NPC scripts, story callbacks, player data or source-scene roots/dirty states changed. The static Editor mesh pose is not an animation playback test.
- Isolated production-source checks passed 9,562 data/save assertions, 1,655 quiz/Pause/session assertions, 2,119 framing assertions, 26 camera safety guards, 75 local-avatar visibility assertions, and the existing NPC sequence/Almanac handoff checks.
- Actual detached camera renders verified that the owned quiz view omits a foreground avatar while the NPC remains visible; a secondary camera retains the avatar before/after, and cancellation restores the owned view. Original force-off and enabled flags, repeated teardown and mid-render teardown were preserved. The inactive-cargo lookup refinement is also covered by the independent visibility tests.
- A fresh Play session and Android test should still confirm Bhan's talking gestures and the camera path at the actual quiz location. No APK was built or published.
