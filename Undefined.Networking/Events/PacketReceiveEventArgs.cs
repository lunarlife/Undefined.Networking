using Undefined.Events;
using Undefined.Networking.Packets;

namespace Undefined.Networking.Events;

public class PacketReceiveEventArgs : IEventArgs
{
    public IPacket Packet { get; }
    public Packer Packer { get; }

    public PacketReceiveEventArgs(IPacket packet, Packer packer)
    {
        Packet = packet;
        Packer = packer;
    }
}