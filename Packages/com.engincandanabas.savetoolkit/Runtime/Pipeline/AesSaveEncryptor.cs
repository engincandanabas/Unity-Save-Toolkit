
using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace SaveToolkit
{
    public interface ISaveEncryptor
    {
        void Encrypt(ArraySegment<byte> plain, PooledBufferWriter output);

        /// <exception cref="SaveCorruptedException">Authentication or decryption failed.</exception>
        void Decrypt(ArraySegment<byte> data, PooledBufferWriter output);
    }

    /// <summary>
    /// AES-256-CBC with a random IV per write, authenticated with
    /// HMAC-SHA256 (encrypt-then-MAC). Layout: [IV 16][cipher][MAC 32].
    /// Keys are derived from the profile passphrase with PBKDF2, so save
    /// files stay portable across devices (Steam Cloud friendly).
    /// Aes / HMAC instances are created once per thread and reused.
    /// </summary>
    public sealed class AesSaveEncryptor : ISaveEncryptor
    {
        private const int KeySize = 32;
        private const int IvSize = 16;
        private const int MacSize = 32;
        private const int BlockSize = 16;

        // The passphrase ships inside the build, so a high iteration count
        // buys little security while costing seconds on low-end phones.
        private const int Pbkdf2Iterations = 10_000;

        private static readonly ConcurrentDictionary<string, byte[]> KeyCache =
            new ConcurrentDictionary<string, byte[]>();

        private static readonly RandomNumberGenerator Random = RandomNumberGenerator.Create();

        private readonly byte[] encryptionKey;
        private readonly byte[] macKey;
        private readonly ThreadLocal<CryptoState> state;

        public AesSaveEncryptor(string passphrase, string salt)
        {
            if (string.IsNullOrEmpty(passphrase))
                throw new ArgumentException("Passphrase is required.", nameof(passphrase));

            if (string.IsNullOrEmpty(salt))
                throw new ArgumentException("Salt is required.", nameof(salt));

            byte[] material = KeyCache.GetOrAdd(
                passphrase + "\0" + salt,
                _ => DeriveKeys(passphrase, salt)
            );

            encryptionKey = new byte[KeySize];
            macKey = new byte[KeySize];
            Buffer.BlockCopy(material, 0, encryptionKey, 0, KeySize);
            Buffer.BlockCopy(material, KeySize, macKey, 0, KeySize);

            state = new ThreadLocal<CryptoState>(() => new CryptoState(macKey));
        }

        public void Encrypt(ArraySegment<byte> plain, PooledBufferWriter output)
        {
            CryptoState s = state.Value;
            int cipherLength = (plain.Count / BlockSize + 1) * BlockSize;
            ArraySegment<byte> destination = output.GetSegment(IvSize + cipherLength + MacSize);
            byte[] target = destination.Array;
            int start = destination.Offset;

            lock (Random)
                Random.GetBytes(s.Iv);

            Buffer.BlockCopy(s.Iv, 0, target, start, IvSize);

            int written = 0;

            using (ICryptoTransform encryptor = s.Aes.CreateEncryptor(encryptionKey, s.Iv))
            {
                int fullBlocks = plain.Count - plain.Count % BlockSize;

                if (fullBlocks > 0)
                    written = encryptor.TransformBlock(
                        plain.Array, plain.Offset, fullBlocks,
                        target, start + IvSize);

                byte[] last = encryptor.TransformFinalBlock(
                    plain.Array, plain.Offset + fullBlocks, plain.Count - fullBlocks);

                Buffer.BlockCopy(last, 0, target, start + IvSize + written, last.Length);
                written += last.Length;
            }

            int signedLength = IvSize + written;
            s.Hmac.TryComputeHash(
                new ReadOnlySpan<byte>(target, start, signedLength),
                new Span<byte>(target, start + signedLength, MacSize),
                out _);

            output.Advance(signedLength + MacSize);
        }

        public void Decrypt(ArraySegment<byte> data, PooledBufferWriter output)
        {
            if (data.Count < IvSize + BlockSize + MacSize)
                throw new SaveCorruptedException("Encrypted payload is too short.");

            CryptoState s = state.Value;
            int signedLength = data.Count - MacSize;
            int cipherLength = signedLength - IvSize;

            if (cipherLength % BlockSize != 0)
                throw new SaveCorruptedException("Encrypted payload has an invalid length.");

            Span<byte> expectedMac = stackalloc byte[MacSize];
            s.Hmac.TryComputeHash(
                new ReadOnlySpan<byte>(data.Array, data.Offset, signedLength),
                expectedMac,
                out _);

            if (!FixedTimeEquals(expectedMac, new ReadOnlySpan<byte>(data.Array, data.Offset + signedLength, MacSize)))
                throw new SaveCorruptedException(
                    "Encrypted payload failed authentication " +
                    "(tampered data or wrong passphrase).");

            Buffer.BlockCopy(data.Array, data.Offset, s.Iv, 0, IvSize);

            try
            {
                using (ICryptoTransform decryptor = s.Aes.CreateDecryptor(encryptionKey, s.Iv))
                {
                    ArraySegment<byte> destination = output.GetSegment(cipherLength);
                    int cipherStart = data.Offset + IvSize;
                    int head = cipherLength - BlockSize;
                    int written = 0;

                    // Decryptors may hold back the last block for padding
                    // removal; only trust the counts they return.
                    if (head > 0)
                        written = decryptor.TransformBlock(
                            data.Array, cipherStart, head,
                            destination.Array, destination.Offset);

                    byte[] tail = decryptor.TransformFinalBlock(data.Array, cipherStart + head, BlockSize);
                    Buffer.BlockCopy(tail, 0, destination.Array, destination.Offset + written, tail.Length);

                    output.Advance(written + tail.Length);
                }
            }
            catch (CryptographicException ex)
            {
                throw new SaveCorruptedException("Decryption failed.", ex);
            }
        }

        private static bool FixedTimeEquals(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
        {
            int diff = 0;

            for (int i = 0; i < MacSize; i++)
                diff |= left[i] ^ right[i];

            return diff == 0;
        }

        private static byte[] DeriveKeys(string passphrase, string salt)
        {
            using (var pbkdf2 = new Rfc2898DeriveBytes(
                       passphrase,
                       Encoding.UTF8.GetBytes(salt),
                       Pbkdf2Iterations,
                       HashAlgorithmName.SHA256))
            {
                return pbkdf2.GetBytes(KeySize * 2);
            }
        }

        private sealed class CryptoState
        {
            public readonly Aes Aes;
            public readonly HMACSHA256 Hmac;
            public readonly byte[] Iv = new byte[IvSize];

            public CryptoState(byte[] macKey)
            {
                Aes = Aes.Create();
                Aes.KeySize = KeySize * 8;
                Aes.Mode = CipherMode.CBC;
                Aes.Padding = PaddingMode.PKCS7;
                Hmac = new HMACSHA256(macKey);
            }
        }
    }
}
