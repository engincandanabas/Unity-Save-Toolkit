
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace SaveToolkit.Tests
{
    public sealed class TestLogger : ISaveLogger
    {
        public bool IsInfoEnabled => false;

        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> Errors = new List<string>();

        public void Log(string message) { }

        public void LogWarning(string message)
        {
            lock (Warnings)
                Warnings.Add(message);
        }

        public void LogError(string message)
        {
            lock (Errors)
                Errors.Add(message);
        }
    }

    public class TestData
    {
        public int Level;
        public string Name;
        public List<int> Items = new List<int> { 1 };
    }

    public class PrivateSetterData
    {
        public int Coins { get; private set; }

        public void SetCoins(int coins)
        {
            Coins = coins;
        }
    }

    public class UnicodeData
    {
        public string Text;
    }

    public class MigratedData
    {
        public int Coins;
        public int Level;
    }

    public sealed class FakeSaveable : ISaveable
    {
        public FakeSaveable(string key, int value)
        {
            SaveKey = key;
            Value = value;
        }

        public string SaveKey { get; }

        public int Value;

        public Type StateType => typeof(TestData);

        public object CaptureState()
        {
            return new TestData { Level = Value };
        }

        public Action OnRestore;

        public void RestoreState(object state)
        {
            Value = ((TestData)state).Level;
            OnRestore?.Invoke();
        }
    }

    /// <summary>Wraps a storage to simulate I/O failures or hold a write in progress.</summary>
    public sealed class FailingStorage : ISaveStorage
    {
        private readonly ISaveStorage inner;
        private readonly ManualResetEventSlim release = new ManualResetEventSlim(false);

        public FailingStorage(ISaveStorage inner)
        {
            this.inner = inner;
        }

        public bool FailReads;

        /// <summary>The next Write blocks until <see cref="Release"/>.</summary>
        public bool BlockWrites;

        public readonly ManualResetEventSlim WriteStarted = new ManualResetEventSlim(false);

        public void Release()
        {
            release.Set();
        }

        public bool RequiresMainThread => inner.RequiresMainThread;

        public void Write(string path, ReadOnlySpan<byte> header, ReadOnlySpan<byte> payload, string backupPath)
        {
            if (BlockWrites)
            {
                BlockWrites = false;
                WriteStarted.Set();
                release.Wait(10000);
            }

            inner.Write(path, header, payload, backupPath);
        }

        public bool TryRead(string path, PooledBufferWriter destination)
        {
            if (FailReads)
                throw new IOException("simulated sharing violation");

            return inner.TryRead(path, destination);
        }

        public bool Exists(string path) => inner.Exists(path);
        public void Delete(string path) => inner.Delete(path);
        public void Move(string sourcePath, string destinationPath) => inner.Move(sourcePath, destinationPath);
        public bool TryGetLastWriteTimeUtc(string path, out DateTime lastWriteUtc) => inner.TryGetLastWriteTimeUtc(path, out lastWriteUtc);
        public IReadOnlyList<string> List(string directory) => inner.List(directory);
        public void DeleteDirectory(string directory) => inner.DeleteDirectory(directory);
    }
}
