using Undefined.Events;
using Undefined.Networking.Packets;

namespace Undefined.Networking.Events;

public class RequestEventArgs : IEventArgs
{
    public IRequest RequestPacket { get; }
    public Response Response { get; }
    public int RequestId { get; }

    public RequestEventArgs(IRequest requestPacket, ResponsePacketType type, int requestId)
    {
        Response = new Response(type);
        RequestPacket = requestPacket;
        RequestId = requestId;
    }
}