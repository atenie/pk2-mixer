using Pk2Mixer.Core.Assets;
using Pk2Mixer.Core.Graphics;
using Pk2Mixer.Core.Locations;

namespace Pk2Mixer.Core;

/// <summary>Copy operations between the two explorer panes.</summary>
public static class AssetOperations
{
    /// <summary>Copies the selected item unchanged into the target location.</summary>
    public static string Copy(IAssetLocation source, AssetItem item, IAssetLocation target)
    {
        if (item.IsFolder)
        {
            throw new InvalidOperationException("folders cannot be copied between these panes");
        }
        var data = source.Read(item);
        var name = source.SuggestName(item);
        var replaced = Write(target, name, data);
        target.Save();
        var action = replaced ? "Replaced" : "Copied";
        return $"{action} {name} ({data.Length:N0} bytes) in {target.Title}";
    }

    /// <summary>
    /// Copies an <c>.IMB</c> (or a <c>.BIN</c> that parses as a bank) as a PNG
    /// contact sheet, one tile per group, coloured with each group's own PALT.
    /// </summary>
    public static string CopyAsPng(IAssetLocation source, AssetItem item, IAssetLocation target,
        PaletteResolver palettes)
    {
        if (item.IsFolder)
        {
            throw new InvalidOperationException("folders cannot be exported as PNG");
        }

        var data = source.Read(item);
        if (!ImbBank.TryParse(data, out var bank, out var error) || bank is null)
        {
            throw new AssetFormatException($"{item.Name} is not an IMB image bank: {error}");
        }

        var image = ImbRenderer.RenderSheet(bank,
            group => palettes.Resolve(group, source).Colors, 12, 0);
        var png = PngWriter.Encode(image);
        var pngName = Path.GetFileNameWithoutExtension(source.SuggestName(item)) + ".png";
        var replaced = Write(target, pngName, png);
        target.Save();
        var action = replaced ? "Replaced" : "Exported";
        return $"{action} {pngName} ({image.Width}×{image.Height} px, {bank.GroupCount} group(s)) " +
               $"in {target.Title}";
    }

    /// <summary>
    /// Writes an entry, replacing an archive entry that already has the same
    /// name hash (PK2 addresses assets by hash, so duplicates would shadow
    /// each other inside the engine).
    /// </summary>
    private static bool Write(IAssetLocation target, string name, byte[] data)
    {
        if (target is ArchiveLocation archive)
        {
            var existing = archive.Archive.FindByName(name);
            if (existing is not null)
            {
                archive.Write(new AssetItem
                {
                    Name = existing.DisplayName,
                    Tag = existing.Index,
                }, name, data);
                return true;
            }
        }
        target.Write(null, name, data);
        return false;
    }

    /// <summary>Renders one IMB bank into an image (used by "Export PNG…").</summary>
    public static RgbaImage RenderBankPng(ImbBank bank, PaletteResolver palettes,
        IAssetLocation? context, bool sheet, int groupIndex)
    {
        if (sheet)
        {
            return ImbRenderer.RenderSheet(bank, group => palettes.Resolve(group, context).Colors);
        }
        if (groupIndex < 0 || groupIndex >= bank.GroupCount)
        {
            throw new ArgumentOutOfRangeException(nameof(groupIndex));
        }
        var group = bank.Groups[groupIndex];
        var frame = group.FirstFrame ?? throw new AssetFormatException("the group has no frames");
        return ImbRenderer.RenderFrame(frame, palettes.Resolve(group, context).Colors);
    }
}
