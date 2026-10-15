/// <summary>Throttle cosmetic availability polling only; never used to authorize interaction.</summary>
public sealed class CargoOutlineRefreshSchedule
{
    public const float Interval = .2f;
    private float next;
    private float last;
    private bool initialized;
    public void Reset()
    {
        initialized = false;
    }

    public bool ShouldRefresh(float now)
    {
        if (!initialized || now < last || now >= next)
        {
            initialized = true;
            last = now;
            next = now + Interval;
            return true;
        }
        last = now;
        return false;
    }
}
