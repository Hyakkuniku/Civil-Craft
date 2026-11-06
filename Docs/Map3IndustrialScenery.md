# Map 3 industrial scenery

The six existing raised platforms are dressed with 97 supplied Map 3 models. They are authored under **Map 3 - Industrial District Scenery**, grouped left to right:

| Group | Theme | Models |
| --- | --- | ---: |
| 01 | Freight and dispatch: warehouses, processing plant, tank service area, container court and crane | 22 |
| 02 | Processing works: utility buildings, storage tanks, pipes and switchgear | 13 |
| 03 | Equipment yard: construction workshop, site cabin, bulldozer and material stock | 11 |
| 04 | Workers district: homes, dormitory, community centre, clinic, store and bus stop | 14 |
| 05 | Arcadia logistics terminal: processing works, warehouses, operations office, crane, silos and control tower | 27 |
| 06 | Receiving depot: warehouse, workshop, office, canteen and delivery storage | 10 |

Each imported model uses one proportional XYZ scale multiplier. Repeated assets keep the same physical size throughout the district. The shipping containers are 12 m long and retain their original 2.6 m height; oversized spools and the dormitory are normalized using measured native renderer bounds. Original imported meshes and shared materials are unchanged.

## Protected space

The authoring tool derives road reservations from the loaded `CanyonDirtPaths` street endpoints, surface bounds and width settings. It reserves asphalt, sidewalks and a further 2 m setback. Additional reservations cover the five simulation approaches, parking zones, NPC meetings, workbenches and their access paths.

All model footprints are supported by the actual platform colliders. Model bases sit 8 mm above the platform, avoiding coplanar flicker. Placement validation also rejects overlapping scenery and penetration into existing solid geometry. The control tower is in the terminal's northwest service area to clear the existing background cliffs.

Original platform dimensions, roads, banks, anchors, vehicle routes, NPC targets, progression, contracts, terrain and navigation data are retained. The tool verifies existing transform matrices and serialized road settings before completing an apply. It creates no runtime scripts or per-frame scene searches. Architecture uses simple authored box colliders; small decorative props remain visual only. Renderer objects use batching flags without adding occlusion flags or changing the existing culling policy.

## Editable assets and repeatable commands

- The open scene contains the actual grouped scenery objects.
- `Assets/Elements/Map3/Prefab/IndustrialDistrictScenery.prefab` is a recoverable, editable copy of the complete layout, retaining nested links to the supplied FBX models. Avoid placing a second copy over the existing scene group.
- `Docs/Map3IndustrialScenery.layout.json` stores the measured positions, rotations, physical sizes and protected areas. The tool reads a temporary override from `Temp/map3-industrial-dressing.json` when present, otherwise this saved layout.
- **Tools → Civil Craft → Map 3 → Industrial Scenery** provides Inspect, Apply Layout and Validate/Preview commands. Apply replaces only its own generated scenery group and supports Undo. It leaves pre-existing unsaved scene edits unsaved.
- Inspection, placement and validation reports and overhead/angled previews are in `Logs/Map3Dressing`. The original saved-scene backup is outside the repository at `D:/THESIS/unity/LocalBackups/CivilCraft-Map3Dressing-20261107-061911/CanyonCrossing.before.unity`.

## Verification

Native Unity inspection measured all source models, platform tops and loaded road reservations. All 97 placements passed native road, NPC, vehicle, support, proportional-scale, footprint and existing-solid clearance checks. The saved prefab was reopened and passed the same checks, confirming that model positions and scales survive serialization. The six districts were visually reviewed from overhead and angled cameras. This was an Edit Mode scene-authoring check; it does not claim an Android frame-time measurement or a complete gameplay drive-through.
