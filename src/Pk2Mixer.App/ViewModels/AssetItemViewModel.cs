using Pk2Mixer.Core;

namespace Pk2Mixer.App.ViewModels;

/// <summary>One row of the explorer's name / filetype / size table.</summary>
public sealed class AssetItemViewModel
{
    public AssetItemViewModel(AssetItem item) => Item = item;

    public AssetItem Item { get; }

    public string Name => Item.Name;

    /// <summary>File type column, matching the research vocabulary (IMB, LNG, DAT, ...).</summary>
    public string TypeLabel => Item.TypeLabel;

    /// <summary>Size in bytes, as required by the column header.</summary>
    public string SizeText => Item.IsFolder ? string.Empty : Item.Size.ToString("N0");

    public long Size => Item.Size;

    public bool IsFolder => Item.IsFolder;

    public AssetKind Kind => Item.Kind;

    public bool IsImageBank => AssetTypes.IsImageBank(Item.Kind);
}
