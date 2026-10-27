using Pk2Mixer.Core;
using Pk2Mixer.Core.Assets;

namespace Pk2Mixer.Core.Locations;

/// <summary>An opened PK2 archive as an explorer location.</summary>
public sealed class ArchiveLocation : IAssetLocation
{
    private string _title;

    public ArchiveLocation(Pk2Archive archive, string filePath)
    {
        Archive = archive ?? throw new ArgumentNullException(nameof(archive));
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = Path.GetFullPath(filePath);
        _title = Path.GetFileName(FilePath);
    }

    public Pk2Archive Archive { get; }

    public string FilePath { get; private set; }

    public string DisplayPath => FilePath;

    public string Title => _title;

    public bool IsWritable => true;

    /// <summary>Total number of entries.</summary>
    public int EntryCount => Archive.Count;

    public long TotalBytes
    {
        get
        {
            long total = 0;
            foreach (var entry in Archive.Entries)
            {
                total += entry.Size;
            }
            return total;
        }
    }

    public static ArchiveLocation Open(string path) =>
        new(Pk2Archive.Load(path), path);

    public static ArchiveLocation CreateNew(string path)
    {
        var archive = Pk2Archive.CreateEmpty(path);
        if (File.Exists(path))
        {
            throw new IOException($"file already exists: {path}");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        archive.Save(path);
        return new ArchiveLocation(archive, path);
    }

    public IReadOnlyList<AssetItem> List()
    {
        var items = new List<AssetItem>(Archive.Count);
        foreach (var entry in Archive.Entries)
        {
            var name = entry.Name;
            string extension;
            AssetKind kind;
            if (!string.IsNullOrEmpty(name))
            {
                extension = AssetTypes.Extension(name);
                kind = AssetTypes.FromExtension(extension);
            }
            else
            {
                var head = Archive.GetData(entry.Index);
                kind = AssetTypes.Detect(string.Empty, head[..Math.Min(head.Length, 16)]);
                extension = kind switch
                {
                    AssetKind.Imb => ".imb",
                    AssetKind.Png => ".png",
                    _ => ".bin",
                };
                name = $"entry_{entry.Index:D4}";
            }

            items.Add(new AssetItem
            {
                Name = name!,
                Extension = extension,
                Size = entry.Size,
                Kind = kind,
                Tag = entry.Index,
            });
        }
        return items;
    }

    public byte[] Read(AssetItem item)
    {
        if (item.Tag is not int index)
        {
            throw new ArgumentException("item does not belong to this archive", nameof(item));
        }
        return Archive.ReadEntry(index);
    }

    public void Write(AssetItem? existing, string name, byte[] data)
    {
        if (existing?.Tag is int index)
        {
            Archive.Replace(index, data, name);
        }
        else
        {
            Archive.Add(data, name);
        }
    }

    public string SuggestName(AssetItem item)
    {
        var extension = item.Extension;
        var name = item.Name;
        if (string.IsNullOrEmpty(extension) && item.Kind != AssetKind.Folder)
        {
            extension = item.Kind switch
            {
                AssetKind.Imb => ".imb",
                AssetKind.Lng => ".lng",
                AssetKind.Dat => ".dat",
                AssetKind.Palette => ".pal",
                AssetKind.Png => ".png",
                _ => ".bin",
            };
        }
        if (!string.IsNullOrEmpty(extension) &&
            !name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            name += extension;
        }
        return FolderLocation.SanitizeFileName(name);
    }

    public void Save()
    {
        Archive.Save(FilePath);
    }

    /// <summary>Saves under a new path (used by "save a copy" style actions).</summary>
    public void SaveAs(string path)
    {
        Archive.Save(path);
        FilePath = path;
        _title = Path.GetFileName(path);
    }

    public bool IsDirty => Archive.IsDirty;

    /// <summary>Looks up a palette entry inside this archive by its documented name.</summary>
    public PaltPalette? FindPalt(int index)
    {
        var name = $"PALT{index:D4}.PAL";
        var entry = Archive.FindByName(name);
        if (entry is null)
        {
            return null;
        }
        return PaltPalette.TryParse(Archive.ReadEntry(entry.Index), out var palette) ? palette : null;
    }

    /// <summary>Reads any palette-like entry by name (used by the palette library).</summary>
    public PaletteSource? ReadPalette(string name)
    {
        var entry = Archive.FindByName(name);
        return entry is null
            ? null
            : PaletteSource.FromData(name, Archive.ReadEntry(entry.Index));
    }
}
