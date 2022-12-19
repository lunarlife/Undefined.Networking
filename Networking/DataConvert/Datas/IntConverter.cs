using System;

namespace Networking.DataConvert.Datas
{
    public sealed class IntConverter : IStaticDataConverter
    {
        public bool IsValidConvertor(Type type) => typeof(int).IsAssignableFrom(type);
        public ushort Length => 4;
        public byte[] Serialize(object o) => BitConverter.GetBytes((int)o);
        public object Deserialize(byte[] data, Type currentType) => BitConverter.ToInt32(data);
    }
}