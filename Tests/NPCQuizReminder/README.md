# NPC pending-quiz reminders

Run `dotnet run --project Tests/NPCQuizReminder/NPCQuizReminder.csproj` from the Unity project root.

Extracts the production NPC reminder, pending-offer query, and ownership-safe dialogue cancellation methods into isolated Unity boundary doubles. Exercises repeated talks, double taps, custom words, speaker animation binding, busy/modal gates, practice and active quiz exclusion, exact phase/step/asset/owner identity, profile/visitor changes, stale callbacks, and cancellation without touching a running Editor, scene, save, account, or network.

The default reminder is used for old authored scenes. Optional **Quiz Waiting Reminder** is available on every NPC ordered Quiz step; editing it changes only the reminder, not its stable progress identity or quiz results. These tests do not claim actual rendered layout or mobile input verification.
