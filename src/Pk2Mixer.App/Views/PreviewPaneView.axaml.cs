using Avalonia.Controls;
using Avalonia.Interactivity;
using Pk2Mixer.App.ViewModels;

namespace Pk2Mixer.App.Views;

public partial class PreviewPaneView : UserControl
{
    public PreviewPaneView()
    {
        InitializeComponent();
    }

    public event EventHandler? ExportPngRequested;
    public event EventHandler? OptionsChanged;

    private PreviewPaneViewModel? Preview => DataContext as PreviewPaneViewModel;

    private void Group_SelectionChanged(object? sender, SelectionChangedEventArgs e) =>
        OptionsChanged?.Invoke(this, EventArgs.Empty);

    private void Sheet_Click(object? sender, RoutedEventArgs e) =>
        OptionsChanged?.Invoke(this, EventArgs.Empty);

    private void ExportPng_Click(object? sender, RoutedEventArgs e) =>
        ExportPngRequested?.Invoke(this, EventArgs.Empty);
}
