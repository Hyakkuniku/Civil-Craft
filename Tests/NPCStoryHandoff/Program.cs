using UnityEngine;

int checks = 0;
void Assert(bool condition, string message) { checks++; if (!condition) throw new InvalidOperationException(message); }
NPCStoryHandoff Fresh()
{
    PlayerDataManager.Instance = new(); TutorialManager.Instance = new(); NPCSpawnConditionMonitor.Hidden.Clear();
    var npc = new NPCProgressionManager
    {
        Phases = new() { new("Shopkeeper"), new("Go to Shopkeeper"), new("CollectMap"), new("Farewell"), new("Find Reed"), new("Delete") },
        CurrentPhaseIndex = 1
    };
    return new NPCStoryHandoff { targetNPC = npc, returnTutorial = new() };
}
void NoHandoff(Action<NPCStoryHandoff> alter, string description)
{
    NPCStoryHandoff handoff = Fresh(); NPCProgressionManager target = handoff.targetNPC; TutorialSequence sequence = handoff.returnTutorial;
    alter(handoff); handoff.TryHandoff();
    Assert(target.MoveCalls == 0 && sequence.Starts == 0, description);
}

NPCStoryHandoff first = Fresh();
first.TryHandoff();
Assert(first.targetNPC.MoveCalls == 1 && first.targetNPC.LastMove == "CollectMap", "First shop handoff moves forward to CollectMap.");
Assert(first.returnTutorial.Starts == 1, "First handoff starts the valid route even before NPC arrival.");
Assert(first.targetNPC.CurrentPhase.phaseId == "Go to Shopkeeper" && first.targetNPC.IsTravelling, "Boundary preserves the real coroutine's phase timing.");
first.TryHandoff(); first.TryHandoff();
Assert(first.targetNPC.MoveCalls == 1 && first.returnTutorial.Starts == 1, "Repeating while walking does not reset travel or rebuild/queue the route.");
first.targetNPC.Arrive("CollectMap");
first.TryHandoff();
Assert(first.targetNPC.MoveCalls == 1 && first.returnTutorial.Starts == 1, "Existing CollectMap route is left alone.");
TutorialManager.Instance.Active = null; first.TryHandoff();
Assert(first.targetNPC.MoveCalls == 1 && first.returnTutorial.Starts == 2, "A valid CollectMap retry restores guidance without another movement.");
PlayerDataManager.Instance.CurrentData.Features.Add("minimap"); TutorialManager.Instance.Active = null;
first.TryHandoff();
Assert(first.targetNPC.MoveCalls == 1 && first.returnTutorial.Starts == 2, "Collecting the map permanently ends this handoff.");

NoHandoff(h => h.isActiveAndEnabled = false, "Disabled sending helper does nothing.");
NoHandoff(h => h.gameObject.scene.valid = false, "Prefab/unattached sending helper does nothing.");
NoHandoff(h => h.gameObject.scene.isLoaded = false, "Unloaded sending scene does nothing.");
NoHandoff(h => h.targetNPC = null, "Missing target does nothing.");
NoHandoff(h => h.targetNPC.isActiveAndEnabled = false, "Disabled target NPC does nothing.");
NoHandoff(h => h.targetNPC.gameObject.activeInHierarchy = false, "Despawned NPC does not receive a route or phase request.");
NoHandoff(h => h.targetNPC.gameObject.scene.valid = false, "Prefab target does nothing.");
NoHandoff(h => h.targetNPC.gameObject.scene.isLoaded = false, "Unloaded target scene does nothing.");
NoHandoff(h => NPCSpawnConditionMonitor.Hidden.Add(h.targetNPC.transform), "Authored-active NPC awaiting spawn rules is not a valid destination.");
NoHandoff(h => h.targetNPC.IsTravelling = true, "Already travelling NPC is not redirected.");
NoHandoff(h => PlayerDataManager.Instance = null, "Missing player manager does nothing.");
NoHandoff(h => PlayerDataManager.Instance.CurrentData = null, "Loading player data does nothing.");
NoHandoff(h => PlayerDataManager.Instance.CurrentData.Features.Add("minimap"), "Existing map unlock prevents old handoff.");
NoHandoff(h => TutorialManager.Instance = null, "Missing tutorial manager leaves phase unchanged.");
NoHandoff(h => h.returnTutorial = null, "Missing return tutorial leaves phase unchanged.");
foreach (string invalid in new[] { null, "", "  " })
{
    NoHandoff(h => h.eligiblePhaseId = invalid, "Blank eligible identity is rejected.");
    NoHandoff(h => h.destinationPhaseId = invalid, "Blank destination identity is rejected.");
    NoHandoff(h => h.completionFeatureId = invalid, "Blank completion identity is rejected.");
}
NoHandoff(h => h.eligiblePhaseId = h.destinationPhaseId, "A same-phase handoff is rejected.");
NoHandoff(h => h.eligiblePhaseId = "Missing", "Missing eligible phase is rejected.");
NoHandoff(h => h.destinationPhaseId = "Missing", "Missing destination phase is rejected.");
NoHandoff(h => h.targetNPC.Phases = null, "Missing phase list is rejected.");
NoHandoff(h => h.targetNPC.Phases.Clear(), "Empty phase list is rejected.");
NoHandoff(h => h.targetNPC.Phases.Add(new("Go to Shopkeeper")), "Ambiguous eligible phase is rejected.");
NoHandoff(h => h.targetNPC.Phases.Add(new("CollectMap")), "Ambiguous destination phase is rejected.");
NoHandoff(h => { h.eligiblePhaseId = "CollectMap"; h.destinationPhaseId = "Go to Shopkeeper"; h.targetNPC.CurrentPhaseIndex = 2; }, "Misconfigured backward handoff is rejected.");
foreach (int phase in new[] { -1, 0, 3, 4, 5, 99 })
    NoHandoff(h => h.targetNPC.CurrentPhaseIndex = phase, "Earlier, later, deleted or invalid phase is never rewound.");

NPCStoryHandoff resumed = Fresh(); resumed.targetNPC.Arrive("CollectMap"); resumed.TryHandoff();
Assert(resumed.targetNPC.MoveCalls == 0 && resumed.returnTutorial.Starts == 1, "A restored CollectMap save can recover valid navigation.");
NoHandoff(h => { h.targetNPC.Arrive("Find Reed"); NPCSpawnConditionMonitor.Hidden.Add(h.targetNPC.transform); }, "A completed/despawned Bhan stays completed after restart.");
// The move itself is authorized above; these cases must only suppress its route.
NPCStoryHandoff despawnedDuringMove = Fresh();
despawnedDuringMove.targetNPC.OnMove = () => despawnedDuringMove.targetNPC.gameObject.activeInHierarchy = false;
despawnedDuringMove.TryHandoff();
Assert(despawnedDuringMove.targetNPC.MoveCalls == 1 && despawnedDuringMove.returnTutorial.Starts == 0,
    "A synchronous arrival/despawn callback does not start an obsolete route.");
NPCStoryHandoff movedAway = Fresh();
movedAway.targetNPC.OnMove = () => movedAway.targetNPC.Arrive("Farewell"); movedAway.TryHandoff();
Assert(movedAway.targetNPC.MoveCalls == 1 && movedAway.returnTutorial.Starts == 0, "An immediate later-phase callback does not create stale guidance.");
NPCStoryHandoff switched = Fresh(); switched.targetNPC.OnMove = () => PlayerDataManager.Instance = new(); switched.TryHandoff();
Assert(switched.returnTutorial.Starts == 0, "A player-manager replacement during movement does not receive old guidance.");
NPCStoryHandoff completedDuringMove = Fresh();
completedDuringMove.targetNPC.OnMove = () => PlayerDataManager.Instance.CurrentData.Features.Add("minimap"); completedDuringMove.TryHandoff();
Assert(completedDuringMove.returnTutorial.Starts == 0, "A feature unlocked by the arrival callback suppresses obsolete guidance.");
NPCStoryHandoff unrelated = Fresh(); TutorialSequence other = new(); TutorialManager.Instance.Active = other; unrelated.TryHandoff();
Assert(unrelated.returnTutorial.Starts == 1, "A valid handoff uses the existing tutorial entry point even when another sequence must finish first.");
Assert(unrelated.targetNPC.Phases.Count == 6 && unrelated.targetNPC.Phases[5].phaseId == "Delete", "No authored phase labels or list entries are changed by runtime handoff.");

foreach (Action<NPCStoryHandoff> obsolete in new Action<NPCStoryHandoff>[]
{
    h => h.targetNPC.gameObject.activeInHierarchy = false,
    h => NPCSpawnConditionMonitor.Hidden.Add(h.targetNPC.transform),
    h => h.targetNPC.Arrive("Find Reed"),
    h => PlayerDataManager.Instance.CurrentData.Features.Add("minimap")
})
{
    NPCStoryHandoff owned = Fresh(); TutorialManager.Instance.Active = owned.returnTutorial; obsolete(owned); owned.TryHandoff();
    Assert(TutorialManager.Instance.Cancellations == 1 && TutorialManager.Instance.Active == null && owned.targetNPC.MoveCalls == 0,
        "An already-active obsolete return tutorial is cancelled without completing it.");
    NPCStoryHandoff notOwned = Fresh(); TutorialSequence otherGuide = new(); TutorialManager.Instance.Active = otherGuide;
    obsolete(notOwned); notOwned.TryHandoff();
    Assert(TutorialManager.Instance.Cancellations == 0 && ReferenceEquals(TutorialManager.Instance.Active, otherGuide),
        "An unrelated active tutorial is preserved when the handoff is obsolete.");
}
NPCStoryHandoff walkingOwned = Fresh(); TutorialManager.Instance.Active = walkingOwned.returnTutorial;
walkingOwned.targetNPC.IsTravelling = true; walkingOwned.TryHandoff();
Assert(TutorialManager.Instance.Cancellations == 0 && ReferenceEquals(TutorialManager.Instance.Active, walkingOwned.returnTutorial),
    "A valid walking-time handoff keeps its existing route.");
Console.WriteLine($"PASS: {checks} linked production NPCStoryHandoff checks. Scene/save/tutorial systems are boundary doubles; no live data or scenes were accessed.");
