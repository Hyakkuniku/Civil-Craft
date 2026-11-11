using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

internal static class CivilCraftAccountWebsite
{
    internal const string Origin = "https://civil-craft.vercel.app";
    internal const string VerificationRequired = "Verify your email before signing in. Check your inbox and spam folder.";
    internal const string VerificationUnavailable = "Could not check email verification. Please try signing in again.";
    internal const string RecoveryRequested = "If an account exists for that email, password recovery instructions have been sent.";

    [Serializable] private sealed class VerificationReply
    {
        public string playFabId;
        public bool verified;
        public string status;
    }

    [Serializable] private sealed class RecoveryRequest { public string email; }
    [Serializable] private sealed class PreferenceRequest { public bool emailUpdates; }
    [Serializable] private sealed class PreferenceReply { public bool emailUpdates; }
    [Serializable] private sealed class ResendRequest { public string identifier; public string sessionTicket; }

    [Serializable] internal sealed class ResendReply
    {
        public bool success;
        public bool alreadyVerified;
        public int cooldownSeconds;
    }

    internal static IEnumerator CheckEmail(string ticket, string playFabId, Action<CivilCraftAccountPolicy.Verification> complete)
    {
        if (string.IsNullOrWhiteSpace(ticket) || string.IsNullOrWhiteSpace(playFabId))
        {
            complete(CivilCraftAccountPolicy.Verification.Unavailable);
            yield break;
        }

        yield return Post("/api/player/auth/verify-session", "{}", ticket, (code, body) =>
        {
            VerificationReply reply = code == 200 ? Read<VerificationReply>(body) : null;
            complete(CivilCraftAccountPolicy.CheckVerification(code, playFabId, reply?.playFabId,
                reply?.verified == true, reply?.status));
        });
    }

    internal static IEnumerator SendRecovery(string email, Action<bool> complete) =>
        Post("/api/email/recovery", JsonUtility.ToJson(new RecoveryRequest { email = email }), null,
            (code, body) => complete(code == 200 && !string.IsNullOrEmpty(body)));

    internal static IEnumerator SaveEmailPreference(string ticket, bool emailUpdates, Action<bool> complete) =>
        Post("/api/player/email-preference", JsonUtility.ToJson(new PreferenceRequest { emailUpdates = emailUpdates }), ticket,
            (code, body) =>
            {
                PreferenceReply reply = code == 200 ? Read<PreferenceReply>(body) : null;
                complete(reply != null && reply.emailUpdates == emailUpdates);
            });

    internal static IEnumerator RequestVerification(string ticket, string identifier, Action<long, ResendReply> complete) =>
        Post("/api/player/auth/resend-verification",
            JsonUtility.ToJson(new ResendRequest { identifier = identifier, sessionTicket = ticket }), ticket,
            (code, body) => complete(code, code == 200 ? Read<ResendReply>(body) : null));

    private static IEnumerator Post(string path, string body, string ticket, Action<long, string> complete)
    {
        using (var request = new UnityWebRequest(Origin + path, UnityWebRequest.kHttpVerbPOST))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            if (!string.IsNullOrEmpty(ticket)) request.SetRequestHeader("Authorization", "Bearer " + ticket);
            request.timeout = 25;
            request.redirectLimit = 0;
            yield return request.SendWebRequest();
            complete(request.responseCode, request.result == UnityWebRequest.Result.Success
                ? request.downloadHandler.text : null);
        }
    }

    private static T Read<T>(string body) where T : class
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try { return JsonUtility.FromJson<T>(body); }
        catch (ArgumentException) { return null; }
    }
}
