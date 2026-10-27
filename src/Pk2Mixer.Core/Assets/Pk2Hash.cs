namespace Pk2Mixer.Core.Assets;

/// <summary>
/// PK2 name hashing, recovered from <c>RT2_PLAT.EXE</c>
/// (<c>FUN_004f0d70</c> fold + <c>FUN_004f48e0</c>/<c>FUN_004f4e60</c> accumulators)
/// and documented in <c>spec/01-archive-pk2.md</c>.
/// </summary>
public static class Pk2Hash
{
    /// <summary>DOS-style uppercase fold: ASCII a..z and CP437 0xf0..0xff.</summary>
    public static byte Fold(byte c) =>
        ((c > 0x60 && c < 0x7b) || c > 0xef) ? (byte)(c - 0x20) : c;

    /// <summary>
    /// Primary hash used by the archive index: forward scan, MSVC <c>rand()</c>
    /// LCG seeded with 7, each folded byte sign-extended.
    /// </summary>
    public static uint NameHash(ReadOnlySpan<char> name)
    {
        uint h = 7u;
        foreach (var ch in name)
        {
            var folded = Fold((byte)ch);
            var v = unchecked((uint)(sbyte)folded);
            h = unchecked(h * 0x41c64e6eu + 0x3039u + v);
        }
        return h;
    }

    public static uint NameHash(string name) => NameHash(name.AsSpan());

    /// <summary>Secondary accumulator the engine's cached asset table also accepts.</summary>
    public static uint NameHashAlt(ReadOnlySpan<char> name)
    {
        uint sum = 0u;
        uint h = 0u;
        for (var i = name.Length - 1; i >= 0; --i)
        {
            var folded = Fold((byte)name[i]);
            var v = unchecked((uint)(sbyte)folded);
            sum = unchecked(sum + v);
            h = unchecked(RotateLeft(h, 7) + v + sum);
        }
        return h;
    }

    public static uint NameHashAlt(string name) => NameHashAlt(name.AsSpan());

    private static uint RotateLeft(uint value, int bits) =>
        (value << bits) | (value >> (32 - bits));
}
