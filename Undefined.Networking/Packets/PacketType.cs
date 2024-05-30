using System;

namespace Undefined.Networking.Packets;

public enum PacketPurpose
{
    Default,
    Request,
    Response
}

public sealed class RequestPacketType : PacketType
{
    public Type ResponseType { get; }

    public override PacketPurpose Purpose => PacketPurpose.Request;

    public RequestPacketType(Type type, Type responseType, ushort id, ICustomSerializer? serializer) : base(type, id,
        serializer)
    {
        ResponseType = responseType;
    }
}

public sealed class ResponsePacketType : PacketType
{
    public Type RequestType { get; }

    public override PacketPurpose Purpose => PacketPurpose.Response;

    internal ResponsePacketType(Type type, Type requestType, ushort id, ICustomSerializer? serializer) : base(type, id,
        serializer)
    {
        Flags |= PacketInfoFlags.IsResponse;
        RequestType = requestType;
    }
}

public sealed class DefaultPacketType : PacketType
{
    public override PacketPurpose Purpose => PacketPurpose.Default;

    internal DefaultPacketType(Type type, ushort id, ICustomSerializer? serializer) : base(type, id, serializer)
    {
    }
}

public abstract class PacketType
{
    public PacketInfoFlags Flags { get; protected set; }
    public bool IsUShortPacketId => (Flags & PacketInfoFlags.IsUShortLength) != 0;
    public Type Type { get; }
    public abstract PacketPurpose Purpose { get; }
    public ushort Id { get; }
    public ICustomSerializer? Serializer { get; }

    internal PacketType(Type type, ushort id, ICustomSerializer? serializer)
    {
        Type = type;
        Id = id;
        Serializer = serializer;
        if (id > byte.MaxValue) Flags |= PacketInfoFlags.IsUShortPacketId;
    }
}