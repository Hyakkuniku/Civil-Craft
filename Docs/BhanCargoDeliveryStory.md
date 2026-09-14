# Bhan to Grandma Susan — Cargo Delivery Story

## Story hook

Professor Bhan is transporting one crate of essential tools and replacement parts to Grandma Susan's shop. A canyon collapse destroyed the delivery route and left the shop disconnected from Canyonreach.

The player helps Bhan move the same persistent crate along the entire journey. Short, contract-free handoffs move it between construction sites, while each completed bridge is tested by carrying it to the next checkpoint.

The crate gives every tutorial and contract a visible purpose: each lesson helps deliver something that a character genuinely needs.

## Complete story sequence

### 1. Meeting Professor Bhan

**Professor Bhan**

> There you are, {PlayerName}. I need an engineer—and Grandma Susan needs this delivery.

> Her shop is running out of tools and repair supplies. Unfortunately, the old canyon road collapsed before I could bring this crate through.

> We'll rebuild the route one crossing at a time.

> Come with me. The delivery is waiting.

### 2. First cargo handoff — no contract

The player follows Bhan to Grandma Susan's crate. This phase retains the stable phase ID `LiveLoad`, but it no longer invokes Bhan's vehicle-inspection lesson.

**Professor Bhan**

> This is Grandma Susan's supply crate. It's the same cargo we'll carry throughout the journey.

> Before we start building, take it to the preparation area beside the first crossing.

> You can't leave important cargo lying anywhere. Carry it to the marked drop-off point.

The player picks up the crate and places it at a nearby contract-free Story Cargo Drop Location. Placing it invokes the location's `On Cargo Placed` event and advances Bhan to the first workbench phase.

**Professor Bhan**

> Good. The crate is ready—but the road ahead is gone.

> Now we need your first bridge.

### 3. First contract — beam bridge

**Contract:** `TUT_CONTRACT1`

**Professor Bhan**

> This first span is short enough for a basic beam bridge.

> Build a continuous road between the banks and support it properly.

> When you're ready, press Simulate and carry the crate across yourself.

> A bridge isn't successful because it looks complete. It succeeds when its cargo reaches the other side safely.

The player builds the bridge, starts simulation, carries the crate across, and uses **Place Cargo** at the contract's drop location.

### 4. First destination turn-in

After the player selects **Save & Continue**, the bridge and delivery position are saved. Bhan is repositioned beside the checkpoint during the transition, while the completion receipt hides the change.

The player turns in the contract to Bhan at the destination rather than walking back.

**Professor Bhan**

> The structure held, and Grandma Susan's supplies are safe.

> That is a successful bridge—not merely one that looks finished.

> Excellent work, {PlayerName}. Claim your reward; you've earned it.

The player manually collects the Gold and XP. Bhan activates his next phase in place without walking over the bridge.

### 5. Load lesson

**Professor Bhan**

> Before we continue, notice what your bridge was carrying.

> The road and supports create dead load. You and the crate added live load while crossing.

> A safe structure must carry both.

The dedicated vehicle-inspection lesson remains unassigned to Bhan and can be introduced later by another NPC.

### 6. Moving to the second site — no contract

A short ground route leads toward the second construction area.

**Professor Bhan**

> The next crossing is wider.

> Pick up the crate and bring it to the next preparation point. I'll meet you there.

The player carries the same crate to the next active Story Cargo Drop Location. The route should be short and forward-moving; it must not create unnecessary backtracking. Placing the crate advances Bhan to the second contract phase.

### 7. Second contract — truss bridge

**Contract:** `TUT_CONTRACT2`

**Professor Bhan**

> A road alone won't be enough for this span. We'll reinforce it with a truss.

> Triangles direct forces through the structure and into the supports.

> Some members will be pulled by tension. Others will be pushed by compression.

> Build carefully, then carry Grandma Susan's crate to the Canyonreach checkpoint.

The player builds and tests the truss bridge, then places the cargo at its contract-specific destination.

### 8. Second destination turn-in

Bhan is repositioned to the Canyonreach checkpoint during the completion transition. The player turns in the contract and collects the reward there.

**Professor Bhan**

> Excellent. The truss distributed the load exactly as planned.

> More importantly, the delivery has reached Canyonreach.

> Grandma Susan's shop is close now. We're one crossing away.

### 9. Through Canyonreach — no contract

The player carries the crate through a short section of Canyonreach to the final preparation area.

**Professor Bhan**

> There—the shop on the other side belongs to Grandma Susan.

> We brought her supplies this far, but the final approach is still broken.

> Take the crate to the final staging point. Then we'll finish what we started.

Placing the cargo at the final Story Cargo Drop Location advances Bhan to the Shopkeeper contract phase.

### 10. Final contract — shop connection

**Contract:** `ShopKeeper`

**Professor Bhan**

> This is no longer a practice crossing.

> Someone is waiting for the cargo in your hands.

> Build the final connection, carry the crate across, and place it at Grandma Susan's delivery point.

> Ready to finish the journey, Engineer?

The player completes the bridge and places the crate at Grandma Susan's contract drop location.

### 11. Final destination turn-in

Bhan appears near the final delivery point during the completion transition. The player retains the normal contract turn-in conversation and manually collects the final reward.

**Professor Bhan**

> Three crossings. One continuous delivery.

> Every bridge carried the same crate closer to someone who needed it.

> You didn't simply complete three contracts, {PlayerName}. You restored an entire route.

> Go speak with Grandma Susan. She has been waiting to meet you.

### 12. Grandma Susan

Grandma Susan remains unavailable until the final `ShopKeeper` cargo placement has been saved. Earlier checkpoints and bridge completion alone must not unlock her interaction.

**Grandma Susan**

> That crate! I was beginning to think the canyon had swallowed it for good.

> Professor Bhan told me you rebuilt the whole route and carried these supplies across yourself.

> You did more than deliver a box, dear.

> You connected my shop to Canyonreach again.

She looks toward the restored route.

> People can reach the shop again, and I can finally send supplies back into town.

> Every bridge on that route tells the same story: you saw a problem, built a solution, and tested it with something that mattered.

> Thank you, Engineer.

### 13. Shop unlocked

**Grandma Susan**

> A delivery like that deserves more than a thank-you.

> My shop is open to you now.

> Spend your earnings on useful supplies—or something that makes you look a little less covered in canyon dust.

> Come back whenever the next job leaves dust on your boots.

The shop feature is unlocked, completing the delivery chapter.

## Cargo rules

### Contract-free story handoffs

- The same scene `CargoItem` is marked as Story Cargo.
- Story Cargo can be picked up during normal gameplay without an active contract.
- It cannot be dropped freely on the ground or loaded into a vehicle.
- It can only be placed at an active Story Cargo Drop Location assigned to that exact cargo.
- Story placement saves the crate's persistent location and invokes `On Cargo Placed`.
- Story placement does not complete a contract, award rewards, bake a bridge, or start simulation.
- After placement, the crate remains locked until another valid story destination becomes active or its next cargo-contract simulation begins.

### Contract bridge tests

- All three delivery contracts use `PlayerCarriedCargo`.
- Simulate exits the Build Mode presentation and restores the complete gameplay environment.
- The stress visualizer remains active while the player carries the crate.
- Only the active contract's exact Cargo Drop Location can complete the test.
- Placing the crate completes the physical test but does not automatically claim the contract.
- **Save & Continue** bakes and saves the bridge.
- Bhan is repositioned to the cargo destination during the transition.
- The player turns in the contract to Bhan at the destination and manually claims Gold and XP.
- After turn-in, Bhan activates the next phase in place without visibly crossing the bridge.

### Failure and cancellation

- No bridge, delivery, reward, or phase advancement is saved.
- Bhan remains at the previous checkpoint.
- The player and crate return to their pre-test positions.
- The bridge returns to editing mode.

## Trigger configuration

The automatic Bhan story setup:

- keeps all existing stable NPC phase IDs and movement destinations;
- marks the shared Bhan crate as Story Cargo;
- links it to all three contract phases and Build Locations;
- assigns contract-specific Cargo Drop Locations to the existing finish zones;
- gives cargo and drop locations persistent save IDs;
- prevents Grandma Susan interaction until the final `ShopKeeper` delivery record exists;
- disconnects the old `TUT_CONTRACT1` binding from the scene `LiveLoadVehicle`.

Each contract-free handoff additionally needs a Story Cargo Drop Location:

1. Select the shared story crate in the Hierarchy.
2. Choose `GameObject > Civil Craft > Story Cargo Drop Location`.
3. Move the created trigger and its `Cargo Drop Socket` to the intended destination.
4. Keep `Assigned Contract` empty.
5. Confirm `Allow Story Delivery Without Contract` is enabled and `Accepted Story Cargo` references the shared crate.
6. Connect `On Cargo Placed` to the appropriate stable NPC phase call or other story event.
7. Activate only the destination that belongs to the current story step.
8. Save the scene.

## Editor application

Open Canyon Crossing in Edit Mode. The base setup applies automatically after scripts reload. It can also be run manually from:

`Tools > Civil Craft > Story > Apply Bhan Cargo Delivery Setup`

Save Canyon Crossing after the Console reports:

`[Bhan Delivery Story] Applied the cargo-delivery dialogue and trigger setup.`

Existing profiles already beyond these phases will remain beyond them. Test the complete revised story with a fresh profile or reset the Bhan and Shopkeeper progression records.
