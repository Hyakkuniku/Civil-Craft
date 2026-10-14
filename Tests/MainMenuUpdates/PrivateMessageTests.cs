using System.Collections;
using System.Reflection;
using PlayFab;
using UnityEngine;
using UnityEngine.Networking;

internal static partial class Program
{
    private static void TestPrivateMessages()
    {
        UnityWebRequest.Reset();
        PlayerPrefs.Choice = 2; PlayFabClientAPI.LoggedIn = true;
        PlayFabSettings.staticPlayer.PlayFabId = "AAA";
        PlayFabSettings.staticPlayer.ClientSessionTicket = "ticket-A";
        var service = new GameObject().AddComponent<PlayerMessagesService>();
        var update = typeof(PlayerMessagesService).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
        void DrainInbox()
        {
            foreach (IEnumerator routine in service.StartedCoroutines.ToArray()) Drain(routine);
            service.StartedCoroutines.Clear();
        }
        UnityWebRequest.Script.Enqueue(new(200, "{\"messages\":[{\"title\":\"Private A\",\"body\":\"Hello A\"}]}"));
        service.SetVisible(true); DrainInbox();
        Check(service.DisplayText.Contains("Hello A"), "Private inbox displays recipient message.");
        Check(UnityWebRequest.Requests.Last().Headers["Authorization"] == "Bearer ticket-A", "Private inbox authenticates in header.");
        Check(!UnityWebRequest.Requests.Last().Url.Contains("ticket"), "No credentials in inbox URL.");
        PlayFabSettings.staticPlayer.PlayFabId = "BBB"; PlayFabSettings.staticPlayer.ClientSessionTicket = "ticket-B";
        update.Invoke(service, null);
        Check(!service.DisplayText.Contains("Hello A"), "Account switch immediately clears former inbox.");
        UnityWebRequest.Script.Enqueue(new(200, "{\"messages\":[]}")); DrainInbox();
        Check(service.DisplayText == "No private messages.", "Other account has its own empty inbox.");
        UnityWebRequest.Script.Enqueue(new(200, "{\"messages\":[{\"title\":\"Old B\",\"body\":\"Stale\"}]}", UnityWebRequest.Result.Success,
            () => PlayFabSettings.staticPlayer.ClientSessionTicket = "new-session"));
        service.Refresh(); DrainInbox();
        Check(!service.DisplayText.Contains("Stale"), "Late response from old session is discarded.");
        update.Invoke(service, null);
        UnityWebRequest.Script.Enqueue(new(200, "{\"messages\":[]}")); DrainInbox();
        Check(service.DisplayText == "No private messages.", "Deleted messages disappear on refresh.");
        UnityWebRequest.Script.Enqueue(new(503, "private provider error", UnityWebRequest.Result.ProtocolError));
        service.Refresh(); DrainInbox();
        Check(service.DisplayText.Contains("unavailable") && !service.DisplayText.Contains("provider"), "Outages do not expose raw errors or stale private data.");
        service.SetVisible(false);
        Check(service.DisplayText == "", "Closing clears private inbox from memory.");
        PlayerPrefs.Choice = 1;
        int count = UnityWebRequest.Requests.Count;
        service.SetVisible(true); DrainInbox();
        Check(UnityWebRequest.Requests.Count == count && service.DisplayText.Contains("Sign in"), "Guest never requests a private inbox.");
        service.SetVisible(false); PlayFabClientAPI.LoggedIn = false;
    }
}
