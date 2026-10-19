
using System;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace SaveToolkit
{
    /// <summary>
    /// 32-byte binary header written in front of every save:
    /// magic "STK2" | header version | format | compression | flags |
    /// data version (int32 LE) | payload length (int32 LE) | SHA-256[0..16].
    /// The header records how the payload was written, so a save stays
    /// readable after the active profile changes.
    /// </summary>
    public static class SaveEnvelope
    {
        public const int HeaderSize = 32;

        private const byte HeaderVersion = 1;
        private const int ChecksumOffset = 16;
        private const int ChecksumSize = 16;
        private const byte EncryptedFlag = 1;

        [ThreadStatic] private static SHA256 sha256;

        /// <summary>Writes the header for <paramref name="payload"/> into <paramref name="destination"/>.</summary>
        public static void WriteHeader(Span<byte> destination, SaveEnvelopeHeader header, ReadOnlySpan<byte> payload)
        {
            if (destination.Length < HeaderSize)
                throw new ArgumentException("Destination is too small.", nameof(destination));

            destination[0] = (byte)'S';
            destination[1] = (byte)'T';
            destination[2] = (byte)'K';
            destination[3] = (byte)'2';
            destination[4] = HeaderVersion;
            destination[5] = (byte)header.Format;
            destination[6] = (byte)header.Compression;
            destination[7] = header.Encrypted ? EncryptedFlag : (byte)0;
            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(8), header.DataVersion);
            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(12), payload.Length);
            ComputeChecksum(payload, destination.Slice(ChecksumOffset, ChecksumSize));
        }

        public static bool HasMagic(ReadOnlySpan<byte> data)
        {
            return data.Length >= 4 &&
                   data[0] == 'S' && data[1] == 'T' && data[2] == 'K' && data[3] == '2';
        }

        /// <summary>Validates the header and checksum and returns the payload slice.</summary>
        /// <exception cref="SaveCorruptedException">Header or checksum is invalid.</exception>
        public static ArraySegment<byte> Read(ArraySegment<byte> data, out SaveEnvelopeHeader header)
        {
            ReadOnlySpan<byte> span = data.AsSpan();

            if (!HasMagic(span) || span.Length < HeaderSize)
                throw new SaveCorruptedException("Save header is missing or truncated.");

            if (span[4] != HeaderVersion)
                throw new SaveCorruptedException($"Unsupported save header version {span[4]}.");

            var format = (SaveSerializationFormat)span[5];
            var compression = (SaveCompressionType)span[6];
            int dataVersion = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(8));
            int payloadLength = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(12));

            if (format != SaveSerializationFormat.Json && format != SaveSerializationFormat.MessagePack ||
                compression != SaveCompressionType.None && compression != SaveCompressionType.GZip)
                throw new SaveCorruptedException("Save header has unknown values.");

            if (payloadLength < 0 || payloadLength != span.Length - HeaderSize)
                throw new SaveCorruptedException("Save payload length mismatch.");

            ReadOnlySpan<byte> payload = span.Slice(HeaderSize);
            Span<byte> actual = stackalloc byte[ChecksumSize];
            ComputeChecksum(payload, actual);

            if (!actual.SequenceEqual(span.Slice(ChecksumOffset, ChecksumSize)))
                throw new SaveCorruptedException("Save checksum mismatch.");

            header = new SaveEnvelopeHeader(format, compression, (span[7] & EncryptedFlag) != 0, dataVersion);
            return new ArraySegment<byte>(data.Array, data.Offset + HeaderSize, payloadLength);
        }

        /// <summary>Allocating convenience overload (tools and tests).</summary>
        public static byte[] Write(SaveEnvelopeHeader header, byte[] payload)
        {
            byte[] result = new byte[HeaderSize + payload.Length];
            WriteHeader(result, header, payload);
            Buffer.BlockCopy(payload, 0, result, HeaderSize, payload.Length);
            return result;
        }

        /// <summary>Allocating convenience overload (tools and tests).</summary>
        public static byte[] Read(byte[] data, out SaveEnvelopeHeader header)
        {
            return Read(new ArraySegment<byte>(data), out header).AsSpan().ToArray();
        }

        private static void ComputeChecksum(ReadOnlySpan<byte> payload, Span<byte> destination)
        {
            SHA256 sha = sha256 ?? (sha256 = SHA256.Create());
            Span<byte> hash = stackalloc byte[32];

            sha.TryComputeHash(payload, hash, out _);
            hash.Slice(0, destination.Length).CopyTo(destination);
        }
    }
}
