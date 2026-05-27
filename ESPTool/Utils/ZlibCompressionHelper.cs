using System.IO.Compression;

namespace EspDotNet.Utils
{

    public class ZlibCompressionHelper
    {
        private const uint ModAdler = 65521;

        public static void CompressToZlibStream(Stream inputStream, Stream compressedStream)
        {
            // Write the zlib header (0x78, 0x9C for default compression)
            compressedStream.WriteByte(0x78);
            compressedStream.WriteByte(0x9C);

            // Compress and checksum in a single pass over the input so the input stream does not
            // need to be seekable (it is read forward exactly once).
            uint a = 1, b = 0;
            using (DeflateStream deflateStream = new DeflateStream(compressedStream, CompressionLevel.Optimal, leaveOpen: true))
            {
                byte[] buffer = new byte[8192];
                int bytesRead;
                while ((bytesRead = inputStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    UpdateAdler32(ref a, ref b, buffer, bytesRead);
                    deflateStream.Write(buffer, 0, bytesRead);
                }
            }

            // Write the zlib footer (Adler-32 checksum of the uncompressed data, big-endian)
            uint checksum = b << 16 | a;
            compressedStream.WriteByte((byte)(checksum >> 24 & 0xFF));
            compressedStream.WriteByte((byte)(checksum >> 16 & 0xFF));
            compressedStream.WriteByte((byte)(checksum >> 8 & 0xFF));
            compressedStream.WriteByte((byte)(checksum & 0xFF));
        }

        // Incrementally folds a chunk of uncompressed data into a running Adler-32 checksum.
        private static void UpdateAdler32(ref uint a, ref uint b, byte[] data, int count)
        {
            for (int i = 0; i < count; i++)
            {
                a = (a + data[i]) % ModAdler;
                b = (b + a) % ModAdler;
            }
        }
    }

}
