using System.Buffers.Binary;
using System.IO.Compression;
using Pk2Mixer.Core;
using Pk2Mixer.Core.Assets;
using Pk2Mixer.Core.Graphics;

namespace Pk2Mixer.Tests;

internal static class PaletteTests
{
    public static void Run()
    {
        TestRunner.Test("RGB555 expansion matches the 5-bit ramp", Rgb555);
        TestRunner.Test("PALT shade table selects the documented row", PaltRows);
        TestRunner.Test("PALT preview renders 256 swatches", PaltRender);
        TestRunner.Test("768-byte defaulN.pal is an RGB screen palette", ScreenPalette);
        TestRunner.Test("FLATC16.DAT parses as a 16-bit ramp", Flatc16);
        TestRunner.Test("FLATC8x.DAT parses as an 8-bit ramp", Flatc8);
        TestRunner.Test("rgbto8.dat parses as a 256x128 byte table", RgbToTableTest);
        TestRunner.Test("PNG writer emits a valid RGBA file", PngEncode);
        TestRunner.Test("LNG parser handles escapes, comments and junk", LngParse);
        TestRunner.Test("hex dump shows offset, bytes and ASCII", HexDumpTest);
        TestRunner.Test("palette library scans loose files and archives", PaletteLibraryScan);
        TestRunner.Test("short paths keep the installation name", ShortPathTest);
        TestRunner.Test("RT2 install detection finds data/*.PK2", InstallFinder);
        TestRunner.Test("folder location lists, writes and de-duplicates names", FolderLocationTest);
        TestRunner.Test("archive location lists named and sniffed entries", ArchiveLocationTest);
        TestRunner.Test("archive location resolves PALTnnnn for a group", ArchivePaltLookup);
        TestRunner.Test("IMB preview describes group and palette", ImbPreviewTest);
        TestRunner.Test("preview classification picks the documented renderer", PreviewClassification);
    }

    private static void Rgb555()
    {
        Check.Equal(new Rgb(255, 255, 255), Rgb.FromRgb555(0x7FFF), "white");
        Check.Equal(new Rgb(0, 0, 255), Rgb.FromRgb555(0x001F), "blue");
        Check.Equal(new Rgb(255, 0, 0), Rgb.FromRgb555(0x7C00), "red");
        Check.Equal(new Rgb(0, 255, 0), Rgb.FromRgb555(0x03E0), "green");
        Check.Equal(new Rgb(0, 0, 0), Rgb.FromRgb555(0x0000), "black");
    }

    private static byte[] SamplePalt()
    {
        var shades = new sbyte[35];
        Array.Fill(shades, (sbyte)-1);
        shades[0] = 0;
        shades[16] = 1;
        ushort[] row0 = [0x0000, 0x7C00];
        ushort[] row1 = [0x7FFF, 0x001F];
        ushort[] row2 = [0x03E0];
        return SyntheticBank.BuildPalt(3, shades, [row0, row1, row2]);
    }

    private static void PaltRows()
    {
        Check.True(PaltPalette.TryParse(SamplePalt(), out var palette), "PALT parses");
        var palt = palette!;
        Check.Equal(3, palt.RowCount, "row count");
        Check.Equal(1, palt.RowForShade(16), "shade 16 -> row 1");
        Check.Equal(0, palt.RowForShade(0), "shade 0 -> row 0");
        Check.Equal(-1, palt.RowForShade(1), "undefined shade");
        Check.Equal(-1, palt.RowForShade(99), "out-of-range shade");
        Check.Equal(new Rgb(255, 255, 255), palt.GetColors(16)[0], "neutral row entry 0");
        Check.Equal(new Rgb(0, 0, 255), palt.GetColors(16)[1], "neutral row entry 1");
        Check.Equal(new Rgb(255, 255, 255), palt.GetColors(1)[0], "undefined shade falls back");
        Check.True(palt.Describe(16).Contains("3 shade rows"), "description mentions rows");
    }

    private static void PaltRender()
    {
        var palt = PaletteSource.FromData("PALT0000.PAL", SamplePalt());
        Check.True(palt is PaltPalette, "factory detects PALT");
        var image = palt!.Render(16);
        Check.Equal(16 * 12, image.Width, "swatch grid width");
        Check.Equal(16 * 12, image.Height, "swatch grid height");
        Check.Equal(255, image.Get(13, 1).B, "second swatch is blue");
        Check.Equal(70, image.Get(1, 1).R, "index 0 is marked as the transparency key");
    }

    private static void ScreenPalette()
    {
        var data = new byte[768];
        for (var i = 0; i < 256; i++)
        {
            data[i * 3] = (byte)i;
            data[i * 3 + 1] = (byte)i;
            data[i * 3 + 2] = (byte)i;
        }
        var palette = PaletteSource.FromData("defaul1.pal", data);
        Check.True(palette is RgbScreenPalette, "screen palette detected");
        Check.Equal(new Rgb(3, 3, 3), palette!.GetColors(16)![3], "entry 3");
        Check.Equal(192, palette.Render(16).Width, "swatch width");
    }

    private static void Flatc16()
    {
        var palette = PaletteSource.FromData("FLATC16.DAT", SyntheticBank.BuildFlatc16(2));
        Check.True(palette is FlatcRamp, "FLATC16 detected");
        var ramp = (FlatcRamp)palette!;
        Check.Equal(16, ramp.Bits, "bits");
        Check.Equal(2, ramp.RowCount, "rows");
        Check.True(ramp.Describe().Contains("2 rows x 32"), "description");
        var image = ramp.Render(16);
        Check.Equal(32, image.Width, "ramp width");
        Check.Equal(2, image.Height, "ramp height");
        Check.Equal(0, image.Get(0, 0).R, "dark end of row 0");
        Check.Equal(255, image.Get(31, 0).R, "bright end of row 0");
        Check.Equal(255, image.Get(31, 1).G, "bright end of row 1");
    }

    private static void Flatc8()
    {
        var palette = PaletteSource.FromData("FLATC80.DAT", SyntheticBank.BuildFlatc8(2));
        Check.True(palette is FlatcRamp, "FLATC8 detected");
        var ramp = (FlatcRamp)palette!;
        Check.Equal(8, ramp.Bits, "bits");
        Check.Equal(64, ramp.EightBit!.Length, "values");
        var image = ramp.Render(16);
        Check.Equal(32, image.Width, "ramp width (32 shades)");
        Check.Equal(2, image.Height, "ramp height");
        Check.Equal(0, image.Get(0, 0).R, "first value grayscale");
        Check.Equal(63, image.Get(31, 1).R, "last value grayscale");
    }

    private static void RgbToTableTest()
    {
        var data = new byte[32768];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i >> 8);   // one constant row per y
        }
        var table = PaletteSource.FromData("rgbto80.dat", data);
        Check.True(table is RgbToTable, "rgbto table detected");
        Check.Equal(32768, ((RgbToTable)table!).Length, "table byte length");
        Check.True(table!.GetColors(16) is null, "not usable as an IMB palette");
        var image = table.Render(16);
        Check.Equal(256, image.Width, "table width");
        Check.Equal(128, image.Height, "table height");
        Check.Equal(0, image.Get(0, 0).R, "row 0");
        Check.Equal(100, image.Get(200, 100).R, "row 100");
    }

    private static void PngEncode()
    {
        var image = RgbaImage.Filled(3, 2, new Rgba(1, 2, 3, 255));
        image.Set(2, 1, new Rgba(10, 20, 30, 128));
        var png = PngWriter.Encode(image);

        Check.True(png.Take(8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
            "PNG signature");

        var offset = 8;
        var sawHeader = false;
        var sawData = false;
        var sawEnd = false;
        while (offset + 12 <= png.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset));
            var type = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);
            var payload = png.AsSpan(offset + 8, length);
            var expectedCrc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + 8 + length));
            Check.Equal(Crc32(png.AsSpan(offset + 4, 4 + length)), expectedCrc, $"{type} CRC");

            if (type == "IHDR")
            {
                sawHeader = true;
                Check.Equal(3u, BinaryPrimitives.ReadUInt32BigEndian(payload), "IHDR width");
                Check.Equal(2u, BinaryPrimitives.ReadUInt32BigEndian(payload[4..]), "IHDR height");
                Check.Equal((byte)8, payload[8], "bit depth");
                Check.Equal((byte)6, payload[9], "colour type RGBA");
            }
            else if (type == "IDAT")
            {
                sawData = true;
                using var input = new MemoryStream(payload.ToArray());
                using var zlib = new ZLibStream(input, CompressionMode.Decompress);
                using var raw = new MemoryStream();
                zlib.CopyTo(raw);
                var bytes = raw.ToArray();
                Check.Equal(2 * (3 * 4 + 1), bytes.Length, "scanline bytes");
                Check.Equal((byte)0, bytes[0], "first filter byte");
                Check.Equal((byte)1, bytes[1], "first pixel red");
                Check.Equal((byte)3, bytes[3], "first pixel blue");
                Check.Equal((byte)128, bytes[^1], "last pixel alpha");
            }
            else if (type == "IEND")
            {
                sawEnd = true;
            }

            offset += 12 + length;
        }

        Check.True(sawHeader, "IHDR chunk present");
        Check.True(sawData, "IDAT chunk present");
        Check.True(sawEnd, "IEND chunk present");
        Check.Equal(png.Length, offset, "chunk stream consumed exactly");
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
        }
        return crc ^ 0xFFFFFFFFu;
    }

    private static void LngParse()
    {
        const string text =
            "; header comment\r\n" +
            "   1    \"\\n\"\r\n" +
            "   7    \"File error opening '%1'\"\r\n" +
            "bad line without id\r\n" +
            "  27    \"%1 is new chairman of %2!\"   \r\n" +
            "  30    \"Tab\\there and a quote: \\\"Q\\\"\"\r\n" +
            "  31    \"unterminated\r\n";
        var lng = LngFile.Parse(text);
        Check.Equal(4, lng.Count, "entry count");
        Check.Equal(1u, lng.Entries[0].Id, "first id");
        Check.Equal("\n", lng.Entries[0].Text, "escaped newline");
        Check.Equal("File error opening '%1'", lng.Entries[1].Text, "first string");
        Check.Equal("%1 is new chairman of %2!", lng.Entries[2].Text, "trailing spaces ignored");
        Check.Equal("Tab\there and a quote: \"Q\"", lng.Entries[3].Text, "tab and quote escapes");
        Check.True(LngFile.LooksLike(System.Text.Encoding.ASCII.GetBytes(text)), "looks like LNG");
        Check.True(!LngFile.LooksLike(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 }), "binary is not LNG");

        var table = new byte[32768];
        for (var i = 0; i < table.Length; i++)
        {
            table[i] = (byte)((i * 37) & 0xFF);
        }
        Check.True(!LngFile.LooksLike(table), "a binary conversion table is not LNG");
    }

    private static void HexDumpTest()
    {
        var dump = HexDump.Format([0x48, 0x69, 0x00, 0xFF]);
        Check.True(dump.Contains("00000000"), "offset");
        Check.True(dump.Contains("48 69 00 ff"), "hex bytes");
        Check.True(dump.Contains("|Hi..|"), "ascii pane");
        Check.Equal("(empty)", HexDump.Format([]), "empty payload");
    }

    private static void PaletteLibraryScan()
    {
        var directory = TestRunner.NewTempDirectory();
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "defaul0.pal"), new byte[768]);
            var archive = Pk2Archive.CreateEmpty();
            archive.Add(SamplePalt(), "PALT0000.PAL");
            archive.Add(SyntheticBank.BuildFlatc16(4), "FLATC16.DAT");
            archive.Add([1, 2, 3], "ground.imb");
            archive.Save(Path.Combine(directory, "Test.PK2"));

            var entries = PaletteLibrary.LoadFromFolder(directory);
            Check.Equal(3, entries.Count, "loaded palettes");
            Check.True(entries.Any(e => e.Name == "PALT0000.PAL"), "PALT from the archive");
            Check.True(entries.Any(e => e.Name == "FLATC16.DAT"), "FLATC from the archive");
            Check.True(entries.Any(e => e.Name == "defaul0.pal"), "loose screen palette");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void ShortPathTest()
    {
        var separator = Path.DirectorySeparatorChar;
        var root = Path.Combine(Path.GetTempPath(), "Railroad Tycoon II");
        var archive = Path.Combine(root, "data", "RT2.PK2");
        var label = ShortPath.Format(archive, root);
        Check.Equal($"Railroad Tycoon II{separator}data{separator}RT2.PK2", label, "relative label");

        var deep = Path.Combine(root, "artwork", "extra", "very", "deeply", "nested", "file.pk2");
        var shortened = ShortPath.Format(deep, root);
        Check.True(shortened.Contains('…'), "middle is elided");
        Check.Equal("RT2.PK2", Path.GetFileName(ShortPath.Format(archive)), "keeps the file name");
    }

    private static void InstallFinder()
    {
        var directory = TestRunner.NewTempDirectory();
        try
        {
            var install = Path.Combine(directory, "Railroad Tycoon II");
            Directory.CreateDirectory(Path.Combine(install, "data"));
            Directory.CreateDirectory(Path.Combine(install, "maps"));
            File.WriteAllBytes(Path.Combine(install, "data", "RT2.PK2"), [0, 0, 0, 0]);

            var found = Rt2InstallFinder.FindIn(install);
            Check.Equal(1, found.Count, "archive found");
            Check.Equal(Path.Combine(install, "data", "RT2.PK2"), found[0].Path, "archive path");
            Check.Equal(install, found[0].InstallRoot, "install root");
            Check.True(found[0].Label.Contains("Railroad Tycoon II"), "label names the install");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void FolderLocationTest()
    {
        var directory = TestRunner.NewTempDirectory();
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "a.imb"), [1, 2]);
            Directory.CreateDirectory(Path.Combine(directory, "sub"));

            var location = new Pk2Mixer.Core.Locations.FolderLocation(directory);
            var items = location.List();
            Check.Equal(2, items.Count, "folder items");
            Check.True(items[0].IsFolder, "folders first");
            Check.Equal("a.imb", items[1].Name, "file name");
            Check.Equal(2L, items[1].Size, "file size");
            Check.Equal(AssetKind.Imb, items[1].Kind, "file kind");
            Check.True(location.Read(items[1]).SequenceEqual(new byte[] { 1, 2 }), "read back");

            location.Write(null, "a.imb", [3, 4, 5]);
            Check.True(File.Exists(Path.Combine(directory, "a (2).imb")), "unique name used");
            Check.True(File.ReadAllBytes(Path.Combine(directory, "a (2).imb"))
                .SequenceEqual(new byte[] { 3, 4, 5 }), "new payload");

            var unique = location.List().First(i => i.Name == "a (2).imb");
            Check.Equal("a (2).imb", location.SuggestName(unique), "suggest name");
            Check.True(location.Parent is not null, "has a parent");
            Check.True(location.Enter("sub").DisplayPath.EndsWith("sub", StringComparison.Ordinal),
                "enter sub");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void ArchiveLocationTest()
    {
        var directory = TestRunner.NewTempDirectory();
        try
        {
            var path = Path.Combine(directory, "Test.PK2");
            var archive = Pk2Archive.CreateEmpty();
            archive.Add([1, 2, 3], "ground.imb");
            archive.Add(SyntheticBank.BuildRawBank());     // unnamed IMB bank
            archive.Save(path);

            var location = new Pk2Mixer.Core.Locations.ArchiveLocation(Pk2Archive.Load(path), path);
            var items = location.List();
            Check.Equal(2, items.Count, "archive items");
            Check.Equal("ground.imb", items[0].Name, "catalogue name");
            Check.Equal(AssetKind.Imb, items[0].Kind, "catalogue kind");
            Check.Equal("entry_0001", items[1].Name, "unnamed entry");
            Check.Equal(AssetKind.Imb, items[1].Kind, "unnamed entry sniffed as IMB");
            Check.Equal(".imb", items[1].Extension, "sniffed extension");
            Check.Equal("entry_0001.imb", location.SuggestName(items[1]), "suggested name");

            location.Write(null, "added.lng", [7, 7, 7]);
            Check.True(location.IsDirty, "dirty after add");
            location.Save();
            var reloaded = Pk2Mixer.Core.Locations.ArchiveLocation.Open(path);
            Check.Equal(3, reloaded.EntryCount, "saved entry count");
            Check.True(reloaded.Archive.FindByName("added.lng") is not null, "added entry persisted");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void ArchivePaltLookup()
    {
        var directory = TestRunner.NewTempDirectory();
        try
        {
            var path = Path.Combine(directory, "pal.pk2");
            var archive = Pk2Archive.CreateEmpty();
            archive.Add(SyntheticBank.BuildRawBank(), "ground.imb");
            archive.Add(SamplePalt(), "PALT0058.PAL");
            archive.Save(path);

            var location = new Pk2Mixer.Core.Locations.ArchiveLocation(Pk2Archive.Load(path), path);
            var palt = location.FindPalt(58);
            Check.True(palt is not null, "PALT0058 resolved");
            Check.Equal(3, palt!.RowCount, "row count");
            Check.True(location.FindPalt(59) is null, "missing PALT returns null");

            var bank = ImbBank.Parse(location.Read(location.List()[0]));
            var preview = AssetPreview.RenderImb(bank, 0, false, "ground.imb",
                group => new GroupPalette(location.FindPalt((int)group.PaletteIndex)!.GetColors(16),
                    group.PaletteName));
            Check.Equal(PreviewKind.Image, preview.Kind, "preview kind");
            Check.Equal(4, preview.Image!.Width, "preview width");
            Check.True(preview.Details.Contains("PALT0058"), "details name the palette");
            Check.True(preview.Details.Contains("4×3"), "details include the frame size");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void ImbPreviewTest()
    {
        var bank = ImbBank.Parse(SyntheticBank.BuildRawBank());
        GroupPalette Resolve(ImbGroup group) => new(ImbRenderer.Grayscale, "test palette");
        var single = AssetPreview.RenderImb(bank, 0, false, "bank.imb", Resolve);
        Check.Equal(PreviewKind.Image, single.Kind, "single-frame preview");
        Check.Equal(3, single.Image!.Height, "single-frame height");

        var sheet = AssetPreview.RenderImb(bank, 0, true, "bank.imb", Resolve);
        Check.Equal(PreviewKind.Image, sheet.Kind, "sheet preview");
        Check.True(sheet.Details.Contains("1 group"), "sheet details");
    }

    private static void PreviewClassification()
    {
        var bankBytes = SyntheticBank.BuildRawBank();
        var imb = AssetPreview.Build("ground.imb", AssetKind.Imb, bankBytes,
            _ => new GroupPalette(ImbRenderer.Grayscale, "test palette"));
        Check.Equal(PreviewKind.Image, imb.Content.Kind, "IMB image");
        Check.True(imb.Bank is not null, "IMB bank is returned");

        // a rgbto table is a palette DAT, not a localization file, even if its
        // bytes happen to contain quotes
        var table = new byte[32768];
        for (var i = 0; i < table.Length; i++)
        {
            table[i] = (byte)((i * 37) & 0xFF);
        }
        var dat = AssetPreview.Build("rgbto80.dat", AssetKind.Dat, table);
        Check.Equal(PreviewKind.Image, dat.Content.Kind, "DAT image");
        Check.True(dat.Content.Details.Contains("RGB -> 8-bit"), "rgbto description");
        Check.True(dat.Bank is null, "no IMB bank for a DAT");

        var pal = AssetPreview.Build("PALT0000.PAL", AssetKind.Palette, SamplePalt());
        Check.Equal(PreviewKind.Image, pal.Content.Kind, "PAL image");
        Check.True(pal.Content.Details.Contains("PALT"), "PALT description");

        var lngText = System.Text.Encoding.ASCII.GetBytes("   1    \"hello\"\r\n   2    \"world\"\r\n");
        var lng = AssetPreview.Build("text.lng", AssetKind.Lng, lngText);
        Check.Equal(PreviewKind.Lng, lng.Content.Kind, "LNG lines");
        Check.Equal(2, lng.Content.Lines!.Count, "LNG entries");

        var bin = AssetPreview.Build("blob.bin", AssetKind.Bin,
            [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08]);
        Check.Equal(PreviewKind.Text, bin.Content.Kind, "hex dump");
        Check.True(bin.Content.Text!.Contains("00000000"), "hex offset");

        var binImb = AssetPreview.Build("00552.bin", AssetKind.Bin, bankBytes);
        Check.Equal(PreviewKind.Image, binImb.Content.Kind, "unnamed .bin IMB");
    }
}
