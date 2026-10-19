
using System;
using System.Buffers;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace SaveToolkit
{
    /// <summary>
    /// Newtonsoft based serializer. Writes UTF-8 straight into the output
    /// buffer and reads from bytes without building an intermediate string;
    /// Newtonsoft's char buffers come from the shared array pool.
    /// </summary>
    public sealed class JsonSaveSerializer : ISaveSerializer
    {
        private readonly JsonSerializer serializer;
        private readonly Formatting formatting;

        public JsonSaveSerializer(bool indented)
        {
            formatting = indented ? Formatting.Indented : Formatting.None;

            serializer = JsonSerializer.Create(new JsonSerializerSettings
            {
                Formatting = formatting,
                ContractResolver = SaveContractResolver.Instance,
                // Lists initialized in constructors are replaced, not appended to.
                ObjectCreationHandling = ObjectCreationHandling.Replace
            });
        }

        public SaveSerializationFormat Format => SaveSerializationFormat.Json;

        public void Serialize(object data, Type type, IBufferWriter<byte> output)
        {
            Utf8BufferTextWriter text = Utf8BufferTextWriter.Get(output);

            try
            {
                var json = new JsonTextWriter(text)
                {
                    ArrayPool = JsonCharArrayPool.Instance,
                    Formatting = formatting,
                    CloseOutput = false
                };

                serializer.Serialize(json, data, type);
                json.Close(); // flushes and returns the pooled char buffer
            }
            finally
            {
                text.Release();
            }
        }

        public object Deserialize(ReadOnlyMemory<byte> bytes, Type type)
        {
            Utf8MemoryTextReader text = Utf8MemoryTextReader.Get(bytes);

            try
            {
                var json = new JsonTextReader(text)
                {
                    ArrayPool = JsonCharArrayPool.Instance,
                    CloseInput = false
                };

                object result = serializer.Deserialize(json, type);
                json.Close();
                return result;
            }
            finally
            {
                text.Release();
            }
        }

        public JToken ToJToken(ReadOnlyMemory<byte> bytes)
        {
            Utf8MemoryTextReader text = Utf8MemoryTextReader.Get(bytes);

            try
            {
                var json = new JsonTextReader(text)
                {
                    ArrayPool = JsonCharArrayPool.Instance,
                    CloseInput = false
                };

                JToken token = JToken.ReadFrom(json);
                json.Close();
                return token;
            }
            finally
            {
                text.Release();
            }
        }

        public void FromJToken(JToken token, IBufferWriter<byte> output)
        {
            Utf8BufferTextWriter text = Utf8BufferTextWriter.Get(output);

            try
            {
                var json = new JsonTextWriter(text)
                {
                    ArrayPool = JsonCharArrayPool.Instance,
                    Formatting = formatting,
                    CloseOutput = false
                };

                token.WriteTo(json);
                json.Close();
            }
            finally
            {
                text.Release();
            }
        }

        /// <summary>
        /// Lets Newtonsoft populate properties with private setters
        /// (e.g. <c>public int Coins { get; private set; }</c>).
        /// Shared so its contract cache is built once per type.
        /// </summary>
        private sealed class SaveContractResolver : DefaultContractResolver
        {
            public static readonly SaveContractResolver Instance = new SaveContractResolver();

            protected override JsonProperty CreateProperty(
                MemberInfo member,
                MemberSerialization memberSerialization)
            {
                JsonProperty property =
                    base.CreateProperty(member, memberSerialization);

                if (!property.Writable &&
                    member is PropertyInfo info &&
                    info.GetSetMethod(true) != null)
                {
                    property.Writable = true;
                }

                return property;
            }
        }
    }
}
