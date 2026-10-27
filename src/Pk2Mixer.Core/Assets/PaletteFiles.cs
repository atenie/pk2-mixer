using Pk2Mixer.Core.Graphics;

namespace Pk2Mixer.Core.Assets;

/// <summary>
/// <c>defaulN.pal</c>: the 8 bpp screen palette, 256 RGB triples (768 bytes).
/// These are interleaved with the terrain ramps in <c>RT2.PK2</c> (entries
/// 1272..1295) and are *not* PALT shade ramps (spec 08).
/// </summary>
public sealed class RgbScreenPalette : PaletteSource
{
    private readonly Rgb[] _colors = new Rgb[256];

    public RgbScreenPalette(byte[] data)
    {
        if (data.Length < 768)
        {
            throw new AssetFormatException($"screen palette needs 768 bytes, got {data.Length}");
        }
        for (var i = 0; i < 256; i++)
        {
            _colors[i] = new Rgb(data[i * 3], data[i * 3 + 1], data[i * 3 + 2]);
        }
    }

    public override string Kind => "PAL";

    public IReadOnlyList<Rgb> Colors => _colors;

    public override Rgb[] GetColors(int shade) => _colors;

    public override string Describe(int shade = DefaultShade) =>
        "8-bit screen palette (256 RGB triples)";

    public override RgbaImage Render(int shade, Rgb[]? screenPalette = null) =>
        RenderSwatches(_colors);
}

/// <summary>
/// <c>rgbto8N.dat</c>: a 32,768-byte RGB -> 8 bpp index conversion table
/// (256 entries wide, 128 rows), used to quantise PALT rows into the screen palette.
/// </summary>
public sealed class RgbToTable : PaletteSource
{
    private readonly byte[] _table;

    public RgbToTable(byte[] data)
    {
        if (data.Length != 32768)
        {
            throw new AssetFormatException($"rgbto table needs 32,768 bytes, got {data.Length}");
        }
        _table = data;
    }

    public override string Kind => "RGBTO";

    /// <summary>Size of the conversion table (always 32,768).</summary>
    public int Length => _table.Length;

    /// <summary>Width of the conversion table in entries.</summary>
    public int Width => 256;

    /// <summary>Height of the conversion table in entries (32,768 / 256 = 128).</summary>
    public int Height => _table.Length / Width;

    public override Rgb[]? GetColors(int shade) => null;

    public override string Describe(int shade = DefaultShade) =>
        "RGB -> 8-bit conversion table (256 x 128 entries, 32,768 bytes)";

    public override RgbaImage Render(int shade, Rgb[]? screenPalette = null)
    {
        var image = new RgbaImage(Width, Height);
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var v = _table[y * Width + x];
                image.Set(x, y, new Rgba(v, v, v, 255));
            }
        }
        return image;
    }
}
