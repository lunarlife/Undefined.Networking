using System;

namespace Undefined.Networking.Packets;

[Flags]
public enum PacketInfoFlags : byte
{
    IsUShortLength = 1 << 0,
    IsIntLength = 1 << 1,
    IsUShortPacketId = 1 << 2,
    IsUShortRequestId = 1 << 3,
    IsResponse = 1 << 5
}