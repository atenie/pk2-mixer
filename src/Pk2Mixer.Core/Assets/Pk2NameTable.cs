namespace Pk2Mixer.Core.Assets;
/// <summary>
/// Hash -> file name catalogue for PK2 entries. PK2 archives store no names, so
/// the names recovered by reverse engineering
/// (<c>../railroad-tycoon-2-decomp/rt2/spec/07-asset-names.md</c>) are embedded
/// as a resource and looked up by hash.
/// </summary>
public sealed class Pk2NameTable
{
    private readonly Dictionary<uint, string> _names;

    private Pk2NameTable(Dictionary<uint, string> names) => _names = names;

    public int Count => _names.Count;

    public static Pk2NameTable Empty { get; } = new(new Dictionary<uint, string>());

    /// <summary>The catalogue shipped with the app, loaded once on first use.</summary>
    public static Pk2NameTable Default { get; } = LoadEmbedded();

    public bool TryGetName(uint hash, out string name) => _names.TryGetValue(hash, out name!);

    public string? GetName(uint hash) => _names.TryGetValue(hash, out var name) ? name : null;

    /// <summary>Parses the embedded "hashhex name" catalogue format.</summary>
    public static Pk2NameTable Parse(IEnumerable<string> lines)
    {
        var names = new Dictionary<uint, string>();
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] == '#')
            {
                continue;
            }
            var space = trimmed.IndexOf(' ');
            if (space != 8 ||
                !uint.TryParse(trimmed.AsSpan(0, 8), System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out var hash))
            {
                continue;
            }
            names[hash] = trimmed[(space + 1)..].Trim();
        }
        return new Pk2NameTable(names);
    }

    private static Pk2NameTable LoadEmbedded()
    {
        try
        {
            var assembly = typeof(Pk2NameTable).Assembly;
            var resource = Array.Find(assembly.GetManifestResourceNames(),
                n => n.EndsWith("asset-names.txt", StringComparison.OrdinalIgnoreCase));
            if (resource is null)
            {
                return Empty;
            }
            using var stream = assembly.GetManifestResourceStream(resource);
            if (stream is null)
            {
                return Empty;
            }
            using var reader = new StreamReader(stream);
            var lines = new List<string>();
            while (reader.ReadLine() is { } line)
            {
                lines.Add(line);
            }
            return Parse(lines);
        }
        catch (IOException)
        {
            return Empty;
        }
        catch (InvalidOperationException)
        {
            return Empty;
        }
    }
}
