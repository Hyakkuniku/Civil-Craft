# Unity game wallet checks

Run the isolated, dependency-free policy checks:

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' run --project Tests/GameWalletPolicy/GameWalletPolicy.csproj
```

These tests do not start Unity, call PlayFab/the website, change balances, read credentials, or modify player saves. They cover approved-save/account/generation isolation, guest/visitor and expired-session denial, canonical reward IDs, request UUIDs, exact numeric bounds, stale receipt version denial, one-time migration gates, terminal rejection proof, and official HTTPS shop links with bounded expiry.

Payout regression checks also exercise the same pure resolver used by NPC Collect, objective reopening, and contract completion: persisted attempt quotes win over volatile/base fallbacks, real zero Gold/EXP remain zero, and legacy jobs without captured failure history are held locally rather than submitted with invented evidence. A held record preserves the original displayed quote and EXP for reviewed recovery; it does not authorize a Coin grant and must not be posted or retried as a normal reward.

`GameWalletService` is attached to persistent `PlayerDataManager`. It calls the main website only, authenticates with the current PlayFab session ticket in an HTTP header, and never puts credentials in a browser URL. The approved account's encrypted `WalletJournals/<title>/<account>.json` retains reward and purchase IDs outside the replaceable story save. Corrupt journals disable online spending and are not silently reset. The server wallet and entitlements remain authoritative; the save's `gold` is only their signed-in Coins cache.

Manual gameplay acceptance remains required after server/database deployment:

1. Guest plus buttons show the authored Sign In / Not Now dialog; cancellation does not leave the game. Sign In preserves the guest save and uses the existing Main Menu authentication.
2. Signed-in plus buttons show Cancel / Open Shop, request an account-bound shop link, and open only the official main website. Log in to the same receiving account and use only the documented simulated payment flow.
3. Login, shop opening, app resume, and the authored Refresh control synchronize Coins/Diamonds. Failed reads show Unavailable instead of fabricated zero balances.
4. Signed-in purchases require online confirmation. A pending purchase keeps the same UUID across reconnect/restart; recovered ownership never causes another debit. Guests retain local offline purchases.
5. Earn contract/achievement rewards offline, reconnect, and verify each deterministic event is credited once. Restore an older story save and confirm the server balance does not roll back or import again.
6. Switch accounts during requests, test cloud-save choices and multiplayer host visits, and verify no stale callback or host progression reaches another account's wallet.

Compilation alone does not verify the deployed API, database setup, Unity scene wiring, actual browser account binding, or real gameplay balance flow. Do not use production-player monetary probes as automated test fixtures.
