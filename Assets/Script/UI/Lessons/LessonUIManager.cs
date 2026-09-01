using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[DefaultExecutionOrder(-25)]
public sealed class LessonUIManager : MonoBehaviour
{
    public static LessonUIManager Instance { get; private set; }

    [Header("Panel")]
    [Tooltip("The complete lesson window. Keep the manager on an always-active Canvas or manager object, not on this panel.")]
    [SerializeField] private GameObject lessonPanel;
    [SerializeField] private TMP_Text lessonTitleText;
    [SerializeField] private Image lessonImage;
    [SerializeField] private TMP_Text lessonDescriptionText;
    [SerializeField] private Button closeButton;

    [Header("Scrolling")]
    [SerializeField] private ScrollRect descriptionScrollRect;
    [Tooltip("The Scroll View Content RectTransform that grows with the description.")]
    [SerializeField] private RectTransform descriptionContent;

    [Header("Behavior")]
    [Tooltip("Uses the project's centralized panel coordinator to hide the HUD and other large panels.")]
    [SerializeField] private bool usePanelCoordinator = true;
    [Tooltip("Temporarily disables overworld movement and camera input while reading.")]
    [SerializeField] private bool lockPlayerControls = true;
    [SerializeField] private bool hideImageWhenMissing = true;

    [Header("Canvas Exclusivity")]
    [Tooltip("While a lesson is open, disable every other currently-rendering Canvas and restore its exact state afterward.")]
    [SerializeField] private bool hideOtherCanvases = true;
    [Tooltip("The Canvas containing Lesson Panel. This is found automatically when left empty.")]
    [SerializeField] private Canvas lessonCanvas;
    [Tooltip("Optional canvases that are allowed to remain visible with the lesson.")]
    [SerializeField] private List<Canvas> canvasesToKeepVisible = new List<Canvas>();

    private sealed class CanvasState
    {
        public Canvas canvas;
        public bool wasEnabled;
    }

    private Coroutine resetScrollRoutine;
    private readonly List<CanvasState> hiddenCanvasStates = new List<CanvasState>();
    private InputManager lockedInputManager;
    private PlayerMotor lockedPlayerMotor;
    private bool restoreMovementInput;
    private bool restoreLookInput;
    private bool restorePlayerMotor;
    private bool controlsAreLocked;

    public LessonData CurrentLesson { get; private set; }
    public GameObject Panel => lessonPanel;
    public bool IsOpen => lessonPanel != null && lessonPanel.activeSelf;

    public event Action<LessonData> LessonOpened;
    public event Action<LessonData> LessonClosed;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[LessonUIManager] Duplicate manager disabled.", this);
            enabled = false;
            return;
        }

        Instance = this;

        if (closeButton != null)
            closeButton.onClick.AddListener(CloseLesson);

        if (lessonPanel != null)
        {
            if (lessonCanvas == null)
                lessonCanvas = lessonPanel.GetComponentInParent<Canvas>();

            RepairCollapsedLessonCanvas();
            lessonPanel.SetActive(false);
        }
    }

    private void OnDisable()
    {
        RestoreHiddenCanvases();
    }

    private void OnDestroy()
    {
        if (closeButton != null)
            closeButton.onClick.RemoveListener(CloseLesson);

        RestoreHiddenCanvases();
        RestorePlayerControls();
        if (Instance == this) Instance = null;
    }

    public void ShowLesson(LessonData lesson)
    {
        if (lesson == null)
        {
            Debug.LogWarning("[LessonUIManager] Cannot show a null LessonData asset.", this);
            return;
        }

        if (lessonPanel == null || lessonTitleText == null || lessonDescriptionText == null)
        {
            Debug.LogError("[LessonUIManager] Required lesson UI references are missing.", this);
            return;
        }

        RepairCollapsedLessonCanvas();

        // A lesson becomes part of the Almanac archive only once it has actually
        // been displayed successfully to the player.
        LessonSaveManager.Unlock(lesson);

        bool wasAlreadyOpen = IsOpen;
        CurrentLesson = lesson;
        lessonTitleText.text = lesson.Title;
        lessonDescriptionText.text = lesson.Description;

        if (lessonImage != null)
        {
            lessonImage.sprite = lesson.Image;
            lessonImage.preserveAspect = true;
            lessonImage.gameObject.SetActive(lesson.Image != null || !hideImageWhenMissing);
        }

        if (usePanelCoordinator && UIPanelCoordinator.Instance != null)
            UIPanelCoordinator.Instance.OpenPanel(lessonPanel);
        else
            lessonPanel.SetActive(true);

        lessonPanel.transform.SetAsLastSibling();

        if (!wasAlreadyOpen)
        {
            HideOtherActiveCanvases();
            LockPlayerControls();
        }

        if (resetScrollRoutine != null)
            StopCoroutine(resetScrollRoutine);
        resetScrollRoutine = StartCoroutine(ResetScrollToTopNextFrame());

        LessonOpened?.Invoke(lesson);
    }

    public void CloseLesson()
    {
        if (!IsOpen)
        {
            RestoreHiddenCanvases();
            RestorePlayerControls();
            return;
        }

        LessonData closedLesson = CurrentLesson;

        if (resetScrollRoutine != null)
        {
            StopCoroutine(resetScrollRoutine);
            resetScrollRoutine = null;
        }

        if (usePanelCoordinator && UIPanelCoordinator.Instance != null)
            UIPanelCoordinator.Instance.ClosePanel(lessonPanel);
        else
            lessonPanel.SetActive(false);

        CurrentLesson = null;
        RestoreHiddenCanvases();
        RestorePlayerControls();
        LessonClosed?.Invoke(closedLesson);
    }

    private IEnumerator ResetScrollToTopNextFrame()
    {
        yield return null;
        Canvas.ForceUpdateCanvases();

        if (descriptionContent != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(descriptionContent);

        Canvas.ForceUpdateCanvases();
        if (descriptionScrollRect != null)
        {
            descriptionScrollRect.StopMovement();
            descriptionScrollRect.verticalNormalizedPosition = 1f;
        }

        resetScrollRoutine = null;
    }

    private void HideOtherActiveCanvases()
    {
        if (!hideOtherCanvases) return;

        // Clear stale state defensively in case a panel was interrupted without
        // passing through CloseLesson.
        RestoreHiddenCanvases();

        if (lessonCanvas == null && lessonPanel != null)
            lessonCanvas = lessonPanel.GetComponentInParent<Canvas>();

        Canvas[] sceneCanvases = FindObjectsOfType<Canvas>(true);
        foreach (Canvas canvas in sceneCanvases)
        {
            if (canvas == null || !canvas.gameObject.scene.IsValid() ||
                !canvas.enabled || !canvas.gameObject.activeInHierarchy ||
                IsPartOfLessonUI(canvas) || canvasesToKeepVisible.Contains(canvas))
                continue;

            hiddenCanvasStates.Add(new CanvasState
            {
                canvas = canvas,
                wasEnabled = canvas.enabled
            });
            canvas.enabled = false;
        }
    }

    private void RepairCollapsedLessonCanvas()
    {
        if (lessonCanvas == null)
            return;

        Transform canvasTransform = lessonCanvas.transform;
        Vector3 scale = canvasTransform.localScale;
        if (Mathf.Abs(scale.x) > 0.0001f &&
            Mathf.Abs(scale.y) > 0.0001f &&
            Mathf.Abs(scale.z) > 0.0001f)
        {
            return;
        }

        canvasTransform.localScale = Vector3.one;
        Debug.LogWarning(
            "[LessonUIManager] LessonCanvas had a zero scale and could not render. Its scale was restored to (1,1,1).",
            lessonCanvas);
    }

    private bool IsPartOfLessonUI(Canvas canvas)
    {
        if (canvas == lessonCanvas) return true;

        return lessonPanel != null &&
               canvas.transform.IsChildOf(lessonPanel.transform);
    }

    private void RestoreHiddenCanvases()
    {
        foreach (CanvasState state in hiddenCanvasStates)
        {
            if (state.canvas != null)
                state.canvas.enabled = state.wasEnabled;
        }

        hiddenCanvasStates.Clear();
    }

    private void LockPlayerControls()
    {
        if (!lockPlayerControls || controlsAreLocked) return;

        lockedInputManager = FindObjectOfType<InputManager>();
        if (lockedInputManager != null)
        {
            restoreMovementInput = lockedInputManager.IsPlayerInputEnabled;
            restoreLookInput = lockedInputManager.IsLookInputEnabled;
            lockedInputManager.SetPlayerInputEnable(false);
            lockedInputManager.SetLookEnabled(false);
        }

        lockedPlayerMotor = FindObjectOfType<PlayerMotor>();
        if (lockedPlayerMotor != null)
        {
            restorePlayerMotor = lockedPlayerMotor.enabled;
            lockedPlayerMotor.enabled = false;
        }

        controlsAreLocked = true;
    }

    private void RestorePlayerControls()
    {
        if (!controlsAreLocked) return;

        if (lockedInputManager != null)
        {
            lockedInputManager.SetPlayerInputEnable(restoreMovementInput);
            lockedInputManager.SetLookEnabled(restoreLookInput);
        }

        if (lockedPlayerMotor != null)
            lockedPlayerMotor.enabled = restorePlayerMotor;

        lockedInputManager = null;
        lockedPlayerMotor = null;
        controlsAreLocked = false;
    }
}
