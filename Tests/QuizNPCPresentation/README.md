# NPC quiz framing regression checks

Run `dotnet run --project Tests/QuizNPCPresentation/QuizNPCPresentation.csproj` from the Unity project root.

The harness extracts the production framing method and executes it against deterministic vector/quaternion boundary maths. It verifies left-side phone/tablet/wide layouts, a portrait upper-speaker region, various NPC headings, clipping bounds, unsupported viewport/FOV/bounds input, and an inset camera viewport. Structural guards check owner resolution, local-camera pairing, recording release, temporary near-clip handling, snapshot restoration, animation fallback, teardown, occlusion ownership, no per-frame discovery and no world/map/save/network mutations.

This does not emulate native Unity animation, physics, render pipelines or scene-specific camera obstruction. Those require the detached Editor visual checks and a fresh native Play/device session described in `Docs/QuizNPCPresentation.md`.
