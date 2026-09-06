#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>Runs without touching the player's save. Also callable with -executeMethod DailyRewardsValidation.Run.</summary>
public static class DailyRewardsValidation
{
    [MenuItem("Tools/Civil Craft/Validate Daily Rewards")]
    public static void Run()
    {
        var schedule = ScriptableObject.CreateInstance<DailyRewardSchedule>();
        var root = new GameObject("Daily reward validation (temporary)");
        root.SetActive(false); // Keep PlayerDataManager.Awake from loading the real save.
        string directory = Path.Combine(Path.GetTempPath(), "CivilCraft-DailyRewards-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            for (int i = 0; i < 7; i++) schedule.rewards.Add(new DailyRewardEntry { gold = 100, exp = 10 });
            DateTime today = new DateTime(2026, 9, 6, 0, 0, 0, DateTimeKind.Utc);
            Check(schedule.TryGetReward(null, today, out int index, out _) && index == 0, "Fresh save starts at day 1");
            var progress = new DailyRewardProgress { lastClaimUtcDate = "2026-09-05", claims = 3 };
            Check(schedule.TryGetReward(progress, today, out index, out _) && index == 3, "UTC midnight advances");
            Check(!schedule.TryGetReward(progress, today.AddSeconds(-1), out _, out _), "Same date blocked");
            Check(!schedule.TryGetReward(progress, today.AddDays(-2), out _, out _), "Clock rollback blocked");
            Check(schedule.TryGetReward(progress, today.AddDays(1), out index, out _) && index == 0, "Missed date resets");
            schedule.resetAfterMissedDay = false;
            Check(schedule.TryGetReward(progress, today.AddDays(30), out index, out _) && index == 3, "Optional keep-progress policy");
            schedule.resetAfterMissedDay = true;
            progress.claims = 7;
            Check(schedule.TryGetReward(progress, today, out index, out _) && index == 0, "Seven-day track repeats");
            schedule.repeat = false;
            Check(!schedule.TryGetReward(progress, today.AddDays(10), out _, out _), "Finite track stays completed after absence");
            schedule.repeat = true;
            progress.lastClaimUtcDate = "bad-date";
            Check(!schedule.TryGetReward(progress, today, out _, out _), "Invalid date fails closed");
            schedule.rewards[0].gold = -1;
            Check(!schedule.TryGetReward(null, today, out _, out _), "Negative rewards rejected");
            schedule.rewards[0].gold = 100;

            var manager = root.AddComponent<PlayerDataManager>();
            manager.allGameAchievements.Clear();
            var data = new PlayerData();
            typeof(PlayerDataManager).GetProperty("CurrentData").SetValue(manager, data);
            var pathField = typeof(PlayerDataManager).GetField("saveFilePath", BindingFlags.NonPublic | BindingFlags.Instance);
            string savePath = Path.Combine(directory, "test.json");
            pathField.SetValue(manager, savePath);
            schedule.rewards[0].cosmeticId = "test-hat";
            Check(manager.TryClaimDailyReward(schedule, out _, out _), "Claim succeeds");
            Check(data.gold == 100 && data.exp == 10 && data.lifetimeGoldEarned == 100 && data.unlockedCosmeticIDs.Contains("test-hat"), "Rewards and lifetime totals granted");
            Check(!manager.TryClaimDailyReward(schedule, out _, out _) && data.gold == 100, "Double click rejected");
            var loaded = JsonUtility.FromJson<PlayerData>(File.ReadAllText(savePath));
            Check(!schedule.TryGetReward(loaded.dailyRewards, DateTime.UtcNow, out _, out _), "Reload preserves claim lock");
            Check(loaded.unlockedCosmeticIDs.Contains("test-hat") && loaded.gold == 100, "Ownership and marker saved together");
            data.dailyRewards = new DailyRewardProgress();
            Check(manager.TryClaimDailyReward(schedule, out _, out _) && data.gold == 300, "Owned cosmetic converts to gold");
            data.dailyRewards = new DailyRewardProgress();
            int oldGold = data.gold;
            // Empty path exercises transaction rollback without generating an expected Unity error log.
            pathField.SetValue(manager, "");
            Check(!manager.TryClaimDailyReward(schedule, out _, out _) && data.gold == oldGold && data.dailyRewards.claims == 0, "Save failure rolls back claim and currency");
            pathField.SetValue(manager, savePath);
            data.gold = int.MaxValue;
            Check(!manager.TryClaimDailyReward(schedule, out _, out _), "Overflow rejected");
            Debug.Log("[DailyRewardsValidation] PASS: 18 date, reward, persistence and rollback checks.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(schedule);
            Directory.Delete(directory, true);
        }
    }
    private static void Check(bool result, string message)
    {
        if (!result) throw new InvalidOperationException("Daily rewards validation failed: " + message);
    }
}
#endif
