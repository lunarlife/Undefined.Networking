using System;
using System.Text;

namespace Networking.DataConvert.Datas
{
    public sealed class StringConverter : IDynamicDataConverter
    {
        public bool IsValidConvertor(Type type) => typeof(string).IsAssignableFrom(type);

        public byte[] Serialize(object o) => Encoding.UTF8.GetBytes((string)o);

        public object Deserialize(byte[] data, Type currentType) => Encoding.UTF8.GetString(data);
    }
}