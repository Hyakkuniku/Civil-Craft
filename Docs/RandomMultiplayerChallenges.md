# Random multiplayer challenges

In a host-world multiplayer session, the host opens **Chat → Random Challenge**. The action is enabled when a guest is connected and the host is free to start. The host's game chooses an eligible location, sends the existing challenge invitation, and shows the selected contract name to both players. The guest can accept or decline. Acceptance uses the existing travel, Ready, separate bridge design and host-authoritative testing flow.

The random pool covers the loaded host-world map, including Canyon Crossing, Town River and Industrial Zone sites. **Both players must have completed the crossing in their own personal saves and retain valid saved bridge data.** Seeing the host's bridge does not complete it for the guest. Each eligible location has an equal selection probability; regions with more eligible crossings have more entries. Repeats are possible.

## Eligibility

The invitation-time check requires a unique scene path, a stable non-tutorial vehicle/Finish Line contract, a valid budget and road width, both anchor banks, available build camera and grid, usable allowed materials, a valid frozen vehicle roster and routes that start before and finish beyond the bridge. Both players must have distinct supported landing positions with clear headroom. Sites currently being redesigned or used by a travelling NPC are excluded.

Timed tests, player-carried cargo tests, tutorial contracts, temporary challenge objects and incomplete setups stay out of the random pool. A disabled story workbench can qualify if its separate build view and surrounding landing geometry are available; the picker never activates a locked story container to make it qualify.

Site and vehicle searches run when the host presses the button, rather than during chat's regular availability refresh. Both personal completion snapshots must be ready and use the same permanent-ID catalog. The host filters by their intersection, then rechecks completion before sending the invitation and before travel. The guest also checks its own current save when accepting. If neither player shares an eligible completed crossing, Chat explains that no ready crossing has been completed by both players yet. The existing manual Challenge action uses the same completion gate.

The host alone chooses and publishes the result. Guests cannot request their own site through this action. The chosen contract, budget, width, vehicle order, weights, cargo masses and departure delays use the existing frozen invitation data for both bridge designs.

Only a bounded completion bitset and contract-manifest fingerprint are synchronized, through an input-owner-to-state-authority reliable RPC. Missing data, different catalogs, invalid mask bits, account changes and disconnected avatars cannot qualify. Save commits refresh the personal snapshot; unchanged acknowledged snapshots do not send continuously. These are owner-reported local save completions, not new backend anti-cheat attestations.

## Story and chat preservation

The feature reads a retained authored contract definition, or the NPC's authored site/contract assignments, when the workbench has no active story contract. That read-only result is cached without accepting a contract or applying a story phase. It does not unlock contracts, change NPC phases, award story rewards or replace saved bridges. The existing completed-crossing Challenge action remains available through its original flow.

Chat history and unsent text remain intact when starting an invitation. The new action is hidden from guests, disabled while waiting for a guest or another challenge, and blocked during dialogue, pause, loading and tutorial activity. Disconnect and cancellation reuse the established challenge cleanup and return-position handling.

Both players need the updated game build. The multiplayer compatibility suffix is `loads4-wide-mutual-v6`, which requires the larger Fusion heap page needed by the full avatar.

## Repeatable checks

- `Tools → Civil Craft → Multiplayer → Validate Random Challenge Destinations` runs detached eligibility, locked-site, route and uniform-selection checks and reports the real map's eligible locations.
- `RandomChallengeFeatureValidation.RunAll` is the batch entry for those checks, existing typed-chat regressions and authored host/guest phone/tablet previews.
- Reports: `Logs/random-challenge-sites.txt`, `Logs/random-challenge-chat-regressions.txt` and `Logs/random-challenge-chat-ui.txt`.
- Preview images: `Logs/RandomChallengeChatUI`.

Verified November 8, 2026 in Unity 2022.3.62f3: 45 detached selection/eligibility assertions passed, including separate personal saves, their shared completion pool, incomplete records, saved-bridge removal, no shared completion, a locked two-vehicle roster and strict story-simulation admission. Another 65 independent catalog tests cover manifest mismatches, invalid masks, readiness and bit boundaries. The seeded distribution check selected the three test sites 3,973 / 4,019 / 4,008 times over 12,000 draws. The real map has 12 structurally ready locations out of 20 workbenches; the online pool is the subset completed by both players. All 12 structural candidates passed both landing checks using the authored player controller's dimensions. Six host/guest phone/tablet previews and existing typed-chat regressions passed. The saved chat button was reloaded and verified with its persistent callback intact.

Run the independent completion checks with `dotnet run --project Tests/ChallengeCompletionGate/ChallengeCompletionGate.csproj`.

The excluded workbenches are three tutorials, Clark's second crossing with an invalid anchor reference, two inspection/realignment contracts without a full legal material set, and two side workbenches without an unambiguous authored contract assignment. Their story configuration was left unchanged; the read-only destination report gives each reason.

Live follow-up requires two updated clients: host and guest chat visibility; host-only, guest-only and mutually completed crossings; no shared completed crossings; save/profile refresh; invite acceptance/decline/timeout; both Ready states; a mutually completed multi-vehicle site; cancellation/disconnect during travel/build/test; and unchanged story progress and saved bridges after returning.
