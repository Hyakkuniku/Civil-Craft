using System.Collections;
using System;
using Unity.Netcode;
using Unity.Services.Relay;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

[System.Serializable]
public class BridgePartData
{
    public GameObject bridgePart;
    public Transform floatingPosition; 
    public Transform attachedPosition; 
}

[System.Serializable] 
public class ModeData
{
    public string modeName; 
    [TextArea(2, 4)] public string description;
    public GameObject uiButton;
    public Transform cameraTarget;
    public BridgePartData[] bridgeParts; 
}

public class ModeSelectionManager : MonoBehaviour
{
    public ModeData[] modes; 
    
    [Header("General Settings")]
    public Camera mainCamera;
    [Tooltip("Speed of the camera panning.")]
    public float transitionSpeed = 2f; 

    [Header("UI Navigation Buttons")]
    public GameObject previousButton; // <-- Assign in Inspector
    public GameObject nextButton;     // <-- Assign in Inspector

    private int currentIndex = 0;
    private Coroutine cameraCoroutine;
    [Header("Scene Presentation References")]
    [SerializeField] private TMP_Text modeHeading, modeDescription;
    [SerializeField] private CanvasGroup descriptionGroup;
    private Coroutine presentationCoroutine;

    [Header("Multiplayer Entry")]
    [SerializeField] private string multiplayerSceneName = "Multiplayer";
    [SerializeField] private string hostPreferenceKey = "MultiplayerSessionRole";
    [SerializeField] private string roomCodePreferenceKey = "MultiplayerRoomCode";
    [SerializeField, Range(4, 8)] private int roomCodeLength = 6;
    [SerializeField] private GameObject multiplayerEntryPanel;
    [SerializeField] private CanvasGroup multiplayerEntryGroup;
    [SerializeField] private TMP_Text multiplayerPromptText;
    [SerializeField] private GameObject hostButtonObject;
    [SerializeField] private GameObject joinButtonObject;
    [SerializeField] private TMP_Text hostButtonLabel;
    [SerializeField] private TMP_Text joinButtonLabel;
    [SerializeField] private TMP_InputField roomCodeInput;
    private Coroutine multiplayerEntryCoroutine;
    private Coroutine roomCodeFocusCoroutine;
    private string defaultMultiplayerPrompt;
    private bool hostCodeReady;
    private bool hostStartInProgress;
    private int hostRequestVersion;
    private bool preserveHostForSceneTransition;
    private bool joinCodeEntryReady;
    private bool joinStartInProgress;
    private int joinRequestVersion;
    private bool preserveClientForSceneTransition;

    void Start()
    {
        if (modes == null || modes.Length == 0) return;
        if (multiplayerPromptText != null)
            defaultMultiplayerPrompt = multiplayerPromptText.text;
        if (roomCodeInput != null)
            roomCodeInput.onSubmit.AddListener(HandleRoomCodeSubmitted);

        // Instantly snap all bridges to their correct starting states
        for (int i = 0; i < modes.Length; i++)
        {
            // If we start at index 0, NOTHING should attach. Everything floats.
            bool shouldAttach = (i == currentIndex && currentIndex != 0);
            SetModeBridges(modes[i], shouldAttach, true);
        }

        UpdateUI();
        MoveCamera(true); 
        MultiplayerConnectionManager connection = FindObjectOfType<MultiplayerConnectionManager>(true);
        if (connection != null && PlayerPrefs.GetString(hostPreferenceKey) == "Join")
        {
            if (connection.IsClientConnected)
                ShowClientWaitingState();
            else if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient)
            {
                preserveClientForSceneTransition = true;
                StartCoroutine(ShowWaitingWhenConnected(connection));
            }
        }
    }

    private void OnDestroy()
    {
        if (roomCodeInput != null)
            roomCodeInput.onSubmit.RemoveListener(HandleRoomCodeSubmitted);

        if (!preserveHostForSceneTransition)
        {
            MultiplayerConnectionManager connection = FindObjectOfType<MultiplayerConnectionManager>(true);
            if (connection != null) connection.StopHosting();
        }

        if (!preserveClientForSceneTransition)
        {
            MultiplayerConnectionManager connection = FindObjectOfType<MultiplayerConnectionManager>(true);
            if (connection != null && !connection.IsClientConnected) connection.StopJoining();
        }
    }

    public void NextMode() { ChangeMode(1); }
    public void PreviousMode() { ChangeMode(-1); }

    private void ChangeMode(int direction)
    {
        if (modes == null || modes.Length == 0) return;
        CloseMultiplayerPanel();
        int previousIndex = currentIndex;

        currentIndex += direction;
        
        // Clamp the index to prevent out-of-bounds instead of looping
        if (currentIndex >= modes.Length) currentIndex = modes.Length - 1;
        if (currentIndex < 0) currentIndex = 0;

        // If the index didn't change (e.g., trying to go previous on index 0), stop here
        if (currentIndex == previousIndex) return;

        UpdateUI();
        MoveCamera(false);

        // --- THE NEW INDEX 0 RULE ---
        if (currentIndex == 0)
        {
            // If we arrived back at Story Mode (0), force ALL pieces to detach and float
            for (int i = 0; i < modes.Length; i++)
            {
                SetModeBridges(modes[i], false, false);
            }
        }
        else
        {
            // Normal behavior for other modes: detach the old, attach the new
            SetModeBridges(modes[previousIndex], false, false);
            SetModeBridges(modes[currentIndex], true, false);
        }
    }

    /// <summary>Opens the authored Host/Join panel in the Mode Selection scene.</summary>
    public void OpenMultiplayerPanel()
    {
        if (multiplayerEntryPanel == null || multiplayerEntryGroup == null)
        {
            Debug.LogError("Assign the authored Multiplayer Entry Panel and its CanvasGroup.", this);
            return;
        }

        ResetMultiplayerEntryPresentation(true);
        multiplayerEntryPanel.SetActive(true);
        if (previousButton != null) previousButton.SetActive(false);
        if (nextButton != null) nextButton.SetActive(false);

        if (multiplayerEntryCoroutine != null) StopCoroutine(multiplayerEntryCoroutine);
        multiplayerEntryCoroutine = StartCoroutine(RevealMultiplayerPanel());
    }

    public void CloseMultiplayerPanel()
    {
        if (multiplayerEntryCoroutine != null)
        {
            StopCoroutine(multiplayerEntryCoroutine);
            multiplayerEntryCoroutine = null;
        }

        if (multiplayerEntryPanel != null)
            multiplayerEntryPanel.SetActive(false);

        ResetMultiplayerEntryPresentation(true);

        if (modes != null && modes.Length > 0)
        {
            if (previousButton != null) previousButton.SetActive(currentIndex > 0);
            if (nextButton != null) nextButton.SetActive(currentIndex < modes.Length - 1);
        }
    }

    public async void HostMultiplayer()
    {
        if (hostStartInProgress) return;

        if (hostCodeReady)
        {
            BeginHostMultiplayerSession();
            return;
        }

        MultiplayerConnectionManager connection = FindObjectOfType<MultiplayerConnectionManager>(true);
        if (connection == null)
        {
            ShowHostError("NETWORK MANAGER IS MISSING.");
            return;
        }

        int requestVersion = ++hostRequestVersion;
        hostStartInProgress = true;
        if (multiplayerPromptText != null)
            multiplayerPromptText.text = "CREATING ONLINE ROOM...";
        if (hostButtonLabel != null) hostButtonLabel.text = "CREATING...";
        SetHostButtonInteractable(false);
        if (joinButtonObject != null) joinButtonObject.SetActive(false);

        try
        {
            string roomCode = await connection.StartHostWithRelayAsync();
            if (this == null || requestVersion != hostRequestVersion ||
                multiplayerEntryPanel == null || !multiplayerEntryPanel.activeInHierarchy)
            {
                return;
            }

            PlayerPrefs.SetString(roomCodePreferenceKey, roomCode);
            PlayerPrefs.Save();
            hostCodeReady = true;
            if (multiplayerPromptText != null)
                multiplayerPromptText.text =
                    $"ROOM CODE\n<size=44><b>{roomCode}</b></size>\nShare this code with Player 2.";
            if (hostButtonLabel != null) hostButtonLabel.text = "START GAME";
        }
        catch (OperationCanceledException)
        {
            // Closing the panel cancels an in-flight room request.
        }
        catch (Exception exception)
        {
            Debug.LogError($"[Multiplayer] Could not create Relay room: {exception}", this);
            if (this != null && requestVersion == hostRequestVersion)
                ShowHostError("COULD NOT CREATE ROOM. CHECK YOUR CONNECTION AND TRY AGAIN.");
        }
        finally
        {
            if (this != null && requestVersion == hostRequestVersion)
            {
                hostStartInProgress = false;
                SetHostButtonInteractable(true);
            }
        }
    }

    private void ShowHostError(string message)
    {
        if (multiplayerPromptText != null) multiplayerPromptText.text = message;
        if (hostButtonLabel != null) hostButtonLabel.text = "HOST GAME";
        if (joinButtonObject != null) joinButtonObject.SetActive(true);
    }

    private void SetHostButtonInteractable(bool interactable)
    {
        if (hostButtonObject == null) return;
        Button button = hostButtonObject.GetComponent<Button>();
        if (button != null) button.interactable = interactable;
    }

    public async void JoinMultiplayer()
    {
        if (joinStartInProgress) return;

        if (!joinCodeEntryReady)
        {
            joinCodeEntryReady = true;
            PlayerPrefs.DeleteKey(roomCodePreferenceKey);

            if (multiplayerPromptText != null)
                multiplayerPromptText.text = "ENTER THE ROOM CODE CREATED BY PLAYER 1.";
            if (hostButtonObject != null) hostButtonObject.SetActive(false);
            if (roomCodeInput != null)
            {
                roomCodeInput.gameObject.SetActive(true);
                roomCodeInput.enabled = true;
                roomCodeInput.interactable = true;
                roomCodeInput.readOnly = false;
                roomCodeInput.text = string.Empty;
                FocusRoomCodeInputNextFrame();
            }
            if (joinButtonLabel != null) joinButtonLabel.text = "CONNECT";
            return;
        }

        string roomCode = NormalizeRoomCode(roomCodeInput != null ? roomCodeInput.text : string.Empty);
        if (!IsValidRoomCode(roomCode))
        {
            if (multiplayerPromptText != null)
                multiplayerPromptText.text = $"ENTER A VALID {Mathf.Clamp(roomCodeLength, 4, 8)}-CHARACTER ROOM CODE.";
            if (roomCodeInput != null)
            {
                roomCodeInput.text = roomCode;
                FocusRoomCodeInputNextFrame();
            }
            return;
        }

        MultiplayerConnectionManager connection = FindObjectOfType<MultiplayerConnectionManager>(true);
        if (connection == null)
        {
            ShowJoinError("NETWORK MANAGER IS MISSING.");
            return;
        }

        int requestVersion = ++joinRequestVersion;
        joinStartInProgress = true;
        preserveClientForSceneTransition = true;
        if (multiplayerPromptText != null) multiplayerPromptText.text = "CONNECTING TO HOST...";
        if (joinButtonLabel != null) joinButtonLabel.text = "CONNECTING...";
        SetJoinButtonInteractable(false);
        if (roomCodeInput != null) roomCodeInput.interactable = false;

        PlayerPrefs.SetString(roomCodePreferenceKey, roomCode);
        PlayerPrefs.SetString(hostPreferenceKey, "Join");
        PlayerPrefs.Save();

        try
        {
            await connection.JoinWithRelayAsync(roomCode);
            if (this == null || requestVersion != joinRequestVersion) return;

            ShowClientWaitingState();
        }
        catch (OperationCanceledException)
        {
            // Closing the panel cancels an in-flight connection attempt.
        }
        catch (RelayServiceException exception)
        {
            Debug.LogWarning($"[Multiplayer] Relay join failed: {exception}", this);
            if (this != null && requestVersion == joinRequestVersion)
            {
                bool invalidCode = exception.Reason == RelayExceptionReason.JoinCodeNotFound ||
                                   exception.Reason == RelayExceptionReason.AllocationNotFound ||
                                   exception.Reason == RelayExceptionReason.EntityNotFound ||
                                   exception.Reason == RelayExceptionReason.InvalidArgument ||
                                   exception.Reason == RelayExceptionReason.InvalidRequest;
                ShowJoinError(invalidCode
                    ? "ROOM NOT FOUND. CHECK THE CODE OR ASK THE HOST TO CREATE A NEW ROOM."
                    : "COULD NOT REACH RELAY. CHECK YOUR CONNECTION AND TRY AGAIN.");
            }
        }
        catch (Exception exception)
        {
            Debug.LogError($"[Multiplayer] Could not join room: {exception}", this);
            if (this != null && requestVersion == joinRequestVersion)
                ShowJoinError("COULD NOT CONNECT TO HOST. CHECK THE CODE AND TRY AGAIN.");
        }
        finally
        {
            if (this != null && requestVersion == joinRequestVersion)
            {
                joinStartInProgress = false;
                SetJoinButtonInteractable(true);
                if (roomCodeInput != null) roomCodeInput.interactable = true;
            }
        }
    }

    private void ShowJoinError(string message)
    {
        preserveClientForSceneTransition = false;
        PlayerPrefs.DeleteKey(roomCodePreferenceKey);
        PlayerPrefs.DeleteKey(hostPreferenceKey);
        PlayerPrefs.Save();
        if (multiplayerPromptText != null) multiplayerPromptText.text = message;
        if (joinButtonLabel != null) joinButtonLabel.text = "CONNECT";
        if (roomCodeInput != null) roomCodeInput.interactable = true;
        FocusRoomCodeInputNextFrame();
    }

    private void ShowClientWaitingState()
    {
        preserveClientForSceneTransition = true;
        if (multiplayerEntryPanel != null) multiplayerEntryPanel.SetActive(true);
        if (multiplayerEntryGroup != null)
        {
            multiplayerEntryGroup.alpha = 1f;
            multiplayerEntryGroup.interactable = true;
            multiplayerEntryGroup.blocksRaycasts = true;
        }
        if (multiplayerPromptText != null)
            multiplayerPromptText.text = "CONNECTED. WAITING FOR HOST TO START...";
        if (roomCodeInput != null) roomCodeInput.gameObject.SetActive(false);
        if (hostButtonObject != null) hostButtonObject.SetActive(false);
        if (joinButtonObject != null) joinButtonObject.SetActive(false);
        if (previousButton != null) previousButton.SetActive(false);
        if (nextButton != null) nextButton.SetActive(false);
    }

    private IEnumerator ShowWaitingWhenConnected(MultiplayerConnectionManager connection)
    {
        while (connection != null && NetworkManager.Singleton != null &&
               NetworkManager.Singleton.IsClient && !connection.IsClientConnected)
            yield return null;

        if (connection != null && connection.IsClientConnected)
            ShowClientWaitingState();
    }

    private void SetJoinButtonInteractable(bool interactable)
    {
        if (joinButtonObject == null) return;
        Button button = joinButtonObject.GetComponent<Button>();
        if (button != null) button.interactable = interactable;
    }

    private void HandleRoomCodeSubmitted(string _)
    {
        if (joinCodeEntryReady) JoinMultiplayer();
    }

    private void FocusRoomCodeInputNextFrame()
    {
        if (roomCodeFocusCoroutine != null)
            StopCoroutine(roomCodeFocusCoroutine);
        roomCodeFocusCoroutine = StartCoroutine(FocusRoomCodeInput());
    }

    private IEnumerator FocusRoomCodeInput()
    {
        // Wait until the Join button has finished processing its pointer click;
        // otherwise it can reclaim selection from the newly revealed input.
        yield return null;

        if (roomCodeInput != null && roomCodeInput.gameObject.activeInHierarchy)
        {
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(roomCodeInput.gameObject);
            roomCodeInput.Select();
            roomCodeInput.ActivateInputField();
        }

        roomCodeFocusCoroutine = null;
    }

    private void ResetMultiplayerEntryPresentation(bool clearStoredCode)
    {
        ++hostRequestVersion;
        ++joinRequestVersion;
        hostStartInProgress = false;
        joinStartInProgress = false;
        preserveClientForSceneTransition = false;
        if (clearStoredCode)
        {
            MultiplayerConnectionManager connection = FindObjectOfType<MultiplayerConnectionManager>(true);
            if (connection != null)
            {
                connection.StopHosting();
                connection.StopJoining();
            }
        }

        if (roomCodeFocusCoroutine != null)
        {
            StopCoroutine(roomCodeFocusCoroutine);
            roomCodeFocusCoroutine = null;
        }

        hostCodeReady = false;
        joinCodeEntryReady = false;
        if (multiplayerPromptText != null && !string.IsNullOrEmpty(defaultMultiplayerPrompt))
            multiplayerPromptText.text = defaultMultiplayerPrompt;
        if (hostButtonLabel != null) hostButtonLabel.text = "HOST GAME";
        SetHostButtonInteractable(true);
        SetJoinButtonInteractable(true);
        if (joinButtonLabel != null) joinButtonLabel.text = "JOIN GAME";
        if (hostButtonObject != null) hostButtonObject.SetActive(true);
        if (joinButtonObject != null) joinButtonObject.SetActive(true);
        if (roomCodeInput != null)
        {
            roomCodeInput.text = string.Empty;
            roomCodeInput.gameObject.SetActive(false);
        }

        if (clearStoredCode)
        {
            PlayerPrefs.DeleteKey(roomCodePreferenceKey);
            PlayerPrefs.DeleteKey(hostPreferenceKey);
            PlayerPrefs.Save();
        }
    }

    private bool IsValidRoomCode(string roomCode)
    {
        if (roomCode.Length != Mathf.Clamp(roomCodeLength, 4, 8)) return false;
        for (int i = 0; i < roomCode.Length; i++)
        {
            if (!char.IsLetterOrDigit(roomCode[i])) return false;
        }
        return true;
    }

    private static string NormalizeRoomCode(string rawCode)
    {
        if (string.IsNullOrWhiteSpace(rawCode)) return string.Empty;

        System.Text.StringBuilder normalized = new System.Text.StringBuilder(rawCode.Length);
        for (int i = 0; i < rawCode.Length; i++)
        {
            char character = char.ToUpperInvariant(rawCode[i]);
            if (!char.IsWhiteSpace(character) && character != '-')
                normalized.Append(character);
        }
        return normalized.ToString();
    }

    private void BeginHostMultiplayerSession()
    {
        MultiplayerConnectionManager connection = FindObjectOfType<MultiplayerConnectionManager>(true);
        NetworkManager manager = NetworkManager.Singleton;
        if (connection == null || !connection.IsHosting || manager == null || manager.SceneManager == null)
        {
            ShowHostError("ROOM IS NO LONGER ACTIVE. HOST AGAIN.");
            hostCodeReady = false;
            return;
        }

        SceneEventProgressStatus status = manager.SceneManager.LoadScene(multiplayerSceneName, LoadSceneMode.Single);
        if (status != SceneEventProgressStatus.Started)
        {
            Debug.LogError($"[Multiplayer] Could not load '{multiplayerSceneName}' through Netcode: {status}", this);
            if (multiplayerPromptText != null) multiplayerPromptText.text = "COULD NOT START GAME. TRY AGAIN.";
            return;
        }

        preserveHostForSceneTransition = true;
        PlayerPrefs.SetString(hostPreferenceKey, "Host");
        PlayerPrefs.Save();
    }

    private IEnumerator RevealMultiplayerPanel()
    {
        multiplayerEntryGroup.alpha = 0f;
        multiplayerEntryGroup.interactable = false;
        multiplayerEntryGroup.blocksRaycasts = true;

        float elapsed = 0f;
        const float duration = 0.18f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            multiplayerEntryGroup.alpha = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            yield return null;
        }

        multiplayerEntryGroup.alpha = 1f;
        multiplayerEntryGroup.interactable = true;
        multiplayerEntryCoroutine = null;
    }

    private void UpdateUI()
    {
        // Update individual mode buttons
        for (int i = 0; i < modes.Length; i++)
        {
            if (modes[i].uiButton != null) modes[i].uiButton.SetActive(i == currentIndex);
        }

        // Hide/Show Next and Previous buttons based on the current index limits
        if (previousButton != null) previousButton.SetActive(currentIndex > 0);
        if (nextButton != null) nextButton.SetActive(currentIndex < modes.Length - 1);
        ModeData mode = modes[currentIndex];
        bool multiplayer = mode.modeName.ToLowerInvariant().Contains("multi");
        if (modeHeading != null) modeHeading.text = mode.modeName.ToUpperInvariant();
        if (modeDescription != null)
        {
            modeDescription.text = !string.IsNullOrWhiteSpace(mode.description) ? mode.description :
                multiplayer ? "Bring your friends along. Choose multiplayer to begin your next building adventure together." :
                "Explore the canyon, meet its people and take on bridge-building contracts. Learn, build and connect the community at your own pace.";
        }
        if (descriptionGroup != null)
        {
            // Occupy the inactive navigation side: Story left, Multiplayer right.
            RectTransform card = (RectTransform)descriptionGroup.transform;
            card.anchorMin = new Vector2(multiplayer ? .69f : .04f, .32f);
            card.anchorMax = new Vector2(multiplayer ? .96f : .31f, .75f);
            card.anchoredPosition = Vector2.zero;
            card.sizeDelta = Vector2.zero;
            if (presentationCoroutine != null) StopCoroutine(presentationCoroutine);
            presentationCoroutine = StartCoroutine(RevealDescription());
        }
    }

    private IEnumerator RevealDescription()
    {
        float elapsed = 0f;
        while (elapsed < .22f)
        {
            elapsed += Time.unscaledDeltaTime;
            descriptionGroup.alpha = Mathf.Lerp(.4f, 1f, Mathf.SmoothStep(0f, 1f, elapsed / .22f));
            yield return null;
        }
        descriptionGroup.alpha = 1f;
        presentationCoroutine = null;
    }

    private void SetModeBridges(ModeData mode, bool isAttached, bool snapInstantly)
    {
        if (mode == null || mode.bridgeParts == null) return;
        foreach (BridgePartData partData in mode.bridgeParts)
        {
            if (partData.bridgePart == null) continue;

            Transform targetTransform = isAttached ? partData.attachedPosition : partData.floatingPosition;
            if (targetTransform == null) continue;

            FloatingObject floater = partData.bridgePart.GetComponent<FloatingObject>();
            if (floater != null)
            {
                // Send the command directly to the object
                floater.SetTarget(targetTransform, !isAttached, snapInstantly);
            }
        }
    }

    private void MoveCamera(bool snap)
    {
        if (modes.Length == 0 || mainCamera == null) return;
        
        Transform target = modes[currentIndex].cameraTarget;
        if (target == null) return;

        if (snap)
        {
            mainCamera.transform.position = target.position;
            mainCamera.transform.rotation = target.rotation;
        }
        else
        {
            if (cameraCoroutine != null) StopCoroutine(cameraCoroutine);
            cameraCoroutine = StartCoroutine(SmoothMoveCamera(target));
        }
    }

    private IEnumerator SmoothMoveCamera(Transform target)
    {
        Vector3 startPos = mainCamera.transform.position;
        Quaternion startRot = mainCamera.transform.rotation;
        float progress = 0f;

        while (progress < 1f)
        {
            progress += Time.deltaTime * transitionSpeed;
            float smoothProgress = Mathf.SmoothStep(0f, 1f, progress);

            mainCamera.transform.position = Vector3.Lerp(startPos, target.position, smoothProgress);
            mainCamera.transform.rotation = Quaternion.Lerp(startRot, target.rotation, smoothProgress);
            yield return null;
        }
        
        mainCamera.transform.position = target.position;
        mainCamera.transform.rotation = target.rotation;
    }
}
