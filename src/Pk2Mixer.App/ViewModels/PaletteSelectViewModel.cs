using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Pk2Mixer.Core;
using Pk2Mixer.Core.Assets;
using Pk2Mixer.App.Services;

namespace Pk2Mixer.App.ViewModels;

/// <summary>View ▸ Palette ▸ Select: pick the palette used to preview IMB assets.</summary>
public sealed class PaletteSelectViewModel : ObservableObject
{
    private readonly List<PaletteEntry> _all;
    private string _search = string.Empty;
    private PaletteEntry? _selected;
    private Bitmap? _previewImage;
    private string _details = "Choose a palette from the list.";

    public PaletteSelectViewModel(IEnumerable<PaletteEntry> entries, PaletteEntry? selected)
    {
        _all = entries.ToList();
        Filter();
        Selected = Entries.FirstOrDefault(e =>
                       selected is not null &&
                       e.Name.Equals(selected.Name, StringComparison.OrdinalIgnoreCase) &&
                       e.SourcePath.Equals(selected.SourcePath, StringComparison.OrdinalIgnoreCase))
                   ?? Entries.FirstOrDefault();
    }

    public ObservableCollection<PaletteEntry> Entries { get; } = [];

    public string CountLabel => $"{Entries.Count:N0} of {_all.Count:N0} palette(s)";

    public string Search
    {
        get => _search;
        set
        {
            if (Set(ref _search, value))
            {
                Filter();
            }
        }
    }

    public PaletteEntry? Selected
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value))
            {
                UpdatePreview();
                Raise(nameof(HasSelection));
            }
        }
    }

    public bool HasSelection => _selected is not null;

    public Bitmap? PreviewImage
    {
        get => _previewImage;
        private set
        {
            var old = _previewImage;
            if (Set(ref _previewImage, value))
            {
                old?.Dispose();
            }
        }
    }

    public string Details
    {
        get => _details;
        private set => Set(ref _details, value);
    }

    private void Filter()
    {
        var term = _search.Trim();
        Entries.Clear();
        foreach (var entry in _all)
        {
            if (term.Length == 0 ||
                entry.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                entry.SourceName.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                Entries.Add(entry);
            }
        }
        Raise(nameof(CountLabel));
        if (_selected is not null && !Entries.Contains(_selected))
        {
            Selected = Entries.FirstOrDefault();
        }
    }

    /// <summary>Adds a palette chosen with the dialog's Browse… button.</summary>
    public void Add(PaletteEntry entry)
    {
        if (!_all.Any(e => e.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase) &&
                           e.SourcePath.Equals(entry.SourcePath, StringComparison.OrdinalIgnoreCase)))
        {
            _all.Add(entry);
        }
        _search = string.Empty;
        Raise(nameof(Search));
        Filter();
        Selected = _all.FirstOrDefault(e =>
            e.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase) &&
            e.SourcePath.Equals(entry.SourcePath, StringComparison.OrdinalIgnoreCase)) ?? _selected;
    }

    public void SetError(string message) => Details = message;

    private void UpdatePreview()
    {
        if (_selected is null)
        {
            PreviewImage = null;
            Details = "Choose a palette from the list.";
            return;
        }

        try
        {
            var image = _selected.Palette.Render(PaletteSource.DefaultShade);
            PreviewImage = BitmapFactory.Create(image);
            Details = $"{_selected.Name}\n{_selected.Palette.Kind} · " +
                      $"{_selected.Palette.Describe()}\nfrom {_selected.SourcePath}";
        }
        catch (Exception ex) when (ex is AssetFormatException or IOException)
        {
            PreviewImage = null;
            Details = $"Cannot render this palette: {ex.Message}";
        }
    }
}
