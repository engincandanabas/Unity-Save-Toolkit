
using System;
using System.Collections.Generic;

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

        public void RestoreState(object state)
        {
            Value = ((TestData)state).Level;
        }
    }
}
