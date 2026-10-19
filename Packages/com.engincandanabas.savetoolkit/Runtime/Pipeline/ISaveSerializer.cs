
using System;
using System.Buffers;
using Newtonsoft.Json.Linq;

namespace SaveToolkit
{
    /// <summary>
    /// Turns save data into bytes and back. Implementations write into the
    /// supplied buffer instead of returning arrays, so the pipeline can run
    /// on pooled memory. Instances are shared across threads.
    /// </summary>
    public interface ISaveSerializer
    {
        SaveSerializationFormat Format { get; }

        void Serialize(object data, Type type, IBufferWriter<byte> output);

        object Deserialize(ReadOnlyMemory<byte> bytes, Type type);

        /// <summary>Converts serialized bytes to a JToken for migrations.</summary>
        JToken ToJToken(ReadOnlyMemory<byte> bytes);

        /// <summary>Writes a migrated JToken back in this serializer's format.</summary>
        void FromJToken(JToken token, IBufferWriter<byte> output);
    }

    /// <summary>
    /// Optional: lets a serializer warn (Editor only) about types that will
    /// fail on some platforms, e.g. MessagePack types without attributes on IL2CPP.
    /// </summary>
    public interface ISaveTypeValidator
    {
        /// <summary>Returns a warning the first time a problematic type is seen, else null.</summary>
        string GetWarning(Type type);
    }
}
