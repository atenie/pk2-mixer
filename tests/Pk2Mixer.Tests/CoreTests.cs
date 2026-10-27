using System.Buffers.Binary;
using Pk2Mixer.Core.Assets;

namespace Pk2Mixer.Tests;

internal static class CoreTests
{
    public static void Run()
    {
        TestRunner.Test("PK2 name hash vectors match the research", HashVectors);
        TestRunner.Test("PK2 name hash is case-insensitive", HashCaseFolding);
        TestRunner.Test("embedded name catalogue resolves retail assets", NameCatalogue);
        TestRunner.Test("PK2 archive round-trips through the writer", Pk2RoundTrip);
        TestRunner.Test("a newly created PK2 archive is a valid empty archive", Pk2Empty);
        TestRunner.Test("PK2 archive append and replace stay contiguous", Pk2Mutate);
        TestRunner.Test("PK2 legacy PAK layout is accepted", Pk2Legacy);
        TestRunner.Test("PK2 rejects a broken index", Pk2RejectsBroken);
        TestRunner.Test("IMB bank parser reads groups, frames and palette index", ImbParse);
        TestRunner.Test("IMB raw frame decodes to palette indices", ImbRawDecode);
        TestRunner.Test("IMB row-RLE frame decodes literal and transparent runs", ImbRleDecode);
        TestRunner.Test("IMB palette gate uses the first mip-0 frame byte", ImbPaletteGate);
        TestRunner.Test("IMB renderer treats palette index 0 as transparent", ImbRender);
    }

    private static void HashVectors()
    {
        Check.Equal(0x16a732e5u, Pk2Hash.NameHash("FLATC16.DAT"), "FLATC16.DAT");
        Check.Equal(0x9f2c528fu, Pk2Hash.NameHash("ground.imb"), "ground.imb");
        Check.Equal(0x0f4908bfu, Pk2Hash.NameHash("trainbuy.imb"), "trainbuy.imb");
        Check.Equal(0x3d8780dbu, Pk2Hash.NameHash("mainwin.win"), "mainwin.win");
        Check.Equal(0x1c86e51fu, Pk2Hash.NameHash("arch1.imb"), "arch1.imb");
    }

    private static void HashCaseFolding()
    {
        Check.Equal(Pk2Hash.NameHash("ground.imb"), Pk2Hash.NameHash("GROUND.IMB"), "upper case");
        Check.Equal(Pk2Hash.NameHash("Mainwin.Win"), Pk2Hash.NameHash("mainwin.win"), "mixed case");
        Check.True(Pk2Hash.NameHashAlt("mainwin.win") != Pk2Hash.NameHash("mainwin.win"),
            "the alternate accumulator differs");
    }

    private static void NameCatalogue()
    {
        Check.True(Pk2NameTable.Default.Count > 2300,
            $"expected the shipped catalogue, got {Pk2NameTable.Default.Count} names");
        Check.True(Pk2NameTable.Default.TryGetName(0x16a732e5u, out var name), "FLATC16.DAT present");
        Check.Equal("FLATC16.DAT", name, "catalogue name");
        Check.True(Pk2NameTable.Default.TryGetName(0x9f2c528fu, out var ground), "ground.imb present");
        Check.Equal("ground.imb", ground, "catalogue name");
        Check.True(!Pk2NameTable.Default.TryGetName(0x12345678u, out _), "unknown hash is absent");
    }

    private static void Pk2RoundTrip()
    {
        var directory = TestRunner.NewTempDirectory();
        try
        {
            var archive = Pk2Archive.CreateEmpty();
            archive.Add([1, 2, 3], "alpha.bin");
            archive.Add([9, 8, 7, 6, 5], "beta.lng");
            Check.Equal(2, archive.Count, "in-memory count");
            Check.Equal(4 + 2 * 12, archive.DataStart, "data start");

            var path = Path.Combine(directory, "roundtrip.pk2");
            archive.Save(path);
            Check.True(File.Exists(path), "file written");
            Check.True(!archive.IsDirty, "dirty flag cleared by save");

            var bytes = File.ReadAllBytes(path);
            Check.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(bytes), "entry count at +0");
            Check.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8)), "first offset");
            Check.Equal(3u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12)), "first size");

            var reloaded = Pk2Archive.Load(path);
            Check.Equal(2, reloaded.Count, "reloaded count");
            Check.Equal(28, reloaded.DataStart, "reloaded data start");
            Check.True(reloaded.IsContiguous, "contiguous index");
            // PK2 stores hashes only, so names are recovered from the catalogue;
            // a name added by the tool is re-resolvable through its hash.
            Check.Equal(Pk2Hash.NameHash("alpha.bin"), reloaded.Entries[0].Hash, "first hash");
            Check.True(reloaded.FindByName("alpha.bin") is not null, "first hash lookup");
            Check.Equal(Pk2Hash.NameHash("beta.lng"), reloaded.Entries[1].Hash, "second hash");
            Check.Equal(5, reloaded.Entries[1].Size, "second size");
            Check.True(reloaded.ReadEntry(0).SequenceEqual(new byte[] { 1, 2, 3 }), "first payload");
            Check.True(reloaded.ReadEntry(1).SequenceEqual(new byte[] { 9, 8, 7, 6, 5 }), "second payload");
            Check.True(reloaded.FindByName("ALPHA.BIN") is not null, "name lookup folds case");
            Check.Equal(reloaded.FileSize, new FileInfo(path).Length, "file size matches");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void Pk2Empty()
    {
        var directory = TestRunner.NewTempDirectory();
        try
        {
            var path = Path.Combine(directory, "empty.pk2");
            var archive = Pk2Archive.CreateEmpty(path);
            Check.Equal(0, archive.Count, "new archive has no entries");
            archive.Save(path);

            var bytes = File.ReadAllBytes(path);
            Check.Equal(4, bytes.Length, "an empty archive is just the u32 count");
            var reloaded = Pk2Archive.Load(path);
            Check.Equal(0, reloaded.Count, "reloaded empty archive");
            Check.Equal(4, reloaded.DataStart, "data start of an empty archive");
            Check.Equal(4L, reloaded.FileSize, "file size of an empty archive");
            Check.True(reloaded.FindByName("ground.imb") is null, "no entries to find");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void Pk2Mutate()
    {
        var archive = Pk2Archive.CreateEmpty();
        archive.Add([1], "one.bin");
        archive.Add([2, 2], "two.bin");
        archive.Replace(0, [3, 3, 3], "one.bin");
        archive.Add([4, 4, 4, 4], "three.bin");

        Check.Equal(3, archive.Count, "count after mutations");
        Check.True(archive.ReadEntry(0).SequenceEqual(new byte[] { 3, 3, 3 }), "replaced payload");
        Check.True(archive.ReadEntry(1).SequenceEqual(new byte[] { 2, 2 }), "second payload intact");
        Check.True(archive.ReadEntry(2).SequenceEqual(new byte[] { 4, 4, 4, 4 }), "appended payload");
        Check.Equal(3, archive.Entries[0].Size, "entry 0 size");
        Check.Equal(3, archive.Entries[1].Offset, "entry 1 offset");
        Check.Equal(5, archive.Entries[2].Offset, "entry 2 offset");

        var reparsed = Pk2Archive.Parse(archive.ToArray());
        Check.Equal(3, reparsed.Count, "reparsed count");
        Check.True(reparsed.IsContiguous, "reparsed index is contiguous");
        Check.Equal(Pk2Hash.NameHash("three.bin"), reparsed.Entries[2].Hash, "reparsed hash");
    }

    private static void Pk2Legacy()
    {
        // u16 count, index at +2, one entry, three payload bytes.
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((ushort)1);
            writer.Write(Pk2Hash.NameHash("legacy.bin"));
            writer.Write(0u);
            writer.Write(3u);
            writer.Write(new byte[] { 0xAA, 0xBB, 0xCC });
        }

        var archive = Pk2Archive.Parse(stream.ToArray());
        Check.True(archive.IsLegacyPak, "legacy layout detected");
        Check.Equal(1, archive.Count, "legacy count");
        Check.True(archive.ReadEntry(0).SequenceEqual(new byte[] { 0xAA, 0xBB, 0xCC }), "legacy payload");
    }

    private static void Pk2RejectsBroken()
    {
        Check.Throws<AssetFormatException>(() => Pk2Archive.Parse([0xFF, 0xFF, 0xFF, 0xFF]),
            "absurd count");
        Check.Throws<AssetFormatException>(() => Pk2Archive.Parse([1, 0, 0]), "truncated header");

        // count=1, offset points past the end of the payload
        var broken = new byte[4 + 12 + 2];
        BinaryPrimitives.WriteUInt32LittleEndian(broken, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(broken.AsSpan(4), 0x11111111);
        BinaryPrimitives.WriteUInt32LittleEndian(broken.AsSpan(8), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(broken.AsSpan(12), 999);
        Check.Throws<AssetFormatException>(() => Pk2Archive.Parse(broken), "extent past end");
    }

    private static void ImbParse()
    {
        var bank = ImbBank.Parse(SyntheticBank.BuildRawBank());
        Check.Equal(1, bank.GroupCount, "group count");
        Check.Equal(1, bank.FrameCount, "frame count");

        var group = bank.Groups[0];
        Check.Equal(58u, group.PaletteIndex, "palette index");
        Check.Equal("PALT0058.PAL", group.PaletteName, "palette name");
        Check.Equal(0, group.Mip0, "mip0");
        Check.True(group.NeedsPalette, "palette gate bit 1");

        var frame = bank.Groups[0].Frames[0];
        Check.Equal(4, frame.Width, "frame width");
        Check.Equal(3, frame.Height, "frame height");
        Check.True(frame.IsRaw, "raw storage");
        Check.Equal(0, frame.Level, "level");
    }

    private static void ImbRawDecode()
    {
        var bank = ImbBank.Parse(SyntheticBank.BuildRawBank());
        var decoded = Pk2Mixer.Core.Graphics.ImbFrameDecoder.Decode(bank.Groups[0].Frames[0]);
        Check.Equal(12, decoded.Indices.Length, "pixel count");
        Check.Equal("raw", decoded.Mode, "mode");
        Check.True(decoded.Indices.SequenceEqual(SyntheticBank.RawPixels), "pixel payload");
    }

    private static void ImbRleDecode()
    {
        var bank = ImbBank.Parse(SyntheticBank.BuildRleBank());
        var decoded = Pk2Mixer.Core.Graphics.ImbFrameDecoder.Decode(bank.Groups[0].Frames[0]);
        Check.Equal("rle", decoded.Mode, "mode");
        Check.Equal(8, decoded.Indices.Length, "pixel count");
        Check.True(decoded.Indices.SequenceEqual(new byte[] { 1, 2, 0, 0, 0, 0, 0, 0 }),
            "decoded literal + transparent runs");
    }

    private static void ImbPaletteGate()
    {
        // A frame id of 0x2b sets bit 1 (palette gate); 0x28 does not.
        var gated = ImbBank.Parse(SyntheticBank.BuildRawBank(id: 0x2b));
        Check.True(gated.Groups[0].NeedsPalette, "0x2b needs a palette");
        var plain = ImbBank.Parse(SyntheticBank.BuildRawBank(id: 0x28));
        Check.True(!plain.Groups[0].NeedsPalette, "0x28 does not need a palette");
    }

    private static void ImbRender()
    {
        var bank = ImbBank.Parse(SyntheticBank.BuildRawBank());
        var colors = new Pk2Mixer.Core.Graphics.Rgb[256];
        for (var i = 0; i < colors.Length; i++)
        {
            colors[i] = new Pk2Mixer.Core.Graphics.Rgb((byte)i, 0, 0);
        }
        colors[1] = new Pk2Mixer.Core.Graphics.Rgb(10, 20, 30);
        var image = ImbRenderer.RenderFrame(bank.Groups[0].Frames[0], colors);
        Check.Equal(4, image.Width, "image width");
        Check.Equal(3, image.Height, "image height");
        var first = image.Get(0, 0);
        Check.Equal((byte)10, first.R, "first red");
        Check.Equal((byte)20, first.G, "first green");
        Check.Equal((byte)255, first.A, "first opaque");
        Check.Equal((byte)0, image.Get(2, 2).A, "index 0 is transparent");
    }
}
