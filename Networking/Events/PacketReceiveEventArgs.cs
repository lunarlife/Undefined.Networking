using Undefined.Events;

namespace Networking.Events;

public class PacketReceiveEventArgs : IEventArgs
{
    public object Packet { get; }

    public PacketReceiveEventArgs(object packet)
    {
        Packet = packet;
    }
}