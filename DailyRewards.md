# Daily login rewards

Edit `Assets/Resources/DailyRewardSchedule.asset` in the Unity Inspector. The shipped track grants 100, 150, 200, 250, 300, 400, and 600 gold. Add/reorder entries or change their gold, EXP, icon, title, cosmetic ID, and duplicate-cosmetic gold. Cosmetic IDs must match the existing PlayerCosmetics catalog. Cosmetics are unlocked without replacing the equipped hat.

## Rules

- Manual claim once per device UTC calendar date, resetting at 00:00 UTC (not a rolling 24 hours).
- Missing one or more UTC dates restarts at Day 1, as requested. Returning the following date advances the track. After Day 7, the track repeats.
- No catch-up gifts. A day only counts when its gift is claimed successfully.
- Same-date claims and dates earlier than the last successful claim are rejected. Device UTC supports offline play but is not tamper-proof; moving the clock forward, editing/deleting saves, and restoring an old save can bypass this local protection. PlayFab currently handles authentication; it is not used as an authoritative reward service.
- `repeat`, `resetAfterMissedDay`, and `requiredFeatureId` are editable. Empty feature ID makes the feature immediately available in gameplay. A nonempty ID uses PlayerDataManager's existing persistent feature-unlock collection. A completed nonrepeating schedule stays completed even after an absence.
- The save stores a global last claim date and positional progress within the track. Reordering/changing the schedule changes future gifts, but cannot clear today's lock. Keep schedule order stable after release when maintaining reward continuity matters.

## Integration

The runtime scene-loaded hook creates one scene-owned DailyRewards view in each configured gameplay scene: CanyonCrossing and BHAN HOUSE. No scene serialization or manual setup is required, including when opening either scene directly. Main Menu and Mode Selection are excluded. The Resources asset is required and included in builds automatically.

The launcher is hidden during Build Mode and other coordinated modals. Opening the cream/wood/gold panel uses UIPanelCoordinator and captures/restores player movement and look controls. Its landscape canvas uses Screen.safeArea and refreshes for resolution, rotation, and resume changes. Long schedules use a horizontally draggable track. Existing Bekind Sans typography is referenced by the schedule.

PlayerDataManager commits currency, lifetime totals, cosmetic ownership, and the date/progress marker together through TrySaveGame. A failed save restores those changes in memory. Currency listeners and achievement checks run only after success. ItemUnlockUI presents the saved gift; it receives no cosmetic ID to avoid granting/equipping the item again. If that popup is absent, the daily panel displays confirmation itself.

## Verification

Run Tools > Civil Craft > Validate Daily Rewards, or Unity batch mode with `-executeMethod DailyRewardsValidation.Run`. The 18 checks cover fresh saves, UTC midnight, same-day duplicates, rollback dates, missed-day reset, optional keep-progress, repeat/finite completion, invalid configuration, reward totals, persistence/reload, duplicate cosmetics, save failure, and overflow. The harness uses a temporary save and an inactive manager; it does not load or overwrite the player's save.

Manual Play Mode checks: open/close from both gameplay scenes; claim and restart; verify Collect and currency UI; enter/exit Build Mode; open other modals; check landscape notch safe areas and long schedules; confirm touch drag, desktop movement/look restoration, and a missing Collect popup fallback. Compilation alone does not verify these interactions or appearance.

### Verification performed (2026-09-06)

- Unity 2022.3.62f3 compiled the full project scripts successfully.
- `DailyRewardsValidation.Run` passed all 18 assertions in the full project; Unity exited with code 0. See `daily-rewards-validation.log` in this worktree (generated, ignored by Git).
- Ten date-policy checks also passed outside Unity against the actual schedule source with Unity metadata stubs.
- No Play Mode, mobile-device, or visual layout tests were performed. The first headless import emitted a Unity Sequences package postprocessor exception and shader fallback warnings. The successful validation run emitted existing PlayerDataManager.OnValidate asset-loading warnings; no C# compilation errors or failed reward assertions occurred.
