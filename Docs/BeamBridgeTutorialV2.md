# Updated Beam Bridge tutorial

Source: [2.NEW Tutorial 1 - Beam Bridge](https://docs.google.com/document/d/1Obj4wLnl_Y0_Mp5PFlWjC2p45mzwhqPOECd5v-O1gvM/edit?tab=t.h5we7ijw95q1), read 19 October 2026.

The author confirmed that any submitted score advances the story. No new quiz reward or passing-score gate is applied. Duplicate transition paragraphs and repeated phase numbers in the source are treated as editorial duplication, not repeated gameplay.

## Flow

Arrival and Bhan's house retain their existing movement, name registration, Almanac pickup/review, hard-hat and door callbacks. Wording follows the updated document; the existing sprint-toggle explanation is retained.

Outside, Bhan introduces the vehicle. Its inspection unlocks Live Load and waits for an Almanac open/close review before Bhan moves to the blueprint. WorkBench's existing ordered sequence now introduces Wood Road, Wood Pier and the Beam Bridge lesson before the first contract. The contract's required vehicle weight is 200 kg; its existing 75,000 budget stays unchanged because the source leaves the budget TBD.

The build tutorial keeps its existing camera gestures, anchor references and ghost placement geometry. Statistics opening/closing, grid toggling and material inspection require real actions. Road-placement feedback and a completed-structure message precede simulation. Simulation includes approach, entry, midpoint, destination, success and failure guidance.

After saving and turning in the bridge, Bhan waits for the completed-entry Almanac review, explains Dead Load, shows its introduction, and offers the five-question quiz through the Almanac's Quiz tab. Players can review lessons before confirming Start Quiz. Once started, the assessment accepts one answer per question, permits Previous/Next and answer changes, and enables Submit only after all questions have answers. It displays the score and continues on any score, including zero. The original practice-quiz mode is unchanged.

The existing transition across the bridge, Wood Beam introduction and Tutorial 2 remain. No new quiz currency, item, experience, feature reward or website behavior is added.

## Save compatibility

- `MainContractNPC`, `TUT_CONTRACT1`, `TUT_CONTRACT2`, all existing Bhan phase IDs and phase indexes remain unchanged.
- New ordered enum roles are appended: Lesson Introduction = 3, Almanac Review = 4; Dialogue = 0, Material = 1, Quiz = 2 are unchanged.
- The quiz is an ordered step in existing `DeadLoad`, identity `beam-bridge-engineering-assessment-v1`. No new phase shifts later numeric gates.
- Existing lesson IDs, including Beam's authored leading whitespace, material IDs, tutorial IDs and asset GUIDs remain unchanged.
- Assessment answers/results use existing profile-separated quiz save records. Completion is committed before NPC continuation. Visitors cannot write host progress.
- Existing accounts already beyond this tutorial are not rewound. Test the full changed flow with a disposable fresh profile; do not delete real saves.

## Authoring commands

`Tools → Civil Craft → Story → Beam Tutorial → 1. Apply New Tutorial to Saved Scenes` validates both scenes and exact targets before authoring. It refuses Play Mode and any already-dirty loaded scene, copies the previous two scenes outside Assets, saves only the intended tutorial changes, then restores the original scene setup. Failures retain partial unsaved edits for review instead of silently discarding them.

`Tools → Civil Craft → Story → Beam Tutorial → 2. Validate Loaded Canyon` checks saved phase identities/order, precontract lesson placement, review/Dead Load/assessment ordering and no new quiz reward.

`Tools → Civil Craft → Quizzes → Create and Validate Quiz UI` regenerates the reusable prefab and runs detached phone/tablet/portrait/desktop layout and click checks. It does not modify released NPC sequences or player saves.

## Play checks

1. Fresh guest: movement → knock → register name → collect/open/close Almanac → hard hat → outside.
2. Inspect the 200 kg vehicle. Close Live Load, open/close the Almanac, then follow Bhan. Ensure inspection cannot skip the review or advance twice.
3. Talk at the blueprint: both material introductions, then Beam Bridge introduction, then first contract. Confirm no duplicate Beam lesson on bridge save.
4. In Build Mode, verify Statistics open/close, Grid toggle, material long press, all existing guided placements, simulation and Retry.
5. Save and turn in. Open/close the recorded bridge entry. Check input is restored before the following dialogue, including Pause → Settings → Resume during review.
6. Finish Dead Load. Open Almanac, review Lessons, choose Quiz, Back, then Start Quiz. Confirm other book tabs/close cannot bypass the attempt.
7. Answer all five incorrectly. Submit must remain disabled until the last answer. The result must be 0/5, Continue must still advance, and no new rewards may appear.
8. Repeat with correct answers C/B/A/D/B; verify 5/5. Check Previous/Next and changing an answer before submission.
9. Restart offline during an assessment and after submitting. Reopen through Bhan/Almanac; answers/results should resume without a second completion callback. Verify save-error Retry Save without advancing prematurely.
10. Check signed-in versus guest saves, protected multiplayer visits, scene unload, Android Back, nested Pause/Settings, phone/tablet scrolling and normal movement after Continue.

Automated boundary checks do not replace a fresh-profile playthrough or Android touch testing.

## If Bhan waits after vehicle inspection

Closing the vehicle inspection and Live Load introduction is not the final gate. The updated tutorial deliberately waits for the player to open the Almanac, review the Live Load entry, and close the book. Bhan then moves from LiveLoad to WorkBench. Opening or closing the inspection again does not replace or restart that pending review, and must not report it as unavailable. A different profile, protected multiplayer visit, or another owner's callback cannot claim the pending review.
