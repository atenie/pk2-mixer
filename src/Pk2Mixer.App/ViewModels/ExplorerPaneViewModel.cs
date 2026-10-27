using System.Collections.ObjectModel;
using Pk2Mixer.Core;
using Pk2Mixer.Core.Locations;

namespace Pk2Mixer.App.ViewModels;

/// <summary>One side of the two-column explorer: a folder, an archive, or nothing.</summary>
public sealed class ExplorerPaneViewModel : ObservableObject
{
    private IAssetLocation? _location;
    private AssetItemViewModel? _selectedItem;
    private bool _isActive;

    public ExplorerPaneViewModel(string side)
    {
        Side = side;
    }

    public string Side { get; }

    public event Action<ExplorerPaneViewModel>? Activated;
    public event Action<AssetItemViewModel?>? SelectionChanged;

    public IAssetLocation? Location => _location;

    public bool HasLocation => _location is not null;

    public bool IsActive
    {
        get => _isActive;
        set => Set(ref _isActive, value);
    }

    public string Title => _location?.Title ?? "(no location)";

    public string PathLabel => _location?.DisplayPath ??
                               "Open a .PK2 archive or a folder using the buttons above.";

    public string RoleLabel => Side == "Left" ? "left pane" : "right pane";

    public string CopyLabel => Side == "Left" ? "Copy →" : "← Copy";

    public string CopyPngLabel => Side == "Left" ? "PNG →" : "← PNG";

    public bool IsArchive => _location is ArchiveLocation;

    public bool IsFolder => _location is FolderLocation;

    public bool CanGoUp => _location is FolderLocation { CanGoUp: true };

    public bool IsDirty => _location is ArchiveLocation { IsDirty: true };

    public string Summary
    {
        get
        {
            if (_location is ArchiveLocation archive)
            {
                var dirty = archive.IsDirty ? " · unsaved changes" : string.Empty;
                return $"{archive.EntryCount:N0} entries · {archive.TotalBytes:N0} bytes{dirty}";
            }
            if (_location is FolderLocation)
            {
                return $"{Items.Count:N0} item(s)";
            }
            return "empty";
        }
    }

    public ObservableCollection<AssetItemViewModel> Items { get; } = [];

    public bool HasSelection => _selectedItem is not null;

    public AssetItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (Set(ref _selectedItem, value))
            {
                Raise(nameof(HasSelection));
                SelectionChanged?.Invoke(value);
            }
        }
    }

    public void Touch() => Activated?.Invoke(this);

    public void SetLocation(IAssetLocation? location, string? selectName = null)
    {
        _location = location;
        SelectedItem = null;
        Refresh(selectName);
        Raise(nameof(Location));
        Raise(nameof(HasLocation));
        Raise(nameof(Title));
        Raise(nameof(PathLabel));
        Raise(nameof(IsArchive));
        Raise(nameof(IsFolder));
        Raise(nameof(CanGoUp));
        Raise(nameof(IsDirty));
        Raise(nameof(Summary));
    }

    public void Refresh(string? selectName = null)
    {
        var previous = selectName ?? SelectedItem?.Item.Name;
        Items.Clear();
        if (_location is not null)
        {
            IReadOnlyList<AssetItem> items;
            try
            {
                items = _location.List();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                items = [];
            }

            foreach (var item in items)
            {
                Items.Add(new AssetItemViewModel(item));
            }
        }

        AssetItemViewModel? match = null;
        if (previous is not null)
        {
            match = Items.FirstOrDefault(i =>
                string.Equals(i.Name, previous, StringComparison.OrdinalIgnoreCase));
        }
        SelectedItem = match;
        Raise(nameof(Summary));
    }

    public void NavigateUp()
    {
        if (_location is FolderLocation { Parent: { } parent })
        {
            SetLocation(parent);
        }
    }

    public void Enter(AssetItemViewModel item)
    {
        if (item.IsFolder && item.Item.Tag is string path && Directory.Exists(path))
        {
            SetLocation(new FolderLocation(path));
        }
    }

    public void NotifyDirtyChanged()
    {
        Raise(nameof(IsDirty));
        Raise(nameof(Summary));
    }
}
