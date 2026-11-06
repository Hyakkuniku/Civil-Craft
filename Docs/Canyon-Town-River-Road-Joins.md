# Canyon Crossing and Town River road joins

This authoring pass covers 32 road joins at 15 sites: Bhan's Beam, Truss and Shopkeeper sites; Reed's main and side sites; Vance's main and side sites; both Silas main sites and both existing side-site locations; Marsh, Ford, Brooks and Ronan.

The Truss tutorial has four joins, including both edges of its central island. Silas's elevated cable/tower anchors and lower rock/pier anchors are not road endpoints. Ronan's raised opposite bank retains its elevation.

## Geometry

All bridge anchor coordinates remain fixed, including the older `Point 1` anchors. This preserves existing saved endpoints, tutorial ghost coordinates and authored starter bridges without a save migration.

The exact bank meshes are copied into `Assets/BridgeBuilder/Generated/RegionalBankJoins`. Their visible lips and existing mesh colliders use the same fitted geometry. The top meets the road collider within 0.5 mm, remains level immediately behind the join, then blends into the original terrain. The bank is slightly below the overlapping asphalt to prevent coplanar flicker. Only the local contact strip is reshaped; the bank transforms and original imported meshes remain unchanged. The extended central Truss island edge remains visible stone geometry.

Bhan's overlapping first-crossing rock is fitted too. Legacy `GeneratedSmoothApproaches` objects are removed from these sites; the repair adds no invisible ramps or endpoint colliders.

Shopkeeper's two small banks use a separate path that preserves all 463 original vertices, 276 faces and both cap/shell material sections. Their caps are levelled as complete surfaces, and their closed banks are extended/widened enough to cover the allowed road width. This avoids cutting or folding the coloured cap into the cliff sides. Their object transforms and original imported meshes stay unchanged.

The fitter measures triangle area in world space and explicitly normalizes small local-space normals. Large FBX scene scales otherwise turn valid triangle normals into zero vectors. Existing generated meshes are updated through Mesh APIs, including their live graphics buffers; serialized field copying alone could leave the Editor displaying stale triangles.

Where the local terrain correction reaches a parked vehicle, its root and route marker heights are adjusted to retain its authored ground clearance. Vehicle/route X/Z, rotations, wheel and cargo references, contract assignments and test order remain intact. Bridge dimensions, material values, progression and player save files are unchanged.

## Editor commands

With CanyonCrossing open in Edit Mode, use **Tools → Civil Craft → Build Sites → Canyon and Town River**:

- **Inspect Road Joins**: read-only scene, collider, anchor and wheel measurements.
- **Align Road Joins**: Undo-supported fitting of the exact audited sites. Missing or unexpected geometry stops the operation. Backups are stored outside Assets under `LocalBackups/RegionalJoins-*`. The scene is saved automatically only when it was clean before authoring; existing unsaved edits remain unsaved.
- **Validate Road Joins**: checks physical/visible surface coverage across lane samples and runs representative wheels in both directions in an isolated physics scene.
- **Render Road Join Previews**: creates detached before/after images for Reed, the Truss island and Ronan's raised bank under `Logs/RegionalRoadJoins`.

Generated meshes retain their source mesh GUID/local ID in asset labels. Repeated authoring starts from those original meshes rather than deforming an already fitted mesh again.

The detached wheel test preserves complete MeshCollider transform hierarchies, including nonuniform parent scales. It never simulates the loaded gameplay scene. Profile mode measures wheel clearance against the actual approach surface, rejecting gaps, stalls or wheel separation over 3 cm.

## Gameplay check

Drive each contract's assigned vehicle across a bridge that is level at its endpoints. Check entry and exit, then repeat after saving/reloading and entering redesign. Include both Ford trucks, both Brooks vehicles and all four Truss tutorial joins. Ronan's crossing should follow the raised road rather than flattening the whole bridge.

Detached wheel tests verify the local contact seam. A full vehicle's suspension and a flexible player-built bridge still need a gameplay test.

## Verification results

- All 32 joins passed visible/physical coverage checks across their lane samples, with less than 2 mm height error.
- All 136 detached wheel crossings passed in both directions at 3 m/s, using assigned wheel tracks and radii. The maximum additional vertical excursion was approximately 1.6 mm.
- The unchanged Industrial Zone checks passed again. Production anchor and saved-bridge compatibility checks passed (29 and 25 checks respectively).
- Repeated fitting from original source meshes passed, without moving any anchors or tutorial ghost endpoints.
- Detached before/after previews were rendered for Reed, the Truss island and Ronan's raised bank.
- Shopkeeper's complete banks were checked from front and back after loading the actual saved mesh assets. The original face indices and two-anchor identity are validated separately. Solid debug anchor cubes are suppressed when the real point mesh is visible; hidden-point Editor markers use wire cubes.
