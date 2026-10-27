namespace Pk2Mixer.Core.Graphics;

/// <summary>One entry of an RT2 palette (8 bits per channel).</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    /// <summary>Expands an RGB555 word (red 14..10, green 9..5, blue 4..0).</summary>
    public static Rgb FromRgb555(ushort value)
    {
        var r = (value >> 10) & 31;
        var g = (value >> 5) & 31;
        var b = value & 31;
        return new Rgb((byte)(r * 255 / 31), (byte)(g * 255 / 31), (byte)(b * 255 / 31));
    }

    public Rgba WithAlpha(byte alpha = 255) => new(R, G, B, alpha);
}

/// <summary>A straight (non-premultiplied) RGBA pixel.</summary>
public readonly record struct Rgba(byte R, byte G, byte B, byte A)
{
    public static readonly Rgba Transparent = new(0, 0, 0, 0);
    public static Rgba FromRgb(Rgb rgb, byte alpha = 255) => new(rgb.R, rgb.G, rgb.B, alpha);
}

/// <summary>
/// A simple 8-bit RGBA image. The asset renderers and the PNG writer are both
/// UI-framework independent, so previews are testable on a headless machine.
/// </summary>
public sealed class RgbaImage
{
    public RgbaImage(int width, int height)
    {
        if (width < 0 || height < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "image dimensions must not be negative");
        }
        Width = width;
        Height = height;
        Pixels = new byte[checked(width * height * 4)];
    }

    public int Width { get; }
    public int Height { get; }
    /// <summary>Row-major RGBA bytes, <c>Width * Height * 4</c> long.</summary>
    public byte[] Pixels { get; }

    public static RgbaImage Filled(int width, int height, Rgba color)
    {
        var image = new RgbaImage(width, height);
        for (var i = 0; i < image.Pixels.Length; i += 4)
        {
            image.Pixels[i] = color.R;
            image.Pixels[i + 1] = color.G;
            image.Pixels[i + 2] = color.B;
            image.Pixels[i + 3] = color.A;
        }
        return image;
    }

    public void Set(int x, int y, Rgba color)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            return;
        }
        var p = (y * Width + x) * 4;
        Pixels[p] = color.R;
        Pixels[p + 1] = color.G;
        Pixels[p + 2] = color.B;
        Pixels[p + 3] = color.A;
    }

    public Rgba Get(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            return Rgba.Transparent;
        }
        var p = (y * Width + x) * 4;
        return new Rgba(Pixels[p], Pixels[p + 1], Pixels[p + 2], Pixels[p + 3]);
    }

    /// <summary>Copy another image onto this one, skipping fully transparent pixels.</summary>
    public void Blit(RgbaImage source, int originX, int originY)
    {
        for (var y = 0; y < source.Height; y++)
        {
            var ty = originY + y;
            if ((uint)ty >= (uint)Height)
            {
                continue;
            }
            for (var x = 0; x < source.Width; x++)
            {
                var tx = originX + x;
                if ((uint)tx >= (uint)Width)
                {
                    continue;
                }
                var s = (y * source.Width + x) * 4;
                if (source.Pixels[s + 3] == 0)
                {
                    continue;
                }
                var t = (ty * Width + tx) * 4;
                Pixels[t] = source.Pixels[s];
                Pixels[t + 1] = source.Pixels[s + 1];
                Pixels[t + 2] = source.Pixels[s + 2];
                Pixels[t + 3] = source.Pixels[s + 3];
            }
        }
    }

    /// <summary>Nearest-neighbour scale down/up so the result fits a preview budget.</summary>
    public RgbaImage ScaleToFit(int maxWidth, int maxHeight)
    {
        if (maxWidth <= 0 || maxHeight <= 0 || (Width <= maxWidth && Height <= maxHeight))
        {
            return this;
        }
        var scale = Math.Min((double)maxWidth / Width, (double)maxHeight / Height);
        var w = Math.Max(1, (int)(Width * scale));
        var h = Math.Max(1, (int)(Height * scale));
        var target = new RgbaImage(w, h);
        for (var y = 0; y < h; y++)
        {
            var sy = Math.Min(Height - 1, y * Height / h);
            for (var x = 0; x < w; x++)
            {
                var sx = Math.Min(Width - 1, x * Width / w);
                target.Set(x, y, Get(sx, sy));
            }
        }
        return target;
    }
}
