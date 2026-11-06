# Bridge Statistics tutorial

Tutorial 1 keeps its real **Open Bridge Statistics** action, then presents six short, authored Continue pages. Each page points to its corresponding readout while construction controls stay locked and the statistics panel stays open:

- **Live Load:** the test vehicle plus its loaded cargo, following the active contract.
- **Road Length:** the total placed roadway, including slopes.
- **Structure:** members and joints in the side-view model; paired visual beams still represent one member.
- **Dead Load:** the weight of the placed bridge materials.
- **Estimated Capacity:** a single-load estimate that updates with the design; simulation verifies the actual crossing.
- **Efficiency Ratio:** estimated capacity divided by dead load, expressing estimated capacity per kg of bridge material.

Estimated Safety is hidden by default and the panel uses its six-row layout. The legacy field and calculations remain compatible with existing scene references. Old statistics tutorial wording, including already-compacted saved objective text, displays the new introduction without the retired term.

The original 28 logical Beam instructions, drawing guides, callbacks, action gates, lesson identity and progression remain in their original order. The six explanation pages sit between the real open action and Grid. No statistics-close requirement is reintroduced. Editor migrations normalize only these recognized pages before their legacy mapping, then reinsert them without duplication.

**Tools → Civil Craft → Tutorials → Bridge Statistics** provides the scoped authoring and read-only validation commands. Applying the update leaves existing unsaved scene edits unsaved. Reports are written to `Logs/bridge-statistics-tutorial.txt`. `Tests/TutorialInstructionCopy` checks display text, terminology, markup and integration. Detached native Beam validation checks the retained action gates and idempotent authoring, and Tutorial Center Layout validation checks the authored pages with the gameplay font on phone/tablet layouts.

7 November 2026 verification: the production text suite passed 252 checks; runtime and Editor sources compiled against Unity's native references. Detached native checks passed 88 Beam action/factory assertions and 59 Truss migration assertions. All 139 authored messages fit the phone, tablet and wide-screen text previews, including all six new explanations. Composite previews passed all 18 explanation/screen combinations at 1600×720, 2048×1280 and 1126×337: full text and six rows visible, Estimated Safety hidden, arrows beside the panel and no card/readout overlap. Reports are in `Logs/bridge-statistics-layout.txt`, `Logs/build-tutorial-pointers.txt` and `Logs/tutorial-instruction-copy.txt`; composite PNGs are in `Temp/BridgeStatisticsGuide`. These are detached Edit Mode checks; actual Play Mode touch/click-through remains a gameplay smoke test.
