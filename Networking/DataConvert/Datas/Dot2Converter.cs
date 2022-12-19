using System;
using Utils.Dots;

namespace Networking.DataConvert.Datas
{
    public sealed class Dot2Converter : IStaticDataConverter
    {
        public bool IsValidConvertor(Type type) => typeof(Dot2).IsAssignableFrom(type);
        public ushort Length => 8;

        public byte[] Serialize(object o)
        {
            var vec = (Dot2)o;
            var x = BitConverter.GetBytes(vec.X);
            var y = BitConverter.GetBytes(vec.Y);
            var ret = new byte[x.Length + y.Length];
            x.CopyTo(ret, 0);
            y.CopyTo(ret, 4);
            return ret;
        }

        public object Deserialize(byte[] data, Type currentType)
        {
            var x = BitConverter.ToSingle(data, 0);
            var y = BitConverter.ToSingle(data, 4);
            return new Dot2(x, y);
        }
    }
}