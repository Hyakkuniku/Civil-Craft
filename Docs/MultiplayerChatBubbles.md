# Multiplayer typed chat bubbles

The existing multiplayer Chat button and typed-message panel remain unchanged. A successfully accepted message also appears above its sender on each connected client. This extends the existing CanyonCrossing host-world chat; it does not enable chat in unrelated multiplayer modes.

## Behavior

- One bubble per sender; a newer message replaces the previous one.
- Six-second lifetime using unscaled time, with a fade during the final second.
- Existing 280-character limit, client cooldown, host validation and server rate limit remain in force.
- Plain text only; typed rich-text tags and escaped formatting commands are not interpreted.
- Cream background, dark border/text, and the existing chat font. Long messages wrap without shrinking the text.
- Bubbles track the locally displayed character position. They are screen-space overlays, so they face the viewer and are not rendered into minimap or portrait textures.
- Bubbles hide beyond 30 metres from the gameplay camera, behind the camera, outside screen/safe-area bounds, over reserved UI regions such as the minimap, and while panels/dialogue/pause or non-normal gameplay obscure the view.
- Time continues while a bubble is hidden; closing a panel does not restart its lifetime. The full message remains in existing session chat history.
- No input blocking, player-save changes, extra per-frame network messages, or scene/prefab authoring is required.
- Overhead labels are not a line-of-sight system: scenery occlusion is not tested for bubbles.

## Networking

The host derives `PlayerRef` from the validated submitting avatar's InputAuthority and includes it in the existing reliable, sequenced delivery RPC. Each client resolves that player object locally. Names are never used to choose which avatar gets a bubble. Unknown/despawned senders do not get deferred/replayed bubbles. Existing chat history still receives the same event signature.

Both host and guest must use the updated build because the delivery RPC signature changed. Do not test this feature with an old APK connected to a new Editor build.

## Verification

- `dotnet run --project Tests/PlayerChatBubble/PlayerChatBubble.csproj`: 27 timing, replacement, normalization, Unicode, history-regression and structural integration checks.
- Runtime and Editor sources compiled against the project's Unity dependencies. This isolated compilation does not substitute for Unity import/Fusion weaving or a live room test.
- After Assets → Refresh, **Tools → Civil Craft → Multiplayer → Validate Chat Bubbles** checks detached phone/tablet/portrait layouts, maximum-length text, literal markup, click-through and cleanup. Report: `Temp/player-chat-bubble-validation.txt`. Preview images: `Temp/chat-bubble-*.png`. It does not save the open scene.

### Editor results — 15 October 2026

Unity import and Fusion processing completed successfully. The 27 standalone checks passed. Detached native checks passed at 1280×720, 1024×768 and 720×1280, including short messages, literal markup, 280-character wrapping, click-through and clearing. Correctly framed maximum-length previews were visually inspected. The source scene's dirty state was preserved and no scene was saved. These checks do not verify live delivery between two players.

## Two-player acceptance test

1. Refresh Unity outside Play Mode. Build the updated APK for the second device/client.
2. Host a CanyonCrossing world and join with the second player.
3. With both players nearby, send a message from the host. Close Chat promptly: confirm it appears over the host on both screens, and once in each chat history.
4. Repeat from the guest; verify it follows the guest rather than the host. Also test matching display names.
5. Send another message before six seconds elapse: only that sender's newest bubble should remain.
6. Test a 280-character message, line breaks and literal `<size=999>hello</size>` text. Confirm no oversized markup or unreadably shrunken text.
7. Move, rotate the camera, walk beyond range, open the minimap/expanded map and Pause/Settings. Check that bubbles neither cover reserved map UI nor block gameplay clicks/touch.
8. Leave the room, reconnect and change scenes. No old bubble should return or attach to a new player.

Live host/guest RPC delivery and Android touch remain pending until this acceptance test is performed.
