
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace SaveToolkit
{
    /// <summary>
    /// Save pipeline: serialize → compress → encrypt → envelope → storage.
    /// Loading reverses it, recovers from backups on corruption and runs
    /// migrations for older data versions.
    /// </summary>
    /// <remarks>
    /// Every stage works on pooled buffers, so a save allocates only what the
    /// serializer itself needs. Storage access is serialized through one gate;
    /// async calls serialize on the caller's thread (a safe snapshot) and do
    /// compression, encryption and I/O on the thread pool.
    /// </remarks>
    public sealed class SaveManager
    {
        public const string SaveExtension = ".sav";

        private const string BackupExtension = ".bak";
        private const string CorruptExtension = ".corrupt";
        private const int MaxKeyLength = 64;
        private const string NotFoundMessage = "Save data not found.";

        private readonly SaveOptions options;
        private readonly ISaveStorage storage;
        private readonly ISaveLogger logger;
        private readonly ISaveSerializer serializer;
        private readonly ISaveSerializer[] readers = new ISaveSerializer[2];
        private readonly ISaveCompressor compressor = new GZipSaveCompressor();
        private readonly ISaveEncryptor encryptor;
        private readonly SaveMigrator migrator = new SaveMigrator();
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        private readonly string[] slotDirectories;
        private readonly Dictionary<string, KeyPaths>[] pathCache;
        private readonly List<PendingWrite> saveAllScratch = new List<PendingWrite>();
        private readonly List<ISaveable> loadAllScratch = new List<ISaveable>();

        private int activeSlot;
        private long writeSequence;
        private bool saveAllScratchInUse;
        private bool loadAllScratchInUse;

        /// <exception cref="ArgumentException">The options are invalid.</exception>
        /// <exception cref="InvalidOperationException">The serializer for the format is unavailable.</exception>
        public SaveManager(SaveOptions options, ISaveStorage storage, ISaveLogger logger = null)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            if (!options.Validate(out string error))
                throw new ArgumentException(error, nameof(options));

            this.options = options.Clone();
            this.storage = storage ?? throw new ArgumentNullException(nameof(storage));
            this.logger = logger ?? new UnitySaveLogger(options.EnableDebugLogs);

            if (!SaveSerializerRegistry.TryCreate(options.Format, this.options, out serializer, out error))
                throw new InvalidOperationException(error);

            readers[(int)options.Format] = serializer;

            // Also created when encryption is off, so older encrypted saves
            // remain readable after the profile changes.
            if (options.HasEncryptionSecret)
                encryptor = new AesSaveEncryptor(options.EncryptionPassphrase, options.EncryptionSalt);

            slotDirectories = new string[options.SlotCount];
            pathCache = new Dictionary<string, KeyPaths>[options.SlotCount];

            for (int i = 0; i < options.SlotCount; i++)
            {
                slotDirectories[i] = "slot_" + i;
                pathCache[i] = new Dictionary<string, KeyPaths>(StringComparer.Ordinal);
            }

            activeSlot = options.DefaultSlot;
        }

        /// <summary>Creates a manager with the storage the profile asks for.</summary>
        public static SaveManager Create(SaveProfile profile, ISaveLogger logger = null)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            ISaveStorage storage = profile.Storage == SaveStorageType.PlayerPrefs
                ? (ISaveStorage)new PlayerPrefsSaveStorage(profile.SaveDirectory)
                : new FileSaveStorage(
                    Path.Combine(Application.persistentDataPath, profile.SaveDirectory),
                    profile.FlushToDisk);

            return new SaveManager(
                profile.ToOptions(),
                storage,
                logger ?? new UnitySaveLogger(profile.EnableDebugLogs)
            );
        }

        public int ActiveSlot => Volatile.Read(ref activeSlot);

        public int SlotCount => options.SlotCount;

        public ISaveStorage Storage => storage;

        /// <summary>Register before the first load that needs it.</summary>
        public void RegisterMigration(ISaveMigration migration)
        {
            migrator.Register(migration);
        }

        // ------------------------------------------------------------------
        // Synchronous API
        // ------------------------------------------------------------------

        public bool Save<T>(string key, T data, out string error)
        {
            return SaveObject(key, data, typeof(T), out error);
        }

        public bool SaveObject(string key, object data, Type type, out string error)
        {
            using (var serialized = new PooledBufferWriter())
            {
                if (!TrySerialize(key, data, type, serialized, out error))
                    return false;

                long sequence = NextSequence();
                int slot = ActiveSlot;
                gate.Wait();

                try
                {
                    return WriteEntry(slot, key, serialized.WrittenSegment, sequence, out error);
                }
                finally
                {
                    gate.Release();
                }
            }
        }

        public bool TryLoad<T>(string key, out T data, out string error)
        {
            bool success = TryLoadObject(key, typeof(T), out object value, out error);

            data = success ? (T)value : default;
            return success;
        }

        /// <remarks>
        /// On success <paramref name="error"/> is null, or a note when the
        /// data was recovered from a backup.
        /// </remarks>
        public bool TryLoadObject(string key, Type type, out object data, out string error)
        {
            data = null;

            if (!ValidateKey(key, out error))
                return false;

            int slot = ActiveSlot;
            SaveResult<object> result;
            gate.Wait();

            try
            {
                result = ReadEntry(slot, key, type);
            }
            finally
            {
                gate.Release();
            }

            data = result.Data;
            error = result.Error;
            return result.Success;
        }

        public bool Exists(string key, out string error)
        {
            if (!ValidateKey(key, out error))
                return false;

            int slot = ActiveSlot;
            gate.Wait();

            try
            {
                KeyPaths paths = GetPaths(slot, key);
                return storage.Exists(paths.Main) || FindLegacyPath(slot, paths) != null;
            }
            catch (Exception ex)
            {
                error = "Save existence check failed: " + ex.Message;
                logger.LogError(error);
                return false;
            }
            finally
            {
                gate.Release();
            }
        }

        public bool Delete(string key, out string error)
        {
            if (!ValidateKey(key, out error))
                return false;

            int slot = ActiveSlot;
            gate.Wait();

            try
            {
                KeyPaths paths = GetPaths(slot, key);

                storage.Delete(paths.Main);
                storage.Delete(paths.Corrupt);

                foreach (string backup in paths.Backups)
                    storage.Delete(backup);

                string legacyPath;
                while ((legacyPath = FindLegacyPath(slot, paths)) != null)
                    storage.Delete(legacyPath);

                if (logger.IsInfoEnabled)
                    logger.Log("Delete completed: " + key);

                return true;
            }
            catch (Exception ex)
            {
                error = "Delete failed: " + ex.Message;
                logger.LogError(error);
                return false;
            }
            finally
            {
                gate.Release();
            }
        }

        // ------------------------------------------------------------------
        // Asynchronous API
        // ------------------------------------------------------------------

        /// <summary>
        /// Serializes on the calling thread (safe snapshot of the data),
        /// then compresses, encrypts and writes on the thread pool.
        /// </summary>
        public Task<SaveResult> SaveAsync<T>(string key, T data)
        {
            var serialized = new PooledBufferWriter();

            if (!TrySerialize(key, data, typeof(T), serialized, out string error))
            {
                serialized.Dispose();
                return Task.FromResult(SaveResult.Fail(error));
            }

            long sequence = NextSequence();
            int slot = ActiveSlot;

            if (!storage.RequiresMainThread)
                return WriteOnThreadPoolAsync(slot, key, serialized, sequence);

            // PlayerPrefs must stay on the main thread.
            try
            {
                gate.Wait();

                try
                {
                    return Task.FromResult(WriteEntry(slot, key, serialized.WrittenSegment, sequence, out error)
                        ? SaveResult.Ok()
                        : SaveResult.Fail(error));
                }
                finally
                {
                    gate.Release();
                }
            }
            finally
            {
                serialized.Dispose();
            }
        }

        public Task<SaveResult<T>> LoadAsync<T>(string key)
        {
            if (!ValidateKey(key, out string error))
                return Task.FromResult(new SaveResult<T>(false, default, error, false, false));

            int slot = ActiveSlot;

            if (!storage.RequiresMainThread)
                return LoadOnThreadPoolAsync<T>(slot, key);

            gate.Wait();

            try
            {
                return Task.FromResult(Convert<T>(ReadEntry(slot, key, typeof(T))));
            }
            finally
            {
                gate.Release();
            }
        }

        // ------------------------------------------------------------------
        // ISaveable groups
        // ------------------------------------------------------------------

        public bool SaveAll(IReadOnlyList<ISaveable> saveables, out string error)
        {
            // Reuse the scratch list unless a nested SaveAll is in progress.
            bool useScratch = !saveAllScratchInUse;
            List<PendingWrite> entries = useScratch ? saveAllScratch : new List<PendingWrite>();
            List<string> errors = null;

            saveAllScratchInUse |= useScratch;

            try
            {
                CaptureAll(saveables, entries, ref errors);

                if (entries.Count > 0)
                {
                    int slot = ActiveSlot;
                    gate.Wait();

                    try
                    {
                        WriteAll(slot, entries, ref errors);
                    }
                    finally
                    {
                        gate.Release();
                    }
                }
            }
            finally
            {
                DisposeAll(entries);

                if (useScratch)
                    saveAllScratchInUse = false;
            }

            error = errors == null ? null : string.Join("\n", errors);
            return errors == null;
        }

        public Task<SaveResult> SaveAllAsync(IReadOnlyList<ISaveable> saveables)
        {
            var entries = new List<PendingWrite>(saveables.Count);
            List<string> errors = null;

            CaptureAll(saveables, entries, ref errors);

            if (entries.Count == 0)
                return Task.FromResult(errors == null ? SaveResult.Ok() : SaveResult.Fail(string.Join("\n", errors)));

            int slot = ActiveSlot;

            if (!storage.RequiresMainThread)
                return WriteAllOnThreadPoolAsync(slot, entries, errors);

            try
            {
                gate.Wait();

                try
                {
                    WriteAll(slot, entries, ref errors);
                }
                finally
                {
                    gate.Release();
                }
            }
            finally
            {
                DisposeAll(entries);
            }

            return Task.FromResult(errors == null ? SaveResult.Ok() : SaveResult.Fail(string.Join("\n", errors)));
        }

        /// <summary>
        /// Restores every saveable that has data. Missing entries are skipped.
        /// </summary>
        public bool LoadAll(IReadOnlyList<ISaveable> saveables, out string error)
        {
            List<string> errors = null;
            int slot = ActiveSlot;

            // RestoreState may enable/disable objects, modify the registry or
            // even call LoadAll again, so iterate over a private copy.
            bool useScratch = !loadAllScratchInUse;
            List<ISaveable> snapshot = useScratch ? loadAllScratch : new List<ISaveable>(saveables.Count);

            loadAllScratchInUse |= useScratch;
            snapshot.Clear();
            for (int i = 0; i < saveables.Count; i++)
                snapshot.Add(saveables[i]);

            try
            {
                for (int i = 0; i < snapshot.Count; i++)
                {
                    ISaveable saveable = snapshot[i];
                    string key = saveable.SaveKey;

                    if (!ValidateKey(key, out string keyError))
                    {
                        AddError(ref errors, key + ": " + keyError);
                        continue;
                    }

                    SaveResult<object> result;
                    gate.Wait();

                    try
                    {
                        result = ReadEntry(slot, key, saveable.StateType);
                    }
                    finally
                    {
                        gate.Release();
                    }

                    if (!result.Success)
                    {
                        if (!result.NotFound)
                            AddError(ref errors, key + ": " + result.Error);

                        continue;
                    }

                    try
                    {
                        saveable.RestoreState(result.Data);
                    }
                    catch (Exception ex)
                    {
                        AddError(ref errors, key + ": RestoreState failed: " + ex.Message);
                        logger.LogError("RestoreState failed for '" + key + "': " + ex);
                    }
                }
            }
            finally
            {
                snapshot.Clear();

                if (useScratch)
                    loadAllScratchInUse = false;
            }

            error = errors == null ? null : string.Join("\n", errors);
            return errors == null;
        }

        // ------------------------------------------------------------------
        // Slots
        // ------------------------------------------------------------------

        public bool SetActiveSlot(int slot, out string error)
        {
            if (!ValidateSlot(slot, out error))
                return false;

            Volatile.Write(ref activeSlot, slot);

            if (logger.IsInfoEnabled)
                logger.Log("Active slot: " + slot);

            return true;
        }

        public bool TryGetSlotInfo(int slot, out SaveSlotInfo info, out string error)
        {
            info = null;

            if (!ValidateSlot(slot, out error))
                return false;

            gate.Wait();

            try
            {
                info = ReadSlotInfo(slot);
                return true;
            }
            catch (Exception ex)
            {
                error = "Slot info failed: " + ex.Message;
                logger.LogError(error);
                return false;
            }
            finally
            {
                gate.Release();
            }
        }

        public bool DeleteSlot(int slot, out string error)
        {
            if (!ValidateSlot(slot, out error))
                return false;

            if (!DeleteDirectory(slotDirectories[slot], out error))
                return false;

            // v1 saves lived at the root and are migrated into the default
            // slot on load; without this a "new game" could resurrect them.
            return slot != options.DefaultSlot || DeleteLegacyFiles(out error);
        }

        private bool DeleteLegacyFiles(out string error)
        {
            gate.Wait();

            try
            {
                foreach (string name in storage.List(""))
                {
                    if (name.EndsWith(".json", StringComparison.Ordinal) ||
                        name.EndsWith(".dat", StringComparison.Ordinal))
                        storage.Delete(name);
                }

                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = "Delete failed: " + ex.Message;
                logger.LogError(error);
                return false;
            }
            finally
            {
                gate.Release();
            }
        }

        public bool DeleteAll(out string error)
        {
            return DeleteDirectory("", out error);
        }

        // ------------------------------------------------------------------
        // Write path
        // ------------------------------------------------------------------

        private bool TrySerialize(
            string key,
            object data,
            Type type,
            PooledBufferWriter output,
            out string error)
        {
            if (!ValidateKey(key, out error))
                return false;

            if (data is null)
            {
                error = "Cannot save null data.";
                logger.LogError(error);
                return false;
            }

#if UNITY_EDITOR
            if (serializer is ISaveTypeValidator validator)
            {
                string warning = validator.GetWarning(type);
                if (warning != null)
                    logger.LogWarning(warning);
            }
#endif

            try
            {
                serializer.Serialize(data, type, output);
                return true;
            }
            catch (Exception ex)
            {
                error = "Serialization failed for '" + key + "': " + ex.Message;
                logger.LogError(error);
                return false;
            }
        }

        private void CaptureAll(IReadOnlyList<ISaveable> saveables, List<PendingWrite> entries, ref List<string> errors)
        {
            for (int i = 0; i < saveables.Count; i++)
            {
                ISaveable saveable = saveables[i];
                object state;

                try
                {
                    state = saveable.CaptureState();
                }
                catch (Exception ex)
                {
                    AddError(ref errors, saveable.SaveKey + ": CaptureState failed: " + ex.Message);
                    logger.LogError("CaptureState failed for '" + saveable.SaveKey + "': " + ex);
                    continue;
                }

                var buffer = new PooledBufferWriter();

                if (TrySerialize(saveable.SaveKey, state, saveable.StateType, buffer, out string error))
                {
                    entries.Add(new PendingWrite(saveable.SaveKey, buffer, NextSequence()));
                }
                else
                {
                    buffer.Dispose();
                    AddError(ref errors, saveable.SaveKey + ": " + error);
                }
            }
        }

        private void WriteAll(int slot, List<PendingWrite> entries, ref List<string> errors)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (!WriteEntry(slot, entries[i].Key, entries[i].Buffer.WrittenSegment, entries[i].Sequence, out string error))
                    AddError(ref errors, error);
            }
        }

        /// <param name="sequence">
        /// Order in which the data was captured. A synchronous save can take
        /// the gate before an earlier queued async save; the older snapshot
        /// must not overwrite the newer one.
        /// </param>
        internal bool WriteEntry(int slot, string key, ArraySegment<byte> serialized, long sequence, out string error)
        {
            if (sequence < GetPaths(slot, key).LastWrittenSequence)
            {
                if (logger.IsInfoEnabled)
                    logger.Log("Skipped an older snapshot of '" + key + "'; a newer save already landed.");

                error = null;
                return true;
            }

            PooledBufferWriter compressed = null;
            PooledBufferWriter encrypted = null;

            try
            {
                ArraySegment<byte> payload = serialized;

                if (options.Compression == SaveCompressionType.GZip)
                {
                    compressed = new PooledBufferWriter(payload.Count / 2 + 64);
                    compressor.Compress(payload, compressed);
                    payload = compressed.WrittenSegment;
                }

                if (options.EnableEncryption)
                {
                    encrypted = new PooledBufferWriter(payload.Count + 96);
                    encryptor.Encrypt(payload, encrypted);
                    payload = encrypted.WrittenSegment;
                }

                KeyPaths paths = GetPaths(slot, key);
                Span<byte> header = stackalloc byte[SaveEnvelope.HeaderSize];

                SaveEnvelope.WriteHeader(
                    header,
                    new SaveEnvelopeHeader(options.Format, options.Compression, options.EnableEncryption, options.DataVersion),
                    payload);

                string backup = null;

                if (options.EnableBackup)
                {
                    RotateBackups(paths);
                    backup = paths.Backups[0];
                }

                storage.Write(paths.Main, header, payload, backup);
                paths.LastWrittenSequence = sequence;

                if (logger.IsInfoEnabled)
                    logger.Log("Save completed: " + key);

                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = "Save failed for '" + key + "': " + ex.Message;
                logger.LogError(error);
                return false;
            }
            finally
            {
                compressed?.Dispose();
                encrypted?.Dispose();
            }
        }

        /// <summary>bak(N-1) → bakN ... bak1 → bak2. The write itself moves main → bak1.</summary>
        private void RotateBackups(KeyPaths paths)
        {
            for (int i = options.BackupCount - 1; i >= 1; i--)
            {
                if (storage.Exists(paths.Backups[i - 1]))
                    storage.Move(paths.Backups[i - 1], paths.Backups[i]);
            }
        }

        private async Task<SaveResult> WriteOnThreadPoolAsync(int slot, string key, PooledBufferWriter serialized, long sequence)
        {
            try
            {
                // ConfigureAwait(false) keeps the gate release off the main
                // thread, so a synchronous Save on the main thread can never
                // deadlock waiting for it.
                await gate.WaitAsync().ConfigureAwait(false);

                try
                {
                    return await Task.Run(() =>
                            WriteEntry(slot, key, serialized.WrittenSegment, sequence, out string error)
                                ? SaveResult.Ok()
                                : SaveResult.Fail(error))
                        .ConfigureAwait(false);
                }
                finally
                {
                    gate.Release();
                }
            }
            finally
            {
                serialized.Dispose();
            }
        }

        private async Task<SaveResult> WriteAllOnThreadPoolAsync(int slot, List<PendingWrite> entries, List<string> captureErrors)
        {
            try
            {
                await gate.WaitAsync().ConfigureAwait(false);

                try
                {
                    return await Task.Run(() =>
                        {
                            List<string> errors = captureErrors;
                            WriteAll(slot, entries, ref errors);

                            return errors == null
                                ? SaveResult.Ok()
                                : SaveResult.Fail(string.Join("\n", errors));
                        })
                        .ConfigureAwait(false);
                }
                finally
                {
                    gate.Release();
                }
            }
            finally
            {
                DisposeAll(entries);
            }
        }

        // ------------------------------------------------------------------
        // Read path
        // ------------------------------------------------------------------

        /// <summary>Never throws: storage errors (locked files, I/O faults) become a failed result.</summary>
        private SaveResult<object> ReadEntry(int slot, string key, Type type)
        {
            try
            {
                return ReadEntryCore(slot, key, type);
            }
            catch (Exception ex)
            {
                string error = "Load failed for '" + key + "': " + ex.Message;
                logger.LogError(error);
                return Failed(error);
            }
        }

        private SaveResult<object> ReadEntryCore(int slot, string key, Type type)
        {
            KeyPaths paths = GetPaths(slot, key);

            using (var raw = new PooledBufferWriter(1024))
            {
                object data = null;
                bool needsRewrite = false;
                string error;
                string corruption = null;

                ReadStatus mainStatus = ReadRaw(paths.Main, raw, out error);
                bool mainExists = mainStatus != ReadStatus.Missing;

                if (mainStatus == ReadStatus.Found)
                {
                    switch (Decode(key, raw.WrittenSegment, type, out data, out needsRewrite, out error))
                    {
                        case DecodeStatus.Ok:
                            if (needsRewrite)
                                Rewrite(slot, key, data, type, null);

                            return Loaded(key, data, null);

                        case DecodeStatus.Failed:
                            return Failed(error);
                    }
                }

                if (mainExists)
                {
                    corruption = error;
                    logger.LogWarning("'" + paths.Main + "' is corrupted: " + error);
                }
                else
                {
                    string legacyPath = FindLegacyPath(slot, paths);

                    if (legacyPath != null)
                    {
                        if (ReadRaw(legacyPath, raw, out error) != ReadStatus.Found ||
                            Decode(key, raw.WrittenSegment, type, out data, out _, out error) != DecodeStatus.Ok)
                            return Failed(error ?? NotFoundMessage);

                        Rewrite(slot, key, data, type, legacyPath);
                        return Loaded(key, data, null);
                    }

                    // Rotation always refills bak1 first, so without bak1 there
                    // is nothing to recover: skip probing every backup slot.
                    if (!storage.Exists(paths.Backups[0]))
                        return NotFound(key);
                }

                for (int i = 0; i < paths.Backups.Length; i++)
                {
                    string backup = paths.Backups[i];
                    ReadStatus status = ReadRaw(backup, raw, out error);

                    if (status == ReadStatus.Missing)
                        continue;

                    DecodeStatus decoded = status == ReadStatus.Found
                        ? Decode(key, raw.WrittenSegment, type, out data, out needsRewrite, out error)
                        : DecodeStatus.Corrupted;

                    if (decoded == DecodeStatus.Failed)
                        return Failed(error);

                    if (decoded == DecodeStatus.Corrupted)
                    {
                        corruption = corruption ?? error;
                        logger.LogWarning("'" + backup + "' is corrupted: " + error);
                        continue;
                    }

                    // Keep the broken file for inspection, restore the backup.
                    if (mainExists)
                        storage.Move(paths.Main, paths.Corrupt);

                    storage.Write(paths.Main, ReadOnlySpan<byte>.Empty, raw.WrittenSpan, null);

                    string note = "Recovered '" + key + "' from backup '" + backup + "'.";
                    logger.LogWarning(note);

                    if (needsRewrite)
                        Rewrite(slot, key, data, type, null);

                    return Loaded(key, data, note);
                }

                if (!mainExists && corruption == null)
                    return NotFound(key);

                try
                {
                    if (mainExists)
                        storage.Move(paths.Main, paths.Corrupt);
                }
                catch (Exception ex)
                {
                    logger.LogWarning("Could not move corrupted save aside: " + ex.Message);
                }

                string corrupted = "Save '" + key + "' is corrupted and no valid backup was found: " + corruption;
                logger.LogError(corrupted);
                return Failed(corrupted);
            }
        }

        /// <summary>
        /// Reads a file into <paramref name="raw"/> (cleared first). Storage
        /// that detects unreadable bytes (e.g. invalid Base64 in PlayerPrefs)
        /// reports Corrupted so the backup path runs.
        /// </summary>
        private ReadStatus ReadRaw(string path, PooledBufferWriter raw, out string corruption)
        {
            raw.Clear();
            corruption = null;

            try
            {
                return storage.TryRead(path, raw) ? ReadStatus.Found : ReadStatus.Missing;
            }
            catch (SaveCorruptedException ex)
            {
                corruption = ex.Message;
                return ReadStatus.Corrupted;
            }
        }

        private SaveResult<object> NotFound(string key)
        {
            if (logger.IsInfoEnabled)
                logger.Log(NotFoundMessage + " (" + key + ")");

            return new SaveResult<object>(false, null, NotFoundMessage, true, false);
        }

        private SaveResult<object> Loaded(string key, object data, string note)
        {
            if (logger.IsInfoEnabled)
                logger.Log("Load completed: " + key);

            return new SaveResult<object>(true, data, note, false, note != null);
        }

        private static SaveResult<object> Failed(string error)
        {
            return new SaveResult<object>(false, null, error, false, false);
        }

        private DecodeStatus Decode(
            string key,
            ArraySegment<byte> raw,
            Type type,
            out object data,
            out bool needsRewrite,
            out string error)
        {
            try
            {
                data = DecodeCore(key, raw, type, out needsRewrite);
                error = null;
                return DecodeStatus.Ok;
            }
            catch (SaveCorruptedException ex)
            {
                data = null;
                needsRewrite = false;
                error = ex.Message;
                return DecodeStatus.Corrupted;
            }
            catch (SaveAuthenticationException ex)
            {
                data = null;
                needsRewrite = false;
                error = "Save '" + key + "' could not be decrypted: " + ex.Message;
                logger.LogError(error);
                return DecodeStatus.Failed;
            }
            catch (SaveVersionException ex)
            {
                data = null;
                needsRewrite = false;
                error = ex.Message;
                logger.LogError(error);
                return DecodeStatus.Failed;
            }
            catch (Exception ex)
            {
                // The bytes are intact (or unverifiable legacy data) but could
                // not become the requested type: usually a code/type mismatch,
                // so the files are left alone.
                data = null;
                needsRewrite = false;
                error = "Load failed for '" + key + "': " + ex.Message;
                logger.LogError(error);
                return DecodeStatus.Failed;
            }
        }

        private object DecodeCore(string key, ArraySegment<byte> raw, Type type, out bool needsRewrite)
        {
            PooledBufferWriter decrypted = null;
            PooledBufferWriter decompressed = null;
            PooledBufferWriter migrated = null;

            try
            {
                SaveEnvelopeHeader header;
                ArraySegment<byte> bytes;

                if (SaveEnvelope.HasMagic(raw))
                {
                    bytes = SaveEnvelope.Read(raw, out header);

                    if (header.Encrypted)
                    {
                        if (encryptor == null)
                            throw new InvalidOperationException(
                                "Save is encrypted but the active profile has no " +
                                "EncryptionPassphrase/EncryptionSalt.");

                        decrypted = new PooledBufferWriter(bytes.Count, true);
                        encryptor.Decrypt(bytes, decrypted);
                        bytes = decrypted.WrittenSegment;
                    }

                    if (header.Compression == SaveCompressionType.GZip)
                    {
                        decompressed = new PooledBufferWriter(bytes.Count * 4, header.Encrypted);
                        compressor.Decompress(bytes, decompressed);
                        bytes = decompressed.WrittenSegment;
                    }
                }
                else
                {
                    // v1 saves had no envelope: plain JSON or MessagePack.
                    header = new SaveEnvelopeHeader(GuessLegacyFormat(raw), SaveCompressionType.None, false, 0);
                    bytes = raw;
                }

                if (header.DataVersion > options.DataVersion)
                    throw new SaveVersionException(
                        "Save '" + key + "' has data version " + header.DataVersion +
                        ", newer than this build (" + options.DataVersion + ").");

                ISaveSerializer reader = GetReader(header.Format);

                if (migrator.HasMigrations(key, header.DataVersion, options.DataVersion))
                {
                    JToken token = reader.ToJToken(bytes);
                    token = migrator.Migrate(key, token, header.DataVersion, options.DataVersion);

                    migrated = new PooledBufferWriter(bytes.Count + 64);
                    reader.FromJToken(token, migrated);
                    bytes = migrated.WrittenSegment;

                    if (logger.IsInfoEnabled)
                        logger.Log("Migrated '" + key + "' from v" + header.DataVersion + " to v" + options.DataVersion + ".");
                }

                object data = reader.Deserialize(bytes, type);

                if (data is null)
                    throw new InvalidDataException("Save data is null.");

                needsRewrite = header.DataVersion < options.DataVersion;
                return data;
            }
            finally
            {
                decrypted?.Dispose();
                decompressed?.Dispose();
                migrated?.Dispose();
            }
        }

        private void Rewrite(int slot, string key, object data, Type type, string legacyPath)
        {
            try
            {
                using (var buffer = new PooledBufferWriter())
                {
                    serializer.Serialize(data, type, buffer);

                    if (WriteEntry(slot, key, buffer.WrittenSegment, NextSequence(), out _) && legacyPath != null)
                        storage.Delete(legacyPath);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning("Could not rewrite '" + key + "' in the current format: " + ex.Message);
            }
        }

        private async Task<SaveResult<T>> LoadOnThreadPoolAsync<T>(int slot, string key)
        {
            await gate.WaitAsync().ConfigureAwait(false);

            try
            {
                SaveResult<object> result = await Task.Run(() => ReadEntry(slot, key, typeof(T)))
                    .ConfigureAwait(false);

                return Convert<T>(result);
            }
            finally
            {
                gate.Release();
            }
        }

        private SaveSlotInfo ReadSlotInfo(int slot)
        {
            string directory = slotDirectories[slot];
            var keys = new List<string>();
            DateTime? latest = null;
            string latestPath = null;

            foreach (string name in storage.List(directory))
            {
                if (!name.EndsWith(SaveExtension, StringComparison.Ordinal))
                    continue;

                keys.Add(name.Substring(0, name.Length - SaveExtension.Length));

                string path = directory + "/" + name;

                if (storage.TryGetLastWriteTimeUtc(path, out DateTime written) &&
                    (latest == null || written > latest.Value))
                {
                    latest = written;
                    latestPath = path;
                }
            }

            var info = new SaveSlotInfo
            {
                Slot = slot,
                Exists = keys.Count > 0,
                Keys = keys,
                LastSavedUtc = latest
            };

            if (latestPath != null)
            {
                using (var raw = new PooledBufferWriter())
                {
                    if (storage.TryRead(latestPath, raw) &&
                        raw.WrittenCount >= SaveEnvelope.HeaderSize &&
                        SaveEnvelope.HasMagic(raw.WrittenSpan))
                        info.DataVersion = BinaryPrimitives.ReadInt32LittleEndian(raw.WrittenSpan.Slice(8));
                }
            }

            return info;
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private bool DeleteDirectory(string directory, out string error)
        {
            gate.Wait();

            try
            {
                storage.DeleteDirectory(directory);

                if (logger.IsInfoEnabled)
                    logger.Log(directory.Length == 0 ? "All saves deleted." : directory + " deleted.");

                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = "Delete failed: " + ex.Message;
                logger.LogError(error);
                return false;
            }
            finally
            {
                gate.Release();
            }
        }

        private ISaveSerializer GetReader(SaveSerializationFormat format)
        {
            ISaveSerializer reader = readers[(int)format];

            if (reader != null)
                return reader;

            if (!SaveSerializerRegistry.TryCreate(format, options, out reader, out string error))
                throw new InvalidOperationException(error);

            readers[(int)format] = reader;
            return reader;
        }

        private KeyPaths GetPaths(int slot, string key)
        {
            Dictionary<string, KeyPaths> cache = pathCache[slot];

            if (!cache.TryGetValue(key, out KeyPaths paths))
            {
                paths = new KeyPaths(slotDirectories[slot] + "/" + key + SaveExtension, key);
                cache[key] = paths;
            }

            return paths;
        }

        private string FindLegacyPath(int slot, KeyPaths paths)
        {
            // Legacy (v1) files are never created again, so a miss is final.
            if (slot != options.DefaultSlot || paths.LegacyMissing)
                return null;

            string[] legacyPaths = paths.Legacy;

            for (int i = 0; i < legacyPaths.Length; i++)
            {
                if (storage.Exists(legacyPaths[i]))
                    return legacyPaths[i];
            }

            paths.LegacyMissing = true;
            return null;
        }

        private static SaveSerializationFormat GuessLegacyFormat(ArraySegment<byte> raw)
        {
            ReadOnlySpan<byte> span = raw.AsSpan();

            for (int i = 0; i < span.Length; i++)
            {
                byte b = span[i];

                if (b == ' ' || b == '\t' || b == '\r' || b == '\n' ||
                    b == 0xEF || b == 0xBB || b == 0xBF)
                    continue;

                return b == '{' || b == '['
                    ? SaveSerializationFormat.Json
                    : SaveSerializationFormat.MessagePack;
            }

            return SaveSerializationFormat.Json;
        }

        private static SaveResult<T> Convert<T>(SaveResult<object> result)
        {
            return new SaveResult<T>(
                result.Success,
                result.Success ? (T)result.Data : default,
                result.Error,
                result.NotFound,
                result.RecoveredFromBackup
            );
        }

        private static void DisposeAll(List<PendingWrite> entries)
        {
            for (int i = 0; i < entries.Count; i++)
                entries[i].Buffer.Dispose();

            entries.Clear();
        }

        private long NextSequence()
        {
            return Interlocked.Increment(ref writeSequence);
        }

        private static void AddError(ref List<string> errors, string error)
        {
            (errors ?? (errors = new List<string>())).Add(error);
        }

        private bool ValidateSlot(int slot, out string error)
        {
            if (slot < 0 || slot >= options.SlotCount)
            {
                error = $"Slot {slot} is out of range (0-{options.SlotCount - 1}).";
                logger.LogError(error);
                return false;
            }

            error = null;
            return true;
        }

        private bool ValidateKey(string key, out string error)
        {
            if (IsValidKey(key))
            {
                error = null;
                return true;
            }

            error = "Invalid save key.";
            logger.LogError(error + " ('" + key + "')");
            return false;
        }

        /// <summary>Keys: 1-64 chars of a-z, A-Z, 0-9, '_' and '-'.</summary>
        public static bool IsValidKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Length > MaxKeyLength)
                return false;

            foreach (char c in key)
            {
                bool valid =
                    (c >= 'a' && c <= 'z') ||
                    (c >= 'A' && c <= 'Z') ||
                    (c >= '0' && c <= '9') ||
                    c == '_' ||
                    c == '-';

                if (!valid)
                    return false;
            }

            return true;
        }

        private enum ReadStatus
        {
            Missing,
            Found,
            Corrupted
        }

        private enum DecodeStatus
        {
            Ok,
            Corrupted,
            Failed
        }

        private readonly struct PendingWrite
        {
            public readonly string Key;
            public readonly PooledBufferWriter Buffer;
            public readonly long Sequence;

            public PendingWrite(string key, PooledBufferWriter buffer, long sequence)
            {
                Key = key;
                Buffer = buffer;
                Sequence = sequence;
            }
        }

        /// <summary>Per-key path strings, built once and cached.</summary>
        private sealed class KeyPaths
        {
            public readonly string Main;
            public readonly string Corrupt;
            public readonly string[] Backups = new string[SaveOptions.MaxBackupCount];

            public bool LegacyMissing;
            public long LastWrittenSequence = long.MinValue;

            private readonly string key;
            private string[] legacy;

            public KeyPaths(string main, string key)
            {
                Main = main;
                Corrupt = main + CorruptExtension;
                this.key = key;

                for (int i = 0; i < Backups.Length; i++)
                    Backups[i] = main + BackupExtension + (i + 1);
            }

            // v1 saves lived at the root without slots.
            public string[] Legacy => legacy ?? (legacy = new[] { key + ".json", key + ".dat", key });
        }
    }
}
