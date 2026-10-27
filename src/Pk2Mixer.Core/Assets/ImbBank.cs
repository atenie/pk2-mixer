using System.Buffers.Binary;

namespace Pk2Mixer.Core.Assets;

/// <summary>One frame (a single mip level of one group) of an <c>.imb</c> bank.</summary>
public sealed class ImbFrame
{
    /// <summary>Frame +0: low byte is the art code (and the palette-gate flags), high byte the mip.</summary>
    public ushort Id { get; internal set; }
    /// <summary>Frame +4: 0x200 = raw pixels, 0x300 = row RLE.</summary>
    public uint Flags { get; internal set; }
    public int X { get; internal set; }
    public int Y { get; internal set; }
    public int Width { get; internal set; }
    public int Height { get; internal set; }
    /// <summary>Index of the sub-array (mip/zoom level) this frame belongs to.</summary>
    public int Level { get; internal set; }
    /// <summary>Stored payload: raw indices or row-RLE bytes.</summary>
    public byte[] Data { get; internal set; } = [];

    public bool IsRaw => (Flags & 0x100) == 0;
}

/// <summary>One 0x44-byte group of an <c>.imb</c> bank.</summary>
public sealed class ImbGroup
{
    /// <summary>Header byte 0: mip mask (bit 0 = mip 0, bit 1 = mip 1).</summary>
    public byte Flags { get; internal set; }
    /// <summary>Header +4: index of the <c>PALT%04d.PAL</c> this group draws with.</summary>
    public uint PaletteIndex { get; internal set; }
    /// <summary>Header +0xc: sub-array index used for zoom 0.</summary>
    public int Mip0 { get; internal set; }
    /// <summary>Byte 0 of the group's zoom-0 first frame: the engine's palette gate.</summary>
    public byte FirstFrameFlags { get; internal set; }
    public List<ImbFrame> Frames { get; } = [];

    /// <summary>True when the group asks for its own <c>PALTnnnn.PAL</c> (frame bit 1).</summary>
    public bool NeedsPalette => (FirstFrameFlags & 2) != 0;

    public ImbFrame? FirstFrame => Frames.Count > 0 ? Frames[0] : null;

    public string PaletteName => $"PALT{PaletteIndex:D4}.PAL";
}

/// <summary>
/// Reader for RT2 <c>.imb</c> image banks (spec 08). Recovered from the loader
/// <c>FUN_004f8b80</c>; the sequential parse must consume the entry exactly.
/// <code>
/// u16 groupCount
/// groupCount * { u8 header[0x40]; u32 subCount;
///                subCount * { u32 blobSize; u16 frameCount; [blob];
///                             frameCount * { u8 frame[0x1d]; u32 dataLen; u8 data[] } } }
/// </code>
/// </summary>
public sealed class ImbBank
{
    private ImbBank() { }

    public IReadOnlyList<ImbGroup> Groups { get; private set; } = [];

    public int GroupCount => Groups.Count;

    public int FrameCount
    {
        get
        {
            var total = 0;
            foreach (var group in Groups)
            {
                total += group.Frames.Count;
            }
            return total;
        }
    }

    /// <summary>Parses a bank; throws <see cref="AssetFormatException"/> on malformed data.</summary>
    public static ImbBank Parse(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < 8)
        {
            throw new AssetFormatException("IMB bank is too small");
        }

        var groupCount = (int)(ReadU32(data, 0) & 0xFFFF);
        if (groupCount is 0 or > 4096)
        {
            throw new AssetFormatException($"IMB bank has an implausible group count ({groupCount})");
        }

        var groups = new List<ImbGroup>(groupCount);
        var p = 4;
        for (var g = 0; g < groupCount; g++)
        {
            Require(data, p, 0x40, g, "group header");
            var group = new ImbGroup
            {
                Flags = data[p],
                PaletteIndex = ReadU32(data, p + 4),
                Mip0 = ReadI32(data, p + 0x0C),
            };
            p += 0x40;

            Require(data, p, 4, g, "sub-array count");
            var subCount = ReadU32(data, p);
            p += 4;
            if (subCount > 65_536)
            {
                throw new AssetFormatException(
                    $"IMB group {g} has an implausible sub-array count ({subCount})");
            }

            byte? firstFrameFlags = null;
            for (var level = 0; level < subCount; level++)
            {
                Require(data, p, 6, g, "sub-array header");
                var blobSize = ReadU32(data, p);
                var frameCount = ReadU16(data, p + 4);
                p += 6;

                if (blobSize >= 2)
                {
                    Require(data, p, (int)blobSize, g, "shared blob");
                    p += (int)blobSize;
                }

                for (var f = 0; f < frameCount; f++)
                {
                    Require(data, p, 0x1D + 4, g, "frame record");
                    var frame = new ImbFrame
                    {
                        Id = (ushort)(ReadU32(data, p) & 0xFFFF),
                        Flags = ReadU32(data, p + 4),
                        X = ReadI32(data, p + 8),
                        Y = ReadI32(data, p + 0x0C),
                        Width = (int)(ReadU32(data, p + 0x10) >> 8),
                        Height = (int)(ReadU32(data, p + 0x14) >> 8),
                        Level = level,
                    };
                    p += 0x1D;

                    var dataLen = ReadU32(data, p);
                    p += 4;
                    Require(data, p, checked((int)dataLen), g, "frame pixels");
                    frame.Data = data.AsSpan(p, (int)dataLen).ToArray();
                    p += (int)dataLen;

                    if (level == group.Mip0 && f == 0)
                    {
                        firstFrameFlags = (byte)(frame.Id & 0xFF);
                    }
                    group.Frames.Add(frame);
                }
            }

            group.FirstFrameFlags = firstFrameFlags ??
                                    (group.Frames.Count > 0 ? (byte)(group.Frames[0].Id & 0xFF) : (byte)0);
            groups.Add(group);
        }

        if (p != data.Length)
        {
            throw new AssetFormatException(
                $"IMB bank has {data.Length - p} trailing bytes after the last group");
        }

        return new ImbBank { Groups = groups };
    }

    public static bool TryParse(byte[] data, out ImbBank? bank, out string? error)
    {
        try
        {
            bank = Parse(data);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is AssetFormatException or ArgumentOutOfRangeException
                                       or OverflowException)
        {
            bank = null;
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Cheap structural guard (matches the exporter's <c>looks_like_imb</c>).</summary>
    public static bool LooksLikeImb(ReadOnlySpan<byte> head)
    {
        if (head.Length < 8)
        {
            return false;
        }
        var value = BinaryPrimitives.ReadUInt32LittleEndian(head);
        return (value >> 16) == 0 && (value & 0xFFFF) is >= 1 and <= 4096;
    }

    public ImbFrame? Frame(int group, int level, int index)
    {
        if (group < 0 || group >= Groups.Count)
        {
            return null;
        }
        var found = 0;
        foreach (var frame in Groups[group].Frames)
        {
            if (frame.Level != level)
            {
                continue;
            }
            if (found++ == index)
            {
                return frame;
            }
        }
        return null;
    }

    private static void Require(byte[] data, int offset, int length, int group, string what)
    {
        if (offset < 0 || length < 0 || (long)offset + length > data.Length)
        {
            throw new AssetFormatException(
                $"IMB group {group}: {what} extends past the end of the bank");
        }
    }

    private static uint ReadU32(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));

    private static ushort ReadU16(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset));

    private static int ReadI32(byte[] data, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset));
}
