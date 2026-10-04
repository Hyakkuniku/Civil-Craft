using System;

internal static class Program
{
    private static int checks;

    private static void Main()
    {
        Expect("idle", PlayerFootstepGait.Silent, 0f);
        Expect("sprint input while blocked by a wall", PlayerFootstepGait.Silent,
            0f, sprinting: true);
        Expect("slow collision drift", PlayerFootstepGait.Silent, 0.05f);
        Expect("walking", PlayerFootstepGait.Walk, 5f);
        Expect("running", PlayerFootstepGait.Run, 12f, sprinting: true);
        Expect("carrying cargo forces walk", PlayerFootstepGait.Walk, 4f);
        Expect("fast movement still walks without sprint", PlayerFootstepGait.Walk, 12f);
        Expect("slow sprint still uses run state", PlayerFootstepGait.Run,
            0.5f, sprinting: true);
        Expect("walking in air", PlayerFootstepGait.Silent, 5f, grounded: false);
        Expect("running in air", PlayerFootstepGait.Silent, 12f,
            sprinting: true, grounded: false);
        Expect("build mode", PlayerFootstepGait.Silent, 5f, allowed: false);
        Expect("paused", PlayerFootstepGait.Silent, 12f,
            sprinting: true, allowed: false);
        Expect("stale motion sample", PlayerFootstepGait.Silent, 12f,
            sprinting: true, allowed: false);
        Expect("disabled player", PlayerFootstepGait.Silent, 5f, allowed: false);
        Expect("remote owner duplicate is suppressed", PlayerFootstepGait.Silent,
            12f, sprinting: true, allowed: false);
        Expect("no movement intent after remote stop", PlayerFootstepGait.Silent,
            1f, allowed: false);

        TestSpeedBoundaries();
        TestMotionSequence();

        Console.WriteLine($"All {checks} footstep playback checks passed.");
    }

    private static void TestSpeedBoundaries()
    {
        Expect("below default motion threshold", PlayerFootstepGait.Silent,
            MathF.BitDecrement(0.15f));
        Expect("at default motion threshold", PlayerFootstepGait.Walk, 0.15f);
        Expect("above default motion threshold", PlayerFootstepGait.Run,
            MathF.BitIncrement(0.15f), sprinting: true);
        Expect("below custom threshold", PlayerFootstepGait.Silent,
            0.25f, minimumSpeed: 0.5f);
        Expect("at custom threshold", PlayerFootstepGait.Walk,
            0.5f, minimumSpeed: 0.5f);
        Expect("zero threshold still suppresses stationary player",
            PlayerFootstepGait.Silent, 0f, minimumSpeed: 0f);
        Expect("negative speed", PlayerFootstepGait.Silent, -5f);
        Expect("negative threshold still suppresses stationary player",
            PlayerFootstepGait.Silent, 0f, minimumSpeed: -1f);
        Expect("NaN speed", PlayerFootstepGait.Silent, float.NaN);
        Expect("positive infinite speed", PlayerFootstepGait.Silent,
            float.PositiveInfinity, sprinting: true);
        Expect("negative infinite speed", PlayerFootstepGait.Silent,
            float.NegativeInfinity);
        Expect("NaN threshold", PlayerFootstepGait.Silent,
            5f, minimumSpeed: float.NaN);
        Expect("infinite threshold", PlayerFootstepGait.Silent,
            5f, minimumSpeed: float.PositiveInfinity);
    }

    private static void TestMotionSequence()
    {
        // Feed the same measured pose sequence as both a local player and a
        // remote proxy. Stopping or leaving the ground must silence either path.
        var sequence = new[]
        {
            (Speed: 0f, Sprint: false, Ground: true, Allowed: true, Gait: PlayerFootstepGait.Silent),
            (Speed: 4f, Sprint: false, Ground: true, Allowed: true, Gait: PlayerFootstepGait.Walk),
            (Speed: 12f, Sprint: true, Ground: true, Allowed: true, Gait: PlayerFootstepGait.Run),
            (Speed: 12f, Sprint: true, Ground: false, Allowed: true, Gait: PlayerFootstepGait.Silent),
            (Speed: 4f, Sprint: false, Ground: true, Allowed: true, Gait: PlayerFootstepGait.Walk),
            (Speed: 4f, Sprint: false, Ground: true, Allowed: false, Gait: PlayerFootstepGait.Silent),
            (Speed: 0f, Sprint: true, Ground: true, Allowed: true, Gait: PlayerFootstepGait.Silent)
        };

        foreach (string path in new[] { "local", "multiplayer proxy" })
            for (int index = 0; index < sequence.Length; index++)
            {
                var pose = sequence[index];
                Expect($"{path} movement sequence pose {index}", pose.Gait,
                    pose.Speed, pose.Sprint, pose.Ground, pose.Allowed);
            }
    }

    private static void Expect(string scenario, PlayerFootstepGait expected,
        float speed, bool sprinting = false, bool grounded = true,
        bool allowed = true, float minimumSpeed = 0.15f)
    {
        PlayerFootstepGait actual = PlayerFootstepPlayback.Resolve(speed,
            sprinting, grounded, allowed, minimumSpeed);
        if (actual != expected)
            throw new Exception($"{scenario}: expected {expected}, received {actual}.");
        checks++;
    }
}
