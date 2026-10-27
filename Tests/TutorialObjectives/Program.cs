using UnityEngine;

int checks = 0;
void Assert(bool condition, string message) { checks++; if (!condition) throw new InvalidOperationException(message); }
(TutorialObjectiveProjection projection, TutorialManager manager, PlayerData data) Fresh()
{
    PlayerDataManager.Instance = new(); PathGuider.Instance = new(); FusionConnectionManager.Instance = null;
    NPCSpawnConditionMonitor.Hidden.Clear(); Transform.searches = 0;
    TutorialManager manager = new()
    {
        sequence = new TutorialSequence { lessonName = "FollowProfessorBhan", showInObjectives = true,
            tutorialSteps = new[] { new TutorialStep { message = "Follow <b><#E09500>Professor Bhan</color></b>\n" },
                new TutorialStep { message = "Speak to <b>Professor Bhan</b>." } } }
    };
    manager.sequence.name = "Follow sequence";
    TutorialManager.Instance = manager;
    return (new TutorialObjectiveProjection(), manager, PlayerDataManager.Instance.CurrentData);
}

var f = Fresh();
var contract = new TrackedTask { title = "Follow Professor Bhan", description = "Contract stays", contractName = "CONTRACT42",
    budget = 500, weight = 700, pendingGold = 150, pendingExp = 30, isReadyToTurnIn = true };
f.data.activeQuests.Add(contract); var savedList = f.data.activeQuests;
Assert(f.projection.Sync(f.manager, f.data), "Checked active tutorial produces a runtime objective.");
TrackedTask first = f.projection.Task;
Assert(first.title == "Follow Professor Bhan", "Default title strips TMP styling and trailing line breaks.");
Assert(first.description == "Follow <b><#E09500>Professor Bhan</color></b>", "Current instruction preserves rich text and trims whitespace.");
Assert(first.isTutorial && first.contractName == "" && first.targetWaypointName == "", "Tutorial row has no contract or name-based navigation identity.");
Assert(first.budget == 0 && first.weight == 0 && first.pendingGold == 0 && first.pendingExp == 0 && !first.isReadyToTurnIn && !first.isCompleted,
    "Projection cannot supply rewards, contract weights, budget, turn-in or completion.");
Assert(f.projection.Key.StartsWith("tutorial:Assets/Scenes/CanyonCrossing.unity|FollowProfessorBhan|/Follow%20sequence["),
    "Projection uses the reload-stable scene, lesson and hierarchy identity.");
Assert(f.projection.Owns(first) && !f.projection.Owns(contract) && !f.projection.Owns(null), "Ownership uses the row reference, not a colliding title.");
Assert(ReferenceEquals(f.data.activeQuests, savedList) && f.data.activeQuests.Count == 1 && f.data.activeQuests[0] == contract,
    "Projection never inserts or replaces saved quests.");
Assert(contract.description == "Contract stays" && contract.isReadyToTurnIn && contract.budget == 500 && contract.pendingGold == 150,
    "Same-title contract remains untouched.");
Assert(f.data.activeObjectiveKey == "contract:existing" && f.data.coins == 75 && f.data.experience == 200 &&
    !f.data.hasUnlockedObjectiveTracker && !f.data.hasUnreadObjectiveAlert && f.data.completedLessons.Count == 0,
    "Projection never changes selection, wallet, tracker unlocks, alerts or lesson completion.");
Assert(!f.projection.Sync(f.manager, f.data) && f.projection.Task == first, "An unchanged frame reuses content and its row.");
string oldDescription = first.description; string oldTitle = first.title;
for (int i = 0; i < 60; i++) Assert(!f.projection.Sync(f.manager, f.data), "Repeated unchanged frame does not report changes.");
Assert(ReferenceEquals(first.description, oldDescription) && ReferenceEquals(first.title, oldTitle), "Unchanged frames do not rebuild formatted strings.");
f.manager.CurrentStepIndex = 1;
Assert(f.projection.Sync(f.manager, f.data) && f.projection.Task == first && first.description == "Speak to <b>Professor Bhan</b>.",
    "Advancing updates the instruction in the same row.");
Assert(first.title == "Follow Professor Bhan", "Title remains the first instruction across steps.");
f.manager.CurrentStepIndex = 0;
Assert(f.projection.Sync(f.manager, f.data) && f.projection.Task == first, "Returning to an earlier step updates without duplicates.");
f.manager.sequence.objectiveTitle = "  <b>Follow</b>\n Bhan  ";
Assert(f.projection.Sync(f.manager, f.data) && first.title == "Follow Bhan", "Authored title override is readable plain text.");
f.manager.CurrentStep.message = "Use <b>Almanac</b>.";
Assert(f.projection.Sync(f.manager, f.data) && first.description == "Use <b>Almanac</b>.", "Authored instruction edits refresh active content.");
f.manager.CurrentStep.message = null;
Assert(f.projection.Sync(f.manager, f.data) && first.description == first.title, "Blank pointer-only instruction retains a readable objective description.");

foreach (Action<TutorialManager> invalidate in new Action<TutorialManager>[] {
    m => m.active = false, m => m.sequence.showInObjectives = false, m => m.CurrentStepIndex = 99,
    m => m.sequence = null, m => m.sequence.tutorialSteps = null, m => m.sequence.tutorialSteps[0] = null,
    m => m.isActiveAndEnabled = false, m => m.gameObject.activeInHierarchy = false,
    m => m.gameObject.scene.valid = false, m => m.gameObject.scene.isLoaded = false,
    m => m.sequence.gameObject.scene.valid = false, m => m.sequence.gameObject.scene.isLoaded = false })
{
    f = Fresh(); f.projection.Sync(f.manager, f.data); var original = f.projection.Task;
    invalidate(f.manager);
    Assert(f.projection.Sync(f.manager, f.data) && f.projection.Task == null && f.projection.Key == "", "Inactive/cancelled/suspended/disabled/invalid tutorial drops its projection.");
    Assert(!f.projection.Owns(original) && !original.isCompleted && f.data.completedLessons.Count == 0, "Removing a projection does not award completion.");
    Assert(!f.projection.Sync(f.manager, f.data), "Repeated absent state does not repeatedly report removal.");
}
f = Fresh(); f.projection.Sync(f.manager, f.data); first = f.projection.Task;
f.manager.active = false; f.projection.Sync(f.manager, f.data); f.manager.active = true;
Assert(f.projection.Sync(f.manager, f.data) && f.projection.Task != first, "Resuming reconstructs one transient row without executing the tutorial.");
Assert(f.manager.Recalls == 0 && f.data.activeQuests.Count == 0, "Resuming does not replay navigation or write objectives.");
f = Fresh(); f.projection.Sync(f.manager, f.data);
Assert(f.projection.Sync(null, f.data) && f.projection.Task == null, "Missing tutorial manager clears its row.");
f = Fresh(); f.projection.Sync(f.manager, f.data);
Assert(f.projection.Sync(f.manager, null) && f.projection.Task == null, "Missing player data clears its row.");
f = Fresh(); f.projection.Sync(f.manager, f.data); first = f.projection.Task;
var profile2 = new PlayerData(); PlayerDataManager.Instance.CurrentData = profile2;
Assert(!f.projection.CanNavigate && f.projection.Sync(f.manager, profile2) && f.projection.Task != first, "A profile swap invalidates old ownership and creates a fresh row.");
Assert(profile2.activeQuests.Count == 0 && f.data.activeQuests.Count == 0, "Neither profile receives persistent tutorial tasks.");
Assert(f.projection.Sync(f.manager, f.data) && f.projection.Task == null, "Stale caller data cannot republish the old profile's task.");
f = Fresh(); f.projection.Sync(f.manager, f.data); first = f.projection.Task;
PlayerDataManager.Instance = new() { CurrentData = f.data };
Assert(f.projection.Sync(f.manager, f.data) && f.projection.Task != first, "Replacing the save manager invalidates runtime row ownership.");
f = Fresh(); f.projection.Sync(f.manager, f.data);
FusionConnectionManager.Instance = new() { IsGuestInHostWorld = true };
Assert(f.projection.Sync(f.manager, f.data) && f.projection.Task == null, "Guests do not project host story tutorials.");

f = Fresh(); f.manager.sequence.lessonName = ""; f.projection.Sync(f.manager, f.data);
string fallbackKey = f.projection.Key; first = f.projection.Task;
var another = new TutorialSequence { lessonName = "", showInObjectives = true, tutorialSteps = f.manager.sequence.tutorialSteps };
another.name = f.manager.sequence.name; f.manager.sequence = another;
Assert(f.projection.Sync(f.manager, f.data) && f.projection.Key != fallbackKey && f.projection.Task != first,
    "Same-name anonymous sequences cannot share a runtime identity.");
f = Fresh(); f.projection.Sync(f.manager, f.data); string canonicalKey = f.projection.Key;
f.manager.sequence.gameObject.scene.path = "Assets/OtherScene.unity";
Assert(f.projection.Sync(f.manager, f.data) && f.projection.Key != canonicalKey, "Same lesson IDs in different scenes have different keys.");

f = Fresh(); var target = new GameObject();
f.manager.CurrentStep.stepWaypoints = new() { null, new GuiderWaypoint(), new GuiderWaypoint { target = target.transform, advancesTutorial = true } };
PathGuider.Instance.presence = _ => target.scene.valid && target.scene.isLoaded;
f.projection.Sync(f.manager, f.data);
Assert(f.projection.CanNavigate && f.projection.TryNavigate() && f.manager.Recalls == 1, "Valid authored route can be recalled.");
Assert(ReferenceEquals(f.manager.Recalled, f.manager.CurrentStep.stepWaypoints) && f.manager.CurrentStepIndex == 0 &&
    f.data.completedLessons.Count == 0 && f.data.activeQuests.Count == 0, "Navigation recalls the exact live route without advancing, completing or persisting anything.");
int queries = PathGuider.Instance.queries; Assert(f.projection.CanNavigate && f.projection.CanNavigate && PathGuider.Instance.queries == queries + 2,
    "Availability delegates to the live guider instead of caching stale NPC/pending-waypoint decisions.");
target.activeInHierarchy = false;
Assert(f.projection.CanNavigate, "Ordinary inactive scene markers remain valid destinations.");
target.scene.isLoaded = false;
Assert(!f.projection.CanNavigate && !f.projection.TryNavigate() && f.manager.Recalls == 1, "Unloaded destination cannot navigate.");
target.scene.isLoaded = true; target.scene.valid = false;
Assert(!f.projection.CanNavigate, "Prefab/unattached destination cannot navigate.");

foreach (bool giverOnly in new[] { false, true })
{
    f = Fresh(); var npc = new NPCProgressionManager(); var giver = new NPCContractGiver();
    target = giverOnly ? giver.gameObject : npc.gameObject;
    if (giverOnly) target.components[typeof(NPCContractGiver)] = giver;
    else target.components[typeof(NPCProgressionManager)] = npc;
    // The projection delegates presence to the existing guider. Its actual NPC
    // gate is tested in NPCGuidePresence; this boundary supplies changing results.
    PathGuider.Instance.presence = _ => target.activeInHierarchy &&
        (giverOnly ? giver.isActiveAndEnabled : npc.isActiveAndEnabled) &&
        !NPCSpawnConditionMonitor.Hidden.Contains(target.transform);
    f.manager.CurrentStep.stepWaypoints = new() { new GuiderWaypoint { target = target.transform } };
    f.projection.Sync(f.manager, f.data);
    Assert(f.projection.CanNavigate, "Present NPC route is available.");
    target.activeInHierarchy = false;
    Assert(!f.projection.CanNavigate && !f.projection.TryNavigate(), "Despawned NPC route is unavailable.");
    target.activeInHierarchy = true;
    NPCSpawnConditionMonitor.Hidden.Add(target.transform);
    Assert(!f.projection.CanNavigate, "Pending/unconfirmed NPC spawn cannot navigate.");
    NPCSpawnConditionMonitor.Hidden.Clear();
    if (giverOnly) giver.isActiveAndEnabled = false; else npc.isActiveAndEnabled = false;
    Assert(!f.projection.CanNavigate, "Disabled NPC route is unavailable.");
    if (giverOnly) giver.isActiveAndEnabled = true; else npc.isActiveAndEnabled = true;
    Assert(f.projection.CanNavigate, "Respawned NPC restores navigation without synchronizing a new task.");
    f.manager.CurrentStep.stepWaypoints.Add(new GuiderWaypoint { target = new GameObject().transform });
    target.activeInHierarchy = false;
    Assert(!f.projection.CanNavigate, "A later valid waypoint cannot bypass the hidden first NPC.");
}
f = Fresh();
f.manager.CurrentStep.stepWaypoints = new() { new GuiderWaypoint { target = new GameObject().transform },
    new GuiderWaypoint { target = new GameObject().transform } };
f.projection.Sync(f.manager, f.data);
PathGuider.Instance.presence = route => ReferenceEquals(route, f.manager.CurrentStep.stepWaypoints);
Assert(f.projection.CanNavigate && f.projection.TryNavigate(), "Projection passes the complete authored route to the guider's pending-waypoint policy.");
PathGuider.Instance.available = false;
Assert(!f.projection.CanNavigate && !f.projection.TryNavigate(), "Changing guider availability immediately invalidates navigation without a new Sync.");
f = Fresh(); f.projection.Sync(f.manager, f.data);
Assert(!f.projection.CanNavigate && !f.projection.TryNavigate(), "No waypoints means no navigate action.");
f.manager.CurrentStep.stepWaypoints = new() { new GuiderWaypoint { target = new GameObject().transform } };
PathGuider.Instance = null; Assert(!f.projection.CanNavigate, "Missing route renderer disables navigation.");
PathGuider.Instance = new(); f.manager.CurrentStepIndex = 1;
Assert(!f.projection.TryNavigate() && f.manager.Recalls == 0, "A stale row cannot recall a newly advanced step before Sync.");
f.manager.CurrentStepIndex = 0; TutorialManager.Instance = new();
Assert(!f.projection.TryNavigate(), "A replaced tutorial manager invalidates old navigation ownership.");
TutorialManager.Instance = f.manager; FusionConnectionManager.Instance = new() { IsGuestInHostWorld = true };
Assert(!f.projection.TryNavigate(), "Changing to a guest session invalidates navigation immediately.");
FusionConnectionManager.Instance = null; PlayerDataManager.Instance = null;
Assert(!f.projection.TryNavigate(), "Missing profile owner invalidates navigation immediately.");
f.projection.Clear(); f.projection.Clear();
Assert(f.projection.Task == null && f.projection.Key == "" && !f.projection.CanNavigate, "Clear is idempotent and has no completion side effects.");

f = Fresh(); f.manager.sequence.ConfigureObjectiveIdentity("bhan-first-walk");
var recorded = TutorialObjectiveHistory.Begin(f.manager.sequence, f.data);
Assert(f.projection.Sync(f.manager, f.data) && ReferenceEquals(f.projection.Task, recorded),
    "An active guide projects its existing saved row instead of duplicating it.");
Assert(f.data.activeQuests.Count == 1 && !recorded.isCompleted, "Viewing a saved guide does not complete it.");
f.manager.active = false; f.projection.Sync(f.manager, f.data);
Assert(f.projection.Task == null && f.data.activeQuests.Contains(recorded) && !recorded.isCompleted,
    "Suspension/cancellation clears only live ownership; the unfinished saved row remains.");
Assert(TutorialObjectiveHistory.Complete(f.manager.sequence, f.data) && recorded.isCompleted,
    "Genuine tutorial completion crosses out the existing saved row.");
string savedDescription = recorded.description;
f.manager.active = true; f.manager.CurrentStepIndex = 1; f.projection.Sync(f.manager, f.data);
Assert(f.projection.Task != recorded && !f.projection.Task.isCompleted && recorded.isCompleted,
    "A repeated walk has an active overlay while the earlier completed history remains complete.");
Assert(recorded.description == savedDescription && f.data.activeQuests.Count == 1,
    "Replay instructions do not overwrite or duplicate completed history.");
f.projection.Clear();
Assert(recorded.isCompleted && f.data.activeQuests.Contains(recorded), "Closing or disabling Objectives preserves completed history.");

Console.WriteLine($"PASS: {checks} linked production tutorial objective projection checks. Scene, saves, tutorial execution and navigation are boundary doubles; no live data changed.");
