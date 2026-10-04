/// <summary>The movement loop that should be audible for the current player pose.</summary>
public enum PlayerFootstepGait
{
    Silent,
    Walk,
    Run
}

/// <summary>
/// Shared playback policy for local CharacterController movement and rendered
/// multiplayer proxies. Speed is physical planar travel, rather than input strength.
/// </summary>
public static class PlayerFootstepPlayback
{
    public static PlayerFootstepGait Resolve(float planarSpeed, bool sprinting,
        bool grounded, bool allowed, float minimumSpeed = 0.15f)
    {
        if (!allowed || !grounded || float.IsNaN(planarSpeed) ||
            float.IsInfinity(planarSpeed) || planarSpeed <= 0f ||
            float.IsNaN(minimumSpeed) || float.IsInfinity(minimumSpeed) ||
            planarSpeed < minimumSpeed)
            return PlayerFootstepGait.Silent;

        return sprinting ? PlayerFootstepGait.Run : PlayerFootstepGait.Walk;
    }
}
