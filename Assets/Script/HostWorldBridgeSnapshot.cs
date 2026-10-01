using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

// Only geometry is transferred. These records never enter PlayerDataManager.
public sealed class HostWorldBridgeSnapshot
{
    public string Name = string.Empty;
    public readonly List<HostWorldBridgeBar> Bars = new List<HostWorldBridgeBar>();
    public readonly List<HostWorldBridgeNode> Nodes = new List<HostWorldBridgeNode>();
}

public struct HostWorldBridgePose
{
    public Vector3 Position;
    public Quaternion Rotation;
    public Vector3 Scale;

    public static HostWorldBridgePose World(Transform transform) => new HostWorldBridgePose
    {
        Position = transform.position, Rotation = transform.rotation, Scale = transform.lossyScale
    };

    public static HostWorldBridgePose Local(Transform transform) => new HostWorldBridgePose
    {
        Position = transform.localPosition, Rotation = transform.localRotation, Scale = transform.localScale
    };
}

public sealed class HostWorldBridgeBar
{
    public string MaterialId;
    public HostWorldBridgePose Pose;
    public readonly List<HostWorldBridgePart> Parts = new List<HostWorldBridgePart>();
    public readonly List<HostWorldBridgeCollider> Colliders = new List<HostWorldBridgeCollider>();
}

public struct HostWorldBridgePart
{
    public bool IsCap;
    public bool Visible;
    public HostWorldBridgePose Pose;
}

public sealed class HostWorldBridgeNode
{
    public HostWorldBridgePose Pose;
    public bool Visible;
    public bool IsAnchor;
    public readonly List<HostWorldBridgeCollider> Colliders = new List<HostWorldBridgeCollider>();
}

public struct HostWorldBridgeCollider
{
    public const byte Box = 0, Sphere = 1, Capsule = 2;
    public byte Kind;
    public HostWorldBridgePose Pose;
    public Vector3 Center;
    public Vector3 Size;
    public float Radius;
    public float Height;
    public int Direction;
    public int Layer;
    public bool HasMaterial;
    public float StaticFriction, DynamicFriction, Bounciness;
    public int FrictionCombine, BounceCombine;
}

/// <summary>Bounded binary geometry packets, independent of Fusion avatar array limits.</summary>
public static class HostWorldBridgeSnapshotCodec
{
    public const int Version = 1;
    public const int MaxPacketBytes = 2 * 1024 * 1024;
    private const int MaxMembers = 8192;

    public static byte[] Encode(HostWorldBridgeSnapshot snapshot)
    {
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write(Version);
            WriteString(writer, snapshot.Name);
            WriteCount(writer, snapshot.Bars.Count, MaxMembers);
            foreach (HostWorldBridgeBar bar in snapshot.Bars)
            {
                WriteString(writer, bar.MaterialId);
                WritePose(writer, bar.Pose);
                WriteCount(writer, bar.Parts.Count, 16);
                foreach (HostWorldBridgePart part in bar.Parts)
                {
                    writer.Write(part.IsCap);
                    writer.Write(part.Visible);
                    WritePose(writer, part.Pose);
                }
                WriteColliders(writer, bar.Colliders);
            }
            WriteCount(writer, snapshot.Nodes.Count, MaxMembers);
            foreach (HostWorldBridgeNode node in snapshot.Nodes)
            {
                WritePose(writer, node.Pose);
                writer.Write(node.Visible);
                writer.Write(node.IsAnchor);
                WriteColliders(writer, node.Colliders);
            }
            if (stream.Length > MaxPacketBytes) throw new InvalidDataException("Bridge geometry packet is too large.");
            return stream.ToArray();
        }
    }

    public static HostWorldBridgeSnapshot Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length > MaxPacketBytes) throw new InvalidDataException("Bridge geometry packet is too large.");
        using (var stream = new MemoryStream(data.ToArray(), false))
        using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
        {
            if (reader.ReadInt32() != Version) throw new InvalidDataException("Unsupported bridge geometry version.");
            var snapshot = new HostWorldBridgeSnapshot { Name = ReadString(reader) };
            int count = ReadCount(reader, MaxMembers);
            for (int i = 0; i < count; i++)
            {
                var bar = new HostWorldBridgeBar { MaterialId = ReadString(reader), Pose = ReadPose(reader) };
                int parts = ReadCount(reader, 16);
                for (int j = 0; j < parts; j++)
                    bar.Parts.Add(new HostWorldBridgePart
                    {
                        IsCap = reader.ReadBoolean(), Visible = reader.ReadBoolean(), Pose = ReadPose(reader)
                    });
                ReadColliders(reader, bar.Colliders);
                snapshot.Bars.Add(bar);
            }
            count = ReadCount(reader, MaxMembers);
            for (int i = 0; i < count; i++)
            {
                var node = new HostWorldBridgeNode
                {
                    Pose = ReadPose(reader), Visible = reader.ReadBoolean(), IsAnchor = reader.ReadBoolean()
                };
                ReadColliders(reader, node.Colliders);
                snapshot.Nodes.Add(node);
            }
            if (stream.Position != stream.Length) throw new InvalidDataException("Unexpected bridge geometry data.");
            return snapshot;
        }
    }

    private static void WriteColliders(BinaryWriter writer, List<HostWorldBridgeCollider> colliders)
    {
        WriteCount(writer, colliders.Count, 32);
        foreach (HostWorldBridgeCollider collider in colliders)
        {
            writer.Write(collider.Kind);
            WritePose(writer, collider.Pose);
            WriteVector(writer, collider.Center);
            WriteVector(writer, collider.Size);
            writer.Write(collider.Radius);
            writer.Write(collider.Height);
            writer.Write(collider.Direction);
            writer.Write(collider.Layer);
            writer.Write(collider.HasMaterial);
            if (!collider.HasMaterial) continue;
            writer.Write(collider.StaticFriction);
            writer.Write(collider.DynamicFriction);
            writer.Write(collider.Bounciness);
            writer.Write(collider.FrictionCombine);
            writer.Write(collider.BounceCombine);
        }
    }

    private static void ReadColliders(BinaryReader reader, List<HostWorldBridgeCollider> colliders)
    {
        int count = ReadCount(reader, 32);
        for (int i = 0; i < count; i++)
        {
            var collider = new HostWorldBridgeCollider
            {
                Kind = reader.ReadByte(), Pose = ReadPose(reader), Center = ReadVector(reader), Size = ReadVector(reader),
                Radius = ReadFloat(reader), Height = ReadFloat(reader), Direction = reader.ReadInt32(),
                Layer = reader.ReadInt32(), HasMaterial = reader.ReadBoolean()
            };
            if (collider.Kind > HostWorldBridgeCollider.Capsule || collider.Layer < 0 || collider.Layer > 31 ||
                collider.Direction < 0 || collider.Direction > 2 ||
                (collider.Kind == HostWorldBridgeCollider.Box &&
                 (collider.Size.x <= 0f || collider.Size.y <= 0f || collider.Size.z <= 0f)) ||
                (collider.Kind != HostWorldBridgeCollider.Box && collider.Radius <= 0f))
                throw new InvalidDataException("Invalid bridge collider.");
            if (collider.HasMaterial)
            {
                collider.StaticFriction = ReadFloat(reader);
                collider.DynamicFriction = ReadFloat(reader);
                collider.Bounciness = ReadFloat(reader);
                collider.FrictionCombine = reader.ReadInt32();
                collider.BounceCombine = reader.ReadInt32();
                if (collider.FrictionCombine < 0 || collider.FrictionCombine > 3 ||
                    collider.BounceCombine < 0 || collider.BounceCombine > 3)
                    throw new InvalidDataException("Invalid bridge physics material.");
            }
            colliders.Add(collider);
        }
    }

    private static void WritePose(BinaryWriter writer, HostWorldBridgePose pose)
    {
        WriteVector(writer, pose.Position);
        writer.Write(pose.Rotation.x); writer.Write(pose.Rotation.y);
        writer.Write(pose.Rotation.z); writer.Write(pose.Rotation.w);
        WriteVector(writer, pose.Scale);
    }

    private static HostWorldBridgePose ReadPose(BinaryReader reader)
    {
        var pose = new HostWorldBridgePose
        {
            Position = ReadVector(reader),
            Rotation = new Quaternion(ReadFloat(reader), ReadFloat(reader), ReadFloat(reader), ReadFloat(reader)),
            Scale = ReadVector(reader)
        };
        float norm = Quaternion.Dot(pose.Rotation, pose.Rotation);
        if (norm < 0.5f || norm > 1.5f) throw new InvalidDataException("Invalid bridge rotation.");
        return pose;
    }

    private static void WriteVector(BinaryWriter writer, Vector3 vector)
    {
        writer.Write(vector.x); writer.Write(vector.y); writer.Write(vector.z);
    }
    private static Vector3 ReadVector(BinaryReader reader) =>
        new Vector3(ReadFloat(reader), ReadFloat(reader), ReadFloat(reader));
    private static float ReadFloat(BinaryReader reader)
    {
        float value = reader.ReadSingle();
        if (float.IsNaN(value) || float.IsInfinity(value) || Mathf.Abs(value) > 1000000f)
            throw new InvalidDataException("Invalid bridge coordinate.");
        return value;
    }
    private static void WriteCount(BinaryWriter writer, int count, int maximum)
    {
        if (count < 0 || count > maximum) throw new InvalidDataException("Too many bridge pieces.");
        writer.Write(count);
    }
    private static int ReadCount(BinaryReader reader, int maximum)
    {
        int count = reader.ReadInt32();
        if (count < 0 || count > maximum) throw new InvalidDataException("Too many bridge pieces.");
        return count;
    }
    private static void WriteString(BinaryWriter writer, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        if (bytes.Length > 512) throw new InvalidDataException("Bridge identifier is too long.");
        writer.Write((ushort)bytes.Length);
        writer.Write(bytes);
    }
    private static string ReadString(BinaryReader reader)
    {
        int length = reader.ReadUInt16();
        if (length > 512) throw new InvalidDataException("Bridge identifier is too long.");
        byte[] bytes = reader.ReadBytes(length);
        if (bytes.Length != length) throw new EndOfStreamException();
        return Encoding.UTF8.GetString(bytes);
    }

    public static bool SameBytes(byte[] left, byte[] right)
    {
        if (left == null || right == null || left.Length != right.Length) return false;
        for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
        return true;
    }

    public static bool IsNewerRevision(int incoming, int current) => unchecked(incoming - current) > 0;
}
