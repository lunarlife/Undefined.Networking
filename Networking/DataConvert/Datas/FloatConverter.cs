using System;

namespace Networking.DataConvert.Datas
{
    public sealed class FloatConverter : IStaticDataConverter
    {
        public bool IsValidConvertor(Type type) => typeof(float).IsAssignableFrom(type);
        public ushort Length => 4;
        public byte[] Serialize(object o) => BitConverter.GetBytes((float)o);
        public object Deserialize(byte[] data, Type currentType) => BitConverter.ToSingle(data);
    }
}