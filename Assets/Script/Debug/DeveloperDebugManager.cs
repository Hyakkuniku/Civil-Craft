using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Persistent, development-only menu for quickly reaching gameplay states.
/// Keep the assigned Canvas as a child of this object so both survive scene loads.
/// </summary>
[DefaultExecutionOrder(-1000)]
[DisallowMultipleComponent]
public sealed class DeveloperDebugManager : MonoBehaviour
{
    public static DeveloperDebugManager Instance { get; private set; }

    [Header("Availability")]
    [Tooltip("Leave off for release builds. Editor and Development Builds are always allowed.")]
    [SerializeField] private bool allowInReleaseBuild;

    [Header("Persistent UI")]
    [Tooltip("Canvas containing the debug window. It must be a child, not this manager's own GameObject.")]
    [SerializeField] private Canvas debugCanvas;
    [SerializeField] private GameObject debugWindow;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text timeScaleText;

    [Header("Selectors")]
    [SerializeField] private TMP_Dropdown sceneDropdown;
    [SerializeField] private TMP_Dropdown tutorialDropdown;
    [SerializeField] private TMP_Dropdown buildLocationDropdown;
    [SerializeField] private TMP_Dropdown npcPhaseDropdown;
    [SerializeField] private TMP_Dropdown achievementDropdown;
    [SerializeField] private TMP_Dropdown coinAmountDropdown;

    [Header("Runtime Tools")]
    [SerializeField] private Slider timeScaleSlider;
    [SerializeField] private Toggle invincibleBridgeToggle;
    [SerializeField] private Vector3 playerTeleportOffset = new Vector3(0f, 1f, 0f);
    [Min(1)] [SerializeField] private int debugCanvasSortOrder = 32000;
    [Min(0.05f)] [SerializeField] private float invincibleJointScanInterval = 0.25f;

    [Header("Safety")]
    [Min(1f)] [SerializeField] private float clearSaveConfirmationSeconds = 4f;

    private readonly List<string> sceneNames = new List<string>();
    private readonly List<TutorialSequence> tutorials = new List<TutorialSequence>();
    private readonly List<BuildLocation> buildLocations = new List<BuildLocation>();
    private readonly List<AchievementSO> achievements = new List<AchievementSO>();
    private static readonly int[] DebugCoinAmounts = { 1000, 10000, 100000, 1000000 };
    private readonly Dictionary<Joint, JointBreakLimits> originalJointLimits =
        new Dictionary<Joint, JointBreakLimits>();
    private NPCProgressionManager npcProgression;
    private float clearSaveConfirmationDeadline = -1f;
    private CursorLockMode previousCursorLockMode;
    private bool previousCursorVisible;
    private bool menuOpen;
    private float nextJointScanTime;

    private struct JointBreakLimits
    {
        public float force;
        public float torque;

        public JointBreakLimits(float force, float torque)
        {
            this.force = force;
            this.torque = torque;
        }
    }

    public static bool IsBridgeInvincible { get; private set; }
    public bool IsMenuOpen => menuOpen;
    public bool IsAvailable => Application.isEditor || Debug.isDebugBuild || allowInReleaseBuild;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        Instance = null;
        IsBridgeInvincible = false;
        BridgePhysicsManager.DebugInvincibleBridge = false;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        if (transform.parent != null)
            transform.SetParent(null, true);

        DontDestroyOnLoad(gameObject);

        if (!IsAvailable)
        {
            if (debugCanvas != null) debugCanvas.gameObject.SetActive(false);
            enabled = false;
            return;
        }

        PrepareCanvas();
        SetMenuVisible(false, false);

        if (timeScaleSlider != null)
        {
            timeScaleSlider.SetValueWithoutNotify(Time.timeScale);
            timeScaleSlider.onValueChanged.AddListener(SetTimeScale);
        }

        if (invincibleBridgeToggle != null)
        {
            invincibleBridgeToggle.SetIsOnWithoutNotify(IsBridgeInvincible);
            invincibleBridgeToggle.onValueChanged.AddListener(SetInvincibleBridge);
        }

        PopulateSceneDropdown();
        RefreshSceneObjects();
        StartCoroutine(RefreshListsAfterSceneInitialization());
    }

    private void OnEnable()
    {
        if (IsAvailable)
            SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    private void OnDestroy()
    {
        if (timeScaleSlider != null)
            timeScaleSlider.onValueChanged.RemoveListener(SetTimeScale);
        if (invincibleBridgeToggle != null)
            invincibleBridgeToggle.onValueChanged.RemoveListener(SetInvincibleBridge);

        if (Instance == this)
        {
            SetInvincibleBridge(false);
            Instance = null;
        }
    }

    private void Update()
    {
        // Physics joints are created when simulation begins, which may happen
        // after the toggle was enabled. Keep newly-created joints protected too.
        if (IsBridgeInvincible && Time.unscaledTime >= nextJointScanTime)
        {
            ApplyInvincibleJointLimits();
            nextJointScanTime = Time.unscaledTime + invincibleJointScanInterval;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (keyboard.f12Key.wasPressedThisFrame || keyboard.backquoteKey.wasPressedThisFrame)
            ToggleMenu();
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        clearSaveConfirmationDeadline = -1f;
        PrepareCanvas();
        StartCoroutine(RefreshListsAfterSceneInitialization());
        SetStatus($"Loaded scene: {scene.name}");
    }

    private IEnumerator RefreshListsAfterSceneInitialization()
    {
        // Persistent managers and scene objects do not all finish Awake/Start in
        // the same frame. Refresh twice so direct-scene testing is reliable too.
        yield return null;
        PopulateSceneDropdown();
        RefreshSceneObjects();
        yield return new WaitForEndOfFrame();
        PopulateSceneDropdown();
        RefreshSceneObjects();
    }

    public void ToggleMenu()
    {
        if (!IsAvailable) return;
        SetMenuVisible(!menuOpen, true);
    }

    public void OpenMenu()
    {
        if (!IsAvailable) return;
        SetMenuVisible(true, true);
    }

    public void CloseMenu()
    {
        SetMenuVisible(false, true);
    }

    private void SetMenuVisible(bool visible, bool manageCursor)
    {
        if (debugWindow != null)
            debugWindow.SetActive(visible);
        else if (debugCanvas != null)
            debugCanvas.gameObject.SetActive(visible);

        menuOpen = visible;

        if (!manageCursor) return;

        if (visible)
        {
            previousCursorLockMode = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            RefreshDropdownValues();
            if (debugCanvas != null) debugCanvas.transform.SetAsLastSibling();
        }
        else
        {
            Cursor.lockState = previousCursorLockMode;
            Cursor.visible = previousCursorVisible;
        }
    }

    private void PrepareCanvas()
    {
        if (debugCanvas == null) return;
        debugCanvas.overrideSorting = true;
        debugCanvas.sortingOrder = debugCanvasSortOrder;
        PrepareDropdown(sceneDropdown);
        PrepareDropdown(tutorialDropdown);
        PrepareDropdown(buildLocationDropdown);
        PrepareDropdown(npcPhaseDropdown);
        PrepareDropdown(achievementDropdown);
        PrepareDropdown(coinAmountDropdown);
    }

    private void PrepareDropdown(TMP_Dropdown dropdown)
    {
        if (dropdown == null || dropdown.template == null) return;

        // Dropdown popups are children of the debug ScrollView. Without their
        // own canvas, its RectMask2D can clip the popup and make it appear as if
        // the dropdown did nothing. Keep the cloned popup above that mask.
        GameObject template = dropdown.template.gameObject;
        Canvas popupCanvas = template.GetComponent<Canvas>();
        if (popupCanvas == null) popupCanvas = template.AddComponent<Canvas>();
        popupCanvas.overrideSorting = true;
        popupCanvas.sortingOrder = debugCanvasSortOrder + 10;

        if (template.GetComponent<GraphicRaycaster>() == null)
            template.AddComponent<GraphicRaycaster>();

        CanvasGroup group = template.GetComponent<CanvasGroup>();
        if (group == null) group = template.AddComponent<CanvasGroup>();
        group.alpha = 1f;
        group.interactable = true;
        group.blocksRaycasts = true;
    }

    public void RefreshSceneObjects()
    {
        tutorials.Clear();
        foreach (TutorialSequence sequence in Resources.FindObjectsOfTypeAll<TutorialSequence>())
        {
            if (IsLoadedSceneObject(sequence)) tutorials.Add(sequence);
        }

        tutorials.Sort((a, b) => string.Compare(GetTutorialLabel(a), GetTutorialLabel(b), StringComparison.OrdinalIgnoreCase));
        SetDropdownOptions(tutorialDropdown, tutorials.ConvertAll(GetTutorialLabel), "No tutorial sequences in this scene");
        SelectActiveTutorialInDropdown();

        buildLocations.Clear();
        foreach (BuildLocation location in Resources.FindObjectsOfTypeAll<BuildLocation>())
        {
            if (IsLoadedSceneObject(location)) buildLocations.Add(location);
        }

        buildLocations.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
        SetDropdownOptions(buildLocationDropdown, buildLocations.ConvertAll(GetBuildLocationLabel), "No build locations in this scene");

        npcProgression = FindObjectOfType<NPCProgressionManager>(true);
        List<string> phases = new List<string>();
        if (npcProgression != null)
        {
            for (int i = 0; i < npcProgression.PhaseCount; i++)
                phases.Add(npcProgression.GetPhaseDisplayName(i));
        }

        SetDropdownOptions(npcPhaseDropdown, phases, "No NPC progression phases in this scene");

        PopulateAchievementDropdown();
        UpdateTimeScaleLabel();
    }

    public void RefreshDropdownValues()
    {
        PopulateSceneDropdown();
        RefreshSceneObjects();
        TutorialSequence activeTutorial = TutorialManager.Instance != null
            ? TutorialManager.Instance.ActiveSequence
            : null;
        string activeTutorialLabel = activeTutorial != null
            ? GetTutorialLabel(activeTutorial)
            : "none";
        SetStatus(
            $"Lists refreshed: {sceneNames.Count} scenes, {tutorials.Count} tutorials, " +
            $"{buildLocations.Count} build locations, " +
            $"{(npcProgression != null ? npcProgression.PhaseCount : 0)} NPC phases, " +
            $"{achievements.Count} achievements. " +
            $"Active tutorial: {activeTutorialLabel}.");
    }

    private void PopulateAchievementDropdown()
    {
        achievements.Clear();
        HashSet<string> achievementIDs = new HashSet<string>(StringComparer.Ordinal);

        if (PlayerDataManager.Instance != null &&
            PlayerDataManager.Instance.allGameAchievements != null)
        {
            foreach (AchievementSO achievement in PlayerDataManager.Instance.allGameAchievements)
                AddAchievementIfUnique(achievement, achievementIDs);
        }

        foreach (AchievementSO achievement in Resources.FindObjectsOfTypeAll<AchievementSO>())
            AddAchievementIfUnique(achievement, achievementIDs);

        achievements.Sort((left, right) => string.Compare(
            left.achievementID,
            right.achievementID,
            StringComparison.OrdinalIgnoreCase));
        SetDropdownOptions(
            achievementDropdown,
            achievements.ConvertAll(GetAchievementLabel),
            "No achievements registered");
    }

    private void AddAchievementIfUnique(AchievementSO achievement, HashSet<string> achievementIDs)
    {
        if (achievement == null || string.IsNullOrWhiteSpace(achievement.achievementID) ||
            !achievementIDs.Add(achievement.achievementID)) return;
        achievements.Add(achievement);
    }

    private static bool IsLoadedSceneObject(Component component)
    {
        return component != null && component.gameObject.scene.IsValid() && component.gameObject.scene.isLoaded;
    }

    private void PopulateSceneDropdown()
    {
        sceneNames.Clear();
        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            if (!string.IsNullOrWhiteSpace(path))
                sceneNames.Add(Path.GetFileNameWithoutExtension(path));
        }

        SetDropdownOptions(sceneDropdown, sceneNames, "No enabled scenes in Build Settings");

        int currentIndex = sceneNames.FindIndex(name =>
            string.Equals(name, SceneManager.GetActiveScene().name, StringComparison.OrdinalIgnoreCase));
        if (sceneDropdown != null && currentIndex >= 0)
        {
            sceneDropdown.SetValueWithoutNotify(currentIndex);
            sceneDropdown.RefreshShownValue();
        }
    }

    public void LoadSelectedScene()
    {
        int index = sceneDropdown != null ? sceneDropdown.value : -1;
        if (index < 0 || index >= sceneNames.Count)
        {
            SetStatus("Select a valid scene first.");
            return;
        }

        Time.timeScale = 1f;
        SetInvincibleBridge(false);
        SetMenuVisible(false, true);
        SceneManager.LoadScene(sceneNames[index]);
    }

    public void ForceStartSelectedTutorial()
    {
        int index = tutorialDropdown != null ? tutorialDropdown.value : -1;
        if (index < 0 || index >= tutorials.Count || tutorials[index] == null)
        {
            SetStatus("Select a valid tutorial first.");
            return;
        }

        if (TutorialManager.Instance == null)
        {
            SetStatus("This scene has no active TutorialManager.");
            return;
        }

        TutorialSequence sequence = tutorials[index];
        TutorialManager.Instance.RestartTutorial(sequence);
        SetStatus($"Force-started tutorial: {GetTutorialLabel(sequence)}");
        CloseMenu();
    }

    public void AutoCompleteSelectedTutorial()
    {
        TutorialManager tutorialManager = TutorialManager.Instance;
        TutorialSequence sequence = tutorialManager != null
            ? tutorialManager.ActiveSequence
            : null;

        if (sequence == null)
        {
            int index = tutorialDropdown != null ? tutorialDropdown.value : -1;
            if (index >= 0 && index < tutorials.Count)
                sequence = tutorials[index];
        }

        if (sequence == null)
        {
            SetStatus("No active or selected tutorial was found.");
            return;
        }

        bool completedActiveTutorial = tutorialManager != null &&
                                       tutorialManager.IsPlayingSequence(sequence);
        if (completedActiveTutorial)
        {
            tutorialManager.SkipTutorial();
        }
        else if (PlayerDataManager.Instance != null && !string.IsNullOrWhiteSpace(sequence.lessonName))
        {
            PlayerDataManager.Instance.CompleteLesson(sequence.lessonName);
        }
        else
        {
            SetStatus("The selected tutorial has no Lesson Name and is not currently playing.");
            return;
        }

        RefreshDropdownValues();
        SetStatus($"Completed {(completedActiveTutorial ? "active" : "selected")} tutorial: {GetTutorialLabel(sequence)}");
    }

    public void CompleteAllTutorials()
    {
        // Refresh first so sequences enabled or created after scene startup are
        // included in the operation.
        RefreshSceneObjects();

        if (tutorials.Count == 0)
        {
            SetStatus("No tutorial sequences were found in this scene.");
            return;
        }

        if (PlayerDataManager.Instance == null)
        {
            SetStatus("PlayerDataManager is unavailable; tutorial completion could not be saved.");
            return;
        }

        int completedCount = 0;
        int unnamedCount = 0;
        foreach (TutorialSequence sequence in tutorials)
        {
            if (sequence == null) continue;
            if (string.IsNullOrWhiteSpace(sequence.lessonName))
            {
                unnamedCount++;
                continue;
            }

            PlayerDataManager.Instance.CompleteLesson(sequence.lessonName);
            completedCount++;
        }

        if (TutorialManager.Instance != null)
            TutorialManager.Instance.DebugCloseActiveTutorialAndClearQueue();

        RefreshSceneObjects();
        string unnamedMessage = unnamedCount > 0
            ? $" {unnamedCount} sequence(s) had no Lesson Name and could not be saved."
            : string.Empty;
        SetStatus($"Completed all {completedCount} named tutorials in this scene.{unnamedMessage}");
    }

    public void TeleportPlayerToSelectedBuildLocation()
    {
        BuildLocation location = GetSelectedBuildLocation();
        PlayerMotor player = FindObjectOfType<PlayerMotor>(true);
        if (location == null || player == null)
        {
            SetStatus(location == null ? "Select a valid build location." : "No PlayerMotor was found.");
            return;
        }

        Transform target = location.navigationTarget != null
            ? location.navigationTarget.transform
            : location.transform;

        CharacterController controller = player.GetComponent<CharacterController>();
        bool controllerWasEnabled = controller != null && controller.enabled;
        if (controllerWasEnabled) controller.enabled = false;
        player.transform.SetPositionAndRotation(target.position + playerTeleportOffset, target.rotation);
        if (controllerWasEnabled) controller.enabled = true;

        SetStatus($"Teleported player to: {GetBuildLocationLabel(location)}");
        CloseMenu();
    }

    public void TeleportNPCToSelectedPhase()
    {
        if (npcProgression == null)
            npcProgression = FindObjectOfType<NPCProgressionManager>(true);

        int index = npcPhaseDropdown != null ? npcPhaseDropdown.value : -1;
        if (npcProgression == null || index < 0 || index >= npcProgression.PhaseCount)
        {
            SetStatus(npcProgression == null ? "No NPCProgressionManager was found." : "Select a valid NPC phase.");
            return;
        }

        if (!npcProgression.DebugWarpToPhase(index))
        {
            SetStatus("NPC phase warp failed. Check the phase target Transform.");
            return;
        }

        SetStatus($"NPC moved and activated: {npcProgression.GetPhaseDisplayName(index)}");
        CloseMenu();
    }

    public void UnlockSelectedBuildLocationContract()
    {
        BuildLocation location = GetSelectedBuildLocation();
        if (location == null)
        {
            SetStatus("Select a valid build location first.");
            return;
        }

        ContractSO contract = location.activeContract;
        NPCContractGiver matchingGiver = null;
        foreach (NPCContractGiver giver in FindObjectsOfType<NPCContractGiver>(true))
        {
            if (giver == null || giver.targetBuildLocation != location) continue;
            matchingGiver = giver;
            if (contract == null) contract = giver.contractToGive;
            break;
        }

        if (contract == null)
        {
            if (npcProgression == null)
                npcProgression = FindObjectOfType<NPCProgressionManager>(true);
            if (npcProgression != null)
                npcProgression.TryGetContractForBuildLocation(location, out contract);
        }

        if (contract == null)
        {
            SetStatus($"No contract is associated with '{location.name}'. Check its NPC phase assignment.");
            return;
        }

        PlayerPrefs.DeleteKey("LockedContract_" + contract.ContractID);
        PlayerPrefs.Save();
        location.activeContract = contract;

        if (matchingGiver != null)
            matchingGiver.DebugUnlockAndAcceptContract();

        if (ObjectiveTrackerUI.Instance != null)
        {
            string destination = location.navigationTarget != null
                ? location.navigationTarget.name
                : location.name;
            ObjectiveTrackerUI.Instance.SetObjective(contract, destination);
        }

        SetStatus($"Unlocked '{contract.name}' at '{location.name}'. Build Mode is now accessible.");
        RefreshDropdownValues();
    }

    public void UnlockSelectedAchievement()
    {
        if (PlayerDataManager.Instance == null)
        {
            SetStatus("PlayerDataManager is unavailable; the achievement could not be saved.");
            return;
        }

        int index = achievementDropdown != null ? achievementDropdown.value : -1;
        if (index < 0 || index >= achievements.Count || achievements[index] == null)
        {
            SetStatus("Select a valid achievement first.");
            return;
        }

        AchievementSO achievement = achievements[index];
        bool unlocked = PlayerDataManager.Instance.DebugUnlockAchievement(achievement);
        PopulateAchievementDropdown();
        SetStatus(unlocked
            ? $"Unlocked achievement: {achievement.achievementName}"
            : $"Achievement was already unlocked: {achievement.achievementName}");
    }

    public void UnlockAllAchievements()
    {
        if (PlayerDataManager.Instance == null)
        {
            SetStatus("PlayerDataManager is unavailable; achievements could not be saved.");
            return;
        }

        PopulateAchievementDropdown();
        if (achievements.Count == 0)
        {
            SetStatus("No registered achievements were found.");
            return;
        }

        int previouslyUnlocked = PlayerDataManager.Instance.CurrentData.unlockedAchievements.Count;
        foreach (AchievementSO achievement in achievements)
            PlayerDataManager.Instance.DebugUnlockAchievement(achievement);

        int totalUnlocked = PlayerDataManager.Instance.CurrentData.unlockedAchievements.Count;
        int newlyUnlocked = Mathf.Max(0, totalUnlocked - previouslyUnlocked);
        PopulateAchievementDropdown();
        SetStatus($"Unlocked all achievements. {newlyUnlocked} new, {totalUnlocked} total unlocked.");
    }

    public void AddSelectedCoins()
    {
        if (PlayerDataManager.Instance == null || PlayerDataManager.Instance.CurrentData == null)
        {
            SetStatus("PlayerDataManager is unavailable; coins could not be added.");
            return;
        }

        int index = coinAmountDropdown != null ? coinAmountDropdown.value : 1;
        index = Mathf.Clamp(index, 0, DebugCoinAmounts.Length - 1);
        int amount = DebugCoinAmounts[index];
        PlayerDataManager.Instance.AddGold(amount);
        int balance = PlayerDataManager.Instance.CurrentData.gold;
        SetStatus($"Added ₱{amount:N0}. Current balance: ₱{balance:N0}.");
    }

    public void SetTimeScale(float value)
    {
        Time.timeScale = Mathf.Clamp(value, 0f, 10f);
        UpdateTimeScaleLabel();
    }

    public void ResetTimeScale()
    {
        if (timeScaleSlider != null) timeScaleSlider.SetValueWithoutNotify(1f);
        SetTimeScale(1f);
    }

    private void UpdateTimeScaleLabel()
    {
        if (timeScaleText != null)
            timeScaleText.text = $"Time Scale: {Time.timeScale:0.00}x";
    }

    public void SetInvincibleBridge(bool enabledState)
    {
        IsBridgeInvincible = enabledState;
        BridgePhysicsManager.DebugInvincibleBridge = enabledState;

        if (enabledState)
        {
            ApplyInvincibleJointLimits();
            nextJointScanTime = Time.unscaledTime + invincibleJointScanInterval;
        }
        else
        {
            foreach (KeyValuePair<Joint, JointBreakLimits> entry in originalJointLimits)
            {
                if (entry.Key == null) continue;
                entry.Key.breakForce = entry.Value.force;
                entry.Key.breakTorque = entry.Value.torque;
            }
            originalJointLimits.Clear();
        }

        if (invincibleBridgeToggle != null && invincibleBridgeToggle.isOn != enabledState)
            invincibleBridgeToggle.SetIsOnWithoutNotify(enabledState);

        SetStatus(enabledState ? "Invincible Bridge enabled." : "Invincible Bridge disabled.");
    }

    private void ApplyInvincibleJointLimits()
    {
        foreach (Joint joint in FindObjectsOfType<Joint>(true))
        {
            if (joint == null) continue;
            if (!originalJointLimits.ContainsKey(joint))
                originalJointLimits.Add(joint, new JointBreakLimits(joint.breakForce, joint.breakTorque));

            joint.breakForce = Mathf.Infinity;
            joint.breakTorque = Mathf.Infinity;
        }
    }

    public void AutoCompleteCurrentContract()
    {
        ContractSO contract = GameManager.Instance != null ? GameManager.Instance.CurrentContract : null;
        if (contract == null)
        {
            BuildLocation location = GetSelectedBuildLocation();
            if (location != null) contract = location.activeContract;
        }

        if (contract == null)
        {
            SetStatus("No current or selected contract was found.");
            return;
        }

        if (LevelCompleteManager.Instance == null)
        {
            SetStatus("This scene has no LevelCompleteManager.");
            return;
        }

        // This deliberately enters the same success sequence used by gameplay.
        // Closing that panel still runs the normal bridge-save/finalization checks.
        LevelCompleteManager.Instance.ResetCompletionState();
        LevelCompleteManager.Instance.CompleteLevel(contract);
        SetStatus($"Triggered success sequence for: {contract.name}");
        CloseMenu();
    }

    public void ClearAllLocalSaveData()
    {
        if (Time.unscaledTime > clearSaveConfirmationDeadline)
        {
            clearSaveConfirmationDeadline = Time.unscaledTime + clearSaveConfirmationSeconds;
            SetStatus($"Press Clear Save again within {clearSaveConfirmationSeconds:0} seconds to confirm.");
            return;
        }

        clearSaveConfirmationDeadline = -1f;
        if (PlayerDataManager.Instance != null)
        {
            PlayerDataManager.Instance.DeleteSaveData();
            DeleteIfPresent(Path.Combine(Application.persistentDataPath, "playerSaveData.json.tmp"));
            DeleteIfPresent(Path.Combine(Application.persistentDataPath, "playerSaveData.json.bak"));
        }
        else
        {
            PlayerPrefs.DeleteAll();
            PlayerPrefs.Save();
            DeleteIfPresent(Path.Combine(Application.persistentDataPath, "playerSaveData.json"));
            DeleteIfPresent(Path.Combine(Application.persistentDataPath, "playerSaveData.json.tmp"));
            DeleteIfPresent(Path.Combine(Application.persistentDataPath, "playerSaveData.json.bak"));
        }

        SetStatus("Local PlayerPrefs and player JSON save data were cleared. Reload the scene for a clean start.");
    }

    private static void DeleteIfPresent(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception exception)
        {
            Debug.LogError($"[DeveloperDebugManager] Could not delete '{path}': {exception.Message}");
        }
    }

    private BuildLocation GetSelectedBuildLocation()
    {
        int index = buildLocationDropdown != null ? buildLocationDropdown.value : -1;
        return index >= 0 && index < buildLocations.Count ? buildLocations[index] : null;
    }

    private static string GetTutorialLabel(TutorialSequence sequence)
    {
        if (sequence == null) return "Missing Tutorial";
        return string.IsNullOrWhiteSpace(sequence.lessonName)
            ? sequence.name
            : $"{sequence.lessonName} ({sequence.name})";
    }

    private void SelectActiveTutorialInDropdown()
    {
        if (tutorialDropdown == null || TutorialManager.Instance == null) return;

        TutorialSequence activeSequence = TutorialManager.Instance.ActiveSequence;
        if (activeSequence == null) return;

        int index = tutorials.IndexOf(activeSequence);
        if (index < 0) return;

        tutorialDropdown.SetValueWithoutNotify(index);
        tutorialDropdown.RefreshShownValue();
    }

    private static string GetBuildLocationLabel(BuildLocation location)
    {
        if (location == null) return "Missing Location";
        string contractName = location.activeContract != null ? location.activeContract.name : "No Contract";
        return $"{location.name} - {contractName}";
    }

    private static string GetAchievementLabel(AchievementSO achievement)
    {
        if (achievement == null) return "Missing Achievement";
        bool unlocked = PlayerDataManager.Instance != null &&
                        PlayerDataManager.Instance.CurrentData != null &&
                        PlayerDataManager.Instance.CurrentData.unlockedAchievements.Contains(
                            achievement.achievementID);
        return $"{achievement.achievementID} - {achievement.achievementName}" +
               (unlocked ? " [Unlocked]" : string.Empty);
    }

    private static void SetDropdownOptions(TMP_Dropdown dropdown, List<string> options, string emptyLabel)
    {
        if (dropdown == null) return;
        dropdown.ClearOptions();
        dropdown.AddOptions(options.Count > 0 ? options : new List<string> { emptyLabel });
        dropdown.SetValueWithoutNotify(0);
        dropdown.RefreshShownValue();
        dropdown.interactable = options.Count > 0;
    }

    private void SetStatus(string message)
    {
        if (statusText != null) statusText.text = message;
        Debug.Log($"[DeveloperDebugManager] {message}", this);
    }
}
