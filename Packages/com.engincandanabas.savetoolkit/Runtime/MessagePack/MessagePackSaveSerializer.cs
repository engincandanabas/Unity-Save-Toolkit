
using System;
using System.Buffers;
using System.Collections.Generic;
using MessagePack;
using MessagePack.Resolvers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine.Scripting;

namespace SaveToolkit
{
    /// <summary>
    /// MessagePack serializer. Writes directly into the pooled output buffer
    /// and reads from memory without copies. Created by
    /// <see cref="SaveSerializerRegistry"/> when this assembly is present.
    /// </summary>
    [Preserve]
    public sealed class MessagePackSaveSerializer : ISaveSerializer, ISaveTypeValidator
    {
        // IL2CPP (iOS / most Android builds) cannot emit dynamic code, so
        // only [MessagePackObject] types with source-generated formatters
        // work there. Mono / Editor fall back to the contractless resolver.
        private static readonly MessagePackSerializerOptions Options =
            MessagePackSerializerOptions.Standard.WithResolver(
#if ENABLE_IL2CPP
                StandardResolver.Instance
#else
                ContractlessStandardResolver.Instance
#endif
            );

        private static readonly HashSet<Type> CheckedTypes = new HashSet<Type>();

        [Preserve]
        public MessagePackSaveSerializer() { }

        public SaveSerializationFormat Format => SaveSerializationFormat.MessagePack;

        public void Serialize(object data, Type type, IBufferWriter<byte> output)
        {
            MessagePackSerializer.Serialize(type, output, data, Options);
        }

        public object Deserialize(ReadOnlyMemory<byte> bytes, Type type)
        {
            return MessagePackSerializer.Deserialize(type, bytes, Options);
        }

        public JToken ToJToken(ReadOnlyMemory<byte> bytes)
        {
            return JToken.Parse(MessagePackSerializer.ConvertToJson(bytes, Options));
        }

        public void FromJToken(JToken token, IBufferWriter<byte> output)
        {
            byte[] bytes = MessagePackSerializer.ConvertFromJson(token.ToString(Formatting.None), Options);
            bytes.AsSpan().CopyTo(output.GetSpan(bytes.Length));
            output.Advance(bytes.Length);
        }

        public string GetWarning(Type type)
        {
            lock (CheckedTypes)
            {
                if (!CheckedTypes.Add(type))
                    return null;
            }

            if (type.IsPrimitive ||
                type == typeof(string) ||
                (type.Namespace != null && type.Namespace.StartsWith("System", StringComparison.Ordinal)) ||
                Attribute.IsDefined(type, typeof(MessagePackObjectAttribute)))
                return null;

            return $"'{type.Name}' has no [MessagePackObject] attribute. " +
                   "It works in the Editor/Mono via the contractless resolver " +
                   "but will fail on IL2CPP (iOS/Android).";
        }
    }
}
