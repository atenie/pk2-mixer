using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Pk2Mixer.App.ViewModels;

namespace Pk2Mixer.App.Views;

public partial class ExplorerPaneView : UserControl
{
    public ExplorerPaneView()
    {
        InitializeComponent();
    }

    public event EventHandler? OpenArchiveRequested;
    public event EventHandler? OpenFolderRequested;
    public event EventHandler? UpRequested;
    public event EventHandler? SaveRequested;
    public event EventHandler? ImportRequested;
    public event EventHandler? CopyRequested;
    public event EventHandler? CopyPngRequested;
    public event EventHandler? ActivatedRequested;

    private ExplorerPaneViewModel? Pane => DataContext as ExplorerPaneViewModel;

    private void OpenArchive_Click(object? sender, RoutedEventArgs e) =>
        OpenArchiveRequested?.Invoke(this, EventArgs.Empty);

    private void OpenFolder_Click(object? sender, RoutedEventArgs e) =>
        OpenFolderRequested?.Invoke(this, EventArgs.Empty);

    private void Up_Click(object? sender, RoutedEventArgs e) =>
        UpRequested?.Invoke(this, EventArgs.Empty);

    private void Save_Click(object? sender, RoutedEventArgs e) =>
        SaveRequested?.Invoke(this, EventArgs.Empty);

    private void Import_Click(object? sender, RoutedEventArgs e) =>
        ImportRequested?.Invoke(this, EventArgs.Empty);

    private void Copy_Click(object? sender, RoutedEventArgs e) =>
        CopyRequested?.Invoke(this, EventArgs.Empty);

    private void CopyPng_Click(object? sender, RoutedEventArgs e) =>
        CopyPngRequested?.Invoke(this, EventArgs.Empty);

    private void ItemsGrid_PointerPressed(object? sender, PointerPressedEventArgs e) =>
        ActivatedRequested?.Invoke(this, EventArgs.Empty);

    private void ItemsGrid_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (Pane?.SelectedItem is { } item)
        {
            Pane.Enter(item);
        }
    }
}
