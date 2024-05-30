using System;
using Undefined.Serializer.Buffers;

namespace Undefined.Networking.Packets;

public interface ICustomSerializer
{
    public Type PacketType { get; }
    public IPacket Deserialize(BufferReader reader);
    public void Serialize(BufferWriter writer);
}

public interface ICustomSerializer<out T> : ICustomSerializer where T : IPacket
{
    Type ICustomSerializer.PacketType => typeof(T);
    IPacket ICustomSerializer.Deserialize(BufferReader reader) => Deserialize(reader);
    void ICustomSerializer.Serialize(BufferWriter writer) => Serialize(writer);

    public new T Deserialize(BufferReader reader);
    public new void Serialize(BufferWriter writer);
}