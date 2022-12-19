using System;

namespace Networking.DataConvert.Datas;

public sealed class UShortConverter : IStaticDataConverter
{
    public bool IsValidConvertor(Type type) => typeof(ushort).IsAssignableFrom(type);
    public ushort Length => 2;

    public byte[] Serialize(object o) => BitConverter.GetBytes((ushort)o);

    public object Deserialize(byte[] data, Type type) => BitConverter.ToUInt16(data);
}