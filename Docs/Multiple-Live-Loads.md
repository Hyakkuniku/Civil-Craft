# Multiple live loads per contract

Existing contracts remain in **Single** mode. No existing budget, weight, material allowance, NPC progression or contract ID is changed by installing this feature.

## Configure a contract

1. Select its **ContractSO** asset. Set **Vehicle test mode** to **Sequential** or **Together**.
2. Keep **Live Load Mode = Vehicle** and **Win Condition = Finish Line**. Multiple-load timed and player-carried delivery contracts are not supported.
3. Add **2–4 Required vehicles**. Give each a display name and a positive **Empty Weight Kg**. The permanent entry ID is generated when you press **+**; reordering entries preserves their IDs.
4. In **Together**, set each **Departure Delay** in simulation seconds after settling, for example `0`, `3`, `6`. Zero starts together. Delays pause with the simulation. Sequential mode follows list order and ignores departure delays.
5. For trucks that the player loads, enable **Allow Cargo** on that entry and set **Minimum Loaded Cargo** if required. Actual loaded cargo mass is added once to that vehicle's empty mass. Keep the existing cargo items, slot IDs and NPC dialogue gates.

Switching back to **Single** retains the original single-vehicle settings and keeps the list available for later use.

## Bind the scene vehicles

1. On the contract's **Build Location**, add one **Multiple Live Load Binding** per required entry.
2. Choose the matching **Contract entry** from its dropdown and assign its exact existing scene **LiveLoadVehicle**. One vehicle cannot fill two entries. These references stay in the scene, not in the ContractSO asset.
3. Set every vehicle's **Assigned Contract** to this contract and its **Physics Manager** to the same active bridge physics manager.
4. Assign each vehicle's existing **Start Point** and **End Point**. The whole vehicle must start before the bridge and finish beyond it. Space starting positions apart; use departure delays for traffic spacing. Space end markers apart if you want distinct visible parking positions.
5. Keep the authored **FinishLineTrigger** assigned to this contract and covering the crossing's destination. Each vehicle must enter it and finish the complete crossing. Isolated multiplayer competition copies use their authored route endpoints instead of story finish zones.
6. Press **Validate This Live-Load Setup (Read Only)**, or use **Tools → Civil Craft → Multiple Live Loads → 1. Validate Open Scene Setups (Read Only)**. Invalid bindings, weights, routes, overlaps and cargo requirements stop a test with a clear message.

For a saved bridge replay, the normal replay flow restores its vehicles. Read-only setup validation and multiplayer invitation preparation can inspect hidden vehicles without activating them.

## What the player sees

- Contract requirements show the vehicle roster, individual empty weights, cargo requirements and crossing mode in a scrollable area.
- A compact build-mode card reports the current vehicle and passed count.
- Sequential tests retain bridge deformation, broken parts and peak stress between vehicles. Together tests combine every released vehicle's supported load at its actual bridge position.
- The first finish cannot complete the contract. Every required vehicle must reach its route end with the whole solid vehicle beyond the bridge. Chassis overhangs count for clearance; wheel contact separately determines supported weight. A failed or missing required participant fails the attempt; waiting vehicles do not accrue stall failures.
- Success receipts and failure details list individual outcomes. Retry or cancellation resets the complete attempt; partial vehicle results are not added to player saves.
- Existing capacity estimates are labelled as single-load estimates. The scalar estimated safety ratio is hidden for multiple-load tests; actual test stress remains available.

## Multiplayer

The host owns simulation and completion. Ordinary spectators receive the complete vehicle roster, body/wheel motion and frozen inspection weights.

Bridge competitions freeze the roster, weights including loaded cargo, mode and departure delays when the host invites the guest. Both submitted bridge designs use that same snapshot. Changes after the invitation do not alter only one player's test. Test indices still identify the host's design and guest's design, not the vehicles.

The competition's existing timeout allowance applies per vehicle in Sequential mode. Together mode adds the latest frozen departure delay to that allowance. Both designs receive the same limit. This watchdog does not add a timer-based completion rule to story contracts.

Both devices need a build containing the new multiplayer protocol. Older builds cannot join these rooms. Competition copies remain isolated from story rewards, NPC progression and saved bridge geometry.

## Authored UI and validation

The status card uses the existing build UI style, starts hidden, and appears only during a multiple-load simulation. Repeatable Editor commands author/verify it in CanyonCrossing, Bhan House and Multiplayer. They preserve existing unsaved edits; save those scenes in Unity when the tool reports **WAIT SAVE**.

The graphics-enabled Editor preview command renders temporary phone/tablet copies with a camera restricted to the preview scene. It checks text bounds, scrolling and actual rendered UI pixels. The source scenes, dirty flags, player data and original scene setup are checked for changes.

Managed tests cover roster validation/scheduling, combined solver behavior, transport bounds and formatting. Native checks use actual Unity collider geometry, inactive source vehicles, frozen invitation values and exact collider restoration. Those native checks do not run a live physics crossing. Compilation and detached graphics checks do not replace device testing.

Before enabling a production contract, test two and four vehicles on the Samsung A12 with the minimap and quality settings unchanged. Compare CPU/GPU frame time and memory against Single mode, and run a host/guest test on two matching builds. No A12 performance result or live two-device result should be inferred from the automated checks.
