using System;
using System.Collections;
using System.Collections.Generic;
using Networking.DataConvert.Exceptions;

namespace Networking.DataConvert.Datas
{
    public sealed class ArrayConverter : IDynamicDataConverter
    {
        public bool IsValidConvertor(Type type) => typeof(Array).IsAssignableFrom(type);


        public byte[] Serialize(object o)
        {
            if (o is byte[] b) return b;
            var array = (Array)o;
            var total = new List<byte[]>();
            var totalLength = 0;
            for (var i = 0; i < array.Length; i++)
            {
                if (DataConverter.Serialize(array.GetValue(i)!) is not { } bytes) continue;
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

        public object Deserialize(byte[] data, Type type)
        {
            var arrType = type.GetElementType();
            if (arrType == null) throw new DeserializeException($"{type.Name} is not array");
            if (arrType == typeof(byte))
                return data;

            var objects = new ArrayList();
            ushort deserialized = 0;
            while (deserialized < data.Length)
            {
                if(DataConverter.Deserialize(data, arrType, ref deserialized) is not { } deserialize) continue;
                objects.Add(deserialize);
            }
            var array = Array.CreateInstance(arrType, objects.Count);
            objects.CopyTo(array);
            return array;
        }
    }
}