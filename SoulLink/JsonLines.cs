using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;

namespace SoulLink;

public static class JsonLines
{
    /// <summary>Bounds bytes before decoding, including an unterminated malicious line.</summary>
    public static async IAsyncEnumerable<string> ReadAsync(
        Stream stream, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        byte[] buffer = new byte[8192];
        using var line = new MemoryStream();
        var utf8 = new UTF8Encoding(false, true);
        while (true)
        {
            int count = await stream.ReadAsync(buffer, cancellationToken);
            if (count == 0)
            {
                if (line.Length > 0) throw new InvalidDataException("Truncated JSON line.");
                yield break;
            }

            for (int i = 0; i < count; i++)
            {
                if (buffer[i] == 10)
                {
                    string value = utf8.GetString(line.GetBuffer(), 0, (int)line.Length).TrimEnd('\r');
                    line.SetLength(0);
                    if (value.Length > 0) yield return value;
                }
                else
                {
                    if (line.Length >= Protocol.MaxMessageBytes) throw new InvalidDataException("Bridge message exceeds size limit.");
                    line.WriteByte(buffer[i]);
                }
            }
        }
    }
}
