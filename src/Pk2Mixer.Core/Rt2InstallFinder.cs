namespace Pk2Mixer.Core;

/// <summary>A <c>.PK2</c>/<c>.PAK</c> archive discovered in a Railroad Tycoon II installation.</summary>
public sealed class FoundArchive
{
    public required string Path { get; init; }
    public required string InstallRoot { get; init; }

    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>Shortened address for the File ▸ Found menu.</summary>
    public string Label => ShortPath.Format(Path, InstallRoot);

    public bool Exists => File.Exists(Path);
}

/// <summary>
/// Locates Railroad Tycoon II installations and their PK2 archives: environment
/// variables (<c>RT2_GAME_DIR</c>, <c>RT2_MAP</c>), the usual Steam/GOG/retail
/// folders on Windows, Linux and macOS, plus Steam library folders.
/// </summary>
public static class Rt2InstallFinder
{
    /// <summary>Roots that are worth searching, existing ones only.</summary>
    public static IReadOnlyList<string> CandidateRoots()
    {
        var roots = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }
            string full;
            try
            {
                full = Path.GetFullPath(path);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException
                                           or PathTooLongException)
            {
                return;
            }
            if (seen.Add(full))
            {
                roots.Add(full);
            }
        }

        Add(Environment.GetEnvironmentVariable("RT2_GAME_DIR"));
        var map = Environment.GetEnvironmentVariable("RT2_MAP");
        if (!string.IsNullOrWhiteSpace(map))
        {
            Add(Path.GetDirectoryName(map));
            Add(Path.GetDirectoryName(Path.GetDirectoryName(map)));
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        if (OperatingSystem.IsWindows())
        {
            foreach (var programRoot in new[] { programFilesX86, programFiles })
            {
                if (string.IsNullOrEmpty(programRoot))
                {
                    continue;
                }
                Add(Path.Combine(programRoot, "Take-Two Interactive"));
                Add(Path.Combine(programRoot, "GOG Galaxy", "Games"));
                Add(Path.Combine(programRoot, "Steam", "steamapps", "common"));
            }
            Add(@"C:\GOG Games");
            Add(@"C:\Games");
            Add(Path.Combine(home, "GOG Games"));

            foreach (var drive in SafeDrives())
            {
                Add(Path.Combine(drive, "GOG Games"));
                Add(Path.Combine(drive, "SteamLibrary", "steamapps", "common"));
                Add(Path.Combine(drive, "Program Files (x86)", "Steam", "steamapps", "common"));
                Add(Path.Combine(drive, "Program Files (x86)", "GOG Galaxy", "Games"));
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            Add(Path.Combine(home, "Library", "Application Support", "Steam", "steamapps", "common"));
            Add(Path.Combine(home, "Applications"));
            Add("/Applications");
        }
        else
        {
            Add(Path.Combine(home, ".steam", "steam", "steamapps", "common"));
            Add(Path.Combine(home, ".local", "share", "Steam", "steamapps", "common"));
            Add(Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share",
                "Steam", "steamapps", "common"));
            Add(Path.Combine(home, "GOG Games"));
            Add(Path.Combine(home, "Games"));
            Add(Path.Combine(home, ".wine", "drive_c", "GOG Games"));
            Add(Path.Combine(home, ".wine", "drive_c", "Program Files (x86)", "Take-Two Interactive"));
        }

        // Extra Steam libraries listed in libraryfolders.vdf.
        foreach (var common in SteamCommonFolders(roots))
        {
            Add(common);
        }

        return roots.Where(Directory.Exists).ToList();
    }

    /// <summary>Searches every candidate root and returns deduplicated archives.</summary>
    public static IReadOnlyList<FoundArchive> FindAll(IProgress<string>? progress = null)
    {
        var results = new List<FoundArchive>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in CandidateRoots())
        {
            progress?.Report($"Searching {root}");
            foreach (var archive in FindIn(root, 5, progress))
            {
                if (seen.Add(archive.Path))
                {
                    results.Add(archive);
                }
            }
        }

        return results
            .OrderBy(a => a.InstallRoot, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Recursively finds PK2/PAK archives below one folder.</summary>
    public static IReadOnlyList<FoundArchive> FindIn(string root, int maxDepth = 6,
        IProgress<string>? progress = null)
    {
        var results = new List<FoundArchive>();
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return results;
        }

        var pending = new Queue<(string Directory, int Depth)>();
        pending.Enqueue((Path.GetFullPath(root), 0));
        while (pending.Count > 0)
        {
            var (directory, depth) = pending.Dequeue();

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                continue;
            }

            foreach (var file in files)
            {
                var extension = AssetTypes.Extension(file);
                if (extension is not (".pk2" or ".pak"))
                {
                    continue;
                }
                results.Add(new FoundArchive
                {
                    Path = Path.GetFullPath(file),
                    InstallRoot = InferInstallRoot(file),
                });
            }

            if (depth >= maxDepth)
            {
                continue;
            }

            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(directory);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                continue;
            }

            foreach (var child in children)
            {
                var name = Path.GetFileName(child);
                if (name.StartsWith('.') || name.Equals("node_modules", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                pending.Enqueue((child, depth + 1));
            }
        }

        progress?.Report($"{results.Count} archive(s) under {root}");
        return results;
    }

    /// <summary>Walks up from an archive to the folder that looks like the game root.</summary>
    public static string InferInstallRoot(string archivePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(archivePath));
        var current = directory;
        for (var i = 0; i < 4 && current is not null; i++)
        {
            if (File.Exists(Path.Combine(current, "RT2_PLAT.EXE")) ||
                File.Exists(Path.Combine(current, "rt2.exe")) ||
                Directory.Exists(Path.Combine(current, "maps")))
            {
                return current;
            }
            current = Path.GetDirectoryName(current);
        }
        return directory ?? archivePath;
    }

    private static IEnumerable<string> SafeDrives()
    {
        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (IOException)
        {
            return [];
        }
        return drives
            .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
            .Select(d => d.RootDirectory.FullName);
    }

    private static IEnumerable<string> SteamCommonFolders(IEnumerable<string> roots)
    {
        var folders = new List<string>();
        foreach (var root in roots.ToList())
        {
            // candidates are usually <steam>/steamapps/common, so look next to them
            var candidates = new[]
            {
                Path.Combine(root, "steamapps", "libraryfolders.vdf"),
                Path.Combine(root, "libraryfolders.vdf"),
                Path.Combine(root, "..", "libraryfolders.vdf"),
                Path.Combine(root, "..", "..", "libraryfolders.vdf"),
            };

            foreach (var vdf in candidates)
            {
                if (!File.Exists(vdf))
                {
                    continue;
                }
                string[] lines;
                try
                {
                    lines = File.ReadAllLines(vdf);
                }
                catch (IOException)
                {
                    continue;
                }
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (!trimmed.StartsWith("\"path\"", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    var first = trimmed.IndexOf('"', 1);
                    var last = first >= 0 ? trimmed.IndexOf('"', first + 1) : -1;
                    if (first > 0 && last > first)
                    {
                        var library = trimmed[(first + 1)..last].Replace("\\\\", "\\");
                        folders.Add(Path.Combine(library, "steamapps", "common"));
                    }
                }
                break;
            }
        }
        return folders;
    }
}
