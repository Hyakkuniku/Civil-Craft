# Material overlap confirmation

The existing authored `ConformationPanel` is reused in CanyonCrossing, Multiplayer and the building managers prefab. Bhan House has no ClipboardManager and its unused legacy panel is left untouched. No replacement modal is generated during gameplay. `MaterialOverlapPanelLayout` only fits its serialized controls to the screen/safe area.

## Player flow

- Copy/cut pieces, position the preview on existing connections, then press Paste.
- A different material on the same endpoints asks whether to replace it. Crossing beams alone do not count as replacement.
- **KEEP EXISTING** and Escape/Android Back cancel the confirmation and retain the movable preview. There is no X button. Nothing is charged or changed.
- **REPLACE** revalidates the placement, budget, tutorial permissions and current build session, then records the change as one Undo/Redo action. Duplicate or stale confirmation callbacks do nothing.
- **Don't ask again** is opt-in, only accepted on successful confirmation, and stored in memory—not player saves or preferences. The shorter label does not change its existing behavior.
- Preview dragging, camera input, build shortcuts and background controls are blocked while confirmation is pending. The coordinator restores the previous UI when it closes. No global time-scale changes are used.
- The active build location's blueprint/grid Canvas stays visible behind this confirmation. Previously the coordinator's normal unrelated-Canvas hiding disabled it. Only that explicitly referenced Canvas is preserved; other modals retain their existing visibility behavior. A deliberately disabled blueprint is never force-enabled.
- Replacement budgets use the final snapped endpoint positions and subtract the replaced construction's cost. Copy previews remain free and identical materials are not charged again.

## Authored UI and checks

The panel uses the existing Bekind Sans font and rounded cream/brown/gold styling. Its original root, Replace/Keep buttons, title and checkbox are preserved; their persistent callbacks point to the scene's own ClipboardManager.

Editor commands:

- **Tools → Civil Craft → Build → Author Material Overlap Confirmation** authors the existing controls. Initially dirty scenes are left unsaved and Undoable; do not use this to overwrite unrelated edits.
- **Tools → Civil Craft → Build → Validate Material Overlap Panel** runs detached placement/history/layout checks without touching player saves or contacting a backend.

Reports are in `Logs/MaterialOverlapPanelAuthoring.txt` and `Logs/material-overlap-panel-validation.txt`. Rendered layout previews are in `output/MaterialOverlapPanel/`.

Verified on 18 October 2026 in Unity 2022.3.62f3: runtime/Editor compilation passed, the authored panel passed 225 native checks, and the existing Clipboard Preview Budget regression passed. Landscape phone, wide-screen, tablet and portrait previews were rendered and visually inspected, including actual glyph positions and background click blocking. These checks used detached fixtures, not real player saves or a backend. Live phone/tutorial/multiplayer smoke tests remain manual.

19 October revision: removed the authored X control, shortened the label to **Don't ask again**, and preserved only the active build blueprint Canvas. All 196 revised native checks passed, including nested Settings restoration, originally disabled Canvas states, the remaining two buttons and full-screen click blocking. The revised layout previews were visually inspected. CanyonCrossing was saved by the user; Multiplayer and the managers prefab are updated as well.

Manual gameplay check: paste a different material onto an existing connection, try Keep/Back, then Replace and Undo/Redo. Repeat with the checkbox, a changed budget, a tutorial and multiplayer permissions. Confirm that camera movement and other build tools cannot act behind the open panel.
