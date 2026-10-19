
namespace SaveToolkit
{
    public readonly struct SaveEnvelopeHeader
    {
        public readonly SaveSerializationFormat Format;
        public readonly SaveCompressionType Compression;
        public readonly bool Encrypted;
        public readonly int DataVersion;

        public SaveEnvelopeHeader(
            SaveSerializationFormat format,
            SaveCompressionType compression,
            bool encrypted,
            int dataVersion)
        {
            Format = format;
            Compression = compression;
            Encrypted = encrypted;
            DataVersion = dataVersion;
        }
    }
}
