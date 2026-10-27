# Newly unlocked Shop pointer

Run `dotnet run --project Tests/ShopUnlockPointer` from the Unity project.

The suite compiles the complete production `ShopButtonTrigger` against isolated engine, button, save-profile, modal and `TutorialPointer` boundaries. It checks restored/new unlock eligibility, completed Shop introductions, modal and gameplay guards, dedicated clone ownership/reuse, source/profile replacement, cleanup, and existing button click behavior.

Pointer geometry and real graphic raycasting remain separate Unity rendering checks. Source guards preserve click-through setup and prohibit introducing save, purchase, tutorial-completion or shared-pointer mutations. No live scene, account, persistent save, shop transaction or network is accessed.
