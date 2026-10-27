using System.Collections.ObjectModel;
using Pk2Mixer.Core;

namespace Pk2Mixer.App.ViewModels;

/// <summary>Application state: two explorer panes, the preview and the palette library.</summary>
public sealed class MainWindowViewModel : ObservableObject
{
    private ExplorerPaneViewModel _activePane;
    private string _status = "Ready";
    private bool _isBusy;
    private PaletteEntry? _selectedPalette;

    public MainWindowViewModel()
    {
        _activePane = Left;
        Left.Activated += pane => ActivePane = pane;
        Right.Activated += pane => ActivePane = pane;
    }

    public ExplorerPaneViewModel Left { get; } = new("Left");

    public ExplorerPaneViewModel Right { get; } = new("Right");

    public PreviewPaneViewModel Preview { get; } = new();

    public ObservableCollection<PaletteEntry> Palettes { get; } = [];

    public PaletteEntry? SelectedPalette
    {
        get => _selectedPalette;
        set
        {
            if (Set(ref _selectedPalette, value))
            {
                Raise(nameof(PaletteStatus));
            }
        }
    }

    public string PaletteStatus => _selectedPalette is null
        ? "bank PALTs (no override)"
        : $"palette override: {_selectedPalette.Name}";

    public AppSettings Settings { get; } = AppSettings.Load();

    public ExplorerPaneViewModel ActivePane
    {
        get => _activePane;
        set
        {
            if (Set(ref _activePane, value))
            {
                Left.IsActive = value == Left;
                Right.IsActive = value == Right;
                Raise(nameof(ActivePaneLabel));
            }
        }
    }

    public ExplorerPaneViewModel OtherPane => ActivePane == Left ? Right : Left;

    public string ActivePaneLabel => $"active: {(ActivePane == Left ? "left" : "right")}";

    public string Status
    {
        get => _status;
        set => Set(ref _status, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => Set(ref _isBusy, value);
    }
}
