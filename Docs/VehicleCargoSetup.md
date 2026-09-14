# Vehicle cargo loading

This is separate from PlayerCarriedCargo crossing tests. It is off by default.

1. On the contract, choose Vehicle and enable Allow Vehicle Cargo. Live Load Weight is the empty vehicle's weight. Minimum Loaded Cargo = 0 makes loading optional; 1 requires one box before Simulate.
2. Assign that contract to the scene LiveLoadVehicle. Give it through the NPC as usual.
3. Select the truck-bed child in the hierarchy. Choose GameObject > Civil Craft > Vehicle Cargo Slot. This creates a saved scene object, not runtime UI.
4. Move the slot to where the cargo object's pivot should sit in the bed and set its rotation. Adjust the BoxCollider trigger so a player behind the truck can reach it. Leave the slot on Interactable layer. Use a separate Cargo Socket child if the interaction area needs a different position from the box.
5. Optionally assign Accepted Cargo to restrict a slot to one particular box. Add separate slots for additional boxes, leaving enough space between cargo positions.
6. Set each CargoItem's Cargo Weight in kilograms. The NPC does not replace this weight with the truck weight for enabled vehicle-cargo contracts.
7. Save the scene after Unity assigns persistent cargo/slot IDs (automatic in Edit Mode; also available under Tools > Civil Craft > Assign Vehicle Cargo Save IDs). Pick up the cargo, approach the bed, then tap Load Cargo. Loading saves immediately and is one-way: the occupied slot has no pickup action, including after quitting/reopening. If saving fails, the box remains held instead of being permanently attached.
8. Enter build mode and Simulate. Truck mass, inspection total, and deterministic bridge stress include the cargo. Loading is locked during a test. Resetting a test keeps boxes attached to the truck, allowing another attempt.

Example: empty truck 1000 kg plus a 200 kg box gives a 1200 kg chassis test load (the existing separate wheel masses are unchanged).

The save stores the cargo ID, slot ID, and loaded weight. On scene load, the original scene cargo is moved into its saved slot (not cloned), keeping its weight and pickup lock. Do not remove the original CargoItem or slot from the authored scene: they are needed for restoration. Missing references retain their save records and log a warning. Existing saves without cargo records start with empty slots. Contract completion, rewards, and bridge saving retain their existing flow. A new game/reset clears cargo records together with player progress.

Manual checks: with the feature off no Load Cargo prompt should appear; with it on loading should increase the inspected total once per box and remove the occupied slot's interaction. Direct PickUp calls must not detach loaded cargo. Simulate should enforce Minimum Loaded Cargo, and repeated test/reset cycles should retain the mounted boxes and their weight. Quit/reopen and verify the same box is restored in the bed, its original ground location is empty, and its weight/pickup lock remain. Verify the box position/scale in Play Mode after moving the authored socket.
