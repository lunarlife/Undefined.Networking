using System;

namespace Networking.Packets;

public sealed class PacketId
{
    public Type Type { get; }
    public int Id { get; }

    public PacketId(Type type, int id)
    {
        Type = type;
        Id = id;
    }
}