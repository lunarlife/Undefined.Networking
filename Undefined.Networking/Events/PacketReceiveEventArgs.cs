using Undefined.Events;

namespace Undefined.Networking.Events;

public class PacketReceiveEventArgs : IEventArgs
{
    public object Packet { get; }
    public Packer Packer { get; }

    public PacketReceiveEventArgs(object packet, Packer packer)
    {
        Packet = packet;
        Packer = packer;
    }
}