using System.Text;

namespace Pk2Mixer.Core.Assets;

/// <summary>Hex + ASCII dump used by the preview pane for opaque payloads.</summary>
public static class HexDump
{
    public static string Format(byte[] data, int maxBytes = 8192, int startOffset = 0)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0)
        {
            return "(empty)";
        }

        var count = Math.Min(data.Length - startOffset, Math.Max(0, maxBytes));
        var builder = new StringBuilder(count * 4 + 128);
        for (var row = 0; row < count; row += 16)
        {
            var offset = startOffset + row;
            builder.Append(offset.ToString("x8")).Append("  ");

            var lineBytes = Math.Min(16, count - row);
            for (var i = 0; i < 16; i++)
            {
                if (i < lineBytes)
                {
                    builder.Append(data[offset + i].ToString("x2")).Append(' ');
                }
                else
                {
                    builder.Append("   ");
                }
                if (i == 7)
                {
                    builder.Append(' ');
                }
            }

            builder.Append(" |");
            for (var i = 0; i < lineBytes; i++)
            {
                var b = data[offset + i];
                builder.Append(b is >= 0x20 and < 0x7f ? (char)b : '.');
            }
            builder.Append('|').AppendLine();
        }

        if (startOffset + count < data.Length)
        {
            builder.AppendLine($"… {data.Length - startOffset - count:N0} more byte(s)");
        }
        return builder.ToString();
    }
}
