
namespace SaveToolkit
{
    public enum SaveSerializationFormat : byte
    {
        Json = 0,
        MessagePack = 1
    }

    public enum SaveCompressionType : byte
    {
        None = 0,
        GZip = 1
    }

    public enum SaveStorageType
    {
        File,
        PlayerPrefs
    }

    public enum SavePlatform
    {
        Editor,
        Mobile,
        Desktop,
        Other
    }

    public enum SaveTrigger
    {
        Manual,
        Pause,
        FocusLost,
        Quit,
        Interval
    }
}
