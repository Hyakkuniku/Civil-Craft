# Dialogue HUD entry buttons

Run `dotnet run --project Tests/DialogueHudButtons/DialogueHudButtons.csproj`.

The suite compiles the complete production dialogue HUD mask plus the actual
Almanac/Objectives/Shop eligibility properties and shared dialogue visibility query.
Unity component lifetime, CanvasGroup composition, dialogue transitions, and modal
SetActive snapshots are explicit boundary inputs. It checks all three targets, unlocks
and stale HUD restores during dialogue, closing/chained/concurrent conversations,
owner eligibility, gate teardown, authored CanvasGroup preservation, and no writes
to progression or GameObject activation. Source guards preserve direct book/Shop access.
The actual raw dialogue hide/restore methods also run against exact button entries,
ancestors, mid-dialogue unlocks, and buttons bound after a stale snapshot was taken.
Shop coverage includes an intervening Build Mode, a locked replacement profile,
the open Shop panel, authored owner visibility, disable/enable, and exclusive-role
rebinding. Dialogue closure never revives an entry that another owner still hides.
No scenes, live player data, accounts, or real UI are changed.
