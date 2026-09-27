using UnityEngine;

/// <summary>
/// Render-only buffer for scene-player poses. It never moves a CharacterController
/// or changes the Fusion simulation state.
/// </summary>
public sealed class RemoteAvatarPoseBuffer
{
    public struct Pose
    {
        public float Time;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;
    }

    private const int Capacity = 32;
    private readonly Pose[] samples = new Pose[Capacity];
    private int start;
    private int count;
    private float localClockOffset;
    private float lastRenderTime;
    private float lastRenderLocalTime;
    private bool hasRendered;

    public int SampleCount => count;
    public bool IsExtrapolating { get; private set; }
    public float LastSampleGap { get; private set; }

    public void Clear()
    {
        start = 0;
        count = 0;
        localClockOffset = 0f;
        lastRenderTime = 0f;
        lastRenderLocalTime = 0f;
        hasRendered = false;
        IsExtrapolating = false;
        LastSampleGap = 0f;
    }

    public bool Add(float sourceTime, Vector3 position, Quaternion rotation, Vector3 scale,
        float localTime, float teleportDistance)
    {
        if (float.IsNaN(sourceTime) || float.IsInfinity(sourceTime)) return false;

        if (count > 0)
        {
            Pose latest = At(count - 1);
            if (sourceTime <= latest.Time) return false;
            LastSampleGap = sourceTime - latest.Time;
            if (LastSampleGap > 1f ||
                (position - latest.Position).sqrMagnitude > teleportDistance * teleportDistance)
                Clear();
        }

        if (count == 0)
        {
            localClockOffset = localTime - sourceTime;
            lastRenderTime = sourceTime;
        }
        else
        {
            // The smallest observed transit time is the best available clock
            // offset. Later packets with extra jitter must not delay all visuals.
            localClockOffset = Mathf.Min(localClockOffset, localTime - sourceTime);
        }

        int slot = (start + count) % Capacity;
        if (count == Capacity)
        {
            start = (start + 1) % Capacity;
            slot = (start + count - 1) % Capacity;
        }
        else count++;

        samples[slot] = new Pose
        {
            Time = sourceTime,
            Position = position,
            Rotation = rotation,
            Scale = scale
        };
        return true;
    }

    public bool TrySample(float localTime, float delay, float maxExtrapolation, out Pose result)
    {
        return TrySample(localTime, delay, maxExtrapolation,
            float.PositiveInfinity, out result);
    }

    public bool TrySample(float localTime, float delay, float maxExtrapolation,
        float maxPlaybackSpeed, out Pose result)
    {
        result = default;
        if (count == 0) return false;

        Pose latest = At(count - 1);
        float targetTime = localTime - localClockOffset - Mathf.Max(0f, delay);
        targetTime = Mathf.Max(lastRenderTime, targetTime);
        targetTime = Mathf.Min(targetTime, latest.Time + Mathf.Max(0f, maxExtrapolation));
        if (hasRendered && !float.IsInfinity(maxPlaybackSpeed))
        {
            // A late RPC or an improved clock-offset estimate must not turn
            // one render frame into a large catch-up jump on the host.
            float elapsed = Mathf.Max(0f, localTime - lastRenderLocalTime);
            targetTime = Mathf.Min(targetTime, lastRenderTime +
                elapsed * Mathf.Max(1f, maxPlaybackSpeed));
        }
        lastRenderTime = targetTime;
        lastRenderLocalTime = localTime;
        hasRendered = true;

        IsExtrapolating = targetTime > latest.Time;
        if (count == 1 || targetTime <= At(0).Time)
        {
            result = At(0);
            return true;
        }

        for (int index = 1; index < count; index++)
        {
            Pose to = At(index);
            if (targetTime > to.Time) continue;
            Pose from = At(index - 1);
            float alpha = Mathf.Clamp01((targetTime - from.Time) /
                Mathf.Max(0.0001f, to.Time - from.Time));
            result = new Pose
            {
                Time = targetTime,
                Position = Vector3.LerpUnclamped(from.Position, to.Position, alpha),
                Rotation = Quaternion.SlerpUnclamped(from.Rotation, to.Rotation, alpha),
                Scale = Vector3.LerpUnclamped(from.Scale, to.Scale, alpha)
            };
            return true;
        }

        result = latest;
        if (!IsExtrapolating) return true;

        Pose previous = At(count - 2);
        float interval = Mathf.Max(0.0001f, latest.Time - previous.Time);
        float extraTime = targetTime - latest.Time;
        // Decay the short extrapolation to avoid a large backwards correction
        // when the owner stops during a lost packet.
        float decay = 1f - 0.5f * extraTime /
            Mathf.Max(0.0001f, maxExtrapolation);
        result.Position += (latest.Position - previous.Position) *
            (extraTime / interval) * Mathf.Clamp01(decay);
        return true;
    }

    private Pose At(int index) => samples[(start + index) % Capacity];
}
