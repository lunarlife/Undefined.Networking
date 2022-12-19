using System;

namespace Networking.DataConvert.Datas
{
    public sealed class DoubleConverter : IStaticDataConverter
    {
        public bool IsValidConvertor(Type type) => typeof(double).IsAssignableFrom(type);
        public ushort Length => 8;
        public byte[] Serialize(object o) => BitConverter.GetBytes((double)o);
        public object Deserialize(byte[] data, Type type) => BitConverter.ToDouble(data);
    }
}