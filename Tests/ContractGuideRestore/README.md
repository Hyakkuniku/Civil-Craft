# Restored contract navigation

Run `dotnet run --project Tests/ContractGuideRestore` from the Unity project.

This harness compiles production `TutorialManager` contract-navigation recovery, selection, ownership, visibility, route binding and recall methods into an isolated assembly. Engine scene discovery, the saved quest list, modal ownership, text/panels and path rendering use explicit boundary doubles. The recovery policy itself is extracted unchanged, not copied into a parallel test implementation.

Scenarios cover restart to an accepted contract or pending turn-in, active-objective selection and fallback, late scene/save/path readiness, profile replacement, stale or ineligible targets, real tutorial and modal deferral, route ownership, and recall. Source guards forbid saving, rewards or progression from navigation recovery. No live scene, player save, account, navigation mesh or network is accessed.
