using Undefined.Events;
using Undefined.Networking.Packets;

namespace Undefined.Networking.Events;

public class ResponseEventArgs : IEventArgs
{
    public IPacket Request { get; }
    public IPacket Response { get; }
    public uint RequestId { get; }

    public ResponseEventArgs(IPacket request, IPacket response, uint requestId)
    {
        Request = request;
        Response = response;
        RequestId = requestId;
    }
}