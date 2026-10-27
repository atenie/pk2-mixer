using System.Buffers.Binary;
using Pk2Mixer.Core;
using Pk2Mixer.Core.Assets;
using Pk2Mixer.Core.Locations;

namespace Pk2Mixer.Tests;

internal static class WorkflowTests
{
    public static void Run()
    {
        TestRunner.Test("copy archive -> folder -> archive preserves bytes", CopyRoundTrip);
        TestRunner.Test("copy .IMB as PNG writes a contact sheet", CopyImbAsPng);
        TestRunner.Test("copying the same name again replaces the entry", CopyReplaces);
    }

    private static byte[] SamplePalt() => SyntheticBank.BuildPalt(3, BuildShadeTable(), BuildRows());

    private static sbyte[] BuildShadeTable()
    {
        var shades = new sbyte[35];
        Array.Fill(shades, (sbyte)-1);
        shades[0] = 0;
        shades[16] = 1;
        return shades;
    }

    private static ushort[][] BuildRows() =>
    [
        [0x0000, 0x7C00],
        [0x7FFF, 0x001F],
        [0x03E0],
    ];

    private static void CopyRoundTrip()
    {
        var directory = TestRunner.NewTempDirectory();
        try
        {
            var archivePath = Path.Combine(directory, "Source.PK2");
            var archive = Pk2Archive.CreateEmpty();
            archive.Add(SyntheticBank.BuildRawBank(), "ground.imb");
            archive.Add(SamplePalt(), "PALT0058.PAL");
            archive.Save(archivePath);

            var source = ArchiveLocation.Open(archivePath);
            var folderPath = Path.Combine(directory, "out");
            Directory.CreateDirectory(folderPath);
            var folder = new FolderLocation(folderPath);

            var item = source.List().First(i => i.Name == "ground.imb");
            var message = AssetOperations.Copy(source, item, folder);
            Check.True(message.Contains("ground.imb"), "status names the file");
            var extracted = Path.Combine(folderPath, "ground.imb");
            Check.True(File.Exists(extracted), "file extracted");
            Check.True(File.ReadAllBytes(extracted).SequenceEqual(source.Read(item)), "payload matches");

            var extractedItem = folder.List().First(i => i.Name == "ground.imb");
            var targetPath = Path.Combine(directory, "Target.PK2");
            AssetOperations.Copy(folder, extractedItem, ArchiveLocation.CreateNew(targetPath));

            var reloaded = ArchiveLocation.Open(targetPath);
            Check.True(reloaded.Archive.FindByName("ground.imb") is not null, "entry present in target");
            var copied = reloaded.List().First(i => i.Name == "ground.imb");
            Check.True(reloaded.Read(copied).SequenceEqual(source.Read(item)),
                "bytes survive the round trip");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void CopyImbAsPng()
    {
        var directory = TestRunner.NewTempDirectory();
        try
        {
            var archivePath = Path.Combine(directory, "Graphics.PK2");
            var archive = Pk2Archive.CreateEmpty();
            archive.Add(SyntheticBank.BuildRawBank(), "ground.imb");
            archive.Add(SamplePalt(), "PALT0058.PAL");
            archive.Save(archivePath);

            var source = ArchiveLocation.Open(archivePath);
            var folderPath = Path.Combine(directory, "png");
            Directory.CreateDirectory(folderPath);
            var folder = new FolderLocation(folderPath);
            var resolver = new PaletteResolver(() => [source], () => [], () => null);

            var item = source.List().First(i => i.Name == "ground.imb");
            var message = AssetOperations.CopyAsPng(source, item, folder, resolver);
            Check.True(message.Contains("ground.png"), "status names the PNG");
            var pngPath = Path.Combine(folderPath, "ground.png");
            Check.True(File.Exists(pngPath), "PNG written");

            var png = File.ReadAllBytes(pngPath);
            Check.True(png.Length > 32, "PNG has content");
            Check.True(png.Take(8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
                "PNG signature");
            Check.Equal(6u, BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16)), "cell width 4 + 2");
            Check.Equal(5u, BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20)), "cell height 3 + 2");

            // the same operation into an archive adds a .png entry
            var targetPath = Path.Combine(directory, "WithPng.PK2");
            var target = ArchiveLocation.CreateNew(targetPath);
            AssetOperations.CopyAsPng(source, item, target, resolver);
            var reloaded = ArchiveLocation.Open(targetPath);
            Check.True(reloaded.Archive.FindByName("ground.png") is not null,
                "PNG entry added to archive");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void CopyReplaces()
    {
        var directory = TestRunner.NewTempDirectory();
        try
        {
            var sourcePath = Path.Combine(directory, "ReplaceSource.PK2");
            var sourceArchive = Pk2Archive.CreateEmpty();
            var newPayload = SyntheticBank.BuildRawBank();
            sourceArchive.Add(newPayload, "ground.imb");
            sourceArchive.Save(sourcePath);

            var targetPath = Path.Combine(directory, "ReplaceTarget.PK2");
            var targetArchive = Pk2Archive.CreateEmpty();
            targetArchive.Add([1, 2, 3], "ground.imb");
            targetArchive.Add([4, 5], "other.bin");
            targetArchive.Save(targetPath);

            var source = ArchiveLocation.Open(sourcePath);
            var target = ArchiveLocation.Open(targetPath);
            var item = source.List().First(i => i.Name == "ground.imb");
            var message = AssetOperations.Copy(source, item, target);

            Check.True(message.StartsWith("Replaced", StringComparison.Ordinal),
                "status reports a replacement");
            Check.Equal(2, target.EntryCount, "entry count is unchanged");
            var entry = target.Archive.FindByName("ground.imb");
            Check.True(entry is not null, "entry still present");
            Check.True(target.Archive.ReadEntry(entry!.Index).SequenceEqual(newPayload),
                "payload was replaced");
            Check.True(target.Archive.FindByName("other.bin") is not null, "other entry intact");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
