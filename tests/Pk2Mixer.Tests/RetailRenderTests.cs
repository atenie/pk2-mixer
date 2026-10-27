using Pk2Mixer.Core.Assets;
using Pk2Mixer.Core.Locations;

namespace Pk2Mixer.Tests;

internal static class RetailRenderTests
{
    public static void Run(string folder)
    {
        TestRunner.Test("retail: ground.imb renders with its PALT0058", () => GroundRenders(folder));
        TestRunner.Test("retail: a PK2 built from retail files round-trips",
            () => ArchiveRoundTrip(folder));
    }

    private static void GroundRenders(string folder)
    {
        var ground = Path.Combine(folder, "ground.imb");
        var paltPath = Path.Combine(folder, "PALT0058.PAL");
        Check.True(File.Exists(ground), "ground.imb not extracted");
        Check.True(File.Exists(paltPath), "PALT0058.PAL not extracted");

        var bank = ImbBank.Parse(File.ReadAllBytes(ground));
        Check.True(bank.GroupCount > 100, $"expected many groups, got {bank.GroupCount}");

        var palt = PaletteSource.FromData("PALT0058.PAL", File.ReadAllBytes(paltPath)) as PaltPalette;
        Check.True(palt is not null, "PALT0058 parses as a shade ramp");

        var group = bank.Groups[4];
        Check.Equal(58u, group.PaletteIndex, "group 4 uses PALT0058");
        var frame = group.FirstFrame!;
        Check.Equal(64, frame.Width, "terrain frame width");
        Check.Equal(64, frame.Height, "terrain frame height");

        var image = ImbRenderer.RenderFrame(frame, palt!.GetColors(16));
        var opaque = 0;
        for (var i = 3; i < image.Pixels.Length; i += 4)
        {
            if (image.Pixels[i] != 0)
            {
                opaque++;
            }
        }
        Check.True(opaque > 100, $"expected visible terrain pixels, got {opaque}");
        Check.True(PngWriter.Encode(image).Length > 100, "PNG produced");
    }

    private static void ArchiveRoundTrip(string folder)
    {
        var directory = TestRunner.NewTempDirectory();
        try
        {
            var names = new[] { "ground.imb", "PALT0058.PAL", "bridge01.imb", "smoke.imb", "road.imb" }
                .Where(n => File.Exists(Path.Combine(folder, n)))
                .ToList();
            Check.True(names.Count >= 3, "not enough retail files for the round trip");

            var archive = Pk2Archive.CreateEmpty();
            foreach (var name in names)
            {
                archive.Add(File.ReadAllBytes(Path.Combine(folder, name)), name);
            }
            var path = Path.Combine(directory, "Retail.PK2");
            archive.Save(path);

            var location = ArchiveLocation.Open(path);
            Check.Equal(names.Count, location.EntryCount, "entry count");
            Check.True(location.Archive.IsContiguous, "written index is contiguous");
            foreach (var name in names)
            {
                var entry = location.Archive.FindByName(name);
                Check.True(entry is not null, $"name hash resolves {name}");
                Check.True(string.Equals(name, entry!.Name, StringComparison.OrdinalIgnoreCase),
                    "catalogue name");
                Check.True(location.Archive.ReadEntry(entry.Index)
                        .SequenceEqual(File.ReadAllBytes(Path.Combine(folder, name))),
                    name + " payload");
            }

            var bank = ImbBank.Parse(location.Read(
                location.List().First(i => i.Name == "ground.imb")));
            Check.True(bank.GroupCount > 100, "extracted bank still parses");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
