# Mobile bridge drawing

Starting a drag targets each eligible point's whole projected mesh bounds, including its long depth in a slanted build-camera view. Destination snapping instead uses its depth-free footprint on the logical bridge plane. No point collider or scene change is required.

- Phones get up to a 12-reference-pixel margin when starting a piece, and a smaller 6-pixel margin around the destination's bridge-plane footprint. Margins scale with the screen's short edge, not world zoom.
- Direct mesh touches take priority over nearby targets. There is no snap stickiness: moving out of the smaller destination zone immediately releases that target.
- The normal bridge point's logical center remains the connection location. A side grab retains its screen offset so the preview starts at that center. The offset logical aim is the only destination target; the raw finger cannot independently pull it onto a distant part of another node's long mesh.
- For destination projection only, renderer-bounds corners retain their world X/Y but are flattened to the node's logical Z before camera projection. Thus visual depth cannot widen the snap zone at an oblique camera angle. Full-depth projection remains available for starting a drag.
- A small drag threshold prevents a stationary tap or finger jitter from placing a piece. Pier placement retains its existing vertical/foundation behavior.
- One finger owns the gesture. A second touch cancels the preview for camera gestures; cancellation, missing touch, focus loss, pause, transitions, and disable release drawing state.
- There are no drawing crosshairs in the main view or magnifier. The existing magnifier follows the endpoint and returns when moving away from an unfinished auto-draw confirmation. Disabling its controller also stops its camera.
- Preview and release share the existing tutorial, length, budget, terrain, node-join and bar-splitting resolver for normal pieces. Final placement, history and tutorial continuation still run on release.
- After length or terrain constraints, a fallback node join must be within 0.15 metres of the endpoint and within the smaller screen-space margin of its depth-free footprint. It no longer uses the 1.2-metre general selection radius.

Selection, Move and Delete keep their existing targeting radius. Broader drawing targets do not broaden destructive selection. Existing auto-draw still requires its confirmation hold and segment validation.

## Inspector controls

On BarCreator, **Touch Drawing** contains:

- **Touch Point Padding Pixels**: default/max 12 at a 720-pixel screen short edge, for starting a piece.
- **Touch Endpoint Padding Pixels**: default/max 6 at the same scale, outside the depth-free destination footprint. Both margins are capped at runtime so old larger serialized values cannot restore aggressive snapping.
- **Touch Draw Drag Pixels**: default 10 at the same scale.
- **Test Touch Drawing In Editor**: enabled by default in the Editor, allowing mouse testing of the phone assistance. Desktop player builds use the whole mesh with a small 2-pixel margin.

Existing point rendering, bridge depth, ownership and active-site restrictions remain. Hidden, inactive, disabled, camera-masked and fully clipped/offscreen point meshes are rejected. Screen footprints use conservative renderer bounds; they deliberately include small empty corners around round meshes for finger tolerance. The starting footprint includes visual depth; the destination footprint does not.

The picker reuses buffers and cached renderer lists rather than reading mesh triangles or adding physics bodies. Its Profiler marker is `Bridge.Draw.PickPoint`.

## Checks

Refresh scripts outside Play Mode, then use **Tools → Civil Craft → Build → Validate Touch Drawing** for detached camera/geometry/gesture checks. Output is under `Temp/mobile-bridge-drawing-validation.txt`. This does not save the open scene or player data.

The checker covers real camera projections, renderer visibility and point ownership, smaller destination margins, gesture offset/slop, wrong-pointer release, maximum length, budget, constrained-node fallback, bar-interior snapping, fractional-origin grid snapping, and crosshair-free magnifier click-through/disable. Live Android touch, two-finger gestures, final bridge commits and tutorial smoke tests remain manual.

Verified on 16 October 2026 with Unity 2022.3.62f3: runtime and Editor compilation passed, and all 176 native checks passed. These include 35/60-degree orthographic and perspective views, fourfold visual-depth invariance, whole-mesh starting versus compact destination targeting, raw-finger override rejection, and endpoint constraints. Open scene roots, active scene and dirty flags were preserved, with no player-save writes.

Manual Editor/phone pass:

1. Select a bridge material. Touch and drag from both ends/sides of an anchor with the camera slanted. Connections should begin at the logical node, with no jump from a stationary tap.
2. Slant the camera and drag across the far side of a destination's long mesh: the endpoint must not jump sideways to its logical center. Move the drawn endpoint near the actual connection on the bridge plane to connect. Starting from either end of the mesh must still work.
3. Zoom out and repeat. Moving away from a destination should release promptly, without a crosshair or a wide magnetic pull.
4. Draw toward length and budget limits, terrain, existing nodes and a bar's interior; release should agree with the preview.
5. Try dense nearby nodes, both bridge directions, and another active build site. Points from the wrong site or plane must not be acquired.
6. Start drawing, then use two fingers, pause, switch tools or leave build mode. No unfinished piece should be committed by cancellation.
7. Hold on a destination to invoke existing auto-draw, move away before confirmation, and check that the magnifier returns. Quick release should still place a normal single piece.
8. Test the Bhan beam/truss tutorials, pier placement, Undo/Redo, and multiplayer challenge editing restrictions. Check Select/Move/Delete separately.

No saved-scene migration is needed. A new APK is required to test these changes on Android.
