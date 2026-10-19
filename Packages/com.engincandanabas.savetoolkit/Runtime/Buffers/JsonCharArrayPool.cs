
using System.Buffers;
using Newtonsoft.Json;

namespace SaveToolkit
{
    /// <summary>Lets Newtonsoft rent its char buffers from the shared pool.</summary>
    internal sealed class JsonCharArrayPool : IArrayPool<char>
    {
        public static readonly JsonCharArrayPool Instance = new JsonCharArrayPool();

        public char[] Rent(int minimumLength)
        {
            return ArrayPool<char>.Shared.Rent(minimumLength);
        }

        public void Return(char[] array)
        {
            ArrayPool<char>.Shared.Return(array);
        }
    }
}
