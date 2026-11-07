using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace LenovoLegionToolkit.CLI.Lib.Extensions;

public static class PipeStreamExtensions
{
    private static readonly Encoding Encoding = Encoding.UTF8;

    /// <summary>
    /// Upper bound for a single message. Real requests/responses are a few KB at most.
    /// </summary>
    internal const int MaxMessageBytes = 1024 * 1024;

    public static async Task WriteObjectAsync<T>(this PipeStream stream, T obj, CancellationToken token = default)
    {
        if (stream.ReadMode != PipeTransmissionMode.Message)
            throw new InvalidOperationException("ReadMode is not PipeTransmissionMode.Message");

        var str = JsonConvert.SerializeObject(obj);
        var bytes = Encoding.GetBytes(str);
        await stream.WriteAsync(bytes, token).ConfigureAwait(false);
    }

    public static async Task<T?> ReadObjectAsync<T>(this PipeStream stream, CancellationToken token = default)
    {
        if (stream.ReadMode != PipeTransmissionMode.Message)
            throw new InvalidOperationException("ReadMode is not PipeTransmissionMode.Message");

        var buffer = new byte[1024];
        using var message = new MemoryStream();

        do
        {
            var read = await stream.ReadAsync(buffer, token).ConfigureAwait(false);

            // 0 bytes before the message is complete means the other side went away.
            if (read == 0)
                throw new IOException("Pipe closed before the message was complete.");

            if (message.Length + read > MaxMessageBytes)
                throw new InvalidDataException($"Message exceeds {MaxMessageBytes} bytes.");

            // Only the bytes actually read; the rest of the buffer holds stale data from earlier chunks.
            message.Write(buffer, 0, read);
        } while (!stream.IsMessageComplete);

        // Decode once at the end so multi-byte UTF-8 characters split across chunks stay intact.
        var json = Encoding.GetString(message.GetBuffer(), 0, (int)message.Length);
        return JsonConvert.DeserializeObject<T>(json);
    }
}
