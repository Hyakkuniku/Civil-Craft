# Closed gameplay overlays at startup

The saved CanyonCrossing and Bhan House scenes contained open result panels, Pause menus and tutorial placeholders. When both scenes were loaded in Play Mode, a duplicate singleton manager could return before closing its separate UI hierarchy. Runtime managers now close their own linked overlays before rejecting duplicates.

The explicit Editor repair is under **Tools → Civil Craft → UI → Startup**:

1. **Close Authored Gameplay Overlays** hides only manager-linked inner completion/failure/Pause panels and Tutorial Manager's Center, Left, Continue, Skip, and pointer controls. Their parent Canvases and manager objects remain active. The normal HUD Pause opener is not removed or permanently hidden.
2. **Validate Closed Gameplay Overlays** checks those references and closed states without changing them.

Both exact target scenes are preflighted before anything changes; missing, cross-scene, duplicate, or Canvas-instead-of-panel references stop the operation. Existing scenes—including dirty scenes—are never auto-saved or closed. Changes are Undoable, and the report lists scenes that need a user save. A scene opened only for the operation is saved only for the operation's changes, then closed; an unexpectedly dirty temporary scene is left for review.

For native automation, place `apply` or `validate` in `Temp/gameplay-startup-ui-v1.request`. The idle Editor processes it and writes `Logs/gameplay-startup-ui-v1.txt`. Play/Prefab Mode, imports, compilation, and builds defer the request.

Validation checks that parent activation/Canvas state, manager references, tutorial sequences, and serialized button triggers remain unchanged. It does not trigger tutorials/results or edit saves. Test normal gameplay with **CanyonCrossing alone**; Bhan House should be entered through the game's scene flow.

Pause startup additionally hides only its own scene's HUD opener before singleton selection; the accepted manager's normal Start/availability logic restores it. Duplicate managers cannot leave an orphaned opener or select another scene's AccessButtons container. Bhan House's legacy opener is named `settings_btn` but is correctly identified by its persistent `TogglePause` callback, not its name. Startup hiding does not call Resume, alter time scale, release input locks, play audio, or open/close coordinator frames. The Pause theme authorer also saves the real menu closed while rendering its isolated active preview clone.
