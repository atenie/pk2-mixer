using Pk2Mixer.Core.Assets;

namespace Pk2Mixer.Core;

/// <summary>File type as shown in the explorer's "filetype" column.</summary>
public enum AssetKind
{
    Folder,
    Imb,
    Bin,
    Lng,
    Dat,
    Palette,
    Png,
    Raw,
    Win,
    Other,
}

public static class AssetTypes
{
    public static string Extension(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }
        var dot = name.LastIndexOf('.');
        if (dot <= 0 || dot == name.Length - 1)
        {
            return string.Empty;
        }
        return name[dot..].ToLowerInvariant();
    }

    public static AssetKind FromExtension(string extension) => extension switch
    {
        ".imb" => AssetKind.Imb,
        ".bin" => AssetKind.Bin,
        ".lng" => AssetKind.Lng,
        ".dat" => AssetKind.Dat,
        ".pal" => AssetKind.Palette,
        ".png" => AssetKind.Png,
        ".raw" => AssetKind.Raw,
        ".win" => AssetKind.Win,
        _ => AssetKind.Other,
    };

    /// <summary>Type from the extension, with a structural sniff for unnamed PK2 entries.</summary>
    public static AssetKind Detect(string name, ReadOnlySpan<byte> head)
    {
        var kind = FromExtension(Extension(name));
        if (kind != AssetKind.Other)
        {
            return kind;
        }
        if (ImbBank.LooksLikeImb(head))
        {
            return AssetKind.Imb;
        }
        if (head.Length >= 8 && head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47)
        {
            return AssetKind.Png;
        }
        return AssetKind.Other;
    }

    /// <summary>Short label for the explorer: IMB, LNG, DAT, PAL, BIN, ...</summary>
    public static string Label(AssetKind kind) => kind switch
    {
        AssetKind.Folder => "folder",
        AssetKind.Imb => "IMB",
        AssetKind.Bin => "BIN",
        AssetKind.Lng => "LNG",
        AssetKind.Dat => "DAT",
        AssetKind.Palette => "PAL",
        AssetKind.Png => "PNG",
        AssetKind.Raw => "RAW",
        AssetKind.Win => "WIN",
        _ => "file",
    };

    public static string Label(string name)
    {
        var extension = Extension(name);
        return extension.Length > 1 ? extension[1..].ToUpperInvariant() : "file";
    }

    public static bool IsImageBank(AssetKind kind) =>
        kind is AssetKind.Imb or AssetKind.Bin;
}

/// <summary>One row in an explorer pane.</summary>
public sealed class AssetItem
{
    public required string Name { get; init; }
    /// <summary>Lower-case extension including the dot, or empty.</summary>
    public string Extension { get; init; } = string.Empty;
    /// <summary>Size in bytes (0 for folders).</summary>
    public long Size { get; init; }
    public AssetKind Kind { get; init; } = AssetKind.Other;
    public bool IsFolder { get; init; }
    /// <summary>Location specific handle: PK2 entry index or full file path.</summary>
    public object? Tag { get; init; }

    public string TypeLabel => AssetTypes.Label(Kind);
}
