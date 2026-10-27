using System.Buffers.Binary;
using Pk2Mixer.Core.Graphics;

namespace Pk2Mixer.Core.Assets;

/// <summary>
/// <c>FLATC16.DAT</c> / <c>FLATC8n.DAT</c>: the flat-cell shading ramps
/// (236 rows x 32 shade entries), used for the solid terrain fill rather than
/// for sprites (spec 08). 16-bit rows are RGB555 words, 8-bit rows are indices
/// into the active screen palette.
/// </summary>
public sealed class FlatcRamp : PaletteSource
{
    private readonly Rgb[] _colors;   // 16-bit only: RowCount * 32 RGB entries

    private FlatcRamp(int rowCount, int bits, Rgb[] colors, byte[]? eightBit)
    {
        RowCount = rowCount;
        Bits = bits;
        _colors = colors;
        EightBit = eightBit;
    }

    public int RowCount { get; }
    public int Bits { get; }
    /// <summary>Raw 8-bit values (null for 16-bit ramps).</summary>
    public byte[]? EightBit { get; }

    public override string Kind => Bits == 16 ? "FLATC16" : "FLATC8";

    public static bool TryParse(byte[] data, bool sixteenBit, out FlatcRamp? ramp)
    {
        ramp = null;
        if (data.Length <= 4)
        {
            return false;
        }
        var payload = data.Length - 4;
        var stride = sixteenBit ? 64 : 32;
        if (payload % stride != 0)
        {
            return false;
        }
        var rows = payload / stride;
        if (rows is 0 or > 4096)
        {
            return false;
        }

        if (sixteenBit)
        {
            var colors = new Rgb[rows * 32];
            for (var r = 0; r < rows; r++)
            {
                for (var c = 0; c < 32; c++)
                {
                    var at = 4 + r * 64 + c * 2;
                    colors[r * 32 + c] = Rgb.FromRgb555(BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at)));
                }
            }
            ramp = new FlatcRamp(rows, 16, colors, null);
            return true;
        }

        var eightBit = data.AsSpan(4).ToArray();
        ramp = new FlatcRamp(rows, 8, [], eightBit);
        return true;
    }

    public override Rgb[]? GetColors(int shade) => null;

    public override string Describe(int shade = DefaultShade) =>
        $"{RowCount} rows x 32 {(Bits == 16 ? "RGB555" : "8-bit")} shade entries";

    public override RgbaImage Render(int shade, Rgb[]? screenPalette = null)
    {
        var image = new RgbaImage(32, RowCount);
        for (var r = 0; r < RowCount; r++)
        {
            for (var c = 0; c < 32; c++)
            {
                Rgba color;
                if (Bits == 16)
                {
                    color = Rgba.FromRgb(_colors[r * 32 + c]);
                }
                else
                {
                    var v = EightBit![r * 32 + c];
                    color = screenPalette is not null
                        ? Rgba.FromRgb(screenPalette[v])
                        : new Rgba(v, v, v, 255);
                }
                image.Set(c, r, color);
            }
        }
        return image;
    }
}
