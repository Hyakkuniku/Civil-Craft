# Player cargo drop location

For PlayerCarriedCargo contracts, the box cannot be dropped freely or swapped for another held item. The NPC giver/phase assigns Player Cargo Contract on the linked CargoItem automatically; set that field manually if the contract is not given through an NPC.

1. Keep the contract mode PlayerCarriedCargo and win condition FinishLine. The completion method now requires placement, rather than simply entering a vehicle finish trigger.
2. On the destination bank create an empty GameObject and add CargoDropLocation. Assign the same Contract asset used by the NPC and workbench.
3. Keep its layer Interactable and BoxCollider Is Trigger enabled. Size the zone so the player's feet can enter it.
4. Create a child empty named Drop Socket and assign it to Drop Socket. Position and rotate it where the box's pivot should sit. This lets the interaction zone and physical box placement use different positions.
5. Keep the workbench's Test Cargo assigned (or let the NPC's Linked Cargo fill it). Save the scene.

Press Simulate, pick up the assigned box, enter the destination zone and tap Place Cargo. Only the active contract's exact test cargo is accepted. Placing it finishes the test through the normal completion panel. Walking into an old FinishLineTrigger alone no longer completes a player-cargo test. Vehicle contracts still use their original finish triggers.

The optional On Cargo Placed event runs after successful delivery. If you previously used a finish trigger's On Level Completed event for this cargo contract, migrate the intended callbacks to this event.

Cancelling/failing still restores the cargo and player snapshot; cleanup is not blocked by the manual-drop restriction. Ordinary unassigned cargo and vehicle cargo loading keep their existing behavior. This change does not add persistent destination placement for delivered player cargo.

## Reusing one box for a second contract

Assign the same scene CargoItem as Linked Cargo in both NPC contract phases (or Test Cargo on both workbenches). Give each destination its own CargoDropLocation assigned to that destination's contract. After delivery 1 the box stays locked at drop location 1. Receiving contract 2 or opening its workbench does not unlock it: successfully starting Simulate for contract 2 does. The player can then pick up the same box and deliver it to location 2. Cancelling/failing restores its pre-test position, parent, weight, physics state and delivered/pickup lock, including when the player never picked it up. Successful delivery locks it again at location 2. Vehicle-mounted permanent cargo is never unlocked by this flow.
