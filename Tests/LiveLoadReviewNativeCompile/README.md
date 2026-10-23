# Native-reference compile

`dotnet run --project Tests/LiveLoadReviewNativeCompile`

Uses the most recent Editor Bee response lists and the installed .NET 8 compiler
to compile the complete runtime and Editor sources against actual Unity APIs.
It replaces every output/ref-output argument with ignored
`Temp/LiveLoadReviewNativeCompile` targets, and makes the Editor compile reference
that newly compiled runtime. It does not overwrite Unity's loaded assemblies,
run gameplay, weave Fusion networking, save scenes, or access player saves.

The native in-Editor visual check is separate:
Tools → Civil Craft → Story → Validate Live Load Review Guidance (Read Only).
