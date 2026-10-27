namespace Pk2Mixer.Core;

/// <summary>Human-friendly shortening of long installation paths.</summary>
public static class ShortPath
{
    private const int MaxLength = 56;

    /// <summary>
    /// Renders <paramref name="fullPath"/> relative to an installation root when
    /// possible, eliding middle directories so the result stays menu-sized.
    /// </summary>
    public static string Format(string fullPath, string? installRoot = null)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return string.Empty;
        }

        var separator = Path.DirectorySeparatorChar;
        if (!string.IsNullOrWhiteSpace(installRoot))
        {
            var root = installRoot!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                var relative = Path.GetRelativePath(root, fullPath);
                if (!relative.StartsWith("..", StringComparison.Ordinal))
                {
                    var rootName = Path.GetFileName(root);
                    if (string.IsNullOrEmpty(rootName))
                    {
                        rootName = root;
                    }
                    var label = $"{rootName}{separator}{relative}";
                    if (label.Length <= MaxLength)
                    {
                        return label;
                    }
                    var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (parts.Length > 2)
                    {
                        return $"{rootName}{separator}…{separator}{parts[^2]}{separator}{parts[^1]}";
                    }
                    return label;
                }
            }
        }

        if (fullPath.Length <= MaxLength)
        {
            return fullPath;
        }

        // Keep the volume/first directory and the last two segments.
        var all = fullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (all.Length <= 3)
        {
            return fullPath;
        }
        var head = all[0].Length == 0 ? separator.ToString() : all[0] + separator;
        return $"{head}…{separator}{all[^2]}{separator}{all[^1]}";
    }
}
