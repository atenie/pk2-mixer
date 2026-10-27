using Pk2Mixer.Core.Graphics;

namespace Pk2Mixer.Core.Assets;

/// <summary>
/// Renders decoded <c>.imb</c> frames with a 256-entry palette. Index 0 is the
/// blitter's transparency key (spec 08), so it is emitted as transparent.
/// </summary>
public static class ImbRenderer
{
    private static readonly Rgb[] Gray = CreateGray();

    /// <summary>Neutral fallback palette used when a group's PALT cannot be resolved.</summary>
    public static Rgb[] Grayscale => Gray;

    public static RgbaImage RenderFrame(ImbFrame frame, Rgb[]? colors)
    {
        var decoded = ImbFrameDecoder.Decode(frame);
        var image = new RgbaImage(decoded.Width, decoded.Height);
        var palette = colors ?? Gray;
        for (var i = 0; i < decoded.Indices.Length; i++)
        {
            var index = decoded.Indices[i];
            if (index == 0)
            {
                continue;   // transparency key
            }
            var rgb = index < palette.Length ? palette[index] : palette[0];
            image.Set(i % decoded.Width, i / decoded.Width, Rgba.FromRgb(rgb));
        }
        return image;
    }

    /// <summary>
    /// Contact sheet of every group's <c>mip0</c> frame (12 per row, like
    /// <c>tools/recon/export_pk2_imb_png.py</c>), with a per-group palette.
    /// </summary>
    public static RgbaImage RenderSheet(ImbBank bank, Func<ImbGroup, Rgb[]?> paletteForGroup,
        int columns = 12, int maxDimension = 4096)
    {
        ArgumentNullException.ThrowIfNull(bank);
        ArgumentNullException.ThrowIfNull(paletteForGroup);

        var tiles = new List<(RgbaImage Image, int Width, int Height)>();
        var cellWidth = 1;
        var cellHeight = 1;
        foreach (var group in bank.Groups)
        {
            var frame = group.FirstFrame;
            if (frame is null || frame.Width <= 0 || frame.Height <= 0)
            {
                continue;
            }
            RgbaImage image;
            try
            {
                image = RenderFrame(frame, paletteForGroup(group));
            }
            catch (AssetFormatException)
            {
                continue;
            }
            tiles.Add((image, image.Width, image.Height));
            cellWidth = Math.Max(cellWidth, image.Width);
            cellHeight = Math.Max(cellHeight, image.Height);
        }

        if (tiles.Count == 0)
        {
            return RgbaImage.Filled(1, 1, new Rgba(24, 24, 24, 255));
        }

        cellWidth += 2;
        cellHeight += 2;
        columns = Math.Max(1, Math.Min(columns, tiles.Count));
        var rows = (tiles.Count + columns - 1) / columns;
        var sheet = RgbaImage.Filled(columns * cellWidth, rows * cellHeight,
            new Rgba(24, 24, 24, 255));

        for (var i = 0; i < tiles.Count; i++)
        {
            var (image, _, _) = tiles[i];
            var originX = (i % columns) * cellWidth + 1;
            var originY = (i / columns) * cellHeight + 1;
            sheet.Blit(image, originX, originY);
        }

        return maxDimension > 0 ? sheet.ScaleToFit(maxDimension, maxDimension) : sheet;
    }

    private static Rgb[] CreateGray()
    {
        var colors = new Rgb[256];
        for (var i = 0; i < 256; i++)
        {
            colors[i] = new Rgb((byte)i, (byte)i, (byte)i);
        }
        return colors;
    }
}
