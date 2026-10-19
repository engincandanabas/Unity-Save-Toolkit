
using System;
using System.Collections.Generic;

namespace SaveToolkit
{
    /// <summary>
    /// Creates serializers by format. JSON is built in; MessagePack comes from
    /// the optional SaveToolkit.MessagePack assembly, found by name so the core
    /// never references MessagePack. Custom serializers can be registered.
    /// </summary>
    public static class SaveSerializerRegistry
    {
        public const string MessagePackDefine = "SAVETOOLKIT_MESSAGEPACK";

        private const string MessagePackTypeName =
            "SaveToolkit.MessagePackSaveSerializer, SaveToolkit.MessagePack";

        private static readonly Dictionary<SaveSerializationFormat, Func<SaveOptions, ISaveSerializer>> Factories =
            new Dictionary<SaveSerializationFormat, Func<SaveOptions, ISaveSerializer>>();

        /// <summary>Overrides how a format's serializer is created.</summary>
        public static void Register(
            SaveSerializationFormat format,
            Func<SaveOptions, ISaveSerializer> factory)
        {
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            lock (Factories)
                Factories[format] = factory;
        }

        public static bool IsAvailable(SaveSerializationFormat format)
        {
            lock (Factories)
            {
                if (Factories.ContainsKey(format))
                    return true;
            }

            return format == SaveSerializationFormat.Json ||
                   (format == SaveSerializationFormat.MessagePack && FindMessagePackType() != null);
        }

        public static bool TryCreate(
            SaveSerializationFormat format,
            SaveOptions options,
            out ISaveSerializer serializer,
            out string error)
        {
            Func<SaveOptions, ISaveSerializer> factory;

            lock (Factories)
                Factories.TryGetValue(format, out factory);

            if (factory != null)
            {
                serializer = factory(options);
                error = null;
                return serializer != null;
            }

            switch (format)
            {
                case SaveSerializationFormat.Json:
                    serializer = new JsonSaveSerializer(options.IndentedJson);
                    error = null;
                    return true;

                case SaveSerializationFormat.MessagePack:
                    Type type = FindMessagePackType();

                    if (type != null)
                    {
                        serializer = (ISaveSerializer)Activator.CreateInstance(type);
                        error = null;
                        return true;
                    }

                    serializer = null;
                    error = "The MessagePack module is not available. Install MessagePack " +
                            "(e.g. MessagePack via NuGetForUnity); Save Toolkit then adds the " +
                            $"{MessagePackDefine} define and compiles SaveToolkit.MessagePack.";
                    return false;

                default:
                    serializer = null;
                    error = $"No serializer registered for format {format}.";
                    return false;
            }
        }

        private static Type FindMessagePackType()
        {
            return Type.GetType(MessagePackTypeName, false);
        }
    }
}
