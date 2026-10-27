using Pk2Mixer.Core.Assets;

namespace Pk2Mixer.Core.Graphics;

/// <summary>Decoded frame pixels: 8-bit palette indices (0 = transparent key).</summary>
public sealed class DecodedFrame
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required byte[] Indices { get; init; }
    public required string Mode { get; init; }
    public int AlphaPixels { get; init; }
}

/// <summary>
/// Pixel decoding for RT2 <c>.imb</c> frames (spec 08, <c>tools/recon/imb_codec.py</c>).
/// Frame flags at +4 select the storage: <c>0x200</c> raw indices, <c>0x300</c>
/// row-based RLE.
/// </summary>
public static class ImbFrameDecoder
{
    private const int MaxPixels = 64 * 1024 * 1024;

    public static DecodedFrame Decode(ImbFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var width = frame.Width;
        var height = frame.Height;
        if (width < 0 || height < 0)
        {
            throw new AssetFormatException($"frame has a negative size ({width}x{height})");
        }
        var pixels = checked(width * height);
        if (pixels > MaxPixels)
        {
            throw new AssetFormatException($"frame is too large ({width}x{height})");
        }
        var indices = new byte[pixels];
        if (pixels == 0)
        {
            return new DecodedFrame { Width = width, Height = height, Indices = indices, Mode = "empty" };
        }

        if (frame.IsRaw)
        {
            if (frame.Data.Length != pixels)
            {
                throw new AssetFormatException(
                    $"raw frame {width}x{height} has {frame.Data.Length} bytes (expected {pixels})");
            }
            Buffer.BlockCopy(frame.Data, 0, indices, 0, pixels);
            return new DecodedFrame { Width = width, Height = height, Indices = indices, Mode = "raw" };
        }

        return DecodeRle(frame, indices);
    }

    private static DecodedFrame DecodeRle(ImbFrame frame, byte[] indices)
    {
        var width = frame.Width;
        var height = frame.Height;
        var data = frame.Data;
        var p = 0;
        var alphaPixels = 0;
        var ended = false;

        for (var y = 0; y < height && !ended; y++)
        {
            if (p >= data.Length)
            {
                throw new AssetFormatException($"RLE row {y}: past end of frame data");
            }

            var start = p;
            int rowLength;
            var b = data[p];
            if (b < 0x80)
            {
                rowLength = b;
                p += 1;
            }
            else if (b < 0xC0)
            {
                if (p + 1 >= data.Length)
                {
                    throw new AssetFormatException($"RLE row {y}: truncated row length");
                }
                rowLength = ((b & 0x3F) << 8) | data[p + 1];
                p += 2;
            }
            else
            {
                ended = true;
                break;
            }

            var rowEnd = start + rowLength;
            if (rowEnd > data.Length)
            {
                throw new AssetFormatException($"RLE row {y}: row extends past frame data");
            }

            var x = 0;
            while (p < rowEnd && x < width)
            {
                var cmd = data[p++];
                if (cmd == 0)
                {
                    break;
                }

                if (cmd < 0x80)
                {
                    // literal palette indices
                    var n = (int)cmd;
                    if (p + n > rowEnd)
                    {
                        throw new AssetFormatException($"RLE row {y}: literal run past row end");
                    }
                    if (x + n > width)
                    {
                        throw new AssetFormatException($"RLE row {y}: literal run past row width");
                    }
                    Buffer.BlockCopy(data, p, indices, y * width + x, n);
                    p += n;
                    x += n;
                }
                else if (cmd < 0xA0)
                {
                    // transparent span, low 3 bits (0 => next byte)
                    var n = cmd & 7;
                    if (n == 0)
                    {
                        if (p >= data.Length)
                        {
                            throw new AssetFormatException($"RLE row {y}: truncated transparent run");
                        }
                        n = data[p++];
                    }
                    x += n;
                }
                else if (cmd < 0xC0)
                {
                    // solid alpha/colour overlay: not palette indices, exported transparent
                    var n = cmd & 0x1F;
                    if (n == 0)
                    {
                        if (p >= data.Length)
                        {
                            throw new AssetFormatException($"RLE row {y}: truncated alpha run");
                        }
                        n = data[p++];
                    }
                    if (p + n > rowEnd)
                    {
                        throw new AssetFormatException($"RLE row {y}: alpha run past row end");
                    }
                    alphaPixels += n;
                    p += n;
                    x += n;
                }
                else
                {
                    // transparent span, low 6 bits; 0 = end of image
                    var n = cmd & 0x3F;
                    if (n == 0)
                    {
                        ended = true;
                        break;
                    }
                    x += n;
                }

                if (x > width)
                {
                    throw new AssetFormatException($"RLE row {y}: run past row width");
                }
            }

            if (!ended)
            {
                p = rowEnd;
            }
        }

        if (p == data.Length - 1 && data.Length > 0 && data[p] == 0xC0)
        {
            p += 1;
        }
        if (p != data.Length || data.Length == 0 || data[data.Length - 1] != 0xC0)
        {
            throw new AssetFormatException(
                $"RLE frame has trailing bytes (decoded {p} of {data.Length})");
        }

        return new DecodedFrame
        {
            Width = width,
            Height = height,
            Indices = indices,
            Mode = "rle",
            AlphaPixels = alphaPixels,
        };
    }
}
