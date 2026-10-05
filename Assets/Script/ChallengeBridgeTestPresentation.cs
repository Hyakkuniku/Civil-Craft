using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

public sealed class ChallengeTestDescriptor
{
    public HostWorldBridgeSnapshot Bridge;
    public string VehicleKey;
    public float Weight;
    public HostWorldBridgePose[] VehiclePoses; // Body followed by the authored wheels.
    public const int MaxBoats = 8;
    public const int MaxPacketBytes = HostWorldBridgeSnapshotCodec.MaxPacketBytes + 32768;
    public string[] BoatKeys = Array.Empty<string>();
    public HostWorldBridgePose[] BoatPoses = Array.Empty<HostWorldBridgePose>();
    public static string VehiclePath(LiveLoadVehicle vehicle)
        => ObjectPath(vehicle.transform);
    public static string ObjectPath(Transform transform)
    {
        var names = new List<string>();
        for (Transform node = transform; node != null; node = node.parent) names.Add(Uri.EscapeDataString(node.name));
        names.Reverse(); return string.Join("/", names);
    }
    public static List<BoatBridgeCrossing> SelectBoats(BuildLocation site, ContractSO definition)
    {
        var boats = new List<BoatBridgeCrossing>();
        foreach (var boat in Resources.FindObjectsOfTypeAll<BoatBridgeCrossing>())
            if (!boat.IsSessionChallengeTestBoat && boat.enabled && boat.gameObject.scene == site.gameObject.scene &&
                boat.assignedContract != null && boat.assignedContract.ContractID == definition.ContractID) boats.Add(boat);
        boats.Sort((a, b) => string.CompareOrdinal(ObjectPath(a.transform), ObjectPath(b.transform)));
        if (boats.Count > MaxBoats) throw new InvalidDataException("Too many authored challenge boats.");
        return boats;
    }
    public static BoatBridgeCrossing ResolveBoat(string key, UnityEngine.SceneManagement.Scene scene)
    {
        BoatBridgeCrossing match = null;
        foreach (var boat in Resources.FindObjectsOfTypeAll<BoatBridgeCrossing>())
        {
            if (boat.IsSessionChallengeTestBoat || boat.gameObject.scene != scene || ObjectPath(boat.transform) != key) continue;
            if (match != null) return null;
            match = boat;
        }
        return match;
    }
    public static LiveLoadVehicle ResolveVehicle(string key, UnityEngine.SceneManagement.Scene scene)
    {
        LiveLoadVehicle match = null;
        foreach (var vehicle in Resources.FindObjectsOfTypeAll<LiveLoadVehicle>())
        {
            if (vehicle.IsSessionChallengeTestVehicle || vehicle.gameObject.scene != scene || VehiclePath(vehicle) != key) continue;
            if (match != null) return null;
            match = vehicle;
        }
        return match;
    }
    // The preview and physics test must select the same authored truck. Prefer
    // the active contract truck, then a deterministic path for hidden saved trucks.
    public static LiveLoadVehicle SelectVehicle(BuildLocation site, ContractSO definition)
    {
        if (site == null || definition == null || definition.liveLoadMode != ContractSO.LiveLoadMode.Vehicle) return null;
        LiveLoadVehicle match = null;
        foreach (var candidate in Resources.FindObjectsOfTypeAll<LiveLoadVehicle>())
        {
            if (candidate.IsSessionChallengeTestVehicle || candidate.gameObject.scene != site.gameObject.scene ||
                candidate.assignedContract == null || candidate.assignedContract.ContractID != definition.ContractID) continue;
            if (match == null || candidate.gameObject.activeInHierarchy && !match.gameObject.activeInHierarchy ||
                candidate.gameObject.activeInHierarchy == match.gameObject.activeInHierarchy &&
                string.CompareOrdinal(VehiclePath(candidate), VehiclePath(match)) < 0) match = candidate;
        }
        return match;
    }
    public byte[] Encode()
    {
        byte[] bridge = HostWorldBridgeSnapshotCodec.Encode(Bridge);
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write(2); writer.Write(bridge.Length); writer.Write(bridge);
            writer.Write(VehicleKey); writer.Write(Weight); writer.Write(VehiclePoses.Length);
            foreach (var pose in VehiclePoses)
            { ChallengeTestMotionCodec.WriteVector(writer, pose.Position); ChallengeTestMotionCodec.WriteRotation(writer, pose.Rotation); ChallengeTestMotionCodec.WriteVector(writer, pose.Scale); }
            if (BoatKeys.Length != BoatPoses.Length || BoatKeys.Length > MaxBoats) throw new InvalidDataException("Invalid test boats.");
            writer.Write(BoatKeys.Length);
            for (int i = 0; i < BoatKeys.Length; i++)
            {
                writer.Write(BoatKeys[i]);
                var pose = BoatPoses[i];
                ChallengeTestMotionCodec.WriteVector(writer, pose.Position);
                ChallengeTestMotionCodec.WriteRotation(writer, pose.Rotation);
                ChallengeTestMotionCodec.WriteVector(writer, pose.Scale);
            }
            if (stream.Length > MaxPacketBytes) throw new InvalidDataException("Test presentation is too large.");
            return stream.ToArray();
        }
    }
    public static ChallengeTestDescriptor Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < 16 || data.Length > MaxPacketBytes) throw new InvalidDataException("Invalid test presentation size.");
        using (var stream = new MemoryStream(data.ToArray(), false))
        using (var reader = new BinaryReader(stream, new UTF8Encoding(false, true)))
        {
            if (reader.ReadInt32() != 2) throw new InvalidDataException("Invalid test presentation version.");
            int size = reader.ReadInt32();
            if (size < 0 || size > HostWorldBridgeSnapshotCodec.MaxPacketBytes || size > stream.Length - stream.Position)
                throw new InvalidDataException("Invalid test bridge size.");
            var descriptor = new ChallengeTestDescriptor { Bridge = HostWorldBridgeSnapshotCodec.Decode(reader.ReadBytes(size)), VehicleKey = reader.ReadString(), Weight = reader.ReadSingle() };
            if (descriptor.VehicleKey.Length == 0 || descriptor.VehicleKey.Length > 2048 ||
                !ChallengeTestMotionCodec.Finite(descriptor.Weight) || descriptor.Weight <= 0f) throw new InvalidDataException("Invalid test vehicle.");
            int count = reader.ReadInt32();
            if (count < 1 || count > 33 || count * 40L + 4 > stream.Length - stream.Position) throw new InvalidDataException("Invalid test wheel count.");
            descriptor.VehiclePoses = new HostWorldBridgePose[count];
            for (int i = 0; i < count; i++) descriptor.VehiclePoses[i] = new HostWorldBridgePose
            { Position = ChallengeTestMotionCodec.ReadVector(reader), Rotation = ChallengeTestMotionCodec.ReadRotation(reader), Scale = ChallengeTestMotionCodec.ReadVector(reader) };
            count = reader.ReadInt32();
            if (count < 0 || count > MaxBoats) throw new InvalidDataException("Invalid test boat count.");
            descriptor.BoatKeys = new string[count]; descriptor.BoatPoses = new HostWorldBridgePose[count];
            var keys = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < count; i++)
            {
                string key = reader.ReadString();
                if (key.Length == 0 || key.Length > 2048 || !keys.Add(key)) throw new InvalidDataException("Invalid test boat key.");
                descriptor.BoatKeys[i] = key;
                descriptor.BoatPoses[i] = new HostWorldBridgePose { Position = ChallengeTestMotionCodec.ReadVector(reader),
                    Rotation = ChallengeTestMotionCodec.ReadRotation(reader), Scale = ChallengeTestMotionCodec.ReadVector(reader) };
            }
            if (stream.Position != stream.Length) throw new InvalidDataException("Unexpected test presentation data.");
            return descriptor;
        }
    }
}

// Small, independently useful unreliable chunks. A lost chunk never blocks another
// member or the next frame; topology/results use separate reliable/state channels.
public sealed class ChallengeTestMotionChunk
{
    public int Sequence, Offset, TotalTargets, TotalBars;
    public float Time;
    public Vector3[] Positions;
    public Quaternion[] Rotations;
    public ushort[] Stress;
    public Vector3[] RopeEnds;
    public float[] RopeLengths;
}
public static class ChallengeTestMotionCodec
{
    public const int TargetsPerChunk = 8;
    public const int MaxTargets = ChallengeBridgeSubmissionCodec.MaxBars + 33 + ChallengeTestDescriptor.MaxBoats;
    public static byte[] Encode(int sequence, int offset, float time, IReadOnlyList<Transform> targets, IReadOnlyList<Bar> bars,
        int wheelCount = -1, IReadOnlyList<BoatBridgeCrossing> boats = null)
    {
        if (wheelCount < 0) wheelCount = targets.Count - bars.Count - 1;
        int boatStart = bars.Count + 1 + wheelCount;
        int count = Mathf.Min(TargetsPerChunk, targets.Count - offset);
        if (count <= 0 || targets.Count > MaxTargets || bars.Count > ChallengeBridgeSubmissionCodec.MaxBars)
            throw new InvalidDataException("Invalid test motion targets.");
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write((byte)3); writer.Write(sequence); writer.Write((ushort)offset); writer.Write((byte)count);
            writer.Write((ushort)targets.Count); writer.Write((ushort)bars.Count); writer.Write(time);
            for (int i = offset; i < offset + count; i++)
            {
                bool rope = i < bars.Count && bars[i].materialData.isRope;
                if (rope)
                {
                    // Reuse the pose's seven floats for both endpoints + rest length.
                    // Ropes deform between nodes; a rigid root pose cannot describe them.
                    WriteVector(writer, bars[i].StartPosition); WriteVector(writer, bars[i].EndPosition);
                    writer.Write(bars[i].SessionSpectatorRopeLength);
                }
                else if (i > bars.Count && i < boatStart)
                {
                    // Wheels travel with the chassis even if its packet arrives
                    // before theirs. Independent world-space interpolation made
                    // child wheels lag behind, wobble and detach from the truck.
                    Transform body = targets[bars.Count];
                    WriteVector(writer, body.InverseTransformPoint(targets[i].position));
                    WriteRotation(writer, Quaternion.Inverse(body.rotation) * targets[i].rotation);
                }
                else { WriteVector(writer, targets[i].position); WriteRotation(writer, targets[i].rotation); }
                BarStressHandler stress = i < bars.Count ? bars[i].GetComponent<BarStressHandler>() : null;
                ushort value = stress != null ? (ushort)Mathf.RoundToInt(Mathf.Clamp01(stress.VisualStressPercent) * 16383f) : (ushort)0;
                if (rope) value |= 0x4000;
                if (stress != null && stress.isBroken) value |= 0x8000;
                if (i >= boatStart && boats != null && !boats[i - boatStart].SessionTestVisible) value |= 0x8000;
                writer.Write(value);
            }
            return stream.ToArray();
        }
    }
    public static ChallengeTestMotionChunk Decode(byte[] bytes)
    {
        if (bytes == null || bytes.Length < 16 || bytes.Length > 16 + TargetsPerChunk * 30) throw new InvalidDataException("Invalid motion chunk size.");
        using (var stream = new MemoryStream(bytes, false))
        using (var reader = new BinaryReader(stream))
        {
            if (reader.ReadByte() != 3) throw new InvalidDataException("Invalid motion version.");
            var chunk = new ChallengeTestMotionChunk { Sequence = reader.ReadInt32(), Offset = reader.ReadUInt16() };
            int count = reader.ReadByte();
            chunk.TotalTargets = reader.ReadUInt16(); chunk.TotalBars = reader.ReadUInt16(); chunk.Time = reader.ReadSingle();
            if (count < 1 || count > TargetsPerChunk || chunk.Offset + count > chunk.TotalTargets || chunk.TotalTargets > MaxTargets ||
                chunk.TotalBars > ChallengeBridgeSubmissionCodec.MaxBars || chunk.TotalTargets < chunk.TotalBars + 1 ||
                !Finite(chunk.Time) || chunk.Time < 0f || count * 30 != stream.Length - stream.Position)
                throw new InvalidDataException("Invalid motion layout.");
            chunk.Positions = new Vector3[count]; chunk.Rotations = new Quaternion[count]; chunk.Stress = new ushort[count];
            chunk.RopeEnds = new Vector3[count]; chunk.RopeLengths = new float[count];
            for (int i = 0; i < count; i++)
            {
                chunk.Positions[i] = ReadVector(reader);
                Quaternion raw = new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                chunk.Stress[i] = reader.ReadUInt16();
                if ((chunk.Stress[i] & 0x4000) != 0)
                {
                    Vector3 end = new Vector3(raw.x, raw.y, raw.z);
                    if (chunk.Offset + i >= chunk.TotalBars || !Finite(end.x) || !Finite(end.y) || !Finite(end.z) ||
                        end.sqrMagnitude > 1e12f || !Finite(raw.w) || raw.w < 0f || raw.w > 1e6f)
                        throw new InvalidDataException("Invalid rope motion.");
                    chunk.RopeEnds[i] = end; chunk.RopeLengths[i] = raw.w;
                    chunk.Rotations[i] = Quaternion.identity;
                }
                else chunk.Rotations[i] = ValidateRotation(raw);
            }
            return chunk;
        }
    }
    internal static void WriteVector(BinaryWriter writer, Vector3 v) { writer.Write(v.x); writer.Write(v.y); writer.Write(v.z); }
    internal static void WriteRotation(BinaryWriter writer, Quaternion q) { writer.Write(q.x); writer.Write(q.y); writer.Write(q.z); writer.Write(q.w); }
    internal static Vector3 ReadVector(BinaryReader reader)
    {
        Vector3 v = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        if (!Finite(v.x) || !Finite(v.y) || !Finite(v.z) || v.sqrMagnitude > 1e12f) throw new InvalidDataException("Invalid motion position.");
        return v;
    }
    internal static Quaternion ReadRotation(BinaryReader reader)
    {
        Quaternion q = new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        return ValidateRotation(q);
    }
    private static Quaternion ValidateRotation(Quaternion q)
    {
        float length = q.x*q.x + q.y*q.y + q.z*q.z + q.w*q.w;
        if (!Finite(length) || length < 0.5f || length > 1.5f) throw new InvalidDataException("Invalid motion rotation.");
        return q.normalized;
    }
    internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}

/// <summary>Guest spectators render meshes only. No scripts, Rigidbody, colliders or local simulation.</summary>
public sealed class ChallengeBridgeTestView : IDisposable
{
    private HostWorldBridgeVisual geometry;
    public GameObject Root => geometry != null ? geometry.Root : null;
    public int TargetCount => targets.Count;
    private readonly List<Transform> targets = new List<Transform>();
    private readonly List<Renderer[]> memberRenderers = new List<Renderer[]>();
    private readonly Dictionary<Renderer, Color[]> originalColors = new Dictionary<Renderer, Color[]>();
    private readonly Dictionary<int, Renderer[]> boatRenderers = new Dictionary<int, Renderer[]>();
    private readonly Dictionary<Renderer, bool> boatRendererStates = new Dictionary<Renderer, bool>();
    private int vehicleIndex, boatStart;
    private readonly Dictionary<int, LineRenderer[]> ropes = new Dictionary<int, LineRenderer[]>();
    private static readonly Color RopeColor = new Color(0.72f, 0.43f, 0.25f, 1f);
    private Material ropeMaterial;
    private Sample[] samples;
    private const int HistoryCapacity = 12;
    private PoseFrame[] history;
    private float latestTime, latestArrival;
    private bool finalPose, hasTimeline;
    private Color warning, critical, broken;
    private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
    private struct Sample
    {
        public bool Received;
        public int Sequence;
        public float Time;
        public float RopeLength;
        public int NextHistory, HistoryCount;
    }
    private struct PoseFrame
    {
        public float Time;
        public Vector3 Position, RopeEnd;
        public Quaternion Rotation;
    }

    public ChallengeBridgeTestView(ChallengeTestDescriptor descriptor, BuildLocation site, BarCreator creator,
        IReadOnlyDictionary<string, BridgeMaterialSO> materials, BridgePhysicsManager settings)
    {
        try
        {
            LiveLoadVehicle source = ChallengeTestDescriptor.ResolveVehicle(descriptor.VehicleKey, site.gameObject.scene);
            if (source == null || source.wheelObjects.Length + 1 != descriptor.VehiclePoses.Length)
                throw new InvalidDataException("The guest's authored test vehicle or wheels do not match the host.");
            geometry = new HostWorldBridgeVisual(descriptor.Bridge, materials, creator.pointToInstantiate, null);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(Root, site.gameObject.scene);
            Root.hideFlags = HideFlags.DontSave;
            for (int i = 0; i < descriptor.Bridge.Bars.Count; i++)
            {
                Transform member = Root.transform.GetChild(i); targets.Add(member);
                Renderer[] renderers = member.GetComponentsInChildren<Renderer>(true); memberRenderers.Add(renderers);
                foreach (Renderer renderer in renderers)
                {
                    Material[] slots = renderer.sharedMaterials;
                    var colors = new Color[slots.Length];
                    for (int slot = 0; slot < slots.Length; slot++)
                        colors[slot] = slots[slot] != null && slots[slot].HasProperty("_BaseColor") ? slots[slot].GetColor("_BaseColor") :
                            slots[slot] != null && slots[slot].HasProperty("_Color") ? slots[slot].GetColor("_Color") : Color.white;
                    originalColors[renderer] = colors;
                }
                BridgeMaterialSO material = materials[descriptor.Bridge.Bars[i].MaterialId];
                if (material.isRope)
                {
                    Shader shader = Resources.Load<Shader>("Shaders/RopeSimulation");
                    if (shader == null) throw new InvalidDataException("The rope spectator shader is missing.");
                    if (ropeMaterial == null)
                    {
                        ropeMaterial = new Material(shader);
                        ropeMaterial.SetColor("_BaseColor", RopeColor);
                    }
                    var lines = new LineRenderer[material.isDualBeam ? 2 : 1];
                    float width = renderers.Length > 0 ? Mathf.Clamp(renderers[0].bounds.size.y * 1.65f, 0.16f, 0.3f) : 0.16f;
                    for (int strand = 0; strand < lines.Length; strand++)
                    {
                        var lineObject = new GameObject("Spectator Rope"); lineObject.transform.SetParent(member, false);
                        var line = lineObject.AddComponent<LineRenderer>(); lines[strand] = line;
                        line.useWorldSpace = true; line.positionCount = 17; line.widthMultiplier = width;
                        line.numCornerVertices = line.numCapVertices = 4; line.textureMode = LineTextureMode.Tile;
                        line.alignment = LineAlignment.View; line.sharedMaterial = ropeMaterial;
                        line.startColor = line.endColor = Color.white;
                        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; line.receiveShadows = false;
                        line.enabled = false;
                        lineObject.transform.localPosition = new Vector3(0, 0, material.isDualBeam ? (strand == 0 ? material.zOffset : -material.zOffset) : 0);
                    }
                    ropes.Add(i, lines);
                }
            }
            var map = new Dictionary<Transform, Transform>();
            GameObject vehicle = HostWorldBridgeVisual.CopyMeshes(source.gameObject, Root.transform, map);
            // The host's Awake can normalize an oversized FBX physics root.
            // An inactive guest source has not necessarily done so. Preserve
            // world-sized body meshes before assigning the host's root scale.
            Vector3 sourceScale = source.transform.lossyScale, bodyScale = descriptor.VehiclePoses[0].Scale;
            Vector3 normalization = new Vector3(ScaleRatio(sourceScale.x, bodyScale.x),
                ScaleRatio(sourceScale.y, bodyScale.y), ScaleRatio(sourceScale.z, bodyScale.z));
            foreach (Transform child in vehicle.transform)
            {
                child.localPosition = Vector3.Scale(child.localPosition, normalization);
                child.localScale = Vector3.Scale(child.localScale, normalization);
            }
            vehicle.SetActive(true); targets.Add(vehicle.transform);
            foreach (GameObject wheel in source.wheelObjects) targets.Add(map[wheel.transform]);
            vehicleIndex = descriptor.Bridge.Bars.Count;
            boatStart = vehicleIndex + descriptor.VehiclePoses.Length;
            for (int i = 0; i < descriptor.VehiclePoses.Length; i++)
            {
                Transform target = targets[descriptor.Bridge.Bars.Count + i]; var pose = descriptor.VehiclePoses[i];
                target.SetPositionAndRotation(pose.Position, pose.Rotation);
                Vector3 scale = target.parent.lossyScale;
                target.localScale = new Vector3(pose.Scale.x / scale.x, pose.Scale.y / scale.y, pose.Scale.z / scale.z);
            }
            // Flatten only the visual wheel parents. Physics axle objects are
            // absent on spectators and may differ between initialized editors.
            for (int i = vehicleIndex + 1; i < boatStart; i++) targets[i].SetParent(vehicle.transform, true);
            for (int i = 0; i < descriptor.BoatKeys.Length; i++)
            {
                BoatBridgeCrossing boatSource = ChallengeTestDescriptor.ResolveBoat(descriptor.BoatKeys[i], site.gameObject.scene);
                if (boatSource == null) throw new InvalidDataException("The guest's authored boat does not match the host.");
                GameObject boat = HostWorldBridgeVisual.CopyMeshes(boatSource.gameObject, Root.transform);
                var pose = descriptor.BoatPoses[i];
                boat.transform.SetPositionAndRotation(pose.Position, pose.Rotation); boat.transform.localScale = pose.Scale;
                boat.SetActive(true);
                Renderer[] meshes = boat.GetComponentsInChildren<Renderer>(true);
                boatRenderers.Add(targets.Count, meshes);
                foreach (Renderer renderer in meshes) boatRendererStates.Add(renderer, renderer.enabled);
                targets.Add(boat.transform);
            }
            warning = settings != null ? settings.warningColor : Color.yellow;
            critical = settings != null ? settings.criticalColor : Color.red;
            broken = settings != null ? settings.brokenColor : Color.black;
            samples = new Sample[targets.Count];
            history = new PoseFrame[targets.Count * HistoryCapacity];
            foreach (Collider collider in Root.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
            Root.SetActive(true);
        }
        catch { Dispose(); throw; }
    }

    public void Receive(ChallengeTestMotionChunk chunk)
    {
        if (chunk.TotalTargets != targets.Count || chunk.TotalBars != memberRenderers.Count) return;
        if (!hasTimeline || chunk.Time > latestTime)
        { latestTime = chunk.Time; latestArrival = Time.unscaledTime; hasTimeline = true; }
        for (int j = 0; j < chunk.Positions.Length; j++)
        {
            int index = chunk.Offset + j; Sample sample = samples[index];
            if (sample.Received && (unchecked(chunk.Sequence - sample.Sequence) <= 0 || chunk.Time < sample.Time)) continue;
            bool rope = (chunk.Stress[j] & 0x4000) != 0;
            if (index < memberRenderers.Count && ropes.ContainsKey(index) != rope) continue;
            history[index * HistoryCapacity + sample.NextHistory] = new PoseFrame { Time = chunk.Time,
                Position = chunk.Positions[j], Rotation = chunk.Rotations[j], RopeEnd = chunk.RopeEnds[j] };
            sample.NextHistory = (sample.NextHistory + 1) % HistoryCapacity;
            sample.HistoryCount = Mathf.Min(HistoryCapacity, sample.HistoryCount + 1);
            sample.Time = chunk.Time; sample.RopeLength = chunk.RopeLengths[j];
            sample.Sequence = chunk.Sequence; sample.Received = true; samples[index] = sample;
            if (boatRenderers.TryGetValue(index, out var boatMeshes))
                foreach (Renderer renderer in boatMeshes) renderer.enabled = boatRendererStates[renderer] && (chunk.Stress[j] & 0x8000) == 0;
            if (index >= memberRenderers.Count) continue;
            float stress = (chunk.Stress[j] & 0x3fff) / 16383f;
            bool isBroken = (chunk.Stress[j] & 0x8000) != 0;
            if (ropes.TryGetValue(index, out var lines))
            {
                foreach (Renderer renderer in memberRenderers[index]) renderer.enabled = false;
                // Each rope shares the spectator material; override its renderer
                // so one loaded/broken strand cannot recolor every other rope.
                Color color = StressColor(RopeColor, stress, isBroken);
                foreach (LineRenderer line in lines)
                {
                    block.Clear(); line.GetPropertyBlock(block);
                    block.SetColor("_BaseColor", color); line.SetPropertyBlock(block);
                }
                continue;
            }
            foreach (Renderer renderer in memberRenderers[index])
            {
                Color[] colors = originalColors[renderer];
                for (int slot = 0; slot < colors.Length; slot++)
                {
                    Color color = StressColor(colors[slot], stress, isBroken);
                    block.Clear(); renderer.GetPropertyBlock(block, slot);
                    block.SetColor("_BaseColor", color); block.SetColor("_Color", color); renderer.SetPropertyBlock(block, slot);
                }
            }
        }
    }
    private Color StressColor(Color original, float stress, bool isBroken)
    {
        Color color = isBroken ? broken : stress < 0.5f
            ? Color.Lerp(original, warning, stress * 2f) : Color.Lerp(warning, critical, (stress - 0.5f) * 2f);
        color.a = original.a;
        return color;
    }
    public void Render(float interpolationSeconds)
    {
        if (finalPose) interpolationSeconds = 0f;
        float renderTime = latestTime + Time.unscaledTime - latestArrival - interpolationSeconds;
        for (int i = 0; i < samples.Length; i++)
        {
            Sample sample = samples[i]; if (!sample.Received || targets[i] == null) continue;
            int oldest = (sample.NextHistory - sample.HistoryCount + HistoryCapacity) % HistoryCapacity;
            PoseFrame previous = history[i * HistoryCapacity + oldest], next = previous;
            for (int frame = 1; frame < sample.HistoryCount; frame++)
            {
                next = history[i * HistoryCapacity + (oldest + frame) % HistoryCapacity];
                if (next.Time >= renderTime) break;
                previous = next;
            }
            float blend = next.Time > previous.Time ? Mathf.Clamp01((renderTime - previous.Time) / (next.Time - previous.Time)) : 1f;
            Vector3 position = Vector3.Lerp(previous.Position, next.Position, blend);
            Quaternion rotation = Quaternion.Slerp(previous.Rotation, next.Rotation, blend);
            if (i > vehicleIndex && i < boatStart)
            { targets[i].localPosition = position; targets[i].localRotation = rotation; }
            else targets[i].SetPositionAndRotation(position, rotation);
            if (ropes.TryGetValue(i, out var lines))
            {
                Vector3 start = targets[i].position;
                Vector3 end = Vector3.Lerp(previous.RopeEnd, next.RopeEnd, blend);
                float length = Vector3.Distance(start, end);
                float slack = Mathf.Max(0f, sample.RopeLength - length - 0.03f);
                float sag = Mathf.Min(Mathf.Sqrt(0.375f * length * slack), sample.RopeLength * 0.12f);
                foreach (LineRenderer line in lines)
                {
                    line.enabled = true;
                    float offset = line.transform.localPosition.z;
                    for (int point = 0; point < 17; point++)
                    {
                        float t = point / 16f; Vector3 curvePosition = Vector3.Lerp(start, end, t);
                        curvePosition.y -= 4f * sag * t * (1f - t); curvePosition.z = start.z + offset;
                        line.SetPosition(point, curvePosition);
                    }
                }
            }
        }
    }
    public void CompleteFinalFrame() { finalPose = true; Render(0f); }
    private static float ScaleRatio(float value, float scale) => Mathf.Abs(scale) > 0.00001f ? value / scale : 1f;
    public void Dispose()
    {
        geometry?.Dispose(); geometry = null; targets.Clear(); memberRenderers.Clear(); originalColors.Clear(); ropes.Clear(); boatRenderers.Clear(); boatRendererStates.Clear();
        samples = null; history = null;
        if (ropeMaterial != null)
        { if (Application.isPlaying) UnityEngine.Object.Destroy(ropeMaterial); else UnityEngine.Object.DestroyImmediate(ropeMaterial); }
        ropeMaterial = null;
    }
}
