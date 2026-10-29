# Restored contract navigation

Run `dotnet run --project Tests/ContractGuideRestore` from the Unity project.

This harness compiles production `TutorialManager` contract-navigation recovery, selection, ownership, visibility, route binding and recall methods into an isolated assembly. Engine scene discovery, the saved quest list, modal ownership, text/panels and path rendering use explicit boundary doubles. The recovery policy itself is extracted unchanged, not copied into a parallel test implementation.

Scenarios cover restart to an accepted contract or pending turn-in, active-objective selection and fallback, late scene/save/path readiness, profile replacement, stale or ineligible targets, real tutorial and modal deferral, route ownership, and recall. Source guards forbid saving, rewards or progression from navigation recovery. No live scene, player save, account, navigation mesh or network is accessed.

The Untrack Objective follow-up also executes the production presentation-only hide method. It verifies that the saved hidden-navigation preference suppresses startup restoration, retains an existing route and objective progress, and leaves real tutorial instructions alone. The boundary field defaults to false for every existing scenario.
