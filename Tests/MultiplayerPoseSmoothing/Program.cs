using System;
using System.Collections.Generic;
using UnityEngine;

internal static class Program
{
    private readonly struct Packet
    {
        public readonly float Delivery;
        public readonly float Source;
        public readonly Vector3 Position;
        public Packet(float delivery, float source, Vector3 position)
        { Delivery = delivery; Source = source; Position = position; }
    }

    private static void Main()
    {
        TestSendSchedule();
        TestResimulationDoesNotConsumeSendSlots();
        TestHostGuestReplay();
        TestNetwork(30, 12, 0f);
        TestNetwork(40, 20, 0.01f);
        TestNetwork(80, 20, 0f);
        TestNetwork(150, 50, 0.02f);
        TestNetwork(250, 50, 0.05f);
        TestStopsAndReversals();
        TestTeleport();
        TestOutOfOrderAndExtrapolationLimit();
        Console.WriteLine("All remote pose smoothing checks passed.");
    }

    private static void TestSendSchedule()
    {
        (int oldCount, double oldGap) = MeasureSends(40, false);
        (int newCount, double newGap) = MeasureSends(40, true);
        (int atThirty, _) = MeasureSends(30, true);
        (int atSixty, _) = MeasureSends(60, true);
        if (oldCount != 160 || newCount < 235 || newCount > 245 ||
            atThirty < 235 || atSixty < 235)
            throw new Exception($"Unexpected 30 Hz scheduler counts: " +
                $"old40={oldCount}, new40={newCount}, new30={atThirty}, new60={atSixty}");
        Console.WriteLine($"30 Hz target, 40 FPS owner: old {oldCount / 8d:0.0}/s " +
            $"(max gap {oldGap * 1000d:0}ms), phase-locked {newCount / 8d:0.0}/s " +
            $"(max gap {newGap * 1000d:0}ms); 30/60 FPS owners " +
            $"{atThirty / 8d:0.0}/{atSixty / 8d:0.0}/s");
    }

    private static void TestResimulationDoesNotConsumeSendSlots()
    {
        MovementPoseSendSchedule schedule = new MovementPoseSendSchedule();
        int sends = 0;
        for (int frame = 0; frame < 480; frame++)
        {
            double now = frame / 60d;
            if (schedule.ShouldSend(now, 30d, false))
                throw new Exception("A re-simulation tick attempted a pose send.");
            if (schedule.ShouldSend(now, 30d, true)) sends++;
        }
        if (sends < 235 || sends > 245)
            throw new Exception($"Re-simulation consumed send slots: {sends} sends in 8s.");
        Console.WriteLine($"Resimulation before every forward tick: {sends / 8d:0.0} sends/s");
    }

    private static (int count, double maximumGap) MeasureSends(int framesPerSecond,
        bool phaseLocked)
    {
        MovementPoseSendSchedule schedule = new MovementPoseSendSchedule();
        double oldNextTime = 0d;
        double previousSend = -1d;
        double maximumGap = 0d;
        int count = 0;
        for (int frame = 0; frame < framesPerSecond * 8; frame++)
        {
            double now = (double)frame / framesPerSecond;
            bool send = phaseLocked ? schedule.ShouldSend(now, 30d) : now >= oldNextTime;
            if (!send) continue;
            if (!phaseLocked) oldNextTime = now + 1d / 30d;
            if (previousSend >= 0d)
                maximumGap = Math.Max(maximumGap, now - previousSend);
            previousSend = now;
            count++;
        }
        return (count, maximumGap);
    }

    private static void TestHostGuestReplay()
    {
        CompareHostGuestReplay(30, 20, 0.01);
        CompareHostGuestReplay(80, 50, 0.03);
    }

    private static void CompareHostGuestReplay(int rttMs, int jitterMs, double loss)
    {
        (float oldStep, int oldStalls, float oldSourceGap) =
            ReplayGuestOnHost(false, rttMs, jitterMs, loss);
        (float newStep, int newStalls, float newSourceGap) =
            ReplayGuestOnHost(true, rttMs, jitterMs, loss);
        if (newStep > oldStep + 0.001f || newStalls > oldStalls ||
            newSourceGap > oldSourceGap + 0.001f)
            throw new Exception("Host replay regressed with the new guest send path.");
        Console.WriteLine($"Host view, 40 FPS guest/60 FPS host, {rttMs}ms RTT + " +
            $"0-{jitterMs}ms one-way jitter, {loss:P0} loss: old/new max render step " +
            $"{oldStep:0.000}/{newStep:0.000}m, paused frames " +
            $"{oldStalls}/{newStalls}, max source gap " +
            $"{oldSourceGap * 1000f:0}/{newSourceGap * 1000f:0}ms");
    }

    private static (float maxStep, int stalledFrames, float maxSourceGap)
        ReplayGuestOnHost(bool newPath, int rttMs, int jitterMs, double loss)
    {
        Random random = new Random(9901);
        MovementPoseSendSchedule schedule = new MovementPoseSendSchedule();
        List<Packet> packets = new List<Packet>();
        double oldNext = 0d;
        const float speed = 5f;
        for (int frame = 0; frame < 40 * 8; frame++)
        {
            double lossDraw = random.NextDouble();
            double jitterDraw = random.NextDouble();
            // Model a busy guest device that occasionally misses three frames.
            if (frame > 0 && frame % 80 is >= 1 and <= 3) continue;
            double now = frame / 40d;
            bool send = newPath ? schedule.ShouldSend(now, 30d) : now >= oldNext;
            if (!send) continue;
            if (!newPath) oldNext = now + 1d / 30d;
            if (lossDraw < loss) continue;
            float source = (float)now;
            float delivery = source + rttMs / 2000f +
                (float)jitterDraw * jitterMs / 1000f;
            packets.Add(new Packet(delivery, source,
                new Vector3(source * speed, 0f, 0f)));
        }
        packets.Sort((a, b) => a.Delivery.CompareTo(b.Delivery));

        RemoteAvatarPoseBuffer buffer = new RemoteAvatarPoseBuffer();
        int next = 0;
        float previousRenderX = 0f;
        float previousSource = -1f;
        float maxStep = 0f;
        float maxSourceGap = 0f;
        int stalledFrames = 0;
        for (float now = 0f; now < 8f; now += 1f / 60f)
        {
            Packet? newestThisFrame = null;
            while (next < packets.Count && packets[next].Delivery <= now)
            {
                Packet packet = packets[next++];
                if (newPath)
                {
                    if (buffer.Add(packet.Source, packet.Position,
                            Quaternion.identity, Vector3.one, now, 8f))
                        MeasureSourceGap(packet.Source);
                }
                else newestThisFrame = packet;
            }
            if (newestThisFrame.HasValue)
            {
                Packet packet = newestThisFrame.Value;
                if (buffer.Add(packet.Source, packet.Position,
                        Quaternion.identity, Vector3.one, now, 8f))
                    MeasureSourceGap(packet.Source);
            }
            if (!buffer.TrySample(now, 0.08f, 0.025f,
                    newPath ? 1.5f : float.PositiveInfinity, out var pose)) continue;
            if (now > 1f && now < 7.5f)
            {
                float step = pose.Position.x - previousRenderX;
                maxStep = Math.Max(maxStep, Math.Abs(step));
                if (step < 0.001f) stalledFrames++;
            }
            previousRenderX = pose.Position.x;
        }
        return (maxStep, stalledFrames, maxSourceGap);

        void MeasureSourceGap(float source)
        {
            if (previousSource >= 0f)
                maxSourceGap = Math.Max(maxSourceGap, source - previousSource);
            previousSource = source;
        }
    }

    private static void TestStopsAndReversals()
    {
        Random random = new Random(7042);
        List<Packet> packets = new List<Packet>();
        for (float source = 0f; source < 6f; source += 1f / 30f)
        {
            if (random.NextDouble() < 0.05) continue;
            float x = source < 2f ? source * 8f :
                source < 3f ? 16f :
                source < 5f ? 16f - (source - 3f) * 8f : 0f;
            float arrival = source + 0.125f + (float)random.NextDouble() * 0.05f;
            packets.Add(new Packet(arrival, source, new Vector3(x, 0f, 0f)));
        }
        packets.Sort((a, b) => a.Delivery.CompareTo(b.Delivery));

        RemoteAvatarPoseBuffer buffer = new RemoteAvatarPoseBuffer();
        int next = 0;
        float largestStopOvershoot = 0f;
        float largestReverseUndershoot = 0f;
        for (float now = 0f; now < 6f; now += 1f / 60f)
        {
            while (next < packets.Count && packets[next].Delivery <= now)
            {
                Packet packet = packets[next++];
                buffer.Add(packet.Source, packet.Position, Quaternion.identity,
                    Vector3.one, now, 8f);
            }
            if (!buffer.TrySample(now, 0.08f, 0.025f, out var pose)) continue;
            if (now > 2.3f && now < 3.1f)
                largestStopOvershoot = Math.Max(largestStopOvershoot, pose.Position.x - 16f);
            if (now > 5.3f)
                largestReverseUndershoot = Math.Max(largestReverseUndershoot, -pose.Position.x);
        }
        if (largestStopOvershoot > 0.2f || largestReverseUndershoot > 0.2f)
            throw new Exception($"Extrapolation overshot a stop/reversal: " +
                $"{largestStopOvershoot:0.000}m / {largestReverseUndershoot:0.000}m");
        Console.WriteLine($"Stops/reversals at 250ms RTT, 50ms jitter, 5% loss: " +
            $"overshoot <= {Math.Max(largestStopOvershoot, largestReverseUndershoot):0.000}m");
    }

    private static void TestNetwork(int rttMs, int jitterMs, float loss)
    {
        const float speed = 5f;
        const float sendStep = 1f / 30f;
        const float renderStep = 1f / 60f;
        Random random = new Random(5103 + rttMs);
        List<Packet> packets = new List<Packet>();
        for (float source = 0f; source < 8f; source += sendStep)
        {
            if (random.NextDouble() < loss) continue;
            float delay = rttMs / 2000f + (float)random.NextDouble() * jitterMs / 1000f;
            packets.Add(new Packet(source + delay, source, new Vector3(source * speed, 0f, 0f)));
        }
        packets.Sort((a, b) => a.Delivery.CompareTo(b.Delivery));

        RemoteAvatarPoseBuffer buffer = new RemoteAvatarPoseBuffer();
        int nextPacket = 0;
        float lastX = 0f;
        float maxFrameMovement = 0f;
        int measuredFrames = 0;
        int extrapolatedFrames = 0;
        for (float now = 0f; now < 8f; now += renderStep)
        {
            while (nextPacket < packets.Count && packets[nextPacket].Delivery <= now)
            {
                Packet packet = packets[nextPacket++];
                buffer.Add(packet.Source, packet.Position, Quaternion.identity,
                    Vector3.one, now, 8f);
            }

            if (!buffer.TrySample(now, 0.08f, 0.025f, out var pose)) continue;
            if (now > 1f && now < 7.5f)
            {
                maxFrameMovement = Math.Max(maxFrameMovement, Math.Abs(pose.Position.x - lastX));
                measuredFrames++;
                if (buffer.IsExtrapolating) extrapolatedFrames++;
            }
            lastX = pose.Position.x;
        }

        if (measuredFrames < 300 || maxFrameMovement > 0.25f)
            throw new Exception($"{rttMs}ms RTT: movement jump {maxFrameMovement:0.000}m");
        Console.WriteLine($"RTT {rttMs}ms, jitter 0-{jitterMs}ms, loss {loss:P0}: " +
            $"max 60fps step {maxFrameMovement:0.000}m; extrapolated {extrapolatedFrames}/{measuredFrames} frames");
    }

    private static void TestTeleport()
    {
        RemoteAvatarPoseBuffer buffer = new RemoteAvatarPoseBuffer();
        buffer.Add(1f, Vector3.zero, Quaternion.identity, Vector3.one, 1.1f, 8f);
        buffer.Add(1.04f, new Vector3(20f, 0f, 0f), Quaternion.identity, Vector3.one, 1.14f, 8f);
        buffer.TrySample(1.14f, 0.08f, 0.025f, out var pose);
        if (pose.Position.x != 20f || buffer.SampleCount != 1)
            throw new Exception("Teleport must discard old interpolation samples.");
    }

    private static void TestOutOfOrderAndExtrapolationLimit()
    {
        RemoteAvatarPoseBuffer buffer = new RemoteAvatarPoseBuffer();
        buffer.Add(1f, Vector3.zero, Quaternion.identity, Vector3.one, 1.1f, 8f);
        buffer.Add(1.033f, new Vector3(0.165f, 0f, 0f), Quaternion.identity,
            Vector3.one, 1.133f, 8f);
        if (buffer.Add(1.02f, new Vector3(100f, 0f, 0f), Quaternion.identity,
                Vector3.one, 1.14f, 8f) || buffer.SampleCount != 2)
            throw new Exception("Out-of-order poses must not rewind or teleport the proxy.");
        buffer.TrySample(2f, 0.08f, 0.025f, out var pose);
        if (pose.Position.x > 0.3f)
            throw new Exception("A stalled stream must stop after bounded extrapolation.");
    }
}
