
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace SaveToolkit
{
    /// <summary>
    /// Stores save bytes as Base64 strings in PlayerPrefs. PlayerPrefs only
    /// holds strings, so every read/write allocates one; prefer
    /// <see cref="FileSaveStorage"/> where files are available.
    /// An index key tracks stored paths and write times. Main thread only.
    /// </summary>
    public sealed class PlayerPrefsSaveStorage : ISaveStorage
    {
        private const string ValuePrefix = "STK2:";
        private const string IndexKey = "__index";
        private const char IndexSeparator = '|';

        private readonly string keyPrefix;
        private Dictionary<string, long> index;

        public PlayerPrefsSaveStorage(string rootKey)
        {
            if (string.IsNullOrWhiteSpace(rootKey))
                throw new ArgumentException("Root key cannot be empty.", nameof(rootKey));

            keyPrefix = rootKey.TrimEnd('/') + "/";
        }

        public bool RequiresMainThread => true;

        public void Write(string path, ReadOnlySpan<byte> header, ReadOnlySpan<byte> payload, string backupPath)
        {
            string key = GetKey(path);

            if (backupPath != null && PlayerPrefs.HasKey(key))
            {
                PlayerPrefs.SetString(GetKey(backupPath), PlayerPrefs.GetString(key));
                GetIndex()[backupPath] = GetIndex().TryGetValue(path, out long previous) ? previous : DateTime.UtcNow.Ticks;
            }

            int length = header.Length + payload.Length;
            byte[] combined = ArrayPool<byte>.Shared.Rent(length);

            try
            {
                header.CopyTo(combined);
                payload.CopyTo(combined.AsSpan(header.Length));
                PlayerPrefs.SetString(key, ValuePrefix + Convert.ToBase64String(combined, 0, length));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(combined);
            }

            GetIndex()[path] = DateTime.UtcNow.Ticks;
            SaveIndex();
            PlayerPrefs.Save();
        }

        public bool TryRead(string path, PooledBufferWriter destination)
        {
            string key = GetKey(path);

            if (!PlayerPrefs.HasKey(key))
                return false;

            destination.Write(DecodeValue(PlayerPrefs.GetString(key)));
            return true;
        }

        public bool Exists(string path)
        {
            return PlayerPrefs.HasKey(GetKey(path));
        }

        public void Delete(string path)
        {
            PlayerPrefs.DeleteKey(GetKey(path));

            if (GetIndex().Remove(path))
                SaveIndex();

            PlayerPrefs.Save();
        }

        public void Move(string sourcePath, string destinationPath)
        {
            string source = GetKey(sourcePath);

            if (!PlayerPrefs.HasKey(source))
                throw new InvalidOperationException($"'{sourcePath}' does not exist.");

            PlayerPrefs.SetString(GetKey(destinationPath), PlayerPrefs.GetString(source));
            PlayerPrefs.DeleteKey(source);

            Dictionary<string, long> entries = GetIndex();
            entries[destinationPath] = entries.TryGetValue(sourcePath, out long ticks) ? ticks : DateTime.UtcNow.Ticks;
            entries.Remove(sourcePath);

            SaveIndex();
            PlayerPrefs.Save();
        }

        public bool TryGetLastWriteTimeUtc(string path, out DateTime lastWriteUtc)
        {
            if (GetIndex().TryGetValue(path, out long ticks) && Exists(path))
            {
                lastWriteUtc = new DateTime(ticks, DateTimeKind.Utc);
                return true;
            }

            lastWriteUtc = default;
            return false;
        }

        public IReadOnlyList<string> List(string directory)
        {
            string prefix = GetDirectoryPrefix(directory);
            var names = new List<string>();

            foreach (string path in GetIndex().Keys)
            {
                if (!path.StartsWith(prefix, StringComparison.Ordinal))
                    continue;

                string name = path.Substring(prefix.Length);

                if (name.IndexOf('/') < 0)
                    names.Add(name);
            }

            return names;
        }

        public void DeleteDirectory(string directory)
        {
            string prefix = GetDirectoryPrefix(directory);
            Dictionary<string, long> entries = GetIndex();
            var removed = new List<string>();

            foreach (string path in entries.Keys)
            {
                if (path.StartsWith(prefix, StringComparison.Ordinal))
                    removed.Add(path);
            }

            foreach (string path in removed)
            {
                PlayerPrefs.DeleteKey(GetKey(path));
                entries.Remove(path);
            }

            SaveIndex();
            PlayerPrefs.Save();
        }

        private static string GetDirectoryPrefix(string directory)
        {
            return string.IsNullOrEmpty(directory) ? "" : directory.TrimEnd('/') + "/";
        }

        private string GetKey(string path)
        {
            return keyPrefix + path;
        }

        private Dictionary<string, long> GetIndex()
        {
            if (index != null)
                return index;

            index = new Dictionary<string, long>(StringComparer.Ordinal);
            string raw = PlayerPrefs.GetString(keyPrefix + IndexKey, "");

            foreach (string line in raw.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int separator = line.LastIndexOf(IndexSeparator);

                if (separator > 0 &&
                    long.TryParse(line.Substring(separator + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks))
                    index[line.Substring(0, separator)] = ticks;
                else
                    index[line] = 0;
            }

            return index;
        }

        private void SaveIndex()
        {
            var builder = new StringBuilder();

            foreach (KeyValuePair<string, long> entry in index)
            {
                builder.Append(entry.Key)
                    .Append(IndexSeparator)
                    .Append(entry.Value.ToString(CultureInfo.InvariantCulture))
                    .Append('\n');
            }

            PlayerPrefs.SetString(keyPrefix + IndexKey, builder.ToString());
        }

        private static byte[] DecodeValue(string value)
        {
            if (value.StartsWith(ValuePrefix, StringComparison.Ordinal))
            {
                try
                {
                    return Convert.FromBase64String(value.Substring(ValuePrefix.Length));
                }
                catch (FormatException ex)
                {
                    // Truncated or edited value: let SaveManager try the backups.
                    throw new SaveCorruptedException("Stored value is not valid Base64.", ex);
                }
            }

            // Legacy v1 values: Base64 MessagePack or plain JSON text.
            try
            {
                return Convert.FromBase64String(value);
            }
            catch (FormatException)
            {
                return Encoding.UTF8.GetBytes(value);
            }
        }
    }
}
