# Industrial Zone road joins

The two Clark sites and three Mills sites in CanyonCrossing now align their permanent road anchors with the existing bank surfaces. Road collider tops are 0.025 m above the anchor centres.

The correction keeps anchor X/Z, contract spans, vehicle routes, materials and story targets. The cliff meshes overlap the endpoint road by at least 0.15 m across the travelled lanes. There are no added invisible endpoint ramps or anchor colliders. Each site's existing Abutment Aligner now references its two real bank colliders.

## Spawned-road runtime correction

The first Edit Mode checks did not exercise `Point.Update` during play. Its legacy grid snapping could round permanent anchors from approximately 4.684 m to 5 m when their transforms became dirty. This lifted a newly drawn road above a correctly measured bank. Permanent scene and multiplayer-session anchors now retain their authored coordinates in Play Mode; ordinary editor node placement keeps its existing snapping.

The concrete road's actual asphalt surface was also 0.032623 m below its +0.025 m physical surface. Its mesh child is offset upward to match the collider, preserving the raised curbs. Wood Road's artwork is unchanged.

- Real Play Mode startup confirmed all ten Industrial Zone road anchors retained their full X/Y/Z coordinates.
- 29 production-update checks cover permanent/session anchors, dirty transforms, width refresh and ordinary editor snapping.
- Actual spawned concrete meshes were ray-tested at standard and 16 m widths. The asphalt surface now measures +0.025000 m at the sampled start, middle and end lane positions.
- 136 detached pier/deck mesh cases passed after the visual adjustment.

## Verification

- All ten joins passed height/coverage checks at the actual wheel tracks and lane samples, with less than 2 mm surface-height error.
- 64 detached wheel runs passed at 3 m/s in both directions using the assigned SUV, truck and crane wheel radii. Maximum extra vertical movement against a flat baseline was approximately 1.4 mm.
- The isolated Editor preview physics scene copies actual static colliders. It never simulates the loaded gameplay scene or writes player progress.
- Saved-anchor compatibility: 25 production-method checks passed. The 30 existing bridge-photo checks also passed.
- The saved scene and earlier Clark/Mills progression bindings passed validation.

These checks verify contact continuity at a flat endpoint. They do not replace driving a complete vehicle over a flexible, player-built bridge.

## Existing saved bridges

Only these five sites opt into 0.25 m vertical compatibility for permanent LeftAnchor/RightAnchor endpoints whose X/Z remain unchanged. Other saved nodes retain their positions. Ambiguous or non-finite coordinates are rejected; other sites retain the normal 0.1 m rule. Reconstruction and saved-photo verification share the same policy. Player save files are not rewritten by the correction.

Retest existing Industrial Zone designs: their first and last members can acquire a slight slope when reconnecting to the corrected anchors.

Pieces already created with whole-number coordinates by the runtime snapping bug are not silently rewritten. Recreate any previously raised road pieces when testing the corrected placement.

## Repeatable Editor commands

With CanyonCrossing open in Edit Mode, use **Tools → Civil Craft → Build Sites → Industrial Zone**:

- **Inspect Road Joins** — read-only collider, anchor, edge and height report.
- **Align Road Joins** — bounded, Undo-supported correction for the five exact sites. Stops on unexpected or uneven geometry. Saves CanyonCrossing only if it was clean before the operation; otherwise leaves existing edits unsaved.
- **Validate Road Joins** — surface coverage, detached wheel crossings and preserved story/site bindings.

Reports are written to `Logs/industrial-anchor-road.txt`. Alignment backups are outside Assets under `LocalBackups/IndustrialJoins-*`.

## Gameplay check

1. Test both Clark freight directions, the Offset Road SUV, Mills's crane, the worker vehicle and final vehicle.
2. Observe entry and exit at normal speed, with the bridge initially level at both ends.
3. Repeat after saving/reloading a bridge and entering redesign mode.
4. Check vehicle chassis clearance and suspension on sloped or heavily deformed designs. Those responses depend on the design and are outside the flat-seam check.
