
namespace SaveToolkit
{
    public readonly struct SaveResult
    {
        public readonly bool Success;
        public readonly string Error;

        private SaveResult(bool success, string error)
        {
            Success = success;
            Error = error;
        }

        public static SaveResult Ok() => new SaveResult(true, null);

        public static SaveResult Fail(string error) => new SaveResult(false, error);
    }

    public readonly struct SaveResult<T>
    {
        public readonly bool Success;
        public readonly string Error;
        public readonly T Data;
        public readonly bool NotFound;
        public readonly bool RecoveredFromBackup;

        public SaveResult(
            bool success,
            T data,
            string error,
            bool notFound,
            bool recoveredFromBackup)
        {
            Success = success;
            Data = data;
            Error = error;
            NotFound = notFound;
            RecoveredFromBackup = recoveredFromBackup;
        }
    }
}
