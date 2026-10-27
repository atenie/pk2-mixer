namespace Pk2Mixer.Core.Locations;

/// <summary>A directory on disk as an explorer location.</summary>
public sealed class FolderLocation : IAssetLocation
{
    public FolderLocation(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        DirectoryPath = Path.GetFullPath(directoryPath);
        if (!Directory.Exists(DirectoryPath))
        {
            throw new DirectoryNotFoundException($"folder not found: {DirectoryPath}");
        }
    }

    public string DirectoryPath { get; }

    public string DisplayPath => DirectoryPath;

    public string Title
    {
        get
        {
            var trimmed = DirectoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var name = Path.GetFileName(trimmed);
            return string.IsNullOrEmpty(name) ? trimmed : name + Path.DirectorySeparatorChar;
        }
    }

    public bool IsWritable => true;

    public bool CanGoUp => Directory.GetParent(DirectoryPath) is not null;

    public FolderLocation? Parent
    {
        get
        {
            var parent = Directory.GetParent(DirectoryPath);
            return parent is null ? null : new FolderLocation(parent.FullName);
        }
    }

    public FolderLocation Enter(string subdirectory) =>
        new(Path.Combine(DirectoryPath, subdirectory));

    public IReadOnlyList<AssetItem> List()
    {
        var items = new List<AssetItem>();

        IEnumerable<string> directories;
        IEnumerable<string> files;
        try
        {
            directories = Directory.EnumerateDirectories(DirectoryPath);
            files = Directory.EnumerateFiles(DirectoryPath);
        }
        catch (UnauthorizedAccessException)
        {
            return items;
        }

        foreach (var directory in directories.OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
        {
            items.Add(new AssetItem
            {
                Name = Path.GetFileName(directory) + Path.DirectorySeparatorChar,
                IsFolder = true,
                Kind = AssetKind.Folder,
                Tag = directory,
            });
        }

        foreach (var file in files.OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(file);
            var extension = AssetTypes.Extension(name);
            long size = 0;
            try
            {
                size = new FileInfo(file).Length;
            }
            catch (IOException)
            {
                // keep 0; the file may have vanished between enumeration and stat
            }
            items.Add(new AssetItem
            {
                Name = name,
                Extension = extension,
                Size = size,
                Kind = AssetTypes.FromExtension(extension),
                Tag = file,
            });
        }
        return items;
    }

    public byte[] Read(AssetItem item)
    {
        if (item.Tag is not string path)
        {
            throw new ArgumentException("item does not belong to this folder", nameof(item));
        }
        return File.ReadAllBytes(path);
    }

    public void Write(AssetItem? existing, string name, byte[] data)
    {
        string path;
        if (existing?.Tag is string existingPath)
        {
            path = existingPath;
        }
        else
        {
            path = UniquePath(DirectoryPath, SanitizeFileName(name));
        }
        File.WriteAllBytes(path, data);
    }

    public string SuggestName(AssetItem item) =>
        item.IsFolder ? Path.GetFileName((string)item.Tag!) : item.Name;

    public void Save()
    {
        // folders are written immediately
    }

    public static string UniquePath(string directory, string fileName)
    {
        var candidate = Path.Combine(directory, fileName);
        if (!File.Exists(candidate))
        {
            return candidate;
        }
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var i = 2; i < 100_000; i++)
        {
            candidate = Path.Combine(directory, $"{stem} ({i}){extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
        throw new IOException($"cannot find a free file name for '{fileName}'");
    }

    /// <summary>Replaces path separators and characters Windows rejects.</summary>
    public static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray();
        var clean = new string(chars).Trim();
        if (clean.Length == 0)
        {
            clean = "asset";
        }
        return clean.Length > 200 ? clean[..200] : clean;
    }
}
