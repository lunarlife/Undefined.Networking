using Utils.Events;

namespace Networking.Packets;

public class PacketReceiveEventData : EventData
{
    public Packet Packet { get; }

    public PacketReceiveEventData(Packet packet)
    {
        Packet = packet;
    }
}