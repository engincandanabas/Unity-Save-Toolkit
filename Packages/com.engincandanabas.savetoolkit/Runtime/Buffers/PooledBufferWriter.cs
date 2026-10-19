
using System;
using System.Buffers;

namespace SaveToolkit
{
    /// <summary>
    /// Growable byte buffer rented from <see cref="ArrayPool{T}.Shared"/>.
    /// Every pipeline stage writes into one of these instead of allocating
    /// a new array. Dispose returns the array to the pool.
    /// </summary>
    public sealed class PooledBufferWriter : IBufferWriter<byte>, IDisposable
    {
        private const int MinimumCapacity = 256;

        private readonly bool clearOnReturn;
        private byte[] buffer;
        private int written;

        /// <param name="initialCapacity">Expected size; the buffer grows as needed.</param>
        /// <param name="clearOnReturn">Zero the array before returning it (use for plaintext).</param>
        public PooledBufferWriter(int initialCapacity = MinimumCapacity, bool clearOnReturn = false)
        {
            this.clearOnReturn = clearOnReturn;
            buffer = ArrayPool<byte>.Shared.Rent(Math.Max(initialCapacity, MinimumCapacity));
        }

        public int WrittenCount => written;

        public ReadOnlySpan<byte> WrittenSpan => new ReadOnlySpan<byte>(ThrowIfDisposed(), 0, written);

        public ReadOnlyMemory<byte> WrittenMemory => new ReadOnlyMemory<byte>(ThrowIfDisposed(), 0, written);

        public ArraySegment<byte> WrittenSegment => new ArraySegment<byte>(ThrowIfDisposed(), 0, written);

        public void Advance(int count)
        {
            if (count < 0 || written + count > ThrowIfDisposed().Length)
                throw new ArgumentOutOfRangeException(nameof(count));

            written += count;
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return buffer.AsMemory(written);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return buffer.AsSpan(written);
        }

        /// <summary>Free space as an array segment, for APIs that need arrays (crypto transforms).</summary>
        public ArraySegment<byte> GetSegment(int sizeHint)
        {
            EnsureCapacity(sizeHint);
            return new ArraySegment<byte>(buffer, written, buffer.Length - written);
        }

        public void Write(ReadOnlySpan<byte> data)
        {
            EnsureCapacity(data.Length);
            data.CopyTo(buffer.AsSpan(written));
            written += data.Length;
        }

        public void Clear()
        {
            written = 0;
        }

        public void Dispose()
        {
            if (buffer == null)
                return;

            ArrayPool<byte>.Shared.Return(buffer, clearOnReturn);
            buffer = null;
            written = 0;
        }

        private void EnsureCapacity(int sizeHint)
        {
            ThrowIfDisposed();

            if (sizeHint < 1)
                sizeHint = 1;

            if (buffer.Length - written >= sizeHint)
                return;

            int newSize = Math.Max(buffer.Length * 2, written + sizeHint);
            byte[] larger = ArrayPool<byte>.Shared.Rent(newSize);

            Buffer.BlockCopy(buffer, 0, larger, 0, written);
            ArrayPool<byte>.Shared.Return(buffer, clearOnReturn);
            buffer = larger;
        }

        private byte[] ThrowIfDisposed()
        {
            return buffer ?? throw new ObjectDisposedException(nameof(PooledBufferWriter));
        }
    }
}
