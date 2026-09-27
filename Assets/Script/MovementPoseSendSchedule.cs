using System;

/// <summary>
/// A phase-locked pose-send timer. Late frames skip missed slots instead of
/// moving every later send back by another full interval.
/// </summary>
public sealed class MovementPoseSendSchedule
{
    private double nextSendTime;
    private bool initialized;

    public void Reset()
    {
        nextSendTime = 0d;
        initialized = false;
    }

    public bool ShouldSend(double now, double sendsPerSecond)
    {
        double interval = 1d / Math.Max(1d, sendsPerSecond);
        if (!initialized)
        {
            nextSendTime = now;
            initialized = true;
        }

        // Unity frames and Fusion ticks rarely land on an exact decimal time.
        if (now + 0.0005d < nextSendTime) return false;

        double elapsedSlots = Math.Floor((now + 0.0005d - nextSendTime) / interval);
        nextSendTime += (Math.Max(0d, elapsedSlots) + 1d) * interval;
        return true;
    }

    public bool ShouldSend(double now, double sendsPerSecond, bool isForwardTick)
    {
        // A resimulated tick must not consume a real send slot. Fusion culls
        // RPCs invoked during resimulation, while this timer is local state.
        return isForwardTick && ShouldSend(now, sendsPerSecond);
    }
}
