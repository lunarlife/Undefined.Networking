namespace Undefined.Networking.Packets;

public interface IRequest : IPacket
{
}

public interface IRequest<T> : IRequest where T : struct, IPacket
{
}