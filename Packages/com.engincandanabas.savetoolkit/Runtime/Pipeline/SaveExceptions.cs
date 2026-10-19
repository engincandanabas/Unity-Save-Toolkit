
using System;

namespace SaveToolkit
{
    /// <summary>
    /// Save bytes failed an integrity check (bad header, checksum, HMAC,
    /// decryption or decompression). Triggers backup recovery.
    /// </summary>
    public sealed class SaveCorruptedException : Exception
    {
        public SaveCorruptedException(string message, Exception inner = null)
            : base(message, inner) { }
    }

    /// <summary>
    /// Save was written by a newer DataVersion than this build knows.
    /// Not treated as corruption; the file is left untouched.
    /// </summary>
    public sealed class SaveVersionException : Exception
    {
        public SaveVersionException(string message)
            : base(message) { }
    }
}
