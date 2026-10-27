using Pk2Mixer.Core.Assets;

namespace Pk2Mixer.Tests;

/// <summary>
/// Optional retail-data tests. They run only when PK2MIXER_RT2_DIR points at a
/// folder of extracted RT2 assets (e.g. the research project's
/// <c>pk2_extracted/Railroad Tycoon 2 Platinum/RT2</c>), so CI stays hermetic.
/// </summary>
internal static class RetailTests
{
    public static void Run()
    {
        var folder = Folder;
        if (folder is null)
        {
            Console.WriteLine("skip retail-data tests (set PK2MIXER_RT2_DIR to run them)");
            return;
        }

        Console.WriteLine($"retail data: {folder}");
        TestRunner.Test("retail: every .imb parses and its first frames decode", AllImbParse);
        TestRunner.Test("retail: every .pal/.DAT palette loads", AllPalettesLoad);
        RetailRenderTests.Run(folder);
    }

    private static string? Folder
    {
        get
        {
            var folder = Environment.GetEnvironmentVariable("PK2MIXER_RT2_DIR");
            return string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder) ? null : folder;
        }
    }

    private static void AllImbParse()
    {
        var directory = Folder!;
        var files = Directory.GetFiles(directory, "*.imb");
        Check.True(files.Length > 0, "no .imb files found");

        var parseFailures = new List<string>();
        var decodeFailures = new List<string>();
        var groups = 0;
        var frames = 0;
        foreach (var file in files)
        {
            ImbBank bank;
            try
            {
                bank = ImbBank.Parse(File.ReadAllBytes(file));
            }
            catch (Exception ex) when (ex is AssetFormatException or IOException)
            {
                parseFailures.Add($"{Path.GetFileName(file)}: {ex.Message}");
                continue;
            }

            groups += bank.GroupCount;
            frames += bank.FrameCount;
            foreach (var group in bank.Groups)
            {
                if (group.FirstFrame is not { Width: > 0, Height: > 0 } frame)
                {
                    continue;
                }
                try
                {
                    var decoded = Pk2Mixer.Core.Graphics.ImbFrameDecoder.Decode(frame);
                    Check.True(decoded.Indices.Length == decoded.Width * decoded.Height,
                        "pixel count");
                }
                catch (AssetFormatException ex)
                {
                    decodeFailures.Add($"{Path.GetFileName(file)}/{group.PaletteIndex}: {ex.Message}");
                }
            }
        }

        Console.WriteLine($"     {files.Length} banks, {groups} groups, {frames} frames");
        Check.Equal(0, parseFailures.Count,
            "banks that failed to parse: " + string.Join("; ", parseFailures.Take(5)));
        Check.Equal(0, decodeFailures.Count,
            "frames that failed to decode: " + string.Join("; ", decodeFailures.Take(5)));
    }

    private static void AllPalettesLoad()
    {
        var directory = Folder!;
        var files = Directory.GetFiles(directory, "*.pal")
            .Concat(Directory.GetFiles(directory, "*.DAT"))
            .ToList();
        Check.True(files.Count > 0, "no palette files found");

        var failures = new List<string>();
        var palt = 0;
        foreach (var file in files)
        {
            try
            {
                var data = File.ReadAllBytes(file);
                var palette = PaletteSource.FromData(Path.GetFileName(file), data);
                if (palette is null)
                {
                    failures.Add(Path.GetFileName(file) + ": unrecognised");
                }
                else if (palette is PaltPalette)
                {
                    palt++;
                }
            }
            catch (Exception ex) when (ex is AssetFormatException or IOException)
            {
                failures.Add(Path.GetFileName(file) + ": " + ex.Message);
            }
        }

        Console.WriteLine($"     {files.Count} palette files ({palt} PALT ramps)");
        Check.Equal(0, failures.Count,
            "palettes that failed: " + string.Join("; ", failures.Take(5)));
    }
}
