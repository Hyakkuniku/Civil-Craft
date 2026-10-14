using System;
using System.Collections;
using System.Text;
using PlayFab;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>Private, memory-only inbox. Never reuses the public release cache.</summary>
public sealed class PlayerMessagesService : MonoBehaviour
{
    [Serializable] public sealed class Message { public string id, title, body, createdAt; }
    [Serializable] public sealed class Inbox { public Message[] messages; }
    public event Action Changed;
    public string DisplayText { get; private set; } = "";
    public bool HasMessages { get; private set; }
    private string account, ticket;
    private UnityWebRequest active;
    private int generation;
    private bool visible;
    private float nextRefresh;

    private static string CurrentTicket() => PlayerPrefs.GetInt("LoginChoice", 0) == 2 && PlayFabClientAPI.IsClientLoggedIn()
        ? PlayFabSettings.staticPlayer.ClientSessionTicket : null;
    public void SetVisible(bool value)
    {
        visible = value;
        Clear();
        if (visible) Refresh();
    }
    private void Clear()
    {
        generation++;
        if (active != null) { active.Abort(); active.Dispose(); }
        active = null;
        StopAllCoroutines();
        account = null; ticket = null; DisplayText = ""; HasMessages = false;
        Changed?.Invoke();
    }
    private void OnDisable() { visible = false; Clear(); }
    private void OnApplicationFocus(bool focused)
    {
        if (!focused) Clear();
        else if (visible) Refresh();
    }
    private void Update()
    {
        if (!visible) return;
        if (ticket != CurrentTicket() || account != PlayFabSettings.staticPlayer.PlayFabId)
        {
            Clear(); Refresh();
        }
        else if (active == null && Time.unscaledTime >= nextRefresh) Refresh();
    }
    public void Refresh()
    {
        if (!visible || active != null) return;
        ticket = CurrentTicket();
        account = PlayFabSettings.staticPlayer.PlayFabId;
        nextRefresh = Time.unscaledTime + 30f;
        if (string.IsNullOrEmpty(ticket) || string.IsNullOrEmpty(account))
        {
            DisplayText = "Sign in to read private messages."; Changed?.Invoke(); return;
        }
        DisplayText = "Checking private messages…"; Changed?.Invoke();
        StartCoroutine(Fetch(generation, account, ticket));
    }
    private IEnumerator Fetch(int expectedGeneration, string expectedAccount, string expectedTicket)
    {
        using (var request = new UnityWebRequest("https://civil-craft.vercel.app/api/player/messages", "GET"))
        {
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = 15; request.redirectLimit = 0;
            request.SetRequestHeader("Authorization", "Bearer " + expectedTicket);
            active = request;
            yield return request.SendWebRequest();
            if (expectedGeneration != generation || expectedTicket != CurrentTicket() || expectedAccount != PlayFabSettings.staticPlayer.PlayFabId) yield break;
            active = null;
            DisplayText = "Private messages unavailable. Connect and press Retry.";
            if (request.result == UnityWebRequest.Result.Success && request.responseCode == 200)
            {
                try
                {
                    string raw = request.downloadHandler.text;
                    if (raw == null || raw.Length > 262144) throw new FormatException();
                    var inbox = JsonUtility.FromJson<Inbox>(raw);
                    if (inbox == null || inbox.messages == null) throw new FormatException();
                    var text = new StringBuilder("MESSAGES FOR YOU\n");
                    foreach (var message in inbox.messages)
                    {
                        if (message == null || string.IsNullOrEmpty(message.title) || message.title.Length > 100 ||
                            message.body == null || message.body.Length > 2000) throw new FormatException();
                        text.Append("\n").Append(message.title).Append("\n").Append(message.body).Append("\n");
                    }
                    DisplayText = inbox.messages.Length == 0 ? "No private messages." : text.ToString();
                    HasMessages = inbox.messages.Length > 0;
                }
                catch { /* Do not display raw provider responses or stale inbox contents. */ }
            }
            nextRefresh = Time.unscaledTime + 30f;
            Changed?.Invoke();
        }
    }
}
