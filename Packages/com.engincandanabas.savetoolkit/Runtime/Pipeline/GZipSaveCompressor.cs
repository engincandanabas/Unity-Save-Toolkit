
using System;
using System.IO;
using System.IO.Compression;

namespace SaveToolkit
{
    public interface ISaveCompressor
    {
        void Compress(ArraySegment<byte> input, PooledBufferWriter output);

        /// <exception cref="SaveCorruptedException">The data is not valid compressed data.</exception>
        void Decompress(ArraySegment<byte> input, PooledBufferWriter output);
    }

    public sealed class GZipSaveCompressor : ISaveCompressor
    {
        private const int ReadChunk = 4096;

        private readonly CompressionLevel level;

        /// <param name="level">Fastest by default: save files are small and written often.</param>
        public GZipSaveCompressor(CompressionLevel level = CompressionLevel.Fastest)
        {
            this.level = level;
        }

        public void Compress(ArraySegment<byte> input, PooledBufferWriter output)
        {
            using (var gzip = new GZipStream(new BufferWriterStream(output), level, true))
            {
                gzip.Write(input.Array, input.Offset, input.Count);
            }
        }

        public void Decompress(ArraySegment<byte> input, PooledBufferWriter output)
        {
            try
            {
                using (var source = new MemoryStream(input.Array, input.Offset, input.Count, false))
                using (var gzip = new GZipStream(source, CompressionMode.Decompress))
                {
                    while (true)
                    {
                        int read = gzip.Read(output.GetSpan(ReadChunk));

                        if (read == 0)
                            break;

                        output.Advance(read);
                    }
                }
            }
            catch (InvalidDataException ex)
            {
                throw new SaveCorruptedException("Decompression failed.", ex);
            }
        }
    }
}
