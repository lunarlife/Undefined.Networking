using System;
using Utils.Enums;

namespace Networking.Packets;

public sealed class PacketId : EnumType
{
    public Type Type { get; }

    public PacketId(Type type)
    {
        Type = type;
    }
}