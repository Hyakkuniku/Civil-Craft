# Almanac bookmarks and profile readability

The handwritten font and existing book art remain unchanged. Runtime presentation repairs apply to every scene using the existing Almanac, without editing player saves, wallet availability or chapter unlock rules.

- Render order is **cover → bookmarks → pages**. Existing full-stretch binding furniture moves into a shared backdrop; page transforms and persistent frame references remain unchanged. Custom scaled/rotated bindings are not silently rewritten.
- Visible bookmarks use consecutive slots. Locked or always-hidden Materials does not leave a vacant gap. Once a material is discovered, the normal Materials unlock rule shows its bookmark again.
- Quiz uses the same exposed header strip as other bookmarks and dark text on the cream face. It no longer floats over the page or inherits a pale-on-pale label.
- Profile currency cards use an icon/label header and a separate full-width value. `Unavailable` and large balances stay truthful, readable and on one line. Rank, experience and footer fields have separate bounds; long names remain inside their own card.

## Verification

Use **Tools → Civil Craft → Almanac → Validate Runtime Book Layout (Read Only)** in Edit Mode. It clones loaded books, installs the real runtime Quiz category before initialization, checks cover geometry, and renders phone/tablet/wide-screen fixtures. It does not open a real book, start a quiz, mark chapters read, or save a source scene.

Automation request: `Temp/almanac-readability-v1.request`. Report: `Logs/almanac-readability-v1.txt`. Previews: `Temp/AlmanacReadability_*.png`. Fixtures separately cover locked and unlocked Materials, no-offer and available-quiz presentation, `Unavailable`, and the maximum signed 32-bit balance. Graphic raycasts verify visible Quiz/Start controls without invoking them.

Focused boundary suites are `Tests/AlmanacBookmarks`, `Tests/AlmanacProfileLayout`, and `Tests/NPCQuizReminder`. A real phone playthrough is still recommended for touch, book opening/closing and NPC interaction.

Verified on 19 October 2026: 24 native rendered profile/quiz fixtures across CanyonCrossing and Bhan House, at 1920×900, 1600×720 and 2048×1280; both Materials states, available Quiz/Start raycasts and source-isolation checks passed. Boundary suites passed 53 bookmark checks plus six guards, 65 profile checks, and 66 reminder checks plus seven guards. Existing dialogue/Pause, assessment-session, Almanac-offer and saved Truss-story regressions passed. Preview balances/names are isolated test data, not changes to a player account.
