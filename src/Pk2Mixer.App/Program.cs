using System.Diagnostics;
using Avalonia;

namespace Pk2Mixer.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Optional Avalonia trace log (binding errors etc.): set PK2MIXER_TRACE=<file>.
        var tracePath = Environment.GetEnvironmentVariable("PK2MIXER_TRACE");
        if (!string.IsNullOrWhiteSpace(tracePath))
        {
            Trace.Listeners.Add(new TextWriterTraceListener(tracePath));
            Trace.AutoFlush = true;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
