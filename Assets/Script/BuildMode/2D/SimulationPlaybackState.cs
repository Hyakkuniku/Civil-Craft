using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

/// <summary>
/// Per-attempt solo playback settings. This class never changes Unity's clock;
/// its owner composes this value with pause and restores the clock on teardown.
/// </summary>
public sealed class SimulationPlaybackState
{
    public const int MinimumStep = -2;
    public static int MaximumStep => PlaybackFactors.Length + MinimumStep - 1;
    private static readonly float[] PlaybackFactors = { 0.25f, 0.5f, 1f, 1.5f, 2f, 3f };
    private readonly Dictionary<object, float> lessonMultipliers =
        new Dictionary<object, float>(OwnerIdentityComparer.Instance);

    public bool Active { get; private set; }
    public int Step { get; private set; }
    public float BaselineScale { get; private set; } = 1f;
    public float PlaybackMultiplier => PlaybackFactors[Step - MinimumStep];

    /// <summary>Authored lesson effects, separately owned and never compounded by repeated updates.</summary>
    public float LessonMultiplier
    {
        get
        {
            double product = 1d;
            foreach (float multiplier in lessonMultipliers.Values) product *= multiplier;
            return (float)Math.Max(0.1d, Math.Min(1d, product));
        }
    }

    /// <summary>The running clock's requested scale; pause is deliberately external.</summary>
    public float Scale
    {
        get
        {
            if (!Active) return 1f;
            double composed = (double)BaselineScale * LessonMultiplier * PlaybackMultiplier;
            return (float)Math.Max(float.Epsilon, Math.Min(float.MaxValue, composed));
        }
    }

    public void Begin(float baselineScale)
    {
        lessonMultipliers.Clear();
        BaselineScale = IsPositiveFinite(baselineScale) ? baselineScale : 1f;
        Step = 0;
        Active = true;
    }

    /// <summary>One click moves one notch; repeated clicks stop at either endpoint.</summary>
    public bool TryStep(int direction)
    {
        if (!Active || direction == 0) return false;
        int next = Math.Max(MinimumStep, Math.Min(MaximumStep, Step + (direction < 0 ? -1 : 1)));
        if (next == Step) return false;
        Step = next;
        return true;
    }

    public void SetLessonMultiplier(object owner, float multiplier)
    {
        if (!Active || owner == null) return;
        lessonMultipliers[owner] = IsPositiveFinite(multiplier)
            ? Math.Max(0.1f, Math.Min(1f, multiplier)) : 1f;
    }

    public bool ClearLessonMultiplier(object owner)
    {
        return owner != null && lessonMultipliers.Remove(owner);
    }

    public void End()
    {
        lessonMultipliers.Clear();
        Step = 0;
        BaselineScale = 1f;
        Active = false;
    }

    private static bool IsPositiveFinite(float value)
    {
        return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private sealed class OwnerIdentityComparer : IEqualityComparer<object>
    {
        public static readonly OwnerIdentityComparer Instance = new OwnerIdentityComparer();
        public new bool Equals(object left, object right) => ReferenceEquals(left, right);
        public int GetHashCode(object value) => RuntimeHelpers.GetHashCode(value);
    }
}
