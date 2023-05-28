using Utils.Events;

namespace Networking.Packets;

public class PacketReceiveEventData : EventData
{
    public object Packet { get; }

    public PacketReceiveEventData(object packet)
    {
        Packet = packet;
    }
}