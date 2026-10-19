
using System;
using System.Buffers;
using System.IO;
using System.Text;

namespace SaveToolkit
{
    /// <summary>
    /// TextWriter that UTF-8 encodes straight into an <see cref="IBufferWriter{T}"/>,
    /// so JSON is never materialized as a string. Reused per thread.
    /// </summary>
    internal sealed class Utf8BufferTextWriter : TextWriter
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        [ThreadStatic] private static Utf8BufferTextWriter cached;

        private readonly Encoder encoder = Utf8.GetEncoder();
        private IBufferWriter<byte> output;

        public override Encoding Encoding => Utf8;

        /// <summary>Returns the thread's writer bound to <paramref name="destination"/>.</summary>
        public static Utf8BufferTextWriter Get(IBufferWriter<byte> destination)
        {
            Utf8BufferTextWriter writer = cached ?? (cached = new Utf8BufferTextWriter());
            writer.output = destination;
            writer.encoder.Reset();
            return writer;
        }

        public void Release()
        {
            Flush();
            output = null;
        }

        public override void Write(char value)
        {
            Span<char> single = stackalloc char[1];
            single[0] = value;
            Write((ReadOnlySpan<char>)single);
        }

        public override void Write(char[] buffer, int index, int count)
        {
            Write(new ReadOnlySpan<char>(buffer, index, count));
        }

        public override void Write(string value)
        {
            if (value != null)
                Write(value.AsSpan());
        }

        public override void Write(ReadOnlySpan<char> chars)
        {
            if (chars.IsEmpty)
                return;

            Span<byte> destination = output.GetSpan(Utf8.GetMaxByteCount(chars.Length));
            int bytes = encoder.GetBytes(chars, destination, false);
            output.Advance(bytes);
        }

        public override void Flush()
        {
            if (output == null)
                return;

            Span<byte> destination = output.GetSpan(8);
            int bytes = encoder.GetBytes(ReadOnlySpan<char>.Empty, destination, true);
            output.Advance(bytes);
        }

        protected override void Dispose(bool disposing)
        {
            // Pooled per thread; never actually disposed.
        }
    }
}
