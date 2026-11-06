# Material toolbar ordering

Run `dotnet run --project Tests/MaterialToolbarOrdering/MaterialToolbarOrdering.csproj`.

The harness compiles the complete production `MaterialToolbarOrdering` source, the production button availability property and authored-index cache, and the production `BuildUIController.RefreshAllMaterialButtons` method. Only Unity hierarchy/layout objects and restriction-service boundaries are doubled.

Checks cover availability before category, Road → Beam → Pier → Rope order, original category ties, exhausted quotas and undo/unlock recovery, inactive wrappers, nonmaterial sibling slots, independent parents/scenes, exact touch-child references, invalid bindings, reparented baselines, idempotent layout updates, and contract-only scroll resets. It never opens Unity or changes scenes, player saves, account data or network state.
