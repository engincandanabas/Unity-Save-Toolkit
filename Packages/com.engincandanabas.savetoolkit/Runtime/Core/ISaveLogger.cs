
namespace SaveToolkit
{
    public interface ISaveLogger
    {
        /// <summary>
        /// When false, callers skip building info messages entirely,
        /// so disabled logging costs no allocations.
        /// </summary>
        bool IsInfoEnabled { get; }

        void Log(string message);
        void LogWarning(string message);
        void LogError(string message);
    }
}
