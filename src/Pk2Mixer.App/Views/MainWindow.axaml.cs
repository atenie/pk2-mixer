using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Pk2Mixer.App.Services;
using Pk2Mixer.App.ViewModels;
using Pk2Mixer.Core;
using Pk2Mixer.Core.Assets;
using Pk2Mixer.Core.Locations;

namespace Pk2Mixer.App.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel _vm = null!;
    private PaletteResolver _paletteResolver = null!;
    private List<FoundArchive> _foundArchives = [];
    private ImbBank? _previewBank;
    private IAssetLocation? _previewLocation;
    private AssetItem? _previewItem;
    private bool _suppressPreviewEvents;
    private bool _initialized;
    private int _previewGeneration;

    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>Wires the views to the view model; called by <see cref="App"/>.</summary>
    public void Initialize(MainWindowViewModel viewModel, string[]? startupArgs = null)
    {
        _vm = viewModel;
        _paletteResolver = new PaletteResolver(
            () => new IAssetLocation?[] { _vm.Left.Location, _vm.Right.Location }
                .OfType<ArchiveLocation>(),
            () => _vm.Palettes,
            () => _vm.SelectedPalette);

        LeftPane.OpenArchiveRequested += async (_, _) => await OpenArchiveAsync(_vm.Left);
        LeftPane.OpenFolderRequested += async (_, _) => await OpenFolderAsync(_vm.Left);
        LeftPane.UpRequested += (_, _) => _vm.Left.NavigateUp();
        LeftPane.SaveRequested += async (_, _) => await SaveAsync(_vm.Left);
        LeftPane.ImportRequested += async (_, _) => await ImportAsync(_vm.Left);
        LeftPane.CopyRequested += async (_, _) => await CopyAsync(_vm.Left, _vm.Right);
        LeftPane.CopyPngRequested += async (_, _) => await CopyPngAsync(_vm.Left, _vm.Right);
        LeftPane.ActivatedRequested += (_, _) => _vm.ActivePane = _vm.Left;

        RightPane.OpenArchiveRequested += async (_, _) => await OpenArchiveAsync(_vm.Right);
        RightPane.OpenFolderRequested += async (_, _) => await OpenFolderAsync(_vm.Right);
        RightPane.UpRequested += (_, _) => _vm.Right.NavigateUp();
        RightPane.SaveRequested += async (_, _) => await SaveAsync(_vm.Right);
        RightPane.ImportRequested += async (_, _) => await ImportAsync(_vm.Right);
        RightPane.CopyRequested += async (_, _) => await CopyAsync(_vm.Right, _vm.Left);
        RightPane.CopyPngRequested += async (_, _) => await CopyPngAsync(_vm.Right, _vm.Left);
        RightPane.ActivatedRequested += (_, _) => _vm.ActivePane = _vm.Right;

        LeftPane.PointerPressed += (_, _) => _vm.ActivePane = _vm.Left;
        RightPane.PointerPressed += (_, _) => _vm.ActivePane = _vm.Right;

        _vm.Left.SelectionChanged += item => { _ = ShowPreviewAsync(_vm.Left); };
        _vm.Right.SelectionChanged += item => { _ = ShowPreviewAsync(_vm.Right); };

        PreviewPane.ExportPngRequested += async (_, _) => await ExportPngAsync();
        PreviewPane.OptionsChanged += (_, _) =>
        {
            if (!_suppressPreviewEvents)
            {
                RenderImbPreview();
            }
        };

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.SelectedPalette))
            {
                RenderImbPreview();
            }
        };

        _vm.ActivePane = _vm.Left;
        RestoreSelectedPalette();
        RestoreFoundArchives();
        _initialized = true;
        _vm.Status = "Ready — open a .PK2 archive, a folder, or use File ▸ Find.";

        OpenStartupLocation(startupArgs);
    }

    /// <summary>Opens a .PK2 archive or folder passed on the command line (Open with…).</summary>
    private void OpenStartupLocation(string[]? args)
    {
        var path = args?.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a) && !a.StartsWith('-'));
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        if (Directory.Exists(path))
        {
            try
            {
                _vm.Left.SetLocation(new FolderLocation(path));
                _vm.ActivePane = _vm.Left;
                _vm.Status = $"Opened folder {path}.";
            }
            catch (Exception ex) when (ex is IOException or DirectoryNotFoundException)
            {
                _vm.Status = "Cannot open the startup folder: " + ex.Message;
            }
            return;
        }

        if (File.Exists(path))
        {
            _ = OpenArchivePathAsync(_vm.Left, path);
        }
        else
        {
            _vm.Status = "Startup path not found: " + path;
        }
    }

    /// <summary>Reloads the last selected palette when it was a loose file.</summary>
    private void RestoreSelectedPalette()
    {
        var source = _vm.Settings.SelectedPaletteSource;
        if (string.IsNullOrEmpty(source) || !File.Exists(source))
        {
            return;
        }

        try
        {
            var entry = PaletteLibrary.LoadFile(source);
            if (entry is null)
            {
                return;
            }
            if (!_vm.Palettes.Any(e => e.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase) &&
                                       e.SourcePath.Equals(entry.SourcePath, StringComparison.OrdinalIgnoreCase)))
            {
                _vm.Palettes.Add(entry);
            }
            _vm.SelectedPalette = entry;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or AssetFormatException)
        {
            // a missing or changed palette file is not fatal
        }
    }

    private void RestoreFoundArchives()
    {
        _foundArchives = _vm.Settings.FoundArchives
            .Where(File.Exists)
            .Select(path => new FoundArchive
            {
                Path = path,
                InstallRoot = Rt2InstallFinder.InferInstallRoot(path),
            })
            .ToList();
        RebuildFoundMenu();
    }

    private void RebuildFoundMenu()
    {
        FoundMenu.Items.Clear();
        if (_foundArchives.Count == 0)
        {
            FoundMenu.Items.Add(new MenuItem
            {
                Header = "(none yet — use File ▸ Find…)",
                IsEnabled = false,
            });
            return;
        }

        foreach (var archive in _foundArchives)
        {
            var item = new MenuItem { Header = archive.Label };
            ToolTip.SetTip(item, archive.Path);
            var path = archive.Path;
            item.Click += async (_, _) => await OpenArchivePathAsync(_vm.ActivePane, path);
            FoundMenu.Items.Add(item);
        }
    }

    private async void New_Click(object? sender, RoutedEventArgs e) => await NewArchiveAsync();

    private async void Open_Click(object? sender, RoutedEventArgs e) =>
        await OpenArchiveAsync(_vm.ActivePane);

    private async void Find_Click(object? sender, RoutedEventArgs e) => await FindAsync();

    private async void SaveActive_Click(object? sender, RoutedEventArgs e) =>
        await SaveAsync(_vm.ActivePane);

    private void Exit_Click(object? sender, RoutedEventArgs e) => Close();

    private async void PaletteLoad_Click(object? sender, RoutedEventArgs e) =>
        await LoadPalettesAsync(askFolder: true);

    private async void PaletteSelect_Click(object? sender, RoutedEventArgs e) =>
        await SelectPaletteAsync();

    private void PaletteUseBank_Click(object? sender, RoutedEventArgs e)
    {
        _vm.SelectedPalette = null;
        _vm.Settings.SelectedPaletteName = null;
        _vm.Settings.SelectedPaletteSource = null;
        _vm.Settings.Save();
        _vm.Status = "Preview uses each bank's own PALT palettes.";
        RenderImbPreview();
    }

    private async Task NewArchiveAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Create a new PK2 archive",
            SuggestedFileName = "new.pk2",
            DefaultExtension = "pk2",
            ShowOverwritePrompt = true,
            FileTypeChoices =
            [
                new FilePickerFileType("PK2 archive") { Patterns = ["*.pk2", "*.PK2"] },
            ],
        });

        var path = file?.TryGetLocalPath();
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            _vm.IsBusy = true;
            _vm.Status = $"Creating {Path.GetFileName(path)}…";
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            var location = await Task.Run(() => ArchiveLocation.CreateNew(path));
            _vm.ActivePane.SetLocation(location);
            _vm.Status = $"Created {location.Title} — copy or import assets into it.";
        }
        catch (Exception ex)
        {
            _vm.Status = "Cannot create the archive: " + ex.Message;
        }
        finally
        {
            _vm.IsBusy = false;
        }
    }

    private async Task OpenArchiveAsync(ExplorerPaneViewModel pane)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open a PK2 archive",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("PK2 archives")
                {
                    Patterns = ["*.pk2", "*.PK2", "*.pak", "*.PAK"],
                },
                new FilePickerFileType("All files") { Patterns = ["*"] },
            ],
        });
        if (files.Count == 0)
        {
            return;
        }

        var paths = files
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!)
            .ToList();

        var target = pane;
        foreach (var path in paths)
        {
            await OpenArchivePathAsync(target, path);
            target = ReferenceEquals(target, _vm.Left) ? _vm.Right : _vm.Left;
        }
    }

    private async Task OpenArchivePathAsync(ExplorerPaneViewModel pane, string path)
    {
        try
        {
            _vm.IsBusy = true;
            _vm.Status = $"Opening {Path.GetFileName(path)}…";
            var location = await Task.Run(() => ArchiveLocation.Open(path));
            pane.SetLocation(location);
            _vm.ActivePane = pane;
            _vm.Status = $"Opened {location.DisplayPath} — {location.EntryCount:N0} entries.";
        }
        catch (Exception ex)
        {
            _vm.Status = $"Cannot open {Path.GetFileName(path)}: {ex.Message}";
        }
        finally
        {
            _vm.IsBusy = false;
        }
    }

    private async Task OpenFolderAsync(ExplorerPaneViewModel pane)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Open a folder",
            AllowMultiple = false,
        });
        var path = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            pane.SetLocation(new FolderLocation(path));
            _vm.ActivePane = pane;
            _vm.Status = $"Opened folder {path}.";
        }
        catch (Exception ex)
        {
            _vm.Status = "Cannot open the folder: " + ex.Message;
        }
    }

    private async Task SaveAsync(ExplorerPaneViewModel pane)
    {
        if (pane.Location is not ArchiveLocation archive || !archive.IsDirty)
        {
            _vm.Status = "Nothing to save in this pane.";
            return;
        }

        try
        {
            _vm.IsBusy = true;
            _vm.Status = $"Saving {archive.Title}…";
            await Task.Run(archive.Save);
            pane.NotifyDirtyChanged();
            _vm.Status = $"Saved {archive.DisplayPath} ({archive.Archive.FileSize:N0} bytes).";
        }
        catch (Exception ex)
        {
            _vm.Status = "Save failed: " + ex.Message;
        }
        finally
        {
            _vm.IsBusy = false;
        }
    }

    private async Task ImportAsync(ExplorerPaneViewModel pane)
    {
        if (pane.Location is null)
        {
            _vm.Status = "Open a folder or archive first.";
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Import files into {pane.Location.Title}",
            AllowMultiple = true,
        });
        var paths = files
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!)
            .ToList();
        if (paths.Count == 0)
        {
            return;
        }

        var location = pane.Location;
        try
        {
            _vm.IsBusy = true;
            _vm.Status = $"Importing {paths.Count} file(s) into {location.Title}…";
            await Task.Run(() =>
            {
                foreach (var path in paths)
                {
                    location.Write(null, Path.GetFileName(path), File.ReadAllBytes(path));
                }
                location.Save();
            });
            pane.Refresh();
            pane.NotifyDirtyChanged();
            _vm.Status = $"Imported {paths.Count} file(s) into {location.Title}.";
        }
        catch (Exception ex)
        {
            _vm.Status = "Import failed: " + ex.Message;
        }
        finally
        {
            _vm.IsBusy = false;
        }
    }

    private async Task CopyAsync(ExplorerPaneViewModel source, ExplorerPaneViewModel target)
    {
        if (source.Location is null || source.SelectedItem is null)
        {
            _vm.Status = "Select a file to copy.";
            return;
        }
        if (target.Location is null)
        {
            _vm.Status = "The other pane has no folder or archive open.";
            return;
        }
        if (target.Location is ArchiveLocation targetArchive && !targetArchive.IsWritable)
        {
            _vm.Status = "The destination archive is read-only.";
            return;
        }

        try
        {
            _vm.IsBusy = true;
            _vm.Status = $"Copying {source.SelectedItem.Name}…";
            var item = source.SelectedItem.Item;
            var message = await Task.Run(() =>
                AssetOperations.Copy(source.Location, item, target.Location));
            source.Refresh();
            target.Refresh();
            source.NotifyDirtyChanged();
            target.NotifyDirtyChanged();
            _vm.Status = message;
        }
        catch (Exception ex)
        {
            _vm.Status = "Copy failed: " + ex.Message;
        }
        finally
        {
            _vm.IsBusy = false;
        }
    }

    private async Task CopyPngAsync(ExplorerPaneViewModel source, ExplorerPaneViewModel target)
    {
        if (source.Location is null || source.SelectedItem is null)
        {
            _vm.Status = "Select an .IMB file to export as PNG.";
            return;
        }
        if (target.Location is null)
        {
            _vm.Status = "The other pane has no folder or archive open.";
            return;
        }

        try
        {
            _vm.IsBusy = true;
            _vm.Status = $"Rendering {source.SelectedItem.Name} as PNG…";
            var item = source.SelectedItem.Item;
            var message = await Task.Run(() =>
                AssetOperations.CopyAsPng(source.Location, item, target.Location, _paletteResolver));
            source.Refresh();
            target.Refresh();
            source.NotifyDirtyChanged();
            target.NotifyDirtyChanged();
            _vm.Status = message;
        }
        catch (Exception ex)
        {
            _vm.Status = "PNG export failed: " + ex.Message;
        }
        finally
        {
            _vm.IsBusy = false;
        }
    }

    private async Task ShowPreviewAsync(ExplorerPaneViewModel pane)
    {
        var item = pane.SelectedItem;
        var location = pane.Location;
        var generation = ++_previewGeneration;

        _previewBank = null;
        _previewItem = item?.Item;
        _previewLocation = location;

        if (item is null || location is null)
        {
            if (generation == _previewGeneration)
            {
                _vm.Preview.Clear();
            }
            return;
        }

        if (item.IsFolder)
        {
            _vm.Preview.ShowEmpty(item.Name, "Double-click the folder row to enter it.");
            return;
        }

        try
        {
            _vm.Status = $"Loading {item.Name}…";
            var data = await Task.Run(() => location.Read(item.Item));
            if (generation != _previewGeneration)
            {
                return;
            }
            RenderPreview(item, data);
        }
        catch (Exception ex)
        {
            if (generation == _previewGeneration)
            {
                _vm.Preview.ShowError(item.Name, ex.Message);
            }
        }
        finally
        {
            if (generation == _previewGeneration)
            {
                _vm.Status = "Ready";
            }
        }
    }

    /// <summary>Chooses the right previewer for the file type documented in spec/.</summary>
    private void RenderPreview(AssetItemViewModel item, byte[] data)
    {
        try
        {
            var result = AssetPreview.Build(item.Name, item.Kind, data,
                group => _paletteResolver.Resolve(group, _previewLocation));
            _previewBank = result.Bank;
            _vm.Preview.Apply(result.Content, result.Bank);
        }
        catch (Exception ex)
        {
            _vm.Preview.ShowError(item.Name, ex.Message);
        }
    }

    private void RenderImbPreview()
    {
        if (_previewBank is null || !_initialized)
        {
            return;
        }

        var title = _previewItem?.Name ?? "IMB bank";
        try
        {
            var keepGroupState = _vm.Preview.GroupNumbers.Count == _previewBank.GroupCount &&
                                 _vm.Preview.GroupIndex >= 0 &&
                                 _vm.Preview.GroupIndex < _previewBank.GroupCount;
            var content = AssetPreview.RenderImb(_previewBank, _vm.Preview.GroupIndex,
                _vm.Preview.Sheet, title,
                group => _paletteResolver.Resolve(group, _previewLocation));
            _suppressPreviewEvents = true;
            try
            {
                _vm.Preview.Apply(content, _previewBank, keepGroupState);
            }
            finally
            {
                _suppressPreviewEvents = false;
            }
        }
        catch (Exception ex)
        {
            _vm.Preview.ShowError(title, ex.Message);
        }
    }

    private async Task ExportPngAsync()
    {
        if (_previewBank is null || _previewItem is null)
        {
            _vm.Status = "Select an .IMB asset first.";
            return;
        }

        var baseName = Path.GetFileNameWithoutExtension(_previewItem.Name) +
                       (_vm.Preview.Sheet ? "-sheet" : $"-g{_vm.Preview.GroupIndex}");
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export PNG",
            SuggestedFileName = baseName + ".png",
            DefaultExtension = "png",
            FileTypeChoices =
            [
                new FilePickerFileType("PNG image") { Patterns = ["*.png"] },
            ],
        });
        var path = file?.TryGetLocalPath();
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        var bank = _previewBank;
        var location = _previewLocation;
        var sheet = _vm.Preview.Sheet;
        var group = _vm.Preview.GroupIndex;
        try
        {
            _vm.IsBusy = true;
            _vm.Status = "Rendering PNG…";
            var image = await Task.Run(() =>
                AssetOperations.RenderBankPng(bank, _paletteResolver, location, sheet, group));
            await Task.Run(() => PngWriter.Write(path, image));
            _vm.Status = $"Exported {Path.GetFileName(path)} ({image.Width}×{image.Height} px).";
        }
        catch (Exception ex)
        {
            _vm.Status = "PNG export failed: " + ex.Message;
        }
        finally
        {
            _vm.IsBusy = false;
        }
    }

    private async Task FindAsync()
    {
        _vm.IsBusy = true;
        _vm.Status = "Searching for Railroad Tycoon II installations…";
        try
        {
            var progress = new Progress<string>(message => _vm.Status = message);
            var found = await Task.Run(() => Rt2InstallFinder.FindAll(progress));

            if (found.Count == 0)
            {
                _vm.Status = "Nothing found automatically — choose the Railroad Tycoon II folder…";
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Choose a Railroad Tycoon II installation folder",
                    AllowMultiple = false,
                });
                var path = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
                if (string.IsNullOrEmpty(path))
                {
                    _vm.Status = "Find cancelled.";
                    return;
                }
                found = await Task.Run(() => Rt2InstallFinder.FindIn(path, 8));
            }

            _foundArchives = found.ToList();
            _vm.Settings.FoundArchives = _foundArchives.Select(a => a.Path).ToList();
            _vm.Settings.Save();
            RebuildFoundMenu();
            _vm.Status = _foundArchives.Count == 0
                ? "No .PK2 or .PAK archives were found."
                : $"Found {_foundArchives.Count} archive(s) — open them from File ▸ Found.";
        }
        catch (Exception ex)
        {
            _vm.Status = "Find failed: " + ex.Message;
        }
        finally
        {
            _vm.IsBusy = false;
        }
    }

    private async Task LoadPalettesAsync(bool askFolder)
    {
        string? folder = null;
        if (askFolder || _vm.Palettes.Count == 0)
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Load palettes from a Railroad Tycoon II folder",
                AllowMultiple = false,
            });
            folder = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
            if (string.IsNullOrEmpty(folder))
            {
                return;
            }
        }
        else
        {
            folder = _vm.Settings.LastPaletteFolder;
        }

        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            _vm.Status = "Choose the Railroad Tycoon II installation folder to load palettes from.";
            return;
        }

        try
        {
            _vm.IsBusy = true;
            _vm.Status = $"Scanning palettes in {folder}…";
            var progress = new Progress<string>(message => _vm.Status = message);
            var entries = await Task.Run(() => PaletteLibrary.LoadFromFolder(folder, progress));
            _vm.Palettes.Clear();
            foreach (var entry in entries)
            {
                _vm.Palettes.Add(entry);
            }
            _vm.Settings.LastPaletteFolder = folder;
            _vm.Settings.Save();
            _vm.Status = $"Loaded {entries.Count:N0} palette(s) from {folder}.";
            RenderImbPreview();
        }
        catch (Exception ex)
        {
            _vm.Status = "Loading palettes failed: " + ex.Message;
        }
        finally
        {
            _vm.IsBusy = false;
        }
    }

    private async Task SelectPaletteAsync()
    {
        if (_vm.Palettes.Count == 0)
        {
            await LoadPalettesAsync(askFolder: true);
            if (_vm.Palettes.Count == 0)
            {
                return;
            }
        }

        var dialog = new PaletteSelectWindow
        {
            DataContext = new PaletteSelectViewModel(_vm.Palettes, _vm.SelectedPalette),
        };
        var chosen = await dialog.ShowDialog<PaletteEntry?>(this);
        if (chosen is null)
        {
            return;
        }

        _vm.SelectedPalette = chosen;
        _vm.Settings.SelectedPaletteName = chosen.Name;
        _vm.Settings.SelectedPaletteSource = chosen.SourcePath;
        _vm.Settings.Save();
        _vm.Status = $"Preview palette: {chosen.Name} ({chosen.SourceName}).";
        RenderImbPreview();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_initialized)
        {
            foreach (var pane in new[] { _vm.Left, _vm.Right })
            {
                if (pane.Location is ArchiveLocation { IsDirty: true } archive)
                {
                    try
                    {
                        archive.Save();
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        _vm.Status = "Could not save " + archive.Title + ": " + ex.Message;
                    }
                }
            }
            _vm.Settings.Save();
        }
        base.OnClosing(e);
    }
}
