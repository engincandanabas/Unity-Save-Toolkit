
using System;
using System.Collections.Generic;

namespace SaveToolkit
{
    /// <summary>
    /// Byte storage keyed by relative, '/'-separated paths
    /// (e.g. "slot_0/player.sav"). Implement this for cloud backends.
    /// Calls are serialized by SaveManager; implementations need not lock.
    /// </summary>
    public interface ISaveStorage
    {
        /// <summary>True when every call must run on the Unity main thread.</summary>
        bool RequiresMainThread { get; }

        /// <summary>
        /// Atomically replaces <paramref name="path"/> with header + payload:
        /// readers see the old or the new bytes, never a mix. When
        /// <paramref name="backupPath"/> is set and the file exists, the old
        /// file ends up at <paramref name="backupPath"/>.
        /// </summary>
        void Write(string path, ReadOnlySpan<byte> header, ReadOnlySpan<byte> payload, string backupPath);

        /// <summary>Appends the file's bytes to <paramref name="destination"/>. False if missing.</summary>
        bool TryRead(string path, PooledBufferWriter destination);

        bool Exists(string path);

        void Delete(string path);

        /// <summary>Moves, overwriting the destination.</summary>
        void Move(string sourcePath, string destinationPath);

        bool TryGetLastWriteTimeUtc(string path, out DateTime lastWriteUtc);

        /// <summary>File names directly inside a directory. Empty if missing.</summary>
        IReadOnlyList<string> List(string directory);

        /// <summary>Deletes a directory recursively. Empty string means everything.</summary>
        void DeleteDirectory(string directory);
    }
}
