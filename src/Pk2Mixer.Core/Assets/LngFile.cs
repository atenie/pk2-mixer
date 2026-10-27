using System.Text;

namespace Pk2Mixer.Core.Assets;

public sealed record LngEntry(uint Id, string Text);

/// <summary>
/// Parser for RT2 <c>.lng</c> localization files (spec 03). Plain CRLF text;
/// lines beginning with <c>;</c> are comments and data lines are
/// <c>&lt;id&gt; "&lt;text&gt;"</c>. Malformed lines are skipped, never fatal.
/// </summary>
public sealed class LngFile
{
    private static readonly Encoding Latin1 = Encoding.Latin1;

    private LngFile(List<LngEntry> entries) => Entries = entries;

    public IReadOnlyList<LngEntry> Entries { get; }

    public int Count => Entries.Count;

    public static LngFile Parse(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Parse(Latin1.GetString(data));
    }

    public static LngFile Parse(string text)
    {
        var entries = new List<LngEntry>();
        var pos = 0;
        while (pos < text.Length)
        {
            var end = pos;
            while (end < text.Length && text[end] != '\r' && text[end] != '\n')
            {
                end++;
            }
            var line = text.AsSpan(pos, end - pos);
            pos = end;
            while (pos < text.Length && (text[pos] == '\r' || text[pos] == '\n'))
            {
                pos++;
            }

            var s = 0;
            while (s < line.Length && (line[s] == ' ' || line[s] == '\t'))
            {
                s++;
            }
            if (s >= line.Length || line[s] == ';')
            {
                continue;
            }

            var d = s;
            while (d < line.Length && line[d] >= '0' && line[d] <= '9')
            {
                d++;
            }
            if (d == s)
            {
                continue;
            }

            var id = 0u;
            for (var k = s; k < d; k++)
            {
                id = unchecked(id * 10u + (uint)(line[k] - '0'));
            }

            var rest = line[d..];
            var quote = rest.IndexOf('"');
            if (quote < 0)
            {
                continue;
            }
            rest = rest[(quote + 1)..];

            var close = -1;
            for (var i = 0; i < rest.Length; i++)
            {
                if (rest[i] == '\\')
                {
                    i++;
                    continue;
                }
                if (rest[i] == '"')
                {
                    close = i;
                    break;
                }
            }
            if (close < 0)
            {
                continue;
            }

            entries.Add(new LngEntry(id, Unescape(rest[..close].ToString())));
        }
        return new LngFile(entries);
    }

    public static string Unescape(string body)
    {
        var builder = new StringBuilder(body.Length);
        for (var i = 0; i < body.Length; i++)
        {
            var c = body[i];
            if (c == '\\' && i + 1 < body.Length)
            {
                switch (body[++i])
                {
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case '\\': builder.Append('\\'); break;
                    case '"': builder.Append('"'); break;
                    default:
                        builder.Append('\\');
                        builder.Append(body[i]);
                        break;
                }
            }
            else
            {
                builder.Append(c);
            }
        }
        return builder.ToString();
    }

    /// <summary>
    /// Lenient detection: quoted id/string lines and little binary noise. The
    /// payload must actually yield at least one entry, so binary tables that
    /// happen to contain a quote byte are not mistaken for localization files.
    /// </summary>
    public static bool LooksLike(byte[] data)
    {
        if (data.Length < 8)
        {
            return false;
        }
        var sampleLength = Math.Min(data.Length, 16 * 1024);
        var quote = 0;
        var control = 0;
        for (var i = 0; i < sampleLength; i++)
        {
            var b = data[i];
            if (b == (byte)'"')
            {
                quote++;
            }
            else if (b < 0x09 && b != 0)
            {
                control++;
            }
        }
        if (quote < 2 || control * 100 >= sampleLength)
        {
            return false;
        }
        return Parse(data).Count > 0;
    }
}
