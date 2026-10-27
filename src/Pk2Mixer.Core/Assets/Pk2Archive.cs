using System.Buffers.Binary;

namespace Pk2Mixer.Core.Assets;

/// <summary>Raised when an archive or asset does not match its documented format.</summary>
public sealed class AssetFormatException : Exception
{
    public AssetFormatException(string message) : base(message) { }
    public AssetFormatException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>One 12-byte index record of a PK2 archive.</summary>
public sealed class Pk2Entry
{
    public int Index { get; internal set; }
    public uint Hash { get; internal set; }
    /// <summary>Byte offset relative to the start of the data region.</summary>
    public int Offset { get; internal set; }
    public int Size { get; internal set; }
    /// <summary>Name recovered from the catalogue, or a name assigned when written.</summary>
    public string? Name { get; internal set; }

    public string DisplayName => Name ?? $"entry_{Index:D4}";
}

/// <summary>
/// Reader/writer for RT2 <c>.PK2</c> archives (spec 01):
/// <code>
/// u32 count
/// count * { u32 hash; u32 offset; u32 size }   // offset relative to dataStart
/// data region                                  // dataStart = 4 + count * 12
/// </code>
/// Entries are stored back to back and addressed by <see cref="Pk2Hash.NameHash"/>.
/// The writer always emits the canonical contiguous layout the retail engine reads.
/// </summary>
public sealed class Pk2Archive
{
    private const int RecordSize = 12;

    private byte[] _payload = [];
    private readonly List<Pk2Entry> _entries = [];
    private string? _path;

    private Pk2Archive() { }

    /// <summary>Path the archive was loaded from / last saved to.</summary>
    public string? Path => _path;

    public IReadOnlyList<Pk2Entry> Entries => _entries;

    public int DataStart => 4 + _entries.Count * RecordSize;

    /// <summary>Total size of the archive as it would be written now, in bytes.</summary>
    public long FileSize => DataStart + _payload.Length;

    /// <summary>Number of live entries (an archive can legally be empty).</summary>
    public int Count => _entries.Count;

    public bool IsDirty { get; private set; }

    /// <summary>True when the loaded index was strictly contiguous (the retail invariant).</summary>
    public bool IsContiguous { get; private set; } = true;

    /// <summary>True when the file used the legacy <c>.PAK</c> layout (u16 count at +0).</summary>
    public bool IsLegacyPak { get; private set; }

    public static Pk2Archive CreateEmpty(string? path = null) => new() { _path = path };

    public static Pk2Archive Load(string path) => Parse(File.ReadAllBytes(path), path);

    /// <summary>Parses a complete archive image held in memory.</summary>
    public static Pk2Archive Parse(byte[] file, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.Length < 4)
        {
            throw new AssetFormatException("file is too small to be a PK2 archive");
        }

        // Modern layout first; fall back to the legacy PAK layout the engine also probes.
        if (TryParseModern(file, path, out var archive) ||
            TryParseLegacy(file, path, out archive))
        {
            return archive!;
        }

        throw new AssetFormatException(
            "not a PK2 archive: the index does not fit the file (bad entry count)");
    }

    public static bool LooksLikeArchive(byte[] file)
    {
        if (file.Length < 4)
        {
            return false;
        }
        var count = BinaryPrimitives.ReadUInt32LittleEndian(file);
        if (count > 0 && 4L + (long)count * RecordSize <= file.Length)
        {
            return true;
        }
        var legacy = BinaryPrimitives.ReadUInt16LittleEndian(file);
        return legacy > 0 && 2L + (long)legacy * RecordSize <= file.Length;
    }

    private static bool TryParseModern(byte[] file, string? path, out Pk2Archive? archive)
    {
        archive = null;
        var count = BinaryPrimitives.ReadUInt32LittleEndian(file);
        var indexBytes = 4L + (long)count * RecordSize;
        if (indexBytes > file.Length || count > 8_000_000)
        {
            return false;
        }

        try
        {
            archive = Build(file, path, (int)count, 4);
        }
        catch (AssetFormatException)
        {
            return false;
        }
        return true;
    }

    private static bool TryParseLegacy(byte[] file, string? path, out Pk2Archive? archive)
    {
        archive = null;
        var count = BinaryPrimitives.ReadUInt16LittleEndian(file);
        var indexBytes = 2L + (long)count * RecordSize;
        if (count == 0 || indexBytes > file.Length)
        {
            return false;
        }

        try
        {
            archive = Build(file, path, count, 2);
        }
        catch (AssetFormatException)
        {
            return false;
        }
        archive.IsLegacyPak = true;
        return true;
    }

    private static Pk2Archive Build(byte[] file, string? path, int count, int indexStart)
    {
        var dataStart = indexStart + count * RecordSize;
        var result = new Pk2Archive { _path = path };
        result._entries.Capacity = count;
        var expectedOffset = 0L;

        for (var i = 0; i < count; i++)
        {
            var p = indexStart + i * RecordSize;
            var hash = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(p));
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(p + 4));
            var size = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(p + 8));
            if ((long)offset + size > file.Length - dataStart)
            {
                throw new AssetFormatException($"entry {i} extends past the end of the file");
            }
            if (offset != expectedOffset)
            {
                result.IsContiguous = false;
            }
            expectedOffset = (long)offset + size;

            result._entries.Add(new Pk2Entry
            {
                Index = i,
                Hash = hash,
                Offset = (int)offset,
                Size = (int)size,
                Name = Pk2NameTable.Default.GetName(hash),
            });
        }

        result._payload = file.AsSpan(dataStart).ToArray();
        return result;
    }

    /// <summary>Raw payload bytes of an entry (empty when out of range).</summary>
    public ReadOnlySpan<byte> GetData(int index)
    {
        if (index < 0 || index >= _entries.Count)
        {
            return ReadOnlySpan<byte>.Empty;
        }
        var entry = _entries[index];
        if (entry.Offset < 0 || entry.Size < 0 || (long)entry.Offset + entry.Size > _payload.Length)
        {
            return ReadOnlySpan<byte>.Empty;
        }
        return _payload.AsSpan(entry.Offset, entry.Size);
    }

    public byte[] ReadEntry(int index) => GetData(index).ToArray();

    public Pk2Entry? FindByHash(uint hash)
    {
        foreach (var entry in _entries)
        {
            if (entry.Hash == hash)
            {
                return entry;
            }
        }
        return null;
    }

    /// <summary>Case-insensitive name lookup with the recovered hash (and its alt).</summary>
    public Pk2Entry? FindByName(string name)
    {
        var hit = FindByHash(Pk2Hash.NameHash(name));
        return hit ?? FindByHash(Pk2Hash.NameHashAlt(name));
    }

    public bool ContainsName(string name) => FindByName(name) is not null;

    /// <summary>Appends a new entry. Returns its index.</summary>
    public int Add(byte[] data, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var offset = _payload.Length;
        if (data.Length > 0)
        {
            var grown = new byte[offset + data.Length];
            Buffer.BlockCopy(_payload, 0, grown, 0, offset);
            Buffer.BlockCopy(data, 0, grown, offset, data.Length);
            _payload = grown;
        }

        var entry = new Pk2Entry
        {
            Index = _entries.Count,
            Hash = name is null ? 0u : Pk2Hash.NameHash(name),
            Offset = offset,
            Size = data.Length,
            Name = name,
        };
        _entries.Add(entry);
        IsDirty = true;
        return entry.Index;
    }

    /// <summary>Replaces an entry's payload (and optionally its name/hash).</summary>
    public void Replace(int index, byte[] data, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (index < 0 || index >= _entries.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        // Rebuild the payload so the archive stays contiguous.
        var rebuilt = new byte[_payload.Length - _entries[index].Size + data.Length];
        var write = 0;
        for (var i = 0; i < _entries.Count; i++)
        {
            var source = i == index ? data : GetData(i).ToArray();
            Buffer.BlockCopy(source, 0, rebuilt, write, source.Length);
            _entries[i].Offset = write;
            _entries[i].Size = source.Length;
            write += source.Length;
        }
        _payload = rebuilt;
        if (name is not null)
        {
            _entries[index].Name = name;
            _entries[index].Hash = Pk2Hash.NameHash(name);
        }
        IsDirty = true;
    }

    /// <summary>Writes the archive to disk in the canonical modern layout.</summary>
    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            Span<byte> header = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)_entries.Count);
            stream.Write(header);

            Span<byte> record = stackalloc byte[RecordSize];
            var offset = 0;
            foreach (var entry in _entries)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(record, entry.Hash);
                BinaryPrimitives.WriteUInt32LittleEndian(record[4..], (uint)offset);
                BinaryPrimitives.WriteUInt32LittleEndian(record[8..], (uint)entry.Size);
                stream.Write(record);
                offset += entry.Size;
            }
            stream.Write(_payload);
        }

        _path = path;
        IsDirty = false;
    }

    /// <summary>Serializes the archive, for tests and in-memory workflows.</summary>
    public byte[] ToArray()
    {
        using var stream = new MemoryStream(DataStart + _payload.Length);
        stream.Write(BitConverter.GetBytes((uint)_entries.Count));
        var offset = 0;
        foreach (var entry in _entries)
        {
            stream.Write(BitConverter.GetBytes(entry.Hash));
            stream.Write(BitConverter.GetBytes((uint)offset));
            stream.Write(BitConverter.GetBytes((uint)entry.Size));
            offset += entry.Size;
        }
        stream.Write(_payload);
        return stream.ToArray();
    }
}
