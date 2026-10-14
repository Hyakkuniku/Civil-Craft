# NPC quizzes

The quiz system works offline and supports any authored number of questions and choices. It is reusable; no existing NPC story has been changed.

## Create the UI and try the example

1. Leave Unity in Edit Mode and use **Assets → Refresh** after importing these scripts.
2. Use **Tools → Civil Craft → Quizzes → Create and Validate Quiz UI**. This creates the reusable `Assets/Resources/UI/QuizUI.prefab` and `Assets/Data/Quizzes/NPC Quiz Example.asset` without changing scenes. Re-running the tool validates existing assets instead of replacing your customization.
3. The example has three questions showing text, image-only, and mixed choices. It does not activate itself or grant a default reward.

## Author quiz questions

1. In Project, choose **Create → Civil Craft → NPC Quiz**.
2. Set the title and add as many questions as needed. They run in list order.
3. Give each question a prompt and at least two choices. Each choice can contain text, an image Sprite, or both.
4. Set **Correct Choice Index** to the one correct answer: `0` is the first choice, `1` the second, and so on. Optional feedback is shown after a correct answer; incorrect answers only show **Try again**.
5. Read the Inspector validation message before assigning the asset. Missing stable IDs are assigned automatically; keep published IDs unchanged. Changing content resets only the affected quiz's saved answers. Existing image-file edits are synchronized before Play Mode and builds so changing an image also invalidates its quiz progress.

## Add a quiz to an NPC phase

1. On the NPC's **NPCProgressionManager**, add a phase with a unique **Phase ID** and the desired NPC location. Preserve the IDs and order of already-released phases.
2. For a quiz-only phase, leave **Contract** empty. Set its **Dialogue Prompt**, such as “Take the quiz”.
3. Add an **Optional Dialogue Sequence** entry, set **Step Type → Quiz**, and assign the quiz asset. Keep its generated **Quiz Step ID** unchanged after release.
4. Add Dialogue steps before or after Quiz if you want a conversation around it. Leaving the quiz does not advance the sequence.
5. Clear **Unlock Feature ID After Dialogue** if no reward is intended. Existing phases default to a minimap feature reward; the quiz does not require or create one.

## Player behavior and testing

The player chooses an answer and presses **Submit**. Correct answers are saved individually, then **Next** advances. Every question must be correct before **Finish** continues the NPC sequence. **Leave Quiz** saves progress; interacting again resumes the first unresolved question, including after restarting offline. A save failure offers **Retry Save** and a safe exit without granting completion.

Test a disposable/offline save first: leave/reopen, restart, wrong answers, image choices, long scrolling content, and **Pause → Settings → Resume**. Confirm movement/look return after leaving or finishing, and follow-up dialogue retains its own control lock. The fullscreen panel hides gameplay HUD, blocks background clicks, and respects Escape/Android Back. Multiplayer visitors cannot save or advance the host's NPC story.

The native authoring report is `Temp/NPCQuizAuthoringValidation.txt`; detached phone/tablet/desktop/portrait previews are `Temp/npc-quiz-*.png`. These checks never run authentication, network calls, live NPC callbacks, or player saves.
