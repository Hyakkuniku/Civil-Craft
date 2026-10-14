using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>Public release notes plus a separate, authenticated private inbox. Never owns player save state.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(GameReleaseService))]
public sealed class MainMenuUpdatesController : MonoBehaviour
{
    public Button updatesButton, closeButton, retryButton, downloadButton;
    public Button releasesTabButton, messagesTabButton;
    public GameObject panel, badge;
    public TMP_Text releaseTitleText, releaseDateText, notesText, installedVersionText, availableVersionText, statusText;
    public ScrollRect notesScroll;
    private GameReleaseService service;
    private PlayerMessagesService messages;
    private bool opened;
    private bool showingMessages;

    private void OnEnable()
    {
        service = GetComponent<GameReleaseService>();
        messages = GetComponent<PlayerMessagesService>() ?? gameObject.AddComponent<PlayerMessagesService>();
        messages.Changed -= Render; messages.Changed += Render;
        if (service != null) { service.Changed -= Render; service.Changed += Render; }
        Bind(updatesButton, Open); Bind(closeButton, Close); Bind(retryButton, Retry); Bind(downloadButton, OpenDownload);
        Bind(releasesTabButton, ShowReleases); Bind(messagesTabButton, ShowMessages);
        Render();
    }

    private void Start() { if (service != null) service.BeginMenuSession(); }

    private void OnDisable()
    {
        if (service != null) service.Changed -= Render;
        if (messages != null) { messages.Changed -= Render; messages.SetVisible(false); }
        Unbind(updatesButton, Open); Unbind(closeButton, Close); Unbind(retryButton, Retry); Unbind(downloadButton, OpenDownload);
        Unbind(releasesTabButton, ShowReleases); Unbind(messagesTabButton, ShowMessages);
        if (opened) Close();
    }

    private void Update()
    {
        if (opened && panel != null && panel.activeInHierarchy && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame &&
            (UIPanelCoordinator.Instance == null || UIPanelCoordinator.Instance.IsOpen(panel))) Close();
    }

    public void Open()
    {
        if (panel == null || opened) return;
        opened = true;
        showingMessages = false;
        // Discover messages while the panel is open, even on the release tab.
        if (messages != null) messages.SetVisible(true);
        if (UIPanelCoordinator.Instance != null) UIPanelCoordinator.Instance.OpenPanel(panel);
        else panel.SetActive(true);
        GetComponent<MainMenuUpdatesLayout>()?.ApplyLayout();
        Render();
        if (notesScroll != null) notesScroll.verticalNormalizedPosition = 1f;
        if (service != null) service.RefreshIfStale();
    }

    public void Close()
    {
        opened = false;
        if (messages != null) messages.SetVisible(false);
        if (panel == null) return;
        if (UIPanelCoordinator.Instance != null) UIPanelCoordinator.Instance.ClosePanel(panel);
        else panel.SetActive(false);
    }

    public void ShowReleases()
    {
        showingMessages = false;
        if (notesScroll != null) notesScroll.verticalNormalizedPosition = 1f;
        Render();
        if (opened && service != null) service.RefreshIfStale();
    }

    public void ShowMessages()
    {
        if (!opened || showingMessages || messages == null || !messages.HasMessages) return;
        showingMessages = true;
        if (notesScroll != null) notesScroll.verticalNormalizedPosition = 1f;
        Render();
    }

    public void Retry()
    {
        if (showingMessages) { if (messages != null) messages.Refresh(); }
        else if (service != null) service.Retry();
    }

    public void OpenDownload()
    {
        if (!showingMessages && service != null && service.State == GameReleaseFetchState.Ready && GameReleasePolicy.HasDownload(service.Snapshot))
            Application.OpenURL(GameReleasePolicy.DownloadUrl);
    }

    private void Render()
    {
        bool hasPrivateMessages = opened && messages != null && messages.HasMessages;
        if (messagesTabButton != null) messagesTabButton.gameObject.SetActive(hasPrivateMessages);
        if (showingMessages && !hasPrivateMessages)
        {
            showingMessages = false;
            if (notesScroll != null) notesScroll.verticalNormalizedPosition = 1f;
        }
        GameReleaseSnapshot snapshot = service != null ? service.Snapshot : null;
        GameReleaseNotes notes = snapshot != null ? snapshot.Latest : null;
        Put(releaseTitleText, notes != null ? notes.Title : "What's new");
        string date = notes != null && DateTime.TryParseExact(notes.ReleaseDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out DateTime released) ? released.ToString("d MMM yyyy", CultureInfo.InvariantCulture) + "  |  v" + notes.Version : "";
        Put(releaseDateText, date);
        Put(notesText, notes != null ? notes.Notes : service != null && service.State == GameReleaseFetchState.Checking ?
            "Loading release notes..." : snapshot != null ? "No release notes have been published yet." : "Connect to read the latest release notes.");
        Put(installedVersionText, "Installed: v" + Application.version);
        Put(availableVersionText, snapshot != null && snapshot.Current != null ?
            (service != null && service.State == GameReleaseFetchState.Ready ? "Available: v" : "Last known: v") + snapshot.Current.Version : "Available: Unknown");
        string status = service != null ? service.StatusMessage : "Update information is unavailable.";
        if (service != null && service.State != GameReleaseFetchState.Ready && service.CacheFetchedAtUtc.HasValue)
            status += "\nLast checked: " + service.CacheFetchedAtUtc.Value.ToLocalTime().ToString("d MMM yyyy HH:mm", CultureInfo.InvariantCulture);
        Put(statusText, status);
        if (badge != null) badge.SetActive(service != null && service.HasVerifiedUpdate);
        if (retryButton != null) retryButton.interactable = service != null && !service.IsRequestInFlight;
        if (downloadButton != null) downloadButton.interactable = service != null && service.State == GameReleaseFetchState.Ready &&
            GameReleasePolicy.HasDownload(snapshot);
        if (releasesTabButton != null) releasesTabButton.interactable = showingMessages;
        if (messagesTabButton != null) messagesTabButton.interactable = !showingMessages;
        if (showingMessages)
        {
            Put(releaseTitleText, "Private Messages");
            Put(releaseDateText, "Only messages for your signed-in account appear here.");
            Put(notesText, opened && messages != null ? messages.DisplayText : "Sign in to read private messages.");
            Put(installedVersionText, ""); Put(availableVersionText, "");
            Put(statusText, "Private inbox · Refreshes while open. Deleted messages disappear on the next refresh.");
            if (retryButton != null) retryButton.interactable = messages != null;
            if (downloadButton != null) downloadButton.interactable = false;
        }
        GetComponent<MainMenuUpdatesLayout>()?.RefreshNotesLayout();
    }

    private static void Put(TMP_Text text, string content)
    {
        if (text == null) return;
        text.richText = false;
        text.text = content;
    }

    private static void Bind(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null) return;
        button.onClick.RemoveListener(action); button.onClick.AddListener(action);
    }
    private static void Unbind(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button != null) button.onClick.RemoveListener(action);
    }
}
