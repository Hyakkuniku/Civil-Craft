# Saved road anchor compatibility

Run `dotnet run --project Tests/SavedRoadAnchorCompatibility/SavedRoadAnchorCompatibility.csproj`.

Extracts the production `BuildLocation` compatibility field and both matching methods, plus `PlayerDataManager.DoesLoadedBridgeMatch`. Unity points, vectors and saved records are boundary doubles; no live scene or player save is touched.

Checks the default strict matching, explicit per-site opt-in, permanent road anchor identity, unchanged X/Z within one centimetre, vertical limit, ambiguous and foreign nodes, invalid numeric values, unchanged saved data, and photo verification for both bar endpoint orders. Interior point and material changes still fail verification. Source guards preserve the original reconstruction matching order before the compatibility fallback.
