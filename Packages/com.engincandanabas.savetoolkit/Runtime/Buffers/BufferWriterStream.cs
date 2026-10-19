
using System;
using System.Buffers;
using System.IO;

namespace SaveToolkit
{
    /// <summary>Write-only <see cref="Stream"/> over an <see cref="IBufferWriter{T}"/>.</summary>
    internal sealed class BufferWriterStream : Stream
    {
        private IBufferWriter<byte> output;

        public BufferWriterStream(IBufferWriter<byte> output)
        {
            this.output = output;
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public void Reset(IBufferWriter<byte> writer)
        {
            output = writer;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Write(new ReadOnlySpan<byte>(buffer, offset, count));
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            buffer.CopyTo(output.GetSpan(buffer.Length));
            output.Advance(buffer.Length);
        }

        public override void WriteByte(byte value)
        {
            output.GetSpan(1)[0] = value;
            output.Advance(1);
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
