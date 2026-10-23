# Isolated Concrete Pier load sweep

Run from the Unity project root:

```powershell
dotnet run --project Tests/ConcretePierLoadSweep/ConcretePierLoadSweep.csproj
```

This read-only gameplay diagnostic compiles the actual production `BridgeMaterialSO` and `DeterministicBridgeStressSolver` with the existing minimal engine boundary from `PierStressRegression`. It reads the current Concrete Pier asset and requires the approved preset to stay unchanged: 400,000 N tension, 2,100,000 N base compression, single 240 kg/m mass, 900 cost/m, 30 m maximum placement length and buckling reference/exponent/floor of 10 m / 2 / 0.15.

## Fixture and output

The graph has fixed banks at (-5,0) and (5,0), one free shared deck joint at (0,0), and one pier base at (0,-height). Two 5 m deck bars are deliberately massless and non-limiting (1,000,000,000 N tension/compression). The actual lower-pier-end inference supplies the foundation; the upper endpoint is not an authored anchor. The real 240 kg/m column self-weight remains active.

Eight heights (10, 20, 25, 26, 27, 28, 29 and 30 m) are tested against seven nominal point-vehicle loads (3, 5, 10, 15, 20, 30 and 40 tonnes). Eleven route samples include the center joint. Each case prints pier utilization, dead-only utilization, overall member peak, stiffness stability and whether 60% / 100% utilization is reached. All cases repeat sample-for-sample. A binary search brackets the nominal vehicle mass where the actual solver's rounded output first reaches each threshold, to within one kilogram.

## Boundaries

These are **isolated gameplay capacity tests, not real-world engineering ratings** or a full bridge balance approval. “Stable” means the solver matrix has full rank; it does not certify that a vehicle completes a native PhysX crossing. The percentage is axial force/capacity, rounded by the production solver. The threshold is not a legal or physical safe load rating. The deliberately massless/non-limiting deck excludes a real deck/truss's weight, load sharing, geometry and deformation. Use the actual curved saved-bridge replay and device playtest for level balance.

The runner verifies the full Concrete Pier asset SHA-256 is unchanged. No live asset, scene, player save, account or network is modified. No balancing values are applied by this task.
