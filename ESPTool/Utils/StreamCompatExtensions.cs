#if NETFRAMEWORK
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace EspDotNet.Utils
{
    // Stream.ReadExactlyAsync was added in .NET 7; this polyfills it for net472.
    internal static class StreamCompatExtensions
    {
        public static async Task ReadExactlyAsync(this Stream stream, Memory<byte> buffer, CancellationToken token)
        {
            int totalRead = 0;
            while (totalRead < buffer.Length)
            {
                byte[] temp = new byte[buffer.Length - totalRead];
                int read = await stream.ReadAsync(temp, 0, temp.Length, token).ConfigureAwait(false);
                if (read == 0)
                    throw new EndOfStreamException();

                temp.AsSpan(0, read).CopyTo(buffer.Span.Slice(totalRead));
                totalRead += read;
            }
        }
    }
}
#endif
