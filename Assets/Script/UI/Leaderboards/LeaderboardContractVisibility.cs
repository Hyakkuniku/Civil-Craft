using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.IO;
using PlayFab.Json;
using UnityEngine.Networking;

/// <summary>Validated public display policy; never changes progression or score submission.</summary>
public static class LeaderboardContractVisibility
{
    public const string FeedUrl = "https://civil-craft.vercel.app/api/leaderboard/contracts";
    public const int TimeoutSeconds = 10;
    public const int MaxResponseBytes = 4096;
    private static readonly HashSet<string> Supported = new HashSet<string>(StringComparer.Ordinal) {
        "ShopKeeper", "TUT_CONTRACT1", "ReedsContract", "VancesContract", "TUT_CONTRACT2",
        "ReedSideInspection", "VanceSideRealignment", "SilasMainContract", "MainContractSilas"
    };

    public static UnityWebRequest CreateRequest(out BoundedDownloadHandler handler)
    {
        handler = new BoundedDownloadHandler();
        var request = new UnityWebRequest(FeedUrl, UnityWebRequest.kHttpVerbGET);
        request.downloadHandler = handler;
        request.timeout = TimeoutSeconds;
        request.redirectLimit = 0;
        request.SetRequestHeader("Accept", "application/json");
        return request;
    }

    // Both the board and completion action use the same bounded public policy.
    public sealed class BoundedDownloadHandler : DownloadHandlerScript
    {
        private readonly MemoryStream received = new MemoryStream();
        public bool Overflowed { get; private set; }
        public string Body => Encoding.UTF8.GetString(received.ToArray());
        public BoundedDownloadHandler() : base(new byte[1024]) { }
        protected override bool ReceiveData(byte[] data, int length)
        {
            if (data == null || length < 0 || length > data.Length || received.Length + length > MaxResponseBytes)
            { Overflowed = true; return false; }
            if (length > 0) received.Write(data, 0, length);
            return true;
        }
    }

    public static bool TryParse(string body, out HashSet<string> contractIds)
    {
        contractIds = null;
        if (string.IsNullOrEmpty(body) || Encoding.UTF8.GetByteCount(body) > MaxResponseBytes ||
            !GameReleasePolicy.IsJsonObjectDocument(body)) return false;
        try
        {
            var root = PlayFabSimpleJson.DeserializeObject(body) as IDictionary<string, object>;
            if (root == null || !root.TryGetValue("schemaVersion", out object schema) ||
                !(schema is long signed && signed == 1L || schema is ulong unsigned && unsigned == 1UL) ||
                !root.TryGetValue("contractIds", out object raw) || !(raw is IList list) || list.Count > Supported.Count) return false;
            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (object value in list)
                if (!(value is string id) || !Supported.Contains(id) || !result.Add(id)) return false;
            contractIds = result;
            return true;
        }
        catch (Exception) { return false; }
    }
}
