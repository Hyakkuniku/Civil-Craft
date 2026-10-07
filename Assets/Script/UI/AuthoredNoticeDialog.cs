using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>A scene-authored account notice. Runtime only changes copy, callbacks and visibility.</summary>
public sealed class AuthoredNoticeDialog : MonoBehaviour
{
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text messageText;
    [SerializeField] private Button closeButton;
    [SerializeField] private Button switchAccountButton;
    [SerializeField] private Button logoutButton;
    private Action switchAccount;
    private Action logout;
    private GameObject previousSelection;

    public bool IsVisible => gameObject.activeInHierarchy;

    private void OnEnable()
    {
        UIReservedRegionLayout.Register(transform as RectTransform);
    }

    public bool Show(string title, string message, Action onSwitchAccount, Action onLogout)
    {
        if (titleText == null || messageText == null || closeButton == null ||
            switchAccountButton == null || logoutButton == null ||
            onSwitchAccount == null || onLogout == null)
        {
            Debug.LogError("The scene-authored notice is missing UI references.", this);
            return false;
        }
        if (!IsVisible)
            previousSelection = EventSystem.current != null
                ? EventSystem.current.currentSelectedGameObject : null;
        titleText.text = title;
        messageText.text = message;
        switchAccount = onSwitchAccount;
        logout = onLogout;
        closeButton.onClick.RemoveListener(Close);
        closeButton.onClick.AddListener(Close);
        switchAccountButton.onClick.RemoveListener(SwitchAccount);
        switchAccountButton.onClick.AddListener(SwitchAccount);
        logoutButton.onClick.RemoveListener(LogOut);
        logoutButton.onClick.AddListener(LogOut);
        gameObject.SetActive(true);
        if (!IsVisible)
        {
            Close();
            return false;
        }
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(closeButton.gameObject);
        return true;
    }

    public void Close()
    {
        ClearActions();
        gameObject.SetActive(false);
        RestoreSelection();
    }

    private void SwitchAccount() => Choose(switchAccount);
    private void LogOut() => Choose(logout);

    private void Choose(Action action)
    {
        // Hidden/stale and repeated button callbacks cannot change the session.
        if (!IsVisible || action == null) return;
        Close();
        action.Invoke();
    }

    private void ClearActions()
    {
        if (closeButton != null) closeButton.onClick.RemoveListener(Close);
        if (switchAccountButton != null) switchAccountButton.onClick.RemoveListener(SwitchAccount);
        if (logoutButton != null) logoutButton.onClick.RemoveListener(LogOut);
        switchAccount = logout = null;
    }

    private void OnDisable()
    {
        UIReservedRegionLayout.Unregister(transform as RectTransform);
        ClearActions();
        RestoreSelection();
    }

    private void RestoreSelection()
    {
        if (EventSystem.current != null)
        {
            if (previousSelection != null && previousSelection.activeInHierarchy)
                EventSystem.current.SetSelectedGameObject(previousSelection);
            else if (EventSystem.current.currentSelectedGameObject != null &&
                EventSystem.current.currentSelectedGameObject.transform.IsChildOf(transform))
                EventSystem.current.SetSelectedGameObject(null);
        }
        previousSelection = null;
    }

    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        bool backPressed = (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) ||
            (Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame);
#elif ENABLE_LEGACY_INPUT_MANAGER
        bool backPressed = Input.GetKeyDown(KeyCode.Escape);
#else
        bool backPressed = false;
#endif
        if (backPressed) Close();
    }

#if UNITY_EDITOR
    public void SetAuthoringReferences(TMP_Text title, TMP_Text message, Button close,
        Button switchButton, Button signOutButton)
    {
        titleText = title;
        messageText = message;
        closeButton = close;
        switchAccountButton = switchButton;
        logoutButton = signOutButton;
    }
#endif
}
