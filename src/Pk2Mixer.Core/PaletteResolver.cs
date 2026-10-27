using Pk2Mixer.Core.Assets;
using Pk2Mixer.Core.Locations;

namespace Pk2Mixer.Core;

/// <summary>
/// Finds the palette a group draws with (spec 08): the group's own
/// <c>PALTnnnn.PAL</c> from the same archive, a loose file next to the asset,
/// any other open archive, the loaded palette library, then the palette chosen
/// in View ▸ Palette ▸ Select.
/// </summary>
public sealed class PaletteResolver
{
    private readonly Func<IEnumerable<ArchiveLocation>> _openArchives;
    private readonly Func<IEnumerable<PaletteEntry>> _library;
    private readonly Func<PaletteEntry?> _selected;

    public PaletteResolver(Func<IEnumerable<ArchiveLocation>> openArchives,
        Func<IEnumerable<PaletteEntry>> library,
        Func<PaletteEntry?> selected)
    {
        _openArchives = openArchives;
        _library = library;
        _selected = selected;
    }

    public GroupPalette Resolve(ImbGroup group, IAssetLocation? context)
    {
        var paletteName = group.PaletteName;

        // An explicitly chosen palette is an override: the user picked it to see
        // the assets under that palette, so it wins over the per-group PALT.
        var selected = _selected();
        var selectedColors = selected?.Palette.GetColors(PaletteSource.DefaultShade);
        if (selectedColors is not null)
        {
            return new GroupPalette(selectedColors, $"selected {selected!.Name}");
        }

        if (context is ArchiveLocation archive)
        {
            var palt = archive.FindPalt((int)group.PaletteIndex);
            if (palt is not null)
            {
                return new GroupPalette(palt.GetColors(PaletteSource.DefaultShade),
                    $"{paletteName} (archive)");
            }
        }
        else if (context is FolderLocation folder)
        {
            var path = Path.Combine(folder.DirectoryPath, paletteName);
            if (File.Exists(path))
            {
                try
                {
                    if (PaltPalette.TryParse(File.ReadAllBytes(path), out var palt))
                    {
                        return new GroupPalette(palt!.GetColors(PaletteSource.DefaultShade),
                            $"{paletteName} (folder)");
                    }
                }
                catch (IOException)
                {
                    // fall through to the other sources
                }
            }
        }

        foreach (var other in _openArchives())
        {
            if (ReferenceEquals(other, context))
            {
                continue;
            }
            var palt = other.FindPalt((int)group.PaletteIndex);
            if (palt is not null)
            {
                return new GroupPalette(palt.GetColors(PaletteSource.DefaultShade),
                    $"{paletteName} (from {other.Title})");
            }
        }

        var libraryMatch = _library().FirstOrDefault(e =>
            e.Name.Equals(paletteName, StringComparison.OrdinalIgnoreCase));
        if (libraryMatch?.Palette is PaltPalette fromLibrary)
        {
            return new GroupPalette(fromLibrary.GetColors(PaletteSource.DefaultShade),
                $"{paletteName} (library)");
        }

        return new GroupPalette(ImbRenderer.Grayscale, "grayscale fallback");
    }
}
