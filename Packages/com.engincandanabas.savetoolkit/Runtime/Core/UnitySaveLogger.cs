
using UnityEngine;

namespace SaveToolkit
{
    /// <summary>
    /// Info logs follow EnableDebugLogs; warnings and errors always show,
    /// since they report data loss risks (corruption, recovery).
    /// </summary>
    public sealed class UnitySaveLogger : ISaveLogger
    {
        private const string Prefix = "[SaveToolkit] ";

        public UnitySaveLogger(bool verbose)
        {
            IsInfoEnabled = verbose;
        }

        public bool IsInfoEnabled { get; }

        public void Log(string message)
        {
            if (IsInfoEnabled)
                Debug.Log(Prefix + message);
        }

        public void LogWarning(string message)
        {
            Debug.LogWarning(Prefix + message);
        }

        public void LogError(string message)
        {
            Debug.LogError(Prefix + message);
        }
    }
}
