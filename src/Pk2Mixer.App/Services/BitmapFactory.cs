using Avalonia.Media.Imaging;
using Pk2Mixer.Core.Assets;
using Pk2Mixer.Core.Graphics;

namespace Pk2Mixer.App.Services;

/// <summary>Converts the headless core's RGBA images into Avalonia bitmaps.</summary>
public static class BitmapFactory
{
    public static Bitmap Create(RgbaImage image)
    {
        using var stream = new MemoryStream(PngWriter.Encode(image));
        return new Bitmap(stream);
    }
}
