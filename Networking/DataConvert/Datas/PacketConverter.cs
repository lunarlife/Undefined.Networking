using System;
using Networking.Packets;

namespace Networking.DataConvert.Datas;

public class PacketConverter : IDynamicDataConverter
{
    public bool IsValidConvertor(Type type) => typeof(Packet).IsAssignableFrom(type);

    public byte[] Serialize(object o) =>
        DataConverter.Combine(DataConverter.Serialize(Packet.GetPacketId(o.GetType())),
            DataConverter.Serialize(o, converterUsing: ConverterUsing.ExcludeCurrent));

    public object? Deserialize(byte[] data, Type type)
    {
        ushort index = 0;
        var id = DataConverter.Deserialize<ushort>(data, ref index);
        return DataConverter.Deserialize(data, Packet.GetPacketType(id), ref index, converterUsing: ConverterUsing.ExcludeCurrent);
    }
}