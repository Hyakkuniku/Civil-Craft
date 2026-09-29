using System;
using UnityEngine;

/// <summary>
/// A single PlayFab integer carries both measurements from the SAME bridge run.
/// Descending statistics then sort by the selected primary metric and its tie-breaker.
/// The server-side publisher must use this exact codec; the client only reads scores.
/// </summary>
public static class LeaderboardScoreCodec
{
    private const int MaxCost = 1000000;
    private const int MaxStressTenths = 1000;
    private const int EfficientBase = MaxStressTenths + 1;
    private const int StrongestBase = MaxCost + 1;

    public static string StatisticName(string contractId, bool strongest)
    {
        if (string.IsNullOrWhiteSpace(contractId)) return string.Empty;

        // Stable, short statistic names even when a contract asset is renamed.
        ulong hash = 14695981039346656037UL;
        string id = contractId.Trim();
        for (int i = 0; i < id.Length; i++)
        {
            hash ^= id[i];
            hash *= 1099511628211UL;
        }
        return (strongest ? "CC_S_" : "CC_E_") + hash.ToString("X16");
    }

    public static bool TryEncode(float cost, float peakStress, bool strongest, out int statistic)
    {
        statistic = 0;
        if (float.IsNaN(cost) || float.IsInfinity(cost) ||
            float.IsNaN(peakStress) || float.IsInfinity(peakStress) ||
            cost < 0f || cost > MaxCost || peakStress < 0f || peakStress > 100f)
            return false;

        int roundedCost = Mathf.RoundToInt(cost);
        int stressTenths = Mathf.RoundToInt(peakStress * 10f);
        long packed = strongest
            ? (long)stressTenths * StrongestBase + roundedCost
            : (long)roundedCost * EfficientBase + stressTenths;
        if (packed > int.MaxValue) return false;
        statistic = int.MaxValue - (int)packed;
        return true;
    }

    public static bool TryDecode(int statistic, bool strongest, out int cost, out float peakStress)
    {
        cost = 0;
        peakStress = 0f;
        long packed = (long)int.MaxValue - statistic;
        if (packed < 0) return false;

        int stressTenths;
        if (strongest)
        {
            stressTenths = (int)(packed / StrongestBase);
            cost = (int)(packed % StrongestBase);
        }
        else
        {
            cost = (int)(packed / EfficientBase);
            stressTenths = (int)(packed % EfficientBase);
        }

        if (cost > MaxCost || stressTenths > MaxStressTenths) return false;
        peakStress = stressTenths / 10f;
        return true;
    }
}
