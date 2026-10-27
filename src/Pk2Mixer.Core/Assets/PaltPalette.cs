using System.Buffers.Binary;
using Pk2Mixer.Core.Graphics;

namespace Pk2Mixer.Core.Assets;

/// <summary>
/// <c>PALT%04d.PAL</c>: a 16-bit shade-ramp palette (spec 08, <c>FUN_004f8ed0</c>):
/// <code>
/// u32 rowCount
/// s8  shadeToRow[35]          // shade 0..34 -> row, -1 = no palette for that shade
/// u16 rows[rowCount][256]     // RGB555
/// </code>
/// File size is exactly <c>39 + 512 * rowCount</c>. Index 0 is a transparency key.
/// </summary>
public sealed class PaltPalette : PaletteSource
{
    private const int HeaderBytes = 4 + 35;

    private readonly sbyte[] _shadeToRow;
    private readonly Rgb[][] _rows;

    private PaltPalette(int rowCount, sbyte[] shadeToRow, Rgb[][] rows)
    {
        RowCount = rowCount;
        _shadeToRow = shadeToRow;
        _rows = rows;
    }

    public int RowCount { get; }

    public override string Kind => "PALT";

    public static bool TryParse(byte[] data, out PaltPalette? palette)
    {
        palette = null;
        if (data.Length < HeaderBytes)
        {
            return false;
        }
        var rowCount = BinaryPrimitives.ReadUInt32LittleEndian(data);
        if (rowCount is 0 or > 4096 || data.Length != HeaderBytes + (long)rowCount * 512)
        {
            return false;
        }
        var count = (int)rowCount;

        var shadeToRow = new sbyte[35];
        for (var i = 0; i < 35; i++)
        {
            shadeToRow[i] = unchecked((sbyte)data[4 + i]);
        }

        var rows = new Rgb[count][];
        for (var r = 0; r < count; r++)
        {
            var row = new Rgb[256];
            var offset = HeaderBytes + r * 512;
            for (var i = 0; i < 256; i++)
            {
                var word = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset + i * 2));
                row[i] = Rgb.FromRgb555(word);
            }
            rows[r] = row;
        }

        palette = new PaltPalette((int)rowCount, shadeToRow, rows);
        return true;
    }

    /// <summary>Row the engine selects for a shade level (or -1 when undefined).</summary>
    public int RowForShade(int shade)
    {
        if (shade < 0 || shade >= _shadeToRow.Length)
        {
            return -1;
        }
        var row = _shadeToRow[shade];
        return row < 0 || row >= RowCount ? -1 : row;
    }

    /// <summary>Row 16 of the dark -> bright ramp, which is what sprite draws use.</summary>
    public int NeutralRow => RowForShade(DefaultShade);

    public override Rgb[] GetColors(int shade)
    {
        var row = RowForShade(shade);
        if (row < 0)
        {
            row = RowForShade(DefaultShade);
        }
        if (row < 0)
        {
            row = 0;
        }
        return _rows[row];
    }

    public IReadOnlyList<Rgb[]> Rows => _rows;

    public override string Describe(int shade = DefaultShade)
    {
        var row = RowForShade(shade);
        return $"{RowCount} shade row{(RowCount == 1 ? "" : "s")}, " +
               $"shade {shade} -> row {(row < 0 ? "undefined (using neutral)" : row.ToString())}";
    }

    public override RgbaImage Render(int shade, Rgb[]? screenPalette = null) =>
        RenderSwatches(GetColors(shade));
}
