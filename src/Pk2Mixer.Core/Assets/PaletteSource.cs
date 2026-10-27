using Pk2Mixer.Core.Graphics;

namespace Pk2Mixer.Core.Assets;

/// <summary>
/// Base class for every palette-like file the game ships: 16-bit <c>PALTnnnn.PAL</c>
/// shade ramps, <c>defaulN.pal</c> 768-byte screen palettes, the <c>FLATC16/8.DAT</c>
/// shading ramps and the <c>rgbto8N.dat</c> conversion tables (spec 08).
/// </summary>
public abstract class PaletteSource
{
    /// <summary>The engine's standard sprite/tile draw shade (neutral row).</summary>
    public const int DefaultShade = 16;

    public abstract string Kind { get; }

    public abstract string Describe(int shade = DefaultShade);

    /// <summary>256 colours to draw an <c>.imb</c> frame, or null if not usable for that.</summary>
    public abstract Rgb[]? GetColors(int shade);

    /// <summary>A small palette/ramp image for the preview pane.</summary>
    public abstract RgbaImage Render(int shade, Rgb[]? screenPalette = null);

    /// <summary>Recognises a palette/DAT payload by its size and documented layout.</summary>
    public static PaletteSource? FromData(string name, byte[] data)
    {
        if (data is null || data.Length < 8)
        {
            return null;
        }

        if (PaltPalette.TryParse(data, out var palt))
        {
            return palt;
        }
        if (data.Length == 768)
        {
            return new RgbScreenPalette(data);
        }
        if (data.Length == 32768)
        {
            return new RgbToTable(data);
        }

        var isFlatc16 = name.Contains("FLATC16", StringComparison.OrdinalIgnoreCase);
        var isFlatc8 = name.Contains("FLATC8", StringComparison.OrdinalIgnoreCase);
        if (isFlatc16 && FlatcRamp.TryParse(data, sixteenBit: true, out var ramp16))
        {
            return ramp16;
        }
        if (isFlatc8 && FlatcRamp.TryParse(data, sixteenBit: false, out var ramp8))
        {
            return ramp8;
        }
        if (FlatcRamp.TryParse(data, sixteenBit: true, out var ramp16b))
        {
            return ramp16b;
        }
        if (FlatcRamp.TryParse(data, sixteenBit: false, out var ramp8b))
        {
            return ramp8b;
        }
        return null;
    }

    protected static RgbaImage RenderSwatches(Rgb[] colors, int cells = 16, int cellSize = 12)
    {
        const int background = 30;
        var image = RgbaImage.Filled(cells * cellSize, cells * cellSize,
            new Rgba(background, background, background, 255));
        for (var i = 0; i < colors.Length && i < cells * cells; i++)
        {
            // index 0 is the blitter's transparency key: mark it instead of painting it
            var color = i == 0
                ? new Rgba(70, 70, 70, 255)
                : Rgba.FromRgb(colors[i]);
            var x0 = (i % cells) * cellSize;
            var y0 = (i / cells) * cellSize;
            for (var y = 0; y < cellSize; y++)
            {
                for (var x = 0; x < cellSize; x++)
                {
                    image.Set(x0 + x, y0 + y, color);
                }
            }
        }
        return image;
    }
}
