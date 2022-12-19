using System;
using System.Collections.Generic;
using System.Linq;

namespace Networking.DataConvert.Datas
{
    public sealed class FlagsEnumConverter : IDynamicDataConverter
    {
        public bool IsValidConvertor(Type type)
        {
            return type.GetCustomAttributes(false).FirstOrDefault(a => a is FlagsAttribute) != null &&
                   typeof(Enum).IsAssignableFrom(type);
        }

        public ushort Length => sizeof(ushort);
        public byte[] Serialize(object o)
        {
            var en = (Enum)o;
            var values = Enum.GetValues(o.GetType());
            var list = from Enum e in values where en.HasFlag(e) select DataConverter.Serialize((ushort)Array.IndexOf(values, e));
            return DataConverter.Combine(list);
        }

        public object? Deserialize(byte[] data, Type type)
        {
            var values = Enum.GetValues(type);
            if (data.Length == 0) return null;

            ushort ind = 0;
            var res = (int)values.GetValue(DataConverter.Deserialize<ushort>(data, ref ind));
            while (ind != data.Length)
            {
                var value = values.GetValue(DataConverter.Deserialize<ushort>(data, ref ind));
                res |= (int)value;
            }
            return Enum.ToObject(type, res);
        }
    }
}