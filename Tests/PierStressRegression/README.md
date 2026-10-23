# Pier stress regression checks

Run from the Unity project root:

```powershell
dotnet run --project Tests/PierStressRegression/PierStressRegression.csproj
```

The original nine solver scenarios remain intact. Additional Concrete Pier checks load the current SO's numeric fields and execute the actual production material math and deterministic solver. They verify the approved provisional gameplay preset (400,000 N tension / 2,100,000 N compression, 900 cost / 240 kg per metre), retained 30 m placement/buckling settings, 10/20/30 m effective compression, a 3,000 kg supported-pier fixture, and a deliberate 40,000 kg tall-pier overload. The old 3,000 N placeholder is also checked against the same fixture so strength changes cannot be mistaken for disabled failure detection.

Mass checks call the production `GetPlacedMassPerMeter()` method to verify the single pier stays at 240 kg/m. Comparison asset values are checked without editing them: paired Concrete Beam 180 kg/m and 1,200 cost/m, Concrete Road 180 kg/m and 650 cost/m, and Wood Pier 182.5 kg/m and 500 cost/m. Cost comparisons check the asset values and the explicit dual-member multiplier used by `Bar.GetCost`; they do not exercise purchase/billing code. An in-memory massless copy with identical strength/buckling parameters confirms that the real solver increases pier utilization from self-weight, while the approved 3,000 kg fixture remains supported.

The isolated deck is deliberately massless and non-limiting; this tests pier behavior rather than certifying a complete bridge design. These are gameplay balance regressions, not real-world engineering ratings or a substitute for a Unity/Marsh crossing playtest. No scene, asset, player save, network, website, or account is modified by the runner.
