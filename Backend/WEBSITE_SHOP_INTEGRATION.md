# Civil Craft game and website shop

Implemented source targets **https://civil-craft.vercel.app** and PayMongo **test**
payments. It is not a completed live rollout: the website/database capability must
be installed and reviewed before enabling the wallet or shipping the updated game.

## Player flow

- The shop's Coin plus and Diamond plus controls use an authored confirmation
  dialog. Guests receive Sign In / Not Now; no browser opens before sign-in.
- Main Menu uses its existing login screen. Gameplay sign-in safely saves and
  returns through the existing multiplayer leave/loading flow. Protected visitor
  saves are not uploaded or used for account wallet operations.
- Signed-in players confirm Open Shop / Cancel. The game sends its session ticket
  only in an HTTPS authorization header to obtain a short-lived, account-bound
  shop link. No session ticket, password, or developer secret is placed in a URL.
- Browser login remains separate. Its player must match the game shop link before
  checkout; a different browser account is offered account switching.
- Only a verified test-payment webhook credits currency. Returning from the browser
  refreshes the wallet; it never grants a pack locally. Diamonds are displayed,
  with no Diamond spending or conversion in this implementation.

## Coins are the existing game Gold

Signed-in Gold/Coins become one server-managed PostgreSQL wallet bound to the
PlayFab account. The `gold` field is retained for old save compatibility, but is
only a cached authoritative balance after import. Purchases and material unlocks
require internet and atomically debit Coins and record permanent ownership.
Guest earning/spending remains local.

After an approved account-save selection, the server imports remaining classic
PlayFab CO plus that selected legacy gold **once**. Explicit guest-save import is
preserved; automatic login does not silently import a guest save. Existing account
currency, receipts and paid ownership cannot be reset/recredited by loading an old
save, another device, or Start Fresh.

Offline gameplay readiness is separate from first-import source approval. A failed
automatic Resume cloud lookup/download can leave the account playable and its
reward journal usable, but cannot approve an irreversible legacy Coin import.
Import waits for the chosen save to reconcile successfully. Strict choices such
as Load Online retain their intent through retries. Already-migrated server wallet
reads do not require this first-import approval. When a held source finishes
reconciling, the wallet refreshes automatically; a successful retry does not
require reopening the shop or restarting the game.

The first import reserves cloud reconciliation for its account/session and sends
a deep-copied snapshot. It releases that reservation on completion or failure;
an outgoing account's callback cannot release a new account's reservation. If an
old journal-only pending reward also appears completed in the selected legacy
save without that save's pending record, import stays held for source review.
The queue is preserved rather than guessing whether legacy Gold already paid it.
Source-backed unpaid rewards remain excluded from historical-paid tombstones,
including contract aliases.

Typed contract/achievement rewards are queued with permanent event IDs. The local
progress/outbox save and the separate encrypted, account-scoped journal preserve
offline earnings through reconnection and account-save replacement. The server
chooses prices/reward formulas from a verified game catalog; arbitrary signed-in
debug grants are disabled. Legacy local earnings and simulation evidence are
client-authored, so this is not an anti-cheat-certified economy.

Contract payouts retain their captured cost, failure count, Coins quote and EXP
quote across restart. A ready quest's saved zero payout is a real quote, not a
missing value. Older unclaimed account rewards with no captured evidence are
preserved locally as held recovery records; they are not submitted with invented
evidence or credited automatically. Their progress and EXP remain saved.

Pending paid purchases recover before earned-reward submission. Recovery keeps
the original operation ID until a current ownership response contains the exact
item and linked cosmetic (or contract material) and the local save succeeds.
An empty/stale ownership response cannot acknowledge a paid purchase. Held or
rejected earnings do not strand paid ownership or replace verified balances with
zero; failed earnings remain queued for retry or review.

Coin-only migration, ownership or local-cache failures do not hide a Diamond
balance already verified for the same account/title/session. Coins remain
unavailable for spending. Authentication, actual connection/timeout failures,
account changes and malformed whole-wallet responses still invalidate both
availability flags; unavailable currencies are not displayed as zero balances.

An explicitly disabled/not-ready Coin wallet now displays a fixed setup message
instead of suggesting an endless generic retry. Only these known HTTP503 states
permit a read-only `/api/player/currencies` request for the independent Diamond
display. The client ignores that endpoint's classic Coins and checkout flags:
it cannot import them, overwrite the Coin cache/counters, or authorize spending.
Unknown errors, invalid/missing Diamond values, expired authentication, and changed
account/title/session still fail closed. A verified Diamond zero is distinct from
an unavailable read.

While refreshing through a recognized Coin-paused response, an already verified
Diamond balance for the same session stays visible until the independent read
finishes. The status identifies that last verified value as refreshing. A first
read never invents a balance; an actual Diamond read failure or session/account
change still marks it unavailable. This avoids healthy-refresh flicker without
enabling Coin spending or hiding an actual outage.

The main website's opaque navigation links have a separate verified link-only
capability. Visiting the main storefront therefore does not require activating
the Coin wallet or making a migration/cutover attestation. This separation does
not permit Coin checkout, grants, imports, or spending while those are disabled.
Production links are restricted to `https://civil-craft.vercel.app`; there is no
unbound URL fallback in the game and no session ticket in browser URLs.

## Authored controls

`WebsiteShopAuthoring` authors the two plus controls, a Refresh balances button,
and a reference-style confirmation dialog in Main Menu, BHAN HOUSE,
CanyonCrossing and Multiplayer. The existing authored dialog prefab is reused;
player runtime never generates a replacement Canvas hierarchy.

The authoring pass preserves already-dirty loaded scenes rather than force-saving
them. Use **Tools → Civil Craft → Author Website Shop Buttons** after scripts finish
compiling, then explicitly save edited scenes. **Tools → Civil Craft → Validate
Saved Website Shop Controls** verifies saved assets; the same validation runs
before builds and rejects missing/disconnected dialog references, either plus
button, disabled callbacks, missing refresh controls and omitted shop scenes.
It does not save scenes, open a browser, request player data or make a purchase.

## Verification and deployment

```powershell
dotnet run --project Tests/GameWalletPolicy/GameWalletPolicy.csproj
dotnet run --project Tests/GameWalletRecovery/GameWalletRecovery.csproj
dotnet run --project Tests/CloudSaveWalletReadiness/CloudSaveWalletReadiness.csproj
dotnet run --project Tests/GameWalletSceneShipping/GameWalletSceneShipping.csproj
```

The recovery executable runs the real client synchronization coroutine and the
checked-in PlayFab SDK JSON parser against a scripted transport and save doubles.
The SDK returns nonnegative JSON integers as `ulong`; the wallet validates these
against the currency-specific and JSON-safe bounds before any signed conversion.
Parser regression tests include a real zero balance, positive balances and
versions, and overflow/malformed values. It makes no PlayFab/payment requests and
does not enable the wallet. Editor/Android acceptance and the reviewed website
cutover are still required before shipping.

The cloud-readiness executable also links the real `CloudSaveManager`, with
scripted file-service responses. It covers failed Resume, strict-choice retries,
successful source selection, conflicts and account-bound import reservations.

The scene-shipping executable links the real Editor scene validation policy and
checks all four on-disk scene assets, including wrong callback targets, disabled
controls, missing dialogs and broken prefab references. Before first publication,
it can also print a wallet-only candidate built from the committed baseline. This
isolates authored additions from unrelated user scene edits without saving or
staging anything; see `Tests/GameWalletSceneShipping/README.md`.

The main website includes `scripts/verify-game-catalog.mjs`, which can compare or
print the canonical catalog directly from these game assets:

```powershell
node scripts/verify-game-catalog.mjs --unity-root '<absolute Unity project path>'
# Add --export to print reviewed JSON without writing either repository.
```

The website's `docs/game-wallet-rollout.md` contains the additive database migration,
restricted-role verifier, feature flags, coordinated legacy-handler cutover,
immutable old-payment compatibility and manual recovery procedure. Follow it with
the main-site developer. Do not rerun the original currency schema, delete receipts,
reset balances, or simply enable the feature without those checks.

Before a release, test two accounts, guest login, expired sessions, browser account
mismatch, both test-payment packs, return/restart refresh, offline queued rewards,
duplicate notifications, old saves, material/cosmetic ownership recovery and
multiplayer visitor protection. Use the actual Android build as well as Editor
testing; source compilation is not a substitute for device/browser acceptance.
