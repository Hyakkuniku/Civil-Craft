# Offline tutorial Safety Vest regression checks

Run from the Unity repository:

```powershell
dotnet run --project Tests/TutorialSafetyVest/TutorialSafetyVest.csproj --verbosity quiet
```

The harness extracts and compiles current production claim, purchase, ownership,
confirmation, card UI, tutorial advancement/exit, wallet reconciliation and
wardrobe-save methods. It uses actual PlayerData, ShopItemData and cosmetic data;
Unity presentation, disk commits and network calls are disposable boundaries.
It does not touch real saves, credentials, player accounts or online balances.

Coverage includes zero funds, signed-in online/offline and guest claims,
confirmation before grant, one atomic ownership save, unchanged Coin cache and
spending statistics, no paid request/deferred debit, repeat claims, partial legacy
ownership repair, null collections, failed-save rollback and tutorial exit/retry,
protected multiplayer guests, unresolved paid requests, exact item whitelisting,
ordinary paid-item regressions, serialized reload, reconnect entitlement merge,
explicit equip/save-look, and account isolation.

## Unity device check

1. Use a fresh guest save or a signed-in account that has not claimed the vest.
2. After loading the signed-in account, disconnect the network. A fresh online
   sign-in still needs a connection; this change does not implement offline login.
3. Open the shop tutorial with any Coin balance, including zero. The vest should
   show **FREE**; other signed-in paid items remain unavailable offline.
4. Confirm the claim. Coins must not change. Follow the Almanac wardrobe steps,
   equip the vest and choose **SAVE LOOK** to finish the lesson.
5. Restart using the same save: the vest stays **OWNED** and equipped. Reconnect:
   wallet refresh must not debit its catalog price or remove ownership.
6. Existing owners retain their vest without a refund or second claim.

The catalog asset's 2,500-Coin price is retained. The exact Safety Vest is issued
once as free starter/tutorial equipment by the game, regardless of connectivity;
no other product gains offline spending. Cloud save synchronization carries the
nonmonetary ownership with account progress normally. Choosing an older cloud
save over newer local progress remains an explicit save-conflict choice.
