# Updated Truss Bridge tutorial

Source: [3.NEW Tutorial 2 - Truss Bridge](https://docs.google.com/document/d/1Obj4wLnl_Y0_Mp5PFlWjC2p45mzwhqPOECd5v-O1gvM/edit?tab=t.l0uc88qoc8cz), read 19 October 2026.

Confirmed decisions: the quiz follows the contract turn-in, before Bhan leaves for Main City. Any submitted score advances, with no additional quiz rewards. The requested temporary world-route gate is deliberately omitted at the author's direction.

## Story and construction

The existing `Beam` phase now contains seven ordered steps: meeting dialogue, Wood Beam introduction dialogue, the Wood Beam material popup, Truss Bridge explanation, Truss lesson popup, Equilibrium explanation, and Equilibrium lesson popup. Both lessons are therefore introduced before the second contract, not after acceptance. Existing material properties and discovery IDs are retained; the Beam material display name is Wood Beam.

`Build Tutorial 2` retains the existing `TUT_CONTRACT2` and second build location. The live load is **400 kg**; the budget remains **75,000**, with the existing **500 Coins / 150 EXP** contract reward and explicit NPC Collect flow. Road/beam restrictions are unchanged.

The updated 19-step guided construction retains all six existing opening steps, including the Erase and Move tool introductions as explicitly requested. It also retains the existing anchors, three blueprint graphs, actual selection/copy/paste gates and final Play unlock. Updated guidance teaches road placement, the first triangular section, selection, copying and placing the copied section. Three added feedback screens pause for Continue after the road, first section and complete truss. Existing tool functionality is unchanged.

Simulation guidance follows vehicle approach, entry, midpoint, near end, success and failure, explaining member load transfer, balanced forces/moments and sufficient strength. Failed tests return to editing without deleting the Tutorial 2 bridge or restarting its completed construction tutorial. Other locations retain their previous retry behavior.

Saving the completed bridge queues the Equilibrium lesson review for the exact second location and matching contract. It waits for build exit, camera transitions and other panels to finish. It does not add a mandatory Almanac open/close gate. The existing bridge record and turn-in guide remain.

After the normal turn-in dialogue and Collect, Bhan's existing `Follow` phase offers the five-question Truss assessment through the Almanac. Players can review before starting. Answers are B/C/A/D/B. Submission and durable completion use the existing assessment system, including any-score continuation, offline/profile-separated progress and the asking NPC's camera presentation. Only after its final dialogue do the original Follow Bhan guide and City-departure callbacks run.

## Save compatibility and boundaries

- All 16 Bhan phase IDs and indexes remain unchanged. No new phase is inserted.
- `MainContractNPC`, `TUT_CONTRACT2`, `Sequence_Build2`, material/lesson IDs, existing GUIDs, blueprint geometry, contract budget and rewards are retained.
- Quiz ID: `civil-craft-truss-bridge-quiz-v1`. Ordered step: `truss-bridge-engineering-assessment-v1` in existing `Follow`.
- An old save still in `Follow` may take the new assessment even if its previous short dialogue was recorded. Saves already at City or later are not rewound. Earlier completed introductions are not forced to replay.
- The saved-bridge review and deferred arrival cancel stale scene/phase/contract or protected multiplayer-visitor work.
- No changes to Bhan House, Tutorial 1 story, website, purchases, authentication, route blockers or player-save formats are part of this update.

## Repeatable Editor commands

Open the saved CanyonCrossing scene in Edit Mode. Use `Tools → Civil Craft → Story → Truss Tutorial → 1. Apply to Saved Canyon (Do Not Save)`.

The command validates exact existing content and callbacks, rejects unknown/custom step shapes, creates a recoverable pre-migration scene copy outside Assets, and changes only the intended Canyon components. It does not open/close other scenes or save any scene. Review and save CanyonCrossing yourself. Missing references stop the migration; failed changes are undone as one group.

`Tools → Civil Craft → Story → Truss Tutorial → 2. Validate Loaded Canyon` checks the flow, retained callbacks, no-restart policy, lesson hook and construction geometry, and reports whether the scene still needs saving. Reports are in `Logs/truss-tutorial-story-v1.txt`.

The build-only authorer and read-only validation are also available under `Tools → Civil Craft → Tutorial 2`.

## Play checks

Use one gameplay scene at a time and a disposable profile; do not delete real saves.

1. Finish Tutorial 1 normally. Verify Wood Beam, Truss and Equilibrium popups appear before the second contract and are recorded in the Almanac.
2. Accept Tutorial 2. Confirm 400 kg, 75,000 budget and only the existing allowed Road/Beam materials.
3. Complete each guided action. Selection, Copy and Paste must require their actual actions. Each feedback Continue must preserve the blueprint until its next step. Play remains gated until the final construction feedback.
4. Fail a test and Retry. The bridge remains editable, no construction step restarts, undo is retained and the next test is usable. Verify Tutorial 1's old retry behavior is unaffected.
5. Pass and Save. The Training Truss Bridge record is created. Equilibrium opens only after the build-exit transition; close it and return to Bhan.
6. Turn in and Collect once. The quiz offer must wait for the reward panel to close. Check the original 500/150 reward is not duplicated.
7. Start through Almanac, answer all incorrectly, submit and continue. The result is 0/5 but Bhan still delivers the departure dialogue and then walks to City. Repeat on another disposable profile with B/C/A/D/B for 5/5.
8. Test offline restart during the quiz, save-error retry, Pause/Settings, scene unload, and a protected multiplayer visit. Stale callbacks must not move Bhan or grant progress.
9. Check an old save still in Follow and another already at City. Only the first can encounter the new quiz; later story is not rewound.

Automated and detached Editor checks do not substitute for a complete Android playthrough.

## Verification record

The Canyon migration and user scene save completed on 19 October 2026. Native validation reports 59 read-only construction checks passing and `NEEDS_SAVE: none`. The independent story harness passes 216 source/asset/saved-scene assertions, including the exact retained Erase/Move introductions. Retry coverage passes 151 executed source assertions plus six integration guards. Existing quiz, assessment session, Almanac access, ordered learning, NPC camera/avatar and startup-panel regressions also pass. Bhan House's file hash was unchanged during this migration.

Runtime and Editor compilation succeeded. One pre-existing unused ShopManager placeholder warning remains unrelated to this update. A full fresh-profile Android story playthrough is still required.

Physics boundary: the contract, display, vehicle assignment and deterministic structural solver use the required 400 kg. Existing PhysX wheel rigidbody masses are separate locomotion bookkeeping and were not retuned by this story update.
