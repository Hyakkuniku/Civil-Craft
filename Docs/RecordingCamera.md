# Cinematic recording controls (Editor and desktop builds)

The recording shortcuts install automatically; no scene or prefab assignment is needed. Use them during normal player gameplay, not while building, loading, paused, typing, or in a tutorial/modal panel.

- **Ctrl+F**: detach into free camera. From a locked shot it resumes camera positioning; while already flying it returns to the player camera.
- **WASD**: fly forward/back and sideways. **Q/E**: lower/raise. Hold **RMB** to look; hold **Shift** to fly faster. Mouse wheel changes flying speed.
- **Ctrl+L**: lock the camera's current position and angle, then move the player normally with its existing controls. Press Ctrl+L again to return to the player camera.
- **Ctrl+H**: the existing Hide HUD shortcut remains independent of the camera controls.

Free movement eases smoothly into/out of motion. Locking stops camera drift immediately. Gravity still applies to the stationary player while positioning the camera. Normal input-map, camera-look and component-enabled settings are not rewritten, so pause and other panels retain their existing control locks.

The camera is local to each game instance in multiplayer: recording does not move another player's camera or change network ownership. Build-mode entry and authored cinematics first return to the normal player camera; scene changes and camera/player destruction release the recording state. Losing application focus releases mouse capture and stops free-camera movement.
