using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class DailyRewardProgress
{
    // ISO UTC date; empty means this save has never claimed. Global across schedules.
    public string lastClaimUtcDate = "";
    public long claims;
}

[Serializable]
public class DailyRewardEntry
{
    public string title = "Builder's gift";
    public Sprite icon;
    [Min(0)] public int gold = 100;
    [Min(0)] public int exp;
    [Tooltip("Existing PlayerCosmetics hat ID. Granted without changing the equipped hat.")]
    public string cosmeticId;
    [Min(0), Tooltip("Gold granted instead when the cosmetic is already owned.")]
    public int ownedCosmeticGold = 100;
    public string Summary => GetSummary(false);
    public string GetSummary(bool cosmeticOwned)
    {
        bool hasCosmetic = !string.IsNullOrWhiteSpace(cosmeticId);
        long totalGold = (long)gold + (hasCosmetic && cosmeticOwned ? ownedCosmeticGold : 0);
        return totalGold + " Gold" + (exp > 0 ? " • " + exp + " EXP" : "") +
            (hasCosmetic ? (cosmeticOwned ? " • Owned item bonus included" : " • Cosmetic") : "");
    }
}

[CreateAssetMenu(menuName = "Civil Craft/Daily Reward Schedule")]
public class DailyRewardSchedule : ScriptableObject
{
    [Tooltip("Repeat after the final claim; otherwise the track ends permanently.")]
    public bool repeat = true;
    [Tooltip("False preserves progress after absences. True restarts at day one after a missed UTC date.")]
    public bool resetAfterMissedDay = true;
    [Tooltip("Optional existing persistent feature ID; empty makes rewards available immediately.")]
    public string requiredFeatureId = "";
    public string[] gameplayScenes = { "CanyonCrossing", "BHAN HOUSE" };
    public TMPro.TMP_FontAsset font;
    public List<DailyRewardEntry> rewards = new List<DailyRewardEntry>();

    public bool TryGetReward(DailyRewardProgress progress, DateTime utcNow, out int index, out string reason)
    {
        index = 0;
        reason = "";
        if (rewards == null || rewards.Count == 0) { reason = "Rewards are not configured."; return false; }
        long claims = Math.Max(0, progress == null ? 0 : progress.claims);
        if (!repeat && claims >= rewards.Count) { index = rewards.Count - 1; reason = "All gifts collected!"; return false; }
        if (progress != null && !string.IsNullOrEmpty(progress.lastClaimUtcDate))
        {
            if (!DateTime.TryParseExact(progress.lastClaimUtcDate, "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime last))
            { reason = "Saved reward date is invalid."; return false; }
            index = repeat ? (int)(claims % rewards.Count) : (int)Math.Min(claims, rewards.Count - 1);
            if (utcNow.Date <= last.Date) { reason = "Next gift at 00:00 UTC."; return false; }
            if (resetAfterMissedDay && (utcNow.Date - last.Date).TotalDays > 1) claims = 0;
        }
        index = repeat ? (int)(claims % rewards.Count) : (int)claims;
        DailyRewardEntry reward = rewards[index];
        if (reward == null || reward.gold < 0 || reward.exp < 0 || reward.ownedCosmeticGold < 0)
        { reason = "This reward needs configuration."; return false; }
        return true;
    }
}
