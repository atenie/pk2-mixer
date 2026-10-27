using System.Buffers.Binary;

namespace Pk2Mixer.Tests;

/// <summary>Builds byte-exact synthetic banks following spec 08.</summary>
internal static class SyntheticBank
{
    /// <summary>4 x 3 raw indices: one pixel at (0,0), one at (1,2), rest transparent.</summary>
    public static readonly byte[] RawPixels =
    [
        1, 2, 3, 0,
        0, 0, 0, 0,
        0, 4, 0, 0,
    ];

    /// <summary>4 x 2 row-RLE payload: [literal 2][transparent 2] / [transparent 4] / end.</summary>
    public static readonly byte[] RlePixels =
    [
        0x05, 0x02, 0x01, 0x02, 0x82,
        0x02, 0xC4,
        0xC0,
    ];

    public static byte[] BuildRawBank(ushort id = 0x2a) =>
        BuildBank(4, 3, 0x200, RawPixels, id);

    public static byte[] BuildRleBank(ushort id = 0x2a) =>
        BuildBank(4, 2, 0x300, RlePixels, id);

    public static byte[] BuildBank(uint width, uint height, uint frameFlags, byte[] pixels,
        ushort id = 0x2a, uint paletteIndex = 58)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        writer.Write(1u);   // group count (only the low u16 is used)

        var header = new byte[0x40];
        header[0] = 0x01;   // mip mask: mip 0 present
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), paletteIndex);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0x0C), 0);   // zoom 0 -> sub 0
        writer.Write(header);

        writer.Write(1u);            // sub-array count
        writer.Write(0u);            // blob size (< 2: no blob)
        writer.Write((ushort)1);     // frame count

        writer.Write((uint)id);      // frame +0x00: art code / palette gate
        writer.Write(frameFlags);    // frame +0x04: 0x200 raw, 0x300 RLE
        writer.Write(0);             // frame +0x08 x
        writer.Write(0);             // frame +0x0c y
        writer.Write(width << 8);    // frame +0x10 width (8.8 fixed point)
        writer.Write(height << 8);   // frame +0x14 height
        writer.Write((byte)0);       // +0x18 pad
        writer.Write(0u);            // +0x19 runtime pixel pointer
        writer.Write((uint)pixels.Length);
        writer.Write(pixels);

        return stream.ToArray();
    }

    /// <summary>A PALT palette with <paramref name="rows"/> shade rows.</summary>
    public static byte[] BuildPalt(int rows, sbyte[] shadeToRow, ushort[][] rowColors)
    {
        if (shadeToRow.Length != 35 || rowColors.Length != rows)
        {
            throw new ArgumentException("bad synthetic palette");
        }

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write((uint)rows);
        foreach (var shade in shadeToRow)
        {
            writer.Write(shade);
        }
        foreach (var row in rowColors)
        {
            for (var i = 0; i < 256; i++)
            {
                writer.Write(i < row.Length ? row[i] : (ushort)0);
            }
        }
        return stream.ToArray();
    }

    public static byte[] BuildFlatc16(int rows)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write((uint)rows);
        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < 32; c++)
            {
                // row 0 is a red ramp, row 1 a green ramp
                var level = (ushort)(c * 31 / 31);
                writer.Write(r == 0 ? (ushort)(level << 10) : (ushort)(level << 5));
            }
        }
        return stream.ToArray();
    }

    public static byte[] BuildFlatc8(int rows)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write((uint)rows);
        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < 32; c++)
            {
                writer.Write((byte)(r * 32 + c));
            }
        }
        return stream.ToArray();
    }
}
