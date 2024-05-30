using Undefined.Networking.Exceptions;

namespace Undefined.Networking.Packets;

public sealed class Response
{
    private readonly ResponsePacketType _type;
    private IPacket? _response;

    public bool Compressed { get; set; }

    public IPacket? ResponsePacket
    {
        get => _response;
        set
        {
            if (_type.Type != value?.GetType())
                throw new ResponseException("Not valid response packet type.");
            _response = value;
        }
    }

    public Response(ResponsePacketType type)
    {
        _type = type;
    }
}