using Pk2Mixer.Core;

namespace Pk2Mixer.Core.Locations;

/// <summary>
/// One side of the two-column file explorer: either a folder on disk or an
/// opened PK2 archive. Both are writable, which is what makes the copy actions
/// (archive -> folder, folder -> archive, archive -> archive) uniform.
/// </summary>
public interface IAssetLocation
{
    /// <summary>Full path shown in the pane header.</summary>
    string DisplayPath { get; }

    /// <summary>Short label, e.g. the file or folder name.</summary>
    string Title { get; }

    bool IsWritable { get; }

    IReadOnlyList<AssetItem> List();

    /// <summary>Reads the payload of one item (folders are not readable).</summary>
    byte[] Read(AssetItem item);

    /// <summary>
    /// Writes an item: a new one when <paramref name="existing"/> is null,
    /// otherwise replaces it. Folders pick a unique name when necessary.
    /// </summary>
    void Write(AssetItem? existing, string name, byte[] data);

    /// <summary>A safe file name for extracting an item (extension included).</summary>
    string SuggestName(AssetItem item);

    /// <summary>Flushes pending changes (no-op for folders).</summary>
    void Save();
}
