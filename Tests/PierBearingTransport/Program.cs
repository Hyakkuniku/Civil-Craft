using System.Text;
using UnityEngine;

int checks = 0;
void Check(bool condition, string reason) { checks++; if (!condition) throw new Exception(reason); }
void Reject(Action action, string reason)
{
    try { action(); }
    catch (InvalidDataException) { checks++; return; }
    catch (EndOfStreamException) { checks++; return; }
    throw new Exception(reason);
}
HostWorldBridgePose Pose() => new HostWorldBridgePose
{
    Position = new Vector3(13f, -4f, 20f), Rotation = new Quaternion(0f, 0f, 0f, 1f), Scale = new Vector3(1f, 1f, 1f)
};
HostWorldBridgeSnapshot Snapshot(Vector3[] vertices, bool isCap = true)
{
    var snapshot = new HostWorldBridgeSnapshot { Name = "Region/Crossing" };
    var bar = new HostWorldBridgeBar { MaterialId = "concrete-pier", Pose = Pose() };
    bar.Parts.Add(new HostWorldBridgePart { IsCap = isCap, Visible = true, Pose = Pose(), BearingVertices = vertices });
    snapshot.Bars.Add(bar);
    return snapshot;
}
Vector3[] Shape(int count) => Enumerable.Range(0, count).Select(i => new Vector3(i % 2, (i % 4) / 2, i / 4)).ToArray();
int BearingOffset(byte[] packet)
{
    using var reader = new BinaryReader(new MemoryStream(packet));
    reader.ReadInt32(); reader.ReadBytes(reader.ReadUInt16()); reader.ReadInt32(); reader.ReadBytes(reader.ReadUInt16());
    reader.BaseStream.Position += 40; reader.ReadInt32(); reader.ReadBoolean(); reader.ReadBoolean(); reader.BaseStream.Position += 40;
    return (int)reader.BaseStream.Position;
}
byte[] MutateInt(byte[] source, int offset, int value)
{
    byte[] copy = (byte[])source.Clone(); BitConverter.GetBytes(value).CopyTo(copy, offset); return copy;
}

foreach (int count in new[] { 0, 8, 16, 512 })
{
    Vector3[] shape = count == 0 ? null : Shape(count);
    byte[] packet = HostWorldBridgeSnapshotCodec.Encode(Snapshot(shape));
    var restored = HostWorldBridgeSnapshotCodec.Decode(packet);
    var part = restored.Bars.Single().Parts.Single();
    Check(restored.Name == "Region/Crossing" && restored.Bars[0].MaterialId == "concrete-pier", "Site/material identity survives transport.");
    Check(part.IsCap && part.Visible && part.Pose.Position.y == -4f, "Bearing visibility and pose survive transport.");
    Check((part.BearingVertices?.Length ?? 0) == count, "Only the transmitted bearing corner count is reconstructed.");
    if (count != 0)
        Check(part.BearingVertices.Zip(shape).All(pair => pair.First.x == pair.Second.x && pair.First.y == pair.Second.y && pair.First.z == pair.Second.z),
            "Every prism corner survives the binary round trip without a source mesh or physics state.");
    Check(HostWorldBridgeSnapshotCodec.SameBytes(packet, HostWorldBridgeSnapshotCodec.Encode(restored)), "An unchanged bearing produces stable synchronization bytes.");
}
foreach (int count in new[] { 1, 7, 9, 513, 520 })
    Reject(() => HostWorldBridgeSnapshotCodec.Encode(Snapshot(Shape(count))), "Invalid prism corner counts must not encode.");
Reject(() => HostWorldBridgeSnapshotCodec.Encode(Snapshot(Shape(8), false)), "A road segment must not carry bearing geometry.");
foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 10001f, -10001f })
    foreach (int axis in new[] { 0, 1, 2 })
    {
        var shape = Shape(8);
        shape[0] = axis == 0 ? new Vector3(invalid, 0, 0) : axis == 1 ? new Vector3(0, invalid, 0) : new Vector3(0, 0, invalid);
        Reject(() => HostWorldBridgeSnapshotCodec.Encode(Snapshot(shape)), "Nonfinite or unbounded bearing corners must not encode.");
    }
byte[] good = HostWorldBridgeSnapshotCodec.Encode(Snapshot(Shape(8)));
int bearingOffset = BearingOffset(good);
foreach (int invalidCount in new[] { -1, 1, 7, 9, 513, int.MaxValue })
    Reject(() => HostWorldBridgeSnapshotCodec.Decode(MutateInt(good, bearingOffset, invalidCount)), "Malformed bearing counts must reject before allocation.");
foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, 10001f })
    Reject(() => HostWorldBridgeSnapshotCodec.Decode(MutateInt(good, bearingOffset + 4, BitConverter.SingleToInt32Bits(invalid))),
        "Malformed remote corners must not reach mesh construction.");
Reject(() => HostWorldBridgeSnapshotCodec.Decode(good.AsSpan(0, bearingOffset + 10)), "Truncated corner payload must reject.");
Reject(() => HostWorldBridgeSnapshotCodec.Decode(MutateInt(good, 0, 3)), "Unknown future geometry versions must reject.");

// Recreate an authentic v1 record: the old layout has no bearing count after its pose.
byte[] old;
using (var stream = new MemoryStream())
{
    using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
    void Text(string value) { byte[] bytes = Encoding.UTF8.GetBytes(value); writer.Write((ushort)bytes.Length); writer.Write(bytes); }
    void OldPose() { foreach (float value in new[] { 13f, -4f, 20f, 0f, 0f, 0f, 1f, 1f, 1f, 1f }) writer.Write(value); }
    writer.Write(1); Text("Legacy/Crossing"); writer.Write(1); Text("wood-pier"); OldPose(); writer.Write(1);
    writer.Write(true); writer.Write(true); OldPose(); writer.Write(0); writer.Write(0);
    writer.Flush(); old = stream.ToArray();
}
var legacy = HostWorldBridgeSnapshotCodec.Decode(old);
Check(legacy.Name == "Legacy/Crossing" && legacy.Bars[0].Parts[0].BearingVertices == null, "Existing v1 prefab-only bridge records remain readable.");
Check(legacy.Bars[0].Parts[0].IsCap && legacy.Bars[0].Parts[0].Visible, "Legacy cap rendering flags remain unchanged.");
Console.WriteLine($"PASS: {checks} real codec round-trip, legacy compatibility and malformed bearing payload checks.");
