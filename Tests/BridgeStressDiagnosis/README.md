# Read-only saved-bridge stress diagnosis

This diagnostic compiles the production material methods, deterministic stress solver, and save decryption code. Only the Unity object boundary is doubled. It decrypts one explicitly selected save in memory, selects one exact contract, and never writes player data, assets, or scenes. Before/after SHA-256 checks protect the input save and referenced material assets.

Run from the project root with an explicit save path:

```powershell
dotnet run --project Tests/BridgeStressDiagnosis/BridgeStressDiagnosis.csproj -- --save "C:/Users/Hyakk/AppData/LocalLow/DefaultCompany/Civil Craft Final/playerSaveData.json" --contract MarshID --load 3000 --samples 101
```

The three comparisons retain the saved geometry and original anchor identity:

1. Actual piers and their current mass.
2. Remove only the pier bars, retaining all original road and beam members.
3. Keep the piers and their stiffness/capacity, but set their mass to zero on in-memory copies only.

All three analyses are repeated and checked sample-for-sample. Separate live-load-only analysis demonstrates that each connected pier takes vehicle load in addition to dead load. Each saved point ID maps to one shared Point object; coincident coordinates do not manufacture connections.

## Marsh diagnosis — 2026-10-21

The selected saved bridge contained 23 points and 37 bars, with recorded cost 303,679.1875 and recorded peak 23.3%. The replay at the scene's 101 samples and contract's 3,000 kg test load reproduced both screenshot results exactly:

| Comparison | Whole-bridge peak | Existing road/beam peak |
| --- | ---: | ---: |
| Actual concrete piers | 23.3% | 14.6% |
| Remove only the two piers | 22.1% | 22.1% |
| Same piers, zero mass in memory | 20.5% | 17.0% |

The actual 23.3% governing member is the **left Concrete Pier**, zero-based saved bar 10: node 21 at (1276.8062, -10) to shared roadway node 3 at (1276.8062, 10.9612). Its height is 20.9612 m, its height-adjusted compression limit is approximately 477,964 N, and its peak compression is approximately 111,366 N at 24% of the route. These capacity figures use the production solver's own quantization helpers. The right pier peaks at 20.0%.

Both piers belong to the connected road component. Their tops are not independently authored anchors; their bottoms are at the configured -10 m foundation height. Their live-load-only peaks correspond to approximately 18.2 kN and 17.5 kN of additional compression, respectively. The original bridge's worst beam drops from 22.1% to 13.9% with piers. Thus the piers carry bridge/vehicle load and relieve the existing frame; the overall peak moves to the left column rather than indicating increased stress throughout the bridge.

The curved geometry is retained in every comparison. This does not establish that a flat alternative would be better. The current height-dependent pier capacity and added self-weight explain the changed governing member without requiring a missing-support diagnosis.

## Tall/heavy load sweep

The later load test is documented in [the combined test report](../ConcretePierLoadSweep/Results-2026-10-21.md). To repeat the hypothetical deeper-foundation sweep on a save that still contains piers, append `--sweep tall-heavy` to the command above.

The latest inspected Marsh save has no piers. To add test-only piers at the two original shared roadway joints and run the sweep without changing that save:

```powershell
dotnet run --project Tests/BridgeStressDiagnosis/BridgeStressDiagnosis.csproj -- --save "C:/Users/Hyakk/AppData/LocalLow/DefaultCompany/Civil Craft Final/playerSaveData.json" --contract MarshID --load 3000 --samples 101 --sweep tall-heavy --test-pier-top-x "1276.8062,1323.3812" --test-pier-base-y -10
```

This mode requires each requested X to uniquely identify an existing non-authored roadway joint. It refuses to add piers when the save already contains any. It lowers only exclusive test foundation nodes equally to reach tallest-pier heights of 25–30 m, retaining the curved deck, the original joints/anchors and the 1.0525 m difference between pier heights. At a 25 m taller pier, the other is 23.9475 m; at 30 m, the other is 28.9475 m. This is a hypothetical deeper foundation, not a change to the authored Marsh site or its current foundation setting. All A/B/C results must be structurally stable and repeat exactly.

## Interpretation limits

This is a replay of the saved successful design using the game's 2D axial-truss model, not a new native PhysX/device capture or a real-world engineering assessment. The solver assumes a foundation at a pier's lower end; it does not query terrain colliders. Runtime peaks can differ if only some load samples are visited, entry loads are partial, members break, geometry changes, or assets/test loads change. Reconstructed forces are approximate because production stress ratios round to 0.1 percentage point. No balance values were changed for this diagnosis.
