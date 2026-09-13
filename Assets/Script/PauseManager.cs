using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class PauseManager : MonoBehaviour
{
    private const string MobilePauseButtonResource = "UI/MobilePauseButton";

    public static PauseManager Instance { get; private set; }

    [Header("UI References")]
    [Tooltip("Drag your main Pause Panel here.")]
    public GameObject pausePanel;
    
    [Tooltip("Drag your Settings Panel here (Optional, to close it when unpausing).")]
    public GameObject settingsPanel; 

    [Tooltip("The existing HUD Pause button. It is hidden for tutorial contracts.")]
    public GameObject pauseButton;

    [Header("Elements to Hide")]
    [Tooltip("Drag any game objects (like HUD elements) here that should disappear when paused.")]
    public GameObject[] objectsToHide; // --- NEW: Array of objects to hide ---

    [Header("Scene Management")]
    [Tooltip("The exact name of your Mode Selection scene.")]
    public string modeSelectionSceneName = "ModeSelection";

    [HideInInspector] public bool isPaused = false;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        // Ensure the pause panel is hidden when the game starts
        if (pausePanel != null) pausePanel.SetActive(false);
    }

    private void Start()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnEnterBuildMode.AddListener(RefreshPauseAvailability);
            GameManager.Instance.OnExitBuildMode.AddListener(RefreshPauseAvailability);
        }

        RefreshPauseAvailability();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnEnterBuildMode.RemoveListener(RefreshPauseAvailability);
            GameManager.Instance.OnExitBuildMode.RemoveListener(RefreshPauseAvailability);
        }
    }

    private void Update()
    {
        DiagnosePauseTaps();
        // Toggle pause with the Escape key
        bool pausePressed = (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) ||
                            (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame);

        if (pausePressed)
        {
            // If the player is building, the GameManager uses Escape to exit build mode. 
            // We don't want to pause the game at the same time!
            if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameManager.GameState.Building)
            {
                return; 
            }

            TogglePause();
        }
    }

    public void TogglePause()
    {
        DiagnosePauseInvocation();
        // If the settings panel is open, pressing Escape should just close settings, not unpause the whole game yet.
        if (isPaused && settingsPanel != null && settingsPanel.activeSelf)
        {
            if (UIPanelCoordinator.Instance != null)
                UIPanelCoordinator.Instance.ClosePanel(settingsPanel);
            else
                settingsPanel.SetActive(false);
            return;
        }

        if (!isPaused && IsPauseBlocked()) return;

        if (isPaused)
        {
            ResumeGame();
        }
        else
        {
            PauseGame();
        }
    }

    public void PauseGame()
    {
        if (IsPauseBlocked())
        {
            RefreshPauseAvailability();
            return;
        }

        isPaused = true;
        Time.timeScale = 0f; // Freezes physics and animations

        if (AudioManager.Instance != null)
            AudioManager.Instance.PauseMusic();
        
        if (UIPanelCoordinator.Instance != null)
            UIPanelCoordinator.Instance.OpenPanel(pausePanel);
        else if (pausePanel != null)
            pausePanel.SetActive(true);

        if (UIPanelCoordinator.Instance == null && objectsToHide != null)
        {
            foreach (GameObject obj in objectsToHide)
            {
                if (obj != null) obj.SetActive(false);
            }
        }

        // Disable player movement and camera look
        InputManager inputObj = FindObjectOfType<InputManager>();
        if (inputObj != null) 
        {
            inputObj.SetPlayerInputEnable(false);
            inputObj.SetLookEnabled(false);
        }
    }

    public void ResumeGame()
    {
        isPaused = false;
        Time.timeScale = 1f; // Unfreezes the game

        if (AudioManager.Instance != null)
            AudioManager.Instance.ResumeMusic();
        
        if (UIPanelCoordinator.Instance != null)
        {
            if (settingsPanel != null && settingsPanel.activeSelf)
                UIPanelCoordinator.Instance.ClosePanel(settingsPanel);
            UIPanelCoordinator.Instance.ClosePanel(pausePanel);
        }
        else
        {
            if (pausePanel != null) pausePanel.SetActive(false);
            if (settingsPanel != null) settingsPanel.SetActive(false);
        }

        // --- NEW: Re-enable the objects in the array ---
        if (UIPanelCoordinator.Instance == null && objectsToHide != null)
        {
            foreach (GameObject obj in objectsToHide)
            {
                if (obj != null) obj.SetActive(true);
            }
        }

        // Re-enable player movement and camera look
        InputManager inputObj = FindObjectOfType<InputManager>();
        if (inputObj != null) 
        {
            inputObj.SetPlayerInputEnable(true);
            inputObj.SetLookEnabled(true);
        }
    }

    public void RefreshPauseAvailability()
    {
        bool resolvedButton = false;
        if (pauseButton == null)
        {
            pauseButton = FindPauseButton();
            resolvedButton = pauseButton != null;
        }

        if (resolvedButton)
            GameplayMobileUILayout.Apply();

        bool pauseAllowed = !IsPauseBlocked();
        if (pauseButton != null) pauseButton.SetActive(pauseAllowed);

        if (!pauseAllowed && isPaused) ResumeGame();
    }

    private static bool IsPauseBlockedByTutorialContract()
    {
        return GameManager.Instance != null &&
               GameManager.Instance.CurrentState == GameManager.GameState.Building &&
               GameManager.Instance.CurrentContract != null &&
               GameManager.Instance.CurrentContract.IsTutorialForCurrentPlayer();
    }

    private static bool IsPauseBlocked()
    {
        // The Main Canvas pause menu belongs to overworld exploration. Build Mode
        // has its own simulation controls and must never expose this pause button.
        bool isInBuildMode = GameManager.Instance != null &&
                             GameManager.Instance.CurrentState == GameManager.GameState.Building;

        return isInBuildMode || IsPauseBlockedByTutorialContract();
    }

    private GameObject FindPauseButton()
    {
        foreach (Button button in FindObjectsOfType<Button>(true))
        {
            for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
            {
                if (button.onClick.GetPersistentTarget(i) == this &&
                    button.onClick.GetPersistentMethodName(i) == nameof(TogglePause))
                {
                    return button.gameObject;
                }
            }
        }

        return CreateMobilePauseButton();
    }

    private GameObject CreateMobilePauseButton()
    {
        RectTransform accessButtons = null;
        foreach (RectTransform candidate in FindObjectsOfType<RectTransform>(true))
        {
            if (candidate != null &&
                candidate.gameObject.scene.IsValid() &&
                candidate.name == "AccessButtons")
            {
                accessButtons = candidate;
                break;
            }
        }

        if (accessButtons == null) return null;

        accessButtons.anchorMin = Vector2.zero;
        accessButtons.anchorMax = Vector2.one;
        accessButtons.pivot = new Vector2(0.5f, 0.5f);
        accessButtons.anchoredPosition = Vector2.zero;
        accessButtons.sizeDelta = Vector2.zero;
        accessButtons.localScale = Vector3.one;

        GameObject prefab = Resources.Load<GameObject>(MobilePauseButtonResource);
        if (prefab == null)
        {
            Debug.LogWarning(
                $"[PauseManager] Missing Resources/{MobilePauseButtonResource}.prefab; " +
                "the scene has no mobile Pause button.",
                this);
            return null;
        }

        GameObject created = Instantiate(prefab, accessButtons, false);
        created.name = "pause_btn";

        Button button = created.GetComponent<Button>();
        if (button != null)
            button.onClick.AddListener(TogglePause);

        return created;
    }

    public void ReturnToModeSelection()
    {
        // CRITICAL: Always reset time scale before loading a new scene, or the next scene will be frozen!
        Time.timeScale = 1f; 
        
        // Ensure the game isn't trying to carry over a paused state
        isPaused = false; 

        SceneManager.LoadScene(modeSelectionSceneName); 
    }

    // Temporary, read-only diagnostics. Calls are omitted from release builds.
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    private void DiagnosePauseInvocation()
    {
        Debug.Log($"[PauseTap] TogglePause RECEIVED frame={Time.frameCount}, " +
            $"paused={isPaused}, blockedByBuildMode={IsPauseBlocked()}, " +
            $"pausePanelActive={pausePanel != null && pausePanel.activeInHierarchy}, " +
            $"settingsActive={settingsPanel != null && settingsPanel.activeSelf}", this);
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    private void DiagnosePauseTaps()
    {
        if (Mouse.current != null)
        {
            if (Mouse.current.leftButton.wasPressedThisFrame)
                DiagnosePausePointer(Mouse.current.position.ReadValue(), "mouse DOWN");
            if (Mouse.current.leftButton.wasReleasedThisFrame)
                DiagnosePausePointer(Mouse.current.position.ReadValue(), "mouse UP");
        }
        if (Touchscreen.current == null) return;
        foreach (var touch in Touchscreen.current.touches)
        {
            if (touch.press.wasPressedThisFrame)
                DiagnosePausePointer(touch.position.ReadValue(), $"touch {touch.touchId.ReadValue()} DOWN");
            if (touch.press.wasReleasedThisFrame)
                DiagnosePausePointer(touch.position.ReadValue(), $"touch {touch.touchId.ReadValue()} UP");
        }
    }

    private void DiagnosePausePointer(Vector2 position, string phase)
    {
        RectTransform rect = pauseButton != null ? pauseButton.transform as RectTransform : null;
        if (rect == null) return;
        Canvas canvas = rect.GetComponentInParent<Canvas>(true);
        Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera : null;
        Vector3[] corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        Vector2 minimum = RectTransformUtility.WorldToScreenPoint(eventCamera, corners[0]);
        Vector2 maximum = minimum;
        for (int i = 1; i < corners.Length; i++)
        {
            Vector2 corner = RectTransformUtility.WorldToScreenPoint(eventCamera, corners[i]);
            minimum = Vector2.Min(minimum, corner);
            maximum = Vector2.Max(maximum, corner);
        }
        // Include a small margin to reveal mismatches around the visible button.
        if (!Rect.MinMaxRect(minimum.x - 24f, minimum.y - 24f,
            maximum.x + 24f, maximum.y + 24f).Contains(position)) return;

        var system = UnityEngine.EventSystems.EventSystem.current;
        var button = pauseButton.GetComponent<Button>();
        var report = new System.Text.StringBuilder();
        report.AppendLine($"[PauseTap] {phase} frame={Time.frameCount} position={position} " +
            $"screen={Screen.width}x{Screen.height} fullscreen={Screen.fullScreen} " +
            $"mode={Screen.fullScreenMode} focused={Application.isFocused} cursor={Cursor.lockState}");
        report.AppendLine($"Pause bounds={minimum}..{maximum}, active={pauseButton.activeInHierarchy}, " +
            $"buttonEnabled={button != null && button.enabled}, interactable={button != null && button.IsInteractable()}, " +
            $"paused={isPaused}, blockedByBuildMode={IsPauseBlocked()}, timeScale={Time.timeScale}");
        report.AppendLine($"EventSystem={system}, active={system != null && system.isActiveAndEnabled}, " +
            $"module={(system != null ? system.currentInputModule : null)}");
        foreach (Canvas parentCanvas in rect.GetComponentsInParent<Canvas>(true))
            report.AppendLine($"Canvas {PauseDiagnosticPath(parentCanvas.transform)}: " +
                $"enabled={parentCanvas.enabled}, mode={parentCanvas.renderMode}, " +
                $"sort={parentCanvas.sortingOrder}, scale={parentCanvas.scaleFactor}");
        foreach (CanvasGroup group in rect.GetComponentsInParent<CanvasGroup>(true))
            report.AppendLine($"CanvasGroup {PauseDiagnosticPath(group.transform)}: " +
                $"alpha={group.alpha}, interactable={group.interactable}, " +
                $"blocksRaycasts={group.blocksRaycasts}, ignoreParents={group.ignoreParentGroups}");
        if (system != null)
        {
            var hits = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
            system.RaycastAll(new UnityEngine.EventSystems.PointerEventData(system) { position = position }, hits);
            report.AppendLine($"Raycast hits={hits.Count} (FIRST is the topmost receiver):");
            for (int i = 0; i < Mathf.Min(hits.Count, 8); i++)
            {
                GameObject target = hits[i].gameObject;
                var graphic = target.GetComponent<Graphic>();
                GameObject clickHandler = UnityEngine.EventSystems.ExecuteEvents
                    .GetEventHandler<UnityEngine.EventSystems.IPointerClickHandler>(target);
                report.AppendLine($"  {i}: {PauseDiagnosticPath(target.transform)}, " +
                    $"graphicAlpha={(graphic != null ? graphic.color.a.ToString() : "n/a")}, " +
                    $"clickHandler={(clickHandler != null ? PauseDiagnosticPath(clickHandler.transform) : "NONE")}");
            }
        }
        Debug.Log(report.ToString(), this);
    }

    private static string PauseDiagnosticPath(Transform target)
    {
        string path = target.name;
        while (target.parent != null)
        {
            target = target.parent;
            path = target.name + "/" + path;
        }
        return path;
    }
}
