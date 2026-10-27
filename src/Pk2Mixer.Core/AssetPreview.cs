using Pk2Mixer.Core.Assets;
using Pk2Mixer.Core.Graphics;

namespace Pk2Mixer.Core;

public enum PreviewKind
{
    Empty,
    Folder,
    Image,
    Lng,
    Text,
    Error,
}

/// <summary>A palette plus the label shown in the preview status line.</summary>
public readonly record struct GroupPalette(Rgb[] Colors, string Label);

/// <summary>Everything the preview pane needs to render one content kind.</summary>
public sealed class PreviewContent
{
    public PreviewKind Kind { get; init; } = PreviewKind.Empty;
    public string Title { get; init; } = string.Empty;
    public string Details { get; init; } = string.Empty;
    public RgbaImage? Image { get; init; }
    public string? Text { get; init; }
    public IReadOnlyList<LngEntry>? Lines { get; init; }
    public string? Error { get; init; }

    public static PreviewContent Empty(string message = "Select an .IMB, .BIN, .DAT or .LNG asset.") =>
        new() { Title = "Preview", Details = message };

    public static PreviewContent Failure(string title, string error) =>
        new() { Kind = PreviewKind.Error, Title = title, Error = error };

    public static PreviewContent Folder(string path) =>
        new() { Kind = PreviewKind.Folder, Title = path, Details = "Double-click the folder row to enter it." };

    public static PreviewContent FromImage(string title, string details, RgbaImage image) =>
        new() { Kind = PreviewKind.Image, Title = title, Details = details, Image = image };

    public static PreviewContent FromText(string title, string details, string text) =>
        new() { Kind = PreviewKind.Text, Title = title, Details = details, Text = text };
}

/// <summary>The preview content for one asset, plus the bank when it is an IMB.</summary>
public sealed class AssetPreviewResult
{
    public required PreviewContent Content { get; init; }
    public ImbBank? Bank { get; init; }
}

/// <summary>
/// Builds preview content for the asset types documented in the research folder:
/// <c>.IMB</c> image banks, <c>.BIN</c> (often an unnamed image bank),
/// <c>.DAT</c>/<c>.PAL</c> palettes and ramps, and <c>.LNG</c> localization files.
/// </summary>
public static class AssetPreview
{
    /// <summary>
    /// Classifies a payload and returns the matching preview. Palette-like and
    /// localization detection is deliberately ordered: a <c>.DAT</c>/<c>.PAL</c>
    /// always gets the palette renderer, even if its bytes happen to look like
    /// text, and only other types are sniffed for <c>.lng</c> syntax.
    /// </summary>
    public static AssetPreviewResult Build(string name, AssetKind kind, byte[] data,
        Func<ImbGroup, GroupPalette>? paletteForGroup = null, int groupIndex = 0, bool sheet = false)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (AssetTypes.IsImageBank(kind) &&
            ImbBank.TryParse(data, out var bank, out _) && bank is not null)
        {
            var resolver = paletteForGroup ??
                           (_ => new GroupPalette(ImbRenderer.Grayscale, "grayscale fallback"));
            return new AssetPreviewResult
            {
                Bank = bank,
                Content = RenderImb(bank, groupIndex, sheet, name, resolver),
            };
        }

        if (kind is AssetKind.Dat or AssetKind.Palette)
        {
            var palette = PaletteSource.FromData(name, data);
            if (palette is not null)
            {
                return new AssetPreviewResult
                {
                    Content = RenderPalette(name, palette, PaletteSource.DefaultShade, data.Length),
                };
            }
        }

        if (kind == AssetKind.Lng || LngFile.LooksLike(data))
        {
            return new AssetPreviewResult
            {
                Content = RenderLng(name, LngFile.Parse(data), data.Length),
            };
        }

        return new AssetPreviewResult { Content = RenderBinary(name, data) };
    }

    public static PreviewContent RenderImb(ImbBank bank, int groupIndex, bool sheet, string title,
        Func<ImbGroup, GroupPalette> resolvePalette, int shade = PaletteSource.DefaultShade)
    {
        ArgumentNullException.ThrowIfNull(bank);
        ArgumentNullException.ThrowIfNull(resolvePalette);
        if (bank.GroupCount == 0)
        {
            return PreviewContent.Failure(title, "the bank has no groups");
        }

        if (sheet)
        {
            try
            {
                var sheetImage = ImbRenderer.RenderSheet(bank, g => resolvePalette(g).Colors, 12, 2400);
                return PreviewContent.FromImage(title,
                    $"{bank.GroupCount} group(s), {bank.FrameCount} frame(s) — contact sheet of each group's first frame",
                    sheetImage);
            }
            catch (AssetFormatException ex)
            {
                return PreviewContent.Failure(title, "cannot render the contact sheet: " + ex.Message);
            }
        }

        var index = Math.Clamp(groupIndex, 0, bank.GroupCount - 1);
        var group = bank.Groups[index];
        var frame = group.FirstFrame;
        if (frame is null)
        {
            return PreviewContent.Failure(title, $"group {index} contains no frames");
        }

        var palette = resolvePalette(group);
        RgbaImage image;
        try
        {
            image = ImbRenderer.RenderFrame(frame, palette.Colors);
        }
        catch (AssetFormatException ex)
        {
            return PreviewContent.Failure(title, $"cannot decode group {index}: {ex.Message}");
        }

        var levels = 0;
        foreach (var f in group.Frames)
        {
            levels = Math.Max(levels, f.Level + 1);
        }
        var paletteInfo = group.NeedsPalette ? $"PALT{group.PaletteIndex:D4}" : "no PALT flag";
        var details =
            $"group {index}/{bank.GroupCount - 1} · {frame.Width}×{frame.Height} · " +
            $"{(frame.IsRaw ? "raw" : "row RLE")} · {levels} mip level(s) · " +
            $"{paletteInfo} → {palette.Label} · shade {shade}";
        return PreviewContent.FromImage(title, details, image);
    }

    public static PreviewContent RenderPalette(string title, PaletteSource palette, int shade,
        int byteSize)
    {
        var image = palette.Render(shade);
        return PreviewContent.FromImage(title,
            $"{palette.Kind} · {palette.Describe(shade)} · {byteSize:N0} bytes", image);
    }

    public static PreviewContent RenderLng(string title, LngFile lng, int byteSize) =>
        new()
        {
            Kind = PreviewKind.Lng,
            Title = title,
            Details = $"LNG localization file · {lng.Count:N0} string(s) · {byteSize:N0} bytes",
            Lines = lng.Entries,
        };

    public static PreviewContent RenderBinary(string title, byte[] data, int maxBytes = 8192) =>
        PreviewContent.FromText(title, $"{data.Length:N0} bytes · hex dump", HexDump.Format(data, maxBytes));
}
