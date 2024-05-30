using System;

namespace Undefined.Networking.Packets;

public interface IPacketData
{
    public IPacket Packet { get; }
    public PacketType Type { get; }
    public bool Compressed { get; }
}

public class PacketData : IPacketData
{
    public IPacket Packet { get; }
    public PacketType Type { get; }
    public bool Compressed { get; }

    internal PacketData(IPacket packet, PacketType type, bool compressed)
    {
        Packet = packet;
        Type = type;
        Compressed = compressed;
    }
}

public interface IIdentifiablePacketData
{
    public ushort Id { get; }
}

public class ResponseData : PacketData, IIdentifiablePacketData
{
    public ushort Id { get; }

    internal ResponseData(ushort id, IPacket packet, PacketType type, bool compressed) : base(packet, type, compressed)
    {
        Id = id;
    }
}

public class RequestData : PacketData, IIdentifiablePacketData
{
    public Delegate? Callback { get; }
    public ushort Id { get; }


    internal RequestData(ushort id, IPacket packet, PacketType type, bool compressed, Delegate? callback) : base(packet,
        type, compressed)
    {
        Id = id;
        Callback = callback;
    }
}