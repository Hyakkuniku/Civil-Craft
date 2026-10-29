using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

[Serializable]
public sealed class BridgePhotoAlbumEntry
{
    public string contractId;
    public string regionId;
    public string resultId;
    public string entryName;
    public string sha256;
    public string capturedAt;
}

[Serializable]
public sealed class BridgePhotoAlbumManifest
{
    public int schemaVersion = 1;
    public List<BridgePhotoAlbumEntry> photos = new List<BridgePhotoAlbumEntry>();
}

/// <summary>
/// Canonical, bounded STORED ZIP codec shared with the website's archive-v1 reader.
/// PNGs are already compressed. No inflater, filesystem extraction, ZIP64,
/// descriptors, comments, extra fields or caller-selected paths are supported.
/// </summary>
public sealed class BridgePhotoAlbum
{
    public const string FileName = "bridge-photos-v1.zip";
    public const int MaximumBytes = 8 * 1024 * 1024;
    public const int MaximumImageBytes = 512 * 1024;
    public const int MaximumManifestBytes = 64 * 1024;
    public const int MaximumPhotos = 64;
    public const int MaximumWidth = 960;
    public const int MaximumHeight = 540;
    private const string ManifestName = "manifest.json";
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
    private static readonly Regex ResultPattern = new Regex("^[a-f0-9]{32}$");
    private static readonly Regex HashPattern = new Regex("^[a-f0-9]{64}$");
    private static readonly Regex ImagePattern = new Regex("^bridge-[a-f0-9]{32}\\.png$");
    private static readonly uint[] CrcTable = BuildCrcTable();
    public readonly BridgePhotoAlbumManifest Manifest = new BridgePhotoAlbumManifest();
    private readonly Dictionary<string, byte[]> images = new Dictionary<string, byte[]>(StringComparer.Ordinal);

    public static string EntryName(string resultId) => "bridge-" + resultId + ".png";
    public static bool IsResultId(string id) => id != null && ResultPattern.IsMatch(id);
    public static bool IsHash(string hash) => hash != null && HashPattern.IsMatch(hash);
    public static string Sha256(byte[] bytes)
    {
        using (SHA256 hash = SHA256.Create())
            return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }

    public BridgePhotoAlbumEntry Find(string contractId, string regionId) =>
        Manifest.photos.Find(entry => entry.contractId == contractId && entry.regionId == regionId);

    public bool Contains(BridgePhotoAlbumEntry entry)
    {
        var current = entry == null ? null : Find(entry.contractId, entry.regionId);
        return current != null && current.resultId == entry.resultId && current.sha256 == entry.sha256 &&
            current.entryName == entry.entryName;
    }

    public void Replace(BridgePhotoAlbumEntry entry, byte[] png)
    {
        ValidateEntry(entry);
        ValidatePng(png);
        if (Sha256(png) != entry.sha256) throw new InvalidDataException("Bridge image checksum mismatch.");
        var previous = Find(entry.contractId, entry.regionId);
        if (previous != null)
        {
            Manifest.photos.Remove(previous);
            images.Remove(previous.entryName);
        }
        if (Manifest.photos.Count >= MaximumPhotos || images.ContainsKey(entry.entryName))
            throw new InvalidDataException("Bridge album entry limit or duplicate result.");
        Manifest.photos.Add(entry);
        images.Add(entry.entryName, (byte[])png.Clone());
    }

    public byte[] Encode()
    {
        if (Manifest.photos.Count > MaximumPhotos) throw new InvalidDataException("Bridge album has too many photos.");
        Manifest.photos.Sort((a, b) => string.Compare(a.entryName, b.entryName, StringComparison.Ordinal));
        byte[] manifest = Utf8.GetBytes(JsonUtility.ToJson(Manifest));
        if (manifest.Length > MaximumManifestBytes) throw new InvalidDataException("Bridge album index is too large.");
        var entries = new List<KeyValuePair<string, byte[]>> { new KeyValuePair<string, byte[]>(ManifestName, manifest) };
        foreach (var photo in Manifest.photos)
        {
            ValidateEntry(photo);
            if (!images.TryGetValue(photo.entryName, out byte[] image)) throw new InvalidDataException("Bridge image is missing.");
            ValidatePng(image);
            if (Sha256(image) != photo.sha256) throw new InvalidDataException("Bridge image checksum mismatch.");
            entries.Add(new KeyValuePair<string, byte[]>(photo.entryName, image));
        }
        using (var buffer = new MemoryStream())
        using (var writer = new BinaryWriter(buffer, Utf8, true))
        {
            var offsets = new List<int>();
            foreach (var item in entries)
            {
                offsets.Add(checked((int)buffer.Position));
                byte[] name = Utf8.GetBytes(item.Key);
                writer.Write(0x04034b50u); writer.Write((ushort)20); writer.Write((ushort)0); writer.Write((ushort)0);
                writer.Write(0u); writer.Write(Crc32(item.Value)); writer.Write(item.Value.Length); writer.Write(item.Value.Length);
                writer.Write((ushort)name.Length); writer.Write((ushort)0); writer.Write(name); writer.Write(item.Value);
                if (buffer.Length > MaximumBytes) throw new InvalidDataException("Bridge album exceeds 8 MiB; no images were removed.");
            }
            int centralOffset = checked((int)buffer.Position);
            for (int index = 0; index < entries.Count; index++)
            {
                var item = entries[index]; byte[] name = Utf8.GetBytes(item.Key);
                writer.Write(0x02014b50u); writer.Write((ushort)20); writer.Write((ushort)20);
                writer.Write((ushort)0); writer.Write((ushort)0); writer.Write(0u); writer.Write(Crc32(item.Value));
                writer.Write(item.Value.Length); writer.Write(item.Value.Length); writer.Write((ushort)name.Length);
                writer.Write((ushort)0); writer.Write((ushort)0); writer.Write((ushort)0); writer.Write((ushort)0);
                writer.Write(0u); writer.Write(offsets[index]); writer.Write(name);
            }
            int centralLength = checked((int)buffer.Position - centralOffset);
            writer.Write(0x06054b50u); writer.Write((ushort)0); writer.Write((ushort)0);
            writer.Write((ushort)entries.Count); writer.Write((ushort)entries.Count);
            writer.Write(centralLength); writer.Write(centralOffset); writer.Write((ushort)0);
            if (buffer.Length > MaximumBytes) throw new InvalidDataException("Bridge album exceeds 8 MiB; no images were removed.");
            return buffer.ToArray();
        }
    }

    public static BridgePhotoAlbum Decode(byte[] bytes)
    {
        if (bytes == null || bytes.Length < 22 || bytes.Length > MaximumBytes)
            throw new InvalidDataException("Bridge album size is invalid.");
        int end = bytes.Length - 22;
        if (U32(bytes, end) != 0x06054b50u || U16(bytes, end + 4) != 0 || U16(bytes, end + 6) != 0 ||
            U16(bytes, end + 20) != 0) throw new InvalidDataException("Bridge album directory is invalid.");
        int count = U16(bytes, end + 10), centralSize = Positive32(bytes, end + 12), central = Positive32(bytes, end + 16);
        if (count < 1 || count > MaximumPhotos + 1 || count != U16(bytes, end + 8) ||
            (long)central + centralSize != end) throw new InvalidDataException("Bridge album directory bounds are invalid.");
        var contents = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        int cursor = central, nextLocal = 0;
        for (int index = 0; index < count; index++)
        {
            Require(bytes, cursor, 46);
            int nameLength = U16(bytes, cursor + 28), size = Positive32(bytes, cursor + 24), compressed = Positive32(bytes, cursor + 20);
            int local = Positive32(bytes, cursor + 42); uint checksum = U32(bytes, cursor + 16);
            int flags = U16(bytes, cursor + 8);
            if (U32(bytes, cursor) != 0x02014b50u || U16(bytes, cursor + 6) > 20 ||
                (flags != 0 && flags != 0x800) || U16(bytes, cursor + 10) != 0 || size != compressed ||
                U16(bytes, cursor + 30) != 0 || U16(bytes, cursor + 32) != 0 || U16(bytes, cursor + 34) != 0 ||
                U32(bytes, cursor + 38) != 0 || local != nextLocal || cursor + 46L + nameLength > end)
                throw new InvalidDataException("Unsupported or inconsistent bridge ZIP entry.");
            Require(bytes, cursor + 46, nameLength);
            string name = Utf8.GetString(bytes, cursor + 46, nameLength);
            if ((name != ManifestName && !ImagePattern.IsMatch(name)) || contents.ContainsKey(name) ||
                size > (name == ManifestName ? MaximumManifestBytes : MaximumImageBytes))
                throw new InvalidDataException("Bridge album entry name, count or size is invalid.");
            Require(bytes, local, 30);
            int localNameLength = U16(bytes, local + 26);
            if (U32(bytes, local) != 0x04034b50u || U16(bytes, local + 4) > 20 ||
                U16(bytes, local + 6) != flags || U16(bytes, local + 8) != 0 ||
                U32(bytes, local + 10) != U32(bytes, cursor + 12) || U32(bytes, local + 14) != checksum ||
                Positive32(bytes, local + 18) != size || Positive32(bytes, local + 22) != size ||
                localNameLength != nameLength || U16(bytes, local + 28) != 0 ||
                (long)local + 30 + nameLength + size > central)
                throw new InvalidDataException("Bridge album local entry does not match its directory.");
            if (Utf8.GetString(bytes, local + 30, localNameLength) != name)
                throw new InvalidDataException("Bridge album entry name mismatch.");
            byte[] content = new byte[size];
            Buffer.BlockCopy(bytes, local + 30 + nameLength, content, 0, size);
            if (Crc32(content) != checksum) throw new InvalidDataException("Bridge ZIP checksum mismatch.");
            contents.Add(name, content);
            nextLocal = local + 30 + nameLength + size;
            cursor += 46 + nameLength;
        }
        if (cursor != end || nextLocal != central || !contents.TryGetValue(ManifestName, out byte[] manifestBytes))
            throw new InvalidDataException("Bridge album has missing or trailing data.");
        BridgePhotoAlbumManifest manifest;
        try { manifest = JsonUtility.FromJson<BridgePhotoAlbumManifest>(Utf8.GetString(manifestBytes)); }
        catch (Exception) { throw new InvalidDataException("Bridge album index is invalid."); }
        if (manifest == null || manifest.schemaVersion != 1 || manifest.photos == null ||
            manifest.photos.Count > MaximumPhotos || manifest.photos.Count != contents.Count - 1)
            throw new InvalidDataException("Bridge album index count is invalid.");
        var album = new BridgePhotoAlbum();
        var contracts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var photo in manifest.photos)
        {
            ValidateEntry(photo);
            if (!contracts.Add(photo.regionId + "\n" + photo.contractId) || !contents.TryGetValue(photo.entryName, out byte[] png))
                throw new InvalidDataException("Bridge album index is ambiguous or incomplete.");
            album.Replace(photo, png);
        }
        return album;
    }

    public static void ValidateEntry(BridgePhotoAlbumEntry entry)
    {
        if (entry == null || !SafeId(entry.contractId) || !SafeId(entry.regionId) || !IsResultId(entry.resultId) ||
            entry.entryName != EntryName(entry.resultId) || !IsHash(entry.sha256) ||
            (entry.capturedAt != null && entry.capturedAt.Length > 64))
            throw new InvalidDataException("Bridge photo identity is invalid.");
    }
    private static bool SafeId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 200 || id != id.Trim()) return false;
        foreach (char c in id) if (char.IsControl(c)) return false;
        return true;
    }
    public static void ValidatePng(byte[] png)
    {
        byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
        if (png == null || png.Length < 33 || png.Length > MaximumImageBytes)
            throw new InvalidDataException("Bridge PNG size is invalid.");
        for (int i = 0; i < signature.Length; i++) if (png[i] != signature[i])
            throw new InvalidDataException("Bridge photo is not PNG.");
        if (Big32(png, 8) != 13 || Encoding.ASCII.GetString(png, 12, 4) != "IHDR" ||
            Big32(png, 16) < 1 || Big32(png, 16) > MaximumWidth ||
            Big32(png, 20) < 1 || Big32(png, 20) > MaximumHeight)
            throw new InvalidDataException("Bridge PNG dimensions are invalid.");
        int offset = 8;
        bool header = false, imageData = false, end = false;
        while (offset < png.Length)
        {
            if ((long)offset + 12 > png.Length) throw new InvalidDataException("Truncated bridge PNG.");
            uint length = Big32(png, offset);
            if (length > MaximumImageBytes || (long)offset + 12 + length > png.Length)
                throw new InvalidDataException("Bridge PNG chunk is out of bounds.");
            string kind = Encoding.ASCII.GetString(png, offset + 4, 4);
            foreach (char character in kind)
                if (!((character >= 'A' && character <= 'Z') || (character >= 'a' && character <= 'z')))
                    throw new InvalidDataException("Invalid bridge PNG chunk type.");
            int size = (int)length;
            if (Crc32(png, offset + 4, size + 4) != Big32(png, offset + 8 + size))
                throw new InvalidDataException("Bridge PNG chunk checksum mismatch.");
            if (kind == "IHDR")
            {
                if (header || offset != 8 || size != 13 || png[offset + 18] != 0 || png[offset + 19] != 0 || png[offset + 20] > 1)
                    throw new InvalidDataException("Invalid bridge PNG header.");
                int depth = png[offset + 16], color = png[offset + 17];
                bool validDepth = color == 0 ? depth == 1 || depth == 2 || depth == 4 || depth == 8 || depth == 16 :
                    color == 3 ? depth == 1 || depth == 2 || depth == 4 || depth == 8 :
                    (color == 2 || color == 4 || color == 6) && (depth == 8 || depth == 16);
                if (!validDepth) throw new InvalidDataException("Invalid bridge PNG pixel format.");
                header = true;
            }
            else if (kind == "IDAT") { if (size > 0) imageData = true; }
            else if (kind == "IEND")
            {
                if (size != 0 || !imageData || offset + 12 != png.Length) throw new InvalidDataException("Invalid bridge PNG ending.");
                end = true;
            }
            else if (kind[0] >= 'A' && kind[0] <= 'Z' && kind != "PLTE")
                throw new InvalidDataException("Unsupported critical bridge PNG chunk.");
            offset += size + 12;
        }
        if (!header || !imageData || !end) throw new InvalidDataException("Incomplete bridge PNG.");
    }
    private static uint Big32(byte[] b, int p) => (uint)b[p] << 24 | (uint)b[p + 1] << 16 | (uint)b[p + 2] << 8 | b[p + 3];
    private static ushort U16(byte[] b, int p) { Require(b, p, 2); return (ushort)(b[p] | b[p + 1] << 8); }
    private static uint U32(byte[] b, int p) { Require(b, p, 4); return (uint)b[p] | (uint)b[p + 1] << 8 | (uint)b[p + 2] << 16 | (uint)b[p + 3] << 24; }
    private static int Positive32(byte[] b, int p) { uint n = U32(b, p); if (n > int.MaxValue) throw new InvalidDataException("Bridge ZIP64/size is unsupported."); return (int)n; }
    private static void Require(byte[] b, int p, int length) { if (p < 0 || length < 0 || (long)p + length > b.Length) throw new InvalidDataException("Truncated bridge album."); }
    public static uint Crc32(byte[] bytes)
        => Crc32(bytes, 0, bytes.Length);
    private static uint Crc32(byte[] bytes, int offset, int length)
    {
        uint crc = 0xffffffff;
        for (int index = offset; index < offset + length; index++) crc = CrcTable[(crc ^ bytes[index]) & 255] ^ (crc >> 8);
        return ~crc;
    }
    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            uint value = index;
            for (int bit = 0; bit < 8; bit++) value = (value & 1) != 0 ? (value >> 1) ^ 0xedb88320 : value >> 1;
            table[index] = value;
        }
        return table;
    }
}
