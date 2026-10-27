using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Pk2Mixer.App.ViewModels;
using Pk2Mixer.App.Views;

namespace Pk2Mixer.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainWindowViewModel();
            var window = new MainWindow { DataContext = viewModel };
            window.Initialize(viewModel, desktop.Args);
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
