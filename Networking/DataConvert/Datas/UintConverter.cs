using System;

namespace Networking.DataConvert.Datas;

public class UintConverter : IStaticDataConverter
{
    public bool IsValidConvertor(Type type) => typeof(uint) == type;
    public byte[] Serialize(object o) => BitConverter.GetBytes((uint)o);
    public object? Deserialize(byte[] data, Type type) => BitConverter.ToUInt32(data);
    public ushort Length => 4;
}