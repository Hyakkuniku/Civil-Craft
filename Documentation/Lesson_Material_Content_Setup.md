# Lesson and material content

Use the same existing ScriptableObject in your triggers, NPC sequences and Almanac lists. Do not duplicate assets or change their permanent IDs.

## Lessons (LessonData)

- **Popup / Lesson Image:** introduction photo; also the default Almanac image.
- **Popup / Popup Description:** short introduction, ideally 1–3 sentences.
- **Almanac / Lesson Description:** full explanation, examples and engineering notes. This preserves the original description field and its existing text.
- **Almanac / Almanac Image:** optional separate diagram/photo. Empty means reuse Lesson Image.

An empty Popup Description uses Lesson Description for backwards compatibility. An empty Lesson Description uses the popup text. Fill both to show different content.

## Materials (BridgeMaterialSO)

- **Popup / Introduction Description:** brief explanation of the material.
- **Popup / Introduction Image:** optional photo; falls back to the existing Material Icon.
- **Almanac / Almanac Description:** full explanation, recommended uses and limitations. Empty means reuse the introduction.
- **Almanac / Almanac Image:** optional reference illustration; falls back to the popup image.

Material Icon remains the build-tool/receipt icon; setting the new photos does not replace it. The popup no longer includes the long property list. Almanac reference pages still show the live cost, mass, maximum length, tension and compression values from this same SO; do not duplicate those numbers in prose unless needed.

## Unlocks and saves

- Showing a lesson introduction unlocks the lesson as before.
- Acknowledging a material introduction with GOT IT discovers the material as before.
- Reading an Almanac entry does not replay introduction callbacks or discover it again.
- Existing save IDs, NPC sequence assignments, contract material permissions and saved progress are unchanged. No save migration/reset is required.
- Existing assets remain readable without filling new fields. They will not automatically gain newly written summaries or full explanations.

## Verification checklist

1. Give one lesson distinct popup and full descriptions; trigger it. Verify the short text/photo, close callback and unlocked Almanac entry.
2. Open its Almanac page: verify full text, optional image, scrolling and return navigation. Reopening must not advance an NPC phase.
3. Repeat for a material. Verify discovery happens on GOT IT, not just when the popup opens; check the reference includes full text and material properties.
4. Queue multiple NPC material introductions and acknowledge each. Verify each discovery and sequence callback happens once.
5. Restart and check unlocked entries remain available; locked entries retain their existing restrictions.
6. Clear optional images/text and verify the documented fallbacks, including an older asset with none of the new fields filled.
7. If using the legacy material tab rather than the learning hub, keep LessonUIManager and its scrollable lesson canvas configured: it now serves the full material reference rather than squeezing it into the introduction popup.
