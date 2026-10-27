using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pk2Mixer.Core;

/// <summary>
/// Small JSON settings file: discovered archives (so File ▸ Found survives a
/// restart), last folders and the selected preview palette.
/// </summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public List<string> FoundArchives { get; set; } = [];

    public string? LastPaletteFolder { get; set; }

    public string? LastLeftPath { get; set; }

    public string? LastRightPath { get; set; }

    public string? SelectedPaletteName { get; set; }

    public string? SelectedPaletteSource { get; set; }

    public static string ConfigDirectory
    {
        get
        {
            if (OperatingSystem.IsWindows())
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(appData, "Pk2Mixer");
            }
            if (OperatingSystem.IsMacOS())
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                return Path.Combine(home, "Library", "Application Support", "Pk2Mixer");
            }
            var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (!string.IsNullOrWhiteSpace(configHome))
            {
                return Path.Combine(configHome, "Pk2Mixer");
            }
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(userProfile, ".config", "Pk2Mixer");
        }
    }

    public static string ConfigFile => Path.Combine(ConfigDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(ConfigFile))
            {
                var json = File.ReadAllText(ConfigFile);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (settings is not null)
                {
                    return settings;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or JsonException)
        {
            // a broken settings file must never stop the app from starting
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDirectory);
            File.WriteAllText(ConfigFile, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // ignore: settings are a convenience, not a requirement
        }
    }
}
