
using System;
using System.IO;
using System.Text;

namespace SaveToolkit
{
    /// <summary>
    /// TextReader that decodes UTF-8 bytes on demand into the caller's
    /// buffer (JsonTextReader's pooled char buffer). Reused per thread.
    /// </summary>
    internal sealed class Utf8MemoryTextReader : TextReader
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        [ThreadStatic] private static Utf8MemoryTextReader cached;

        private readonly Decoder decoder = Utf8.GetDecoder();
        private ReadOnlyMemory<byte> source;
        private int position;
        private int peekedChar = -1;
        private int pendingChar = -1;

        public static Utf8MemoryTextReader Get(ReadOnlyMemory<byte> bytes)
        {
            Utf8MemoryTextReader reader = cached ?? (cached = new Utf8MemoryTextReader());
            reader.decoder.Reset();
            reader.source = bytes;
            reader.peekedChar = -1;
            reader.pendingChar = -1;

            ReadOnlySpan<byte> span = bytes.Span;
            bool hasBom = span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF;
            reader.position = hasBom ? 3 : 0;

            return reader;
        }

        public void Release()
        {
            source = default;
        }

        public override int Read(char[] buffer, int index, int count)
        {
            return Read(new Span<char>(buffer, index, count));
        }

        public override int Read(Span<char> buffer)
        {
            if (buffer.IsEmpty)
                return 0;

            int produced = 0;

            if (peekedChar >= 0)
            {
                buffer[produced++] = (char)peekedChar;
                peekedChar = -1;
            }

            if (pendingChar >= 0 && produced < buffer.Length)
            {
                buffer[produced++] = (char)pendingChar;
                pendingChar = -1;
            }

            ReadOnlySpan<byte> remaining = source.Span.Slice(position);

            // Need room for a surrogate pair; a 1-char request can be served
            // from the pending char next time.
            while (produced < buffer.Length && !remaining.IsEmpty)
            {
                Span<char> target = buffer.Slice(produced);

                if (target.Length < 2)
                {
                    Span<char> pair = stackalloc char[2];
                    decoder.Convert(remaining, pair, remaining.Length <= 4, out int used, out int chars, out _);
                    position += used;
                    remaining = remaining.Slice(used);

                    if (chars > 0)
                    {
                        target[0] = pair[0];
                        produced++;

                        if (chars > 1)
                            pendingChar = pair[1];
                    }

                    break;
                }

                decoder.Convert(remaining, target, true, out int bytesUsed, out int charsUsed, out _);
                position += bytesUsed;
                remaining = remaining.Slice(bytesUsed);
                produced += charsUsed;

                if (bytesUsed == 0 && charsUsed == 0)
                    break;
            }

            return produced;
        }

        public override int Read()
        {
            Span<char> single = stackalloc char[1];
            return Read(single) == 0 ? -1 : single[0];
        }

        public override int Peek()
        {
            if (peekedChar >= 0)
                return peekedChar;

            // Read() may stash the second half of a surrogate pair in
            // pendingChar, so the peeked char gets its own slot.
            int value = Read();

            if (value >= 0)
                peekedChar = value;

            return value;
        }

        protected override void Dispose(bool disposing)
        {
            // Pooled per thread; never actually disposed.
        }
    }
}
