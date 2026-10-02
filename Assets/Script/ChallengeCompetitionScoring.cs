using System;

public enum ChallengeWinner { Pending, Host, Guest, Draw, NoWinner }

/// <summary>Session-only competition grading. Never awards story stars or saves progress.</summary>
public static class ChallengeCompetitionScoring
{
    public const double CostWeight = 0.40;
    public const double StrengthWeight = 0.60;

    public readonly struct Score
    {
        public readonly bool Qualified;
        public readonly double CostPercent, StrengthPercent;
        // Comparing the published hundredths avoids invisible tie-breaks below UI precision.
        public readonly int Hundredths;
        public Score(bool qualified, double costPercent, double strengthPercent, int hundredths)
        { Qualified = qualified; CostPercent = costPercent; StrengthPercent = strengthPercent; Hundredths = hundredths; }
    }

    public static bool ValidMeasurements(float budget, float cost, float peakStress) =>
        Finite(budget) && budget > 0f && Finite(cost) && cost >= 0f && Finite(peakStress) && peakStress >= 0f;

    public static Score Grade(ChallengeTestOutcome outcome, float cost, float budget, float peakStress)
    {
        if (!ValidMeasurements(budget, cost, peakStress)) return default;
        double costPercent = Clamp01(1.0 - (double)cost / budget) * 100.0;
        double strengthPercent = Clamp01(1.0 - peakStress) * 100.0;
        // Match the existing host submission validator's cent-sized numerical tolerance.
        bool qualified = outcome == ChallengeTestOutcome.Crossed && (double)cost <= (double)budget + 0.01;
        int hundredths = qualified ? (int)Math.Round((costPercent * CostWeight + strengthPercent * StrengthWeight) * 100.0,
            MidpointRounding.AwayFromZero) : 0;
        return new Score(qualified, costPercent, strengthPercent, hundredths);
    }

    public static ChallengeWinner Compare(Score host, Score guest)
    {
        if (!host.Qualified && !guest.Qualified) return ChallengeWinner.NoWinner;
        if (!host.Qualified) return ChallengeWinner.Guest;
        if (!guest.Qualified) return ChallengeWinner.Host;
        return host.Hundredths == guest.Hundredths ? ChallengeWinner.Draw :
            host.Hundredths > guest.Hundredths ? ChallengeWinner.Host : ChallengeWinner.Guest;
    }

    private static double Clamp01(double value) => Math.Min(1.0, Math.Max(0.0, value));
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
