using System;

namespace Networking.DataConvert.Datas
{
    public sealed class BoolConverter : IStaticDataConverter
    {
        public bool IsValidConvertor(Type type) => typeof(bool).IsAssignableFrom(type);
        public ushort Length => 1;
        public byte[] Serialize(object o) => BitConverter.GetBytes((bool)o);
        public object? Deserialize(byte[] data, Type type) => BitConverter.ToBoolean(data);
    }
}