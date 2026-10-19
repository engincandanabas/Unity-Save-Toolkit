
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

namespace SaveToolkit
{
    /// <summary>
    /// Files under a root directory. Writes go to a temp file that atomically
    /// replaces the target; File.Replace moves the old file to the backup
    /// path in the same operation, so backups cost no copy.
    /// </summary>
    /// <remarks>
    /// Under Unity's Mono every File.Exists / Directory.CreateDirectory call
    /// allocates ~1-2.5 KB, so this class remembers which files and folders it
    /// has seen. Files deleted behind its back are handled when an operation
    /// finds them missing.
    /// </remarks>
    public sealed class FileSaveStorage : ISaveStorage
    {
        private const string TempExtension = ".tmp";

        private readonly bool flushToDisk;
        private readonly ConcurrentDictionary<string, PathInfo> paths =
            new ConcurrentDictionary<string, PathInfo>();
        private readonly HashSet<string> knownFiles = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> createdDirectories = new HashSet<string>(StringComparer.Ordinal);

        /// <param name="rootDirectory">Absolute root folder.</param>
        /// <param name="flushToDisk">
        /// Force data to the physical disk on every write. Survives power loss
        /// and OS crashes, but costs extra time per save.
        /// </param>
        public FileSaveStorage(string rootDirectory, bool flushToDisk = true)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
                throw new ArgumentException(
                    "Root directory cannot be empty.",
                    nameof(rootDirectory)
                );

            RootDirectory = rootDirectory;
            this.flushToDisk = flushToDisk;
        }

        public string RootDirectory { get; }

        public bool RequiresMainThread => false;

        public void Write(string path, ReadOnlySpan<byte> header, ReadOnlySpan<byte> payload, string backupPath)
        {
            PathInfo info = GetInfo(path);

            EnsureDirectory(info);

            try
            {
                try
                {
                    WriteTemp(info.Temp, header, payload);
                }
                catch (DirectoryNotFoundException)
                {
                    // Folder removed behind our back: recreate once.
                    ForgetDirectory(info.Directory);
                    EnsureDirectory(info);
                    WriteTemp(info.Temp, header, payload);
                }

                bool replaced = false;

                if (FileExists(path, info))
                {
                    try
                    {
                        Replace(info.Full, info.Temp, backupPath != null ? GetInfo(backupPath).Full : null);
                        replaced = true;
                    }
                    catch (FileNotFoundException)
                    {
                        Forget(path);
                    }
                }

                if (!replaced)
                    File.Move(info.Temp, info.Full);

                Remember(path);

                if (replaced && backupPath != null)
                    Remember(backupPath);
            }
            catch
            {
                TryDeleteFile(info.Temp);
                throw;
            }
        }

        public bool TryRead(string path, PooledBufferWriter destination)
        {
            PathInfo info = GetInfo(path);

            if (!FileExists(path, info))
                return false;

            try
            {
                using (var stream = new FileStream(
                           info.Full,
                           FileMode.Open,
                           FileAccess.Read,
                           FileShare.Read,
                           1,
                           FileOptions.SequentialScan))
                {
                    int remaining = (int)stream.Length;
                    Span<byte> span = destination.GetSpan(remaining);

                    while (remaining > 0)
                    {
                        int read = stream.Read(span.Slice(0, remaining));

                        if (read == 0)
                            break;

                        destination.Advance(read);
                        span = span.Slice(read);
                        remaining -= read;
                    }
                }

                return true;
            }
            catch (Exception ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException)
            {
                Forget(path);
                return false;
            }
        }

        public bool Exists(string path)
        {
            return FileExists(path, GetInfo(path));
        }

        public void Delete(string path)
        {
            PathInfo info = GetInfo(path);

            if (FileExists(path, info))
                File.Delete(info.Full);

            Forget(path);
        }

        public void Move(string sourcePath, string destinationPath)
        {
            PathInfo source = GetInfo(sourcePath);
            PathInfo destination = GetInfo(destinationPath);

            EnsureDirectory(destination);

            if (FileExists(destinationPath, destination))
                File.Delete(destination.Full);

            try
            {
                File.Move(source.Full, destination.Full);
            }
            catch (FileNotFoundException)
            {
                // Source vanished (deleted outside the game): nothing to move.
                Forget(sourcePath);
                Forget(destinationPath);
                return;
            }

            Forget(sourcePath);
            Remember(destinationPath);
        }

        public bool TryGetLastWriteTimeUtc(string path, out DateTime lastWriteUtc)
        {
            PathInfo info = GetInfo(path);

            if (!FileExists(path, info))
            {
                lastWriteUtc = default;
                return false;
            }

            lastWriteUtc = File.GetLastWriteTimeUtc(info.Full);
            return true;
        }

        public IReadOnlyList<string> List(string directory)
        {
            string fullPath = GetDirectoryPath(directory);

            if (!Directory.Exists(fullPath))
                return Array.Empty<string>();

            string[] files = Directory.GetFiles(fullPath);

            for (int i = 0; i < files.Length; i++)
                files[i] = Path.GetFileName(files[i]);

            return files;
        }

        public void DeleteDirectory(string directory)
        {
            string fullPath = GetDirectoryPath(directory);

            if (Directory.Exists(fullPath))
                Directory.Delete(fullPath, true);

            lock (knownFiles)
            {
                knownFiles.Clear();
                createdDirectories.Clear();
            }
        }

        private static void WriteTemp(string tempPath, ReadOnlySpan<byte> header, ReadOnlySpan<byte> payload, bool flush)
        {
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 1))
            {
                stream.Write(header);
                stream.Write(payload);
                stream.Flush(flush);
            }
        }

        private void WriteTemp(string tempPath, ReadOnlySpan<byte> header, ReadOnlySpan<byte> payload)
        {
            WriteTemp(tempPath, header, payload, flushToDisk);
        }

        private bool FileExists(string path, PathInfo info)
        {
            lock (knownFiles)
            {
                if (knownFiles.Contains(path))
                    return true;
            }

            if (!File.Exists(info.Full))
                return false;

            Remember(path);
            return true;
        }

        private void Remember(string path)
        {
            lock (knownFiles)
                knownFiles.Add(path);
        }

        private void Forget(string path)
        {
            lock (knownFiles)
                knownFiles.Remove(path);
        }

        private void EnsureDirectory(PathInfo info)
        {
            lock (knownFiles)
            {
                if (createdDirectories.Contains(info.Directory))
                    return;
            }

            Directory.CreateDirectory(info.Directory);

            lock (knownFiles)
                createdDirectories.Add(info.Directory);
        }

        private void ForgetDirectory(string directory)
        {
            lock (knownFiles)
                createdDirectories.Remove(directory);
        }

        private PathInfo GetInfo(string relativePath)
        {
            if (paths.TryGetValue(relativePath, out PathInfo info))
                return info;

            string full = Path.Combine(RootDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            info = new PathInfo(full, full + TempExtension, Path.GetDirectoryName(full));
            paths[relativePath] = info;
            return info;
        }

        private string GetDirectoryPath(string relativePath)
        {
            return string.IsNullOrEmpty(relativePath)
                ? RootDirectory
                : Path.Combine(RootDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static void Replace(string destination, string source, string backup)
        {
            try
            {
                File.Replace(source, destination, backup);
            }
            catch (Exception ex) when (
                !(ex is FileNotFoundException) &&
                (ex is IOException ||
                 ex is PlatformNotSupportedException ||
                 ex is UnauthorizedAccessException))
            {
                // Some file systems (or antivirus locks on Windows) reject
                // File.Replace; fall back to two renames.
                if (backup != null)
                {
                    if (File.Exists(backup))
                        File.Delete(backup);

                    File.Move(destination, backup);
                }
                else
                {
                    File.Delete(destination);
                }

                File.Move(source, destination);
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private sealed class PathInfo
        {
            public readonly string Full;
            public readonly string Temp;
            public readonly string Directory;

            public PathInfo(string full, string temp, string directory)
            {
                Full = full;
                Temp = temp;
                Directory = directory;
            }
        }
    }
}
