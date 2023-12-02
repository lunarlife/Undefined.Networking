using Utils.Events;

namespace Networking.Packets;

public class PacketReceiveEventArgs : IEventArgs
{
    public object Packet { get; }

    public PacketReceiveEventArgs(object packet)
    {
        Packet = packet;
    }
}