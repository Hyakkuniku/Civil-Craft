# NPC exclamation markers

On an NPC's **NPCProgressionManager → Phases**, enable **Show Exclamation Mark** on the desired phase. Save your Inspector/scene change normally.

The marker appears only after the NPC reaches that phase's destination. It stays hidden during walking, teleport dissolve, or interrupted travel. Restoring an arrived phase from a save also restores its marker. Idle roaming after arrival keeps the marker visible.

- Above the NPC: a 32-pixel, high-contrast gold exclamation badge projected above the character's head.
- On the minimap: a 22-pixel badge at the NPC's position.
- On the expanded map: a 28-pixel badge that stays readable during zoom and pan.

No icon assignment or new shader is required. The graphics never block NPC/bridge selection. Existing map unlock rules and story phases remain unchanged, and old phases default to unchecked.

The overhead badge hides during dialogue, Pause, fullscreen panels, and build/transition mode. The map badge appears only when the map is available and the NPC is inside its camera view. Visiting multiplayer guests do not gain host story markers from their own progression.

Talking does not automatically clear the checkbox. The marker follows the checked active phase; uncheck it or move to an unchecked phase to hide it.

Development checks: **Tools → Civil Craft → NPCs → Validate Exclamation Markers** creates detached previews in `Temp/npc-exclamation-*.png` and a report in `Temp/NPCExclamationValidation.txt`. It does not save source scenes or run player saves/accounts/network calls.
