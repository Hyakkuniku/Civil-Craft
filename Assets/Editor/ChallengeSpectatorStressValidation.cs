using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Transient mesh fixtures only; does not start a runner or simulate a bridge.</summary>
public static class ChallengeSpectatorStressValidation
{
    public static void Run(Transform fixture, StringBuilder report)
    {
        ValidateMotionScope(report);
        GameObject root = Child("Spectator Stress Validation", fixture);
        var createdMaterials = new List<BridgeMaterialSO>();
        Material appearance = null;
        ChallengeBridgeTestView view = null;
        try
        {
            Shader shader = Resources.Load<Shader>("Shaders/RopeSimulation");
            Check(shader != null, "The authored rope shader could not be loaded.");
            Color original = new Color(0.2f, 0.35f, 0.5f, 1f);
            Color ropeOriginal = new Color(0.72f, 0.43f, 0.25f, 1f);
            appearance = new Material(shader);
            appearance.SetColor("_BaseColor", original);
            GameObject segment = GameObject.CreatePrimitive(PrimitiveType.Cube);
            segment.transform.SetParent(root.transform, false);
            segment.GetComponent<Renderer>().sharedMaterial = appearance;
            var site = Child("Stress Fixture Site", root.transform).AddComponent<BuildLocation>();
            var creator = Child("Stress Fixture Creator", root.transform).AddComponent<BarCreator>();
            var settings = Child("Stress Fixture Settings", root.transform).AddComponent<BridgePhysicsManager>();
            settings.warningColor = new Color(1f, 0.7f, 0.1f, 1f);
            settings.criticalColor = new Color(1f, 0.1f, 0.05f, 1f);
            settings.brokenColor = new Color(0.15f, 0.05f, 0.02f, 1f);
            var source = Child("Stress Fixture Vehicle", root.transform).AddComponent<LiveLoadVehicle>();
            source.wheelObjects = Array.Empty<GameObject>();
            var descriptor = new ChallengeTestDescriptor
            {
                Bridge = new HostWorldBridgeSnapshot { Name = "Validation/Spectator Stress" },
                VehicleKey = ChallengeTestDescriptor.VehiclePath(source), Weight = 1f,
                VehiclePoses = new[] { HostWorldBridgePose.World(source.transform) }
            };
            var catalog = new Dictionary<string, BridgeMaterialSO>();
            for (int i = 0; i < 5; i++)
            {
                var material = ScriptableObject.CreateInstance<BridgeMaterialSO>();
                createdMaterials.Add(material);
                material.name = "Spectator Validation Material " + i;
                material.segmentPrefab = material.pierCapPrefab = segment;
                material.isRope = i < 2; material.isDualBeam = i < 2;
                material.isRoad = i == 3; material.isPier = i == 4;
                catalog.Add(material.Id, material);
                var member = new HostWorldBridgeBar
                {
                    MaterialId = material.Id,
                    Pose = new HostWorldBridgePose { Position = new Vector3(i * 4, 0, 0), Rotation = Quaternion.identity, Scale = Vector3.one }
                };
                member.Parts.Add(new HostWorldBridgePart
                {
                    Visible = true,
                    Pose = new HostWorldBridgePose { Rotation = Quaternion.identity, Scale = Vector3.one }
                });
                if (material.isPier) member.Parts.Add(new HostWorldBridgePart
                {
                    IsCap = true, Visible = true,
                    Pose = new HostWorldBridgePose { Position = Vector3.up, Rotation = Quaternion.identity, Scale = Vector3.one }
                });
                descriptor.Bridge.Bars.Add(member);
            }
            view = new ChallengeBridgeTestView(descriptor, site, creator, catalog, settings);
            Check(view.Root.GetComponentsInChildren<MonoBehaviour>(true).Length == 0 &&
                view.Root.GetComponentsInChildren<Rigidbody>(true).Length == 0 &&
                view.Root.GetComponentsInChildren<Collider>(true).Length == 0,
                "Stress presentation created local gameplay or physics components.");
            int sequence = 0;
            foreach (ushort encoded in new ushort[] { 0, 4096, 8192, 12288, 16383, 0x8000 })
            {
                var chunk = Chunk(descriptor, ++sequence, encoded);
                view.Receive(chunk); view.Render(0f);
                float stress = (encoded & 0x3fff) / 16383f;
                bool broken = (encoded & 0x8000) != 0;
                Color expectedRope = Expected(ropeOriginal, settings, stress, broken);
                var testedLines = view.Root.transform.GetChild(0).GetComponentsInChildren<LineRenderer>(true);
                var untouchedLines = view.Root.transform.GetChild(1).GetComponentsInChildren<LineRenderer>(true);
                Check(testedLines.Length == 2 && untouchedLines.Length == 2, "Dual rope strands were not built.");
                foreach (LineRenderer line in testedLines)
                {
                    Check(line.enabled, "Authoritative rope stress hid the visible strand.");
                    CheckColor(ReadColor(line, -1), expectedRope, "Rope stress or broken color did not follow its host sample.");
                    CheckColor(line.sharedMaterial.GetColor("_BaseColor"), ropeOriginal, "Stress mutated the shared rope material.");
                }
                foreach (LineRenderer line in untouchedLines)
                {
                    Check(line.sharedMaterial == testedLines[0].sharedMaterial, "The fixture no longer exercises shared rope materials.");
                    CheckColor(ReadColor(line, -1), ropeOriginal, "One loaded rope recolored an unloaded rope.");
                }
                for (int i = 2; i < 5; i++)
                    foreach (Renderer renderer in view.Root.transform.GetChild(i).GetComponentsInChildren<Renderer>(true))
                        CheckColor(ReadColor(renderer, 0), Expected(original, settings, stress, broken),
                            "Beam, road or pier stress changed from the authoritative blend.");
                CheckColor(appearance.GetColor("_BaseColor"), original, "Stress mutated an authored member material.");
            }
            // An old safe sample must not erase a newer failure, even when motion is reordered.
            view.Receive(Chunk(descriptor, 1, 0));
            foreach (LineRenderer line in view.Root.transform.GetChild(0).GetComponentsInChildren<LineRenderer>(true))
                CheckColor(ReadColor(line, -1), settings.brokenColor, "Late motion erased a rope's newer failure color.");
            report.AppendLine("PASS: Host stress samples color both dual-rope strands through safe, warning, critical and broken states; independent shared-material ropes remain independent. Beam, road, pier and cap colors retain the same blend. Reordered samples cannot erase failure colors, and the guest view contains no scripts, Rigidbody or colliders.");
        }
        finally
        {
            view?.Dispose();
            Object.DestroyImmediate(root);
            foreach (var material in createdMaterials) Object.DestroyImmediate(material);
            if (appearance != null) Object.DestroyImmediate(appearance);
        }
    }

    private static void ValidateMotionScope(StringBuilder report)
    {
        MethodInfo method = typeof(FusionChallengeTestSync).GetMethod("CanApplyTestMotion", BindingFlags.Static | BindingFlags.NonPublic);
        Check(method != null, "The spectator motion scope guard is missing.");
        Func<MultiplayerChallengeState, int, int, int, int, bool> accepts = (state, revision, index, preparedRevision, preparedIndex) =>
            (bool)method.Invoke(null, new object[] { state, revision, index, preparedRevision, preparedIndex });
        var matching = new MultiplayerChallengeState { Phase = MultiplayerChallengePhase.Testing, Revision = 14, TestIndex = 1 };
        Check(accepts(matching, 14, 1, 14, 1), "Matching live test motion was rejected.");
        foreach (var phase in new[] { MultiplayerChallengePhase.Building, MultiplayerChallengePhase.PreparingTest, MultiplayerChallengePhase.TestResults })
        {
            var state = matching; state.Phase = phase;
            Check(!accepts(state, 14, 1, 14, 1), "Motion applied outside the live test: " + phase);
        }
        var nextTest = matching; nextTest.TestIndex = 2;
        Check(!accepts(nextTest, 14, 1, 14, 1) && accepts(nextTest, 14, 2, 14, 2), "Prior-test motion leaked into the next engineer's test.");
        var nextRevision = matching; nextRevision.Revision = 15;
        Check(!accepts(nextRevision, 14, 1, 14, 1) && !accepts(matching, 14, 1, 15, 1) &&
            !accepts(matching, 14, 1, 14, 2) && !accepts(matching, 14, 0, 14, 0), "Stale or unprepared test motion was accepted.");
        report.AppendLine("PASS: Spectator motion applies only during Testing to the matching challenge revision, test index and prepared view; introduction, results and stale-test packets are rejected.");
    }

    private static ChallengeTestMotionChunk Chunk(ChallengeTestDescriptor descriptor, int sequence, ushort stress)
    {
        int count = descriptor.Bridge.Bars.Count + 1;
        var chunk = new ChallengeTestMotionChunk
        {
            Sequence = sequence, Time = sequence, TotalTargets = count, TotalBars = count - 1,
            Positions = new Vector3[count], Rotations = new Quaternion[count], Stress = new ushort[count],
            RopeEnds = new Vector3[count], RopeLengths = new float[count]
        };
        for (int i = 0; i < count; i++)
        {
            chunk.Rotations[i] = Quaternion.identity;
            chunk.Positions[i] = i < count - 1 ? descriptor.Bridge.Bars[i].Pose.Position : descriptor.VehiclePoses[0].Position;
            chunk.Stress[i] = i == 1 || i == count - 1 ? (ushort)0 : stress;
            if (i < 2)
            {
                chunk.Stress[i] |= 0x4000;
                chunk.RopeEnds[i] = chunk.Positions[i] + Vector3.right * 3;
                chunk.RopeLengths[i] = 3f;
            }
        }
        return chunk;
    }
    private static Color Expected(Color original, BridgePhysicsManager settings, float stress, bool broken)
    {
        if (broken) return settings.brokenColor;
        return stress < 0.5f ? Color.Lerp(original, settings.warningColor, stress * 2f)
            : Color.Lerp(settings.warningColor, settings.criticalColor, (stress - 0.5f) * 2f);
    }
    private static Color ReadColor(Renderer renderer, int slot)
    {
        var block = new MaterialPropertyBlock();
        if (slot < 0) renderer.GetPropertyBlock(block); else renderer.GetPropertyBlock(block, slot);
        return block.GetColor("_BaseColor");
    }
    private static void CheckColor(Color actual, Color expected, string message)
    {
        Check(Mathf.Abs(actual.r - expected.r) < 0.0001f && Mathf.Abs(actual.g - expected.g) < 0.0001f &&
            Mathf.Abs(actual.b - expected.b) < 0.0001f && Mathf.Abs(actual.a - expected.a) < 0.0001f,
            message + " Expected " + expected + ", got " + actual);
    }
    private static GameObject Child(string name, Transform parent)
    {
        var obj = new GameObject(name); obj.transform.SetParent(parent, false); return obj;
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
