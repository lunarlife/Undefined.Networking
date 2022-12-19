using System;

namespace Networking.DataConvert
{
    public interface IConverter
    {
        public bool IsValidConvertor(Type type);

        public byte[] Serialize(object o);
        public object? Deserialize(byte[] data, Type type);

        public object? Deserialize(byte[] buffer, ushort index, ushort length, Type type)
        {
            if (index == 0 && length == buffer.Length) return Deserialize(buffer, type);
            var newBuffer = new byte[length];
            Array.Copy(buffer, index, newBuffer, 0, length);
            return Deserialize(newBuffer, type);
        }
    }
}