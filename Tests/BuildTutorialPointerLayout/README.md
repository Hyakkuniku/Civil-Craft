# Build arrow placement

`dotnet run --project Tests/BuildTutorialPointerLayout`

Links the production pure pixel-space placement helper. Verifies safe-area bounds,
full bounce envelopes, separation from control rectangles, edge flipping and
zero-offset Undo behavior across phone/tablet/wide layouts. Native UGUI rendering,
actual scene bindings and shared-pointer ownership are separate checks.
