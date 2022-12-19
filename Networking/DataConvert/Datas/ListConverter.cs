using System;
using System.Collections;
using System.Collections.Generic;
using Networking.DataConvert.Exceptions;

namespace Networking.DataConvert.Datas
{
    public sealed class ListConverter : IDynamicDataConverter
    {
        public bool IsValidConvertor(Type type) => typeof(List<>).IsAssignableFrom(type);

        public byte[] Serialize(object o)
        {
            if (o is List<byte> b) return b.ToArray();
            var list = (IList)o;
            var total = new List<byte[]>();
            var totalLength = 0;
            foreach (var obj in list)
            {
                if (DataConverter.Serialize(obj) is not { } bytes) continue;
                total.Add(bytes);
                totalLength += bytes.Length;
            }
            if (total.Count == 0) return Array.Empty<byte>();
            var ret = new byte[totalLength];
            var index = 0;
            foreach (var bytes in total)
            {
                bytes.CopyTo(ret, index);
                index += bytes.Length;
            }
            return ret;
        }

        public object? Deserialize(byte[] data, Type type)
        {
            var arrType = type.GetGenericArguments()[0];
            if (arrType == null) throw new DeserializeException($"{type.Name} is not array");
            if (arrType == typeof(byte))
                return data;
            var objects = new List<object>();
            ushort deserialized = 0;
            while (deserialized < data.Length)
            {
                if(DataConverter.Deserialize(data, arrType, ref deserialized) is not { } deserialize) continue;
                objects.Add(deserialize);
            }
            return objects;
        }
    }
}