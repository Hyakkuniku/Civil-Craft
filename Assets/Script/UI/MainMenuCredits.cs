using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]
public sealed class MainMenuCredits : MonoBehaviour
{
    [SerializeField] private RectTransform safeFrame;
    [SerializeField] private RectTransform viewport;
    [SerializeField] private RectTransform roll;
    [SerializeField] private TMP_Text credits;
    [SerializeField] private RectTransform endCard;
    [SerializeField] private Button exitButton;
    [SerializeField, Min(1f)] private float scrollSpeed = 62f;
    [SerializeField] private bool suspendGameplayInput;

    private Rect lastSafeArea;
    private Vector2Int lastScreen;
    private Vector2 lastViewportSize;
    private float startY;
    private float endY;
    private float position;
    private float openingHold;
    private GameObject previousSelection;
    private InputManager gameplayInput;
    private bool previousMovement, previousLook;

    public void Open()
    {
        if (gameObject.activeSelf) return;
        EventSystem events = EventSystem.current;
        previousSelection = events != null ? events.currentSelectedGameObject : null;
        if (suspendGameplayInput)
        {
            gameplayInput = FindObjectOfType<InputManager>();
            if (gameplayInput != null)
            {
                previousMovement = gameplayInput.IsPlayerInputEnabled;
                previousLook = gameplayInput.IsLookInputEnabled;
                gameplayInput.SetPlayerInputEnable(false);
                gameplayInput.SetLookEnabled(false);
            }
        }

        if (UIPanelCoordinator.Instance != null) UIPanelCoordinator.Instance.OpenPanel(gameObject);
        else
        {
            transform.SetAsLastSibling();
            gameObject.SetActive(true);
        }

        FitSafeArea();
        Canvas.ForceUpdateCanvases();
        LayoutRoll(0f);
        openingHold = 0.85f;
        if (events != null) events.SetSelectedGameObject(exitButton.gameObject);
    }

    public void Close()
    {
        if (!gameObject.activeSelf) return;
        AudioManager.Instance?.PlaySFX("Click");
        if (UIPanelCoordinator.Instance != null) UIPanelCoordinator.Instance.ClosePanel(gameObject);
        else gameObject.SetActive(false);

        if (EventSystem.current != null && previousSelection != null && previousSelection.activeInHierarchy)
            EventSystem.current.SetSelectedGameObject(previousSelection);
        previousSelection = null;
    }

    private void OnDisable()
    {
        if (gameplayInput == null) return;
        gameplayInput.SetPlayerInputEnable(previousMovement);
        gameplayInput.SetLookEnabled(previousLook);
        gameplayInput = null;
    }

    private void Update()
    {
        if (UIPanelCoordinator.Instance != null && !UIPanelCoordinator.Instance.IsOpen(gameObject)) return;
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Close();
            return;
        }

        if (lastSafeArea != Screen.safeArea || lastScreen.x != Screen.width || lastScreen.y != Screen.height)
            FitSafeArea();
        if (lastViewportSize != viewport.rect.size)
            LayoutRoll(Mathf.InverseLerp(startY, endY, position));

        if (openingHold > 0f)
            openingHold -= Time.unscaledDeltaTime;
        else if (position < endY)
            MoveRoll(Mathf.Min(endY, position + scrollSpeed * Time.unscaledDeltaTime));
    }

    private void FitSafeArea()
    {
        lastSafeArea = Screen.safeArea;
        lastScreen = new Vector2Int(Screen.width, Screen.height);
        if (lastScreen.x <= 0 || lastScreen.y <= 0) return;

        var screenSize = new Vector2(lastScreen.x, lastScreen.y);
        safeFrame.anchorMin = Vector2.Scale(lastSafeArea.min, new Vector2(1f / screenSize.x, 1f / screenSize.y));
        safeFrame.anchorMax = Vector2.Scale(lastSafeArea.max, new Vector2(1f / screenSize.x, 1f / screenSize.y));
    }

    private void LayoutRoll(float progress)
    {
        lastViewportSize = viewport.rect.size;
        RectTransform body = credits.rectTransform;
        float height = Mathf.Ceil(credits.GetPreferredValues(credits.text, body.rect.width, Mathf.Infinity).y) + 16f;
        body.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        float endCardTop = -body.anchoredPosition.y + height + 78f;
        endCard.anchoredPosition = new Vector2(0f, -endCardTop);

        startY = -lastViewportSize.y * .13f;
        endY = endCardTop + endCard.rect.height * .5f - lastViewportSize.y * .5f;
        MoveRoll(Mathf.Lerp(startY, endY, progress));
    }

    private void MoveRoll(float y)
    {
        position = y;
        roll.anchoredPosition = new Vector2(0f, y);
    }
}
