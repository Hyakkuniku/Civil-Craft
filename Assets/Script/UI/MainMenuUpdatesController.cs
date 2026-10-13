using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>Scene-authored public notes UI. Does not require login or own any player state.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(GameReleaseService))]
public sealed class MainMenuUpdatesController : MonoBehaviour
{
    public Button updatesButton, closeButton, retryButton, downloadButton;
    public GameObject panel, badge;
    public TMP_Text releaseTitleText, releaseDateText, notesText, installedVersionText, availableVersionText, statusText;
    public ScrollRect notesScroll;
    private GameReleaseService service;
    private bool opened;

    private void OnEnable()
    {
        service = GetComponent<GameReleaseService>();
        if (service != null) { service.Changed -= Render; service.Changed += Render; }
        Bind(updatesButton, Open); Bind(closeButton, Close); Bind(retryButton, Retry); Bind(downloadButton, OpenDownload);
        Render();
    }

    private void Start() { if (service != null) service.BeginMenuSession(); }

    private void OnDisable()
    {
        if (service != null) service.Changed -= Render;
        Unbind(updatesButton, Open); Unbind(closeButton, Close); Unbind(retryButton, Retry); Unbind(downloadButton, OpenDownload);
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
        if (panel == null) return;
        if (UIPanelCoordinator.Instance != null) UIPanelCoordinator.Instance.ClosePanel(panel);
        else panel.SetActive(false);
    }

    public void Retry() { if (service != null) service.Retry(); }

    public void OpenDownload()
    {
        if (service != null && service.State == GameReleaseFetchState.Ready && GameReleasePolicy.HasDownload(service.Snapshot))
            Application.OpenURL(GameReleasePolicy.DownloadUrl);
    }

    private void Render()
    {
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
