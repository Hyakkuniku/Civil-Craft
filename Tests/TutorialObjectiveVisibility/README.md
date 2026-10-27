# Tutorial Objectives HUD restoration

Run `dotnet run --project Tests/TutorialObjectiveVisibility/TutorialObjectiveVisibility.csproj`.

Compiles the actual Objectives entry visibility method, lesson close/Canvas restore,
and coordinator close/unwind/restore methods. The key regression is closing Live
Load inspection while its tutorial projection is removed: an existing persisted
Objectives unlock must keep the HUD button available before the first contract.
Also covers ordinary locking, contract offers, nested modals, repeated closes, and
reload without an active tutorial. Tutorial lifecycle registration and saving are
separate integration boundaries; this suite does not test or change player saves.
Unity objects, opening/capturing a modal, and control restoration are test doubles.
