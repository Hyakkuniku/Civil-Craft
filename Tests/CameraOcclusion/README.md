# Camera-safe occlusion regression checks

Run `dotnet run --project Tests/CameraOcclusion/CameraOcclusion.csproj` from the Unity project root.

The runner compiles the current, complete `CanyonOcclusionProfile` and `CanyonCameraOcclusionPolicy` source files against small Unity boundary doubles. It exercises actual policy code rather than recreating the eligibility rules in the test. Build outputs stay in this suite's ignored `bin`/`obj` folders; no existing tracked test outputs are touched.

Coverage includes camera ownership and scene identity, no-bake/default-off behavior, build/simulation/loading/transition modes, multiple excluded cameras, both rendering callback paths, native flag-only mutations, rotated/scaled view areas, malformed/nonfinite bounds, nested/duplicate/destroyed suspension owners, disabled and removed components, and domain/scene-reload-disabled session resets. Structural guards also check replacement minimap/portrait cameras, recording/cinematic releases, exact-bit Editor setup, generated-prop preservation, dirty-scene rejection, recoverable backup placement, and verified-bake saving.

These tests do **not** establish that Unity's native baked visibility is correct or that Android is faster. Run the Editor/native rendering checks and the same-route device A/B procedure described in `Docs/CameraSafeOcclusion.md` before enabling the rollout switch. No player saves, authentication, websites, or network calls are used by this suite.
