using Pk2Mixer.Core;
using Pk2Mixer.Core.Assets;
using Pk2Mixer.Core.Graphics;
using Pk2Mixer.Core.Locations;

namespace Pk2Mixer.Tests;

internal static class ResolverTests
{
    public static void Run()
    {
        TestRunner.Test("palette resolver prefers the archive PALT", ArchiveFirst);
        TestRunner.Test("palette resolver falls back to library, selection, grayscale", Fallbacks);
        TestRunner.Test("an explicit palette selection overrides the bank PALT", SelectionOverrides);
    }

    private static sbyte[] ShadeTable()
    {
        var shades = new sbyte[35];
        Array.Fill(shades, (sbyte)-1);
        shades[0] = 0;
        shades[16] = 1;
        return shades;
    }

    private static byte[] SamplePalt() =>
        SyntheticBank.BuildPalt(3, ShadeTable(),
        [
            [0x0000, 0x7C00],
            [0x7FFF, 0x001F],
            [0x03E0],
        ]);

    private static void ArchiveFirst()
    {
        var directory = TestRunner.NewTempDirectory();
        try
        {
            var archivePath = Path.Combine(directory, "Src.PK2");
            var archive = Pk2Archive.CreateEmpty();
            archive.Add(SyntheticBank.BuildRawBank(), "ground.imb");
            archive.Add(SamplePalt(), "PALT0058.PAL");
            archive.Save(archivePath);

            var location = ArchiveLocation.Open(archivePath);
            var bank = ImbBank.Parse(location.Read(location.List().First(i => i.Name == "ground.imb")));
            var resolver = new PaletteResolver(() => [location], () => [], () => null);
            var resolved = resolver.Resolve(bank.Groups[0], location);
            Check.True(resolved.Label.Contains("archive"), "archive palette label");
            Check.Equal(new Rgb(255, 255, 255), resolved.Colors[0],
                "white comes from PALT0058 row 1");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void Fallbacks()
    {
        var bank = ImbBank.Parse(SyntheticBank.BuildRawBank());
        var group = bank.Groups[0];

        var libraryPalette = PaletteSource.FromData("PALT0058.PAL", SamplePalt())!;
        var library = new[]
        {
            new PaletteEntry
            {
                Name = "PALT0058.PAL",
                SourcePath = Path.Combine(Path.GetTempPath(), "Library.PK2"),
                Palette = libraryPalette,
            },
        };
        var libraryResolver = new PaletteResolver(() => [], () => library, () => null);
        var fromLibrary = libraryResolver.Resolve(group, null);
        Check.True(fromLibrary.Label.Contains("library"), "library fallback label");
        Check.Equal(new Rgb(255, 255, 255), fromLibrary.Colors[0], "library colours");

        var selectedEntry = new PaletteEntry
        {
            Name = "defaul0.pal",
            SourcePath = Path.Combine(Path.GetTempPath(), "defaul0.pal"),
            Palette = PaletteSource.FromData("defaul0.pal", GrayPalette())!,
        };
        var selectedResolver = new PaletteResolver(() => [], () => [], () => selectedEntry);
        var fromSelection = selectedResolver.Resolve(group, null);
        Check.True(fromSelection.Label.Contains("selected"), "selection fallback label");
        Check.Equal(new Rgb(10, 10, 10), fromSelection.Colors[10], "selected colours");

        var emptyResolver = new PaletteResolver(() => [], () => [], () => null);
        var fallback = emptyResolver.Resolve(group, null);
        Check.Equal("grayscale fallback", fallback.Label, "grayscale label");
        Check.Equal(ImbRenderer.Grayscale[200], fallback.Colors[200], "grayscale colours");
    }

    private static byte[] GrayPalette()
    {
        var data = new byte[768];
        for (var i = 0; i < 256; i++)
        {
            data[i * 3] = (byte)i;
            data[i * 3 + 1] = (byte)i;
            data[i * 3 + 2] = (byte)i;
        }
        return data;
    }

    private static void SelectionOverrides()
    {
        var directory = TestRunner.NewTempDirectory();
        try
        {
            var archivePath = Path.Combine(directory, "Override.PK2");
            var archive = Pk2Archive.CreateEmpty();
            archive.Add(SyntheticBank.BuildRawBank(), "ground.imb");
            archive.Add(SamplePalt(), "PALT0058.PAL");
            archive.Save(archivePath);

            var location = ArchiveLocation.Open(archivePath);
            var bank = ImbBank.Parse(location.Read(location.List().First(i => i.Name == "ground.imb")));
            var selected = new PaletteEntry
            {
                Name = "defaul0.pal",
                SourcePath = Path.Combine(directory, "defaul0.pal"),
                Palette = PaletteSource.FromData("defaul0.pal", GrayPalette())!,
            };

            var resolver = new PaletteResolver(() => [location], () => [], () => selected);
            var resolved = resolver.Resolve(bank.Groups[0], location);
            Check.True(resolved.Label.Contains("selected"), "selection label");
            Check.Equal(new Rgb(0, 0, 0), resolved.Colors[0],
                "the selected palette overrides PALT0058");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
