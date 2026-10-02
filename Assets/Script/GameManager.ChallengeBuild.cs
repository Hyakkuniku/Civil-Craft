using UnityEngine;

public partial class GameManager
{
    public bool IsSessionChallengeBuild => ActiveBuildLocation != null && ActiveBuildLocation.IsSessionChallengeLocation;
    public bool CanEditSessionChallengeBuild
    {
        get
        {
            FusionConnectionManager connection = FusionConnectionManager.Instance;
            if (!IsSessionChallengeBuild || connection == null || !connection.IsHostWorldSession || connection.IsNetworkSceneLoading) return false;
            FusionMultiplayerAvatar host = FusionMultiplayerAvatar.FindHost(connection.Runner);
            return host != null && host.ChallengeState.Phase == MultiplayerChallengePhase.Building &&
                host.ChallengeState.Revision == ActiveBuildLocation.SessionChallengeRevision &&
                !MultiplayerChallengeRules.HasSubmitted(host.ChallengeState, connection.Runner.LocalPlayer, host.Object.InputAuthority) &&
                !(connection.Runner.GetComponent<FusionChallengeSubmissionSync>()?.LocalEditingLocked ?? false) &&
                !MultiplayerChallengeLobbyUI.IsSubmissionConfirmationOpen && !MultiplayerChallengeLobbyUI.IsLeaveConfirmationOpen &&
                !SessionChatUI.IsOpen;
        }
    }

    private bool challengeMainCameraEnabled, challengeLocationCameraEnabled, challengeGridEnabled;
    private bool challengeMotorEnabled, challengeInteractEnabled;
    private PlayerMotor challengeMotor;
    private PlayerInteract challengeInteract;
    private bool challengeEnding;
    private Vector3 challengeCameraPosition;
    private Quaternion challengeCameraRotation;
    private float challengeCameraSize, challengeCameraFieldOfView;

    public bool EnterSessionChallengeBuild(BuildLocation location, Transform player)
    {
        if (location == null || !location.IsSessionChallengeLocation || player == null ||
            CurrentState != GameState.Normal || isTransitioning || location.gameObject.scene != gameObject.scene) return false;
        FusionConnectionManager connection = FusionConnectionManager.Instance;
        FusionMultiplayerAvatar host = connection != null ? FusionMultiplayerAvatar.FindHost(connection.Runner) : null;
        if (host == null || !connection.IsHostWorldSession || connection.IsNetworkSceneLoading ||
            host.ChallengeState.Phase != MultiplayerChallengePhase.ReadyToBuild ||
            host.ChallengeState.Revision != location.SessionChallengeRevision ||
            host.ChallengeState.SiteKey.ToString() != location.SessionSourceSiteKey ||
            (connection.Runner.LocalPlayer != host.Object.InputAuthority && connection.Runner.LocalPlayer != host.ChallengeState.Guest)) return false;
        if (mainCamera == null) return false;

        CurrentState = GameState.Building;
        ActiveBuildLocation = location;
        CurrentContract = location.activeContract;
        currentPlayerTransform = player;
        challengeMotor = player.GetComponent<PlayerMotor>();
        challengeInteract = player.GetComponent<PlayerInteract>();
        challengeMotorEnabled = challengeMotor != null && challengeMotor.enabled;
        challengeInteractEnabled = challengeInteract != null && challengeInteract.enabled;
        mainCamParent = mainCamera.transform.parent;
        mainCamLocalPos = mainCamera.transform.localPosition;
        mainCamLocalRot = mainCamera.transform.localRotation;
        challengeMainCameraEnabled = mainCamera.enabled;
        challengeLocationCameraEnabled = location.locationCamera != null && location.locationCamera.enabled;
        if (location.locationCamera != null)
        {
            challengeCameraPosition = location.locationCamera.transform.position;
            challengeCameraRotation = location.locationCamera.transform.rotation;
            challengeCameraSize = location.locationCamera.orthographicSize;
            challengeCameraFieldOfView = location.locationCamera.fieldOfView;
        }
        challengeGridEnabled = location.gridImage != null && location.gridImage.enabled;
        mainCamera.transform.SetParent(null);
        mainCamera.transform.SetPositionAndRotation(location.locationCamera != null
            ? location.locationCamera.transform.position : location.GetDesiredCameraPosition(), location.locationCamera != null
            ? location.locationCamera.transform.rotation : location.GetDesiredCameraRotation());
        if (location.locationCamera != null) { mainCamera.enabled = false; location.locationCamera.enabled = true; }
        CaptureAndHide(uiElementsToHide, uiStateBeforeBuildMode);
        HideDecorativeCanyons(location);
        foreach (GameObject ui in buildModeUIElements) if (ui != null) ui.SetActive(true);
        InvokeEventSafely(OnEnterBuildMode);
        if (BuildUIController.Instance != null)
        {
            BuildUIController.Instance.maxBudget = CurrentContract.budget;
            BuildUIController.Instance.RefreshContractBuildUI();
            BuildUIController.Instance.MarkBridgeDirty();
            BuildUIController.Instance.ShowSessionBuildStatus(true);
        }
        BarCreator creator = FindObjectOfType<BarCreator>(true);
        if (location.gridImage != null) location.gridImage.enabled = creator != null && creator.isGridSnappingEnabled;
        FindObjectOfType<MagnifyingGlassController>(true)?.RefreshForActiveBuildLocation();
        return true;
    }

    // Synchronous cleanup also works when a peer disconnects or the scene unloads.
    // Input remains frozen until the lobby service restores the original return pose.
    public void ExitSessionChallengeBuild()
    {
        if (!IsSessionChallengeBuild || challengeEnding) return;
        challengeEnding = true;
        try
        {
            BuildLocation location = ActiveBuildLocation;
            CurrentState = GameState.Normal;
            FindObjectOfType<BarCreator>(true)?.CancelAllModes();
            if (location.gridImage != null) location.gridImage.enabled = challengeGridEnabled;
            if (location.locationCamera != null)
            {
                location.locationCamera.enabled = challengeLocationCameraEnabled;
                location.locationCamera.transform.SetPositionAndRotation(challengeCameraPosition, challengeCameraRotation);
                location.locationCamera.orthographicSize = challengeCameraSize;
                location.locationCamera.fieldOfView = challengeCameraFieldOfView;
            }
            if (mainCamera != null)
            {
                mainCamera.enabled = challengeMainCameraEnabled;
                mainCamera.transform.SetParent(mainCamParent);
                mainCamera.transform.localPosition = mainCamLocalPos;
                mainCamera.transform.localRotation = mainCamLocalRot;
            }
            RestoreDecorativeCanyons();
            foreach (GameObject ui in buildModeUIElements) if (ui != null) ui.SetActive(false);
            RestoreCapturedStates(uiStateBeforeBuildMode);
            InvokeEventSafely(OnExitBuildMode);
            if (challengeMotor != null) challengeMotor.enabled = challengeMotorEnabled;
            if (challengeInteract != null) challengeInteract.enabled = challengeInteractEnabled;
            InputManager input = currentPlayerTransform != null ? currentPlayerTransform.GetComponent<InputManager>() : null;
            if (input != null) { input.SetPlayerInputEnable(false); input.SetLookEnabled(false); }
            BuildUIController.Instance?.ShowTimer(false);
            currentPlayerTransform = null;
            ActiveBuildLocation = null;
            CurrentContract = null;
            challengeMotor = null;
            challengeInteract = null;
            HideTransitionFader();
        }
        finally { challengeEnding = false; }
    }
}
