using Pk2Mixer.Core.Assets;
using Pk2Mixer.Core.Locations;

namespace Pk2Mixer.Core;

/// <summary>A palette (or palette-like DAT) available to the previewer.</summary>
public sealed class PaletteEntry
{
    public required string Name { get; init; }
    /// <summary>File the payload came from (a loose file or a PK2 archive).</summary>
    public required string SourcePath { get; init; }
    public required PaletteSource Palette { get; init; }

    public string SourceName => Path.GetFileName(SourcePath);

    public string Label => $"{Name}  —  {SourceName}";
}

/// <summary>
/// Scans an RT2 installation (or any folder) for every palette the game ships:
/// <c>PALTnnnn.PAL</c> shade ramps and <c>FLATC*.DAT</c>/<c>rgbto*.dat</c> tables
/// as loose files and inside the PK2 archives.
/// </summary>
public static class PaletteLibrary
{
    private const long MaxPaletteBytes = 512 * 1024;

    /// <summary>Loads every palette below <paramref name="folder"/> (recursive).</summary>
    public static IReadOnlyList<PaletteEntry> LoadFromFolder(string folder,
        IProgress<string>? progress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException($"folder not found: {folder}");
        }

        var entries = new List<PaletteEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in EnumerateFiles(folder, f => IsPaletteLikeFile(f)))
        {
            var name = Path.GetFileName(file);
            try
            {
                if (new FileInfo(file).Length > MaxPaletteBytes)
                {
                    continue;
                }
                var data = File.ReadAllBytes(file);
                var palette = PaletteSource.FromData(name, data);
                if (palette is not null &&
                    seen.Add($"file|{Path.GetFullPath(file)}|{name}"))
                {
                    entries.Add(new PaletteEntry { Name = name, SourcePath = file, Palette = palette });
                }
            }
            catch (IOException)
            {
                // unreadable file: skip
            }
            catch (UnauthorizedAccessException)
            {
                // unreadable file: skip
            }
        }
        progress?.Report($"{entries.Count} loose palette(s) read");

        foreach (var archivePath in EnumerateFiles(folder, IsArchiveFile))
        {
            Pk2Archive archive;
            try
            {
                archive = Pk2Archive.Load(archivePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                           or AssetFormatException)
            {
                continue;
            }

            foreach (var entry in archive.Entries)
            {
                if (entry.Name is null || entry.Size > MaxPaletteBytes || !IsPaletteLikeName(entry.Name))
                {
                    continue;
                }
                var palette = PaletteSource.FromData(entry.Name, archive.ReadEntry(entry.Index));
                if (palette is not null &&
                    seen.Add($"pk2|{Path.GetFullPath(archivePath)}|{entry.Name}"))
                {
                    entries.Add(new PaletteEntry
                    {
                        Name = entry.Name,
                        SourcePath = archivePath,
                        Palette = palette,
                    });
                }
            }
            progress?.Report($"{entries.Count} palette(s) read — {Path.GetFileName(archivePath)}");
        }

        return entries
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.SourceName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Loads a single palette file chosen by the user.</summary>
    public static PaletteEntry? LoadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var data = File.ReadAllBytes(path);
        var palette = PaletteSource.FromData(Path.GetFileName(path), data);
        return palette is null
            ? null
            : new PaletteEntry { Name = Path.GetFileName(path), SourcePath = path, Palette = palette };
    }

    public static bool IsPaletteLikeName(string name)
    {
        var extension = AssetTypes.Extension(name);
        return extension is ".pal" or ".dat";
    }

    private static bool IsPaletteLikeFile(string path) =>
        IsPaletteLikeName(Path.GetFileName(path));

    private static bool IsArchiveFile(string path)
    {
        var extension = AssetTypes.Extension(path);
        return extension is ".pk2" or ".pak";
    }

    private static IEnumerable<string> EnumerateFiles(string folder, Func<string, bool> filter)
    {
        try
        {
            return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Where(filter)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }
}
