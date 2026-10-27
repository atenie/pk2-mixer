using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Pk2Mixer.Core;
using Pk2Mixer.Core.Assets;
using Pk2Mixer.App.ViewModels;

namespace Pk2Mixer.App.Views;

public partial class PaletteSelectWindow : Window
{
    public PaletteSelectWindow()
    {
        InitializeComponent();
    }

    private PaletteSelectViewModel? ViewModel => DataContext as PaletteSelectViewModel;

    private async void Browse_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Load a palette or DAT table",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Palettes and DAT tables")
                {
                    Patterns = ["*.pal", "*.dat", "*.PAL", "*.DAT"],
                },
            ],
        });
        if (files.Count == 0)
        {
            return;
        }
        var path = files[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            var entry = PaletteLibrary.LoadFile(path);
            if (entry is null)
            {
                ViewModel?.SetError($"'{Path.GetFileName(path)}' is not a recognised palette or DAT table");
                return;
            }
            ViewModel?.Add(entry);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or AssetFormatException)
        {
            ViewModel?.SetError(ex.Message);
        }
    }

    private void Ok_Click(object? sender, RoutedEventArgs e) => Close(ViewModel?.Selected);

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);
}
