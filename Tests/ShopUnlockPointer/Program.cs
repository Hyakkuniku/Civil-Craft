using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

internal static class Program
{
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
    private static object Call(object target, string name, params object[] args)
    {
        try { return target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, args); }
        catch (TargetInvocationException error) { throw error.InnerException; }
    }
    private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
    private sealed class Fixture
    {
        public PlayerDataManager save;
        public GameObject hud;
        public Canvas canvas;
        public Button button;
        public ShopButtonTrigger trigger;
        public ShopManager shop;
        public TutorialManager tutorial;
        public TutorialPointer template;
        public Fixture(bool unlocked = false)
        {
            UnityEngine.Object.all.Clear(); UnityEngine.Object.clones = UnityEngine.Object.destroys = 0;
            Time.timeScale = 1; Time.unscaledTime = 0; Application.isPlaying = true;
            GameManager.Instance = new GameManager(); PauseManager.Instance = null; UIPanelCoordinator.Instance = null;
            FusionConnectionManager.Instance = null; LoadingScreenManager.IsLoading = false;
            DialogueManager.IsAnyDialogueVisible = DialogueManager.IsAnyDialogueActive = false;
            MultiplayerChallengeLobbyUI.IsChallengeActive = false; QuizUIManager.IsAnyQuizActive = false;
            save = new PlayerDataManager(); PlayerDataManager.Instance = save;
            if (unlocked) save.CurrentData.features.Add("shop");
            canvas = new GameObject("Existing gameplay Canvas").AddComponent<Canvas>();
            hud = new GameObject("Existing Shop HUD"); hud.transform.SetParent(canvas.transform, false);
            button = hud.AddComponent<Button>(); trigger = hud.AddComponent<ShopButtonTrigger>();
            shop = new GameObject("Existing Shop manager").AddComponent<ShopManager>(); ShopManager.Instance = shop; shop.Panel.SetActive(false);
            template = Template();
            tutorial = new GameObject("Existing Tutorial manager").AddComponent<TutorialManager>(); TutorialManager.Instance = tutorial; tutorial.SharedPointer = template;
            Set(trigger, "button", button); Set(trigger, "shopManager", shop);
            Call(trigger, "Awake"); Call(trigger, "Start");
        }
        public TutorialPointer Template()
        {
            var item = new GameObject("Existing authored arrow"); item.transform.SetParent(canvas.transform, false);
            TutorialPointer pointer = item.AddComponent<TutorialPointer>(); item.AddComponent<Image>(); item.AddComponent<CanvasGroup>();
            var child = new GameObject("Arrow artwork"); child.transform.SetParent(item.transform, false); child.AddComponent<Image>();
            pointer.Hide(); return pointer;
        }
        public TutorialPointer Arrow => Get<TutorialPointer>(trigger, "unlockPointer");
        public bool Visible => Arrow != null && !Arrow.destroyed && Arrow.gameObject.activeInHierarchy;
        public void Tick() { Time.unscaledTime += .02f; if (trigger.isActiveAndEnabled) Call(trigger, "LateUpdate"); }
        public void Unlock() { save.CurrentData.features.Add("shop"); save.OnFeatureUnlocksChanged?.Invoke(); Tick(); }
    }
    private static void Main()
    {
        Eligibility(); HudEntryEligibility(); PassiveTutorialGuidance(); Blocking(); OwnershipAndLifecycle(); ButtonAndSubscriptions(); SourceGuards();
        Console.WriteLine($"PASS: {checks} full production ShopButtonTrigger assertions/source guards. Engine objects, cloning, pointer positioning and UI events are explicit boundary doubles; actual graphic raycasting remains a Unity check. No live scene, save, account, purchase, reward or network accessed.");
    }
    private static void Eligibility()
    {
        Fixture f = new(); f.Tick();
        Check(!f.hud.activeSelf && f.Arrow == null && UnityEngine.Object.clones == 0, "Locked Shop creates no arrow and keeps the existing button hidden.");
        f.Unlock();
        Check(f.hud.activeSelf && f.Visible && f.Arrow.target == f.button.transform, "Persistent unlock event reveals the existing Shop button and points to its whole RectTransform.");
        Check(!ReferenceEquals(f.Arrow, f.template) && f.Arrow.transform.parent == f.canvas.transform, "The advisory uses a dedicated arrow clone under the actual root Canvas.");
        int clones = UnityEngine.Object.clones; TutorialPointer arrow = f.Arrow;
        for (int i = 0; i < 60; i++) f.Tick();
        Check(ReferenceEquals(f.Arrow, arrow) && UnityEngine.Object.clones == clones, "Normal frames reuse one pointer instead of cloning each update.");
        Check(f.save.CurrentData.completedLessons.Count == 0 && f.shop.opens == 0, "Showing guidance does not open the Shop or mark its introduction complete.");
        f = new(unlocked: true); f.Tick();
        Check(f.Visible, "An old/restarted save with Shop unlocked and unfinished introduction restores guidance without a new unlock event.");
        f.save.CurrentData.completedLessons.Add("Sequence_Shop"); f.Tick();
        Check(!f.Visible, "Completing the real Shop introduction stops unlock guidance.");
        f.save.CurrentData.completedLessons.Remove("Sequence_Shop"); f.Tick();
        Check(f.Visible, "Still-pending introductory guidance remains eligible if completion is not recorded.");
        f.save.CurrentData.completedLessons = null; f.Tick();
        Check(f.Visible, "Old normalized/missing lesson lists are safe and do not falsely claim introduction completion.");
        f = new(unlocked: true); Set(f.trigger, "requirePersistentUnlock", false); f.Tick();
        Check(f.hud.activeSelf && !f.Visible, "The testing-only bypass does not pretend the Shop was persistently unlocked.");
        f = new(unlocked: true); Set(f.trigger, "requiredFeatureId", "  "); f.Tick();
        Check(f.Visible, "Blank feature IDs keep the existing default shop feature behavior.");
    }
    private static void HudEntryEligibility()
    {
        Fixture f = new(unlocked: true);
        Check(ReferenceEquals(DialogueHudButtonGate.boundButton, f.hud) && ReferenceEquals(DialogueHudButtonGate.boundShop, f.trigger),
            "Awake binds the existing Shop HUD object and its actual trigger owner to the shared dialogue mask.");
        Check(f.trigger.CanShowHudEntry, "An unlocked, enabled Shop outside Build Mode is eligible for HUD presentation.");
        // The full mask fixture checks global dialogue visibility; this owner
        // property must continue to express feature/panel intent independently.
        DialogueManager.IsAnyDialogueVisible = true;
        Check(f.trigger.CanShowHudEntry, "Dialogue does not overwrite Shop's owner eligibility or feature unlock.");
        DialogueManager.IsAnyDialogueVisible = false;
        f.shop.Panel.SetActive(true);
        Check(!f.trigger.CanShowHudEntry, "An open Shop panel owns its entry visibility.");
        f.shop.Panel.SetActive(false);
        GameManager.Instance.CurrentState = GameManager.GameState.Building;
        Check(!f.trigger.CanShowHudEntry, "Existing Build Mode suppression remains part of Shop's restoration eligibility.");
        Set(f.trigger, "hideDuringBuildMode", false);
        Check(f.trigger.CanShowHudEntry, "The authored testing override for Build Mode is preserved.");
        Set(f.trigger, "hideDuringBuildMode", true); GameManager.Instance.CurrentState = GameManager.GameState.Normal;
        f.save.CurrentData.features.Remove("shop");
        Check(!f.trigger.CanShowHudEntry, "An expired or switched locked profile cannot expose Shop after dialogue.");
        f.save.CurrentData.features.Add("shop");
        Set(f.trigger, "visibleOutsideBuildMode", false);
        Check(!f.trigger.CanShowHudEntry, "The initial authored visibility intent is retained.");
        Set(f.trigger, "visibleOutsideBuildMode", true);
        f.trigger.enabled = false;
        Check(!f.trigger.CanShowHudEntry, "Disabled Shop triggers cannot restore a stale HUD entry.");
        f.trigger.enabled = true; f.hud.SetActive(false);
        Check(!f.trigger.CanShowHudEntry, "An inactive HUD remains under its existing owner's control.");
    }
    private static void Blocking()
    {
        foreach (string state in new[] { "disabled-option", "locked", "missing-data", "intro-complete", "shop-open", "shop-missing", "shop-panel-missing", "shop-foreign", "button-disabled", "button-noninteractable", "parent-noninteractable", "canvas-disabled", "canvas-hidden", "tutorial-missing", "tutorial-disabled", "tutorial-foreign", "tutorial-active", "tutorial-suppressed", "shared-arrow-active", "dialogue", "pause", "loading", "panel", "time", "transition", "build", "simulation", "visitor", "challenge", "multiplayer" })
        {
            Fixture f = new(unlocked: true); f.Tick(); TutorialPointer arrow = f.Arrow;
            if (state == "disabled-option") Set(f.trigger, "showUnlockPointer", false);
            if (state == "locked") f.save.CurrentData.features.Clear();
            if (state == "missing-data") f.save.CurrentData = null;
            if (state == "intro-complete") f.save.CurrentData.completedLessons.Add("Sequence_Shop");
            if (state == "shop-open") f.shop.Panel.SetActive(true);
            if (state == "shop-missing") { Set(f.trigger, "shopManager", null); ShopManager.Instance = null; }
            if (state == "shop-panel-missing") f.shop.Panel = null;
            if (state == "shop-foreign") f.shop.gameObject.scene = new Scene(2, "Other");
            if (state == "button-disabled") f.button.enabled = false;
            if (state == "button-noninteractable") f.button.interactable = false;
            if (state == "parent-noninteractable") f.canvas.gameObject.AddComponent<CanvasGroup>().interactable = false;
            if (state == "canvas-disabled") f.canvas.enabled = false;
            if (state == "canvas-hidden") { f.canvas.gameObject.SetActive(false); Call(f.trigger, "OnDisable"); }
            if (state == "tutorial-missing") TutorialManager.Instance = null;
            if (state == "tutorial-disabled") f.tutorial.enabled = false;
            if (state == "tutorial-foreign") f.tutorial.gameObject.scene = new Scene(2, "Other");
            if (state == "tutorial-active") f.tutorial.IsTutorialActive = true;
            if (state == "tutorial-suppressed") f.tutorial.IsTutorialPresentationSuppressed = true;
            if (state == "shared-arrow-active") f.template.gameObject.SetActive(true);
            if (state == "dialogue") DialogueManager.IsAnyDialogueVisible = true;
            if (state == "pause") PauseManager.Instance = new PauseManager { isPaused = true };
            if (state == "loading") LoadingScreenManager.IsLoading = true;
            if (state == "panel") UIPanelCoordinator.Instance = new UIPanelCoordinator { HasOpenPanel = true };
            if (state == "time") Time.timeScale = 0;
            if (state == "transition") GameManager.Instance.IsTransitioning = true;
            if (state == "build") GameManager.Instance.CurrentState = GameManager.GameState.Building;
            if (state == "simulation") GameManager.Instance.CurrentState = GameManager.GameState.Simulation;
            if (state == "visitor") FusionConnectionManager.Instance = new FusionConnectionManager { IsGuestSaveProtected = true };
            if (state == "challenge") MultiplayerChallengeLobbyUI.IsChallengeActive = true;
            if (state == "multiplayer") f.hud.scene = new Scene(1, "Multiplayer");
            f.Tick();
            Check(!f.Visible && !arrow.gameObject.activeSelf, "Advisory yields to normal ownership/unavailable targets: " + state);
            Check(f.shop.opens == 0, "A visibility gate does not invoke gameplay or purchases: " + state);
        }
    }
    private static TutorialStep NavigationStep(bool hasWaypoints)
    {
        return new TutorialStep
        {
            message = hasWaypoints ? "Go back to Professor Bhan\n" : "Find Reed",
            screenPosition = TutorialPosition.Left,
            showNextButton = false,
            stepWaypoints = hasWaypoints ? new List<GuiderWaypoint>
            {
                new GuiderWaypoint { target = new GameObject("Professor Bhan destination").transform, advancesTutorial = false }
            } : new List<GuiderWaypoint>()
        };
    }
    private static void PassiveTutorialGuidance()
    {
        // Both are real authored story shapes: GoBackToProfBhan has a route,
        // while Find Reed is a passive Left objective with no waypoints.
        foreach (bool hasWaypoints in new[] { true, false })
        {
            Fixture f = new(unlocked: true);
            TutorialStep step = NavigationStep(hasWaypoints);
            List<GuiderWaypoint> route = step.stepWaypoints;
            GuiderWaypoint[] waypoints = route.ToArray();
            string message = step.message;
            f.tutorial.IsTutorialActive = true;
            f.tutorial.CurrentStep = step;
            int points = f.template.points, hides = f.template.hides;
            f.Tick();
            Check(f.Visible && f.Arrow.target == f.button.transform,
                "Shop unlock guidance coexists with passive Left story objective: " + message.Trim());
            Check(f.tutorial.IsTutorialActive && ReferenceEquals(f.tutorial.CurrentStep, step) &&
                  step.message == message && step.screenPosition == TutorialPosition.Left && !step.showNextButton &&
                  !step.canSkip && !step.advanceOnClick && !step.usePointer && step.requiredAction == TutorialStepAction.None,
                "Advisory does not finish, replace or alter the active passive story objective.");
            Check(ReferenceEquals(step.stepWaypoints, route) && route.SequenceEqual(waypoints) &&
                  route.All(item => !item.advancesTutorial),
                "Advisory leaves the current story trail and its waypoint advancement flags unchanged.");
            Check(f.template.points == points && f.template.hides == hides && !f.template.gameObject.activeSelf && f.shop.opens == 0,
                "Coexisting Shop guidance neither steals the shared pointer nor opens the shop or advances story.");
            f.Tick();
            Check(f.Visible && ReferenceEquals(f.tutorial.CurrentStep, step),
                "A continuing navigation objective cannot silently suppress the advisory on later frames.");
        }
        foreach (string restriction in new[] { "missing-step", "center", "low-center", "unknown-position", "continue", "skip", "click-action", "pointer", "selection-action", "lock-look", "lock-jump", "lock-run", "world-highlight" })
        {
            Fixture f = new(unlocked: true); f.Tick(); TutorialPointer arrow = f.Arrow;
            TutorialStep step = NavigationStep(false);
            f.tutorial.IsTutorialActive = true;
            f.tutorial.CurrentStep = step;
            if (restriction == "missing-step") f.tutorial.CurrentStep = null;
            if (restriction == "center") step.screenPosition = TutorialPosition.Center;
            if (restriction == "low-center") step.screenPosition = TutorialPosition.LowCenter;
            if (restriction == "unknown-position") step.screenPosition = (TutorialPosition)2;
            if (restriction == "continue") step.showNextButton = true;
            if (restriction == "skip") step.canSkip = true;
            if (restriction == "click-action") step.advanceOnClick = true;
            if (restriction == "pointer") step.usePointer = true;
            if (restriction == "selection-action") step.requiredAction = TutorialStepAction.SelectTool;
            if (restriction == "lock-look") step.lockLook = true;
            if (restriction == "lock-jump") step.lockJump = true;
            if (restriction == "lock-run") step.lockRun = true;
            if (restriction == "world-highlight") step.worldHighlightObject = new GameObject("Required drawing guide");
            f.Tick();
            Check(!f.Visible && !arrow.gameObject.activeSelf,
                "Required/unknown tutorial instruction retains priority over Shop advisory: " + restriction);
            Check(f.shop.opens == 0 && f.tutorial.IsTutorialActive && ReferenceEquals(f.tutorial.CurrentStep, restriction == "missing-step" ? null : step),
                "Yielding to instruction never opens the Shop or changes its owning tutorial: " + restriction);
            f.tutorial.CurrentStep = NavigationStep(false); f.Tick();
            Check(f.Visible && f.tutorial.IsTutorialActive,
                "Changing back to a passive objective resumes the advisory without completing the tutorial: " + restriction);
        }
        foreach (TutorialStepAction action in Enum.GetValues<TutorialStepAction>().Where(value => value != TutorialStepAction.None))
        {
            Fixture f = new(unlocked: true);
            f.tutorial.IsTutorialActive = true;
            f.tutorial.CurrentStep = NavigationStep(false);
            f.tutorial.CurrentStep.requiredAction = action;
            f.Tick();
            Check(!f.Visible && f.tutorial.CurrentStep.requiredAction == action,
                "Every real semantic action retains priority, without changing the requirement: " + action);
        }
        Fixture shared = new(unlocked: true);
        shared.tutorial.IsTutorialActive = true;
        shared.tutorial.CurrentStep = NavigationStep(false);
        shared.template.gameObject.SetActive(true);
        shared.Tick();
        Check(!shared.Visible && shared.template.gameObject.activeSelf,
            "An independently active shared arrow still wins even during a passive navigation objective.");
        shared.template.Hide(); shared.Tick();
        Check(shared.Visible && shared.tutorial.IsTutorialActive,
            "Releasing a shared arrow lets passive-objective Shop guidance resume without replacing the tutorial.");
        Fixture suppressed = new(unlocked: true);
        suppressed.tutorial.IsTutorialActive = true;
        suppressed.tutorial.CurrentStep = NavigationStep(false);
        suppressed.tutorial.IsTutorialPresentationSuppressed = true;
        suppressed.Tick();
        Check(!suppressed.Visible,
            "Inspection/modal suppression remains authoritative for otherwise-passive navigation guidance.");
    }
    private static void OwnershipAndLifecycle()
    {
        Fixture f = new(unlocked: true); f.template.points = 17; f.template.hides = 11; f.Tick();
        Check(f.template.points == 17 && f.template.hides == 11 && !f.template.gameObject.activeSelf, "Showing the Shop clone does not point, hide, or activate the shared tutorial/Almanac arrow.");
        Check(f.Arrow.GetComponentsInChildren<Graphic>(true).All(g => !g.raycastTarget) && f.Arrow.GetComponent<CanvasGroup>().blocksRaycasts == false,
            "The cloned TutorialPointer Awake boundary keeps all arrow graphics and its CanvasGroup click-through.");
        TutorialPointer old = f.Arrow; TutorialPointer replacement = f.Template(); f.tutorial.SharedPointer = replacement; f.Tick();
        Check(old.gameObject.destroyed && !ReferenceEquals(f.Arrow, old) && f.Visible, "A changed authored pointer template replaces only the private Shop clone.");
        Check(!f.template.gameObject.destroyed && !replacement.gameObject.destroyed, "Template replacement never destroys either shared arrow source.");
        f.tutorial.SharedPointer = null; f.Tick(); Check(!f.Visible, "A temporarily missing shared template hides rather than substituting unrelated artwork.");
        f.tutorial.SharedPointer = replacement; f.Tick(); Check(f.Visible, "A late/restored shared arrow template resumes pending Shop guidance.");
        f.shop.Panel.SetActive(true); f.Tick(); Check(!f.Visible, "Opening the Shop hides the overworld advisory.");
        f.shop.Panel.SetActive(false); f.Tick(); Check(f.Visible, "Closing the Shop before the introduction completes restores pending guidance.");
        f.save.CurrentData = new PlayerData { features = new HashSet<string> { "shop" }, completedLessons = new List<string> { "Sequence_Shop" } }; f.Tick();
        Check(!f.Visible, "Replacing CurrentData cannot retain the previous account's pending introduction pointer.");
        var next = new PlayerDataManager(); next.CurrentData.features.Add("shop"); PlayerDataManager.Instance = next; f.Tick();
        Check(f.Visible, "A different save manager with an unfinished unlocked Shop can show its own pending guidance.");
        old = f.Arrow; f.hud.SetActive(false);
        Check(!old.gameObject.activeSelf, "Disabling the Shop HUD immediately hides the separate clone living outside its hierarchy.");
        f.hud.SetActive(true); f.Tick(); Check(f.Visible, "Re-enabling an eligible Shop HUD resumes the advisory.");
        old = f.Arrow; Call(f.trigger, "OnDestroy"); Call(f.trigger, "OnDestroy");
        Check(old.gameObject.destroyed && Get<TutorialPointer>(f.trigger, "unlockPointer") == null && !replacement.gameObject.destroyed,
            "Repeated teardown destroys only the private clone and retains shared tutorial artwork.");
    }
    private static void ButtonAndSubscriptions()
    {
        Fixture f = new(unlocked: true); f.Tick(); int listeners = f.button.onClick.ListenerCount;
        Check(listeners == 1, "The original Shop button has exactly one ordinary OpenShop listener.");
        f.button.Click(); f.Tick();
        Check(f.shop.opens == 1 && !f.Visible && f.button.onClick.ListenerCount == listeners, "Clicking through the arrow opens the existing Shop once without replacing its button wiring.");
        f.shop.Panel.SetActive(false); f.Tick();
        PlayerDataManager previous = f.save;
        var next = new PlayerDataManager(); next.CurrentData.features.Add("shop"); PlayerDataManager.Instance = next; f.Tick();
        Check(previous.OnFeatureUnlocksChanged == null && next.OnFeatureUnlocksChanged?.GetInvocationList().Length == 1,
            "Save-manager changes release the previous unlock event and bind the current one exactly once.");
        for (int i = 0; i < 20; i++) f.Tick();
        Check(next.OnFeatureUnlocksChanged.GetInvocationList().Length == 1, "Repeated updates do not duplicate unlock subscriptions.");
        Call(f.trigger, "OnDestroy");
        Check(f.button.onClick.ListenerCount == 0 && next.OnFeatureUnlocksChanged == null && GameManager.Instance.OnEnterBuildMode.ListenerCount == 0 && GameManager.Instance.OnExitBuildMode.ListenerCount == 0,
            "Teardown removes only the component's normal button/build/unlock subscriptions.");
    }
    private static void SourceGuards()
    {
        string root = FindRoot();
        string source = File.ReadAllText(Path.Combine(root, "Assets/Script/UI/Shop/ShopButtonTrigger.cs"));
        foreach (string forbidden in new[] { "SaveGame(", "TrySaveGame(", "PlayerPrefs", "Purchase(", "BuyItem(", "MarkLessonCompleted", "completedLessons.Add", "StartTutorial(", "ShowNextStep(", "SharedPointer.PointAt", "template.PointAt", "template.Hide()" })
            Check(!source.Contains(forbidden, StringComparison.Ordinal), "Shop pointer cannot change saves, purchases, progression or the shared pointer: " + forbidden);
        Check(source.Contains("Instantiate(template,") && source.Contains("unlockPointer.PointAtBuildTarget("), "Only a dedicated clone uses safe edge placement.");
        Check(source.Contains("HasBlockingTutorialForUnlockPointer(tutorial)"),
            "The actual visibility policy distinguishes required tutorials from passive walking objectives.");
        Check(!source.Contains("tutorial.IsTutorialActive || tutorial.IsTutorialPresentationSuppressed"),
            "A blanket tutorial-active suppression cannot return and hide the arrow during walking reminders.");
        Check(source.Contains("Destroy(unlockPointer.gameObject)") && source.Contains("unlockPointerTemplate = null"), "Cleanup owns only the cloned pointer.");
        Check(source.Contains("DialogueHudButtonGate.BindShop(gameObject, this)"),
            "All authored Shop triggers use the shared dialogue mask on their existing HUD object.");
        Check(source.Contains("public bool CanShowHudEntry") && source.Contains("visibleOutsideBuildMode") && source.Contains("IsShopUnlocked()"),
            "Shop restoration eligibility retains the existing persistent-unlock and authored-visibility policies.");
        string pointer = File.ReadAllText(Path.Combine(root, "Assets/Script/Level/Tutorial/TutorialPointer.cs"));
        Check(pointer.Contains("graphic.raycastTarget = false") && pointer.Contains("canvasGroup.interactable = false") && pointer.Contains("canvasGroup.blocksRaycasts = false"),
            "The actual cloned pointer initialization still disables graphical and group click interception.");
    }
    private static string FindRoot()
    {
        for (DirectoryInfo directory = new(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Assets/Script/UI/Shop/ShopButtonTrigger.cs"))) return directory.FullName;
        throw new InvalidOperationException("Unity project not found.");
    }
}
